using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MagitekMonsters.Services;

public sealed record StarterStagePreview(string Species, string[] Types);
public sealed record StarterPreview(string Species, string Ability, StarterStagePreview[] Stages, StarterStagePreview? RegionalFinal);

public static class StarterPreviewCatalog
{
    private static readonly Dictionary<string, StarterPreview> Entries = Load();
    public static IEnumerable<StarterPreview> All => Entries.Values;
    public static StarterPreview? Get(string species) => Entries.GetValueOrDefault(species);

    private static Dictionary<string, StarterPreview> Load()
    {
        using var stream = typeof(StarterPreviewCatalog).Assembly.GetManifestResourceStream("MagitekMonsters.StarterPreviews.json")
                           ?? throw new InvalidDataException("Starter previews are missing.");
        var catalog = JsonSerializer.Deserialize<PreviewFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                      ?? throw new InvalidDataException("Starter previews are invalid.");
        return catalog.Starters.ToDictionary(entry => entry.Species, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PreviewFile(string Source, StarterPreview[] Starters);
}
