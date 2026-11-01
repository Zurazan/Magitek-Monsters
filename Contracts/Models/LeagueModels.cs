using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class LeagueRunDto
{
    public string? RegionId { get; set; }
    public string? CircuitId { get; set; }
    public string? RegionName { get; set; }
    public string? CircuitName { get; set; }
    public bool Rematch { get; set; }
    public string Id { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Stage { get; set; }
    public List<string> OpponentIds { get; set; } = [];
    public string? BattleId { get; set; }
    public int CheckpointActions { get; set; }
    public List<LeagueVitalDto> Vitals { get; set; } = [];
}
public sealed class LeagueVitalDto
{
    public string PokemonId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public bool Shiny { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<LeagueMoveDto> Moves { get; set; } = [];
}
public sealed class LeagueMoveDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Pp { get; set; }
    public int MaxPp { get; set; }
}
