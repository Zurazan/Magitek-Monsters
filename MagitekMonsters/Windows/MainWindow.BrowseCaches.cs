using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private sealed record DexDirectory(List<PokedexEntryDto> Source, List<PokedexEntryDto> Entries, HashSet<string> Caught)
    {
        public bool Registered(PokedexEntryDto entry) => entry.Registered && Caught.Contains(entry.Species);
    }

    private TrainerSnapshotDto? dexDirectoryTrainer;
    private List<PokedexEntryDto>? dexDirectoryCatalog;
    private int dexDirectoryCatalogCount;
    private string? dexDirectorySearch;
    private bool dexDirectoryCaughtOnly;
    private DexDirectory? dexDirectory;
    private List<CollectionObjectiveDto>? objectiveSource;
    private int cachedObjectiveCategory;
    private int cachedObjectiveState;
    private List<CollectionObjectiveDto>? cachedObjectives;
    private List<PokemonDto>? overviewSource;
    private Dictionary<int, List<PokemonDto>>? overviewGroups;
    private bool overviewEmpty;
    private int[]? overviewBoxes;
    private TrainerSnapshotDto? comparisonTrainer;
    private List<PokemonDto>? comparisonPokemon;
    private TrainerSnapshotDto? onlineOwnedTrainer;
    private List<PokemonDto>? onlineOwned;
    private List<PokemonDto>? onlineCandidateSource;
    private List<PokemonDto>? onlineCandidates;
    private string? onlineCandidateSearch;
    private int onlineCandidateMode;
    private int onlineCandidateBox;
    private List<PokemonDto>? raidCandidateSource;
    private List<PokemonDto>? raidCandidates;
    private string? raidCandidateSearch;
    private TrainerSnapshotDto? savedTeamsTrainer;
    private IReadOnlyList<SavedTeamView>? savedTeamViews;
    private List<ShopItemDto>? martSource;
    private List<ShopItemDto>? martItems;
    private bool martTokens;
    private string? martCategory;
    private string? martSearch;
    private List<BagEntry>? bagFilterSource;
    private List<BagEntry>? filteredBagItems;
    private string? bagFilterSearch;
    private int bagFilterPocket;
    private HashSet<string>? synchronizedIds;
    private HashSet<string>? synchronizedSelection;
    private List<PokemonDto>? synchronizedRoster;
    private int synchronizedMaximum;
    private HashSet<string>? selectedRosterIds;
    private HashSet<string>? selectedRosterKeys;
    private List<PokemonDto>? selectedRosterSource;
    private List<PokemonDto>? selectedRosterPokemon;
    private static readonly string[] ObjectiveCategories = ["", "species", "type", "family", "generation"];

    private DexDirectory GetDexDirectory()
    {
        if (dexDirectory is not null && ReferenceEquals(dexDirectoryTrainer, trainer)
            && ReferenceEquals(dexDirectoryCatalog, pokedexCatalog) && dexDirectoryCatalogCount == pokedexCatalog.Count
            && dexDirectorySearch == pokedexSearch && dexDirectoryCaughtOnly == pokedexCaughtOnly) return dexDirectory;
        dexDirectoryTrainer = trainer; dexDirectoryCatalog = pokedexCatalog; dexDirectoryCatalogCount = pokedexCatalog.Count;
        dexDirectorySearch = pokedexSearch; dexDirectoryCaughtOnly = pokedexCaughtOnly;
        return dexDirectory = BuildDexDirectory();
    }

    private DexDirectory BuildDexDirectory()
    {
        var caught = trainer!.Pokedex.CaughtSpecies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var source = pokedexCatalog.Count > 0 ? pokedexCatalog : trainer.Pokedex.CaughtSpecies.Select((species, index) => new PokedexEntryDto
        {
            DexNumber = index + 1, Species = species, Registered = true, Methods = ["Acquisition data is synchronizing."]
        }).ToList();
        bool Registered(PokedexEntryDto entry) => entry.Registered && caught.Contains(entry.Species);
        var entries = source.Where(entry => (!pokedexCaughtOnly || Registered(entry)) && (string.IsNullOrWhiteSpace(pokedexSearch)
                || entry.DexNumber.ToString().Contains(pokedexSearch, StringComparison.OrdinalIgnoreCase)
                || Registered(entry) && entry.Species.Contains(pokedexSearch, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.DexNumber).ToList();
        return new(source, entries, caught);
    }

    private List<CollectionObjectiveDto> GetCollectionObjectives()
    {
        var source = trainer!.CollectionPoints.Objectives;
        if (cachedObjectives is not null && ReferenceEquals(source, objectiveSource)
            && cachedObjectiveCategory == objectiveCategory && cachedObjectiveState == objectiveState) return cachedObjectives;
        objectiveSource = source; cachedObjectiveCategory = objectiveCategory; cachedObjectiveState = objectiveState;
        return cachedObjectives = source.Where(entry => (objectiveCategory == 0 || entry.Category == ObjectiveCategories[objectiveCategory])
                && (objectiveState == 2 || entry.Completed == (objectiveState == 1)))
            .OrderBy(entry => entry.Completed).ThenByDescending(entry => (double)entry.Progress / Math.Max(1, entry.Target))
            .ThenBy(entry => entry.Category).ThenBy(entry => entry.Target).ToList();
    }

    private (Dictionary<int, List<PokemonDto>> Groups, int[] Boxes) GetStorageOverview(List<PokemonDto> filtered)
    {
        if (overviewGroups is null || !ReferenceEquals(overviewSource, filtered))
        {
            overviewSource = filtered;
            overviewGroups = filtered.GroupBy(pokemon => pokemon.BoxNumber).ToDictionary(group => group.Key, group => group.ToList());
            overviewBoxes = null;
        }
        if (overviewBoxes is null || overviewEmpty != storageShowEmptyBoxes)
        {
            overviewEmpty = storageShowEmptyBoxes;
            overviewBoxes = Enumerable.Range(1, StorageBrowser.BoxCount)
                .Where(number => overviewEmpty || overviewGroups.ContainsKey(number)).ToArray();
        }
        return (overviewGroups, overviewBoxes);
    }

    private List<PokemonDto> GetComparisonPokemon()
    {
        if (comparisonPokemon is not null && ReferenceEquals(comparisonTrainer, trainer)) return comparisonPokemon;
        comparisonTrainer = trainer;
        return comparisonPokemon = trainer?.Party.Concat(trainer.Storage).OrderBy(pokemon => pokemon.DisplaySpecies, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pokemon => pokemon.Nickname, StringComparer.OrdinalIgnoreCase).ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList() ?? [];
    }

    private void ResetBrowseCaches()
    {
        ResetDetailCaches();
        ResetStorageSelectionCache();
        dexDirectoryTrainer = comparisonTrainer = null; dexDirectoryCatalog = null; dexDirectory = null;
        objectiveSource = cachedObjectives = null; overviewSource = comparisonPokemon = null;
        overviewGroups = null; overviewBoxes = null;
        onlineOwnedTrainer = savedTeamsTrainer = null;
        onlineOwned = onlineCandidateSource = onlineCandidates = raidCandidateSource = raidCandidates = null;
        savedTeamViews = null;
        martSource = martItems = null; bagFilterSource = filteredBagItems = null;
        synchronizedIds = synchronizedSelection = selectedRosterIds = selectedRosterKeys = null;
        synchronizedRoster = selectedRosterSource = selectedRosterPokemon = null;
    }

    private List<PokemonDto> OnlineOwnedPokemon()
    {
        if (onlineOwned is not null && ReferenceEquals(onlineOwnedTrainer, trainer)) return onlineOwned;
        onlineOwnedTrainer = trainer;
        return onlineOwned = trainer is null ? [] : PvpRoster.Owned(trainer.Party, trainer.Storage);
    }

    private List<PokemonDto> GetOnlineCandidates(List<PokemonDto> owned)
    {
        if (onlineCandidates is not null && ReferenceEquals(onlineCandidateSource, owned) && onlineCandidateSearch == onlineRosterSearch
            && onlineCandidateMode == onlineRosterSource && onlineCandidateBox == onlineRosterBox) return onlineCandidates;
        onlineCandidateSource = owned; onlineCandidateSearch = onlineRosterSearch;
        onlineCandidateMode = onlineRosterSource; onlineCandidateBox = onlineRosterBox;
        return onlineCandidates = PvpRoster.Filter(owned, onlineRosterSource, onlineRosterBox, onlineRosterSearch);
    }

    private List<PokemonDto> GetRaidCandidates()
    {
        var owned = OnlineOwnedPokemon();
        if (raidCandidates is not null && ReferenceEquals(raidCandidateSource, owned) && raidCandidateSearch == raidRosterSearch) return raidCandidates;
        raidCandidateSource = owned; raidCandidateSearch = raidRosterSearch;
        return raidCandidates = owned.Where(pokemon => pokemon.Nickname.Contains(raidRosterSearch, StringComparison.OrdinalIgnoreCase)
            || pokemon.Species.Contains(raidRosterSearch, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private IReadOnlyList<SavedTeamView> GetSavedTeams()
    {
        if (savedTeamViews is not null && ReferenceEquals(savedTeamsTrainer, trainer)) return savedTeamViews;
        savedTeamsTrainer = trainer;
        return savedTeamViews = SavedTeams.Resolve(trainer!);
    }

    private List<ShopItemDto> GetMartItems(bool tokens, string category, string search)
    {
        if (martItems is not null && ReferenceEquals(martSource, shop) && martTokens == tokens && martCategory == category && martSearch == search) return martItems;
        martSource = shop; martTokens = tokens; martCategory = category; martSearch = search;
        return martItems = MartCatalog.Filter(shop, tokens, category, search);
    }

    private List<BagEntry> GetBagItems(List<BagEntry> owned)
    {
        if (filteredBagItems is not null && ReferenceEquals(bagFilterSource, owned) && bagFilterPocket == bagPocket && bagFilterSearch == bagSearch) return filteredBagItems;
        bagFilterSource = owned; bagFilterPocket = bagPocket; bagFilterSearch = bagSearch;
        return filteredBagItems = BagInventory.Filter(owned, BagInventory.Pockets[bagPocket].Id, bagSearch);
    }

    private void SynchronizeOnlineRoster(HashSet<string> ids, List<PokemonDto> owned, int maximum)
    {
        if (ReferenceEquals(synchronizedIds, ids) && ReferenceEquals(synchronizedRoster, owned)
            && synchronizedMaximum == maximum && synchronizedSelection!.SetEquals(ids)) return;
        PvpRoster.Synchronize(ids, owned, maximum);
        synchronizedIds = ids; synchronizedRoster = owned; synchronizedMaximum = maximum;
        synchronizedSelection ??= [];
        synchronizedSelection.Clear(); synchronizedSelection.UnionWith(ids);
    }

    private List<PokemonDto> SelectedOnlinePokemon(HashSet<string> ids)
    {
        var owned = OnlineOwnedPokemon();
        if (selectedRosterPokemon is not null && ReferenceEquals(selectedRosterSource, owned)
            && ReferenceEquals(selectedRosterIds, ids) && selectedRosterKeys!.SetEquals(ids)) return selectedRosterPokemon;
        selectedRosterSource = owned; selectedRosterIds = ids;
        selectedRosterKeys ??= [];
        selectedRosterKeys.Clear(); selectedRosterKeys.UnionWith(ids);
        return selectedRosterPokemon = PvpRoster.Selected(owned, ids);
    }
}
