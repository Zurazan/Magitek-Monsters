using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<RegionalLeagueDto> regionalLeagues = [];
    private string regionalRegionId = "kanto";
    private string regionalCircuitId = "kanto-classic";
    private string regionalLeaderId = string.Empty;
    private int regionalStage;

    private void ResetRegionalLeagues()
    {
        regionalRegionId = "kanto"; regionalCircuitId = "kanto-classic"; regionalLeaderId = ""; regionalStage = 0;
        leagueRematch = false; leagueEntryCircuitId = null; pveSetupCircuitId = null; pveSetupOpponentId = null;
    }

    private RegionalCircuitDto? SelectedRegionalCircuit => regionalLeagues.SelectMany(region => region.Circuits).FirstOrDefault(circuit => circuit.Id == regionalCircuitId);

    private string? RegionalPreparationLock(OpponentDto opponent)
    {
        var clears = RegionalLeaguePresentation.Clears(trainer!);
        if (opponent.RegionId is null) return PvePresentation.LockReason(opponent, clears, opponents);
        var circuit = regionalLeagues.SelectMany(region => region.Circuits).FirstOrDefault(circuit => circuit.Id == pveSetupCircuitId);
        var region = regionalLeagues.FirstOrDefault(region => region.Id == opponent.RegionId);
        if (circuit is null || region is null || circuit.RegionId != region.Id) return "Choose a valid regional League lineup.";
        if (opponent.RegionalRole == "gym")
            return RegionalLeaguePresentation.GymLock(region, circuit, opponent, clears, opponent.RematchOf is not null, regionalLeagues, opponents);
        return RegionalLeaguePresentation.CircuitLock(region, circuit, clears, regionalLeagues, opponents)
            ?? PvePresentation.LockReason(opponent, clears, opponents);
    }

    private void DrawRegionalLeagues()
    {
        if (trainer is null) return;
        if (regionalLeagues.Count == 0) { ImGui.TextWrapped("The regional League catalog is loading. It retries automatically."); return; }
        var clears = RegionalLeaguePresentation.Clears(trainer);
        var region = regionalLeagues.FirstOrDefault(region => region.Id == regionalRegionId) ?? regionalLeagues[0];
        regionalRegionId = region.Id;
        var scale = CompanionScale;
        ImGui.SetNextItemWidth(Math.Min(240 * scale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Region", region.Name))
        {
            foreach (var choice in regionalLeagues)
                if (ImGui.Selectable(choice.Name, choice.Id == regionalRegionId))
                {
                    region = choice; regionalRegionId = choice.Id; regionalCircuitId = choice.Circuits.FirstOrDefault()?.Id ?? "";
                    regionalLeaderId = ""; regionalStage = 0; leagueRematch = false;
                }
            ImGui.EndCombo();
        }
        var circuit = region.Circuits.FirstOrDefault(circuit => circuit.Id == regionalCircuitId) ?? region.Circuits.FirstOrDefault();
        if (circuit is null) { ImGui.TextWrapped("No League lineup is available for this region."); return; }
        regionalCircuitId = circuit.Id;
        if (region.Circuits.Count > 1)
        {
            ImGui.SetNextItemWidth(Math.Min(430 * scale, ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo("Lineup", circuit.Name))
            {
                foreach (var choice in region.Circuits)
                    if (ImGui.Selectable(choice.Name, choice.Id == circuit.Id))
                    { circuit = choice; regionalCircuitId = choice.Id; regionalLeaderId = ""; regionalStage = 0; leagueRematch = false; }
                ImGui.EndCombo();
            }
        }
        var champion = RegionalLeagueProgress.HasChampion(clears, region.Id);
        if (!champion) leagueRematch = false;
        if (DrawCompanionSegment("First clears", !leagueRematch, 126 * scale)) leagueRematch = false;
        if (ImGui.GetContentRegionAvail().X >= 186 * scale + ImGui.GetStyle().ItemSpacing.X) ImGui.SameLine();
        ImGui.BeginDisabled(!champion);
        if (DrawCompanionSegment("Champion rematches", leagueRematch, 180 * scale)) leagueRematch = true;
        ImGui.EndDisabled();
        if (!champion && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Become this region's Champion to unlock level-100 rematches.");
        var slots = RegionalLeaguePresentation.Milestones(region, circuit);
        var earned = slots.Count(slot => RegionalLeaguePresentation.Earned(slot, clears));
        ImGui.Spacing();
        if (ImGui.BeginTable("RegionalStages", 3, ImGuiTableFlags.SizingStretchSame))
        {
            string[] labels = [region.Format == "trials" ? $"Island Challenge {earned}/{slots.Length}" : slots.Length == 0 ? "Entry requirements" : $"Gyms {earned}/{slots.Length}",
                circuit.OpponentIds.Count == 1 ? "League challenge" : region.Format == "cup" ? "Champion Cup" : "Elite Four", region.Format == "trials" ? "League final" : "Champion"];
            for (var index = 0; index < labels.Length; index++)
            {
                ImGui.TableNextColumn();
                if (DrawAccentButton(labels[index], new Vector2(-1, 36 * scale), regionalStage == index ? CompanionAccent : CompanionMuted)) regionalStage = index;
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        if (regionalStage == 1) { DrawLeagueEntry(); return; }
        if (regionalStage == 2) { DrawRegionalChampion(region, circuit, clears); return; }
        if (slots.Length == 0)
        {
            var reason = RegionalLeaguePresentation.LeagueLock(region, circuit, clears, leagueRematch, regionalLeagues, opponents);
            ImGui.TextWrapped(reason ?? "This lineup is ready. Prepare your team for the League challenge.");
            if (DrawAccentButton("View League lineup", new Vector2(-1, 34 * scale), CompanionAccent)) regionalStage = 1;
            return;
        }
        DrawRegionalGymBoard(region, circuit, slots, clears);
    }

    private void DrawRegionalGymBoard(RegionalLeagueDto region, RegionalCircuitDto circuit, RegionalMilestoneDto[] slots, HashSet<string> clears)
    {
        var scale = CompanionScale;
        var selectedSlot = slots.FirstOrDefault(slot => slot.LeaderIds.Contains(regionalLeaderId));
        if (selectedSlot is null)
        {
            selectedSlot = slots.FirstOrDefault(slot => !RegionalLeaguePresentation.Earned(slot, clears)) ?? slots[0];
            var index = Array.IndexOf(slots, selectedSlot);
            regionalLeaderId = circuit.PreferredLeaderIds.ElementAtOrDefault(index) ?? selectedSlot.LeaderIds[0];
        }
        var available = ImGui.GetContentRegionAvail();
        var sideBySide = available.X >= 690 * scale;
        var listWidth = sideBySide ? available.X * 0.56f : available.X;
        var detailWidth = sideBySide ? Math.Max(1, available.X - listWidth - 10 * scale) : available.X;
        var cardHeight = ImGui.GetTextLineHeight() * 4 + 33 * scale;
        var listHeight = sideBySide ? Math.Max(1, available.Y) : Math.Min(cardHeight * 2 + 12 * scale, Math.Max(180 * scale, available.Y * 0.45f));
        ImGui.BeginChild("RegionalGyms", new Vector2(listWidth, listHeight), false);
        if (ImGui.BeginTable("RegionalGymCards", listWidth >= 340 * scale ? 2 : 1, ImGuiTableFlags.SizingStretchSame))
        {
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                var leaderId = slot.Id == selectedSlot.Id ? regionalLeaderId : circuit.PreferredLeaderIds.ElementAtOrDefault(index) ?? slot.LeaderIds[0];
                var leader = opponents.FirstOrDefault(entry => entry.Id == (leagueRematch ? "rematch-" + leaderId : leaderId));
                if (leader is null) continue;
                ImGui.TableNextColumn();
                var reason = RegionalLeaguePresentation.GymLock(region, circuit, leader, clears, leagueRematch, regionalLeagues, opponents);
                if (DrawRegionalGymCard(slot, leader, slot.Id == selectedSlot.Id, RegionalLeaguePresentation.Earned(slot, clears), reason, cardHeight))
                { regionalLeaderId = leaderId; selectedSlot = slot; }
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
        if (sideBySide) ImGui.SameLine(0, 10 * scale); else ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("RegionalGymDetail", new Vector2(detailWidth, sideBySide ? Math.Max(1, available.Y) : 310 * scale), true);
        if (selectedSlot.LeaderIds.Count > 1)
        {
            var selected = opponents.FirstOrDefault(entry => entry.Id == regionalLeaderId);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##AlternateGymLeader", selected?.Name ?? "Choose leader"))
            {
                foreach (var id in selectedSlot.LeaderIds)
                {
                    var choice = opponents.FirstOrDefault(entry => entry.Id == id);
                    if (choice is not null && ImGui.Selectable(choice.Name, id == regionalLeaderId)) regionalLeaderId = id;
                }
                ImGui.EndCombo();
            }
        }
        var opponent = opponents.FirstOrDefault(entry => entry.Id == (leagueRematch ? "rematch-" + regionalLeaderId : regionalLeaderId));
        if (opponent is not null)
        {
            var reason = RegionalLeaguePresentation.GymLock(region, circuit, opponent, clears, leagueRematch, regionalLeagues, opponents);
            DrawRegionalOpponent(opponent, selectedSlot.Name, reason, circuit.Id);
            if (trainer!.PveProgress.LegacyLeagueCredits.Contains(regionalLeaderId)) ImGui.TextWrapped("Progress carried over from your previous Gym or League victories.");
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }

    private bool DrawRegionalGymCard(RegionalMilestoneDto slot, OpponentDto opponent, bool selected, bool earned, string? reason, float height)
    {
        var clicked = DrawPveCardSurface("RegionalGym##" + slot.Id, height, selected, out var min, out var max, out var after);
        var scale = CompanionScale; var width = Math.Max(1, max.X - min.X - 24 * scale);
        ImGui.GetWindowDrawList().PushClipRect(min + new Vector2(5 * scale, 0), max - new Vector2(5 * scale, 0), true);
        ImGui.SetCursorScreenPos(min + new Vector2(12, 10) * scale); ImGui.BeginGroup();
        DrawClippedText(earned ? "EARNED" : reason is null ? "NEXT CHALLENGE" : "LOCKED", width, earned ? PokemonGreen : reason is null ? CompanionAccent : CompanionMuted);
        DrawClippedText(opponent.Name, width, selected ? CompanionAccent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        DrawClippedText($"{opponent.Specialty} / Lv. {opponent.RecommendedLevel}", width, CompanionMuted);
        DrawClippedText(slot.Name, width, CompanionMuted);
        ImGui.EndGroup(); ImGui.GetWindowDrawList().PopClipRect(); ImGui.SetCursorScreenPos(after);
        return clicked;
    }

    private void DrawRegionalOpponent(OpponentDto opponent, string reward, string? reason, string circuitId)
    {
        ImGui.TextColored(CompanionAccent, opponent.Name);
        ImGui.TextWrapped($"{opponent.Specialty} / Lv. {opponent.RecommendedLevel}");
        ImGui.TextWrapped(opponent.TacticalHint);
        if (reward.Length > 0) ImGui.TextColored(PokemonBlue, reward);
        DrawPveTeamStrip(opponent.TeamSpecies.ToArray(), ImGui.GetContentRegionAvail().X);
        ImGui.TextWrapped($"Signature partner: {opponent.Ace}");
        ImGui.TextWrapped($"¥ {opponent.RewardCurrency:N0} / {opponent.RewardExperience * (InventoryCount("expshare") > 0 ? 2 : 1):N0} party EXP");
        ImGui.TextWrapped("Medicine: 3 actions. Held items remain active.");
        if (reason is not null) ImGui.TextWrapped(reason);
        ImGui.BeginDisabled(busy || CollectionLocked || reason is not null);
        if (DrawAccentButton("Prepare battle", new Vector2(-1, 34 * CompanionScale), CompanionAccent)) OpenPveChallenge(opponent.Id, circuitId);
        ImGui.EndDisabled();
    }

    private void DrawRegionalChampion(RegionalLeagueDto region, RegionalCircuitDto circuit, HashSet<string> clears)
    {
        var original = circuit.OpponentIds.LastOrDefault();
        var champion = opponents.FirstOrDefault(entry => entry.Id == (leagueRematch ? "rematch-" + original : original));
        if (champion is null) { ImGui.TextWrapped("The Champion lineup is loading."); return; }
        var complete = original is not null && clears.Contains(original);
        ImGui.TextColored(complete ? PokemonGreen : PokemonYellow, complete ? $"{region.Name} League complete" : "Final opponent");
        var reason = RegionalLeaguePresentation.LeagueLock(region, circuit, clears, leagueRematch, regionalLeagues, opponents);
        if (!complete) reason ??= "Reach the final battle of this region's League run to earn the first victory.";
        DrawRegionalOpponent(champion, circuit.Name, reason, circuit.Id);
        if (DrawAccentButton("View League run", new Vector2(-1, 32 * CompanionScale), CompanionAccent)) regionalStage = 1;
    }
}
