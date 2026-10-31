using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record StorageFilter(string Search, int Box, string? Type, int Generation, int Sort, bool Favorites, bool Shiny, bool Duplicates);

public static class StorageBrowser
{
    public const int BoxCount = 100;
    public const int BoxCapacity = 30;
    private static readonly string[] Stats = ["hp", "atk", "def", "spa", "spd", "spe"];
    public static string Variant(PokemonDto pokemon) => pokemon.Appearance + (pokemon.Shiny ? ":shiny" : ":normal");
    public static Dictionary<string, int> Duplicates(IEnumerable<PokemonDto> owned) => owned.DistinctBy(pokemon => pokemon.Id)
        .GroupBy(Variant, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    public static int IvTotal(PokemonDto pokemon) => Stats.Sum(stat => pokemon.Ivs.GetValueOrDefault(stat));
    public static int PerfectIvs(PokemonDto pokemon) => Stats.Count(stat => pokemon.Ivs.GetValueOrDefault(stat) == 31);
    public static bool ReleaseProtected(PokemonDto pokemon) => pokemon.Favorite || pokemon.FusionHostId.Length > 0 || pokemon.Fusion is not null;

    public static PokemonDto?[] BoxSlots(IEnumerable<PokemonDto> pokemon, bool preserveSlots = true)
    {
        var slots = new PokemonDto?[BoxCapacity];
        var index = 0;
        foreach (var entry in pokemon.Where(entry => entry.PartySlot is null))
        {
            var slot = preserveSlots ? entry.BoxSlot - 1 : index++;
            if (slot >= 0 && slot < slots.Length) slots[slot] = entry;
        }
        return slots;
    }

    public static List<PokemonDto> Filter(IEnumerable<PokemonDto> storage, IReadOnlyDictionary<string, int> duplicates, StorageFilter filter)
    {
        var search = filter.Search.Trim();
        var query = storage.Where(pokemon => pokemon.PartySlot is null && (!filter.Favorites || pokemon.Favorite) && (!filter.Shiny || pokemon.Shiny)
            && (filter.Box == 0 || pokemon.BoxNumber == filter.Box) && (filter.Type is null || pokemon.Types.Contains(filter.Type))
            && (filter.Generation == 0 || pokemon.Generation == filter.Generation) && (!filter.Duplicates || duplicates.GetValueOrDefault(Variant(pokemon)) > 1)
            && (search.Length == 0 || new[] { pokemon.DisplaySpecies, pokemon.Nickname, pokemon.Ability, pokemon.HeldItem, pokemon.Nature, pokemon.MintNature }
                .Concat(pokemon.Moves).Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase))));
        IOrderedEnumerable<PokemonDto> sorted = filter.Sort switch
        {
            1 => query.OrderBy(pokemon => pokemon.DisplaySpecies, StringComparer.OrdinalIgnoreCase).ThenBy(pokemon => pokemon.Nickname, StringComparer.OrdinalIgnoreCase),
            2 => query.OrderByDescending(pokemon => pokemon.Level).ThenBy(pokemon => pokemon.DisplaySpecies, StringComparer.OrdinalIgnoreCase),
            3 => query.OrderByDescending(pokemon => pokemon.CreatedAt, StringComparer.Ordinal),
            4 => query.OrderByDescending(IvTotal),
            5 => query.OrderByDescending(PerfectIvs).ThenByDescending(IvTotal),
            _ => query.OrderBy(pokemon => pokemon.BoxNumber).ThenBy(pokemon => pokemon.BoxSlot)
        };
        return sorted.ThenBy(pokemon => pokemon.BoxNumber).ThenBy(pokemon => pokemon.BoxSlot).ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList();
    }

    public static List<PokemonDto> Selected(IEnumerable<PokemonDto> storage, ISet<string> ids) => storage
        .Where(pokemon => pokemon.PartySlot is null && ids.Contains(pokemon.Id)).OrderBy(pokemon => pokemon.BoxNumber).ThenBy(pokemon => pokemon.BoxSlot).ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList();

    public static void Synchronize(HashSet<string> selected, IEnumerable<PokemonDto> storage)
    {
        var eligible = storage.Where(pokemon => pokemon.PartySlot is null && pokemon.FusionHostId.Length == 0).Select(pokemon => pokemon.Id).ToHashSet();
        selected.IntersectWith(eligible);
    }

    public static void Select(HashSet<string> selected, IEnumerable<PokemonDto> candidates)
    {
        foreach (var pokemon in candidates.Where(pokemon => pokemon.PartySlot is null && pokemon.FusionHostId.Length == 0))
        {
            if (selected.Count >= StorageLimits.MaximumBatch) break;
            selected.Add(pokemon.Id);
        }
    }
}
