using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class CollectionPointProgressDto
{
    public int Balance { get; set; }
    public int TotalEarned { get; set; }
    public int RegisteredSpecies { get; set; }
    public int CompletedObjectives { get; set; }
    public List<string> UnlockedStarters { get; set; } = [];
    public List<CollectionObjectiveDto> Objectives { get; set; } = [];
    public List<string> ClaimedFormRewards { get; set; } = [];
}

public sealed class CollectionObjectiveDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Target { get; set; }
    public int Points { get; set; }
    public int Progress { get; set; }
    public bool Completed { get; set; }
    public string? EarnedAt { get; set; }
}

public sealed class CollectionRewardCatalogDto
{
    public List<CollectionRewardDto> Rewards { get; set; } = [];
}

public sealed class CollectionRewardDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Species { get; set; }
    public int? Generation { get; set; }
    public string? Type { get; set; }
    public int Points { get; set; }
    public int? CopyPrice { get; set; }
    public int RequiredSpecies { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> RequiredForms { get; set; } = [];
}
