using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string selectedPvpFormat = string.Empty;
    private string previewBattleId = string.Empty;
    private readonly List<int> previewOrder = [];
    private List<BattleSidePokemonDto> previewRoster = [];
    private int previewChosenCount;
    private string modifierRequest = string.Empty;
    private string pvpModifier = string.Empty;
    private string pvpTeamError = string.Empty;

    private PvpFormatDto? DrawPvpFormats(PvpSeasonDto season)
    {
        if (!season.Formats.Any(format => format.Id == selectedPvpFormat)) selectedPvpFormat = season.Formats.FirstOrDefault()?.Id ?? string.Empty;
        ImGui.TextColored(CompanionAccent, "SEASON FORMAT");
        var selected = DrawOnlineFormatCombo(season.Formats, ref selectedPvpFormat, "QueueFormat");
        if (selected is not null) DrawOnlineFormatSummary(selected);
        ImGui.TextWrapped("Shared season rank and RT. Skill matchmaking is separate per format.");
        if (!season.FormatsAvailable) ImGui.TextColored(PokemonRed, "Season matchmaking unavailable: the server needs its pinned battle rules.");
        return selected;
    }

    private void DrawOnlinePreview(BattleSnapshotDto current)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4) * scale);
        ImGui.BeginChild("OnlinePreviewHeader", new Vector2(0, 48 * scale), false, FixedBattlePanel);
        DrawBattleConsoleHeader(current, true); DrawBattleConsolePopups(current);
        ImGui.EndChild();
        if (previewBattleId != current.Id) { previewRoster = []; previewChosenCount = 0; }
        if (current.Request?.Side?.Pokemon is { } requestedRoster)
        { previewRoster = requestedRoster; previewChosenCount = current.Request.MaxChosenTeamSize ?? requestedRoster.Count; }
        var roster = previewRoster;
        var count = Math.Clamp(previewChosenCount, 0, roster.Count);
        var waiting = current.Request is null || current.Request.Wait == true;
        PvpPresentation.PrepareOrder(previewOrder, roster.Count, count, previewBattleId != current.Id);
        previewBattleId = current.Id;
        ImGui.TextWrapped(waiting ? "Team submitted. Waiting for your opponent to confirm." : count == roster.Count ? "Choose your lead and battle order. All registered Pokémon will battle."
            : $"Choose {count} from your registered roster. {(current.GameType == "doubles" ? "The first two lead together." : "Your first pick leads.")}");
        var layout = PvpPresentation.Lobby(ImGui.GetContentRegionAvail().X, Math.Max(1, ImGui.GetContentRegionAvail().Y - 72 * scale), scale);
        ImGui.BeginChild("OnlinePreviewOpponent", new Vector2(layout.SetupWidth, layout.SetupHeight), true);
        DrawOnlineOpponentRoster(current);
        ImGui.EndChild();
        if (layout.SideBySide) ImGui.SameLine(0, layout.Gap); else ImGui.Spacing();
        ImGui.BeginChild("OnlinePreviewOrder", new Vector2(layout.TeamWidth, layout.TeamHeight), true);
        ImGui.BeginDisabled(busy || waiting);
        DrawPvpPreview(current);
        ImGui.EndDisabled(); ImGui.EndChild();
        var lead = previewOrder.Count > 0 ? CleanIdent(roster[previewOrder[0] - 1].Ident) : "Choose a lead";
        DrawClippedText($"{previewOrder.Count}/{count} ready / Lead: {lead}", ImGui.GetContentRegionAvail().X, PokemonYellow);
        var button = Math.Max(1, ImGui.GetContentRegionAvail().X - 130 * scale);
        ImGui.BeginDisabled(busy || previewOrder.Count != count || count == 0 || waiting);
        if (DrawAccentButton("Confirm team and order", new Vector2(button, 34 * scale), PokemonGreen))
        { var choice = "team " + string.Concat(previewOrder); Run(() => ChoosePvpBattle(choice), "Confirming battle team..."); }
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton("Forfeit", new Vector2(-1, 34 * scale), PokemonRed)) ImGui.OpenPopup("Confirm online forfeit");
        ImGui.EndDisabled();
        if (BeginConfirmationModal("Confirm online forfeit"))
        {
            ImGui.TextWrapped("Forfeit this match?");
            DrawOnlineForfeitNotice();
            if (DrawConfirmationActions("Confirm forfeit", PokemonRed, busy)) { ImGui.CloseCurrentPopup(); Run(() => ChoosePvpBattle("forfeit"), "Forfeiting match..."); }
            EndConfirmationModal();
        }
        ImGui.PopStyleVar(2);
    }

    private void DrawOnlineOpponentRoster(BattleSnapshotDto current)
    {
        ImGui.TextColored(PokemonOrange, "OPPONENT'S TEAM");
        var roster = current.Log.Where(line => line.StartsWith("|poke|p2|", StringComparison.Ordinal)).Select(line => line.Split('|')[3]).ToArray();
        if (roster.Length == 0) { ImGui.TextWrapped("Waiting for opponent preview..."); return; }
        var scale = CompanionScale;
        var columns = ImGui.GetContentRegionAvail().X >= 210 * scale ? 2 : 1;
        if (!ImGui.BeginTable("OnlineOpponentRoster", columns, ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var details in roster)
        {
            ImGui.TableNextColumn();
            var name = details.Split(',')[0];
            var width = ImGui.GetContentRegionAvail().X;
            var size = Math.Min(58 * scale, width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - size) / 2));
            DrawPokemonIcon(name, details.Contains(", shiny", StringComparison.Ordinal), new Vector2(size));
            DrawClippedText(name, width, CompanionMuted);
            var types = SpeciesDisplayCatalog.Get(name)?.Types ?? [];
            DrawConsoleTypeRow(types, BattleLayout.Types(width, scale * 0.7f, types.Length));
            ImGui.Spacing();
        }
        ImGui.EndTable();
    }

    private void DrawPvpPreview(BattleSnapshotDto current)
    {
        var roster = previewRoster;
        var count = Math.Clamp(previewChosenCount, 0, roster.Count);
        var all = count == roster.Count;
        ImGui.TextColored(CompanionAccent, $"YOUR TEAM / {previewOrder.Count} of {count} ready");
        ImGui.TextWrapped(all ? "Make lead puts that Pokémon first. Earlier/Later sets the rest." : "Pick in battle order. Remove a pick to choose a different Pokémon.");
        if (DrawAccentSmallButton(all ? "Reset order" : "Clear picks", CompanionMuted))
        { previewOrder.Clear(); if (all) previewOrder.AddRange(Enumerable.Range(1, roster.Count)); }
        var orderedSlots = previewOrder.Concat(Enumerable.Range(1, roster.Count).Where(slot => !previewOrder.Contains(slot))).ToArray();
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var columns = width >= 420 * scale ? 3 : width >= 280 * scale ? 2 : 1;
        if (!ImGui.BeginTable("ConsoleTeamOrder", columns, ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var slot in orderedSlots)
        {
            ImGui.TableNextColumn(); ImGui.PushID(slot);
            var pokemon = roster[slot - 1]; var order = previewOrder.IndexOf(slot); var selected = order >= 0;
            var role = !selected ? "NOT PICKED" : order < (current.GameType == "doubles" ? 2 : 1) ? $"{order + 1}. LEAD" : $"{order + 1}. RESERVE";
            ImGui.PushStyleColor(ImGuiCol.ChildBg, selected ? new Vector4(0.13f, 0.18f, 0.25f, 1) : CompanionSurface);
            ImGui.BeginChild("TeamOrderCard", new Vector2(0, Math.Max(132 * scale, 5 * ImGui.GetTextLineHeight() + 44 * scale)), true, FixedBattlePanel);
            DrawClippedText(role, ImGui.GetContentRegionAvail().X, selected ? PokemonYellow : CompanionMuted);
            DrawPokemonIcon(pokemon.Details.Split(',')[0], pokemon.Details.Contains(", shiny", StringComparison.Ordinal), new Vector2(34 * scale));
            ImGui.SameLine(); ImGui.BeginGroup();
            DrawClippedText(CleanIdent(pokemon.Ident), Math.Max(1, ImGui.GetContentRegionAvail().X), CompanionAccent);
            var types = SpeciesDisplayCatalog.Get(pokemon.Details.Split(',')[0])?.Types ?? [];
            DrawConsoleTypeRow(types, BattleLayout.Types(ImGui.GetContentRegionAvail().X, scale * 0.72f, types.Length));
            ImGui.EndGroup();
            ImGui.BeginDisabled(!selected && previewOrder.Count >= count);
            if (DrawAccentButton(all ? order == 0 ? "Leading" : "Make lead" : selected ? "Remove pick" : $"Pick #{previewOrder.Count + 1}", new Vector2(-1, 24 * scale), selected ? CompanionAccent : CompanionMuted))
                PvpPresentation.Pick(previewOrder, slot, roster.Count, count);
            ImGui.EndDisabled();
            var button = Math.Max(1, (ImGui.GetContentRegionAvail().X - 4 * scale) / 2);
            ImGui.BeginDisabled(order <= 0);
            if (DrawAccentButton("Earlier", new Vector2(button, 22 * scale), CompanionAccent)) PvpPresentation.Move(previewOrder, order, -1);
            ImGui.EndDisabled(); ImGui.SameLine(0, 4 * scale);
            ImGui.BeginDisabled(order < 0 || order >= previewOrder.Count - 1);
            if (DrawAccentButton("Later", new Vector2(button, 22 * scale), CompanionMuted)) PvpPresentation.Move(previewOrder, order, 1);
            ImGui.EndDisabled(); ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawPvpModifier(BattleSnapshotDto current, BattleActiveDto? active)
    {
        var stamp = $"{current.Id}:{current.Turn}:{current.PlayerActive?.Ident}";
        if (modifierRequest != stamp) { modifierRequest = stamp; pvpModifier = string.Empty; }
        var options = new List<(string Id, string Label)> { (string.Empty, "Normal move") };
        if (active?.CanMegaEvo == true) options.Add(("mega", "Mega Evolve"));
        if (active?.CanUltraBurst == true) options.Add(("ultra", "Ultra Burst"));
        if (!string.IsNullOrWhiteSpace(active?.CanTerastallize)) options.Add(("terastallize", $"Terastallize: {active.CanTerastallize}"));
        if (active?.CanZMove?.Any(move => move is not null) == true) options.Add(("zmove", "Use Z-Move"));
        if (!options.Any(option => option.Id == pvpModifier)) pvpModifier = string.Empty;
        if (options.Count < 2) return;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##BattleModifier", options.First(option => option.Id == pvpModifier).Label))
        {
            foreach (var option in options) if (ImGui.Selectable(option.Label, pvpModifier == option.Id)) pvpModifier = option.Id;
            ImGui.EndCombo();
        }
    }

    private string PvpModifierSuffix(BattleActiveDto? active, int move)
    {
        if (pvpModifier == "zmove" && (active?.CanZMove is not { } zMoves || move >= zMoves.Count || zMoves[move] is null)) return string.Empty;
        return pvpModifier.Length == 0 ? string.Empty : " " + pvpModifier;
    }
}
