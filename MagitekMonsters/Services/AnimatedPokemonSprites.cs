using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace MagitekMonsters.Services;

public sealed class AnimatedPokemonSprites : IDisposable
{
    private const int MaximumDownloadBytes = 4 * 1024 * 1024;
    private const int MaximumDimension = 512;
    private const int MaximumFrames = 180;
    private const int MaximumMemoryEntries = 16;

    private readonly ConcurrentDictionary<SpriteKey, CacheEntry> entries = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly HttpClient http;
    private readonly string cacheDirectory;
    private readonly AssetCatalog assets;
    private volatile bool disposed;

    public AnimatedPokemonSprites(string cacheDirectory, AssetCatalog assets)
    {
        this.assets = assets;
        this.cacheDirectory = Path.Combine(cacheDirectory, PokemonSpriteSource.CacheFolder);
        Directory.CreateDirectory(this.cacheDirectory);
        http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = MaximumDownloadBytes
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MagitekMonsters/{ClientBuildVersion.Current} animated battle sprites");
    }

    public AnimatedSprite? GetCurrentFrame(string species, bool shiny)
    {
        if (disposed || Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion)
            return null;
        if (assets.ModernSprites && assets.PokemonAnimationId(species, shiny) is null)
            return null;

        var key = new SpriteKey(species.Trim(), shiny);
        var entry = entries.GetOrAdd(key, createdKey => new CacheEntry(
            new Lazy<Task<Animation?>>(
                () => LoadAnimation(createdKey, cancellation.Token),
                LazyThreadSafetyMode.ExecutionAndPublication)));
        entry.Touch();
        TrimMemoryCache(key);

        var loading = entry.Animation.Value;
        if (!loading.IsCompletedSuccessfully)
            return null;

        return loading.Result?.CurrentFrame;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cancellation.Cancel();
        foreach (var entry in entries.Values)
            entry.DisposeIfLoaded();
        entries.Clear();
        http.Dispose();
        cancellation.Dispose();
    }

    private async Task<Animation?> LoadAnimation(SpriteKey key, CancellationToken token)
    {
        try
        {
            var preferred = assets.PokemonAnimationId(key.Species, key.Shiny);
            var candidates = (preferred is null ? SpriteIds(key.Species) : new[] { preferred }).Distinct(StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0)
                return null;

            var cachePath = Path.Combine(
                cacheDirectory,
                $"{(key.Shiny ? "shiny-" : string.Empty)}{candidates[0]}.gif");

            byte[]? data = null;
            if (File.Exists(cachePath))
            {
                data = await File.ReadAllBytesAsync(cachePath, token).ConfigureAwait(false);
                if (data.Length > MaximumDownloadBytes)
                    data = null;
            }

            if (data is null)
            {
                data = await Download(key.Shiny, candidates, token).ConfigureAwait(false);
                if (data is null)
                    return null;
                await File.WriteAllBytesAsync(cachePath, data, token).ConfigureAwait(false);
            }

            var animation = await DecodeAndUpload(key, data, token).ConfigureAwait(false);
            if (disposed)
            {
                animation.Dispose();
                return null;
            }
            return animation;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Animated front sprite unavailable for {Species} (shiny: {Shiny}).", key.Species, key.Shiny);
            return null;
        }
    }

    private async Task<byte[]?> Download(bool shiny, IEnumerable<string> candidates, CancellationToken token)
    {
        foreach (var spriteId in candidates)
        {
            var url = PokemonSpriteSource.AnimationUrl(spriteId, shiny);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                throw new InvalidDataException("The animated sprite exceeded the download limit.");

            var data = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            if (data.Length > MaximumDownloadBytes)
                throw new InvalidDataException("The animated sprite exceeded the download limit.");
            return data;
        }
        return null;
    }

