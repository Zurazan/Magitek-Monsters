using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public static class RegionalLeaguePresentation
{
    public static HashSet<string> Clears(TrainerSnapshotDto trainer) =>
        trainer.PveProgress.DefeatedOpponentIds.Concat(trainer.PveProgress.LegacyLeagueCredits).ToHashSet(StringComparer.Ordinal);

    public static RegionalMilestoneDto[] Milestones(RegionalLeagueDto region, RegionalCircuitDto circuit) =>
        circuit.MilestoneIds.Select(id => region.Milestones.FirstOrDefault(slot => slot.Id == id)).OfType<RegionalMilestoneDto>().ToArray();

    public static bool Earned(RegionalMilestoneDto slot, ISet<string> clears) => slot.LeaderIds.Any(clears.Contains);

    public static string? CircuitLock(RegionalLeagueDto region, RegionalCircuitDto circuit, ISet<string> clears,
        IReadOnlyList<RegionalLeagueDto> regions, IReadOnlyList<OpponentDto> opponents)
    {
        var missing = region.RequiredOpponentIds.Where(id => !clears.Contains(id)).ToArray();
        if (missing.Length > 0) return "Defeat " + string.Join(", ", missing.Select(id => opponents.FirstOrDefault(entry => entry.Id == id)?.Name ?? id)) + " first.";
        if (circuit.RequiredChampionRegionId is { Length: > 0 } required && !RegionalLeagueProgress.HasChampion(clears, required))
            return "Complete the " + (regions.FirstOrDefault(region => region.Id == required)?.Name ?? required) + " League first.";
        return null;
    }

    public static string? GymLock(RegionalLeagueDto region, RegionalCircuitDto circuit, OpponentDto opponent,
        ISet<string> clears, bool rematch, IReadOnlyList<RegionalLeagueDto> regions, IReadOnlyList<OpponentDto> opponents)
    {
        var circuitLock = CircuitLock(region, circuit, clears, regions, opponents);
        if (circuitLock is not null) return circuitLock;
        var original = opponent.RematchOf ?? opponent.Id;
        if (rematch && (!clears.Contains(original) || !RegionalLeagueProgress.HasChampion(clears, region.Id)))
            return "Defeat this trainer and become this region's Champion before rematching.";
        var slots = Milestones(region, circuit);
        var index = Array.FindIndex(slots, slot => slot.Id == opponent.MilestoneId);
        if (index < 0) return "Choose a lineup containing this Gym or trial.";
        var missing = slots.Take(index).Where(slot => !Earned(slot, clears)).ToArray();
        return missing.Length == 0 ? null : "Earn " + string.Join(", ", missing.Select(slot => slot.Name)) + " first.";
    }

    public static string? LeagueLock(RegionalLeagueDto region, RegionalCircuitDto circuit, ISet<string> clears, bool rematch,
        IReadOnlyList<RegionalLeagueDto> regions, IReadOnlyList<OpponentDto> opponents)
    {
        var circuitLock = CircuitLock(region, circuit, clears, regions, opponents);
        if (circuitLock is not null) return circuitLock;
        var slots = Milestones(region, circuit);
        if (slots.Length != circuit.MilestoneIds.Count) return "The regional catalog is still synchronizing.";
        var missing = slots.Where(slot => !Earned(slot, clears)).ToArray();
        if (missing.Length > 0) return "Earn " + string.Join(", ", missing.Select(slot => slot.Name)) + " before entering this League.";
        if (rematch && !clears.Contains(circuit.OpponentIds.LastOrDefault() ?? ""))
            return "Complete this League lineup before starting its level-100 rematch.";
        return null;
    }

    public static OpponentDto[] Stages(IReadOnlyList<OpponentDto> opponents, RegionalCircuitDto circuit, bool rematch)
    {
        var stages = circuit.OpponentIds.Select(id => opponents.FirstOrDefault(opponent => opponent.Id == (rematch ? "rematch-" + id : id))).ToArray();
        return stages.Any(stage => stage is null) ? [] : stages.OfType<OpponentDto>().ToArray();
    }
}
