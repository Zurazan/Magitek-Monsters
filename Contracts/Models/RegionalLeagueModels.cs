using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class RegionalLeagueDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = "standard";
    public List<string> RequiredOpponentIds { get; set; } = [];
    public List<RegionalMilestoneDto> Milestones { get; set; } = [];
    public List<RegionalCircuitDto> Circuits { get; set; } = [];
}

public sealed class RegionalMilestoneDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "badge";
    public List<string> LeaderIds { get; set; } = [];
}

public sealed class RegionalCircuitDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RegionId { get; set; } = string.Empty;
    public string? RequiredChampionRegionId { get; set; }
    public List<string> MilestoneIds { get; set; } = [];
    public List<string> PreferredLeaderIds { get; set; } = [];
    public List<string> OpponentIds { get; set; } = [];
}
