using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static unsafe void DrawTrainerTitle(string text, string titleId, float width, Vector4 fallback)
    {
        if (!TrainerTitleStyle.TryGet(titleId, out var style))
        {
            DrawClippedText(text, width, fallback);
            return;
        }
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(1, width), ImGui.GetTextLineHeight());
        ImGui.Dummy(size);
        if (!ImGui.IsItemVisible()) return;
        var radius = style.Glow.W > 0 ? Math.Max(1, ImGui.GetFontSize() / 17f) : 0;
        var draw = ImGui.GetWindowDrawList();
        var edge = radius * 2;
        draw.PushClipRect(start - new Vector2(edge), start + size + new Vector2(0, edge), true);
        if (radius > 0)
        {
            var outer = ImGui.GetColorU32(style.Glow with { W = style.Glow.W * 0.25f });
            var inner = ImGui.GetColorU32(style.Glow);
            draw.AddText(start + new Vector2(-edge, 0), outer, text);
            draw.AddText(start + new Vector2(edge, 0), outer, text);
            draw.AddText(start + new Vector2(0, -edge), outer, text);
            draw.AddText(start + new Vector2(0, edge), outer, text);
            draw.AddText(start + new Vector2(-radius, -radius), inner, text);
            draw.AddText(start + new Vector2(radius, -radius), inner, text);
            draw.AddText(start + new Vector2(-radius, radius), inner, text);
            draw.AddText(start + new Vector2(radius, radius), inner, text);
        }
        var from = ImGui.GetColorU32(style.Text);
        var firstVertex = draw.VtxBuffer.Size;
        draw.AddText(start, from, text);
        var textWidth = Math.Max(1, ImGui.CalcTextSize(text).X);
        var to = ImGui.GetColorU32(style.End);
        var firstColor = new Vector3(from & 255, (from >> 8) & 255, (from >> 16) & 255);
        var lastColor = new Vector3(to & 255, (to >> 8) & 255, (to >> 16) & 255);
        for (var index = firstVertex; index < draw.VtxBuffer.Size; index++)
        {
            ref var vertex = ref draw.VtxBuffer.Data[index];
            var position = Math.Clamp((vertex.Pos.X - start.X) / textWidth, 0, 1);
            var color = Vector3.Lerp(firstColor, lastColor, position);
            vertex.Col = (vertex.Col & 0xff000000) | (uint)(color.X + 0.5f)
                | ((uint)(color.Y + 0.5f) << 8) | ((uint)(color.Z + 0.5f) << 16);
        }
        draw.PopClipRect();
        if (ImGui.IsItemHovered() && textWidth > width) ImGui.SetTooltip(text);
    }
}
