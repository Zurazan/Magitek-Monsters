using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly ShowcaseCatalog showcaseCatalog = new();
    private static readonly string[] ShowcaseAvailability = ["All", "Unlocked", "Locked"];
    private string showcaseSearch = "", showcaseBadgeId = "", showcaseRevealId = "";
    private int showcaseCategory, showcaseAvailability, showcasePage;
    private bool showcaseFocusSearch;

    private void ResetShowcase()
    {
        showcaseCatalog.Clear(); showcaseSearch = showcaseBadgeId = showcaseRevealId = "";
        showcaseCategory = showcaseAvailability = showcasePage = 0; showcaseFocusSearch = false;
    }
    private void ReadShowcase()
    {
        if (trainer is null) return;
        var entries = showcaseCatalog.Read(trainer, regionalLeagues, opponents);
        if (entries.All(badge => badge.Id != showcaseBadgeId))
            showcaseBadgeId = entries.FirstOrDefault(badge => !badge.Unlocked)?.Id ?? entries.FirstOrDefault()?.Id ?? "";
    }
    private void RevealShowcase(string id)
    {
        showcaseBadgeId = showcaseRevealId = id; showcaseCategory = showcaseAvailability = 0; showcaseSearch = "";
    }
    private bool ChangeShowcaseBadge(TrainerCardPreferences preferences, string id, int replacement = -1)
    {
        if (busy || trainer is null) return false;
        var card = TrainerCard.Resolve(trainer, preferences, regionalLeagues);
        if (!card.EarnedBadges.Any(badge => badge.Id == id)) return false;
        var selected = card.Badges.Select(badge => badge.Id).ToList();
        if (selected.Contains(id)) return false;
        if (replacement >= 0)
        {
            if (replacement >= selected.Count) return false;
            selected[replacement] = id;
        }
        else
        {
            if (selected.Count >= TrainerCard.MaximumBadges) return false;
            selected.Add(id);
        }
        preferences.BadgeIds = selected; SaveTrainerCard(); return true;
    }
    private void RemoveShowcaseBadge(TrainerCardPreferences preferences, string id)
    {
        if (busy || trainer is null) return;
        var selected = TrainerCard.Resolve(trainer, preferences, regionalLeagues).Badges.Select(badge => badge.Id).ToList();
        if (!selected.Remove(id)) return;
        preferences.BadgeIds = selected; SaveTrainerCard();
    }
    private void DrawShowcaseCatalog(TrainerCardPreferences preferences, TrainerCardView card)
    {
        ReadShowcase();
        var scale = CompanionScale;
        ImGui.Spacing();
        ImGui.TextColored(CompanionAccent, $"YOUR SHOWCASE / {card.Badges.Count}/{TrainerCard.MaximumBadges}");
        var autoLabel = preferences.BadgeIds is null ? "Automatic enabled" : "Use automatic";
        var buttonWidth = ImGui.CalcTextSize(autoLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        if (ImGui.GetItemRectSize().X + ImGui.GetStyle().ItemSpacing.X + buttonWidth < ImGui.GetContentRegionAvail().X) ImGui.SameLine();
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton(autoLabel, preferences.BadgeIds is null ? PokemonGreen : CompanionMuted))
        { preferences.BadgeIds = null; SaveTrainerCard(); }
        ImGui.EndDisabled();
        ImGui.TextWrapped("These badges appear on your shared trainer card. Click a slot to inspect it.");
        if (ImGui.BeginTable("ShowcaseSlots", 3, ImGuiTableFlags.SizingStretchSame))
        {
            for (var slot = 0; slot < TrainerCard.MaximumBadges; slot++)
            {
                ImGui.TableNextColumn();
                DrawShowcaseSlot(preferences, card.Badges.ElementAtOrDefault(slot), slot);
            }
            ImGui.EndTable();
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(CompanionAccent, $"BADGE CATALOGUE / {showcaseCatalog.Unlocked}/{showcaseCatalog.Entries.Count} unlocked");
        DrawShowcaseFilters();
        var visible = showcaseCatalog.Filter(showcaseSearch, ShowcaseCatalog.Categories[showcaseCategory], showcaseAvailability);
        if (visible.All(badge => badge.Id != showcaseBadgeId)) showcaseBadgeId = visible.FirstOrDefault()?.Id ?? "";
        var width = ImGui.GetContentRegionAvail().X;
        if (width >= 650 * scale && ImGui.BeginTable("ShowcaseBrowser", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Catalogue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Unlock details", ImGuiTableColumnFlags.WidthFixed, Math.Max(215 * scale, width * .31f));
            ImGui.TableNextColumn(); DrawShowcaseGrid(visible, card, 3);
            ImGui.TableNextColumn(); DrawShowcaseDetails(preferences, card);
            ImGui.EndTable();
        }
        else
        {
            DrawShowcaseGrid(visible, card, 2);
            ImGui.Spacing(); DrawShowcaseDetails(preferences, card);
        }
    }
    private bool DrawShowcaseFilters()
    {
        var changed = false;
        if (!ImGui.BeginTable("ShowcaseFilters", 3, ImGuiTableFlags.SizingStretchProp)) return false;
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Availability", ImGuiTableColumnFlags.WidthStretch, .8f);
        ImGui.TableSetupColumn("Search", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableNextColumn(); ImGui.TextDisabled("Category"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.Combo("##ShowcaseCategory", ref showcaseCategory, ShowcaseCatalog.Categories, ShowcaseCatalog.Categories.Length);
        ImGui.TableNextColumn(); ImGui.TextDisabled("Availability"); ImGui.SetNextItemWidth(-1);
        changed |= ImGui.Combo("##ShowcaseAvailability", ref showcaseAvailability, ShowcaseAvailability, ShowcaseAvailability.Length);
        ImGui.TableNextColumn(); ImGui.TextDisabled("Search"); ImGui.SetNextItemWidth(-1);
        if (showcaseFocusSearch) { showcaseFocusSearch = false; ImGui.SetKeyboardFocusHere(); }
        changed |= ImGui.InputTextWithHint("##ShowcaseSearch", "Name or requirement", ref showcaseSearch, 100);
        ImGui.EndTable();
        if (changed) showcasePage = 0;
        return changed;
    }
    private void DrawShowcaseSlot(TrainerCardPreferences preferences, TrainerCardBadge? badge, int slot)
    {
        var scale = CompanionScale;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = Math.Max(70 * scale, 3 * ImGui.GetTextLineHeight() + 12 * scale);
        var min = ImGui.GetCursorScreenPos();
        ImGui.PushID("ShowcaseSlot" + slot);
        var clicked = ImGui.InvisibleButton("##Inspect", new Vector2(Math.Max(1, width - (badge is null ? 0 : 24 * scale)), height));
        var hovered = ImGui.IsItemHovered(); var after = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(badge is null ? CompanionSurface : new Vector4(.14f, .17f, .22f, 1)), 3 * scale);
        draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(badge is null ? CompanionMuted with { W = .25f } : CompanionAccent with { W = hovered ? 1 : .55f }), 3 * scale);
        if (badge is not null)
        {
            var entry = showcaseCatalog.Entries.FirstOrDefault(item => item.Id == badge.Id);
            DrawAchievementIconAt(badge.Id, min + new Vector2(8, 7) * scale, new Vector2(30 * scale));
            ImGui.SetCursorScreenPos(min + new Vector2(8 * scale, height - ImGui.GetTextLineHeight() - 7 * scale));
            DrawClippedText(entry?.Name ?? badge.Name, Math.Max(1, width - 16 * scale), CompanionAccent);
            if (hovered) ImGui.SetTooltip($"{badge.Name}\nSlot {slot + 1} / Click to inspect");
            if (clicked) RevealShowcase(badge.Id);
            ImGui.SetCursorScreenPos(min + new Vector2(width - 23 * scale, 4 * scale));
            ImGui.BeginDisabled(busy);
            if (DrawAccentSmallButton("x##Remove", PokemonOrange)) RemoveShowcaseBadge(preferences, badge.Id);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove this showcase badge.");
        }
        else
        {
            ImGui.SetCursorScreenPos(min + new Vector2(8, 10) * scale);
            DrawClippedText($"Slot {slot + 1}", Math.Max(1, width - 16 * scale), CompanionMuted);
            ImGui.SetCursorScreenPos(min + new Vector2(8 * scale, height - ImGui.GetTextLineHeight() - 7 * scale));
            DrawClippedText("Choose a badge", Math.Max(1, width - 16 * scale), CompanionMuted);
            if (clicked) showcaseFocusSearch = true;
        }
        ImGui.SetCursorScreenPos(after); ImGui.PopID();
    }
    private void DrawShowcaseGrid(IReadOnlyList<ShowcaseBadge> badges, TrainerCardView card, int rows)
    {
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (170 * CompanionScale)), 1, 3);
        var pageSize = columns * rows;
        if (showcaseRevealId.Length > 0)
        {
            var index = 0; while (index < badges.Count && badges[index].Id != showcaseRevealId) index++;
            if (index < badges.Count) showcasePage = index / pageSize;
            showcaseRevealId = "";
        }
        showcasePage = Math.Clamp(showcasePage, 0, Math.Max(0, (badges.Count - 1) / pageSize));
        if (badges.Count == 0) ImGui.TextWrapped("No badges match your search.");
        else if (ImGui.BeginTable("ShowcaseBadgeGrid", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var badge in badges.Skip(showcasePage * pageSize).Take(pageSize))
            { ImGui.TableNextColumn(); DrawShowcaseTile(badge, card.Badges.Any(item => item.Id == badge.Id)); }
            ImGui.EndTable();
        }
        DrawPager("ShowcasePages", ref showcasePage, badges.Count, pageSize);
        var countLabel = $"{badges.Count:N0} badges";
        if (ImGui.GetItemRectSize().X + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(countLabel).X < ImGui.GetContentRegionAvail().X) ImGui.SameLine();
        ImGui.TextDisabled(countLabel);
    }
    private void DrawShowcaseTile(ShowcaseBadge badge, bool shown)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight();
        var min = ImGui.GetCursorScreenPos(); var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = Math.Max(64 * scale, 3 * line + 14 * scale);
        ImGui.PushID(badge.Id);
        var clicked = ImGui.InvisibleButton("##ShowcaseBadge", new Vector2(width, height)); var hovered = ImGui.IsItemHovered();
        var after = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(showcaseBadgeId == badge.Id ? new Vector4(.16f, .20f, .27f, 1) : CompanionSurface), 3 * scale);
        draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(showcaseBadgeId == badge.Id || hovered ? CompanionAccent : CompanionMuted with { W = .25f }), 3 * scale);
        DrawAchievementIconAt(badge.Id, min + new Vector2(6 * scale, (height - 34 * scale) / 2), new Vector2(34 * scale), badge.Unlocked);
        var text = min + new Vector2(47 * scale, 8 * scale); var textWidth = Math.Max(1, width - 55 * scale);
        ImGui.SetCursorScreenPos(text); DrawClippedText(badge.Name, textWidth, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SetCursorScreenPos(text + new Vector2(0, line + 4 * scale));
        DrawClippedText(shown ? "In showcase" : badge.Unlocked ? "Unlocked" : "Locked / " + badge.ProgressLabel, textWidth, shown ? CompanionAccent : badge.Unlocked ? PokemonGreen : CompanionMuted);
        var bar = new Vector2(text.X, min.Y + height - 9 * scale);
        draw.AddRectFilled(bar, bar + new Vector2(textWidth, 3 * scale), ImGui.GetColorU32(CompanionMuted with { W = .18f }));
        draw.AddRectFilled(bar, bar + new Vector2(textWidth * Math.Clamp((float)badge.Progress / badge.Target, 0, 1), 3 * scale), ImGui.GetColorU32(ExpBarBlue));
        if (hovered) ImGui.SetTooltip($"{badge.Name}\n{badge.Source}\n{badge.Requirement}\n{badge.ProgressLabel}");
        if (clicked) showcaseBadgeId = badge.Id;
        ImGui.SetCursorScreenPos(after); ImGui.PopID();
    }
    private void DrawShowcaseDetails(TrainerCardPreferences preferences, TrainerCardView card)
    {
        var badge = showcaseCatalog.Entries.FirstOrDefault(item => item.Id == showcaseBadgeId);
        if (badge is null) return;
        var scale = CompanionScale; var width = Math.Max(1, ImGui.GetContentRegionAvail().X); var content = Math.Max(1, width - 16 * scale);
        var min = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup(); ImGui.Dummy(new Vector2(1, 8 * scale)); ImGui.Indent(8 * scale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);
        if (ImGui.BeginTable("ShowcaseDetailHeading", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Badge", ImGuiTableColumnFlags.WidthFixed, 44 * scale);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextColumn();
            var icon = ImGui.GetCursorScreenPos(); ImGui.Dummy(new Vector2(44 * scale));
            DrawAchievementIconAt(badge.Id, icon, new Vector2(44 * scale));
            ImGui.TableNextColumn();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - 8 * scale));
            ImGui.TextWrapped(badge.Category); ImGui.TextWrapped(badge.Name);
            ImGui.PopTextWrapPos(); ImGui.EndTable();
        }
        var shown = card.Badges.Any(item => item.Id == badge.Id);
        ImGui.TextColored(badge.Unlocked ? PokemonGreen : PokemonYellow, shown ? "In your showcase" : badge.Unlocked ? "Unlocked" : "Locked");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(CompanionMuted, "HOW TO UNLOCK");
        ImGui.TextWrapped(badge.Requirement);
        ImGui.TextWrapped(badge.Source);
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, ExpBarBlue);
        ImGui.ProgressBar(Math.Clamp((float)badge.Progress / badge.Target, 0, 1), new Vector2(content, 5 * scale), "");
        ImGui.PopStyleColor(); ImGui.TextWrapped(badge.ProgressLabel);
        ImGui.Spacing(); ImGui.BeginDisabled(busy || !badge.Unlocked);
        var label = !badge.Unlocked ? "Unlock to showcase" : shown ? "Remove from showcase" : card.Badges.Count < TrainerCard.MaximumBadges ? "Add to showcase" : "Replace a badge";
        var actionHeight = Math.Max(30 * scale, ImGui.GetFrameHeight());
        if (DrawAccentButton(label, new Vector2(content, actionHeight), badge.Unlocked ? PokemonGreen : CompanionMuted))
        {
            if (shown) RemoveShowcaseBadge(preferences, badge.Id);
            else if (card.Badges.Count < TrainerCard.MaximumBadges) ChangeShowcaseBadge(preferences, badge.Id);
            else ImGui.OpenPopup("Replace showcase badge");
        }
        ImGui.EndDisabled();
        if (ImGui.BeginPopup("Replace showcase badge"))
        {
            ImGui.TextUnformatted("Choose a slot to replace");
            for (var index = 0; index < card.Badges.Count; index++)
                if (ImGui.Selectable($"{index + 1}. {card.Badges[index].Name}"))
                { ChangeShowcaseBadge(preferences, badge.Id, index); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        ImGui.PopTextWrapPos(); ImGui.Unindent(8 * scale); ImGui.Dummy(new Vector2(1, 8 * scale)); ImGui.EndGroup();
        ImGui.GetWindowDrawList().AddRect(min, new Vector2(min.X + width, ImGui.GetItemRectMax().Y), ImGui.GetColorU32(CompanionMuted with { W = .3f }), 3 * scale);
    }
}
