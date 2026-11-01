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
    private readonly RefreshState refreshState = new();
    private readonly ConcurrentQueue<Action> backgroundResults = new();
    private CancellationTokenSource? backgroundCancellation;
    private bool backgroundRefreshing;
    private DateTimeOffset backgroundRetryAfter = DateTimeOffset.MinValue;
    private long connectionSyncGeneration;

    private void QueueConnectionSync()
    {
        var generation = plugin.Connection.Generation;
        backgroundResults.Enqueue(() =>
        {
            if (plugin.Connection.Generation != generation || plugin.Connection.State != ServerConnectionState.Syncing) return;
            connectionSyncGeneration = generation;
            CancelBackgroundRefresh();
            backgroundRetryAfter = DateTimeOffset.MinValue;
        });
    }

    private void BackgroundConnectionSync()
    {
        var generation = connectionSyncGeneration;
        var previous = trainer!;
        var activeBattleId = battle?.Status == "active" ? battle.Id : null;
        var needsPokedex = pokedexCatalog.Count == 0;
        RunBackground(async token =>
        {
            var recovered = await ConnectionRecovery.Fetch(plugin.Api, previous, activeBattleId, needsPokedex, token);
            return () => ApplyConnectionSync(generation, recovered);
        }, () => { });
    }

    private void ApplyConnectionSync(long generation, ConnectionRecovery recovered)
    {
        if (trainer?.User.Id != recovered.Trainer.User.Id || plugin.Connection.Generation != generation
            || plugin.Connection.State != ServerConnectionState.Syncing) return;
        trainer = recovered.Trainer;
        pvpStatus = recovered.Pvp;
        ObserveRecentBattle(pvpStatus.Battle, true);
        trainer.PvpProfile = recovered.Pvp.Profile;
        if (recovered.Battle is { } current) ApplyBattleResponse(current);
        else if (recovered.BattleChecked) battle = null;
        raidBoard = null;
        if (recovered.Raid is { } room) ApplyRaid(room);
        else
        {
            raidRoom = null;
            pendingRaidChoice = null; raidTargetMove = null; raidModifier = "";
            raidCaptureRequest = null; raidCaptureBall = null;
        }
        if (recovered.Pokedex is { } dex) pokedexCatalog = dex.Pokemon;
        AcknowledgeBattleResultTrainer();
        nextPvpPollAt = nextRaidPollAt = DateTimeOffset.UtcNow.AddSeconds(2);
        if (plugin.Connection.CompleteSync(generation)) connectionSyncGeneration = 0;
    }

    private void CancelBackgroundRefresh()
    {
        refreshState.Invalidate();
        try { backgroundCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void RunBackground(Func<CancellationToken, Task<Action>> fetch, Action retry)
    {
        if (busy || backgroundRefreshing || trainer is null) return;
        backgroundRefreshing = true;
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
                { backgroundCancellation = null; backgroundRefreshing = false; }
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
                        backgroundRetryAfter = DateTimeOffset.UtcNow.AddSeconds(2);
                        nextBattlePollAt = nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(2);
                    }
                    else apply?.Invoke();
                }
            });
        }
    }

    private void BackgroundTrainer()
    {
        RunBackground(async token =>
        {
            var next = await plugin.Api.GetMe(token);
            var dex = next.Pokedex.Caught != trainer?.Pokedex.Caught ? await plugin.Api.GetPokedexCatalog(token) : null;
            return () =>
            {
                trainer = next;
                AcknowledgeBattleResultTrainer();
                if (dex is not null) pokedexCatalog = dex.Pokemon;
                if (battle is { } current) refreshState.Acknowledge(current.Id, current.Revision, true);
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

    private void BackgroundPvp()
    {
        RunBackground(async token =>
        {
            var next = await plugin.Api.GetPvpStatus(token);
            return () =>
            {
                if (next.Battle is { } match && LiveStateOrder.IsOlder(match, pvpStatus?.Battle)) next.Battle = pvpStatus!.Battle;
                pvpStatus = next;
                ObserveRecentBattle(next.Battle, true);
                if (trainer is not null) trainer.PvpProfile = next.Profile;
                nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(plugin.Connection.CanInvoke ? 10 : next.State == "queued" ? 2 : 1);
            };
        }, () => serverPvpRefreshRequested = true);
    }

    private void ApplyBattleResponse(BattleSnapshotDto next, string? choice = null)
    {
        if (LiveStateOrder.IsOlder(next, battle)) return;
        ObserveRecentBattle(next, action: choice is not null);
        battle = next;
        refreshState.Acknowledge(next.Id, next.Revision, choice is not null);
        if (trainer is null) return;
        if (next.Kind == "trainer") trainer.ActiveTrainerBattle = next.Status == "active" ? next : null;
        if (next.FrontierId is null && next.LeagueId is null)
            foreach (var vital in next.PartyVitals)
                if (trainer.Party.FirstOrDefault(pokemon => pokemon.Id == vital.PokemonId) is { } pokemon) pokemon.Vitals = vital;
        var parts = choice?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length >= 2 && parts[0] is "item" or "ball")
            if (trainer.Inventory.FirstOrDefault(item => item.ItemId == parts[1]) is { } item) item.Quantity = Math.Max(0, item.Quantity - 1);
        if (next.Status != "active") refreshState.Trainer();
    }
}
