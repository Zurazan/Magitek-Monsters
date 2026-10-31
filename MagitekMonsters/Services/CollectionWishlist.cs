using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class CollectionWish
{
    public string Kind { get; set; } = "species";
    public string Id { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
}

public sealed class CollectionWishlistPreferences
{
    public List<CollectionWish> Pins { get; set; } = [];
}

public enum WishlistAvailability { None, SpeciesHere, FormHere, FormUnconfirmed }
public sealed record CollectionWishView(CollectionWish Pin, string Name, bool Completed, string Progress,
    WishlistAvailability Availability, string? Appearance);

public static class CollectionWishlist
{
    public const int MaximumPins = 50;

    private static bool Valid(CollectionWish? pin) => pin is not null && pin.Kind is "species" or "form" or "objective"
        && !string.IsNullOrWhiteSpace(pin.Id) && pin.Id.Length <= 160 && !pin.Id.Any(char.IsControl);
    private static string Key(string kind, string id) => kind + ":" + (kind == "objective" ? id.ToLowerInvariant() : SpriteNames.Normalize(id));

    public static CollectionWishlistPreferences Preferences(IDictionary<string, CollectionWishlistPreferences> all, string owner)
    {
        if (!all.TryGetValue(owner, out var preferences) || preferences is null) all[owner] = preferences = new();
        preferences.Pins = (preferences.Pins ?? []).Where(Valid).DistinctBy(pin => Key(pin.Kind, pin.Id)).Take(MaximumPins).ToList();
        return preferences;
    }

    public static bool Contains(CollectionWishlistPreferences preferences, string kind, string id) =>
        preferences.Pins.Any(pin => Key(pin.Kind, pin.Id) == Key(kind, id));

    public static bool Toggle(CollectionWishlistPreferences preferences, string kind, string id, string species = "")
    {
        var key = Key(kind, id);
        if (preferences.Pins.RemoveAll(pin => Key(pin.Kind, pin.Id) == key) > 0) return true;
        var entry = new CollectionWish { Kind = kind, Id = id.Trim(), Species = kind == "form" ? species : "" };
        if (!Valid(entry) || preferences.Pins.Count >= MaximumPins) return false;
        preferences.Pins.Add(entry);
        return true;
    }

    public static bool Add(CollectionWishlistPreferences preferences, string kind, string id, string species = "") =>
        Contains(preferences, kind, id) || Toggle(preferences, kind, id, species);

    public static string BaseSpecies(string appearance) => SpeciesDisplayCatalog.Get(appearance)?.BaseSpecies ?? appearance;
    private static bool Same(string first, string second) => SpriteNames.Normalize(first) == SpriteNames.Normalize(second);

    public static WishlistAvailability Match(CollectionWish pin, string appearance)
    {
        if (pin.Kind == "objective") return WishlistAvailability.None;
        if (pin.Kind == "species") return Same(pin.Id, BaseSpecies(appearance)) ? WishlistAvailability.SpeciesHere : WishlistAvailability.None;
        var desired = SpeciesDisplayCatalog.Get(pin.Id);
        var desiredSpecies = string.IsNullOrWhiteSpace(pin.Species) ? desired?.BaseSpecies ?? pin.Id : pin.Species;
        if (Same(pin.Id, appearance) && !Same(pin.Id, desiredSpecies)) return WishlistAvailability.FormHere;
        var actual = SpeciesDisplayCatalog.Get(appearance);
        return Same(desiredSpecies, actual?.BaseSpecies ?? appearance) && actual is { Form.Length: 0 }
            ? WishlistAvailability.FormUnconfirmed : WishlistAvailability.None;
    }

    public static WishlistAvailability MatchAppearance(CollectionWishlistPreferences preferences, string appearance) =>
        preferences.Pins.Select(pin => Match(pin, appearance)).OrderByDescending(value => value == WishlistAvailability.FormHere ? 3
            : value == WishlistAvailability.SpeciesHere ? 2 : value == WishlistAvailability.FormUnconfirmed ? 1 : 0).FirstOrDefault();

    public static IReadOnlyList<CollectionWishView> Resolve(CollectionWishlistPreferences preferences, TrainerSnapshotDto trainer,
        IReadOnlyList<string>? available = null)
    {
        available ??= [];
        var species = trainer.Pokedex.CaughtSpecies.Select(SpriteNames.Normalize).ToHashSet();
        var forms = trainer.Pokedex.CaughtForms.Select(SpriteNames.Normalize).ToHashSet();
        return preferences.Pins.Where(Valid).Select(pin => {
            if (pin.Kind == "objective")
            {
                var objective = trainer.CollectionPoints.Objectives.FirstOrDefault(entry => entry.Id.Equals(pin.Id, StringComparison.OrdinalIgnoreCase));
                return new CollectionWishView(pin, objective?.Name ?? pin.Id, objective?.Completed == true,
                    objective is null ? "Goal data unavailable" : $"{objective.Progress:N0} / {objective.Target:N0}", WishlistAvailability.None, null);
            }
            var completed = (pin.Kind == "form" ? forms : species).Contains(SpriteNames.Normalize(pin.Id));
            var match = available.Select(appearance => (Appearance: appearance, Match: Match(pin, appearance)))
                .OrderByDescending(entry => entry.Match == WishlistAvailability.FormHere ? 3 : entry.Match == WishlistAvailability.SpeciesHere ? 2
                    : entry.Match == WishlistAvailability.FormUnconfirmed ? 1 : 0).FirstOrDefault();
            return new CollectionWishView(pin, SpeciesDisplayCatalog.Name(pin.Id), completed,
                completed ? pin.Kind == "form" ? "Form registered" : "Species registered" : pin.Kind == "form" ? "Form not registered" : "Species not registered",
                match.Match, match.Match == WishlistAvailability.None ? null : match.Appearance);
        }).ToArray();
    }

    public static CollectionWish? FindSpecies(string query, IEnumerable<PokedexEntryDto> catalog)
    {
        query = query.Trim();
        var numeric = int.TryParse(query.TrimStart('#'), out var number);
        var entry = catalog.FirstOrDefault(candidate => numeric ? candidate.DexNumber == number : Same(candidate.Species, query));
        return entry is null ? null : new() { Kind = "species", Id = entry.Species };
    }
}
