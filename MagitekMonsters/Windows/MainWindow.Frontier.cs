using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool FrontierBusy => trainer?.FrontierRun?.Status is "active" or "between";
    private List<FrontierRentalDto> frontierRentals = [];
    private readonly ConcurrentDictionary<string, string> frontierRequests = new();
    private bool openFrontierRuns;
    private bool openBpExchange;
    private string? shownFrontierRunId;

    private void OpenFrontier(bool exchange = false)
    {
        openBpExchange = exchange;
        openFrontierRuns = !exchange;
        mainSection = 8;
    }

    private void ResetFrontierDesk()
    {
        openFrontierRuns = true;
        openBpExchange = false;
        shownFrontierRunId = null;
        frontierFactory = false;
        frontierTeamModeChosen = false;
        frontierBracket = frontierMode = frontierRentalIndex = 0;
        frontierTeamIds.Clear();
        factoryViewKey = string.Empty;
        factoryPicks.Clear();
        factorySwapOut = factorySwapIn = string.Empty;
        frontierRewardGroup = FrontierRewardGroup.Exchange;
        ResetFrontierRewardList();
        frontierRewardSearch = paradoxSearch = string.Empty;
        paradoxEra = 0;
    }

    private void DrawFrontier()
    {
        if (trainer is not { } current) return;
        if (battle?.FrontierId is not null) { DrawBattleConsole(battle); return; }
        var saved = FrontierBusy ? current.FrontierRun : null;
        if (shownFrontierRunId != saved?.Id)
        {
            shownFrontierRunId = saved?.Id;
            if (saved is not null)
            {
                frontierFactory = saved.Facility == "factory";
                if (!openBpExchange) openFrontierRuns = true;
            }
        }

        DrawFrontierHeading("Battle Frontier", 1.35f, $"{current.Frontier.Balance:N0} BP · {current.Frontier.LifetimeWins:N0} lifetime wins",
            WalletAccent(WalletCurrency.BattlePoints));
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 5) * CompanionScale);
        if (!ImGui.BeginTabBar("FrontierDeskTabs", ImGuiTabBarFlags.FittingPolicyResizeDown))
        {
            ImGui.PopStyleVar();
            return;
        }
        var runsLabel = saved is not null ? "Runs / Saved###FrontierRuns" : "Runs###FrontierRuns";
        if (ImGui.BeginTabItem(runsLabel, openFrontierRuns && !openBpExchange ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            if (openFrontierRuns && !openBpExchange) { openFrontierRuns = false; ImGui.SetScrollY(0); }
            DrawFrontierRuns(current, saved);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Rewards", openBpExchange ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            if (openBpExchange)
            {
                openBpExchange = false;
                frontierRewardGroup = FrontierRewardGroup.Exchange;
                ResetFrontierRewardList();
                ImGui.SetScrollY(0);
            }
            DrawFrontierRewards(current);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Achievements"))
        {
            DrawFrontierAchievements();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
        ImGui.PopStyleVar();
    }

    private void DrawFrontierAbandonPopup()
    {
        if (!BeginConfirmationModal("End Frontier run?")) return;
        ImGui.TextWrapped("End this streak? Earned BP, achievements, personal bests and Ultra Beast unlocks stay saved. The next run starts at zero.");
        if (DrawConfirmationActions("End run", PokemonOrange, busy, "Keep playing"))
        {
            ImGui.CloseCurrentPopup();
            Run(EndFrontier, "Ending the Frontier run...");
        }
        EndConfirmationModal();
    }

    private async Task EnterFrontier(string bracket, string mode, string rentalId, string[] ids)
    {
        var refresh = true;
        try { battle = await InSession(plugin.Api.StartFrontier(bracket, mode, rentalId, ids)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Frontier run saved. Choose your action.");
    }
    private async Task ContinueFrontier()
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.NextFrontier(run.Id, run.Revision)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Next Frontier fight ready. Your team has been fully healed.");
    }
    private async Task EndFrontier()
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { await InSession(plugin.Api.AbandonFrontier(run.Id, run.Revision)); battle = null; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Streak ended. Earned BP and permanent progress retained.");
    }
    private async Task BuyFrontierReward(string rewardId)
    {
        var owner = trainer!.User.Id; var key = $"{owner}:{rewardId}";
        var requestId = frontierRequests.GetOrAdd(key, _ => Guid.NewGuid().ToString());
        var result = await InSession(plugin.Api.RedeemFrontierReward(requestId, rewardId, 1));
        frontierRequests.TryRemove(key, out _);
        if (trainer?.User.Id != owner) return;
        trainer = result;
        await InSession(LoadPokedexCatalog()); SetStatus("Frontier reward received. BP and purchase history saved.");
    }
    private async Task StartParadoxHunt(string permitId)
    {
        EncounterDto encounter;
        var refresh = true;
        try { encounter = await InSession(plugin.Api.StartParadoxResearch(permitId)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0;
        SetStatus(encounter.Pokemon.Shiny ? $"A shiny {encounter.Pokemon.Species} appeared!" : $"Research encounter: {encounter.Pokemon.Species}. Your permit remains permanent.");
    }
}
