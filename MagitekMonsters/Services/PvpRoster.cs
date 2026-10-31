using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public static class PvpRoster
{
    public static List<PokemonDto> Owned(IEnumerable<PokemonDto> party, IEnumerable<PokemonDto> storage) => party.Concat(storage)
        .Where(pokemon => pokemon.FusionHostId.Length == 0).DistinctBy(pokemon => pokemon.Id)
        .OrderBy(pokemon => pokemon.PartySlot is null ? 1 : 0).ThenBy(pokemon => pokemon.PartySlot)
        .ThenBy(pokemon => pokemon.BoxNumber).ThenBy(pokemon => pokemon.BoxSlot).ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList();

    public static List<PokemonDto> Selected(IEnumerable<PokemonDto> owned, ISet<string> ids) => owned.Where(pokemon => ids.Contains(pokemon.Id)).ToList();

    public static void Synchronize(HashSet<string> ids, IReadOnlyList<PokemonDto> owned, int maximum)
    {
        var available = owned.Select(pokemon => pokemon.Id).ToHashSet();
        ids.RemoveWhere(id => !available.Contains(id));
        foreach (var pokemon in Selected(owned, ids).Skip(Math.Clamp(maximum, 0, 6))) ids.Remove(pokemon.Id);
    }

    public static List<PokemonDto> Filter(IEnumerable<PokemonDto> owned, int source, int box, string search)
    {
        var query = Key(search);
        return owned.Where(pokemon => (source != 1 || pokemon.PartySlot is not null) && (source != 2 || pokemon.PartySlot is null)
            && (box <= 0 || pokemon.PartySlot is null && pokemon.BoxNumber == box)
            && (query.Length == 0 || new[] { pokemon.Nickname, pokemon.DisplaySpecies, pokemon.Ability, pokemon.HeldItem }
                .Concat(pokemon.Types).Concat(pokemon.Moves).Any(value => Key(value).Contains(query, StringComparison.OrdinalIgnoreCase)))).ToList();
    }

    public static string Location(PokemonDto pokemon) => pokemon.PartySlot is int slot ? $"Party {slot}" : $"PC Box {pokemon.BoxNumber}";
    private static string Key(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
}
