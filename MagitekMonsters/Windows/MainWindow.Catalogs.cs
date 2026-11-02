using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private long loadedCatalogGeneration = -1;
    private bool catalogRefreshRequested;
    private bool catalogCheckBusy;
    private DateTimeOffset nextCatalogCheckAt = DateTimeOffset.UtcNow.AddMinutes(1);
    private DateTimeOffset nextLegacyCatalogRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);

    private void RetryCatalogs()
    {
        catalogRefreshRequested = true;
        nextCatalogRetryAt = DateTimeOffset.UtcNow.AddSeconds(5);
    }

    private void ObserveCatalogVersion()
    {
        if (!IsOpen || catalogCheckBusy || DateTimeOffset.UtcNow < nextCatalogCheckAt) return;
        catalogCheckBusy = true;
        nextCatalogCheckAt = DateTimeOffset.UtcNow.AddMinutes(1);
        _ = Check();
        async Task Check()
        {
            var failed = false;
            try { await plugin.Api.GetMetadata(disposeCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) { }
            catch (Exception exception) { failed = true; Plugin.Log.Debug(exception, "Catalog version check failed"); }
            finally
            {
                backgroundResults.Enqueue(() =>
                {
                    catalogCheckBusy = false;
                    if (failed) nextCatalogCheckAt = DateTimeOffset.UtcNow.AddSeconds(10);
                    else if (plugin.Api.Catalogs.Revision is null && DateTimeOffset.UtcNow >= nextLegacyCatalogRefreshAt)
                    {
                        catalogRefreshRequested = true;
                        nextLegacyCatalogRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
                    }
                });
            }
        }
    }

    private void BackgroundCatalogs()
    {
        catalogRefreshRequested = false;
        RunBackground(token => FetchCatalogs(true, true, token), RetryCatalogs);
    }

    private void ApplyHabitat(uint territory, MagitekMonsters.Models.HabitatDto next)
    {
        if (Plugin.ClientState.TerritoryType != territory) return;
        if (!plugin.Api.Catalogs.TryApply([next], () =>
        {
            var target = loadedTerritory == territory && targetIndex > 0 ? habitat?.Species.ElementAtOrDefault(targetIndex - 1) : null;
            habitat = next;
            loadedTerritory = territory;
            targetIndex = target is null ? 0 : Math.Max(0, next.Species.IndexOf(target) + 1);
        })) RetryCatalogs();
    }

    private void ApplyPokedex(MagitekMonsters.Models.PokedexCatalogDto next)
    {
        if (!plugin.Api.Catalogs.TryApply([next], () =>
        {
            pokedexCatalog = next.Pokemon;
            if (selectedPokedexSpecies is not null && !next.Pokemon.Any(entry => entry.Registered
                && entry.Species.Equals(selectedPokedexSpecies, StringComparison.OrdinalIgnoreCase))) selectedPokedexSpecies = null;
        })) RetryCatalogs();
    }

    private async Task RefreshFriendlyFormats()
    {
        var catalog = await InSession(plugin.Api.GetFriendlyFormats(disposeCancellation.Token));
        if (!plugin.Api.Catalogs.TryApply([catalog], () => { friendlyFormats.Clear(); friendlyFormats.AddRange(catalog.Formats); })) RetryCatalogs();
    }

    private async Task<Action> FetchCatalogs(bool includePokedex, bool refreshHabitat, CancellationToken token)
    {
        var territory = refreshHabitat && trainer is not null ? (uint?)Plugin.ClientState.TerritoryType : null;
        TerritoryContextInfo? context = territory.HasValue ? CurrentTerritoryContext(territory.Value) : null;
        var starterTask = plugin.Api.GetStarters(token);
        var shopTask = plugin.Api.GetShop(token);
        var collectionTask = plugin.Api.GetCollectionRewards(token);
        var frontierTask = plugin.Api.GetFrontierCatalog(token);
        var legendaryTask = plugin.Api.GetLegendaryCatalog(token);
        var trialsTask = plugin.Api.GetSpecialTrials(token);
        var prizeTask = plugin.Api.GetPrizePokemon(token);
        var opponentsTask = plugin.Api.GetOpponents(token);
        var formatsTask = plugin.Api.GetFriendlyFormats(token);
        var pokedexTask = includePokedex && plugin.Api.HasSession ? plugin.Api.GetPokedexCatalog(token) : null;
        var helpTask = pokedexTask is not null ? plugin.Api.GetBattleHelp(token) : null;
        var habitatTask = context is { } area ? plugin.Api.GetHabitat(territory!.Value, area.LocalizedName, area.EnglishName, area.RegionName, area.ZoneName, token) : null;
        var tasks = new List<Task> { starterTask, shopTask, collectionTask, frontierTask, legendaryTask, trialsTask, prizeTask, opponentsTask, formatsTask };
        if (pokedexTask is not null) tasks.Add(pokedexTask);
        if (helpTask is not null) tasks.Add(helpTask);
        if (habitatTask is not null) tasks.Add(habitatTask);
        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (starterTask.Result.Starters.Count == 0) throw new InvalidOperationException("The server returned an empty starter catalog.");
        var responses = new List<object> { starterTask.Result, shopTask.Result, collectionTask.Result, frontierTask.Result,
            legendaryTask.Result, trialsTask.Result, prizeTask.Result, opponentsTask.Result, formatsTask.Result };
        if (pokedexTask is not null) responses.Add(pokedexTask.Result);
        if (helpTask is not null) responses.Add(helpTask.Result);
        if (habitatTask is not null) responses.Add(habitatTask.Result);
        return () =>
        {
            if (!plugin.Api.Catalogs.TryApply(responses, () =>
            {
                starters = starterTask.Result.Starters;
                ApplyShopCatalog(shopTask.Result);
                collectionRewards = collectionTask.Result.Rewards;
                frontierRentals = frontierTask.Result.Rentals;
                legendaryBosses = legendaryTask.Result.Bosses;
                if (pendingLegendaryPermit is { } permit) pendingLegendaryPermit = legendaryBosses.FirstOrDefault(boss => boss.Id == permit.Id);
                specialTrials = trialsTask.Result.Trials;
                prizePokemon = prizeTask.Result.Pokemon;
                opponents = opponentsTask.Result.Opponents;
                regionalLeagues = opponentsTask.Result.Regions;
                friendlyFormats.Clear(); friendlyFormats.AddRange(formatsTask.Result.Formats);
                if (friendlyFormats.All(format => format.Id != friendlyFormatId)) friendlyFormatId = friendlyFormats.FirstOrDefault()?.Id ?? "gen9nationaldex";
                if (pokedexTask is not null) ApplyPokedex(pokedexTask.Result);
                if (helpTask is not null) ApplyBattleHelp(helpTask.Result);
                if (habitatTask is not null && Plugin.ClientState.TerritoryType == territory) ApplyHabitat(territory!.Value, habitatTask.Result);
                raidBoard = null; raidRefreshRequested = true;
                nextEventRefresh = DateTimeOffset.MinValue;
                loadedCatalogGeneration = plugin.Api.Catalogs.Generation;
                catalogRefreshRequested = false; catalogError = null;
                nextCatalogRetryAt = DateTimeOffset.MinValue;
                nextLegacyCatalogRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
                SetStatus("Game data updated.", quiet: true);
            })) RetryCatalogs();
        };
    }
}
