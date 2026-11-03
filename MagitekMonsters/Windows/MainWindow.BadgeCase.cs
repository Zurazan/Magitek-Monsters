using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string badgeCaseRegionId = "";

    private void DrawRegionalBadgeCase()
    {
        if (trainer is null) return;
        if (regionalLeagues.Count == 0) { ImGui.TextWrapped("The regional League catalog is loading."); return; }
        var records = TrainerLeagueRecords.Resolve(trainer, regionalLeagues);
        var badges = records.SelectMany(record => record.Milestones).Where(slot => slot.Definition.Kind == "badge").ToArray();
        var trials = records.SelectMany(record => record.Milestones).Where(slot => slot.Definition.Kind != "badge").ToArray();
        ImGui.TextWrapped($"Gym badges {badges.Count(slot => slot.Earned)}/{badges.Length} / Island challenges {trials.Count(slot => slot.Earned)}/{trials.Length} / Regional Champions {records.Count(record => record.Champion)}/{records.Count}");
        var selected = records.FirstOrDefault(record => record.Region.Id == badgeCaseRegionId)
            ?? records.FirstOrDefault(record => record.Champion || record.Milestones.Any(slot => slot.Earned)) ?? records[0];
        badgeCaseRegionId = selected.Region.Id;
        ImGui.SetNextItemWidth(Math.Min(300 * CompanionScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##BadgeCaseRegion", selected.Region.Name))
        {
            foreach (var record in records)
                if (ImGui.Selectable(record.Region.Name, selected.Region.Id == record.Region.Id))
                { selected = record; badgeCaseRegionId = record.Region.Id; }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped(selected.Region.Format == "trials" ? "Trials and grand trials belong to the Island Challenge. They are not Gym badges."
            : selected.Milestones.Count == 0 ? "This League awards no Gym badges. Complete its lineup to earn Champion honours."
            : "Each badge counts once. Alternate leaders and rematches share the same badge.");
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (220 * CompanionScale)), 1, 4);
        if (selected.Milestones.Count > 0 && ImGui.BeginTable("RegionalBadgeGrid", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var slot in selected.Milestones)
            {
                ImGui.TableNextColumn();
                DrawRegionalBadgeSlot(selected, slot);
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        ImGui.TextColored(CompanionAccent, "LEAGUE LINEUPS");
        foreach (var circuit in selected.Region.Circuits)
        {
            var complete = selected.CompletedCircuits.Any(entry => entry.Id == circuit.Id);
            var label = complete ? "COMPLETE" : "NOT CLEARED";
            var icon = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(28 * CompanionScale));
            DrawAchievementIconAt("champion:" + selected.Region.Id, icon, new Vector2(28 * CompanionScale), complete);
            ImGui.SameLine();
            ImGui.TextWrapped(label + " / " + circuit.Name);
        }
    }

    private void DrawRegionalBadgeSlot(TrainerLeagueRecord record, TrainerLeagueMilestone slot)
    {
        var scale = CompanionScale;
        var line = ImGui.GetTextLineHeight();
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 3 * line + 20 * scale);
        var color = slot.Earned ? PokemonYellow : CompanionMuted;
        ImGui.PushID(slot.Definition.Id);
        ImGui.InvisibleButton("##Badge", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(Vector4.Lerp(CompanionSurface, color, slot.Earned ? 0.08f : 0)), 4 * scale);
        draw.AddRect(min, min + size, ImGui.GetColorU32(color with { W = hovered ? 0.9f : 0.35f }), 4 * scale);
        var iconSize = new Vector2(44 * scale);
        DrawAchievementIconAt("league:" + slot.Definition.Id, min + new Vector2(8 * scale, (size.Y - iconSize.Y) / 2), iconSize, slot.Earned);
        var textX = 62 * scale;
        draw.PushClipRect(min + new Vector2(textX, 0), min + size - new Vector2(8 * scale, 0), true);
        draw.AddText(min + new Vector2(textX, 8 * scale), ImGui.GetColorU32(color), slot.Earned ? "EARNED" : "LOCKED");
        draw.AddText(min + new Vector2(textX, 8 * scale + line), ImGui.GetColorU32(slot.Earned ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : CompanionMuted), slot.Definition.Name);
        var kind = slot.Definition.Kind switch { "trial" => "Island trial", "grand-trial" => "Grand trial", _ => "Gym badge" };
        draw.AddText(min + new Vector2(textX, 8 * scale + 2 * line), ImGui.GetColorU32(CompanionMuted), kind);
        draw.PopClipRect();
        if (hovered) ImGui.SetTooltip($"{record.Region.Name} / {slot.Definition.Name}\n{kind} / {(slot.Earned ? "Earned" : "Not earned yet")}");
        ImGui.PopID();
    }
}
