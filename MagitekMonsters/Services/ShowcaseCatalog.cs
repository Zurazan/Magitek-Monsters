using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record ShowcaseBadge(string Id, string Name, string Category, string Source, string Requirement,
    int Progress, int Target, bool Unlocked)
{
    public string ProgressLabel => Target <= 1 ? Unlocked ? "Completed" : "Not completed" : $"{Progress:N0} / {Target:N0}";
}

public sealed class ShowcaseCatalog
{
    public static readonly string[] Categories = ["All badges", "Gym badges", "Island challenges", "Champions", "Frontier", "Collection", "Journey"];
    private TrainerSnapshotDto? owner;
    private IReadOnlyList<RegionalLeagueDto>? regions;
    private IReadOnlyList<OpponentDto>? opponents;
    private IReadOnlyList<ShowcaseBadge> entries = [];
    private IReadOnlyList<ShowcaseBadge>? filtered;
    private string query = "", category = "";
    private int availability;
    public int Unlocked { get; private set; }
    public IReadOnlyList<ShowcaseBadge> Entries => entries;
    public void Clear()
    {
        owner = null; regions = null; opponents = null; entries = []; filtered = null; Unlocked = 0;
    }
    public IReadOnlyList<ShowcaseBadge> Read(TrainerSnapshotDto trainer, IReadOnlyList<RegionalLeagueDto> leagues, IReadOnlyList<OpponentDto> trainers)
    {
        if (ReferenceEquals(owner, trainer) && ReferenceEquals(regions, leagues) && ReferenceEquals(opponents, trainers)) return entries;
        owner = trainer; regions = leagues; opponents = trainers; filtered = null;
        var earned = TrainerCard.Resolve(trainer, new(), leagues).EarnedBadges.Select(badge => badge.Id).ToHashSet(StringComparer.Ordinal);
        var names = trainers.DistinctBy(opponent => opponent.Id).ToDictionary(opponent => opponent.Id, opponent => opponent.Name, StringComparer.Ordinal);
        var result = new List<ShowcaseBadge>();
        foreach (var league in TrainerLeagueRecords.Resolve(trainer, leagues))
        {
            foreach (var milestone in league.Milestones)
            {
                var slot = milestone.Definition; var id = "league:" + slot.Id;
                var leaders = slot.LeaderIds.Where(names.ContainsKey).Select(key => names[key]).Distinct(StringComparer.Ordinal).ToArray();
                var requirement = leaders.Length > 0 ? "Defeat " + string.Join(" or ", leaders) + " in a " + league.Region.Name + " trainer circuit."
                    : "Complete the " + slot.Name + " challenge in " + league.Region.Name + ".";
                result.Add(new(id, slot.Name, slot.Kind == "badge" ? "Gym badges" : "Island challenges", "PvE League / " + league.Region.Name,
                    requirement, earned.Contains(id) ? 1 : 0, 1, earned.Contains(id)));
            }
            var champion = "champion:" + league.Region.Id;
            result.Add(new(champion, league.Region.Name + " Champion", "Champions", "PvE League / " + league.Region.Name,
                "Complete a full " + league.Region.Name + " League lineup. Exhibition battles do not count.", earned.Contains(champion) ? 1 : 0, 1, earned.Contains(champion)));
        }
        foreach (var achievement in trainer.Frontier.Achievements)
        {
            var id = "frontier:" + achievement.Id;
            var (source, requirement) = achievement.Id switch
            {
                var value when value.StartsWith("accessible-", StringComparison.Ordinal) => ("Battle Frontier / Accessible", $"Reach a {achievement.Target:N0}-win streak in the Accessible bracket."),
                var value when value.StartsWith("standard-", StringComparison.Ordinal) => ("Battle Frontier / Standard", $"Reach a {achievement.Target:N0}-win streak in the Standard bracket."),
                var value when value.StartsWith("lifetime-", StringComparison.Ordinal) => ("Battle Frontier / All battles", $"Win {achievement.Target:N0} Frontier battles in total. These wins do not need to be consecutive."),
                var value when value.StartsWith("rental-", StringComparison.Ordinal) => ("Battle Frontier / Rental teams", $"Win {achievement.Target:N0} Frontier battles with rental teams."),
                var value when value.StartsWith("owned-", StringComparison.Ordinal) => ("Battle Frontier / Owned teams", $"Win {achievement.Target:N0} Frontier battles with your own Pokémon."),
                _ => ("Battle Frontier", achievement.Name + ".")
            };
            result.Add(new(id, achievement.Name, "Frontier", source, requirement, Math.Clamp(achievement.Progress, 0, Math.Max(1, achievement.Target)),
                Math.Max(1, achievement.Target), earned.Contains(id)));
        }
        foreach (var objective in trainer.CollectionPoints.Objectives)
        {
            var id = "collection:" + objective.Id;
            result.Add(new(id, objective.Name, "Collection", "Collection goals / " + objective.Category,
                objective.Name.TrimEnd('.') + ".", Math.Clamp(objective.Progress, 0, Math.Max(1, objective.Target)), Math.Max(1, objective.Target), earned.Contains(id)));
        }
        result.Add(new("partner", "First partner", "Journey", "First-time setup", "Choose your first partner Pokémon.", trainer.StarterChosen ? 1 : 0, 1, earned.Contains("partner")));
        entries = result.DistinctBy(badge => badge.Id).ToArray();
        Unlocked = entries.Count(badge => badge.Unlocked);
        return entries;
    }
    public IReadOnlyList<ShowcaseBadge> Filter(string search, string group, int state)
    {
        search = search.Trim();
        if (filtered is not null && query == search && category == group && availability == state) return filtered;
        query = search; category = group; availability = state;
        return filtered = entries.Where(badge => (group == Categories[0] || badge.Category == group)
            && (state == 0 || badge.Unlocked == (state == 1))
            && (search.Length == 0 || badge.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || badge.Source.Contains(search, StringComparison.OrdinalIgnoreCase) || badge.Requirement.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
