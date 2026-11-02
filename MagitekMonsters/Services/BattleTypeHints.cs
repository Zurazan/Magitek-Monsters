using System;
using System.Globalization;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public enum BattleTypeHintKind { Neutral, SuperEffective, Resisted, Immune, Status, Variable }
public sealed record BattleTypePreview(string Badge, string Type, string Description, BattleTypeHintKind Kind);

public static class BattleTypeHints
{
    public static string Multiplier(double value) => value == 0.5 ? "½×" : value == 0.25 ? "¼×" : value.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    public static BattleTypePreview? Preview(MoveTypeHintsDto? hints, string modifier = "", int target = 0)
    {
        if (hints is null) return null;
        var hint = modifier switch { "mega" => hints.Mega, "terastallize" => hints.Terastallize, "ultra" => hints.Ultra, _ => hints.Normal };
        if (hint is null || hint.Note == "No type preview") return null;
        if (hint.Category == "Status") return new("Status", hint.Type, "Status move. No damage multiplier.", BattleTypeHintKind.Status);
        var targets = hint.Targets.Where(entry => target == 0 || entry.Target == target).ToList();
        if (target != 0 && targets.Count == 0) return null;
        var known = targets.Where(entry => entry.Multiplier is >= 0 and <= 8 && double.IsFinite(entry.Multiplier.Value)).ToList();
        var values = known.Select(entry => entry.Multiplier!.Value).Distinct().ToList();
        var rows = targets.Select(entry => $"{entry.Name}: {(entry.Multiplier is double value ? Multiplier(value) + " type effectiveness" : entry.Note ?? (hint.Note.Length > 0 ? hint.Note : "Unknown matchup"))}");
        var description = string.Join("\n", rows) + "\nBased on visible types and your move. Abilities, items and battle effects can change the result.";
        if (values.Count == 1 && known.Count == targets.Count)
        {
            var value = values[0];
            var label = value == 0 ? "No effect" : value > 1 ? "Super effective" : value < 1 ? "Resisted" : "Neutral";
            var kind = value == 0 ? BattleTypeHintKind.Immune : value > 1 ? BattleTypeHintKind.SuperEffective : value < 1 ? BattleTypeHintKind.Resisted : BattleTypeHintKind.Neutral;
            return new(Multiplier(value), hint.Type, label + "\n" + description.Trim(), kind);
        }
        var badge = values.Count > 1 || known.Count > 0 ? "Mixed" : targets.Count == 1 && targets[0].Note == "Heals ally" ? "Heal"
            : hint.Note.Contains("damage", StringComparison.OrdinalIgnoreCase) || hint.Note == "One-hit KO move" ? "Fixed" : "Varies";
        return new(badge, hint.Type.Length > 0 ? hint.Type : "Varies", (hint.Note + "\n" + description).Trim(), BattleTypeHintKind.Variable);
    }
}
