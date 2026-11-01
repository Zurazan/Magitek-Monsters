using System;
using System.Collections.Generic;
using System.Linq;

namespace MagitekMonsters.Services;

public readonly record struct HeaderRegion(float X, float Y, float Width, float Height);
public sealed record CompanionHeaderPlan(float Height, HeaderRegion Name, HeaderRegion Connection, HeaderRegion Uid,
    HeaderRegion Online, float? DividerX, IReadOnlyList<HeaderRegion> Currencies, bool WalletBelow);

public static class CompanionHeaderLayout
{
    public static CompanionHeaderPlan Plan(float width, float lineHeight, float frameHeight, float scale, float uidWidth,
        float onlineWidth, float connectionWidth, IReadOnlyList<float> currencyWidths)
    {
        static float Positive(float value) => float.IsFinite(value) ? Math.Max(1, value) : 1;
        width = Positive(width); lineHeight = Positive(lineHeight); frameHeight = Positive(frameHeight);
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4) : 1;
        var gap = 12 * scale; var rowGap = 5 * scale; var dividerGap = 18 * scale;
        var nameHeight = lineHeight * 1.25f;
        connectionWidth = Math.Min(width, Positive(connectionWidth));
        var separateConnection = width < connectionWidth + 140 * scale + gap;
        var name = new HeaderRegion(0, 0, separateConnection ? width : Math.Max(1, width - connectionWidth - gap), nameHeight);
        var connectionY = separateConnection ? nameHeight + rowGap : 0;
        var connection = new HeaderRegion(width - connectionWidth, connectionY, connectionWidth, frameHeight);
        var metadataY = Math.Max(nameHeight, connectionY + frameHeight) + rowGap;
        uidWidth = Math.Min(width, Positive(uidWidth)); onlineWidth = Math.Min(width, Positive(onlineWidth));
        var metadataWrap = uidWidth + dividerGap + onlineWidth > width;
        var uid = new HeaderRegion(0, metadataY, uidWidth, lineHeight);
        var online = new HeaderRegion(metadataWrap ? 0 : uidWidth + dividerGap,
            metadataWrap ? metadataY + lineHeight + rowGap : metadataY, onlineWidth, lineHeight);
        var metadataWidth = metadataWrap ? width : uidWidth + dividerGap + onlineWidth;
        var widths = currencyWidths.Select(value => Math.Min(width, Positive(value))).ToArray();
        var walletWidth = widths.Sum() + Math.Max(0, widths.Length - 1) * gap;
        var below = widths.Length > 0 && (metadataWrap || metadataWidth + gap + walletWidth > width);
        var walletY = below ? online.Y + lineHeight + rowGap : metadataY;
        var regions = new HeaderRegion[widths.Length];
        var index = 0;
        while (index < widths.Length)
        {
            var end = index + 1; var rowWidth = widths[index];
            while (end < widths.Length && rowWidth + gap + widths[end] <= width)
            { rowWidth += gap + widths[end]; end++; }
            var x = width - rowWidth;
            for (var at = index; at < end; at++) { regions[at] = new(x, walletY, widths[at], lineHeight); x += widths[at] + gap; }
            index = end;
            if (index < widths.Length) walletY += lineHeight + rowGap;
        }
        var height = Math.Max(online.Y + lineHeight, widths.Length > 0 ? walletY + lineHeight : 0);
        return new(height, name, connection, uid, online, metadataWrap ? null : uidWidth + dividerGap / 2,
            regions, below);
    }
}
