using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class RecentActivityEntry
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class RecentActivityPreferences
{
    public List<RecentActivityEntry> Entries { get; set; } = [];
    public List<string> SeenKeys { get; set; } = [];
}

public static class RecentActivity
{
    public const int MaximumEntries = 200;
    public const int MaximumSeenKeys = 800;

    private static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 240 && !key.Any(char.IsControl);
    private static bool Valid(RecentActivityEntry? entry) => entry is not null && ValidKey(entry.Key)
        && entry.Kind is "pokemon" or "evolution" or "training" or "battle" or "reward" or "milestone"
        && !string.IsNullOrWhiteSpace(entry.Summary) && entry.Summary.Length <= 240 && entry.Detail is { Length: <= 600 }
        && entry.RecordedAt != default;

    public static RecentActivityPreferences Preferences(IDictionary<string, RecentActivityPreferences> all, string owner)
    {
        if (!all.TryGetValue(owner, out var preferences) || preferences is null) all[owner] = preferences = new();
        preferences.Entries = (preferences.Entries ?? []).Where(Valid).DistinctBy(entry => entry.Key).Take(MaximumEntries).ToList();
        preferences.SeenKeys = preferences.Entries.Select(entry => entry.Key).Concat(preferences.SeenKeys ?? [])
            .Where(ValidKey).Distinct().Take(MaximumSeenKeys).ToList();
        return preferences;
    }

    public static bool Record(RecentActivityPreferences preferences, IEnumerable<RecentActivityEntry> entries)
    {
        var seen = preferences.SeenKeys.Concat(preferences.Entries.Select(entry => entry.Key)).ToHashSet();
        var changed = false;
        foreach (var entry in entries.Where(Valid))
        {
            if (!seen.Add(entry.Key)) continue;
            preferences.Entries.Insert(0, entry);
            preferences.SeenKeys.Insert(0, entry.Key);
            changed = true;
        }
        if (preferences.Entries.Count > MaximumEntries) preferences.Entries.RemoveRange(MaximumEntries, preferences.Entries.Count - MaximumEntries);
        if (preferences.SeenKeys.Count > MaximumSeenKeys) preferences.SeenKeys.RemoveRange(MaximumSeenKeys, preferences.SeenKeys.Count - MaximumSeenKeys);
        return changed;
    }

    public static RecentActivityEntry Entry(string key, string kind, string summary, string detail, DateTimeOffset now) => new()
    {
        Key = key, Kind = kind, Summary = Clean(summary, 240), Detail = Clean(detail, 600), RecordedAt = now
    };

    private static string Clean(string value, int maximum) => new string(value.Where(character => !char.IsControl(character)).Take(maximum).ToArray()).Trim();
    public static string PokemonName(PokemonDto pokemon) => (pokemon.Shiny ? "shiny " : "") + pokemon.Appearance;

    private static RecentActivityEntry Acquired(PokemonDto pokemon, DateTimeOffset now)
    {
        var caught = (pokemon.Origin?.Method ?? pokemon.Source) is "wild" or "paradox-research" or "raid-den"
            or "legendary-sanctuary" or "bloodmoon-hunt" or "recurring-event";
        return Entry("pokemon:" + pokemon.Id, "pokemon", $"{(caught ? "Caught" : "Received")} {PokemonName(pokemon)}",
            $"Lv. {pokemon.Level}" + (string.IsNullOrWhiteSpace(pokemon.Origin?.Location) ? "" : " / " + pokemon.Origin.Location), now);
    }

