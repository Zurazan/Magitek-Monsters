using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class FrontierProgressDto
{
    public int Balance { get; set; }
    public int TotalEarned { get; set; }
    public int LifetimeWins { get; set; }
    public int RentalWins { get; set; }
    public int OwnedWins { get; set; }
    public int BestAccessible { get; set; }
    public int BestStandard { get; set; }
    public bool RentalUnlocked { get; set; }
    public bool OwnedUnlocked { get; set; }
    public bool StandardUnlocked { get; set; }
    public string? NextParadoxAt { get; set; }
    public List<FrontierAchievementDto> Achievements { get; set; } = [];
    public List<FrontierRewardDto> Rewards { get; set; } = [];
}
public sealed class FrontierRunDto
{
    public string Id { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Bracket { get; set; } = string.Empty;
    public string TeamMode { get; set; } = string.Empty;
    public string Facility { get; set; } = "tower";
    public string Phase { get; set; } = string.Empty;
    public int Streak { get; set; }
    public string BattleId { get; set; } = string.Empty;
    public List<FrontierMemberDto> Team { get; set; } = [];
    public List<FrontierMemberDto> Offers { get; set; } = [];
}
public sealed class FrontierMemberDto
{
    public string Id { get; set; } = string.Empty;
    public PokemonDto Pokemon { get; set; } = new();
}
public sealed class FrontierAchievementDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Target { get; set; }
    public int Progress { get; set; }
    public int Bonus { get; set; }
    public bool Completed { get; set; }
    public string? EarnedAt { get; set; }
}
public sealed class FrontierRewardDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Species { get; set; }
    public string? Era { get; set; }
    public string? ItemId { get; set; }
    public int Price { get; set; }
    public int CopyPrice { get; set; }
    public int CurrentPrice { get; set; }
    public int Purchased { get; set; }
    public int? LifetimeWins { get; set; }
    public string? AchievementId { get; set; }
    public int Progress { get; set; }
    public bool Unlocked { get; set; }
}
public sealed class FrontierCatalogDto
{
    public List<FrontierRentalDto> Rentals { get; set; } = [];
}
public sealed class FrontierRentalDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<PokemonDto> Team { get; set; } = [];
    public List<PokemonDto> StandardTeam { get; set; } = [];
}
