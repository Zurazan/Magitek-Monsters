using System;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public readonly record struct BattleHealth(int Hp, int MaxHp, string Status, bool Fainted)
{
    public float Ratio => MaxHp > 0 ? Math.Clamp((float)Hp / MaxHp, 0, 1) : 0;
    public string Label => Fainted ? "Fainted" : MaxHp > 0 ? $"{Hp}/{MaxHp} HP{(Status.Length > 0 ? " / " + Status.ToUpperInvariant() : "")}" : "HP unavailable";
}

public static class BattleBag
{
    public static BattleHealth Health(string condition)
    {
        var remaining = condition.AsSpan(); var first = Word(ref remaining); var slash = first.IndexOf('/');
        var hp = 0; var max = 0;
        var known = slash >= 0 && int.TryParse(first[..slash], out hp) && int.TryParse(first[(slash + 1)..], out max);
        var maximum = known ? Math.Max(0, max) : 0;
        var current = known ? Math.Clamp(hp, 0, maximum) : 0;
        var fainted = first.SequenceEqual("fnt") || known && current == 0;
        var status = "";
        while (Word(ref remaining) is { Length: > 0 } word)
        {
            if (word.SequenceEqual("fnt")) fainted = true;
            else status = status.Length == 0 ? Status(word) : string.Concat(status, " ", word);
        }
        return new(current, maximum, status, fainted);
    }

    private static ReadOnlySpan<char> Word(ref ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty && text[0] == ' ') text = text[1..];
        var separator = text.IndexOf(' ');
        if (separator < 0) { var word = text; text = []; return word; }
        var value = text[..separator]; text = text[(separator + 1)..]; return value;
    }
    private static string Status(ReadOnlySpan<char> value) => value switch {
        "par" => "par", "psn" => "psn", "tox" => "tox", "brn" => "brn", "slp" => "slp", "frz" => "frz", _ => value.ToString()
    };

    public static string Pocket(ShopItemDto item) => item.BattleEffect switch
    {
        "pp-restore" or "pp-restore-all" => "PP",
        "full-heal" => "Status",
        _ => "Healing"
    };

    public static string Summary(ShopItemDto item) => item.BattleEffect switch
    {
        "revive" => "Revive / Half HP",
        "max-revive" => "Revive / Full HP",
        "full-heal" => "Full HP and status",
        "pp-restore" => item.Id == "ether" ? "+10 PP / One move" : "Full PP / One move",
        "pp-restore-all" => item.Id == "elixir" ? "+10 PP / All moves" : "Full PP / All moves",
        "heal" => item.HealAmount is > 0 and < 9999 ? $"+{item.HealAmount} HP" : "Full HP",
        _ => ""
    };

    public static string Reason(ShopItemDto item, BattleSidePokemonDto pokemon)
    {
        var hp = Health(pokemon.Condition);
        if (item.BattleEffect is "revive" or "max-revive") return hp.Fainted ? "" : "Not fainted";
        if (hp.Fainted) return "Needs a Revive";
        if (item.BattleEffect is "pp-restore" or "pp-restore-all")
            return pokemon.PpMoves.Any(move => move.Pp < move.MaxPp) ? "" : "PP full";
        if (item.BattleEffect is not ("heal" or "full-heal")) return "Cannot use in battle";
        if (hp.MaxHp == 0) return "HP unavailable";
        return hp.Hp < hp.MaxHp || item.BattleEffect == "full-heal" && hp.Status.Length > 0 ? ""
            : item.BattleEffect == "full-heal" ? "HP and status clear" : "HP full";
    }

    public static int MoveSlot(BattleSidePokemonDto pokemon, int preferred)
    {
        if (preferred >= 0 && preferred < pokemon.PpMoves.Count && pokemon.PpMoves[preferred].Pp < pokemon.PpMoves[preferred].MaxPp) return preferred;
        return pokemon.PpMoves.FindIndex(move => move.Pp < move.MaxPp);
    }

    public static string Preview(ShopItemDto item, BattleSidePokemonDto pokemon, int moveSlot)
    {
        var reason = Reason(item, pokemon);
        if (reason.Length > 0) return reason;
        var hp = Health(pokemon.Condition);
        if (item.BattleEffect == "revive") return "Revive with half HP";
        if (item.BattleEffect == "max-revive") return "Revive with full HP";
        if (item.BattleEffect == "pp-restore-all") return item.Id == "elixir" ? "+10 PP to each move, up to its maximum" : "Fully restore every move's PP";
        if (item.BattleEffect == "pp-restore")
        {
            if (moveSlot < 0 || moveSlot >= pokemon.PpMoves.Count) return "Choose a move";
            var move = pokemon.PpMoves[moveSlot];
            var after = (int)Math.Min(move.MaxPp, (long)move.Pp + (item.Id == "ether" ? 10 : 999));
            return $"{move.Name}: PP {move.Pp} > {after}/{move.MaxPp}";
        }
        var healed = (int)Math.Min(hp.MaxHp, (long)hp.Hp + (item.HealAmount ?? 9999));
        return $"HP {hp.Hp} > {healed}/{hp.MaxHp}" + (item.BattleEffect == "full-heal" ? " / Clears status" : "");
    }

    public static string? Command(ShopItemDto item, BattleSidePokemonDto pokemon, int partySlot, int moveSlot)
    {
        if (partySlot is < 0 or > 5 || Reason(item, pokemon).Length > 0) return null;
        if (item.BattleEffect != "pp-restore") return $"item {item.Id} {partySlot + 1}";
        if (moveSlot < 0 || moveSlot >= pokemon.PpMoves.Count || pokemon.PpMoves[moveSlot].Pp >= pokemon.PpMoves[moveSlot].MaxPp) return null;
        return $"item {item.Id} {partySlot + 1} {moveSlot + 1}";
    }
}
