using System;

namespace MagitekMonsters.Services;

public readonly record struct StoragePaneLayout(bool PartyRail, bool SideBySide, float PartyWidth, float BoxWidth, float DetailWidth, float CellHeight);

public static class StorageLayout
{
    public static StoragePaneLayout Measure(float width, float scale)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        var rail = width >= 400 * scale;
        var sideBySide = width >= 720 * scale;
        var party = rail ? 106 * scale : 0;
        var detail = sideBySide ? Math.Clamp(width * 0.28f, 210 * scale, 260 * scale) : width;
        var box = Math.Max(1, width - party - (rail ? 10 * scale : 0) - (sideBySide ? detail + 10 * scale : 0));
        var cell = Math.Clamp((box - 20 * scale - 25 * scale) / 6, 1, 72 * scale);
        return new(rail, sideBySide, party, box, detail, cell);
    }

    public static float GridHeight(float cellHeight, float lineHeight, float spacing, float scale) =>
        20 * scale + lineHeight + spacing + 4 * scale + 5 * (cellHeight + 5 * scale) + 8 * scale;

    public static float PartySlotHeight(float available, float spacing, float scale) => Math.Max(1, (available - 6 * spacing - 2 * scale) / 6);

    public static (int First, int Last) BoxRange(int box)
    {
        var first = (Math.Clamp(box, 1, StorageBrowser.BoxCount) - 1) / 10 * 10 + 1;
        return (first, Math.Min(first + 9, StorageBrowser.BoxCount));
    }
}
