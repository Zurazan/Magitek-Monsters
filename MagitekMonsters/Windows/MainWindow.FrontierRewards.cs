using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum FrontierRewardGroup { Exchange, Beasts, Research }

    private static readonly string[] ParadoxEras = ["Both eras", "Ancient", "Future"];
    private FrontierRewardGroup frontierRewardGroup;
    private int frontierRewardPage;
    private string? expandedFrontierRewardId;
    private string frontierRewardSearch = string.Empty;
    private string paradoxSearch = string.Empty;
    private int paradoxEra;

    private int ParadoxWaitSeconds => DateTimeOffset.TryParse(trainer?.Frontier.NextParadoxAt, out var available)
        ? Math.Max(0, (int)Math.Ceiling((available - DateTimeOffset.UtcNow).TotalSeconds)) : 0;

    private void ResetFrontierRewardList()
    {
        frontierRewardPage = 0;
        expandedFrontierRewardId = null;
    }

    private void DrawFrontierRewards(TrainerSnapshotDto current)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        DrawFrontierRewardGroup("BP Exchange", FrontierRewardGroup.Exchange);
        SameLineIfFits("Ultra Beasts", right);
        DrawFrontierRewardGroup("Ultra Beasts", FrontierRewardGroup.Beasts);
        SameLineIfFits("Paradox Research", right);
        DrawFrontierRewardGroup("Paradox Research", FrontierRewardGroup.Research);
        ImGui.Separator();

        var research = frontierRewardGroup == FrontierRewardGroup.Research;
        if (research) DrawFrontierResearchStatus(current);
        ref var search = ref (research ? ref paradoxSearch : ref frontierRewardSearch);
        if (frontierRewardGroup != FrontierRewardGroup.Beasts)
        {
            var searchHint = research ? "Search Paradox Pokémon..." : "Search BP rewards...";
            ImGui.SetNextItemWidth(Math.Min(230 * CompanionScale, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputTextWithHint("##FrontierRewardSearch", searchHint, ref search, 80)) ResetFrontierRewardList();
            if (research)
            {
                var eraWidth = 125 * CompanionScale;
                if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + eraWidth <= right) ImGui.SameLine();
                ImGui.SetNextItemWidth(Math.Min(eraWidth, ImGui.GetContentRegionAvail().X));
                if (ImGui.Combo("##FrontierEra", ref paradoxEra, ParadoxEras, ParadoxEras.Length)) ResetFrontierRewardList();
            }
        }

        var kind = frontierRewardGroup switch { FrontierRewardGroup.Beasts => "pokemon", FrontierRewardGroup.Research => "permit", _ => "item" };
        var query = frontierRewardGroup == FrontierRewardGroup.Beasts ? string.Empty : search;
        var rewards = current.Frontier.Rewards.Where(entry => entry.Kind == kind && entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            && (!research || paradoxEra == 0 || entry.Era == ParadoxEras[paradoxEra])).ToArray();
        const int pageSize = 6;
        if (rewards.Length > pageSize)
        {
            var oldPage = frontierRewardPage;
            DrawPager("FrontierRewards", ref frontierRewardPage, rewards.Length, pageSize);
            if (frontierRewardPage != oldPage) expandedFrontierRewardId = null;
        }
        else frontierRewardPage = 0;
        if (rewards.Length == 0) DrawGoalText("No matching rewards.", CompanionMuted);
        foreach (var reward in rewards.Skip(frontierRewardPage * pageSize).Take(pageSize)) DrawFrontierReward(current.Frontier, reward);
    }

    private void DrawFrontierRewardGroup(string label, FrontierRewardGroup group)
    {
        if (!DrawAccentButton(label, Vector2.Zero, frontierRewardGroup == group ? CompanionAccent : CompanionMuted)
            || frontierRewardGroup == group) return;
        frontierRewardGroup = group;
        ResetFrontierRewardList();
    }

    private void DrawFrontierResearchStatus(TrainerSnapshotDto current)
    {
        var wait = ParadoxWaitSeconds;
        DrawGoalText(wait == 0 ? "Research ready" : $"Research cooldown: {wait / 60}:{wait % 60:00}", wait == 0 ? PokemonGreen : PokemonYellow);
        if (current.ActiveEncounter is not { Status: "active" } encounter) return;
        DrawGoalText($"Saved wild encounter: {encounter.Pokemon.Species}", PokemonOrange);
        if (DrawAccentButton("Resume wild encounter", Vector2.Zero, CompanionAccent))
            Run(async () => { await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0; }, "Restoring your wild battle...");
    }

    private void DrawFrontierReward(FrontierProgressDto progress, FrontierRewardDto reward)
    {
        var item = reward.ItemId is not null ? itemCatalog.FirstOrDefault(entry => entry.Id == reward.ItemId) : null;
        var permit = reward.Kind == "permit";
        var ownedPermit = permit && reward.Purchased > 0;
        var atLimit = item?.MaxOwned is int limit && InventoryCount(item.Id) >= limit;
        var shortfall = Math.Max(0, reward.CurrentPrice - progress.Balance);
        var info = ownedPermit ? "Permanent permit owned" : !reward.Unlocked ? "Locked" : atLimit ? "Bag limit reached"
            : shortfall > 0 ? $"Need {shortfall:N0} more BP" : reward.Kind == "pokemon" ? "Purchase unlocked" : "Available";
        var accent = ownedPermit ? PokemonGreen : !reward.Unlocked || atLimit ? CompanionMuted : shortfall > 0 ? PokemonYellow : CompanionAccent;
        ImGui.PushID(reward.Id);
        var expanded = expandedFrontierRewardId == reward.Id;
        if (DrawFrontierRewardRow(reward, info, ownedPermit ? "No repeat fee" : $"{reward.CurrentPrice:N0} BP", accent, expanded))
            expandedFrontierRewardId = expanded ? null : reward.Id;
        if (expandedFrontierRewardId == reward.Id) DrawFrontierRewardDetails(progress, reward, item, ownedPermit, atLimit, shortfall);
        ImGui.PopID();
    }

    private bool DrawFrontierRewardRow(FrontierRewardDto reward, string status, string price, Vector4 accent, bool expanded)
    {
        var scale = CompanionScale;
        var pad = 8 * scale;
        var art = 36 * scale;
        var textX = art + 2 * pad;
        var width = ImGui.GetContentRegionAvail().X;
        var line = ImGui.GetTextLineHeight();
        var priceWidth = ImGui.CalcTextSize(price).X;
        var inline = width - textX - priceWidth >= 135 * scale;
        var textWidth = Math.Max(1, width - textX - pad * 3 - (inline ? priceWidth : 0));
        var titleHeight = ImGui.CalcTextSize(reward.Name, false, textWidth).Y;
        var statusHeight = ImGui.CalcTextSize(status, false, textWidth).Y;
        var height = 2 * pad + Math.Max(art, titleHeight + statusHeight + (inline ? 0 : line + 3 * scale));
        var clicked = DrawPveCardSurface("FrontierReward", height, expanded, out var min, out var max, out var after);
        var draw = ImGui.GetWindowDrawList();
        var artPosition = min + new Vector2(pad, (height - art) / 2);
        if (reward.Species is not null) DrawPokemonIconAt(reward.Species, false, artPosition, new Vector2(art));
        else
        {
            ImGui.SetCursorScreenPos(artPosition);
            DrawItemImage(reward.ItemId ?? string.Empty, new Vector2(art));
            ImGui.SetCursorScreenPos(after);
        }
        var origin = min + new Vector2(textX, pad);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), origin, ImGui.GetColorU32(expanded ? CompanionAccent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), reward.Name, textWidth);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), origin + new Vector2(0, titleHeight), ImGui.GetColorU32(accent), status, textWidth);
        if (inline) DrawGoalRightText(new Vector2(max.X - 3 * pad, min.Y + (height - line) / 2), price, PokemonYellow);
        else draw.AddText(origin + new Vector2(0, titleHeight + statusHeight + 2 * scale), ImGui.GetColorU32(PokemonYellow), price);
        var arrow = new Vector2(max.X - pad * 1.4f, min.Y + (height - line) / 2);
        draw.AddText(arrow - new Vector2(3 * scale, 0), ImGui.GetColorU32(CompanionMuted), expanded ? "▾" : "›");
        return clicked;
    }

    private void DrawFrontierRewardDetails(FrontierProgressDto progress, FrontierRewardDto reward, ShopItemDto? item, bool ownedPermit, bool atLimit, int shortfall)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(9, 7) * CompanionScale);
        if (ImGui.BeginTable("FrontierRewardDetails", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            if (reward.Kind == "permit")
            {
                DrawGoalText($"{reward.Era} / {reward.Progress:N0}/{reward.LifetimeWins:N0} lifetime wins", CompanionAccent);
                DrawGoalText("Buy once to open repeat level-50 hunts with your normal party. Hunts share a five-minute cooldown.", CompanionMuted);
            }
            else if (reward.Kind == "pokemon")
            {
                var achievement = progress.Achievements.FirstOrDefault(entry => entry.Id == reward.AchievementId);
                DrawGoalText($"Lifetime wins: {reward.Progress:N0}/{reward.LifetimeWins:N0}", CompanionAccent);
                if (achievement is not null)
                    DrawGoalText($"Or {achievement.Name}: {achievement.Progress:N0}/{achievement.Target:N0}", CompanionAccent);
                DrawGoalText("Either route unlocks purchase permanently.", CompanionMuted);
                if (reward.Species == "Poipole") DrawGoalText("Poipole evolves when it knows Dragon Pulse.", CompanionMuted);
            }
            if (reward.Kind != "permit") DrawGoalText(reward.Description, CompanionMuted);
            if (item is not null && !string.IsNullOrWhiteSpace(item.AcquisitionNote)) DrawGoalText(item.AcquisitionNote, CompanionMuted);
            if (item is { BpOnly: false, Price: > 0 }) DrawGoalText($"Also available for ¥ {item.Price:N0} in Poké Mart.", CompanionMuted);

            if (ownedPermit)
            {
                var wait = ParadoxWaitSeconds;
                ImGui.BeginDisabled(CollectionLocked || wait > 0);
                if (DrawAccentButton("Hunt", Vector2.Zero, CompanionAccent)) Run(() => StartParadoxHunt(reward.Id), $"Locating {reward.Species}...");
                ImGui.EndDisabled();
                if (CollectionEditRestriction is not null) DrawGoalText("Finish your current activity before starting a hunt.", PokemonOrange);
            }
            else
            {
                var label = reward.Kind == "permit" ? "Buy permit" : reward.Kind == "pokemon" && reward.Purchased > 0 ? "Buy copy" : "Buy";
                ImGui.BeginDisabled(busy || !reward.Unlocked || atLimit || shortfall > 0 || reward.Kind == "pokemon" && CollectionLocked);
                if (DrawAccentButton($"{label} / {reward.CurrentPrice:N0} BP", Vector2.Zero, PokemonGreen)) Run(() => BuyFrontierReward(reward.Id), $"Redeeming {reward.Name}...");
                ImGui.EndDisabled();
                if (reward.Kind == "pokemon" && CollectionEditRestriction is not null)
                    DrawGoalText("Finish your current activity before receiving a Pokémon.", PokemonOrange);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }
}
