using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<string>? discoveredFormSource;
    private HashSet<string>? discoveredForms;
    private List<string>? defeatedOpponentSource;
    private HashSet<string>? defeatedOpponents;
    private List<OpponentDto>? circuitOpponentSource;
    private OpponentDto[]? circuitOpponents;
    private int cachedCircuit;
    private PokemonBuildDto? teachingBuild;
    private TrainerSnapshotDto? teachingTrainer;
    private List<ShopItemDto>? teachingCatalog;
    private string? teachingSearch;
    private List<ShopItemDto>? teachingItems;
    private sealed record CardPartnerChoice(PokemonDto Pokemon, string Button);
    private TrainerSnapshotDto? cardPartnerTrainer;
    private string? cardPartnerSearch;
    private List<CardPartnerChoice>? cardPartners;

    private HashSet<string> GetDiscoveredForms()
    {
        var source = trainer!.Pokedex.CaughtForms;
        if (discoveredForms is not null && ReferenceEquals(discoveredFormSource, source)) return discoveredForms;
        discoveredFormSource = source;
        return discoveredForms = source.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> GetDefeatedOpponents()
    {
        var source = trainer!.PveProgress.DefeatedOpponentIds;
        if (defeatedOpponents is not null && ReferenceEquals(defeatedOpponentSource, source)) return defeatedOpponents;
        defeatedOpponentSource = source;
        return defeatedOpponents = source.ToHashSet();
    }

    private OpponentDto[] GetCircuitOpponents()
    {
        if (circuitOpponents is not null && ReferenceEquals(circuitOpponentSource, opponents) && cachedCircuit == pveTierIndex) return circuitOpponents;
        return BuildCircuitOpponents();
    }

    private OpponentDto[] BuildCircuitOpponents()
    {
        circuitOpponentSource = opponents; cachedCircuit = pveTierIndex;
        var circuit = PvePresentation.Circuits[pveTierIndex].Id;
        return circuitOpponents = opponents.Where(entry => entry.RegionId is null && !entry.Legacy && entry.RematchOf is null
            && (circuit == "all" || entry.Tier == circuit)).ToArray();
    }

    private List<ShopItemDto> GetTeachingItems(PokemonBuildDto build, string search)
    {
        if (teachingItems is not null && ReferenceEquals(teachingBuild, build) && ReferenceEquals(teachingTrainer, trainer)
            && ReferenceEquals(teachingCatalog, itemCatalog) && teachingSearch == search) return teachingItems;
        return BuildTeachingItems(build, search);
    }

    private List<ShopItemDto> BuildTeachingItems(PokemonBuildDto build, string search)
    {
        var compatible = build.TeachableItemIds.ToHashSet();
        teachingBuild = build; teachingTrainer = trainer; teachingCatalog = itemCatalog; teachingSearch = search;
        return teachingItems = itemCatalog.Where(item => compatible.Contains(item.Id)
                && (string.IsNullOrWhiteSpace(search) || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => InventoryCount(item.Id) > 0).ThenBy(item => item.Name).ToList();
    }

    private List<CardPartnerChoice> GetCardPartners()
    {
        if (cardPartners is not null && ReferenceEquals(cardPartnerTrainer, trainer) && cardPartnerSearch == trainerCardSearch) return cardPartners;
        return BuildCardPartners();
    }

    private List<CardPartnerChoice> BuildCardPartners()
    {
        var search = trainerCardSearch.Trim();
        cardPartnerTrainer = trainer; cardPartnerSearch = trainerCardSearch;
        return cardPartners = trainer!.Party.Concat(trainer.Storage).OrderBy(entry => entry.PartySlot is null)
            .ThenBy(entry => entry.PartySlot).ThenBy(entry => entry.BoxNumber).ThenBy(entry => entry.BoxSlot)
            .Select(pokemon => (Pokemon: pokemon, Label: TrainerCard.PartnerLabel(pokemon)))
            .Where(entry => search.Length == 0 || entry.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new CardPartnerChoice(entry.Pokemon, $"{entry.Label} / {(entry.Pokemon.PartySlot is int slot ? $"Party {slot}" : $"Box {entry.Pokemon.BoxNumber}, slot {entry.Pokemon.BoxSlot}")}##CardPartner{entry.Pokemon.Id}"))
            .ToList();
    }

    private void ResetDetailCaches()
    {
        ResetBattleItemCaches();
        ResetEvolutionReadiness();
        discoveredFormSource = defeatedOpponentSource = null; discoveredForms = defeatedOpponents = null;
        circuitOpponentSource = null; circuitOpponents = null;
        teachingBuild = null; teachingTrainer = cardPartnerTrainer = null;
        teachingCatalog = teachingItems = null; cardPartners = null;
    }
}
