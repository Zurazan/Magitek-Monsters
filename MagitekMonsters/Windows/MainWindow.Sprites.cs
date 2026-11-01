using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using MagitekMonsters.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, SpriteRegion> spriteRegions = new(StringComparer.OrdinalIgnoreCase);

    private void DrawFrontSprite(string species, bool shiny, Vector2 size)
    {
        if (GetFrontSprite(species, shiny) is { } sprite) DrawFittedSprite(sprite.Texture, sprite.Region, size);
        else ImGui.Dummy(size);
    }

    private (IDalamudTextureWrap Texture, SpriteRegion Region)? GetFrontSprite(string species, bool shiny)
    {
        var path = plugin.Assets.PokemonFront(species, shiny);
        var texture = path.Length > 0 ? Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault() : null;
        if (texture is null) return null;
        if (!spriteRegions.TryGetValue(path, out var region))
        {
            region = new SpriteRegion(0, 0, texture.Width, texture.Height);
            try
            {
                using var image = Image.Load<Rgba32>(path);
                var pixels = new byte[checked(image.Width * image.Height * 4)];
                image.CopyPixelDataTo(pixels);
                region = SpriteRegion.Find(pixels, image.Width, image.Height);
            }
            catch (Exception exception) { Plugin.Log.Warning(exception, "Could not measure sprite {Species}.", species); }
            spriteRegions[path] = region;
        }
        return (texture, region);
    }

    private void DrawPokemonIconAt(string species, bool shiny, Vector2 origin, Vector2 size)
    {
        if (GetFrontSprite(species, shiny) is { } sprite) DrawFittedSpriteAt(sprite.Texture, sprite.Region, origin, size);
        DrawAlcremieBadgeAt(species, size, origin + size);
    }

    private static void DrawFittedSprite(IDalamudTextureWrap texture, SpriteRegion region, Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        DrawFittedSpriteAt(texture, region, origin, size);
    }

    private static void DrawFittedSpriteAt(IDalamudTextureWrap texture, SpriteRegion region, Vector2 origin, Vector2 size)
    {
        var fitted = region.Fit(size);
        var min = origin + (size - fitted) / 2;
        ImGui.GetWindowDrawList().AddImage(texture.Handle, min, min + fitted,
            region.UvMin(texture.Width, texture.Height), region.UvMax(texture.Width, texture.Height));
    }
}