    private static async Task<Animation> DecodeAndUpload(SpriteKey key, byte[] data, CancellationToken token)
    {
        var image = GifSpriteDecoder.Decode(data, MaximumDimension, MaximumFrames, token, PokemonSpriteSource.MaximumAnimationBytes);
        var frames = new List<AnimationFrame>(image.Frames.Count);
        SpriteRegion? region = null;
        try
        {
            for (var index = 0; index < image.Frames.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var source = image.Frames[index];
                var pixels = source.Pixels;
                var bounds = SpriteRegion.Find(pixels, image.Width, image.Height);
                region = region?.Union(bounds) ?? bounds;
                var texture = await Plugin.TextureProvider.CreateFromRawAsync(
                    RawImageSpecification.Rgba32(image.Width, image.Height),
                    pixels,
                    $"Magitek Monsters animated {key.Species} frame {index}",
                    token).ConfigureAwait(false);
                var frameDelay = source.Delay;
                frames.Add(new AnimationFrame(texture, Math.Clamp(frameDelay * 10, 20, 2_000)));
            }
            return new Animation(frames, region!.Value);
        }
        catch
        {
            foreach (var frame in frames)
                frame.Texture.Dispose();
            throw;
        }
    }

    private void TrimMemoryCache(SpriteKey current)
    {
        if (entries.Count <= MaximumMemoryEntries)
            return;

        var oldest = entries
            .Where(pair => pair.Key != current && pair.Value.IsLoaded)
            .OrderBy(pair => pair.Value.LastAccess)
            .FirstOrDefault();
        if (!oldest.Equals(default(KeyValuePair<SpriteKey, CacheEntry>)) && entries.TryRemove(oldest.Key, out var removed))
            removed.DisposeIfLoaded();
    }

    private static IEnumerable<string> SpriteIds(string species)
    {
        var decomposed = species
            .Replace("♀", "f", StringComparison.Ordinal)
            .Replace("♂", "m", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var cleaned = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            cleaned.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var parts = cleaned.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            yield break;

        var boundaryCount = Math.Min(parts.Length - 1, 4);
        var combinations = 1 << boundaryCount;
        var returned = new HashSet<string>(StringComparer.Ordinal);
        for (var mask = combinations - 1; mask >= 0; mask--)
        {
            var candidate = new StringBuilder(parts[0]);
            for (var index = 1; index < parts.Length; index++)
            {
                var preserveBoundary = index - 1 < boundaryCount && (mask & (1 << (index - 1))) != 0;
                if (preserveBoundary)
                    candidate.Append('-');
                candidate.Append(parts[index]);
            }
            if (returned.Add(candidate.ToString()))
                yield return candidate.ToString();
        }
    }

    private readonly record struct SpriteKey(string Species, bool Shiny);

    private sealed class CacheEntry(Lazy<Task<Animation?>> animation)
    {
        private long lastAccess = Stopwatch.GetTimestamp();

        public Lazy<Task<Animation?>> Animation { get; } = animation;
        public long LastAccess => Interlocked.Read(ref lastAccess);
        public bool IsLoaded => Animation.IsValueCreated && Animation.Value.IsCompletedSuccessfully;

        public void Touch() => Interlocked.Exchange(ref lastAccess, Stopwatch.GetTimestamp());

        public void DisposeIfLoaded()
        {
            if (Animation.IsValueCreated && Animation.Value.IsCompletedSuccessfully)
                Animation.Value.Result?.Dispose();
        }
    }

    public readonly record struct AnimatedSprite(IDalamudTextureWrap Texture, SpriteRegion Region);

    private sealed class Animation(IReadOnlyList<AnimationFrame> frames, SpriteRegion region) : IDisposable
    {
        private readonly long startedAt = Stopwatch.GetTimestamp();
        private readonly int totalDuration = frames.Sum(frame => frame.DurationMilliseconds);

        public AnimatedSprite CurrentFrame
        {
            get
            {
                var elapsed = (int)((Stopwatch.GetTimestamp() - startedAt) * 1_000L / Stopwatch.Frequency % totalDuration);
                foreach (var frame in frames)
                {
                    if (elapsed < frame.DurationMilliseconds)
                        return new AnimatedSprite(frame.Texture, region);
                    elapsed -= frame.DurationMilliseconds;
                }
                return new AnimatedSprite(frames[^1].Texture, region);
            }
        }

        public void Dispose()
        {
            foreach (var frame in frames)
                frame.Texture.Dispose();
        }
    }

    private sealed record AnimationFrame(IDalamudTextureWrap Texture, int DurationMilliseconds);
}
