using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class CollectionSettingsDto
{
    public MagitekMonsters.Services.TrainerCardPreferences? TrainerCard { get; set; }
    public List<TeamPresetDto> TeamPresets { get; set; } = [];
    public Dictionary<string, string> BoxNames { get; set; } = [];
}

public sealed class TeamPresetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> PokemonIds { get; set; } = [];
}

public sealed class PokemonOriginDto
{
    public string Method { get; set; } = string.Empty;
    public string? Location { get; set; }
    public uint? TerritoryId { get; set; }
    public int? Level { get; set; }
    public string? EventOccurrenceId { get; set; }
}

public sealed class TextExportDto
{
    public string Text { get; set; } = string.Empty;
}

public sealed class PvpFormatCatalogDto
{
    public List<PvpFormatDto> Formats { get; set; } = [];
}

public sealed class DirectChallengeDto
{
    public string? FormatId { get; set; }
    public string FormatName { get; set; } = "Classic 3v3";
    public int MinTeamSize { get; set; } = 3;
    public int MaxTeamSize { get; set; } = 3;
    public string Id { get; set; } = string.Empty;
    public string FromUserId { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public string FromTrainerId { get; set; } = string.Empty;
    public string ToTrainerId { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public List<string> PokemonIds { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
}

public sealed class BattleHelpDto
{
    public List<MoveDetailsDto> Moves { get; set; } = [];
    public List<AbilityDetailsDto> Abilities { get; set; } = [];
}
