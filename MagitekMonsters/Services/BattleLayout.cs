using System;
using System.Globalization;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public readonly record struct BattleConsoleLayout(float MainWidth, float LogWidth, float HeaderHeight, float FieldHeight, float LatestHeight, float ActionHeight, float Gap);
public readonly record struct BattleTypeLayout(float BadgeWidth, float Width, float Height, bool Stacked);
public enum BattleMenu { Fight, Switch, Bag, Catch }
public readonly record struct BattleMoveLabel(string Name, int? Power)
{
    public string ButtonText => Power is int power ? $"{Name} ({power} power)" : Name;
}

public static class BattleLayout
{
    public static BattleMoveLabel MoveLabel(string name)
    {
        var split = name.LastIndexOf(' ');
        if (split > 0)
        {
            var stem = name[..split];
            var canonical = stem.Equals("Return", StringComparison.OrdinalIgnoreCase) ? "Return"
                : stem.Equals("Frustration", StringComparison.OrdinalIgnoreCase) ? "Frustration" : null;
            if (canonical is not null && int.TryParse(name.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var power) && power is >= 1 and <= 102)
                return new(canonical, power);
        }
        return new(name, null);
    }

    public static BattleMenu NextMenu(BattleMenu selected, bool newBattle, bool newTurn, bool forced, bool wild)
    {
        if (forced) return BattleMenu.Switch;
        if (newBattle) return BattleMenu.Fight;
        return !newTurn || wild && selected == BattleMenu.Catch ? selected : BattleMenu.Fight;
    }

    public static BattleTypeLayout Types(float width, float scale, int count)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        count = Math.Clamp(count, 0, 2);
        if (count == 0) return new(0, 0, 0, false);
        var gap = Math.Min(5 * scale, width / 4);
        var badge = Math.Min(64 * scale, (width - (count - 1) * gap) / count);
        return new(badge, badge * count + gap * (count - 1), 18 * scale, width < 340 * scale);
    }

    public static BattleConsoleLayout Console(float width, float height, float scale, bool doubles)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        height = float.IsFinite(height) ? Math.Max(1, height) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        var gap = 6 * scale;
        var log = Math.Min(Math.Clamp(width * 0.28f, 190 * scale, 300 * scale), width * 0.38f);
        var header = 44 * scale;
        var latest = 24 * scale;
        var field = Math.Clamp(height - header - latest - (doubles ? 214 : 210) * scale - 3 * gap, 100 * scale, 180 * scale);
        var actions = Math.Max(1, height - header - latest - field - 3 * gap);
        return new(Math.Max(1, width - (log > 0 ? log + gap : 0)), log, header, field, latest, actions, gap);
    }

    public static bool HasUsablePp(BattleMoveDto move) => move.Pp is > 0 || move.Pp is null && move.Maxpp is null;

    public static string MovePpLabel(BattleMoveDto move) => move.Pp is not null || move.Maxpp is not null
        ? $"PP {move.Pp ?? 0}/{move.Maxpp ?? 0}"
        : move.Id == "recharge" ? "Recharge turn" : move.Id == "struggle" ? "No PP cost" : "Continue move";

    public static bool CanMove(BattleMoveDto move, bool zSelected, BattleZMoveDto? zMove) =>
        HasUsablePp(move) && (!move.Disabled || zSelected && zMove is not null) && (!zSelected || zMove is not null);

    public static bool CanSwitch(BattleSidePokemonDto pokemon, bool forced, bool trapped, bool reviving) =>
        !pokemon.Active && (forced || !trapped) && pokemon.Condition.EndsWith(" fnt", StringComparison.Ordinal) == reviving;

    public static string? DoublesCommand(BattleActiveDto? active, int moveIndex, int target, string modifier, bool pass, int switchSlot)
    {
        if (pass) return "pass";
        if (switchSlot > 0) return $"switch {switchSlot}";
        if (active is null || moveIndex < 0 || moveIndex >= active.Moves.Count) return null;
        var zMove = modifier == "zmove" ? active.CanZMove?.ElementAtOrDefault(moveIndex) : null;
        if (!CanMove(active.Moves[moveIndex], modifier == "zmove", zMove)) return null;
        var targets = modifier == "zmove" ? zMove!.LegalTargets : active.Moves[moveIndex].LegalTargets;
        if (targets.Count > 0 && !targets.Contains(target)) return null;
        return $"move {moveIndex + 1}" + (targets.Count == 0 ? "" : $" {target}") + (modifier.Length == 0 ? "" : $" {modifier}");
    }
}
