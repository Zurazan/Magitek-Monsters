using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public readonly record struct PveBoardGrid(int Columns, float CardHeight, float GridHeight, float ActionHeight);

public static class PvePresentation
{
    public static readonly (string Label, string Id)[] Circuits =
    [
        ("All", "all"), ("Rookie", "rookie"), ("Bronze", "bronze"), ("Silver", "silver"),
        ("Gold", "gold"), ("Ace", "ace"), ("Master", "master"), ("Gyms", "gym"),
        ("Elite Four", "elite"), ("Champion", "champion")
    ];

    public static bool IndividualUnlocked(OpponentDto opponent, ISet<string> defeated) =>
        defeated.Contains(opponent.Id) || opponent.RematchOf is not null || opponent.Tier is not ("elite" or "champion");

    public static OpponentDto[] LeagueStages(IReadOnlyList<OpponentDto> opponents, bool rematch) =>
        opponents.Where(entry => entry.Tier is "elite" or "champion" && (entry.RematchOf is not null) == rematch).ToArray();

    public static string? LockReason(OpponentDto opponent, ISet<string> defeated, IReadOnlyList<OpponentDto> opponents)
    {
        var missing = opponent.RequiredOpponentIds.Where(id => !defeated.Contains(id)).ToArray();
        if (missing.Length > 0)
            return "Defeat " + string.Join(", ", missing.Select(id => opponents.FirstOrDefault(entry => entry.Id == id)?.Name ?? id)) + " first.";
        return IndividualUnlocked(opponent, defeated) ? null : "Earn the first victory in a League run to unlock individual rematches.";
    }

    public static string? TeamError(IReadOnlyList<PokemonDto> team, string challenge, int level) =>
        team.Count == 0 ? "Select at least one Pokémon."
        : challenge == "limited-party" && team.Count > 3 ? "Select at most three Pokémon."
        : challenge == "level-cap" && team.Any(pokemon => pokemon.Level > level) ? $"Your selected team exceeds level {level}."
        : null;

    public static PveBoardGrid Board(float width, float height, float scale, float lineHeight, int count, int largestTeam)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        height = float.IsFinite(height) ? Math.Max(1, height) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        lineHeight = float.IsFinite(lineHeight) ? Math.Max(1, lineHeight) : 17 * scale;
        var columns = width >= 580 * scale ? 2 : 1;
        var cardWidth = Math.Max(1, width / columns - 8 * scale);
        var spritesPerRow = Math.Max(1, (int)((cardWidth - 24 * scale) / (44 * scale)));
        var spriteRows = Math.Max(1, (Math.Max(0, largestTeam) + spritesPerRow - 1) / spritesPerRow);
        var cardHeight = 4 * lineHeight + (36 + spriteRows * 44) * scale;
        var actions = width < 580 * scale ? 4 * lineHeight + 84 * scale : 3 * lineHeight + 48 * scale;
        var rows = Math.Max(1, (Math.Max(0, count) + columns - 1) / columns);
        var gridHeight = Math.Max(1, Math.Min(rows * (cardHeight + 8 * scale) + 4 * scale,
            Math.Max(cardHeight + 8 * scale, height - actions - 8 * scale)));
        return new(columns, cardHeight, gridHeight, actions);
    }
}
