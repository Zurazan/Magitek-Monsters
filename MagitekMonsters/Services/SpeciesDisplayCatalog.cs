using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MagitekMonsters.Services;

public sealed record SpeciesDisplay(string Name, string BaseSpecies, string Form, string[] Types);
public sealed record MegaEvolutionDisplay(string Species, string BaseSpecies, string[] Sources, string ItemId,
    string ItemName, string Move, string Ability, string Requirement);

public static class SpeciesDisplayCatalog
{
    private static readonly DisplayFile Catalog = Load();
    private static readonly Dictionary<string, SpeciesDisplay> Entries = Catalog.Species.ToDictionary(entry => SpriteNames.Normalize(entry.Name));
    public static SpeciesDisplay? Get(string species) => Entries.GetValueOrDefault(SpriteNames.Normalize(species));
    public static IReadOnlyList<MegaEvolutionDisplay> MegaEvolutions => Catalog.MegaEvolutions;
    public static IEnumerable<MegaEvolutionDisplay> Megas(string species) => Catalog.MegaEvolutions.Where(entry =>
        SpriteNames.Normalize(entry.BaseSpecies) == SpriteNames.Normalize(Get(species)?.BaseSpecies ?? species));
    public static bool MegaMatches(MegaEvolutionDisplay route, string species, string ability, IEnumerable<string> moves) =>
        route.Sources.Any(source => SpriteNames.Normalize(source) == SpriteNames.Normalize(species))
        && (route.Ability.Length == 0 || route.Ability.Equals(ability, StringComparison.OrdinalIgnoreCase))
        && (route.Move.Length == 0 || moves.Any(move => SpriteNames.Normalize(move) == SpriteNames.Normalize(route.Move)));
    public static string HabitatDescription(string themeId) => Catalog.Habitats.GetValueOrDefault(themeId, "");

    public static string Name(string species)
    {
        var entry = Get(species);
        if (entry is null) return species;
        var region = entry.Form switch { "Alola" => "Alolan", "Galar" => "Galarian", "Hisui" => "Hisuian", "Paldea" => "Paldean", _ => null };
        return (region is null ? entry.Name : $"{region} {entry.BaseSpecies}").Normalize(NormalizationForm.FormC);
    }

    public static bool IsCaught(string species, ISet<string> caughtForms, ISet<string> caughtSpecies)
    {
        var key = SpriteNames.Normalize(species);
        if (caughtForms.Contains(key)) return true;
        var entry = Get(species);
        return entry is { Form.Length: 0 } && caughtSpecies.Contains(SpriteNames.Normalize(entry.BaseSpecies));
    }

    private static DisplayFile Load()
    {
        using var stream = typeof(SpeciesDisplayCatalog).Assembly.GetManifestResourceStream("MagitekMonsters.SpeciesDisplay.json")
                           ?? throw new InvalidDataException("Species display data is missing.");
        return JsonSerializer.Deserialize<DisplayFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidDataException("Species display data is invalid.");
    }

    private sealed record DisplayFile(SpeciesDisplay[] Species, Dictionary<string, string> Habitats, MegaEvolutionDisplay[] MegaEvolutions);
}
