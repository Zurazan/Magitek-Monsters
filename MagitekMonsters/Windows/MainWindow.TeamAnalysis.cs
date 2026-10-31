using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string teamAnalysisOwner = string.Empty;
    private string? teamAnalysisPresetId;
    private bool requestTeamAnalysis;
    private bool teamAnalysisInLibrary;
    private string teamAnalysisStamp = string.Empty;
    private BattleHelpDto? teamAnalysisMoveCatalog;
    private TeamAnalysisReport? teamAnalysisReport;
    private string teamAnalysisDefenseType = "Ice";
    private string teamAnalysisAttackType = "Water";

    private void ResetTeamAnalysis()
    {
        teamAnalysisOwner = teamAnalysisStamp = string.Empty;
        teamAnalysisPresetId = null;
        requestTeamAnalysis = teamAnalysisInLibrary = false;
        teamAnalysisMoveCatalog = null;
        teamAnalysisReport = null;
        teamAnalysisDefenseType = "Ice"; teamAnalysisAttackType = "Water";
    }

    private void OpenTeamAnalysis(string? presetId = null)
    {
        if (trainer is null) return;
        teamAnalysisOwner = trainer.User.Id;
        teamAnalysisPresetId = presetId;
        teamAnalysisInLibrary = presetId is not null;
        requestTeamAnalysis = true;
        teamAnalysisReport = null;
    }

    private void DrawTeamAnalysis(bool inLibrary = false)
    {
        if (teamAnalysisInLibrary != inLibrary) return;
        if (requestTeamAnalysis)
        {
            if (teamAnalysisOwner == trainer?.User.Id) ImGui.OpenPopup("Team analysis");
            requestTeamAnalysis = false;
        }
        if (!ImGui.IsPopupOpen("Team analysis")) return;
        var scale = CompanionScale; var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(820 * scale, work.X * 0.94f), Math.Min(760 * scale, work.Y * 0.9f)), ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal("Team analysis", ref open, ImGuiWindowFlags.None)) return;
        if (trainer is null || teamAnalysisOwner != trainer.User.Id)
        { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        var preset = teamAnalysisPresetId is null ? null : trainer.Collection.TeamPresets.FirstOrDefault(team => team.Id == teamAnalysisPresetId);
        if (teamAnalysisPresetId is not null && preset is null)
        { ImGui.TextWrapped("This saved team is no longer available."); if (ImGui.Button("Close")) ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        var owned = trainer.Party.Concat(trainer.Storage).DistinctBy(pokemon => pokemon.Id).ToDictionary(pokemon => pokemon.Id);
        var members = preset is null ? trainer.Party.OrderBy(pokemon => pokemon.PartySlot).ToArray()
            : preset.PokemonIds.Where(owned.ContainsKey).Select(id => owned[id]).ToArray();
        var expected = preset?.PokemonIds.Count ?? members.Length;
        var stamp = JsonSerializer.Serialize(new { expected, Pokemon = members.Select(pokemon => new {
            pokemon.Id, pokemon.Nickname, pokemon.Species, pokemon.Form, pokemon.Types, pokemon.Ability, pokemon.HeldItem, pokemon.Moves, pokemon.Ivs }) });
        if (teamAnalysisReport is null || stamp != teamAnalysisStamp || !ReferenceEquals(teamAnalysisMoveCatalog, battleHelp))
        {
            teamAnalysisReport = TeamAnalysis.Analyze(members, battleMoveHelp.Values, expected);
            teamAnalysisStamp = stamp; teamAnalysisMoveCatalog = battleHelp;
        }
        DrawSectionHeader(preset is null ? "Current party analysis" : preset.Name, $"{teamAnalysisReport.Members.Count}/{expected} members analyzed / Current builds");
        var footer = ImGui.GetFrameHeight() + 16 * scale;
        if (ImGui.BeginChild("TeamAnalysisBody", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - footer)), false))
            DrawTeamAnalysisContent(teamAnalysisReport);
        ImGui.EndChild();
        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawTeamAnalysisContent(TeamAnalysisReport report)
    {
        if (report.MissingMembers > 0) ImGui.TextWrapped($"Partial team: {report.MissingMembers} missing or fusion-locked member(s) excluded.");
        if (report.Members.Count == 0) { ImGui.TextWrapped("Add an owned, available Pokémon to analyze this team."); return; }
        ImGui.TextWrapped(string.Join(" / ", report.Members.Select(member => member.Name)));
        if (report.UnknownDefenders > 0) ImGui.TextWrapped($"Type data is missing for {report.UnknownDefenders} member(s). Defensive counts are incomplete.");
        if (report.UnknownMoves > 0) ImGui.TextWrapped($"Details are missing for {report.UnknownMoves} move(s). Attacking coverage is incomplete.");
        var conditional = report.Members.Where(member => member.Notes.Count > 0).Select(member => member.Name).ToArray();
        if (conditional.Length > 0) ImGui.TextWrapped("Conditions or move exceptions: " + string.Join(", ", conditional) + ". Details below.");
        var shared = report.Defense.Where(row => row.Weak >= 2).OrderByDescending(row => row.Weak).ToArray();
        ImGui.TextWrapped(shared.Length == 0 ? "No shared weakness in the known base matchups."
            : "Shared weaknesses: " + string.Join(", ", shared.Select(row => $"{row.Type} ({row.Weak})")));
        var exposed = report.Defense.Where(row => row.Weak > 0 && row.Resistant + row.Immune == 0).ToArray();
        if (exposed.Length > 0) ImGui.TextWrapped("Weak with no resistance or immunity: " + string.Join(", ", exposed.Select(row => row.Type)));
        ImGui.Spacing();
        if (ImGui.BeginTabBar("TeamAnalysisTabs"))
        {
            if (ImGui.BeginTabItem("Defensive matchups"))
            {
                DrawTeamDefense(report);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Attacking coverage"))
            {
                DrawTeamOffense(report);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Conditions & move exceptions"))
        {
            var noted = false;
            foreach (var member in report.Members.Where(member => member.Notes.Count > 0))
            {
                noted = true; ImGui.TextColored(CompanionAccent, member.Name);
                foreach (var note in member.Notes) { ImGui.Bullet(); ImGui.SameLine(); ImGui.TextWrapped(note); }
            }
            if (!noted) ImGui.TextWrapped("No additional member-specific conditions.");
        }
        ImGui.TextWrapped("Base matchups use current types, permanent forms and supported abilities. Weather, terrain, Tera, temporary boosts, conditional item effects and opposing abilities are not simulated.");
    }

    private void DrawTeamDefense(TeamAnalysisReport report)
    {
        ImGui.TextWrapped("Incoming attack types. Counts are Pokémon; select a row for each member's matchup.");
        if (ImGui.BeginTable("TeamDefenseTypes", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableSetupColumn("Type"); ImGui.TableSetupColumn("Weak"); ImGui.TableSetupColumn("Resist"); ImGui.TableSetupColumn("Immune"); ImGui.TableHeadersRow();
            foreach (var row in report.Defense)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                if (ImGui.Selectable($"{row.Type}##DefenseType", teamAnalysisDefenseType == row.Type, ImGuiSelectableFlags.SpanAllColumns)) teamAnalysisDefenseType = row.Type;
                ImGui.TableNextColumn(); ImGui.TextColored(row.Weak > 1 ? PokemonRed : row.Weak > 0 ? PokemonOrange : CompanionMuted, row.Weak.ToString());
                TeamAnalysisTooltip(row.Members.Where(value => value.Multiplier > 1).Select(value => value.Member.Name));
                ImGui.TableNextColumn(); ImGui.TextColored(row.Resistant > 0 ? PokemonBlue : CompanionMuted, row.Resistant.ToString());
                TeamAnalysisTooltip(row.Members.Where(value => value.Multiplier is > 0 and < 1).Select(value => value.Member.Name));
                ImGui.TableNextColumn(); ImGui.TextColored(row.Immune > 0 ? PokemonGreen : CompanionMuted, row.Immune.ToString());
                TeamAnalysisTooltip(row.Members.Where(value => value.Multiplier == 0).Select(value => value.Member.Name));
            }
            ImGui.EndTable();
        }
        var selected = report.Defense.FirstOrDefault(row => row.Type == teamAnalysisDefenseType) ?? report.Defense[0];
        ImGui.Spacing(); ImGui.TextColored(CompanionAccent, $"{selected.Type} matchups");
        foreach (var value in selected.Members)
        {
            var multiplier = value.Multiplier is { } amount ? amount.ToString("0.###", CultureInfo.InvariantCulture) + "x" : "Unknown";
            ImGui.TextWrapped($"{value.Member.Name} / {string.Join(" + ", value.Member.Types)} / {multiplier} / {value.Source}");
        }
    }

    private void DrawTeamOffense(TeamAnalysisReport report)
    {
        ImGui.TextWrapped("Coverage against single-type opponents, using current moves. SE users counts Pokémon with a super-effective move. This compares type effectiveness, not damage.");
        var gaps = report.Offense.Where(row => row.SuperEffective == 0).Select(row => row.Type).ToArray();
        ImGui.TextWrapped(gaps.Length == 0 ? "Every type has a known super-effective answer." : "No known super-effective coverage: " + string.Join(", ", gaps));
        if (ImGui.BeginTable("TeamOffenseTypes", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableSetupColumn("Vs. type"); ImGui.TableSetupColumn("SE users"); ImGui.TableSetupColumn("Best matchup"); ImGui.TableHeadersRow();
            foreach (var row in report.Offense)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                if (ImGui.Selectable($"{row.Type}##OffenseType", teamAnalysisAttackType == row.Type, ImGuiSelectableFlags.SpanAllColumns)) teamAnalysisAttackType = row.Type;
                ImGui.TableNextColumn(); ImGui.TextColored(row.SuperEffective > 0 ? PokemonGreen : CompanionMuted, row.SuperEffective.ToString());
                TeamAnalysisTooltip(row.Members.Where(value => value.Multiplier > 1).Select(value => $"{value.Member.Name}: {value.Move}"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Members.Count == 0 ? "No move" : row.Best.ToString("0.###", CultureInfo.InvariantCulture) + "x");
            }
            ImGui.EndTable();
        }
        var selected = report.Offense.FirstOrDefault(row => row.Type == teamAnalysisAttackType) ?? report.Offense[0];
        ImGui.Spacing(); ImGui.TextColored(CompanionAccent, $"Moves against {selected.Type}");
        foreach (var value in selected.Members.OrderByDescending(value => value.Multiplier))
            ImGui.TextWrapped($"{value.Member.Name} / {value.Move} / {value.Multiplier.ToString("0.###", CultureInfo.InvariantCulture)}x");
        if (selected.Members.Count == 0) ImGui.TextWrapped("No reliable damaging move data is available for this lineup.");
    }

    private static void TeamAnalysisTooltip(IEnumerable<string> values)
    {
        if (!ImGui.IsItemHovered()) return;
        var names = values.ToArray();
        if (names.Length > 0) ImGui.SetTooltip(string.Join("\n", names));
    }
}
