using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private const int IdlePvpPollSeconds = 15;
    private readonly RefreshState refreshState = new();
    private readonly ConcurrentQueue<Action> backgroundResults = new();
    private CancellationTokenSource? backgroundCancellation;
    private bool backgroundRefreshing;
    private bool backgroundDefersLiveUpdates;
    private DateTimeOffset backgroundRetryAfter = DateTimeOffset.MinValue;
    private long connectionSyncGeneration;
    private bool pvpRecoveryPending;

    private void QueueConnectionSync()
    {
        var generation = plugin.Connection.Generation;
        backgroundResults.Enqueue(() =>
        {
            if (plugin.Connection.Generation != generation || plugin.Connection.State != ServerConnectionState.Syncing) return;
            connectionSyncGeneration = generation;
            pvpRecoveryPending = true;
            if (plugin.Api.Catalogs.Revision is null) catalogRefreshRequested = true;
            CancelBackgroundRefresh();
            backgroundRetryAfter = DateTimeOffset.MinValue;
        });
    }

    private void BackgroundConnectionSync()
    {
        var generation = connectionSyncGeneration;
        var previous = trainer!;
        var coveredBattle = battle;
        var activeBattleId = battle?.Status == "active" ? battle.Id : null;
        var needsPokedex = pokedexCatalog.Count == 0;
        RunBackground(async token =>
        {
            var recovered = await ConnectionRecovery.Fetch(plugin.Api, previous, activeBattleId, needsPokedex, token);
            return () => ApplyConnectionSync(generation, recovered, coveredBattle);
        }, () => { });
    }

    private void ApplyConnectionSync(long generation, ConnectionRecovery recovered, BattleSnapshotDto? coveredBattle)
    {
        if (trainer?.User.Id != recovered.Trainer.User.Id || plugin.Connection.Generation != generation
            || plugin.Connection.State != ServerConnectionState.Syncing) return;
        trainer = recovered.Trainer;
        if (recovered.Battle is { } current) ApplyBattleResponse(current);
        else if (recovered.BattleChecked) battle = null;
        raidBoard = null;
        if (recovered.Raid is { } room) ApplyRaid(room);
        else ClearRaidView();
        if (recovered.RaidMissing) refreshState.Trainer();
        if (recovered.RefreshPokedex)
        {
            pokedexRefreshRequested = true;
            nextPokedexRefreshAt = DateTimeOffset.MinValue;
        }
        AcknowledgeBattleResultTrainer(coveredBattle);
        nextRaidPollAt = DateTimeOffset.UtcNow.AddSeconds(2);
        nextPvpPollAt = DateTimeOffset.MinValue;
        if (plugin.Connection.CompleteSync(generation))
        {
            connectionSyncGeneration = 0;
            shopRefreshRequested = true;
        }
    }

    private void CancelBackgroundRefresh()
    {
        refreshState.Invalidate();
        try { backgroundCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void RunBackground(Func<CancellationToken, Task<Action>> fetch, Action retry, bool sharedBackoff = true, bool deferLiveUpdates = true)
    {
        if (busy || backgroundRefreshing || trainer is null) return;
        backgroundRefreshing = true;
        backgroundDefersLiveUpdates = deferLiveUpdates;
        var generation = refreshState.Generation;
        var session = sessionResponses.Generation;
        var account = trainer.User.Id;
        var owner = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        backgroundCancellation = owner;
        _ = Fetch();
        async Task Fetch()
        {
            Action? apply = null;
            Exception? failure = null;
            try { apply = await fetch(owner.Token); }
            catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
            catch (Exception exception) { failure = exception; }
            var cancelled = owner.IsCancellationRequested;
            owner.Dispose();
            backgroundResults.Enqueue(() =>
            {
                if (ReferenceEquals(backgroundCancellation, owner))
                { backgroundCancellation = null; backgroundRefreshing = false; backgroundDefersLiveUpdates = false; }
                if (!disposeCancellation.IsCancellationRequested && sessionResponses.IsCurrent(session) && trainer?.User.Id == account)
                {
                    if (!refreshState.IsCurrent(generation) || cancelled) retry();
                    else if (failure is ApiException { StatusCode: 426 } update)
                        plugin.Api.Update.Require(update.Message);
                    else if (failure is ApiException { StatusCode: 401 })
                        Disconnect(false);
                    else if (failure is not null)
                    {
                        Plugin.Log.Warning(failure, "Magitek Monsters background refresh failed");
                        retry();
                        if (sharedBackoff)
                        {
                            backgroundRetryAfter = DateTimeOffset.UtcNow.AddSeconds(2);
                            nextBattlePollAt = nextPvpPollAt = backgroundRetryAfter;
                        }
                    }
                    else apply?.Invoke();
                }
            });
        }
    }

    private void BackgroundTrainer()
    {
        var coveredBattle = battle;
        RunBackground(async token =>
        {
            var next = await plugin.Api.GetMe(token);
            return () =>
            {
                RequestPokedexIfChanged(trainer?.Pokedex, next.Pokedex);
                trainer = next;
                AcknowledgeBattleResultTrainer(coveredBattle);
                if (coveredBattle is not null) refreshState.Acknowledge(coveredBattle.Id, coveredBattle.Revision, true);
            };
        }, () => refreshState.Trainer());
    }

    private void BackgroundBattle()
    {
        var id = battle?.Id;
        var revision = battle?.Revision;
        if (id is null) return;
        RunBackground(async token =>
        {
            var next = await plugin.Api.GetBattle(id, token);
            return () =>
            {
                if (battle?.Id != id || LiveStateOrder.IsOlder(next, battle)) return;
                ApplyBattleResponse(next);
                nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(plugin.Connection.CanInvoke ? 10 : 1);
            };
        }, () => { if (battle?.Id == id && battle.Revision == revision) refreshState.Battle(id, null); });
    }

    private bool ShouldRefreshPvp(DateTimeOffset now)
    {
        var lobbyVisible = IsOpen && mainSection == 4;
        if (pvpRecoveryPending) return now >= nextPvpPollAt;
        if (pvpStatus is not { } status)
            return now >= nextPvpPollAt && (lobbyVisible || serverPvpRefreshRequested);
        return serverPvpRefreshRequested || now >= nextPvpPollAt
            && (status.State == "queued" || status.Challenges.Count > 0
                || status.State == "idle" && lobbyVisible
                || status.Battle is { Status: "active" } match
                    && (plugin.Connection.CanInvoke || match.Request is null || match.Request.Wait == true));
    }

    private void BackgroundPvp()
    {
        var recovering = pvpRecoveryPending;
        var loading = pvpStatus is null && !recovering;
        var requested = serverPvpRefreshRequested;
        serverPvpRefreshRequested = false;
        RunBackground(async token =>
        {
            var next = await plugin.Api.GetPvpStatus(token);
            return () =>
            {
                if (next.Battle is { } match && LiveStateOrder.IsOlder(match, pvpStatus?.Battle)) next.Battle = pvpStatus!.Battle;
                pvpStatus = next;
                pvpRecoveryPending = false;
                if (trainer is not null) trainer.PvpProfile = next.Profile;
                nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(next.State == "idle" && next.Challenges.Count == 0 ? IdlePvpPollSeconds
                    : plugin.Connection.CanInvoke ? 10 : next.State == "queued" ? 2 : 1);
            };
        }, () =>
        {
            if (loading && pvpStatus is not null) return;
            if (recovering || loading) nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(2);
            if (!recovering && (!loading || requested)) serverPvpRefreshRequested = true;
        }, sharedBackoff: !recovering && !loading, deferLiveUpdates: !loading);
    }

    private void ApplyBattleResponse(BattleSnapshotDto next, string? choice = null)
    {
        if (LiveStateOrder.IsOlder(next, battle)) return;
        if (next.Id != battle?.Id) eventBattleId = string.Empty;
        if (next.Kind == "wild" && trainer?.ActiveEncounter is { } encounter && encounter.Id == next.Id && IsEventEncounter(encounter)) eventBattleId = next.Id;
        battle = next;
        refreshState.Acknowledge(next.Id, next.Revision, choice is not null);
        if (trainer is null) return;
        if (next.Kind == "trainer") trainer.ActiveTrainerBattle = next.Status == "active" ? next : null;
        if (next.FrontierId is null && next.LeagueId is null)
            foreach (var vital in next.PartyVitals)
                if (trainer.Party.FirstOrDefault(pokemon => pokemon.Id == vital.PokemonId) is { } pokemon) pokemon.Vitals = vital;
        var parts = choice?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length >= 2 && parts[0] is "item" or "ball") ConsumeLocalItem(parts[1]);
        if (next.Status != "active") refreshState.Trainer();
    }

    private void ConsumeLocalItem(string itemId)
    {
        if (trainer is not { } current) return;
        var index = current.Inventory.FindIndex(item => item.ItemId == itemId);
        if (index < 0) return;
        var item = current.Inventory[index];
        current.Inventory = [.. current.Inventory];
        current.Inventory[index] = new() { ItemId = item.ItemId, Quantity = Math.Max(0, item.Quantity - 1) };
    }
}
