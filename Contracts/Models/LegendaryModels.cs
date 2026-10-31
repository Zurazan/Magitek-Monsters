using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class LegendaryProgressDto
{
    public List<string> Permits { get; set; } = [];
    public List<string> DefeatedBossIds { get; set; } = [];
    public int RequiredBosses { get; set; }
    public int CompletedBosses { get; set; }
    public List<string> MissingBossIds { get; set; } = [];
    public bool MewAvailable { get; set; }
}

public sealed class LegendaryCatalogDto
{
    public List<LegendaryBossDto> Bosses { get; set; } = [];
    public List<string> RequiredBossIds { get; set; } = [];
}

public sealed class LegendaryBossDto
{
    public RaidProgressDto? Raid { get; set; }
    public List<string> Moves { get; set; } = [];
    public string Id { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public int DexNumber { get; set; }
    public int CaptureLevel { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Price { get; set; }
    public bool Final { get; set; }
    public List<string> TeamSpecies { get; set; } = [];
    public string TacticalHint { get; set; } = string.Empty;
}

public sealed class RaidProgressDto
{
    public int Version { get; set; }
    public int HpMultiplier { get; set; }
    public int OffenseFloor { get; set; }
    public int EnrageTurn { get; set; }
    public int TurnLimit { get; set; }
    public int Phase { get; set; }
    public int CompletedTurns { get; set; }
    public int BonusActions { get; set; }
}
