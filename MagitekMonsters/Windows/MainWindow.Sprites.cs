using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
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
        var region = plugin.Assets.PokemonRegion(path, texture.Width, texture.Height);
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
