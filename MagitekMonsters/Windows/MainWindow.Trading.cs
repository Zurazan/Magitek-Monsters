using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private TradingSnapshotDto? trading;
    private bool tradeRefreshRequested;
    private DateTimeOffset nextTradePollAt;
    private bool tradeHistoryOpen, requestTradeHistory;
    private string tradeTrainerCode = "";
    private string tradePokemonId = "";
    private string wonderPokemonId = "";
    private int tradeHistoryPage;
    private StoragePicker? tradePicker;
    private bool requestTradeConfirmation;
    private string tradeConfirmationId = "";
    private int tradeConfirmationRevision;
    private bool requestWonderConfirmation;
    private List<LegendaryBossDto>? tradingBossSource;
    private HashSet<string> tradingRestricted = [];
    private TrainerSnapshotDto? tradeSelectionSource;
    private string? cachedTradeId, cachedWonderId;
    private PokemonDto? cachedTradePokemon, cachedWonderPokemon;

    private void ResetTrading()
    {
        trading = null; tradeRefreshRequested = false; nextTradePollAt = DateTimeOffset.MinValue;
        tradeTrainerCode = tradePokemonId = wonderPokemonId = tradeConfirmationId = "";
        tradeHistoryPage = 0;
        tradeHistoryOpen = requestTradeHistory = false;
        requestTradeConfirmation = requestWonderConfirmation = false;
        requestTradeCancellation = false; tradeCancellationId = "";
        tradePicker = null; tradingBossSource = null; tradingRestricted = [];
        tradeSelectionSource = null; cachedTradeId = cachedWonderId = null;
        cachedTradePokemon = cachedWonderPokemon = null;
    }
    private void ApplyTrading(TradingSnapshotDto next)
    {
        trading = next; tradeRefreshRequested = false;
        if (next.Active?.YourPokemon?.Id == tradePokemonId) tradePokemonId = "";
        if (next.Wonder?.Pokemon.Id == wonderPokemonId) wonderPokemonId = "";
        nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(next.Active is not null ? 5 : next.Wonder is not null ? 15 : 60);
    }
    private async Task RefreshTrading()
    {
        ApplyTrading(await InSession(plugin.Api.GetTrades(disposeCancellation.Token)));
        SetStatus("Trading refreshed.", quiet: true);
    }
    private void BackgroundTrading()
    {
        tradeRefreshRequested = false; nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(30);
        RunBackground(async token =>
        {
            var next = await InSession(plugin.Api.GetTrades(token));
            return () => ApplyTrading(next);
        }, () => nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(60));
    }
    private async Task SaveTrade(Func<Task<TradingSnapshotDto>> action, bool refreshTrainer = true)
    {
        try
        {
            ApplyTrading(await InSession(action()));
            if (refreshTrainer) await InSession(RefreshTrainer());
            SetStatus(trading?.Unread > 0 ? "Trade complete. Your new Pokémon is in PC storage." : "Trade saved.");
        }
        catch { tradeRefreshRequested = true; throw; }
    }
    private PokemonDto? TradingSelection(bool wonder)
    {
        if (!ReferenceEquals(tradeSelectionSource, trainer))
        {
            tradeSelectionSource = trainer; cachedTradeId = cachedWonderId = null;
            cachedTradePokemon = cachedWonderPokemon = null;
        }
        var id = wonder ? wonderPokemonId : tradePokemonId;
        if (trainer is null || string.IsNullOrEmpty(id)) return null;
        if ((wonder ? cachedWonderId : cachedTradeId) == id) return wonder ? cachedWonderPokemon : cachedTradePokemon;
        PokemonDto? selected = null;
        foreach (var pokemon in trainer.Party) if (pokemon.Id == id) { selected = pokemon; break; }
        if (selected is null)
            foreach (var pokemon in trainer.Storage) if (pokemon.Id == id) { selected = pokemon; break; }
        if (wonder) { cachedWonderId = id; cachedWonderPokemon = selected; }
        else { cachedTradeId = id; cachedTradePokemon = selected; }
        return selected;
    }
    private bool IsRestrictedTradeSpecies(PokemonDto pokemon)
    {
        if (!ReferenceEquals(tradingBossSource, legendaryBosses))
        {
            tradingBossSource = legendaryBosses;
            tradingRestricted = legendaryBosses.Select(boss => SpeciesDisplayCatalog.Get(boss.Species)?.BaseSpecies ?? boss.Species).ToHashSet(StringComparer.Ordinal);
        }
        var species = SpeciesDisplayCatalog.Get(pokemon.DisplaySpecies)?.BaseSpecies ?? pokemon.Species;
        return tradingRestricted.Contains(species);
    }
    private async Task ViewTradedPokemon(TradeReceiptDto receipt)
    {
        if (trainer is null) return;
        var owner = trainer.User.Id;
        if (receipt.SeenAt is null) ApplyTrading(await InSession(plugin.Api.ReadTradeReceipt(receipt.Id)));
        await InSession(RefreshTrainer());
        if (trainer?.User.Id != owner) return;
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == receipt.Received.Id);
        if (pokemon is null) { SetStatus("This Pokémon is no longer in your collection."); return; }
        mainSection = pokemon.PartySlot.HasValue ? 1 : 2;
        if (mainSection == 1) selectedPartyPokemon = pokemon.Id;
        else
        {
            ObserveStorageOwner(); selectedStoragePokemon = pokemon.Id;
            storageSearch = ""; OpenStorageBox(pokemon.BoxNumber); storageType = storageGeneration = storageSort = 0;
            storageFavorites = storageShiny = storageDuplicatesOnly = false;
        }
        SetStatus("Traded Pokémon selected.");
    }
}
