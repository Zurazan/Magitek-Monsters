using System;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public readonly record struct BattleResultPartyLayout(int Columns, float HealthLabelY, float HealthBarY, float ExperienceLabelY, float ExperienceBarY, float BarHeight, float CardHeight);

public static class BattleResultPresentation
{
    public static BattleResultPartyLayout PartyLayout(float width, float scale, float lineHeight)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        lineHeight = float.IsFinite(lineHeight) ? Math.Max(1, lineHeight) : 17 * scale;
        var healthLabel = Math.Max(40 * scale, 2 * lineHeight + 4 * scale) + 6 * scale;
        var healthBar = healthLabel + lineHeight + 3 * scale;
        var bar = 6 * scale;
        var experienceLabel = healthBar + bar + 10 * scale;
        var experienceBar = experienceLabel + lineHeight + 3 * scale;
        return new(width >= 560 * scale ? 2 : 1, healthLabel, healthBar, experienceLabel, experienceBar, bar, experienceBar + bar + 12 * scale);
    }

    public static int ExperienceGained(BattleResultPokemonDto pokemon) => Math.Max(0, pokemon.ExperienceAfter - pokemon.ExperienceBefore);

    public static (float Ratio, string Label) Experience(BattleResultPokemonDto pokemon)
    {
        var level = Math.Clamp(pokemon.LevelAfter, 1, 100);
        if (level == 100) return (1f, "MAX LEVEL");
        var floor = level * level * level;
        var next = (level + 1) * (level + 1) * (level + 1);
        var experience = Math.Clamp(pokemon.ExperienceAfter, floor, next);
        return ((experience - floor) / (float)(next - floor), $"{next - experience:N0} to Lv. {level + 1}");
    }

    public static string Level(BattleResultPokemonDto pokemon, bool frontier) => frontier ? "Battle Lv. 50"
        : pokemon.LevelAfter > pokemon.LevelBefore ? $"Lv. {pokemon.LevelBefore} → {pokemon.LevelAfter}" : $"Lv. {pokemon.LevelAfter}";

    public static string Outcome(string status) => status switch
    {
        "won" => "Victory", "caught" => "Pokémon caught!", "lost" => "Defeat", "draw" => "Draw", "fled" => "Escaped safely!", "error" => "Battle interrupted", _ => "Battle finished"
    };

    public static string LogLine(string line, string status) => status is "fled" or "caught" && (line == "|tie" || line.StartsWith("|tie|", StringComparison.Ordinal))
        ? string.Empty : line;
}
