using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace MagitekMonsters.Services;

internal sealed class MoveEffectTextures : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly AssetCatalog assets;
    private readonly ITextureProvider provider;
    private Dictionary<string, Task<IDalamudTextureWrap?>>? textures;
    private bool disposed;

    public MoveEffectTextures(AssetCatalog assets, ITextureProvider provider)
    {
        this.assets = assets;
        this.provider = provider;
    }

    internal void Preload() => _ = GetTextures();

    internal IDalamudTextureWrap? Get(string file) =>
        GetTextures()?.TryGetValue(file, out var texture) == true && texture.IsCompletedSuccessfully ? texture.Result : null;

    private Dictionary<string, Task<IDalamudTextureWrap?>>? GetTextures()
    {
        lock (gate)
        {
            if (disposed) return null;
            return textures ??= assets.MoveEffects.ToDictionary(asset => asset.Key,
                asset => Load(provider, asset.Key, asset.Value, lifetime.Token), StringComparer.Ordinal);
        }
    }

    private static async Task<IDalamudTextureWrap?> Load(ITextureProvider provider, string file, string path, CancellationToken token)
    {
        try { return await provider.GetFromFile(path).RentAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) Plugin.Log?.Warning(error, "Could not load battle effect image {File}.", file);
            return null;
        }
    }

    public void Dispose()
    {
        Dictionary<string, Task<IDalamudTextureWrap?>>? pending;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            pending = textures;
            textures = null;
        }
        lifetime.Cancel();
        if (pending is null)
        {
            lifetime.Dispose();
            return;
        }
        _ = DisposeWhenLoaded(pending);
    }

    private async Task DisposeWhenLoaded(Dictionary<string, Task<IDalamudTextureWrap?>> pending)
    {
        var loaded = await Task.WhenAll(pending.Values).ConfigureAwait(false);
        foreach (var texture in loaded) texture?.Dispose();
        lifetime.Dispose();
    }
}
