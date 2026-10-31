using System;

namespace MagitekMonsters.Services;

public readonly record struct CompanionSplit(bool SideBySide, float ListWidth, float DetailWidth, float Height);

public static class CompanionLayout
{
    public static CompanionSplit Selection(float width, float height, float scale)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        height = float.IsFinite(height) ? Math.Max(1, height) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        var wide = width >= 700 * scale;
        var detail = wide ? 280 * scale : width;
        return new CompanionSplit(wide, wide ? width - detail - 12 * scale : width, detail, height);
    }
}
