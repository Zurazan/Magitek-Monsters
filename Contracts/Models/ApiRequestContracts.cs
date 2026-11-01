using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MagitekMonsters.Contracts;

public sealed record ApiRequestContract(string Method, string Path, Type BodyType, bool AllowEmpty = false);

public static class ApiRequestContracts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    public static readonly IReadOnlyList<ApiRequestContract> All = new ApiRequestContract[]
    {
        Post<EmptyRequest>("/api/auth/xivauth/start", true), Post<LoginPollRequest>("/api/auth/xivauth/poll"), Post<DevelopmentLoginRequest>("/api/auth/development"),
        new("PUT", "/api/trainer/card", typeof(TrainerCardSaveRequest)), Post<EmptyRequest>("/api/gifts/{receiptId}/seen", true),
        Post<TrainerUidChoiceRequest>("/api/trainer/uid"), new("PUT", "/api/trainer/vanity", typeof(TrainerVanityRequest)),
        Post<SpeciesRequest>("/api/trainer/starter"), Post<EmptyRequest>("/api/trainer/rest"), Post<SpeciesRequest>("/api/prize-exchange/redeem"),
        Post<ShopPurchaseRequest>("/api/shop/buy"), Post<CollectionRewardRequest>("/api/collection-exchange/redeem"),
        Post<RewardPurchaseRequest>("/api/frontier/redeem"), Post<LegendaryPermitRequest>("/api/legendary/redeem"),
        Post<WildSearchRequest>("/api/encounters/search"), Post<ParadoxEncounterRequest>("/api/frontier/paradox/encounter"),
        Post<EventEncounterRequest>("/api/recurring-events/encounter"), Post<WildBattleRequest>("/api/battles/wild"),
        Post<BattleChoiceRequest>("/api/battles/{battleId}/choose"), Post<TrainerBattleRequest>("/api/battles/pve"),
        Post<StorageMoveRequest>("/api/storage/move"), Post<StorageBoxRequest>("/api/storage/box"), Post<StorageBatchRequest>("/api/storage/batch"),
        Post<PokemonCollectionRequest>("/api/pokemon/{pokemonId}/collection"), Post<SavedTeamRequest>("/api/teams"),
        Post<EmptyRequest>("/api/teams/{presetId}/apply", true), Post<TeamExportRequest>("/api/teams/export"), Post<BoxNameRequest>("/api/boxes/{boxNumber}/name"),
        Post<HeldItemRequest>("/api/pokemon/held-item"), Post<PokemonRequest>("/api/items/rare-candy"), Post<TrainingItemRequest>("/api/items/train"),
        Post<EvTrainingRequest>("/api/items/ev-plan/preview"), Post<EvTrainingRequest>("/api/items/ev-plan/apply"),
        Post<TeachMoveRequest>("/api/moves/teach"), Post<RelearnMoveRequest>("/api/moves/relearn"), Post<ResolveMoveRequest>("/api/moves/{learningId}/resolve"),
        Post<EvolutionTravelRequest>("/api/pokemon/travel"), Post<EvolveRequest>("/api/pokemon/evolve"), Post<FormChangeRequest>("/api/pokemon/form"),
        Post<FusionRequest>("/api/pokemon/fuse"), Post<UnfusionRequest>("/api/pokemon/unfuse"),
        Post<FrontierStartRequest>("/api/frontier/start"), Post<FactoryDraftRequest>("/api/frontier/factory/draft"), Post<FactoryPickRequest>("/api/frontier/factory/pick"),
        Post<FrontierNextRequest>("/api/frontier/next"), Post<RunRequest>("/api/frontier/abandon"), Post<LeagueStartRequest>("/api/league/start"),
        Post<RunRequest>("/api/league/next"), Post<RunRequest>("/api/league/abandon"), Post<LeagueMedicineRequest>("/api/league/medicine"),
        Post<PvpQueueRequest>("/api/pvp/queue"), Post<ChallengeRequest>("/api/pvp/challenges"), Post<ChallengeAcceptRequest>("/api/pvp/challenges/{challengeId}/accept"),
        Post<EmptyRequest>("/api/pvp/challenges/{challengeId}/declined", true), Post<EmptyRequest>("/api/pvp/challenges/{challengeId}/cancelled", true),
        Post<BattleChoiceRequest>("/api/pvp/battles/{battleId}/choose"), Post<EmptyRequest>("/api/pvp/battles/{battleId}/forfeit", true),
        Post<RaidCreateRequest>("/api/raids/create"), Post<RaidJoinRequest>("/api/raids/join"), Post<RaidReadyRequest>("/api/raids/{roomId}/ready"),
        Post<EmptyRequest>("/api/raids/{roomId}/start"), Post<RaidChooseRequest>("/api/raids/{roomId}/choose"), Post<RaidCatchRequest>("/api/raids/{roomId}/catch"),
        Post<RequestReceipt>("/api/raids/{roomId}/skip"), Post<ConfirmationRequest>("/api/raids/{roomId}/leave"), Post<EmptyRequest>("/api/auth/logout", true),
    };
    private static ApiRequestContract Post<T>(string path, bool allowEmpty = false) where T : IClientRequest => new("POST", path, typeof(T), allowEmpty);
    public static ApiRequestContract? Find(string method, string path)
    {
        var segments = path.TrimEnd('/').Split('/');
        return All.FirstOrDefault(contract => contract.Method == method && Matches(contract.Path.Split('/'), segments));
    }
    private static bool Matches(string[] pattern, string[] segments) => pattern.Length == segments.Length
        && pattern.Where((part, index) => !(part.StartsWith('{') && part.EndsWith('}')) && !string.Equals(part, segments[index], StringComparison.OrdinalIgnoreCase)).Any() == false;
    public static IClientRequest Read(ApiRequestContract contract, JsonObject body)
    {
        try
        {
            CheckFields(body);
            return (IClientRequest)(body.Deserialize(contract.BodyType, Json) ?? throw new JsonException("Send a request object."));
        }
        catch (ArgumentException exception) { throw new JsonException("Request fields must be unique.", exception); }
    }
    private static void CheckFields(JsonNode? node)
    {
        if (node is JsonObject value) foreach (var field in value) CheckFields(field.Value);
        else if (node is JsonArray array) foreach (var item in array) CheckFields(item);
    }
}
