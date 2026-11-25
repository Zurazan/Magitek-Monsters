using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly record struct GenderMark(FontAwesomeIcon Icon, string Label, Vector4 Color);

    private static GenderMark? PokemonGenderMark(string? gender) => gender switch
    {
        "M" => new(FontAwesomeIcon.Mars, "Male", new Vector4(.50f, .72f, 1, 1)),
        "F" => new(FontAwesomeIcon.Venus, "Female", new Vector4(1, .58f, .72f, 1)),
        _ => null
    };

    private static void DrawGenderedPokemonText(string name, string? gender, Vector4 color, bool centered = false)
    {
        var mark = PokemonGenderMark(gender);
        var iconSize = ImGui.GetTextLineHeight();
        var iconSpace = mark is null ? 0 : iconSize + 4 * CompanionScale;
        var available = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var wrap = Math.Max(1, available - iconSpace);
        var textSize = ImGui.CalcTextSize(name, false, wrap);
        var width = Math.Min(wrap, textSize.X) + iconSpace;
        if (centered) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (available - width) / 2));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, Math.Max(textSize.Y, iconSize)));
        var draw = ImGui.GetWindowDrawList();
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), origin, ImGui.GetColorU32(color), name, wrap);
        if (mark is not { } icon) return;
        var iconPosition = origin + new Vector2(width - iconSize, 0);
        DrawPokemonGenderMark(icon, iconPosition, iconSize);
        if (ImGui.IsItemHovered() && ImGui.IsMouseHoveringRect(iconPosition, iconPosition + new Vector2(iconSize)))
            ImGui.SetTooltip(icon.Label);
    }

    private static void DrawPokemonGenderMark(GenderMark mark, Vector2 position, float size)
    {
        var draw = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(mark.Color);
        var font = Plugin.PluginInterface?.UiBuilder?.IconFontFixedWidthHandle;
        if (font is { Available: true })
        {
            using (font.Push())
            {
                var glyph = mark.Icon.ToIconString();
                var glyphSize = ImGui.CalcTextSize(glyph);
                var scale = Math.Min(1, size / Math.Max(1, Math.Max(glyphSize.X, glyphSize.Y)));
                var offset = (new Vector2(size) - glyphSize * scale) / 2;
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, position + offset, color, glyph);
            }
            return;
        }

        var stroke = Math.Max(1, size * .09f);
        Vector2 Point(float x, float y) => position + new Vector2(x, y) * size;
        if (mark.Icon == FontAwesomeIcon.Mars)
        {
            draw.AddCircle(Point(.39f, .61f), .23f * size, color, 0, stroke);
            draw.AddLine(Point(.56f, .44f), Point(.83f, .17f), color, stroke);
            draw.AddLine(Point(.57f, .17f), Point(.83f, .17f), color, stroke);
            draw.AddLine(Point(.83f, .17f), Point(.83f, .43f), color, stroke);
        }
        else
        {
            draw.AddCircle(Point(.5f, .35f), .23f * size, color, 0, stroke);
            draw.AddLine(Point(.5f, .58f), Point(.5f, .9f), color, stroke);
            draw.AddLine(Point(.32f, .76f), Point(.68f, .76f), color, stroke);
        }
    }
}
