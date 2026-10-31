using System;
using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class CoopRaidProgressDto
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int BestStars { get; set; }
}
public sealed class RaidDenDto
{
    public string Key { get; set; } = "";
    public string Species { get; set; } = "";
    public string Theme { get; set; } = "";
    public string TeraType { get; set; } = "";
    public int Stars { get; set; }
    public int Level { get; set; }
    public int Currency { get; set; }
    public int Experience { get; set; }
    public string Medicine { get; set; } = "";
    public string Ball { get; set; } = "";
    public string LockReason { get; set; } = "";
    public bool RewardClaimed { get; set; }
    public DateTimeOffset RotationEndsAt { get; set; }
}
public sealed class RaidListingDto
{
    public string Id { get; set; } = "";
    public string DenKey { get; set; } = "";
    public string HostName { get; set; } = "";
    public int Players { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class RaidBoardDto
{
    public List<RaidDenDto> Dens { get; set; } = [];
    public List<RaidListingDto> Rooms { get; set; } = [];
    public string? ActiveRoomId { get; set; }
    public DateTimeOffset ServerTime { get; set; }
    public CoopRaidProgressDto Progress { get; set; } = new();
}
public sealed class RaidPlayerDto
{
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public string TrainerId { get; set; } = "";
    public string Species { get; set; } = "";
    public int Level { get; set; }
    public bool Shiny { get; set; }
    public bool Ready { get; set; }
    public bool Host { get; set; }
    public bool Active { get; set; }
}
public sealed class RaidMoveDto
{
    public string Move { get; set; } = "";
    public string Type { get; set; } = "";
    public int Index { get; set; }
    public int? Pp { get; set; }
    public int? Maxpp { get; set; }
    public bool Disabled { get; set; }
    public List<int> LegalTargets { get; set; } = [];
}
public sealed class RaidActorDto
{
    public string Species { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public List<string> Types { get; set; } = [];
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Level { get; set; }
    public bool Shiny { get; set; }
    public int Slot { get; set; }
    public string TrainerName { get; set; } = "";
    public bool Npc { get; set; }
    public bool Submitted { get; set; }
    public int Cheers { get; set; }
    public bool CanCheer { get; set; }
    public bool CanMega { get; set; }
    public bool CanTera { get; set; }
    public List<RaidMoveDto> Moves { get; set; } = [];
}
public sealed class RaidSnapshotDto
{
    public string Status { get; set; } = "";
    public int Turn { get; set; }
    public int Phase { get; set; }
    public int ShieldRounds { get; set; }
    public int LivesRemaining { get; set; }
    public int RoundLimit { get; set; }
    public RaidActorDto Boss { get; set; } = new();
    public List<RaidActorDto> Allies { get; set; } = [];
    public List<string> Log { get; set; } = [];
}
public sealed class RaidReceiptDto
{
    public int Currency { get; set; }
    public int Experience { get; set; }
    public string PokemonId { get; set; } = "";
    public List<InventoryEntryDto> Items { get; set; } = [];
}
public sealed class RaidRoomDto
{
    public string Id { get; set; } = "";
    public int Revision { get; set; }
    public string RoundId { get; set; } = "";
    public string Status { get; set; } = "";
    public RaidDenDto Den { get; set; } = new();
    public bool Host { get; set; }
    public bool Private { get; set; }
    public string Code { get; set; } = "";
    public int OwnSlot { get; set; }
    public bool Ready { get; set; }
    public List<RaidPlayerDto> Players { get; set; } = [];
    public PokemonDto SelectedPokemon { get; set; } = new();
    public bool CanAct { get; set; }
    public bool Submitted { get; set; }
    public bool Paused { get; set; }
    public RaidSnapshotDto? Snapshot { get; set; }
    public double SecondsRemaining { get; set; }
    public DateTimeOffset ServerTime { get; set; }
    public bool RewardClaimed { get; set; }
    public bool RepeatReward { get; set; }
    public bool RewardsPending { get; set; }
    public RaidReceiptDto? Receipt { get; set; }
    public string CaptureState { get; set; } = "unavailable";
    public PokemonDto? CapturePokemon { get; set; }
    public string? CaughtId { get; set; }
    public DateTimeOffset? CaptureExpiresAt { get; set; }
    public string? EndReason { get; set; }
}
