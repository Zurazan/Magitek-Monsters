using System;
using System.Threading.Channels;
using MagitekMonsters.Contracts;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly Channel<(long Generation, LiveEvent Update)> liveUpdates = Channel.CreateBounded<(long, LiveEvent)>(new BoundedChannelOptions(32)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private void OnLiveState(long generation, LiveEvent update) => liveUpdates.Writer.TryWrite((generation, update));
    private void ApplyLiveUpdates()
    {
        if (liveUpdates is null || busy || backgroundRefreshing) return;
        while (liveUpdates.Reader.TryRead(out var pending))
        {
            if (trainer is null || pending.Generation != plugin.Connection.Generation) continue;
            var update = pending.Update;
            if (update.Raid is { } room && (raidRoom?.Id == room.Id || trainer.ActiveRaidId == room.Id))
            { ApplyRaid(room); nextRaidPollAt = DateTimeOffset.UtcNow.AddSeconds(10); }
            if (update.Battle is not { } next) continue;
            if (update.Name == "pvp.battle.updated")
            {
                if (pvpStatus?.Battle?.Id != next.Id) { serverPvpRefreshRequested = true; continue; }
                if (LiveStateOrder.IsOlder(next, pvpStatus.Battle)) continue;
                pvpStatus.Battle = next; ObserveRecentBattle(next, true);
                nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(10);
                if (next.Status != "active") { serverPvpRefreshRequested = true; refreshState.Trainer(); }
            }
            else if (battle?.Id == next.Id && !LiveStateOrder.IsOlder(next, battle))
            { ApplyBattleResponse(next); nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(10); }
        }
    }
}
