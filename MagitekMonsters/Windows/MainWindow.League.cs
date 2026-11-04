using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;
using MagitekMonsters.Contracts;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool LeagueBusy => trainer?.LeagueRun?.Status is "active" or "between";
    private bool leagueRematch;
    private string? leagueEntryCircuitId;

    private void RestoreTrainerBattle()
    {
        var saved = trainer?.ActiveTrainerBattle;
        if (saved?.Status != "active") return;
        if (battle is null || battle.Id != saved.Id || saved.Revision > battle.Revision) battle = saved;
    }

    private void DrawLeagueEntry()
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        ImGui.Spacing();
        var circuit = SelectedRegionalCircuit;
        var region = regionalLeagues.FirstOrDefault(region => region.Id == circuit?.RegionId);
        var clears = RegionalLeaguePresentation.Clears(trainer);
        var champion = RegionalLeagueProgress.HasChampion(clears, region?.Id);
        if (circuit is null)
        {
            ImGui.BeginDisabled(busy || LeagueBusy);
            if (DrawCompanionSegment("Original League", !leagueRematch, 145 * scale)) leagueRematch = false;
            ImGui.EndDisabled();
            if (ImGui.GetContentRegionAvail().X >= 315 * scale + ImGui.GetStyle().ItemSpacing.X) ImGui.SameLine();
            ImGui.BeginDisabled(busy || !champion || LeagueBusy);
            if (DrawCompanionSegment("Level-100 rematch", leagueRematch, 170 * scale)) leagueRematch = true;
            ImGui.EndDisabled();
        }
        if (!champion) { leagueRematch = false; ImGui.TextDisabled("Rematch League unlocks after defeating the Champion."); }
        var stages = circuit is null ? PvePresentation.LeagueStages(opponents, leagueRematch)
            : RegionalLeaguePresentation.Stages(opponents, circuit, leagueRematch);
        ImGui.Spacing();
        if (region is not null) ImGui.TextColored(PokemonBlue, $"{region.Name} / {circuit!.Name}");
        ImGui.TextColored(CompanionAccent, $"{stages.Length} fights. One committed team.");
        ImGui.TextWrapped("Defeat the listed lineup to complete this League. HP, status and PP carry over. No free healing. Your team and builds stay locked until the run ends.");
        ImGui.TextWrapped("Each fight has three medicine actions, shared with its following checkpoint. Rewards are saved after each victory. Disconnecting pauses the run.");
        if (trainer.LeagueRun is { } last && !LeagueBusy)
            ImGui.TextWrapped($"Last run: {last.RegionName ?? "Previous League"} / {last.Status.ToUpperInvariant()} / {last.Stage}/{last.OpponentIds.Count} cleared");
        DrawLeagueStages(stages.Select(entry => entry.Id).ToArray(), -1);
        ImGui.Spacing();
        ImGui.BeginChild("LeagueEntryOpponents", new Vector2(0, Math.Max(150 * scale, ImGui.GetContentRegionAvail().Y - 112 * scale)), false);
        foreach (var stage in stages)
        {
            ImGui.PushID(stage.Id);
            ImGui.TextColored(CompanionAccent, $"{stage.TrainerClass} / {stage.Name}");
            ImGui.TextColored(CompanionMuted, $"Lv. {stage.RecommendedLevel} / Difficulty {stage.Difficulty}/5 / IVs {stage.DefaultIvs}/31");
            ImGui.TextWrapped(stage.TacticalHint);
            DrawPveTeamStrip(stage.TeamSpecies.ToArray(), ImGui.GetContentRegionAvail().X);
            ImGui.Separator(); ImGui.Spacing(); ImGui.PopID();
        }
        ImGui.EndChild();
        var first = stages.FirstOrDefault();
        var reason = region is not null && circuit is not null
            ? RegionalLeaguePresentation.LeagueLock(region, circuit, clears, leagueRematch, regionalLeagues, opponents)
            : first is null ? "League catalog is unavailable." : PvePresentation.LockReason(first, clears, opponents, false);
        var eligible = first is not null && reason is null;
        if (reason is not null) ImGui.TextWrapped(reason);
        ImGui.BeginDisabled(busy || !eligible || CollectionLocked || trainer.Party.Count == 0);
        if (DrawAccentButton("Prepare League run", new Vector2(210, 34) * scale, CompanionAccent))
        {
            pveTeamIds.Clear();
            foreach (var pokemon in trainer.Party) pveTeamIds.Add(pokemon.Id);
            leagueEntryCircuitId = circuit?.Id;
            ImGui.OpenPopup("League preparation");
        }
        ImGui.EndDisabled();
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(720 * scale, work.X * 0.9f), Math.Min(650 * scale, work.Y * 0.85f)), ImGuiCond.Appearing);
        if (BeginCenteredModal("League preparation", ImGuiWindowFlags.None))
        {
            ImGui.TextColored(PokemonYellow, leagueRematch ? "COMMIT YOUR TEAM / LEVEL-100 REMATCH" : "COMMIT YOUR TEAM");
            ImGui.BeginChild("LeaguePreparationBody", new Vector2(0, -58 * scale), false);
            ImGui.TextWrapped("Levels, moves, stats and held items are frozen for this run. Earned EXP still goes to your normal party, but does not refill HP/PP or change the run's builds. Buy supplies before entering.");
            ImGui.TextWrapped("Three medicine actions per fight and checkpoint combined. No free healing or refunds. Victory rewards are saved after every fight.");
            ImGui.Spacing();
            DrawPveTeamSelection("LeagueOwnedTeam");
            DrawPveDefeatNotice(trainer.Party.Where(pokemon => pveTeamIds.Contains(pokemon.Id)));
            ImGui.EndChild(); ImGui.Separator();
            ImGui.BeginDisabled(busy || !eligible || CollectionLocked || !trainer.Party.Any(entry => pveTeamIds.Contains(entry.Id)));
            if (DrawAccentButton("Enter the League", new Vector2(190, 34) * scale, PokemonRed))
            {
                var ids = trainer.Party.OrderBy(entry => entry.PartySlot).Where(entry => pveTeamIds.Contains(entry.Id)).Select(entry => entry.Id).ToArray();
                ImGui.CloseCurrentPopup(); Run(() => StartLeague(ids), "Entering the Pokémon League...");
            }
            ImGui.EndDisabled(); ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.Separator();
    }

    private bool DrawLeagueCheckpoint()
    {
        var run = trainer?.LeagueRun;
        if (run?.Status != "between" || battle?.Status == "active") return false;
        var scale = CompanionScale;
        DrawSectionHeader("League checkpoint", $"{run.RegionName ?? "Previous League"} / {(run.Rematch ? "Level-100 rematch" : run.CircuitName ?? "League run")} / {run.Stage}/{run.OpponentIds.Count} victories saved");
        DrawLeagueStages(run.OpponentIds.ToArray(), run.Stage);
        var next = opponents.FirstOrDefault(entry => entry.Id == run.OpponentIds.ElementAtOrDefault(run.Stage));
        var buttonHeight = Math.Max(32 * scale, ImGui.GetFrameHeight());
        var inline = ImGui.GetContentRegionAvail().X >= 380 * scale;
        var footerHeight = buttonHeight * (inline ? 1 : 2) + ImGui.GetTextLineHeightWithSpacing() * 2 + 18 * scale;
        ImGui.BeginChild("LeagueCheckpointBody", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - footerHeight)), false);
        DrawLeagueNextOpponent(next, run);
        ImGui.Spacing();
        DrawLeagueCheckpointBag(run);
        ImGui.EndChild(); ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.BeginDisabled(busy || !run.Vitals.Any(entry => entry.Hp > 0));
        if (DrawAccentButton($"Continue to {next?.Name ?? "next fight"}", new Vector2(inline ? 230 * scale : -1, buttonHeight), CompanionAccent)) Run(NextLeague, "Continuing the League run...");
        ImGui.EndDisabled();
        if (inline) ImGui.SameLine();
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Abandon run", PokemonOrange)) ImGui.OpenPopup("Abandon League run?");
        ImGui.EndDisabled();
        DrawClippedText("No automatic healing. Close and resume later.", ImGui.GetContentRegionAvail().X, CompanionMuted);
        if (BeginConfirmationModal("Abandon League run?"))
        {
            ImGui.TextWrapped("End this run? Cleared-fight rewards stay saved. Used supplies are not refunded. Your next run starts from Elite Four I.");
            if (DrawConfirmationActions("Abandon", PokemonRed, busy, "Keep playing")) { ImGui.CloseCurrentPopup(); Run(AbandonLeague, "Abandoning the League run..."); }
            EndConfirmationModal();
        }
        return true;
    }

    private void DrawLeagueStages(string[] ids, int stage)
    {
        var scale = CompanionScale;
        var columns = ImGui.GetContentRegionAvail().X >= 560 * scale ? Math.Max(1, ids.Length) : 1;
        if (!ImGui.BeginTable("LeagueStageTracker", columns, ImGuiTableFlags.SizingStretchSame)) return;
        for (var index = 0; index < ids.Length; index++)
        {
            ImGui.TableNextColumn();
            var opponent = opponents.FirstOrDefault(entry => entry.Id == ids[index]);
            var cleared = stage >= 0 && index < stage;
            var next = index == stage;
            var color = cleared ? PokemonGreen : next ? CompanionAccent : CompanionMuted;
            var position = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            DrawClippedText($"{index + 1}. {opponent?.Name ?? "League opponent"}", width, color);
            if (ImGui.IsItemHovered() && opponent is not null) ImGui.SetTooltip($"{opponent.Name}\n{(cleared ? "Victory saved" : next ? "Next fight" : "Upcoming fight")}");
            ImGui.GetWindowDrawList().AddRectFilled(position + new Vector2(0, ImGui.GetTextLineHeight() + 4 * scale),
                position + new Vector2(width, ImGui.GetTextLineHeight() + 7 * scale), ImGui.GetColorU32(color with { W = cleared || next ? 0.8f : 0.3f }));
            ImGui.Dummy(new Vector2(0, 9 * scale));
        }
        ImGui.EndTable();
    }

    private void DrawTrainerForfeit()
    {
        if (DrawAccentSmallButton(battle?.FrontierId is not null ? "Forfeit Frontier run" : battle?.LeagueId is null ? "Forfeit battle" : "Forfeit League run", PokemonOrange)) ImGui.OpenPopup("Forfeit trainer battle?");
        if (!BeginConfirmationModal("Forfeit trainer battle?")) return;
        ImGui.TextWrapped("This counts as a defeat. Used supplies are not refunded. League and Frontier forfeits end the entire run. Previously earned rewards and permanent unlocks stay saved.");
        if (battle is { DefeatPenaltyCap: > 0 }) DrawPveDefeatNotice(battle.DefeatPenaltyCap);
        if (DrawConfirmationActions("Forfeit", PokemonRed, busy)) { ImGui.CloseCurrentPopup(); Run(() => ChooseBattle("forfeit"), "Forfeiting the battle..."); }
        EndConfirmationModal();
    }
    private async Task StartLeague(string[] ids)
    {
        var refresh = true;
        try { battle = await InSession(plugin.Api.StartLeague(ids, leagueRematch, circuitId: leagueEntryCircuitId)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("League run saved. Choose your action.");
    }
    private async Task NextLeague()
    {
        var run = trainer!.LeagueRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.NextLeague(run.Id, run.Revision)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Next League fight started.");
    }
    private async Task HealLeague(bool singleMove)
    {
        var run = trainer!.LeagueRun!;
        var refresh = true;
        try { await InSession(plugin.Api.HealLeague(run.Id, run.Revision, leagueItemId, leagueTargetId, singleMove ? leagueMoveId : null)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Medicine used. Checkpoint saved.");
    }
    private async Task AbandonLeague()
    {
        var run = trainer!.LeagueRun!;
        var refresh = true;
        try { await InSession(plugin.Api.AbandonLeague(run.Id, run.Revision)); battle = null; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("League run ended. Your team is unlocked.");
    }
}
