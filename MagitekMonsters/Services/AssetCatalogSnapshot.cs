using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MagitekMonsters.Services;

public sealed class AssetCatalogSnapshot
{
    public const string FileName = "asset-catalog.bin";
    private const int Version = 1;
    private const int MaximumEntries = 100_000;
    private const int MaximumTextBytes = 1024 * 1024;
    private const int ChecksumBytes = 32;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static ReadOnlySpan<byte> Magic => "MMASSETS"u8;

    public readonly record struct PortraitBounds(SpriteRegion Region, int Width, int Height);
    public byte[] ManifestHash { get; private init; } = [];
    public bool ModernSprites { get; private init; }
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<(string Species, bool Shiny), string> Pokemon { get; } = [];
    public Dictionary<(string Species, bool Shiny), string> Animations { get; } = [];
    public Dictionary<string, PortraitBounds> Regions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static AssetCatalogSnapshot FromManifest(string path) => FromManifest(File.ReadAllBytes(path));

    public static AssetCatalogSnapshot FromManifest(ReadOnlyMemory<byte> data)
    {
        using var json = ReadJson(data);
        var root = json.RootElement;
        if (root.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("Unsupported graphics manifest.");
        var result = new AssetCatalogSnapshot {
            ManifestHash = SHA256.HashData(data.Span),
            ModernSprites = root.TryGetProperty("pokemonStyle", out var style) && style.GetString() == PokemonSpriteSource.Style
        };
        foreach (var entry in root.GetProperty("itemAliases").EnumerateObject())
            result.Aliases[SpriteNames.Normalize(entry.Name)] = SpriteNames.Normalize(entry.Value.GetString()!);
        foreach (var entry in root.GetProperty("pokemon").EnumerateObject())
            foreach (var variant in entry.Value.EnumerateObject())
            {
                var path = variant.Value.GetString()!; ValidatePath(path);
                result.Pokemon[(SpriteNames.Normalize(entry.Name), Variant(variant.Name))] = path;
            }
        if (root.TryGetProperty("pokemonAnimations", out var animations))
            foreach (var entry in animations.EnumerateObject())
                foreach (var variant in entry.Value.EnumerateObject())
                {
                    var shiny = Variant(variant.Name); var id = variant.Value.GetString()!;
                    _ = PokemonSpriteSource.AnimationUrl(id, shiny);
                    result.Animations[(SpriteNames.Normalize(entry.Name), shiny)] = id;
                }
        if (root.TryGetProperty("pokemonRegions", out var regions))
            foreach (var entry in regions.EnumerateObject())
            {
                ValidatePath(entry.Name);
                var value = entry.Value;
                if (value.GetArrayLength() is not (6 or 7)) throw new InvalidDataException("Invalid sprite region.");
                var bounds = new PortraitBounds(new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32(), value[3].GetInt32()), value[4].GetInt32(), value[5].GetInt32());
                ValidateBounds(bounds); result.Regions[entry.Name] = bounds;
            }
        return result;
    }

