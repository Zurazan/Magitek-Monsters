using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record StorageFilter(string Search, int Box, string? Type, int Generation, bool Favorites, bool Shiny, bool Duplicates);

internal enum StorageSort { Species, Type, LevelHigh, LevelLow, Generation, Newest, IvTotal, PerfectIvs }

public static class StorageBrowser
{
    public const int BoxCount = StorageLimits.BoxCount;
    public const int BoxCapacity = StorageLimits.BoxCapacity;
    private static readonly string[] Stats = ["hp", "atk", "def", "spa", "spd", "spe"];
    public static string Variant(PokemonDto pokemon) => pokemon.Appearance + (pokemon.Shiny ? ":shiny" : ":normal");
    public static Dictionary<string, int> Duplicates(IEnumerable<PokemonDto> owned) => owned.DistinctBy(pokemon => pokemon.Id)
        .GroupBy(Variant, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    public static int IvTotal(PokemonDto pokemon) => Stats.Sum(stat => pokemon.Ivs.GetValueOrDefault(stat));
    public static int PerfectIvs(PokemonDto pokemon) => Stats.Count(stat => pokemon.Ivs.GetValueOrDefault(stat) == 31);
    public static bool ReleaseProtected(PokemonDto pokemon) => pokemon.Favorite || pokemon.FusionHostId.Length > 0 || pokemon.Fusion is not null || pokemon.TradeLockId is not null;

    public static PokemonDto?[] BoxSlots(IEnumerable<PokemonDto> pokemon)
    {
        var slots = new PokemonDto?[BoxCapacity];
        foreach (var entry in pokemon.Where(entry => entry.PartySlot is null))
        {
            var slot = entry.BoxSlot - 1;
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
        return query.OrderBy(pokemon => pokemon.BoxNumber).ThenBy(pokemon => pokemon.BoxSlot)
            .ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList();
    }

    internal static IEnumerable<PokemonDto> SortBox(IEnumerable<PokemonDto> pokemon, StorageSort order)
    {
        var sorted = order switch
        {
            StorageSort.Species => pokemon.OrderBy(entry => entry.DisplaySpecies, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Nickname, StringComparer.OrdinalIgnoreCase),
            StorageSort.LevelHigh => pokemon.OrderByDescending(entry => entry.Level).ThenBy(entry => entry.DisplaySpecies, StringComparer.OrdinalIgnoreCase),
            StorageSort.Newest => pokemon.OrderByDescending(entry => entry.CreatedAt, StringComparer.Ordinal),
            StorageSort.IvTotal => pokemon.OrderByDescending(IvTotal),
            StorageSort.PerfectIvs => pokemon.OrderByDescending(PerfectIvs).ThenByDescending(IvTotal),
            StorageSort.Type => pokemon.OrderBy(entry => entry.Types.Count == 0)
                .ThenBy(entry => entry.Types.FirstOrDefault(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Types.ElementAtOrDefault(1), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.DisplaySpecies, StringComparer.OrdinalIgnoreCase),
            StorageSort.LevelLow => pokemon.OrderBy(entry => entry.Level).ThenBy(entry => entry.DisplaySpecies, StringComparer.OrdinalIgnoreCase),
            StorageSort.Generation => pokemon.OrderBy(entry => entry.Generation <= 0).ThenBy(entry => entry.Generation)
                .ThenBy(entry => entry.DisplaySpecies, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
        return sorted.ThenBy(entry => entry.BoxSlot);
    }

    public static List<PokemonDto> Selected(IEnumerable<PokemonDto> storage, ISet<string> ids) => storage
        .Where(pokemon => pokemon.PartySlot is null && ids.Contains(pokemon.Id)).OrderBy(pokemon => pokemon.BoxNumber).ThenBy(pokemon => pokemon.BoxSlot).ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList();

    public static void Synchronize(HashSet<string> selected, IEnumerable<PokemonDto> storage)
    {
        var eligible = storage.Where(pokemon => pokemon.PartySlot is null && pokemon.FusionHostId.Length == 0 && pokemon.TradeLockId is null).Select(pokemon => pokemon.Id).ToHashSet();
        selected.IntersectWith(eligible);
    }

    public static void Select(HashSet<string> selected, IEnumerable<PokemonDto> candidates)
    {
        foreach (var pokemon in candidates.Where(pokemon => pokemon.PartySlot is null && pokemon.FusionHostId.Length == 0 && pokemon.TradeLockId is null))
        {
            if (selected.Count >= StorageLimits.MaximumBatch) break;
            selected.Add(pokemon.Id);
        }
    }
}
