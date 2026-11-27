using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool FrontierBusy => trainer?.FrontierRun?.Status is "active" or "between";
    private List<FrontierRentalDto> frontierRentals = [];
    private Dictionary<string, FrontierPayoutDto> frontierPayouts = [];
    private sealed record FrontierPurchase(string Owner, string RewardId, string Name, int Quantity, string RequestId);
    private FrontierPurchase? pendingFrontierPurchase;
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
        ResetFrontierRewards();
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
        ApplyCheckpointBattle(await InSession(plugin.Api.StartFrontier(bracket, mode, rentalId, ids)));
        SetStatus("Frontier run saved. Choose your action.");
    }
    private async Task ContinueFrontier()
    {
        var run = trainer!.FrontierRun!;
        ApplyCheckpointBattle(await InSession(plugin.Api.NextFrontier(run.Id, run.Revision)));
        SetStatus("Next Frontier fight ready. Your team has been fully healed.");
    }
    private async Task EndFrontier()
    {
        var run = trainer!.FrontierRun!;
        trainer!.FrontierRun = await InSession(plugin.Api.AbandonFrontier(run.Id, run.Revision));
        trainer.ActiveTrainerBattle = battle = null;
        refreshState.Trainer();
        SetStatus("Streak ended. Earned BP and permanent progress retained.");
    }
    private Task BuyFrontierReward(string rewardId, int quantity = 1)
    {
        if (trainer is not { } current) return Task.CompletedTask;
        if (pendingFrontierPurchase is not null)
        {
            SetStatus("Retry the pending purchase in Battle Frontier Rewards first.", true);
            return Task.CompletedTask;
        }
        var name = current.Frontier.Rewards.FirstOrDefault(reward => reward.Id == rewardId)?.Name ?? rewardId;
        return SendFrontierPurchase(new(current.User.Id, rewardId, name, quantity, Guid.NewGuid().ToString()));
    }

    private async Task SendFrontierPurchase(FrontierPurchase attempt)
    {
        if (trainer?.User.Id != attempt.Owner) return;
        TrainerSnapshotDto result;
        try { result = await InSession(plugin.Api.RedeemFrontierReward(attempt.RequestId, attempt.RewardId, attempt.Quantity)); }
        catch (Exception error) when (error is UnconfirmedActionException or ApiException { StatusCode: >= 500 })
        {
            if (trainer?.User.Id == attempt.Owner) pendingFrontierPurchase = attempt;
            throw;
        }
        catch (ApiException error) when (error.StatusCode is 400 or 403 or 404 or 409)
        {
            if (trainer?.User.Id == attempt.Owner) pendingFrontierPurchase = null;
            throw;
        }
        if (trainer?.User.Id != attempt.Owner) return;
        pendingFrontierPurchase = null;
        RequestPokedexIfChanged(trainer.Pokedex, result.Pokedex);
        trainer = result;
        SetStatus("Frontier reward received. BP and purchase history saved.");
    }

    private void DrawPendingFrontierPurchase()
    {
        if (pendingFrontierPurchase is not { } attempt || trainer?.User.Id != attempt.Owner) return;
        ImGui.TextWrapped($"Purchase unconfirmed: {attempt.Quantity:N0} × {attempt.Name}.");
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Retry same purchase", CompanionAccent))
            Run(() => SendFrontierPurchase(attempt), "Checking BP purchase...");
        ImGui.EndDisabled();
    }
    private async Task StartParadoxHunt(string permitId)
    {
        var encounter = await InSession(plugin.Api.StartParadoxResearch(permitId));
        trainer!.ActiveEncounter = encounter;
        refreshState.Trainer();
        mainSection = 0;
        await InSession(ResumeWildBattle(encounter.Id));
        SetStatus(encounter.Pokemon.Shiny ? $"A shiny {encounter.Pokemon.Species} appeared!" : $"Research encounter: {encounter.Pokemon.Species}. Your permit remains permanent.");
    }
}
