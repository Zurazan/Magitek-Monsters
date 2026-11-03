using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MagitekMonsters.Services;

public sealed class AssetCatalog
{
    private readonly string graphicsRoot;
    private readonly string pokemonRoot;
    private readonly bool useBinarySnapshot;
    private readonly Dictionary<string, string> itemPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Species, bool Shiny), string> pokemonPaths = [];
    private readonly Dictionary<(string Species, bool Shiny), string> animationIds = [];
    private readonly Dictionary<string, string> typePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (SpriteRegion Region, int Width, int Height)> spriteRegions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Species, bool Shiny), PokemonAsset> pokemonLookups = new();
    private readonly ConcurrentDictionary<string, string> itemLookups = new(StringComparer.OrdinalIgnoreCase);
    private sealed record PokemonAsset(string Path, bool Exact, string? Animation, string? SpriteId);

    public AssetCatalog(string assemblyDirectory) : this(assemblyDirectory, useBinarySnapshot: true) { }

    internal AssetCatalog(string assemblyDirectory, bool useBinarySnapshot)
    {
        this.useBinarySnapshot = useBinarySnapshot;
        graphicsRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "Graphics"));
        pokemonRoot = Path.Combine(graphicsRoot, "pokemon") + Path.DirectorySeparatorChar;
        IsAvailable = Directory.Exists(graphicsRoot);
        string[] types = ["Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy"];
        for (var index = 0; index < types.Length; index++)
        {
            var file = Path.Combine(graphicsRoot, "types", "generation-viii", "brilliant-diamond-shining-pearl", $"{index + 1}.png");
            if (File.Exists(file)) typePaths[types[index]] = file;
        }
        var items = Path.Combine(graphicsRoot, "items");
        if (Directory.Exists(items))
            foreach (var file in Directory.EnumerateFiles(items, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(file => Priority(Path.GetRelativePath(items, file))).ThenBy(file => file, StringComparer.Ordinal))
                itemPaths.TryAdd(SpriteNames.Normalize(Path.GetFileNameWithoutExtension(file)), file);
        var knownPokemonFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shiny in new[] { false, true })
        {
            var folder = Path.Combine(graphicsRoot, "pokemon", shiny ? "shiny" : "");
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                knownPokemonFiles.Add(file);
                pokemonPaths.TryAdd((SpriteNames.Normalize(Path.GetFileNameWithoutExtension(file)), shiny), file);
            }
        }
        var manifest = Path.Combine(graphicsRoot, "asset-manifest.json");
        if (!File.Exists(manifest)) return;
        var metadata = LoadMetadata(manifest);
        ModernSprites = metadata.ModernSprites; aliases = metadata.Aliases; animationIds = metadata.Animations;
        foreach (var entry in metadata.Pokemon)
        {
            var file = AssetPath(entry.Value);
            if (knownPokemonFiles.TryGetValue(file, out var known)) pokemonPaths[entry.Key] = known;
            else if (File.Exists(file)) pokemonPaths[entry.Key] = file;
            else pokemonPaths.Remove(entry.Key);
        }
        foreach (var entry in metadata.Regions)
        {
            var file = AssetPath(entry.Key);
            if (knownPokemonFiles.TryGetValue(file, out var known)) file = known;
            spriteRegions[file] = (entry.Value.Region, entry.Value.Width, entry.Value.Height);
        }
        var overrides = Path.Combine(graphicsRoot, "overrides", "items");
        if (Directory.Exists(overrides))
            foreach (var file in Directory.EnumerateFiles(overrides, "*.png")) itemPaths[SpriteNames.Normalize(Path.GetFileNameWithoutExtension(file))] = file;
    }
    public bool IsAvailable { get; }
    public bool ModernSprites { get; }
    public bool UsesBinarySnapshot { get; private set; }
    private AssetCatalogSnapshot LoadMetadata(string manifest)
    {
        var snapshot = Path.Combine(graphicsRoot, AssetCatalogSnapshot.FileName);
        if (useBinarySnapshot && File.Exists(snapshot))
        {
            try
            {
                using var source = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                var metadata = AssetCatalogSnapshot.Read(snapshot, SHA256.HashData(source));
                UsesBinarySnapshot = true; return metadata;
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return AssetCatalogSnapshot.FromManifest(manifest);
    }
    public string? PokemonAnimationId(string species, bool shiny) => Pokemon(species, shiny).Animation;
    public bool HasItem(string itemId) => ResolveItem(itemId) is not null;
    public string Item(string itemId) => ResolveItem(itemId) ?? string.Empty;
    public string TypeBadge(string type) => typePaths.GetValueOrDefault(type, string.Empty);
    public string PokemonFront(string species, bool shiny) => Pokemon(species, shiny).Path;
    private PokemonAsset Pokemon(string species, bool shiny)
    {
        if (pokemonLookups.TryGetValue((species, shiny), out var cached)) return cached;
        var key = SpriteNames.Normalize(species);
        var exact = pokemonPaths.TryGetValue((key, shiny), out var file);
        var spriteId = exact ? Path.GetFileNameWithoutExtension(file) : null;
        if (!exact)
        {
            var separator = species.IndexOf('-');
            if (separator <= 0 || !pokemonPaths.TryGetValue((SpriteNames.Normalize(species[..separator]), shiny), out file))
                file = pokemonPaths.GetValueOrDefault(("UNKNOWN", shiny), string.Empty);
        }
        var result = new PokemonAsset(file!, exact, animationIds.GetValueOrDefault((key, shiny)), spriteId);
        if (pokemonLookups.Count < 4096) pokemonLookups.TryAdd((species, shiny), result);
        return result;
    }
    public bool HasExactPokemon(string species, bool shiny) => Pokemon(species, shiny).Exact;
    public string? PokemonSpriteId(string species) => Pokemon(species, false).SpriteId;
    public SpriteRegion PokemonRegion(string path, int width, int height) => spriteRegions.TryGetValue(path, out var saved)
        && saved.Width == width && saved.Height == height ? saved.Region : new(0, 0, width, height);
    private string? ResolveItem(string id)
    {
        if (itemLookups.TryGetValue(id, out var cached)) return cached.Length > 0 ? cached : null;
        var key = SpriteNames.Normalize(id);
        var path = itemPaths.GetValueOrDefault(key);
        if (path is null && aliases.TryGetValue(key, out var alias)) path = itemPaths.GetValueOrDefault(alias);
        if (itemLookups.Count < 2048) itemLookups.TryAdd(id, path ?? string.Empty);
        return path;
    }
    private string AssetPath(string relative)
    {
        var file = Path.GetFullPath(Path.Combine(graphicsRoot, relative));
        if (!file.StartsWith(pokemonRoot, StringComparison.OrdinalIgnoreCase)
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
