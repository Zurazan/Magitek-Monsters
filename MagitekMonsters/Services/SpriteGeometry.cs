using System;
using System.Numerics;

namespace MagitekMonsters.Services;

public readonly record struct SpriteRegion(int X, int Y, int Width, int Height)
{
    public static SpriteRegion Find(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length)
            throw new ArgumentException("Invalid sprite pixels.");
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] == 0) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        return right < 0 ? new SpriteRegion(0, 0, width, height) : new SpriteRegion(left, top, right - left + 1, bottom - top + 1);
    }

    public SpriteRegion Union(SpriteRegion other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new SpriteRegion(left, top, Math.Max(X + Width, other.X + other.Width) - left, Math.Max(Y + Height, other.Y + other.Height) - top);
    }

    public Vector2 Fit(Vector2 box)
    {
        var ratio = Math.Min(box.X / Math.Max(1, Width), box.Y / Math.Max(1, Height));
        return new Vector2(Width, Height) * Math.Max(0, ratio);
    }

    public Vector2 UvMin(int width, int height) => new(X / (float)width, Y / (float)height);
    public Vector2 UvMax(int width, int height) => new((X + Width) / (float)width, (Y + Height) / (float)height);
}
