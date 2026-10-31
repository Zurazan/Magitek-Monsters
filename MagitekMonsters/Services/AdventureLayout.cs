using System;

namespace MagitekMonsters.Services;

public readonly record struct AdventureGrid(int Columns, float CardHeight, float GridHeight, float ActionHeight, float LeadHeight, bool StackedActions);

public static class AdventureLayout
{
    public static AdventureGrid FieldGuide(float width, float height, float scale, float lineHeight, int speciesCount)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        height = float.IsFinite(height) ? Math.Max(1, height) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        lineHeight = float.IsFinite(lineHeight) ? Math.Max(1, lineHeight) : 17 * scale;
        var columns = (int)Math.Clamp(width / (176 * scale), 1, 4);
        var card = Math.Max(106 * scale, 56 * scale + 2 * lineHeight);
        var stacked = width < 600 * scale;
        var actions = 3 * lineHeight + (stacked ? 70 : 28) * scale;
        var lead = width < 420 * scale ? Math.Max(64 * scale, 2 * lineHeight + 24 * scale) : Math.Max(50 * scale, lineHeight + 24 * scale);
        var rows = Math.Max(1, (Math.Max(0, speciesCount) + (long)columns - 1) / columns);
        var natural = rows * (card + 8 * scale) + 4 * scale;
        var grid = Math.Max(1, Math.Min(natural, Math.Max(card + 8 * scale, height - actions - lead - 16 * scale)));
        return new AdventureGrid(columns, card, (float)grid, actions, lead, stacked);
    }
}
