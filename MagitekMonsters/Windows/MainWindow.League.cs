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
    private string leagueTargetId = string.Empty;
    private string leagueItemId = "potion";
    private string leagueMoveId = string.Empty;
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
        ImGui.BeginChild("LeagueCheckpointBody", new Vector2(0, Math.Max(180 * scale, ImGui.GetContentRegionAvail().Y - 58 * scale)), false);
        var next = opponents.FirstOrDefault(entry => entry.Id == run.OpponentIds.ElementAtOrDefault(run.Stage));
        ImGui.TextColored(CompanionAccent, $"NEXT: {next?.Name ?? "League opponent"}");
        if (next is not null)
        {
            ImGui.TextWrapped(next.TacticalHint);
            DrawPveTeamStrip(next.TeamSpecies.ToArray(), ImGui.GetContentRegionAvail().X);
        }
        ImGui.TextWrapped("No free healing. Medicine uses real inventory and the preceding fight's remaining actions. Leaving the window or disconnecting preserves this checkpoint.");
        if (!run.Vitals.Any(entry => entry.PokemonId == leagueTargetId)) leagueTargetId = run.Vitals.FirstOrDefault()?.PokemonId ?? string.Empty;
        ImGui.Spacing(); ImGui.TextColored(CompanionMuted, "CARRIED TEAM / Click a Pokémon to target medicine");
        DrawLeagueHealthCards(run);
        var target = run.Vitals.FirstOrDefault(entry => entry.PokemonId == leagueTargetId);
        if (target is not null && ImGui.CollapsingHeader($"All move PP / {target.Name}###LeagueTargetPP"))
            foreach (var move in target.Moves) ImGui.TextColored(move.Pp == 0 ? PokemonOrange : CompanionMuted, $"{move.Name} / {move.Pp}/{move.MaxPp} PP");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(PokemonOrange, $"CHECKPOINT MEDICINE / {Math.Max(0, 3 - run.CheckpointActions)} of 3 actions remaining");
        ImGui.SetNextItemWidth(Math.Min(260 * scale, ImGui.GetContentRegionAvail().X * 0.6f));
        if (ImGui.BeginCombo("Medicine target", target?.Name ?? "Choose Pokémon"))
        {
            foreach (var pokemon in run.Vitals) if (ImGui.Selectable(pokemon.Name, pokemon.PokemonId == leagueTargetId)) { leagueTargetId = pokemon.PokemonId; leagueMoveId = string.Empty; }
            ImGui.EndCombo();
        }
        target = run.Vitals.FirstOrDefault(entry => entry.PokemonId == leagueTargetId);
        var medicine = shop.Where(entry => entry.BattleEffect is not null || entry.Id is "ether" or "maxether" or "elixir" or "maxelixir").Where(entry => InventoryCount(entry.Id) > 0).ToList();
        if (!medicine.Any(entry => entry.Id == leagueItemId)) leagueItemId = medicine.FirstOrDefault()?.Id ?? string.Empty;
        ImGui.SetNextItemWidth(Math.Min(260 * scale, ImGui.GetContentRegionAvail().X * 0.6f));
        if (ImGui.BeginCombo("Item", medicine.FirstOrDefault(entry => entry.Id == leagueItemId)?.Name ?? "No medicine owned"))
        {
            foreach (var item in medicine) if (ImGui.Selectable($"{item.Name} x{InventoryCount(item.Id)}", item.Id == leagueItemId)) leagueItemId = item.Id;
            ImGui.EndCombo();
        }
        var singleMove = leagueItemId is "ether" or "maxether";
        if (singleMove && target is not null)
        {
            if (!target.Moves.Any(move => move.Id == leagueMoveId)) leagueMoveId = target.Moves.FirstOrDefault()?.Id ?? string.Empty;
            ImGui.SetNextItemWidth(Math.Min(260 * scale, ImGui.GetContentRegionAvail().X * 0.6f));
            if (ImGui.BeginCombo("Restore PP", target.Moves.FirstOrDefault(move => move.Id == leagueMoveId)?.Name ?? "Choose move"))
            {
                foreach (var move in target.Moves) if (ImGui.Selectable($"{move.Name} {move.Pp}/{move.MaxPp}", move.Id == leagueMoveId)) leagueMoveId = move.Id;
                ImGui.EndCombo();
            }
        }
        ImGui.BeginDisabled(busy || run.CheckpointActions >= 3 || target is null || medicine.Count == 0 || (singleMove && leagueMoveId.Length == 0));
        if (DrawAccentButton("Use medicine", new Vector2(170, 32) * scale, PokemonGreen))
            Run(() => HealLeague(singleMove), "Using checkpoint medicine...");
        ImGui.EndDisabled();
        ImGui.EndChild(); ImGui.Spacing();
        ImGui.BeginDisabled(busy || !run.Vitals.Any(entry => entry.Hp > 0));
        if (DrawAccentButton("Continue next fight", new Vector2(220, 36) * scale, CompanionAccent)) Run(NextLeague, "Continuing the League run...");
        ImGui.EndDisabled();
        if (ImGui.GetContentRegionAvail().X >= ImGui.CalcTextSize("Abandon run").X + 240 * scale) ImGui.SameLine();
        if (DrawAccentSmallButton("Abandon run", PokemonOrange)) ImGui.OpenPopup("Abandon League run?");
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

    private void DrawLeagueHealthCards(LeagueRunDto run)
    {
        var scale = CompanionScale;
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (240 * scale)), 1, 3);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4) * scale);
        if (ImGui.BeginTable("LeagueCarriedTeam", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var pokemon in run.Vitals)
            {
                ImGui.TableNextColumn();
                if (DrawPveCardSurface($"LeagueHealth##{pokemon.PokemonId}", 45 * scale + 4 * ImGui.GetTextLineHeight(),
                    pokemon.PokemonId == leagueTargetId, out var min, out var max, out var after))
                { leagueTargetId = pokemon.PokemonId; leagueMoveId = string.Empty; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", pokemon.Moves.Select(move => $"{move.Name} / {move.Pp}/{move.MaxPp} PP")));
                var width = Math.Max(1, max.X - min.X - 72 * scale);
                ImGui.SetCursorScreenPos(min + new Vector2(10, 10) * scale);
                DrawPokemonIcon(pokemon.Species, pokemon.Shiny, new Vector2(44 * scale));
                ImGui.SetCursorScreenPos(min + new Vector2(62, 10) * scale);
                ImGui.BeginGroup();
                DrawClippedText(pokemon.Name, width, CompanionAccent);
                DrawClippedText(SpeciesDisplayCatalog.Name(pokemon.Species), width, CompanionMuted);
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(min + new Vector2(10, 58) * scale);
                ImGui.BeginGroup();
                var fullWidth = Math.Max(1, max.X - min.X - 20 * scale);
                DrawClippedText($"HP {pokemon.Hp}/{pokemon.MaxHp}{HealthStatus(pokemon)}", fullWidth, pokemon.Hp == 0 ? PokemonOrange : CompanionMuted);
                DrawProgressBar(pokemon.Hp / (float)Math.Max(1, pokemon.MaxHp), new Vector2(fullWidth, 6 * scale), "", HpBarRed);
                var lowest = pokemon.Moves.OrderBy(move => move.Pp / (float)Math.Max(1, move.MaxPp)).FirstOrDefault();
                DrawClippedText(lowest is null ? "No move PP available" : $"Lowest PP: {lowest.Name} {lowest.Pp}/{lowest.MaxPp}", fullWidth, CompanionMuted);
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(after);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
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
