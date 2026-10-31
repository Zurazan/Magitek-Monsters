using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class BattleResultDto
{
    public int Currency { get; set; }
    public int BattlePoints { get; set; }
    public int CollectionPoints { get; set; }
    public bool FirstClear { get; set; }
    public List<string> Badges { get; set; } = [];
    public List<string> Medals { get; set; } = [];
    public List<BattleResultPokemonDto> Party { get; set; } = [];
    public BattleCaptureResultDto? Capture { get; set; }
    public bool CaptureAvailable { get; set; }
}

public sealed class BattleResultPokemonDto
{
    public string PokemonId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public bool Shiny { get; set; }
    public bool Rental { get; set; }
    public int LevelBefore { get; set; }
    public int LevelAfter { get; set; }
    public int ExperienceBefore { get; set; }
    public int ExperienceAfter { get; set; }
    public List<string> LearnedMoves { get; set; } = [];
    public LeagueVitalDto? Vitals { get; set; }
}

public sealed class BattleCaptureResultDto
{
    public string PokemonId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public bool Shiny { get; set; }
    public int Level { get; set; }
    public bool NewSpecies { get; set; }
    public bool InParty { get; set; }
}
