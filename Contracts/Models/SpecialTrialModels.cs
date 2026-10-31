using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class SpecialTrialProgressDto
{
    public List<string> DefeatedTrialIds { get; set; } = [];
    public List<string> MissingBloodmoonForms { get; set; } = [];
    public bool BattleBondUnlocked { get; set; }
}

public sealed class SpecialTrialCatalogDto
{
    public List<SpecialTrialDto> Trials { get; set; } = [];
}

public sealed class SpecialTrialDto
{
    public string Id { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TacticalHint { get; set; } = string.Empty;
    public string LeadSpecies { get; set; } = string.Empty;
    public List<string> RequiredForms { get; set; } = [];
    public List<string> TeamSpecies { get; set; } = [];
    public int RecommendedLevel { get; set; }
    public int? BagLimit { get; set; }
    public RaidProgressDto? Raid { get; set; }
}