    public static AssetCatalogSnapshot Read(string path, ReadOnlySpan<byte> manifestHash)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        return Read(stream, manifestHash);
    }

    public static AssetCatalogSnapshot Read(Stream stream, ReadOnlySpan<byte> manifestHash)
    {
        var end = stream.Length - ChecksumBytes;
        if (end < 61 || stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Invalid asset snapshot size.");
        var digest = Digest(stream, end);
        Span<byte> saved = stackalloc byte[ChecksumBytes]; stream.ReadExactly(saved);
        if (!CryptographicOperations.FixedTimeEquals(digest, saved)) throw new InvalidDataException("Asset snapshot checksum mismatch.");
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) || reader.ReadInt32() != Version)
            throw new InvalidDataException("Unsupported asset snapshot.");
        var sourceHash = reader.ReadBytes(ChecksumBytes);
        if (!sourceHash.AsSpan().SequenceEqual(manifestHash)) throw new InvalidDataException("Stale asset snapshot.");
        var result = new AssetCatalogSnapshot { ManifestHash = sourceHash, ModernSprites = ReadFlag(reader, end) };
        var count = ReadCount(reader, end, 8); result.Aliases.EnsureCapacity(count);
        for (var index = 0; index < count; index++)
        {
            var key = ReadKey(reader, end); var value = ReadKey(reader, end);
            if (!result.Aliases.TryAdd(key, value)) throw new InvalidDataException("Duplicate asset alias.");
        }
        count = ReadCount(reader, end, 9); result.Pokemon.EnsureCapacity(count);
        for (var index = 0; index < count; index++)
        {
            var key = ReadKey(reader, end); var shiny = ReadFlag(reader, end); var path = ReadText(reader, end); ValidatePath(path);
            if (!result.Pokemon.TryAdd((key, shiny), path)) throw new InvalidDataException("Duplicate asset portrait.");
        }
        count = ReadCount(reader, end, 9); result.Animations.EnsureCapacity(count);
        for (var index = 0; index < count; index++)
        {
            var key = ReadKey(reader, end); var shiny = ReadFlag(reader, end); var id = ReadText(reader, end);
            _ = PokemonSpriteSource.AnimationUrl(id, shiny);
            if (!result.Animations.TryAdd((key, shiny), id)) throw new InvalidDataException("Duplicate asset animation.");
        }
        count = ReadCount(reader, end, 28); result.Regions.EnsureCapacity(count);
        for (var index = 0; index < count; index++)
        {
            var path = ReadText(reader, end); ValidatePath(path); RequireBytes(stream, end, 24);
            var bounds = new PortraitBounds(new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()), reader.ReadInt32(), reader.ReadInt32());
            ValidateBounds(bounds);
            if (!result.Regions.TryAdd(path, bounds)) throw new InvalidDataException("Duplicate portrait bounds.");
        }
        if (stream.Position != end) throw new InvalidDataException("Unexpected asset snapshot data.");
        return result;
    }

    public static void Write(Stream stream, AssetCatalogSnapshot snapshot)
    {
        if (snapshot.ManifestHash.Length != ChecksumBytes) throw new InvalidDataException("Invalid source manifest hash.");
        stream.SetLength(0); stream.Position = 0;
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
        {
            writer.Write(Magic); writer.Write(Version); writer.Write(snapshot.ManifestHash); writer.Write(snapshot.ModernSprites);
            writer.Write(snapshot.Aliases.Count);
            foreach (var entry in snapshot.Aliases.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            { WriteText(writer, entry.Key); WriteText(writer, entry.Value); }
            writer.Write(snapshot.Pokemon.Count);
            foreach (var entry in snapshot.Pokemon.OrderBy(entry => entry.Key.Species, StringComparer.Ordinal).ThenBy(entry => entry.Key.Shiny))
            { WriteText(writer, entry.Key.Species); writer.Write(entry.Key.Shiny); WriteText(writer, entry.Value); }
            writer.Write(snapshot.Animations.Count);
            foreach (var entry in snapshot.Animations.OrderBy(entry => entry.Key.Species, StringComparer.Ordinal).ThenBy(entry => entry.Key.Shiny))
            { WriteText(writer, entry.Key.Species); writer.Write(entry.Key.Shiny); WriteText(writer, entry.Value); }
            writer.Write(snapshot.Regions.Count);
            foreach (var entry in snapshot.Regions.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                WriteText(writer, entry.Key); var bounds = entry.Value;
                writer.Write(bounds.Region.X); writer.Write(bounds.Region.Y); writer.Write(bounds.Region.Width); writer.Write(bounds.Region.Height);
                writer.Write(bounds.Width); writer.Write(bounds.Height);
            }
        }
        var end = stream.Position; var digest = Digest(stream, end);
        stream.Position = end; stream.Write(digest); stream.Flush();
    }

    private static JsonDocument ReadJson(ReadOnlyMemory<byte> data)
    {
        var bytes = data.Span;
        if (bytes.Length >= 2 && (bytes[0] is 0xff or 0xfe || bytes[0] == 0 && bytes[1] == 0))
        {
            using var reader = new StreamReader(new MemoryStream(data.ToArray()));
            return JsonDocument.Parse(reader.ReadToEnd());
        }
        var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        return JsonDocument.Parse(data[offset..]);
    }

    private static bool Variant(string name) => name switch {
        "normal" => false, "shiny" => true, _ => throw new InvalidDataException("Invalid sprite variant.")
    };

    private static void ValidatePath(string path)
    {
        if (!path.StartsWith("pokemon/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.IndexOfAny(['\\', ':', '\0']) >= 0 || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid sprite path.");
    }

    private static void ValidateBounds(PortraitBounds bounds)
    {
        var region = bounds.Region;
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || (long)region.X + region.Width > bounds.Width || (long)region.Y + region.Height > bounds.Height)
            throw new InvalidDataException("Invalid sprite region.");
    }

    private static string ReadKey(BinaryReader reader, long end)
    {
        var key = ReadText(reader, end);
        foreach (var value in key)
            if (value is not (>= 'A' and <= 'Z' or >= '0' and <= '9')) throw new InvalidDataException("Invalid asset key.");
        return key;
    }

    private static bool ReadFlag(BinaryReader reader, long end)
    {
        RequireBytes(reader.BaseStream, end, 1);
        return reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid asset flag.") };
    }

    private static int ReadCount(BinaryReader reader, long end, int minimumBytes)
    {
        RequireBytes(reader.BaseStream, end, sizeof(int)); var count = reader.ReadInt32();
        if (count < 0 || count > MaximumEntries || count > (end - reader.BaseStream.Position) / minimumBytes)
            throw new InvalidDataException("Invalid asset count.");
        return count;
    }

    private static string ReadText(BinaryReader reader, long end)
    {
        RequireBytes(reader.BaseStream, end, sizeof(int)); var length = reader.ReadInt32();
        if (length is < 0 or > MaximumTextBytes) throw new InvalidDataException("Invalid asset string length.");
        RequireBytes(reader.BaseStream, end, length);
        var rented = length > 1024 ? ArrayPool<byte>.Shared.Rent(length) : null;
        try
        {
            Span<byte> bytes = rented is null ? stackalloc byte[length] : rented.AsSpan(0, length);
            reader.BaseStream.ReadExactly(bytes); return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid asset UTF-8.", error); }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        var length = Utf8.GetByteCount(value);
        if (length > MaximumTextBytes) throw new InvalidDataException("Asset string is too long.");
        writer.Write(length); var rented = length > 1024 ? ArrayPool<byte>.Shared.Rent(length) : null;
        try
        {
            Span<byte> bytes = rented is null ? stackalloc byte[length] : rented.AsSpan(0, length);
            Utf8.GetBytes(value, bytes); writer.Write(bytes);
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    private static void RequireBytes(Stream stream, long end, int count)
    {
        if (count < 0 || stream.Position > end - count) throw new InvalidDataException("Truncated asset snapshot.");
    }

    private static byte[] Digest(Stream stream, long count)
    {
        stream.Position = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (count > 0)
            {
                var size = (int)Math.Min(count, buffer.Length);
                stream.ReadExactly(buffer.AsSpan(0, size)); hash.AppendData(buffer, 0, size); count -= size;
            }
            return hash.GetHashAndReset();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
