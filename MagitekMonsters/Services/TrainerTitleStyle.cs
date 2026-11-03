using System.Numerics;

namespace MagitekMonsters.Services;

public readonly record struct TrainerTitleStyle(Vector4 Text, Vector4 End, Vector4 Glow)
{
    public static bool TryGet(string titleId, out TrainerTitleStyle style)
    {
        style = titleId switch
        {
            "developer" => new(new(0.83f, 0.68f, 1f, 1f), new(0.46f, 0.90f, 1f, 1f), new(0.49f, 0.20f, 0.82f, 0.30f)),
            "beta-tester" => new(new(1f, 0.76f, 0.31f, 1f), new(1f, 0.93f, 0.68f, 1f), new(0.76f, 0.40f, 0.08f, 0.30f)),
            _ => default
        };
        return style != default;
    }
}
