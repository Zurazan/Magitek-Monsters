using System.Collections.Generic;
using System.Text.Json.Serialization;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Contracts;

public interface IClientRequest;
public sealed record EmptyRequest : IClientRequest
{
    public static readonly EmptyRequest Instance = new();
}
public sealed record LoginPollRequest(string? LoginId) : IClientRequest;
public sealed record DevelopmentLoginRequest(string? CharacterName, string? HomeWorld) : IClientRequest;
public sealed record TrainerCardSaveRequest(TrainerCardPreferences Card, bool OnlyIfMissing = false) : IClientRequest;
public sealed record TrainerUidChoiceRequest(string? Uid, bool KeepGenerated, bool Confirmed) : IClientRequest;
public sealed record TrainerVanityRequest([property: JsonRequired] string? Code) : IClientRequest;
public sealed record SpeciesRequest(string Species) : IClientRequest;
public sealed record PokemonRequest(string PokemonId) : IClientRequest;
public sealed record ShopPurchaseRequest(string ItemId, int Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null) : IClientRequest;
public sealed record RewardPurchaseRequest(string RequestId, string RewardId, int Quantity) : IClientRequest;
public sealed record CollectionRewardRequest(string RequestId, string RewardId, string Mode) : IClientRequest;
public sealed record LegendaryPermitRequest(string RequestId, string BossId) : IClientRequest;
public sealed record ParadoxEncounterRequest(string PermitId) : IClientRequest;
public sealed record EventEncounterRequest(string OccurrenceId, string Species, string RequestId) : IClientRequest;
public sealed record WildSearchRequest(uint TerritoryId, string TerritoryName, string EnglishName, string RegionName, string ZoneName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetSpecies = null) : IClientRequest;
public sealed record WildBattleRequest(string EncounterId) : IClientRequest;
public sealed record BattleChoiceRequest(string Choice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Revision = null) : IClientRequest;
public sealed record StorageMoveRequest(string PokemonId, string Destination,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Slot = null) : IClientRequest;
public sealed record StorageBoxRequest(string PokemonId, int BoxNumber) : IClientRequest;
public sealed record PokemonCollectionRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Nickname = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Favorite = null) : IClientRequest;
public sealed record SavedTeamRequest(string Id, string Name, IReadOnlyList<string> PokemonIds) : IClientRequest;
public sealed record BoxNameRequest(string Name) : IClientRequest;
public sealed record StorageBatchRequest(string RequestId, string Action, IReadOnlyList<string> PokemonIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BoxNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Favorite = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Confirmed = null) : IClientRequest;
public sealed record TeamExportRequest(List<string> PokemonIds) : IClientRequest;
public sealed record PvpQueueRequest(string Mode, string FormatId, IReadOnlyList<string> PokemonIds) : IClientRequest;
public sealed record ChallengeRequest(string TrainerId, IReadOnlyList<string> PokemonIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FormatId = null) : IClientRequest;
public sealed record ChallengeAcceptRequest(IReadOnlyList<string> PokemonIds) : IClientRequest;
public sealed record FrontierStartRequest(string Bracket, string TeamMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RentalId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? PokemonIds = null) : IClientRequest;
public sealed record FactoryDraftRequest(string Bracket) : IClientRequest;
public sealed record FactoryPickRequest(string RunId, int Revision, IReadOnlyList<string> PokemonIds) : IClientRequest;
public sealed record RunRequest(string RunId, int Revision) : IClientRequest;
public sealed record FrontierNextRequest(string RunId, int Revision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SwapOutId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SwapInId = null) : IClientRequest;
public sealed record EvTrainingRequest(string PokemonId, Dictionary<string, int> TargetEvs, bool UseResetItem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviewHash = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null) : IClientRequest;
public sealed record TrainingItemRequest(string PokemonId, string ItemId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Quantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TeraType = null) : IClientRequest;
public sealed record TeachMoveRequest(string PokemonId, string ItemId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ForgetMove = null) : IClientRequest;
public sealed record RelearnMoveRequest(string PokemonId, string Move,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ForgetMove = null) : IClientRequest;
public sealed record ResolveMoveRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ForgetMove = null) : IClientRequest;
public sealed record EvolutionTravelRequest(string RequestId, IReadOnlyList<string> PokemonIds, double Distance) : IClientRequest;
public sealed record EvolutionConditionsRequest(int EorzeaMinute, string Weather,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TerritoryTheme = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? EorzeaDay = null);
public sealed record EvolveRequest(string PokemonId, string TargetSpecies, EvolutionConditionsRequest? Environment = null) : IClientRequest;
public sealed record HeldItemRequest(string PokemonId, string? ItemId) : IClientRequest;
public sealed record FormChangeRequest(string PokemonId, string TargetSpecies, EvolutionConditionsRequest? Environment = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetAbility = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ForgetMove = null) : IClientRequest;
public sealed record FusionRequest(string RequestId, string PokemonId, string PartnerId, string TargetSpecies,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ForgetMove = null) : IClientRequest;
public sealed record UnfusionRequest(string RequestId, string PokemonId, string FusionId) : IClientRequest;
public sealed record TrainerBattleRequest(string OpponentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Challenge = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? PokemonIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Monotype = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CircuitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EventOccurrenceId = null) : IClientRequest;
public sealed record LeagueStartRequest(IReadOnlyList<string> PokemonIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Rematch = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CircuitId = null) : IClientRequest;
public sealed record LeagueMedicineRequest(string RunId, int Revision, string ItemId, string PokemonId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MoveId = null) : IClientRequest;
public sealed record RaidCreateRequest(string DenKey, string PokemonId, bool Private) : IClientRequest;
public sealed record RaidJoinRequest(string PokemonId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RoomId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null) : IClientRequest;
public sealed record RaidReadyRequest(bool Ready,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PokemonId = null) : IClientRequest;
public sealed record RaidTurnChoice(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Move = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Modifier = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cheer = null);
public sealed record RaidChooseRequest(string RoundId, string RequestId, RaidTurnChoice Choice) : IClientRequest;
public sealed record RaidCatchRequest(string RequestId, string? BallId) : IClientRequest;
public sealed record RequestReceipt(string RequestId) : IClientRequest;
public sealed record ConfirmationRequest(bool Confirmed) : IClientRequest;
