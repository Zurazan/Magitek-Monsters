using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private sealed class DoublesChoice
    {
        public int Move = -1;
        public int Switch;
        public int Target;
        public string Modifier = string.Empty;
        public bool Pass;
    }
    private string doublesRequest = string.Empty;
    private readonly DoublesChoice[] doublesChoices = [new(), new()];

    private void DrawDoublesChoices(BattleSnapshotDto current)
    {
        var request = current.Request!;
        var roster = request.Side?.Pokemon ?? [];
        var forced = request.ForceSwitch is not null;
        var stamp = $"{current.Id}:{current.Turn}:{JsonSerializer.Serialize(request)}";
        if (doublesRequest != stamp)
        {
            doublesRequest = stamp;
            for (var slot = 0; slot < 2; slot++)
                doublesChoices[slot] = new DoublesChoice { Pass = forced ? request.ForceSwitch?.ElementAtOrDefault(slot) != true : request.Active?.ElementAtOrDefault(slot)?.PassRequired == true };
        }
        var height = Math.Max(1, ImGui.GetContentRegionAvail().Y - 36 * CompanionScale);
        if (ImGui.BeginTable("DoublesSlots", 2, ImGuiTableFlags.SizingStretchSame))
        {
            for (var slot = 0; slot < 2; slot++)
            {
                ImGui.TableNextColumn();
                ImGui.PushID(slot);
                ImGui.BeginChild("SlotActions", new Vector2(0, height), false, FixedBattlePanel);
                DrawDoublesSlot(current, slot, forced, roster);
                ImGui.EndChild();
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        var actions = Enumerable.Range(0, 2).Select(slot => DoublesCommand(request.Active?.ElementAtOrDefault(slot), doublesChoices[slot])).ToArray();
        var requiredSwitches = Math.Min(request.ForceSwitch?.Count(value => value) ?? 0, roster.Count(candidate => !candidate.Active && !candidate.Condition.EndsWith(" fnt", StringComparison.Ordinal)));
        var complete = actions.All(action => action is not null) && (!forced || doublesChoices.Count(choice => choice.Switch != 0) >= requiredSwitches);
        ImGui.BeginDisabled(!complete);
        if (DrawAccentButton(forced ? "Confirm replacements" : "Confirm both actions", new Vector2(-1, 28 * CompanionScale), PokemonGreen))
        {
            var command = string.Join(", ", actions);
            Run(() => ChoosePvpBattle(command), "Submitting your doubles turn...");
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Slot 1: {actions[0] ?? "Choose an action and target"}\nSlot 2: {actions[1] ?? "Choose an action and target"}");
    }

    private void DrawDoublesSlot(BattleSnapshotDto current, int slot, bool forced, List<BattleSidePokemonDto> roster)
    {
        var request = current.Request!;
        var choice = doublesChoices[slot];
        var active = request.Active?.ElementAtOrDefault(slot);
        var required = forced ? request.ForceSwitch?.ElementAtOrDefault(slot) == true : active?.PassRequired != true;
        DrawClippedText($"SLOT {slot + 1} / {current.PlayerActives.ElementAtOrDefault(slot)?.Name ?? "Empty slot"}", ImGui.GetContentRegionAvail().X, CompanionAccent);
        if (!required)
        {
            choice.Pass = true;
            DrawClippedText(forced ? "No replacement needed" : "Cannot act this turn", ImGui.GetContentRegionAvail().X, CompanionMuted);
            return;
        }
        var reviving = roster.ElementAtOrDefault(slot)?.Reviving == true;
        var shortage = forced && !reviving && (request.ForceSwitch?.Count(value => value) ?? 0) > roster.Count(candidate => !candidate.Active && !candidate.Condition.EndsWith(" fnt", StringComparison.Ordinal));
        var switching = forced || choice.Switch > 0 || choice.Pass;
        ImGui.SetNextItemWidth(forced ? -1 : Math.Max(60, ImGui.GetContentRegionAvail().X * 0.4f));
        if (ImGui.BeginCombo("##SlotAction", switching ? choice.Pass ? "Leave empty" : reviving ? "Revive" : "Switch" : "Fight"))
        {
            if (!forced && ImGui.Selectable("Fight", !switching)) { choice.Switch = 0; choice.Pass = false; }
            ImGui.BeginDisabled(!forced && active?.Trapped == true);
            if (ImGui.Selectable(reviving ? "Revive" : "Switch", switching && !choice.Pass))
            {
                choice.Move = -1; choice.Pass = false;
                choice.Switch = roster.FindIndex(candidate => BattleLayout.CanSwitch(candidate, forced, active?.Trapped == true, reviving)
                    && doublesChoices[1 - slot].Switch != roster.IndexOf(candidate) + 1) + 1;
            }
            ImGui.EndDisabled();
            if (shortage && ImGui.Selectable("Leave empty", choice.Pass)) { choice.Pass = true; choice.Switch = 0; choice.Move = -1; }
            ImGui.EndCombo();
        }
        switching = forced || choice.Switch > 0 || choice.Pass;
        if (!forced && active is not null) { ImGui.SameLine(); DrawDoublesModifier(slot, active); }
        if (choice.Pass) { DrawClippedText("This slot will remain empty", ImGui.GetContentRegionAvail().X, CompanionMuted); return; }
        if (switching)
        {
            var selected = roster.ElementAtOrDefault(choice.Switch - 1);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##SlotSwitch", selected is null ? "Choose Pokémon" : $"{CleanIdent(selected.Ident)} / {selected.Condition}"))
            {
                for (var index = 0; index < roster.Count; index++)
                {
                    var candidate = roster[index];
                    ImGui.BeginDisabled(!BattleLayout.CanSwitch(candidate, forced, active?.Trapped == true, reviving) || doublesChoices[1 - slot].Switch == index + 1);
                    if (ImGui.Selectable($"{CleanIdent(candidate.Ident)} / {candidate.Condition}", choice.Switch == index + 1))
                    { choice.Switch = index + 1; choice.Move = -1; choice.Pass = false; }
                    ImGui.EndDisabled();
                }
                ImGui.EndCombo();
            }
            DrawClippedText(reviving ? "Choose a fainted teammate" : "This replaces the active slot", ImGui.GetContentRegionAvail().X, PokemonOrange);
            return;
        }
        if (active is null) return;
        var cardHeight = Math.Clamp((ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeight() - 12 * CompanionScale) / 2, 30 * CompanionScale, 48 * CompanionScale);
        if (ImGui.BeginTable("DoublesMoves", 2, ImGuiTableFlags.SizingStretchSame))
        {
            for (var index = 0; index < active.Moves.Count; index++)
            {
                ImGui.TableNextColumn();
                var move = active.Moves[index];
                var zMove = choice.Modifier == "zmove" ? active.CanZMove?.ElementAtOrDefault(index) : null;
                ImGui.BeginDisabled(!BattleLayout.CanMove(move, choice.Modifier == "zmove", zMove));
                if (DrawConsoleMoveButton($"DoublesMove{index}", zMove?.Move ?? move.Move, move, cardHeight, choice.Move == index))
                { choice.Move = index; choice.Switch = 0; choice.Pass = false; choice.Target = 0; }
                ImGui.EndDisabled();
            }
            ImGui.EndTable();
        }
        var targets = DoublesTargets(active, choice);
        if (choice.Move < 0) { DrawClippedText("Choose a move", ImGui.GetContentRegionAvail().X, CompanionMuted); return; }
        if (targets.Count == 0) { choice.Target = 0; DrawClippedText("Automatic target", ImGui.GetContentRegionAvail().X, CompanionMuted); return; }
        if (!targets.Contains(choice.Target)) choice.Target = targets.Count == 1 ? targets[0] : 0;
        string TargetName(int location)
        {
            var ally = location < 0;
            var target = (ally ? current.PlayerActives : current.OpponentActives).ElementAtOrDefault(Math.Abs(location) - 1);
            return $"{(ally ? "Ally" : "Foe")} {Math.Abs(location)} / {target?.Name ?? "Empty slot"}";
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##SlotTarget", choice.Target == 0 ? "Choose target" : TargetName(choice.Target)))
        {
            foreach (var location in targets)
                if (ImGui.Selectable(TargetName(location), choice.Target == location)) choice.Target = location;
            ImGui.EndCombo();
        }
    }

    private void DrawDoublesModifier(int slot, BattleActiveDto active)
    {
        var choice = doublesChoices[slot];
        var modifiers = new List<(string Id, string Label)> { (string.Empty, "Normal") };
        if (active.CanMegaEvo) modifiers.Add(("mega", "Mega Evolve"));
        if (active.CanUltraBurst) modifiers.Add(("ultra", "Ultra Burst"));
        if (!string.IsNullOrWhiteSpace(active.CanTerastallize)) modifiers.Add(("terastallize", $"Tera: {active.CanTerastallize}"));
        if (active.CanZMove?.Any(move => move is not null) == true) modifiers.Add(("zmove", "Z-Move"));
        if (!modifiers.Any(modifier => modifier.Id == choice.Modifier)) choice.Modifier = string.Empty;
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##SlotModifier", modifiers.First(modifier => modifier.Id == choice.Modifier).Label)) return;
        foreach (var modifier in modifiers)
            if (ImGui.Selectable(modifier.Label, choice.Modifier == modifier.Id))
            {
                choice.Modifier = modifier.Id; choice.Move = -1; choice.Target = 0;
                if (modifier.Id.Length > 0 && doublesChoices[1 - slot].Modifier == modifier.Id)
                { doublesChoices[1 - slot].Modifier = string.Empty; doublesChoices[1 - slot].Move = -1; doublesChoices[1 - slot].Target = 0; }
            }
        ImGui.EndCombo();
    }

    private static List<int> DoublesTargets(BattleActiveDto active, DoublesChoice choice) => choice.Modifier == "zmove"
        ? active.CanZMove?.ElementAtOrDefault(choice.Move)?.LegalTargets ?? [] : active.Moves.ElementAtOrDefault(choice.Move)?.LegalTargets ?? [];

    private static string? DoublesCommand(BattleActiveDto? active, DoublesChoice choice) =>
        BattleLayout.DoublesCommand(active, choice.Move, choice.Target, choice.Modifier, choice.Pass, choice.Switch);
}
