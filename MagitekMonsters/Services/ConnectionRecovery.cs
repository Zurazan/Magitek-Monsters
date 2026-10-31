using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public interface IConnectionRecoveryApi
{
    Task<TrainerSnapshotDto> GetMe(CancellationToken cancellationToken);
    Task<PvpStatusDto> GetPvpStatus(CancellationToken cancellationToken);
    Task<BattleSnapshotDto> GetBattle(string id, CancellationToken cancellationToken);
    Task<RaidRoomDto> GetRaid(string id, CancellationToken cancellationToken);
    Task<PokedexCatalogDto> GetPokedexCatalog(CancellationToken cancellationToken);
}

public sealed record ConnectionRecovery(TrainerSnapshotDto Trainer, PvpStatusDto Pvp, BattleSnapshotDto? Battle,
    RaidRoomDto? Raid, PokedexCatalogDto? Pokedex, bool BattleChecked)
{
    public static async Task<ConnectionRecovery> Fetch(IConnectionRecoveryApi api, TrainerSnapshotDto previous,
        string? activeBattleId, bool needsPokedex, CancellationToken cancellationToken)
    {
        var account = previous.User.Id;
        var next = await api.GetMe(cancellationToken).ConfigureAwait(false);
        if (next.User.Id != account) throw new InvalidOperationException("The trainer changed during connection recovery.");
        var battleId = next.ActiveTrainerBattle?.Id ?? activeBattleId;
        var pvp = api.GetPvpStatus(cancellationToken);
        var battle = battleId is null ? Task.FromResult<BattleSnapshotDto?>(null) : ReadBattle(battleId);
        var raid = next.ActiveRaidId is { } raidId ? ReadRaid(raidId) : Task.FromResult<RaidRoomDto?>(null);
        var dexChanged = needsPokedex || next.Pokedex.Caught != previous.Pokedex.Caught
            || next.Pokedex.ShinyCaught != previous.Pokedex.ShinyCaught
            || !next.Pokedex.CaughtForms.SequenceEqual(previous.Pokedex.CaughtForms)
            || !next.Pokedex.CaughtSpecies.SequenceEqual(previous.Pokedex.CaughtSpecies);
        var dex = dexChanged ? ReadPokedex() : Task.FromResult<PokedexCatalogDto?>(null);
        await Task.WhenAll(pvp, battle, raid, dex).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (battleId is not null && battle.Result is null) next.ActiveTrainerBattle = null;
        if (next.ActiveRaidId is not null && raid.Result is null) { next.ActiveRaidId = null; next.RaidLocked = false; }
        return new(next, pvp.Result, battle.Result, raid.Result, dex.Result, battleId is not null);

        async Task<BattleSnapshotDto?> ReadBattle(string id)
        {
            try { return await api.GetBattle(id, cancellationToken).ConfigureAwait(false); }
            catch (ApiException exception) when (exception.StatusCode == 404) { return null; }
        }
        async Task<RaidRoomDto?> ReadRaid(string id)
        {
            try { return await api.GetRaid(id, cancellationToken).ConfigureAwait(false); }
            catch (ApiException exception) when (exception.StatusCode == 404) { return null; }
        }
        async Task<PokedexCatalogDto?> ReadPokedex() => await api.GetPokedexCatalog(cancellationToken).ConfigureAwait(false);
    }
}
