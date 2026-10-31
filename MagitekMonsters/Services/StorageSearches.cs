using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MagitekMonsters.Services;

public sealed class StorageSavedSearch
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public StorageFilter Filter { get; set; } = StorageSearches.Empty;
}

public sealed class StorageSearchPreferences
{
    public List<StorageSavedSearch> Searches { get; set; } = [];
}

public static class StorageSearches
{
    public const int MaximumSearches = 20;
    public const int MaximumNameLength = 32;
    public static readonly StorageFilter Empty = new("", 0, null, 0, 0, false, false, false);
    private static readonly string[] Types = ["Normal", "Fire", "Water", "Electric", "Grass", "Ice", "Fighting", "Poison", "Ground", "Flying", "Psychic", "Bug", "Rock", "Ghost", "Dragon", "Dark", "Steel", "Fairy"];
    private static readonly string[] Sorts = ["Box order", "Species", "Level: highest first", "Newest first", "IV total: highest first", "Perfect IVs: most first"];

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl)
        && name.Trim().EnumerateRunes().Count() <= MaximumNameLength;

    public static StorageFilter Normalize(StorageFilter? filter)
    {
        filter ??= Empty;
        return filter with { Search = new string((filter.Search ?? "").Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim(),
            Box = 0, Type = Types.FirstOrDefault(type => type.Equals(filter.Type, StringComparison.OrdinalIgnoreCase)),
            Generation = Math.Clamp(filter.Generation, 0, 9), Sort = Math.Clamp(filter.Sort, 0, Sorts.Length - 1) };
    }

    public static StorageSearchPreferences Preferences(IDictionary<string, StorageSearchPreferences> all, string owner)
    {
        if (!all.TryGetValue(owner, out var preferences) || preferences is null) all[owner] = preferences = new();
        preferences.Searches = (preferences.Searches ?? []).Where(search => search is not null && Guid.TryParse(search.Id, out _) && ValidName(search.Name))
            .DistinctBy(search => search.Id).DistinctBy(search => search.Name.Trim(), StringComparer.OrdinalIgnoreCase).Take(MaximumSearches)
            .Select(search => new StorageSavedSearch { Id = search.Id, Name = search.Name.Trim(), Filter = Normalize(search.Filter) }).ToList();
        return preferences;
    }

    public static string? Save(StorageSearchPreferences preferences, string? id, string name, StorageFilter filter)
    {
        if (!ValidName(name)) return $"Enter a name with 1 to {MaximumNameLength} characters.";
        name = name.Trim();
        var existing = id is null ? null : preferences.Searches.FirstOrDefault(search => search.Id == id);
        if (id is not null && existing is null) return "This saved search was removed. Save a new search instead.";
        if (preferences.Searches.Any(search => search.Id != id && search.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return "That name is already used. Choose another name or edit the existing search.";
        if (existing is null && preferences.Searches.Count >= MaximumSearches) return $"All {MaximumSearches} search slots are used. Edit or remove a saved search first.";
        if (existing is null) preferences.Searches.Add(new() { Id = Guid.NewGuid().ToString(), Name = name, Filter = Normalize(filter) });
        else { existing.Name = name; existing.Filter = Normalize(filter); }
        return null;
    }

    public static bool Remove(StorageSearchPreferences preferences, string id) => preferences.Searches.RemoveAll(search => search.Id == id) > 0;

    public static string Description(StorageFilter filter)
    {
        filter = Normalize(filter);
        var parts = new List<string>();
        if (filter.Search.Length > 0) parts.Add("Search: " + filter.Search);
        if (filter.Type is not null) parts.Add(filter.Type + " type");
        if (filter.Generation > 0) parts.Add("Gen " + filter.Generation);
        if (filter.Favorites) parts.Add("Favourites");
        if (filter.Shiny) parts.Add("Shiny only");
        if (filter.Duplicates) parts.Add("Duplicates only");
        if (parts.Count == 0) parts.Add("All Pokémon");
        parts.Add(Sorts[filter.Sort]);
        return string.Join(" / ", parts);
    }
}
