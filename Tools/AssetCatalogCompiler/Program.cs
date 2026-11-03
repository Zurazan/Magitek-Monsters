using System.IO.Compression;
using MagitekMonsters.Services;

try
{
    if (args.Length == 2 && args[0] == "--verify-archive")
    {
        using var archive = ZipFile.OpenRead(args[1]);
        using var manifest = ReadEntry("Graphics/asset-manifest.json");
        using var snapshot = ReadEntry("Graphics/" + AssetCatalogSnapshot.FileName);
        Verify(AssetCatalogSnapshot.FromManifest(manifest.ToArray()), snapshot.ToArray());
        MemoryStream ReadEntry(string path)
        {
            using var stream = (archive.GetEntry(path) ?? throw new InvalidDataException("Missing " + path)).Open();
            var memory = new MemoryStream(); stream.CopyTo(memory); memory.Position = 0; return memory;
        }
    }
    else if (args.Length == 3 && args[0] == "--verify")
        Verify(AssetCatalogSnapshot.FromManifest(args[1]), File.ReadAllBytes(args[2]));
    else if (args.Length == 2)
    {
        var metadata = AssetCatalogSnapshot.FromManifest(args[0]);
        using var encoded = new MemoryStream(); AssetCatalogSnapshot.Write(encoded, metadata);
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var bytes = encoded.ToArray();
        if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(bytes))
        {
            var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, output, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        File.SetLastWriteTimeUtc(output, DateTime.UtcNow);
        Console.WriteLine($"Generated asset snapshot: {bytes.Length:N0} bytes.");
    }
    else throw new ArgumentException("Use <manifest> <snapshot>, --verify <manifest> <snapshot>, or --verify-archive <zip>.");
}
catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }

static void Verify(AssetCatalogSnapshot metadata, byte[] bytes)
{
    using var source = new MemoryStream(bytes); _ = AssetCatalogSnapshot.Read(source, metadata.ManifestHash);
    using var expected = new MemoryStream(); AssetCatalogSnapshot.Write(expected, metadata);
    if (!bytes.AsSpan().SequenceEqual(expected.ToArray())) throw new InvalidDataException("Asset snapshot does not match its manifest.");
    Console.WriteLine("PASS: checksummed asset snapshot matches its complete source metadata.");
}