    public static IReadOnlyList<RecentActivityEntry> TrainerChanges(TrainerSnapshotDto? previous, TrainerSnapshotDto next, DateTimeOffset now)
    {
        if (previous is null || string.IsNullOrEmpty(next.User.Id) || previous.User.Id != next.User.Id) return [];
        var entries = new List<RecentActivityEntry>();
        var owned = previous.Party.Concat(previous.Storage).Where(pokemon => pokemon.Id.Length > 0).DistinctBy(pokemon => pokemon.Id).ToDictionary(pokemon => pokemon.Id);
        foreach (var pokemon in next.Party.Concat(next.Storage).Where(pokemon => pokemon.Id.Length > 0).DistinctBy(pokemon => pokemon.Id))
        {
            if (!owned.TryGetValue(pokemon.Id, out var before)) entries.Add(Acquired(pokemon, now));
            else if (before.Species != pokemon.Species)
                entries.Add(Entry($"evolution:{pokemon.Id}:{pokemon.Appearance}", "evolution", $"Evolved {before.Appearance} into {pokemon.Appearance}",
                    (pokemon.Shiny ? "Shiny / " : "") + $"Lv. {pokemon.Level}", now));
        }
        foreach (var badge in next.Pokedex.Badges.Except(previous.Pokedex.Badges))
            entries.Add(Entry("badge:" + badge, "milestone", "Earned " + badge, "Trainer badge", now));
        var goals = previous.CollectionPoints.Objectives.DistinctBy(goal => goal.Id).ToDictionary(goal => goal.Id, goal => goal.Completed);
        foreach (var goal in next.CollectionPoints.Objectives.Where(goal => goal.Completed && goals.TryGetValue(goal.Id, out var completed) && !completed))
            entries.Add(Entry("goal:" + goal.Id, "milestone", "Completed " + goal.Name, $"+{goal.Points:N0} CP", now));
        var achievements = previous.Frontier.Achievements.DistinctBy(goal => goal.Id).ToDictionary(goal => goal.Id, goal => goal.Completed);
        foreach (var goal in next.Frontier.Achievements.Where(goal => goal.Completed && achievements.TryGetValue(goal.Id, out var completed) && !completed))
            entries.Add(Entry("frontier-goal:" + goal.Id, "milestone", "Completed " + goal.Name, "Battle Frontier", now));
        return entries;
    }

    public static IReadOnlyList<RecentActivityEntry> BattleEntries(BattleSnapshotDto battle, DateTimeOffset now, bool online = false)
    {
        if (string.IsNullOrEmpty(battle.Id) || battle.Status is not ("won" or "lost" or "draw" or "caught" or "fled")) return [];
        var entries = new List<RecentActivityEntry>();
        var context = online ? "Online battle" : battle.FrontierId is not null ? "Battle Frontier"
            : battle.LeagueId is not null ? "Pokémon League" : battle.Kind == "wild" ? "Wild battle" : "Trainer battle";
        if (battle.Status != "caught")
        {
            var outcome = battle.Status switch { "won" => "Won against", "lost" => "Lost against", "draw" => "Drew against", _ => "Fled from" };
            var rewards = battle.Status == "won" && battle.Result is { } result
                ? (result.Currency > 0 ? $" / +¥ {result.Currency:N0}" : "") + (result.BattlePoints > 0 ? $" / +{result.BattlePoints:N0} BP" : "") : "";
            entries.Add(Entry("battle:" + battle.Id, "battle", $"{outcome} {battle.OpponentName}", $"{context} / {battle.Turn} turns{rewards}", now));
        }
        if (battle.Result?.Capture is { PokemonId.Length: > 0 } capture)
            entries.Add(Entry("pokemon:" + capture.PokemonId, "pokemon", $"Caught {(capture.Shiny ? "shiny " : "")}{capture.Species}",
                $"Lv. {capture.Level} / {context}", now));
        return entries;
    }

    public static IReadOnlyList<RecentActivityEntry> RaidEntries(RaidRoomDto room, DateTimeOffset now)
    {
        if (room.Id.Length == 0 || room.Status is not ("won" or "lost")) return [];
        var entries = new List<RecentActivityEntry> { Entry("raid:" + room.Id, "battle",
            $"{(room.Status == "won" ? "Cleared" : "Lost")} {room.Den.Species} den", $"Cooperative raid / {room.Den.Stars} stars", now) };
        if (room.Receipt is { } receipt && !room.RewardsPending && (receipt.Currency > 0 || receipt.Experience > 0 || receipt.Items.Any(item => item.Quantity > 0)))
            entries.Add(Entry("raid-reward:" + room.Id, "reward", "Received raid rewards",
                $"{room.Den.Species} / +¥ {receipt.Currency:N0} / +{receipt.Experience:N0} EXP"
                    + (receipt.Items.Count > 0 ? " / " + string.Join(", ", receipt.Items.Select(item => $"{item.Quantity} {item.ItemId}")) : ""), now));
        if (room.CaughtId is { Length: > 0 } id && room.CapturePokemon is { } pokemon)
            entries.Add(Entry("pokemon:" + id, "pokemon", "Caught " + PokemonName(pokemon), $"Lv. {pokemon.Level} / Cooperative raid", now));
        return entries;
    }
}
