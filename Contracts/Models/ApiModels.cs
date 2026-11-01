using System;
using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class SessionDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
    public UserDto User { get; set; } = new();
}

public sealed class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string TrainerId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CharacterName { get; set; }
    public string? HomeWorld { get; set; }
}

public sealed class TrainerSnapshotDto
{
    public GiftInboxDto Gifts { get; set; } = new();
    public string? ActiveRaidId { get; set; }
    public bool RaidLocked { get; set; }
    public CoopRaidProgressDto RaidProgress { get; set; } = new();
    public SearchCooldownDto? SearchCooldown { get; set; }
    public SpecialTrialProgressDto SpecialTrials { get; set; } = new();
    public LegendaryProgressDto Legendary { get; set; } = new();
    public FrontierProgressDto Frontier { get; set; } = new();
    public FrontierRunDto? FrontierRun { get; set; }
    public BattleSnapshotDto? ActiveTrainerBattle { get; set; }
    public LeagueRunDto? LeagueRun { get; set; }
    public UserDto User { get; set; } = new();
    public int Currency { get; set; }
    public bool StarterChosen { get; set; }
    public List<PokemonDto> Party { get; set; } = [];
    public List<PokemonDto> Storage { get; set; } = [];
    public List<InventoryEntryDto> Inventory { get; set; } = [];
    public EncounterDto? ActiveEncounter { get; set; }
    public List<PendingMoveDto> PendingMoves { get; set; } = [];
    public List<EvolutionOptionDto> EvolutionOptions { get; set; } = [];
    public List<FormOptionDto> FormOptions { get; set; } = [];
    public List<FusionOptionDto> FusionOptions { get; set; } = [];
    public PokedexDto Pokedex { get; set; } = new();
    public PveProgressDto PveProgress { get; set; } = new();
    public PvpProfileDto PvpProfile { get; set; } = new();
    public CollectionSettingsDto Collection { get; set; } = new();
    public CollectionPointProgressDto CollectionPoints { get; set; } = new();
}

public sealed class SearchCooldownDto
{
    public double RemainingSeconds { get; set; }
}

public sealed class PveProgressDto
{
    public List<string> LegacyLeagueCredits { get; set; } = [];
    public int TotalAttempts { get; set; }
    public int TotalLosses { get; set; }
    public List<PveMedalDto> Medals { get; set; } = [];
    public List<string> DefeatedOpponentIds { get; set; } = [];
    public int TotalWins { get; set; }
}

public sealed class PveMedalDto
{
    public string OpponentId { get; set; } = string.Empty;
    public string MedalId { get; set; } = string.Empty;
    public string EarnedAt { get; set; } = string.Empty;
}

public sealed class PveRulesDto
{
    public string Challenge { get; set; } = "standard";
    public string? MedalId { get; set; }
    public string Label { get; set; } = "Standard";
    public int? BagLimit { get; set; }
    public int? LevelCap { get; set; }
    public int PartyLimit { get; set; } = 6;
    public string? Monotype { get; set; }
}

public sealed class PvpProfileDto
{
    public string SeasonId { get; set; } = string.Empty;
    public PvpSeasonDto Season { get; set; } = new();
    public string Rank { get; set; } = "Z";
    public int RankIndex { get; set; }
    public int RankPoints { get; set; }
    public int CurrentRankStart { get; set; }
    public int? NextRankAt { get; set; }
    public int RankedWins { get; set; }
    public int RankedLosses { get; set; }
    public int RankedDraws { get; set; }
    public int CasualWins { get; set; }
    public int CasualLosses { get; set; }
    public int CasualDraws { get; set; }
    public RankTokensDto? RankTokens { get; set; }
    public DateTimeOffset? RankedCooldownUntil { get; set; }
}

public sealed class RankTokensDto
{
    public long Balance { get; set; }
    public long TotalEarned { get; set; }
    public long SeasonPaid { get; set; }
    public long SeasonEntitlement { get; set; }
    public long SeasonRemaining { get; set; }
    public long NextPromotionTokens { get; set; }
    public List<RankTokenSeasonDto> History { get; set; } = [];
}

public sealed class RankTokenSeasonDto
{
    public string SeasonId { get; set; } = string.Empty;
    public string SeasonName { get; set; } = string.Empty;
    public string Rank { get; set; } = "Z";
    public long TotalTokens { get; set; }
    public string CompletedAt { get; set; } = string.Empty;
}

