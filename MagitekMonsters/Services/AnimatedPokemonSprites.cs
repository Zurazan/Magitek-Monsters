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
    private const long MaximumDiskBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan DiskRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<SpriteKey, CacheEntry> entries = new();
    private readonly SpriteLoadQueue loads = new();
    private readonly object diskCacheGate = new();
    private int trimRequested, diskPruned;
    private readonly HttpClient http;
    private readonly string cacheDirectory;
    private readonly AssetCatalog assets;
    private volatile bool disposed;

    public AnimatedPokemonSprites(string cacheDirectory, AssetCatalog assets)
    {
        this.assets = assets;
        this.cacheDirectory = Path.Combine(cacheDirectory, PokemonSpriteSource.CacheFolder);
        http = new HttpClient { Timeout = DownloadTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MagitekMonsters/{ClientBuildVersion.Current} animated battle sprites");
    }

    public AnimatedSprite? GetCurrentFrame(string species, bool shiny)
    {
        if (disposed || Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion)
            return null;
        if (assets.ModernSprites && assets.PokemonAnimationId(species, shiny) is null)
            return null;

        var key = new SpriteKey(species.Trim(), shiny);
        if (!entries.TryGetValue(key, out var entry))
        {
            entry = entries.GetOrAdd(key, static (createdKey, owner) => owner.CreateEntry(createdKey), this);
            Volatile.Write(ref trimRequested, 1);
        }
        if (entry.ShouldRetry)
        {
            var replacement = CreateEntry(key);
            if (entries.TryUpdate(key, replacement, entry)) entry = replacement;
        }
        entry.Touch();
        if (Interlocked.Exchange(ref trimRequested, 0) != 0) TrimMemoryCache(key);

        var loading = entry.Animation.Value;
        if (!loading.IsCompletedSuccessfully)
            return null;

        return loading.Result.Animation?.CurrentFrame;
    }

    private CacheEntry CreateEntry(SpriteKey key) => new(new Lazy<Task<LoadResult>>(
        () => loads.Run(async token =>
        {
            try
            {
                var animation = await LoadAnimation(key, token).ConfigureAwait(false);
                return new LoadResult(animation, Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency);
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                if (!disposed) Plugin.Log.Warning(exception, "Animated front sprite unavailable for {Species} (shiny: {Shiny}).", key.Species, key.Shiny);
                return new LoadResult(null, Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency);
            }
            finally { Volatile.Write(ref trimRequested, 1); }
        }), LazyThreadSafetyMode.ExecutionAndPublication));

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        loads.Dispose();
        foreach (var entry in entries.Values)
            entry.DisposeWhenReady();
        entries.Clear();
        http.Dispose();
    }

    private async Task<Animation?> LoadAnimation(SpriteKey key, CancellationToken token)
    {
        if (Interlocked.Exchange(ref diskPruned, 1) == 0) PruneDiskCache();
        var preferred = assets.PokemonAnimationId(key.Species, key.Shiny);
        var candidates = (preferred is null ? SpriteIds(key.Species) : new[] { preferred }).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return null;
        _ = PokemonSpriteSource.AnimationUrl(candidates[0], key.Shiny);
        var cachePath = Path.Combine(cacheDirectory, $"{(key.Shiny ? "shiny-" : string.Empty)}{candidates[0]}.gif");
        var image = await ReadCached(cachePath, token).ConfigureAwait(false);
        if (image is null)
        {
            var data = await Download(key.Shiny, candidates, token).ConfigureAwait(false);
            if (data is null) return null;
            image = Decode(data, token);
            await WriteCache(cachePath, data, token).ConfigureAwait(false);
        }
        var animation = await Upload(key, image, token).ConfigureAwait(false);
        if (!disposed) return animation;
        animation.Dispose();
        return null;
    }

    private async Task<byte[]?> Download(bool shiny, IEnumerable<string> candidates, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(DownloadTimeout);
        token = timeout.Token;
        foreach (var spriteId in candidates)
        {
            var url = PokemonSpriteSource.AnimationUrl(spriteId, shiny);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                throw new InvalidDataException("The animated sprite exceeded the download limit.");

            await response.Content.LoadIntoBufferAsync(MaximumDownloadBytes, token).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<GifSpriteDecoder.Animation?> ReadCached(string path, CancellationToken token)
    {
        try
        {
            byte[] data;
            await using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (file.Length > MaximumDownloadBytes) throw new InvalidDataException("The cached sprite exceeded the download limit.");
                data = new byte[(int)file.Length];
                await file.ReadExactlyAsync(data, token).ConfigureAwait(false);
            }
            var image = Decode(data, token);
            try
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return image;
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException
            or SixLabors.ImageSharp.ImageFormatException or SixLabors.ImageSharp.Memory.InvalidMemoryOperationException)
        {
            DeleteCacheFile(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private async Task WriteCache(string path, byte[] data, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllBytesAsync(temporary, data, token).ConfigureAwait(false);
            lock (diskCacheGate) File.Move(temporary, path, overwrite: true);
            PruneDiskCache();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { DeleteCacheFile(temporary); }
    }

    private void PruneDiskCache()
    {
        lock (diskCacheGate)
        {
            try
            {
                var folder = new DirectoryInfo(cacheDirectory);
                var expires = DateTime.UtcNow - DiskRetention;
                long kept = 0;
                foreach (var file in folder.EnumerateFiles("*.gif").OrderByDescending(file => file.LastWriteTimeUtc))
                {
                    if (file.LastWriteTimeUtc < expires || file.Length > MaximumDownloadBytes || kept + file.Length > MaximumDiskBytes)
                        DeleteCacheFile(file.FullName);
                    else kept += file.Length;
                }
                foreach (var file in folder.EnumerateFiles("*.gif.*.tmp"))
                    if (file.LastWriteTimeUtc < expires) DeleteCacheFile(file.FullName);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void DeleteCacheFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static GifSpriteDecoder.Animation Decode(byte[] data, CancellationToken token) =>
        GifSpriteDecoder.Decode(data, MaximumDimension, MaximumFrames, token, PokemonSpriteSource.MaximumAnimationBytes);

    private static async Task<Animation> Upload(SpriteKey key, GifSpriteDecoder.Animation image, CancellationToken token)
    {
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
            return new Animation(frames.ToArray(), region!.Value);
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
        while (entries.Count > MaximumMemoryEntries)
        {
            KeyValuePair<SpriteKey, CacheEntry>? oldest = null;
            foreach (var pair in entries)
                if (pair.Key != current && pair.Value.IsLoaded && (oldest is null || pair.Value.LastAccess < oldest.Value.Value.LastAccess)) oldest = pair;
            if (oldest is null || !entries.TryRemove(oldest.Value.Key, out var removed)) break;
            removed.DisposeIfLoaded();
        }
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

    private sealed record LoadResult(Animation? Animation, long RetryAt);

    private sealed class CacheEntry(Lazy<Task<LoadResult>> animation)
    {
        private long lastAccess = Stopwatch.GetTimestamp();

        public Lazy<Task<LoadResult>> Animation { get; } = animation;
        public long LastAccess => Interlocked.Read(ref lastAccess);
        public bool IsLoaded => Animation.IsValueCreated && Animation.Value.IsCompletedSuccessfully;

        public bool ShouldRetry => IsLoaded && Animation.Value.Result is { Animation: null } failure
            && Stopwatch.GetTimestamp() >= failure.RetryAt;

        public void Touch() => Interlocked.Exchange(ref lastAccess, Stopwatch.GetTimestamp());

        public void DisposeIfLoaded()
        {
            if (Animation.IsValueCreated && Animation.Value.IsCompletedSuccessfully)
                Animation.Value.Result.Animation?.Dispose();
        }
        public void DisposeWhenReady()
        {
            if (Animation.IsValueCreated) _ = DisposeAfterLoad(Animation.Value);
        }
        private static async Task DisposeAfterLoad(Task<LoadResult> task)
        {
            try { (await task.ConfigureAwait(false)).Animation?.Dispose(); }
            catch { }
        }
    }

    public readonly record struct AnimatedSprite(IDalamudTextureWrap Texture, SpriteRegion Region);

    private sealed class Animation(AnimationFrame[] frames, SpriteRegion region) : IDisposable
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
