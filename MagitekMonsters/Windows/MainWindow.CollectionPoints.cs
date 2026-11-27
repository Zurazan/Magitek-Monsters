using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private const int NextGoalView = 0;
    private const int CompletedGoalView = 1;
    private const int AllGoalView = 2;
    private const int PinnedGoalView = 3;
    private const int GoalsPerPage = 5;
    private static readonly string[] GoalCategoryLabels =
        ["All categories", "Species milestones", "Type collections", "Evolution families", "Generation surveys"];

    private List<CollectionRewardDto> collectionRewards = [];
    private int objectiveCategory;
    private int objectiveState;
    private int objectivePage;
    private string? expandedObjective;
    private bool expandFirstObjective = true;
    private readonly ConcurrentDictionary<string, string> redemptionRequests = new();

    private void DrawCollectionGoals()
    {
        if (trainer is null) return;
        var progress = trainer.CollectionPoints;
        var pins = LocalWishlist().Pins;
        DrawCollectionGoalsHeader(progress);
        DrawGoalFilters(pins.Count(pin => pin.Kind == "objective"));
        DrawGoalText(objectiveState switch
        {
            NextGoalView => "Next milestone in each collection, closest to completion first.",
            CompletedGoalView => "These rewards have already been added to your balance.",
            PinnedGoalView => "Goals pinned to your collection wishlist.",
            _ => "Every milestone, grouped by category."
        }, CompanionMuted);

        var goals = GetCollectionObjectives();
        objectivePage = Math.Clamp(objectivePage, 0, Math.Max(0, (goals.Count - 1) / GoalsPerPage));
        var start = objectivePage * GoalsPerPage;
        var end = Math.Min(start + GoalsPerPage, goals.Count);
        if (expandFirstObjective && start < end)
        {
            expandedObjective = goals[start].Id;
            expandFirstObjective = false;
        }
        if (start == end)
            DrawGoalText(objectiveState == PinnedGoalView ? "No pinned goals in this category." : "No goals in this view.", CompanionMuted);

        for (var index = start; index < end; index++)
        {
            var goal = goals[index];
            var pinned = pins.Any(pin => pin.Kind == "objective" && pin.Id.Equals(goal.Id, StringComparison.OrdinalIgnoreCase));
            ImGui.PushID(goal.Id);
            DrawGoalRow(goal, pinned);
            if (expandedObjective == goal.Id) DrawGoalDetails(goal, pinned);
            ImGui.PopID();
        }
        DrawGoalFooter(start, end, goals.Count, pins.Count);
        if (ImGui.CollapsingHeader("How Collection Points work"))
        {
            DrawGoalText("Each goal pays out once, automatically. Registrations stay recorded after release. Admin Pokédex completion does not count toward goals.", CompanionMuted);
            DrawGoalText($"Lifetime earned: {progress.TotalEarned:N0} CP / Starters unlocked: {progress.UnlockedStarters.Count}/27", CompanionMuted);
        }
        DrawCollectionWishlist();
    }

    private void DrawCollectionGoalsHeader(CollectionPointProgressDto progress)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var balance = $"{progress.Balance:N0} CP";
        var actionWidth = GoalButtonWidth("Collection Exchange") + ImGui.CalcTextSize(balance).X + spacing;
        ImGui.BeginGroup();
        ImGui.TextUnformatted("Collection Goals");
        DrawGoalText($"{progress.RegisteredSpecies:N0} species registered / {progress.CompletedObjectives:N0}/{progress.Objectives.Count:N0} goals completed", CompanionMuted);
        ImGui.EndGroup();
        var headingWidth = ImGui.GetItemRectSize().X;
        if (headingWidth + spacing + actionWidth <= width)
            ImGui.SameLine(0, width - headingWidth - actionWidth);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(PokemonYellow, balance);
        ImGui.SameLine();
        if (DrawAccentButton("Collection Exchange", Vector2.Zero, CompanionAccent))
        {
            mainSection = 5;
            openCollectionExchange = true;
        }
        ImGui.Separator();
    }

    private void DrawGoalFilters(int pinnedCount)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var used = 0f;
        GoalViewButton("Next up", NextGoalView);
        GoalViewButton("All goals", AllGoalView);
        GoalViewButton("Completed", CompletedGoalView);
        GoalViewButton($"Pinned ({pinnedCount})###PinnedGoals", PinnedGoalView);

        var categoryWidth = Math.Min(width, 170 * CompanionScale);
        if (used + spacing + categoryWidth <= width) ImGui.SameLine(0, width - used - categoryWidth);
        ImGui.SetNextItemWidth(categoryWidth);
        if (ImGui.Combo("##ObjectiveCategory", ref objectiveCategory, GoalCategoryLabels, GoalCategoryLabels.Length)) ResetGoalPage();

        void GoalViewButton(string label, int view)
        {
            var buttonWidth = Math.Min(width, GoalButtonWidth(label));
            if (used > 0 && used + spacing + buttonWidth <= width)
            {
                ImGui.SameLine();
                used += spacing;
            }
            else used = 0;

            if (DrawAccentButton(label, new Vector2(buttonWidth, 0), objectiveState == view ? CompanionAccent : CompanionMuted)
                && objectiveState != view)
            {
                objectiveState = view;
                ResetGoalPage();
            }
            used += buttonWidth;
        }
    }

    private void ResetGoalPage()
    {
        objectivePage = 0;
        expandedObjective = null;
        expandFirstObjective = false;
    }

    private void DrawGoalRow(CollectionObjectiveDto goal, bool pinned)
    {
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var line = ImGui.GetTextLineHeight();
        var pad = 8 * scale;
        var art = 28 * scale;
        var nameX = art + 2 * pad;
        var kind = goal.Category switch
        {
            "species" => "Species milestone",
            "type" => "Type collection",
            "family" => "Evolution families",
            "generation" => "Generation survey",
            _ => "Collection goal"
        };
        if (pinned) kind += " / Pinned";
        var count = $"{Math.Min(goal.Progress, goal.Target):N0}/{goal.Target:N0}";
        var remaining = Math.Max(0, goal.Target - goal.Progress);
        var left = goal.Completed ? "" : $"{remaining:N0} left";
        var reward = $"+{goal.Points:N0} CP";
        var rewardRight = width - 3 * pad;
        var rewardWidth = Math.Max(ImGui.CalcTextSize(reward).X, goal.Completed ? ImGui.CalcTextSize("Awarded").X : 0);
        var nameEnd = rewardRight - rewardWidth - pad;
        var meterWidth = Math.Max(116 * scale, ImGui.CalcTextSize(count).X + ImGui.CalcTextSize(left).X + pad);
        var wide = nameEnd - nameX >= meterWidth + 190 * scale;
        var nameWidth = Math.Max(1, nameEnd - nameX - (wide ? meterWidth + pad : 0));
        var nameHeight = ImGui.CalcTextSize(goal.Name, false, nameWidth).Y;
        var textHeight = nameHeight + ImGui.CalcTextSize(kind, false, nameWidth).Y + 2 * scale;
        var meterHeight = line + 8 * scale;
        var height = 2 * pad + Math.Max(art, wide ? Math.Max(textHeight, meterHeight) : textHeight + meterHeight + 3 * scale);
        var expanded = expandedObjective == goal.Id;
        if (DrawPveCardSurface("GoalRow", height, expanded, out var min, out var max, out var after))
        {
            expanded = !expanded;
            expandedObjective = expanded ? goal.Id : null;
        }

        DrawAchievementIconAt("collection:" + goal.Id, min + new Vector2(pad, (height - art) / 2), new Vector2(art), goal.Completed);
        var draw = ImGui.GetWindowDrawList();
        var textColor = expanded ? CompanionAccent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(nameX, pad), ImGui.GetColorU32(textColor), goal.Name, nameWidth);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(nameX, pad + nameHeight + 2 * scale),
            ImGui.GetColorU32(pinned ? PokemonYellow : CompanionMuted), kind, nameWidth);

        var meter = min + new Vector2(wide ? nameEnd - meterWidth : nameX, wide ? (height - meterHeight) / 2 : pad + textHeight + 3 * scale);
        if (!wide) meterWidth = Math.Max(1, nameWidth);
        draw.AddText(meter, ImGui.GetColorU32(CompanionMuted), count);
        if (left.Length > 0 && ImGui.CalcTextSize(count).X + ImGui.CalcTextSize(left).X + pad <= meterWidth)
            DrawGoalRightText(meter + new Vector2(meterWidth, 0), left, CompanionMuted);
        ImGui.SetCursorScreenPos(meter + new Vector2(0, line + 3 * scale));
        DrawProgressBar(goal.Progress / (float)Math.Max(1, goal.Target), new Vector2(meterWidth, 4 * scale), "", goal.Completed ? PokemonGreen : ExpBarBlue);

        var rewardTop = min + new Vector2(rewardRight, (height - line * (goal.Completed ? 2 : 1)) / 2);
        DrawGoalRightText(rewardTop, reward, goal.Completed ? PokemonGreen : PokemonYellow);
        if (goal.Completed) DrawGoalRightText(rewardTop + new Vector2(0, line), "Awarded", CompanionMuted);
        var caret = new Vector2(max.X - 1.5f * pad, min.Y + height / 2);
        var caretColor = ImGui.GetColorU32(CompanionMuted);
        var tip = 3 * scale;
        if (expanded)
            draw.AddTriangleFilled(caret + new Vector2(-tip, -tip / 2), caret + new Vector2(tip, -tip / 2), caret + new Vector2(0, tip), caretColor);
        else
            draw.AddTriangleFilled(caret + new Vector2(-tip / 2, -tip), caret + new Vector2(-tip / 2, tip), caret + new Vector2(tip, 0), caretColor);
        ImGui.SetCursorScreenPos(after);
    }

    private void DrawGoalDetails(CollectionObjectiveDto goal, bool pinned)
    {
        var scale = CompanionScale;
        var pinLabel = pinned ? "Unpin goal" : "Pin goal";
        var pinWidth = GoalButtonWidth(pinLabel) + 4 * scale;
        var wide = ImGui.GetContentRegionAvail().X >= pinWidth + 300 * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 6) * scale);
        if (ImGui.BeginTable("GoalDetails", wide ? 2 : 1, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            if (wide) ImGui.TableSetupColumn("Pin", ImGuiTableColumnFlags.WidthFixed, pinWidth);
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            if (goal.Completed)
            {
                var awarded = DateTimeOffset.TryParse(goal.EarnedAt, out var earnedAt)
                    ? $"Reward added on {earnedAt.LocalDateTime:g}." : "Reward added to your Collection Points.";
                DrawGoalText(awarded, CompanionMuted);
            }
            else
            {
                DrawGoalText(goal.Category switch
                {
                    "family" => "Register every species in an available evolution family to complete it.",
                    "type" => "Register different Pokémon species of the listed type.",
                    "generation" => "Register different species introduced in the listed generation.",
                    _ => "Register different Pokémon species in your Pokédex."
                }, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                var later = objectiveTracks![CollectionGoalTrack(goal)].Where(entry => entry.Target > goal.Target).Take(3)
                    .Select(entry => $"{entry.Target:N0} (+{entry.Points:N0} CP)").ToArray();
                if (later.Length > 0) DrawGoalText("Next milestones / " + string.Join(" / ", later), CompanionMuted);
            }
            if (wide) ImGui.TableNextColumn();
            DrawWishlistPin("objective", goal.Id, pinLabel);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawGoalFooter(int start, int end, int total, int pins)
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.BeginGroup();
        ImGui.BeginDisabled(start == 0);
        if (DrawAccentSmallButton("Prev##GoalPage", CompanionAccent)) { objectivePage--; expandedObjective = null; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled(total == 0 ? "0 goals" : $"{start + 1:N0}-{end:N0} of {total:N0}");
        ImGui.SameLine();
        ImGui.BeginDisabled(end >= total);
        if (DrawAccentSmallButton("Next##GoalPage", CompanionAccent)) { objectivePage++; expandedObjective = null; }
        ImGui.EndDisabled();
        ImGui.EndGroup();
        var pagerWidth = ImGui.GetItemRectSize().X;
        var wishlist = $"Collection wishlist ({pins})";
        var wishlistWidth = GoalButtonWidth(wishlist);
        if (pagerWidth + ImGui.GetStyle().ItemSpacing.X + wishlistWidth <= width)
            ImGui.SameLine(0, width - pagerWidth - wishlistWidth);
        if (DrawAccentSmallButton(wishlist, PokemonYellow)) requestCollectionWishlist = true;
    }

    private static float GoalButtonWidth(string label) => ImGui.CalcTextSize(label, true).X + 2 * ImGui.GetStyle().FramePadding.X;

    private static void DrawGoalText(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static void DrawGoalRightText(Vector2 right, string text, Vector4 color)
    {
        ImGui.GetWindowDrawList().AddText(right - new Vector2(ImGui.CalcTextSize(text).X, 0), ImGui.GetColorU32(color), text);
    }

    private bool openCollectionExchange;

    private async Task RedeemCollectionReward(string rewardId, string mode)
    {
        var owner = trainer!.User.Id;
        var key = $"{owner}:{rewardId}:{mode}";
        var requestId = redemptionRequests.GetOrAdd(key, _ => Guid.NewGuid().ToString());
        var result = await InSession(plugin.Api.RedeemCollectionReward(requestId, rewardId, mode, disposeCancellation.Token));
        redemptionRequests.TryRemove(key, out _);
        if (trainer?.User.Id != owner) return;
        RequestPokedexIfChanged(trainer.Pokedex, result.Pokedex);
        trainer = result;
        SetStatus("Collection reward received. Points and unlocks saved.");
    }
}
