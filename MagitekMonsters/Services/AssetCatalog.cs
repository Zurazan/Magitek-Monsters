using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MagitekMonsters.Services;

public sealed class AssetCatalog
{
    private readonly string graphicsRoot;
    private readonly Dictionary<string, string> itemPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Species, bool Shiny), string> pokemonPaths = [];
    private readonly Dictionary<(string Species, bool Shiny), string> animationIds = [];

    public AssetCatalog(string assemblyDirectory)
    {
        graphicsRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "Graphics"));
        var items = Path.Combine(graphicsRoot, "items");
        if (Directory.Exists(items))
            foreach (var file in Directory.EnumerateFiles(items, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(file => Priority(Path.GetRelativePath(items, file))).ThenBy(file => file, StringComparer.Ordinal))
                itemPaths.TryAdd(SpriteNames.Normalize(Path.GetFileNameWithoutExtension(file)), file);
        foreach (var shiny in new[] { false, true })
        {
            var folder = Path.Combine(graphicsRoot, "pokemon", shiny ? "shiny" : "");
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
                pokemonPaths.TryAdd((SpriteNames.Normalize(Path.GetFileNameWithoutExtension(file)), shiny), file);
        }
        var manifest = Path.Combine(graphicsRoot, "asset-manifest.json");
        if (!File.Exists(manifest)) return;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        if (json.RootElement.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("Unsupported graphics manifest.");
        ModernSprites = json.RootElement.TryGetProperty("pokemonStyle", out var style) && style.GetString() == PokemonSpriteSource.Style;
        if (json.RootElement.TryGetProperty("pokemonAnimations", out var animations))
            foreach (var entry in animations.EnumerateObject())
                foreach (var variant in entry.Value.EnumerateObject())
                {
                    if (variant.Name is not ("normal" or "shiny")) throw new InvalidDataException("Invalid sprite variant.");
                    var id = variant.Value.GetString()!;
                    _ = PokemonSpriteSource.AnimationUrl(id, variant.Name == "shiny");
                    animationIds[(SpriteNames.Normalize(entry.Name), variant.Name == "shiny")] = id;
                }
        foreach (var entry in json.RootElement.GetProperty("itemAliases").EnumerateObject())
            aliases[SpriteNames.Normalize(entry.Name)] = SpriteNames.Normalize(entry.Value.GetString()!);
        foreach (var entry in json.RootElement.GetProperty("pokemon").EnumerateObject())
            foreach (var variant in entry.Value.EnumerateObject())
                pokemonPaths[(SpriteNames.Normalize(entry.Name), variant.Name == "shiny")] = AssetPath(variant.Value.GetString()!);
        var overrides = Path.Combine(graphicsRoot, "overrides", "items");
        if (Directory.Exists(overrides))
            foreach (var file in Directory.EnumerateFiles(overrides, "*.png")) itemPaths[SpriteNames.Normalize(Path.GetFileNameWithoutExtension(file))] = file;
    }
    public bool IsAvailable => Directory.Exists(graphicsRoot);
    public bool ModernSprites { get; }
    public string? PokemonAnimationId(string species, bool shiny) => animationIds.GetValueOrDefault((SpriteNames.Normalize(species), shiny));
    public bool HasItem(string itemId) => ResolveItem(itemId) is not null;
    public string Item(string itemId) => ResolveItem(itemId) ?? string.Empty;
    public string TypeBadge(string type)
    {
        string[] types = ["Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy"];
        var index = Array.FindIndex(types, entry => string.Equals(entry, type, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return string.Empty;
        var file = Path.Combine(graphicsRoot, "types", "generation-viii", "brilliant-diamond-shining-pearl", $"{index + 1}.png");
        return File.Exists(file) ? file : string.Empty;
    }
    public string PokemonFront(string species, bool shiny)
    {
        var key = SpriteNames.Normalize(species);
        if (pokemonPaths.TryGetValue((key, shiny), out var exact) && File.Exists(exact)) return exact;
        var separator = species.IndexOf('-');
        if (separator > 0 && pokemonPaths.TryGetValue((SpriteNames.Normalize(species[..separator]), shiny), out var basic) && File.Exists(basic)) return basic;
        return pokemonPaths.GetValueOrDefault(("UNKNOWN", shiny), string.Empty);
    }
    public string PokemonIcon(string species, bool shiny) => PokemonFront(species, shiny);
    public bool HasExactPokemon(string species, bool shiny) =>
        pokemonPaths.TryGetValue((SpriteNames.Normalize(species), shiny), out var file) && File.Exists(file);
    public bool HasPokemonIcon(string species, bool shiny) => false;
    public string? PokemonSpriteId(string species) => pokemonPaths.TryGetValue((SpriteNames.Normalize(species), false), out var file)
        ? Path.GetFileNameWithoutExtension(file) : null;
    private string? ResolveItem(string id)
    {
        var key = SpriteNames.Normalize(id);
        if (itemPaths.TryGetValue(key, out var direct)) return direct;
        return aliases.TryGetValue(key, out var alias) && itemPaths.TryGetValue(alias, out var mapped) ? mapped : null;
    }
    private string AssetPath(string relative)
    {
        var file = Path.GetFullPath(Path.Combine(graphicsRoot, relative));
        if (!file.StartsWith(Path.Combine(graphicsRoot, "pokemon") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !relative.StartsWith("pokemon/", StringComparison.Ordinal) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid sprite path.");
        return file;
    }
    private static int Priority(string relative)
    {
        var folder = relative.Replace('\\', '/').Split('/');
        if (folder.Length == 1) return 50;
        return folder[0] switch { "gen9" => 90, "gen8" => 80, "gen5" => 40, "gen3" => 30, _ => 0 };
    }
}
