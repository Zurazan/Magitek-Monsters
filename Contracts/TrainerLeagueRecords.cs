using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record TrainerLeagueMilestone(RegionalMilestoneDto Definition, bool Earned);
public sealed record TrainerLeagueRecord(RegionalLeagueDto Region, IReadOnlyList<TrainerLeagueMilestone> Milestones,
    IReadOnlyList<RegionalCircuitDto> CompletedCircuits)
{
    public bool Champion => CompletedCircuits.Any(circuit => circuit.OpponentIds.Count > 1);
    public bool CompletedChallenge(bool badgesOnly = false) => Region.Circuits.Any(circuit => circuit.MilestoneIds.Count > 0
        && circuit.MilestoneIds.All(id => Milestones.Any(slot => slot.Definition.Id == id && slot.Earned
            && (!badgesOnly || slot.Definition.Kind == "badge"))));
}

public static class TrainerLeagueRecords
{
    private static readonly Dictionary<string, string> LegacyBadges = new(StringComparer.Ordinal)
    {
        ["Verdant Badge"] = "kanto:boulder", ["Forge Badge"] = "kanto:cascade", ["Tidal Badge"] = "kanto:thunder",
        ["Tempest Badge"] = "kanto:rainbow", ["Umbral Badge"] = "kanto:soul", ["Iron Badge"] = "kanto:marsh",
        ["Wyrm Badge"] = "kanto:volcano", ["Prismatic Badge"] = "kanto:earth"
    };

    public static IReadOnlyList<TrainerLeagueRecord> Resolve(TrainerSnapshotDto trainer, IReadOnlyList<RegionalLeagueDto> regions)
    {
        var clears = trainer.PveProgress.DefeatedOpponentIds.Concat(trainer.PveProgress.LegacyLeagueCredits).ToHashSet(StringComparer.Ordinal);
        var badges = trainer.Pokedex.Badges.ToHashSet(StringComparer.Ordinal);
        var uniqueNames = regions.SelectMany(region => region.Milestones).GroupBy(slot => slot.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var legacy = badges.Where(LegacyBadges.ContainsKey).Select(name => LegacyBadges[name]).ToHashSet(StringComparer.Ordinal);
        return regions.Select(region => new TrainerLeagueRecord(region,
            region.Milestones.Select(slot => new TrainerLeagueMilestone(slot, slot.LeaderIds.Any(clears.Contains)
                || badges.Contains(region.Name + " / " + slot.Name) || uniqueNames.Contains(slot.Name) && badges.Contains(slot.Name)
                || legacy.Contains(slot.Id))).ToArray(),
            region.Circuits.Where(circuit => circuit.OpponentIds.Count > 0 && clears.Contains(circuit.OpponentIds[^1])).ToArray())).ToArray();
    }

    public static string BadgeId(string id, IReadOnlyList<RegionalLeagueDto> regions)
    {
        if (!id.StartsWith("gym:", StringComparison.Ordinal)) return id;
        var name = id[4..];
        if (LegacyBadges.TryGetValue(name, out var legacy)) return "league:" + legacy;
        var matches = regions.SelectMany(region => region.Milestones.Where(slot => name == region.Name + " / " + slot.Name || name == slot.Name))
            .Select(slot => "league:" + slot.Id).Distinct(StringComparer.Ordinal).ToArray();
        return matches.Length == 1 ? matches[0] : id;
    }
}
