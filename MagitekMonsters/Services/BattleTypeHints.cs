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
    public static BattleTypePreview? Preview(MoveTypeHintsDto? hints, string modifier = "", int target = 0, bool includeDescription = true)
    {
        if (hints is null) return null;
        var hint = modifier switch { "mega" => hints.Mega, "terastallize" => hints.Terastallize, "ultra" => hints.Ultra, _ => hints.Normal };
        if (hint is null || hint.Note == "No type preview") return null;
        if (hint.Category == "Status") return new("Status", hint.Type, includeDescription ? "Status move. No damage multiplier." : "", BattleTypeHintKind.Status);
        var count = 0; var known = 0; var value = 0d; var mixed = false;
        MoveTargetHintDto? first = null;
        foreach (var entry in hint.Targets)
        {
            if (target != 0 && entry.Target != target) continue;
            count++; first ??= entry;
            if (entry.Multiplier is not (>= 0 and <= 8) || !double.IsFinite(entry.Multiplier.Value)) continue;
            if (known == 0) value = entry.Multiplier.Value;
            else if (value != entry.Multiplier.Value) mixed = true;
            known++;
        }
        if (target != 0 && count == 0) return null;
        var description = "";
        if (includeDescription)
        {
            var rows = hint.Targets.Where(entry => target == 0 || entry.Target == target)
                .Select(entry => $"{entry.Name}: {(entry.Multiplier is double multiplier ? Multiplier(multiplier) + " type effectiveness" : entry.Note ?? (hint.Note.Length > 0 ? hint.Note : "Unknown matchup"))}");
            description = string.Join("\n", rows) + "\nBased on visible types and your move. Abilities, items and battle effects can change the result.";
        }
        if (known > 0 && !mixed && known == count)
        {
            var label = value == 0 ? "No effect" : value > 1 ? "Super effective" : value < 1 ? "Resisted" : "Neutral";
            var kind = value == 0 ? BattleTypeHintKind.Immune : value > 1 ? BattleTypeHintKind.SuperEffective : value < 1 ? BattleTypeHintKind.Resisted : BattleTypeHintKind.Neutral;
            return new(Multiplier(value), hint.Type, includeDescription ? label + "\n" + description.Trim() : "", kind);
        }
        var badge = known > 0 ? "Mixed" : count == 1 && first!.Note == "Heals ally" ? "Heal"
            : hint.Note.Contains("damage", StringComparison.OrdinalIgnoreCase) || hint.Note == "One-hit KO move" ? "Fixed" : "Varies";
        return new(badge, hint.Type.Length > 0 ? hint.Type : "Varies", includeDescription ? (hint.Note + "\n" + description).Trim() : "", BattleTypeHintKind.Variable);
    }
}
