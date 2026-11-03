using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawAchievementIconAt(string id, Vector2 origin, Vector2 size, bool earned = true)
    {
        var path = plugin.Assets.Achievement(id);
        if (path.Length == 0 || Plugin.TextureProvider is null) return;
        var texture = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        if (texture is null) return;
        var fitted = new SpriteRegion(0, 0, texture.Width, texture.Height).Fit(size);
        var min = origin + (size - fitted) / 2;
        ImGui.GetWindowDrawList().AddImage(texture.Handle, min, min + fitted, Vector2.Zero, Vector2.One,
            ImGui.GetColorU32(earned ? Vector4.One : new Vector4(0.35f, 0.35f, 0.35f, 0.55f)));
    }
}
