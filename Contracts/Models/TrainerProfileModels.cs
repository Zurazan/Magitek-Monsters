using System.Collections.Generic;
using MagitekMonsters.Services;

namespace MagitekMonsters.Models;

public sealed class TrainerProfileDto
{
    public string TrainerId { get; set; } = string.Empty;
    public string? VanityCode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Color { get; set; }
    public TrainerProfilePartnerDto? Partner { get; set; }
    public List<TrainerCardBadge> Badges { get; set; } = [];
    public int RegisteredSpecies { get; set; }
    public int ShinySpecies { get; set; }
    public int TrainerWins { get; set; }
    public int FrontierWins { get; set; }
}

public sealed class TrainerProfilePartnerDto
{
    public string Species { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Shiny { get; set; }
}