public sealed class PvpSeasonDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Preseason";
    public string Description { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string? EndsAt { get; set; }
    public int WinPoints { get; set; } = 100;
    public int DrawPoints { get; set; } = 35;
    public int LossPoints { get; set; } = 10;
    public int BaseAdvancePoints { get; set; } = 200;
    public int AdvanceGrowth { get; set; } = 20;
    public int TokenRewardPerRank { get; set; }
    public int PromotionRewardPercent { get; set; }
    public List<PvpFormatDto> Formats { get; set; } = [];
    public string RulesVersion { get; set; } = string.Empty;
    public bool FormatsAvailable { get; set; }
}

public sealed class PvpFormatDto
{
    public string GameType { get; set; } = "singles";
    public bool OpenTeamSheets { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinTeamSize { get; set; }
    public int MaxTeamSize { get; set; }
    public int? PickedTeamSize { get; set; }
    public int? AdjustLevel { get; set; }
    public int? AdjustLevelDown { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public List<string> Rules { get; set; } = [];
}

public sealed class PvpStatusDto
{
    public string State { get; set; } = "idle";
    public string? QueuedMode { get; set; }
    public string? QueuedFormatId { get; set; }
    public BattleSnapshotDto? Battle { get; set; }
    public PvpProfileDto Profile { get; set; } = new();
    public List<string> Ranks { get; set; } = [];
    public List<DirectChallengeDto> Challenges { get; set; } = [];
}

public sealed class EvolutionOptionDto
{
    public List<EvolutionEnvironmentCheckDto> EnvironmentChecks { get; set; } = [];
    public List<EvolutionEnvironmentRouteDto> EnvironmentAlternatives { get; set; } = [];
    public string PokemonId { get; set; } = string.Empty;
    public string TargetSpecies { get; set; } = string.Empty;
    public string TargetAppearance { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int? RequiredLevel { get; set; }
    public string? RequiredItemId { get; set; }
    public string? RequiredHeldItemId { get; set; }
    public string? RequiredTime { get; set; }
    public string? RequiredWeather { get; set; }
    public bool? ReadyWithoutEnvironment { get; set; }
    public string Requirement { get; set; } = string.Empty;
    public bool Ready { get; set; }
}

public sealed class EvolutionEnvironmentCheckDto
{
    public string Kind { get; set; } = string.Empty;
    public List<string> Values { get; set; } = [];
    public bool Negate { get; set; }
}

public sealed class EvolutionEnvironmentRouteDto
{
    public string? RequiredTime { get; set; }
    public string? RequiredWeather { get; set; }
    public List<EvolutionEnvironmentCheckDto> EnvironmentChecks { get; set; } = [];
}

public sealed class EvolutionProgressDto
{
    public string AlcremieDecoration { get; set; } = string.Empty;
    public int Friendship { get; set; }
    public double Travelled { get; set; }
    public int Coins { get; set; }
}

public sealed class PendingMoveDto
{
    public string Id { get; set; } = string.Empty;
    public string PokemonId { get; set; } = string.Empty;
    public string PokemonName { get; set; } = string.Empty;
    public string Move { get; set; } = string.Empty;
    public int Level { get; set; }
}

public sealed class PokedexDto
{
    public int Caught { get; set; }
    public int ShinyCaught { get; set; }
    public int Total { get; set; }
    public List<string> CaughtSpecies { get; set; } = [];
    public List<string> CaughtForms { get; set; } = [];
    public List<string> Badges { get; set; } = [];
}

public sealed class PokemonDto
{
    public LeagueVitalDto? Vitals { get; set; }
    public string FusionHostId { get; set; } = string.Empty;
    public FusionOptionDto? Fusion { get; set; }
    public EvolutionProgressDto? EvolutionProgress { get; set; }
    public bool Favorite { get; set; }
    public List<string> Types { get; set; } = [];
    public int Generation { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public PokemonOriginDto? Origin { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySpecies => string.IsNullOrWhiteSpace(Form) ? Species : $"{Species}-{Form}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Appearance => Species == "Alcremie"
        ? $"Alcremie-{(Form.Length == 0 ? "Vanilla-Cream" : Form)}-{AlcremieDecoration}"
        : DisplaySpecies;
    [System.Text.Json.Serialization.JsonIgnore]
    public string AlcremieDecoration
    {
        get
        {
            var decoration = EvolutionProgress?.AlcremieDecoration ?? "";
            return decoration is "love" or "berry" or "clover" or "flower" or "star" or "ribbon" or "strawberry"
                ? char.ToUpperInvariant(decoration[0]) + decoration[1..] : "Strawberry";
        }
    }
    public string Nickname { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Experience { get; set; }
    public bool Shiny { get; set; }
    public string Nature { get; set; } = string.Empty;
    public string MintNature { get; set; } = string.Empty;
    public string TeraType { get; set; } = string.Empty;
    public string Ability { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public List<string> Moves { get; set; } = [];
    public string HeldItem { get; set; } = string.Empty;
    public string Pokeball { get; set; } = string.Empty;
    public Dictionary<string, int> Ivs { get; set; } = [];
    public Dictionary<string, int> Evs { get; set; } = [];
    public int? PartySlot { get; set; }
    public int BoxNumber { get; set; }
    public int BoxSlot { get; set; }
    public List<string> RelearnableMoves { get; set; } = [];
}

public sealed class PokedexCatalogDto
{
    public List<PokedexEntryDto> Pokemon { get; set; } = [];
}

public sealed class PokemonBuildDto
{
    public List<string> Types { get; set; } = [];
    public string TeraType { get; set; } = string.Empty;
    public List<string> AllowedTeraTypes { get; set; } = [];
    public Dictionary<string, int> Stats { get; set; } = [];
    public Dictionary<string, int> Level50Stats { get; set; } = [];
    public string EffectiveNature { get; set; } = string.Empty;
    public string NatureEffect { get; set; } = string.Empty;
    public string AbilityDescription { get; set; } = string.Empty;
    public List<AbilityDetailsDto> Abilities { get; set; } = [];
    public bool CanUseCapsule { get; set; }
    public string? PatchAbility { get; set; }
    public List<MoveDetailsDto> Moves { get; set; } = [];
    public List<string> TeachableItemIds { get; set; } = [];
}

public sealed class AbilityDetailsDto
{
    public string Name { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class MoveDetailsDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Power { get; set; }
    public int? Accuracy { get; set; }
    public int Pp { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class TrainingEffectDto
{
    public string Kind { get; set; } = string.Empty;
    public string? Stat { get; set; }
    public int Amount { get; set; }
    public string? Nature { get; set; }
}

public sealed class PokedexEntryDto
{
    public int DexNumber { get; set; }
    public string Species { get; set; } = string.Empty;
    public bool Registered { get; set; }
    public List<string> Methods { get; set; } = [];
    public List<string> WildLocations { get; set; } = [];
    public List<string> Forms { get; set; } = [];
}

public sealed class InventoryEntryDto
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class EncounterDto
{
    public string Id { get; set; } = string.Empty;
    public string HabitatId { get; set; } = string.Empty;
    public uint TerritoryId { get; set; }
    public string? TargetSpecies { get; set; }
    public PokemonDto Pokemon { get; set; } = new();
    public double CaptureDifficulty { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string ExpiresAt { get; set; } = string.Empty;
}

public sealed class CaptureResultDto
{
    public bool Caught { get; set; }
    public PokemonDto? Pokemon { get; set; }
    public int BallsRemaining { get; set; }
    public EncounterDto Encounter { get; set; } = new();
}

public sealed class StarterCatalogDto
{
    public List<StarterDto> Starters { get; set; } = [];
}

public sealed class StarterDto
{
    public string Species { get; set; } = string.Empty;
    public int Generation { get; set; }
    public string Type { get; set; } = string.Empty;
}

public sealed class HabitatDto
{
    public bool Catalogued { get; set; } = true;
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public string ThemeId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Species { get; set; } = [];
}

public sealed class ShopCatalogDto
{
    public List<ShopItemDto> Items { get; set; } = [];
    public List<ShopItemDto> TrainingItems { get; set; } = [];
    public List<ShopItemDto> InventoryItems { get; set; } = [];
}

public sealed class ShopItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Price { get; set; }
    public string Currency { get; set; } = "yen";
    public bool BpOnly { get; set; }
    public int? BpPrice { get; set; }
    public string AcquisitionReference { get; set; } = string.Empty;
    public string AcquisitionNote { get; set; } = string.Empty;
    public bool GuaranteedCapture { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool CanBeHeld { get; set; }
    public double? CatchMultiplier { get; set; }
    public int? MaxOwned { get; set; }
    public string? BattleEffect { get; set; }
    public int? HealAmount { get; set; }
    public TrainingEffectDto? TrainingEffect { get; set; }
    public string? Move { get; set; }
    public string? MoveSource { get; set; }
    public string? IconId { get; set; }
}

public sealed class PrizePokemonCatalogDto
{
    public List<PrizePokemonDto> Pokemon { get; set; } = [];
}

public sealed class PrizePokemonDto
{
    public string Id { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Price { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class OpponentCatalogDto
{
    public List<RegionalLeagueDto> Regions { get; set; } = [];
    public List<OpponentDto> Opponents { get; set; } = [];
}

public sealed class OpponentDto
{
    public string? RegionId { get; set; }
    public string? MilestoneId { get; set; }
    public string? RegionalRole { get; set; }
    public string Specialty { get; set; } = string.Empty;
    public string Ace { get; set; } = string.Empty;
    public bool Legacy { get; set; }
    public string? RematchOf { get; set; }
    public string TacticalHint { get; set; } = string.Empty;
    public int? BagLimit { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TrainerClass { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int RecommendedLevel { get; set; }
    public int Difficulty { get; set; }
    public string Ai { get; set; } = string.Empty;
    public int DefaultIvs { get; set; }
    public int RewardCurrency { get; set; }
    public int RewardExperience { get; set; }
    public int TeamSize { get; set; }
    public List<string> TeamSpecies { get; set; } = [];
    public List<string> RequiredOpponentIds { get; set; } = [];
    public string? Badge { get; set; }
}

public sealed class AuthorizationFlowDto
{
    public string LoginId { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
}

public sealed class AuthorizationPollDto
{
    public string Status { get; set; } = string.Empty;
    public int RetryAfterSeconds { get; set; }
    public string? Message { get; set; }
    public SessionDto? Session { get; set; }
}

public sealed class BattleSnapshotDto
{
    public BattleResultDto? Result { get; set; }
    public int DefeatPenaltyCap { get; set; }
    public int CurrencyLost { get; set; }
    public List<LeagueVitalDto> PartyVitals { get; set; } = [];
    public string GameType { get; set; } = "singles";
    public List<BattleCombatantDto?> PlayerActives { get; set; } = [];
    public List<BattleCombatantDto?> OpponentActives { get; set; } = [];
    public List<BattleTeamSheetDto> OpponentTeamSheet { get; set; } = [];
    public RaidProgressDto? Raid { get; set; }
    public string? FormatId { get; set; }
    public string? FormatName { get; set; }
    public string? FrontierId { get; set; }
    public int RewardBP { get; set; }
    public string? LeagueId { get; set; }
    public bool Friendly { get; set; }
    public PveRulesDto? Rules { get; set; }
    public int BagActionsUsed { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int? Revision { get; set; }
    public int? CaptureShakes { get; set; }
    public string Id { get; set; } = string.Empty;
    public string OpponentId { get; set; } = string.Empty;
    public string OpponentName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> Log { get; set; } = [];
    public BattleRequestDto? Request { get; set; }
    public string? Error { get; set; }
    public int Turn { get; set; }
    public BattleCombatantDto? PlayerActive { get; set; }
    public BattleCombatantDto? OpponentActive { get; set; }
    public int RewardCurrency { get; set; }
    public int RewardExperience { get; set; }
    public string? ChoiceDeadline { get; set; }
}

public sealed class BattleCombatantDto
{
    public string Ident { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Shiny { get; set; }
    public string Condition { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool Fainted { get; set; }
}

public sealed class BattleRequestDto
{
    public bool? Wait { get; set; }
    public bool TeamPreview { get; set; }
    public int? MaxChosenTeamSize { get; set; }
    public List<bool>? ForceSwitch { get; set; }
    public List<BattleActiveDto>? Active { get; set; }
    public BattleSideDto? Side { get; set; }
}

public sealed class BattleActiveDto
{
    public bool Trapped { get; set; }
    public bool PassRequired { get; set; }
    public bool CanMegaEvo { get; set; }
    public bool CanUltraBurst { get; set; }
    public string? CanTerastallize { get; set; }
    public List<BattleZMoveDto?>? CanZMove { get; set; }
    public List<BattleMoveDto> Moves { get; set; } = [];
}

public sealed class BattleZMoveDto
{
    public List<int> LegalTargets { get; set; } = [];
    public string Move { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

public sealed class BattleMoveDto
{
    public string Target { get; set; } = string.Empty;
    public List<int> LegalTargets { get; set; } = [];
    public string Move { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int? Pp { get; set; }
    public int? Maxpp { get; set; }
    public bool Disabled { get; set; }
}

public sealed class BattleSideDto
{
    public List<BattleSidePokemonDto> Pokemon { get; set; } = [];
}

public sealed class BattleSidePokemonDto
{
    public List<LeagueMoveDto> PpMoves { get; set; } = [];
    public bool Reviving { get; set; }
    public string Ident { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string Item { get; set; } = string.Empty;
}

public sealed class BattleTeamSheetDto
{
    public string Species { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string Ability { get; set; } = string.Empty;
    public string TeraType { get; set; } = string.Empty;
    public List<string> Moves { get; set; } = [];
}

public sealed class ApiErrorDto
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double? RetryAfterSeconds { get; set; }
}
