using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MagitekMonsters.Models;
using MagitekMonsters.Contracts;

namespace MagitekMonsters.Services;

public sealed class ApiClient : IDisposable, IConnectionRecoveryApi
{
    private readonly Configuration configuration;
    private readonly ProtectedTokenStore tokenStore;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private readonly ConditionalWeakTable<TrainerSnapshotDto, SnapshotChange> snapshotChanges = new();
    private sealed record SnapshotChange(string Id);
    public ClientUpdateState Update { get; } = new();
    public ServerConnection? Live { get; set; }

    public ApiClient(Configuration configuration, ProtectedTokenStore tokenStore)
    {
        this.configuration = configuration;
        this.tokenStore = tokenStore;
    }

    public bool HasSession => !string.IsNullOrWhiteSpace(tokenStore.Read());
    public Task<ServerMetadataDto> GetMetadata(CancellationToken cancellationToken = default) =>
        Send<ServerMetadataDto>(HttpMethod.Get, "/api/meta", null, false, cancellationToken);
    public string? SnapshotChangeId(TrainerSnapshotDto snapshot) => snapshotChanges.TryGetValue(snapshot, out var change) ? change.Id : null;

    public Task<AuthorizationFlowDto> StartXivAuth(CancellationToken cancellationToken = default) =>
        Send<AuthorizationFlowDto>(HttpMethod.Post, "/api/auth/xivauth/start", EmptyRequest.Instance, false, cancellationToken);

    public Task<AuthorizationPollDto> PollXivAuth(string loginId, CancellationToken cancellationToken = default) =>
        Send<AuthorizationPollDto>(HttpMethod.Post, "/api/auth/xivauth/poll", new LoginPollRequest(loginId), false, cancellationToken);

    public Task<TrainerSnapshotDto> GetMe(CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Get, "/api/me", null, true, cancellationToken);
    public Task<JsonElement> ChooseTrainerUid(TrainerUidChoiceRequest request) =>
        Send<JsonElement>(HttpMethod.Post, "/api/trainer/uid", request, true, default);
    public Task<JsonElement> SaveVanity(string? code) =>
        Send<JsonElement>(HttpMethod.Put, "/api/trainer/vanity", new TrainerVanityRequest(code), true, default);
    public Task<TrainerProfileDto> GetTrainerProfile(string trainerId, CancellationToken cancellationToken = default) =>
        Send<TrainerProfileDto>(HttpMethod.Get, $"/api/trainers/{Uri.EscapeDataString(trainerId.Trim())}/card", null, true, cancellationToken);
    public Task<TrainerCardPreferences> SaveTrainerCard(TrainerCardPreferences card, bool onlyIfMissing = false) =>
        Send<TrainerCardPreferences>(HttpMethod.Put, "/api/trainer/card", new TrainerCardSaveRequest(card, onlyIfMissing), true, default);
    public Task<GiftInboxDto> GetGifts(CancellationToken cancellationToken = default) =>
        Send<GiftInboxDto>(HttpMethod.Get, "/api/gifts", null, true, cancellationToken);
    public Task<GiftInboxDto> ReadGift(string receipt, CancellationToken cancellationToken = default) =>
        Send<GiftInboxDto>(HttpMethod.Post, $"/api/gifts/{receipt}/seen", null, true, cancellationToken);

    public Task<RaidBoardDto> GetRaids(CancellationToken cancellationToken = default) =>
        Send<RaidBoardDto>(HttpMethod.Get, "/api/raids", null, true, cancellationToken);
    public Task<RaidRoomDto> GetRaid(string id, CancellationToken cancellationToken = default) =>
        Send<RaidRoomDto>(HttpMethod.Get, $"/api/raids/{id}", null, true, cancellationToken);
    public Task<RaidRoomDto> RaidAction(string? id, string action, IClientRequest body, CancellationToken cancellationToken = default) =>
        Send<RaidRoomDto>(HttpMethod.Post, id is null ? $"/api/raids/{action}" : $"/api/raids/{id}/{action}", body, true, cancellationToken);

    public Task<RecurringEventScheduleDto> GetRecurringEvents() =>
        Send<RecurringEventScheduleDto>(HttpMethod.Get, "/api/recurring-events", null, true, default);

    public Task<EncounterDto> SearchRecurringEvent(string occurrenceId, string species, string requestId) =>
        Send<EncounterDto>(HttpMethod.Post, "/api/recurring-events/encounter", new EventEncounterRequest(occurrenceId, species, requestId), true, default);

    public Task<BattleSnapshotDto> StartEventSanctuary(string opponentId, string eventOccurrenceId, string[] pokemonIds) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/battles/pve", new TrainerBattleRequest(opponentId, "standard", pokemonIds, EventOccurrenceId: eventOccurrenceId), true, default);

    public Task<StarterCatalogDto> GetStarters(CancellationToken cancellationToken = default) =>
        Send<StarterCatalogDto>(HttpMethod.Get, "/api/catalog/starters", null, false, cancellationToken);

    public Task<ShopCatalogDto> GetShop(CancellationToken cancellationToken = default) =>
        Send<ShopCatalogDto>(HttpMethod.Get, "/api/catalog/shop", null, false, cancellationToken);

    public Task<CollectionRewardCatalogDto> GetCollectionRewards(CancellationToken cancellationToken = default) =>
        Send<CollectionRewardCatalogDto>(HttpMethod.Get, "/api/catalog/collection-rewards", null, false, cancellationToken);

    public Task<TrainerSnapshotDto> RedeemCollectionReward(string requestId, string rewardId, string mode, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/collection-exchange/redeem", new CollectionRewardRequest(requestId, rewardId, mode), true, cancellationToken);

    public Task<PrizePokemonCatalogDto> GetPrizePokemon(CancellationToken cancellationToken = default) =>
        Send<PrizePokemonCatalogDto>(HttpMethod.Get, "/api/catalog/prize-pokemon", null, false, cancellationToken);

    public Task<OpponentCatalogDto> GetOpponents(CancellationToken cancellationToken = default) =>
        Send<OpponentCatalogDto>(HttpMethod.Get, "/api/catalog/pve-opponents", null, false, cancellationToken);

    public Task<PokedexCatalogDto> GetPokedexCatalog(CancellationToken cancellationToken = default) =>
        Send<PokedexCatalogDto>(HttpMethod.Get, "/api/catalog/pokedex", null, true, cancellationToken);

    public Task<TrainerSnapshotDto> SelectStarter(string species, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/trainer/starter", new SpeciesRequest(species), true, cancellationToken);

    public Task<HabitatDto> GetHabitat(
        uint territoryId,
        string territoryName,
        string englishName,
        string regionName,
        string zoneName,
        CancellationToken cancellationToken = default) =>
        Send<HabitatDto>(HttpMethod.Get,
            $"/api/habitats/current?territoryId={territoryId}" +
            $"&territoryName={Uri.EscapeDataString(territoryName)}" +
            $"&englishName={Uri.EscapeDataString(englishName)}" +
            $"&regionName={Uri.EscapeDataString(regionName)}" +
            $"&zoneName={Uri.EscapeDataString(zoneName)}",
            null, true, cancellationToken);

    public Task<EncounterDto> Search(
        uint territoryId,
        string territoryName,
        string englishName,
        string regionName,
        string zoneName,
        string? targetSpecies,
        CancellationToken cancellationToken = default)
    {
        var body = new WildSearchRequest(territoryId, territoryName, englishName, regionName, zoneName, targetSpecies);
        return Send<EncounterDto>(HttpMethod.Post, "/api/encounters/search", body, true, cancellationToken);
    }

    public Task<BattleSnapshotDto> StartWildBattle(string encounterId, CancellationToken cancellationToken = default) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/battles/wild", new WildBattleRequest(encounterId), true, cancellationToken);

    public Task<TrainerSnapshotDto> MovePokemon(string pokemonId, string destination, int? slot = null, CancellationToken cancellationToken = default)
    {
        var body = new StorageMoveRequest(pokemonId, destination, slot);
        return Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/storage/move", body, true, cancellationToken);
    }

    public Task<TrainerSnapshotDto> ReleasePokemon(string pokemonId, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Delete, $"/api/pokemon/{pokemonId}", null, true, cancellationToken);

    public Task<TrainerSnapshotDto> EditPokemon(string pokemonId, string? nickname = null, bool? favorite = null) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, $"/api/pokemon/{pokemonId}/collection",
            nickname is not null ? new PokemonCollectionRequest(Nickname: nickname) : new PokemonCollectionRequest(Favorite: favorite), true, default);

    public Task<TrainerSnapshotDto> SaveTeam(string id, string name, IEnumerable<string> pokemonIds) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/teams", new SavedTeamRequest(id, name, pokemonIds.ToArray()), true, default);

    public Task<TrainerSnapshotDto> ApplyTeam(string id) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, $"/api/teams/{id}/apply", EmptyRequest.Instance, true, default);

    public Task<TrainerSnapshotDto> DeleteTeam(string id) =>
        Send<TrainerSnapshotDto>(HttpMethod.Delete, $"/api/teams/{id}", null, true, default);

    public Task<TrainerSnapshotDto> RenameBox(int boxNumber, string name) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, $"/api/boxes/{boxNumber}/name", new BoxNameRequest(name), true, default);

    public Task<TrainerSnapshotDto> MoveToBox(string pokemonId, int boxNumber) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/storage/box", new StorageBoxRequest(pokemonId, boxNumber), true, default);

    public Task<TrainerSnapshotDto> StorageBatch(string requestId, string action, IEnumerable<string> pokemonIds, int boxNumber = 1, bool favorite = true, CancellationToken cancellationToken = default)
    {
        var ids = pokemonIds.ToArray();
        var body = action switch
        {
            "box" => new StorageBatchRequest(requestId, action, ids, BoxNumber: boxNumber),
            "favorite" => new StorageBatchRequest(requestId, action, ids, Favorite: favorite),
            "release" => new StorageBatchRequest(requestId, action, ids, Confirmed: true),
            _ => new StorageBatchRequest(requestId, action, ids)
        };
        return Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/storage/batch", body, true, cancellationToken);
    }

    public Task<TextExportDto> ExportTeam(IEnumerable<string> pokemonIds) =>
        Send<TextExportDto>(HttpMethod.Post, "/api/teams/export", new TeamExportRequest([.. pokemonIds]), true, default, changesState: false);

    public Task<BattleHelpDto> GetBattleHelp(CancellationToken cancellationToken = default) =>
        Send<BattleHelpDto>(HttpMethod.Get, "/api/catalog/battle-help", null, true, cancellationToken);

    public Task<PvpFormatCatalogDto> GetFriendlyFormats() => Send<PvpFormatCatalogDto>(HttpMethod.Get, "/api/catalog/pvp-formats", null, false, default);
    public Task<PvpStatusDto> ChallengeTrainer(string trainerId, IEnumerable<string> pokemonIds, string formatId) =>
        Send<PvpStatusDto>(HttpMethod.Post, "/api/pvp/challenges", new ChallengeRequest(trainerId, pokemonIds.ToArray(), formatId), true, default);

    public Task<PvpStatusDto> AcceptChallenge(string id, IEnumerable<string> pokemonIds) =>
        Send<PvpStatusDto>(HttpMethod.Post, $"/api/pvp/challenges/{id}/accept", new ChallengeAcceptRequest(pokemonIds.ToArray()), true, default);

    public Task<PvpStatusDto> ResolveChallenge(string id, string action) =>
        Send<PvpStatusDto>(HttpMethod.Post, $"/api/pvp/challenges/{id}/{action}", EmptyRequest.Instance, true, default);

    public Task<FrontierCatalogDto> GetFrontierCatalog() =>
        Send<FrontierCatalogDto>(HttpMethod.Get, "/api/catalog/frontier", null, false, default);

    public Task<SpecialTrialCatalogDto> GetSpecialTrials() =>
        Send<SpecialTrialCatalogDto>(HttpMethod.Get, "/api/catalog/special-trials", null, false, default);

    public Task<LegendaryCatalogDto> GetLegendaryCatalog() =>
        Send<LegendaryCatalogDto>(HttpMethod.Get, "/api/catalog/legendary-bosses", null, false, default);

    public Task<TrainerSnapshotDto> BuyLegendaryPermit(string requestId, string bossId) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/legendary/redeem", new LegendaryPermitRequest(requestId, bossId), true, default);

    public Task<BattleSnapshotDto> StartFrontier(string bracket, string teamMode, string rentalId, IEnumerable<string> pokemonIds) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/frontier/start", teamMode == "rental"
            ? new FrontierStartRequest(bracket, teamMode, RentalId: rentalId) : new FrontierStartRequest(bracket, teamMode, PokemonIds: pokemonIds.ToArray()), true, default);

    public Task<FrontierRunDto> DraftFactory(string bracket) =>
        Send<FrontierRunDto>(HttpMethod.Post, "/api/frontier/factory/draft", new FactoryDraftRequest(bracket), true, default);

    public Task<BattleSnapshotDto> PickFactory(string runId, int revision, IEnumerable<string> pokemonIds) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/frontier/factory/pick", new FactoryPickRequest(runId, revision, pokemonIds.ToArray()), true, default);

    public Task<BattleSnapshotDto> NextFrontier(string runId, int revision, string? swapOutId = null, string? swapInId = null) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/frontier/next", new FrontierNextRequest(runId, revision, swapOutId, swapInId), true, default);

    public Task<FrontierRunDto> AbandonFrontier(string runId, int revision) =>
        Send<FrontierRunDto>(HttpMethod.Post, "/api/frontier/abandon", new RunRequest(runId, revision), true, default);

    public Task<TrainerSnapshotDto> RedeemFrontierReward(string requestId, string rewardId, int quantity) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/frontier/redeem", new RewardPurchaseRequest(requestId, rewardId, quantity), true, default);

    public Task<EncounterDto> StartParadoxResearch(string permitId) =>
        Send<EncounterDto>(HttpMethod.Post, "/api/frontier/paradox/encounter", new ParadoxEncounterRequest(permitId), true, default);

    public Task<TrainerSnapshotDto> BuyItem(string itemId, int quantity, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/shop/buy", new ShopPurchaseRequest(itemId, quantity, Guid.NewGuid().ToString()), true, cancellationToken);

    public Task<EvTrainingPlanDto> PreviewEvTraining(string pokemonId, Dictionary<string, int> targetEvs, bool useResetItem, CancellationToken cancellationToken = default) =>
        Send<EvTrainingPlanDto>(HttpMethod.Post, "/api/items/ev-plan/preview", new EvTrainingRequest(pokemonId, targetEvs, useResetItem), true, cancellationToken, changesState: false);

    public Task<TrainerSnapshotDto> ApplyEvTraining(string pokemonId, Dictionary<string, int> targetEvs, bool useResetItem, string previewHash, string requestId) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/items/ev-plan/apply", new EvTrainingRequest(pokemonId, targetEvs, useResetItem, previewHash, requestId), true, default);

    public Task<TrainerSnapshotDto> UseTeraShard(string pokemonId, string teraType, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/items/train", new TrainingItemRequest(pokemonId, "terashard", 1, TeraType: teraType), true, cancellationToken);

    public Task<TrainerSnapshotDto> RedeemPrizePokemon(string species, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/prize-exchange/redeem", new SpeciesRequest(species), true, cancellationToken);

    public Task<TrainerSnapshotDto> UseTrainingItem(
        string pokemonId,
        string itemId,
        string? stat,
        int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        var body = new TrainingItemRequest(pokemonId, itemId, quantity, stat);
        return Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/items/train", body, true, cancellationToken);
    }

    public Task<PokemonBuildDto> GetPokemonBuild(string pokemonId, CancellationToken cancellationToken = default) =>
        Send<PokemonBuildDto>(HttpMethod.Get, $"/api/pokemon/{pokemonId}/build", null, true, cancellationToken);

    public Task<TrainerSnapshotDto> TeachMove(string pokemonId, string itemId, string? forgetMove, CancellationToken cancellationToken = default)
    {
        var body = new TeachMoveRequest(pokemonId, itemId, forgetMove);
        return Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/moves/teach", body, true, cancellationToken);
    }

    public Task<TrainerSnapshotDto> UseRareCandy(string pokemonId, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/items/rare-candy", new PokemonRequest(pokemonId), true, cancellationToken);

    public Task<JsonElement> RecordEvolutionTravel(string requestId, string[] pokemonIds, double distance, CancellationToken cancellationToken = default) =>
        Send<JsonElement>(HttpMethod.Post, "/api/pokemon/travel", new EvolutionTravelRequest(requestId, pokemonIds, distance), true, cancellationToken);

    public Task<TrainerSnapshotDto> EvolvePokemon(string pokemonId, string targetSpecies, EvolutionEnvironment? environment, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/pokemon/evolve", new EvolveRequest(pokemonId, targetSpecies, environment?.ToRequest()), true, cancellationToken);

    public Task<TrainerSnapshotDto> SetHeldItem(string pokemonId, string? itemId, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/pokemon/held-item", new HeldItemRequest(pokemonId, itemId), true, cancellationToken);

    public Task<TrainerSnapshotDto> ChangeForm(string pokemonId, string targetSpecies, string? targetAbility, string? forgetMove, EvolutionEnvironment? environment, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/pokemon/form", new FormChangeRequest(pokemonId, targetSpecies, environment?.ToRequest(), targetAbility, forgetMove), true, cancellationToken);

    public Task<TrainerSnapshotDto> FusePokemon(string requestId, string pokemonId, string partnerId, string targetSpecies, string? forgetMove, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/pokemon/fuse", new FusionRequest(requestId, pokemonId, partnerId, targetSpecies, forgetMove), true, cancellationToken);

    public Task<TrainerSnapshotDto> UnfusePokemon(string requestId, string pokemonId, string fusionId, CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/pokemon/unfuse", new UnfusionRequest(requestId, pokemonId, fusionId), true, cancellationToken);

    public Task<TrainerSnapshotDto> ResolveMoveLearning(
        string learningId,
        string? forgetMove,
        CancellationToken cancellationToken = default)
    {
        var body = new ResolveMoveRequest(forgetMove);
        return Send<TrainerSnapshotDto>(HttpMethod.Post, $"/api/moves/{learningId}/resolve", body, true, cancellationToken);
    }

    public Task<TrainerSnapshotDto> RelearnMove(
        string pokemonId,
        string move,
        string? forgetMove,
        CancellationToken cancellationToken = default)
    {
        var body = new RelearnMoveRequest(pokemonId, move, forgetMove);
        return Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/moves/relearn", body, true, cancellationToken);
    }

    public Task<BattleSnapshotDto> StartBattle(string opponentId, string challenge, string? monotype, string[] pokemonIds, CancellationToken cancellationToken = default, string? circuitId = null)
    {
        var body = new TrainerBattleRequest(opponentId, challenge, pokemonIds, monotype, circuitId);
        return Send<BattleSnapshotDto>(HttpMethod.Post, "/api/battles/pve", body, true, cancellationToken);
    }

    public Task<BattleSnapshotDto> ChooseBattleAction(string battleId, string choice, int? revision = null, CancellationToken cancellationToken = default) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, $"/api/battles/{battleId}/choose",
            new BattleChoiceRequest(choice, revision), true, cancellationToken);

    public Task<BattleSnapshotDto> GetBattle(string battleId, CancellationToken cancellationToken = default) =>
        Send<BattleSnapshotDto>(HttpMethod.Get, $"/api/battles/{battleId}", null, true, cancellationToken);

    public Task<BattleSnapshotDto> StartLeague(string[] pokemonIds, bool rematch = false, CancellationToken cancellationToken = default, string? circuitId = null) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/league/start", new LeagueStartRequest(pokemonIds, rematch, circuitId), true, cancellationToken);
    public Task<BattleSnapshotDto> NextLeague(string runId, int revision, CancellationToken cancellationToken = default) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, "/api/league/next", new RunRequest(runId, revision), true, cancellationToken);
    public Task<LeagueRunDto> HealLeague(string runId, int revision, string itemId, string pokemonId, string? moveId, CancellationToken cancellationToken = default) =>
        Send<LeagueRunDto>(HttpMethod.Post, "/api/league/medicine", new LeagueMedicineRequest(runId, revision, itemId, pokemonId, moveId), true, cancellationToken);
    public Task<LeagueRunDto> AbandonLeague(string runId, int revision, CancellationToken cancellationToken = default) =>
        Send<LeagueRunDto>(HttpMethod.Post, "/api/league/abandon", new RunRequest(runId, revision), true, cancellationToken);

    public Task<PvpStatusDto> GetPvpStatus(CancellationToken cancellationToken = default) =>
        Send<PvpStatusDto>(HttpMethod.Get, "/api/pvp/status", null, true, cancellationToken);

    public Task<PvpStatusDto> JoinPvpQueue(string mode, string formatId, IEnumerable<string> pokemonIds, CancellationToken cancellationToken = default) =>
        Send<PvpStatusDto>(HttpMethod.Post, "/api/pvp/queue", new PvpQueueRequest(mode, formatId, pokemonIds.ToArray()), true, cancellationToken);

    public Task<PvpStatusDto> LeavePvpQueue(CancellationToken cancellationToken = default) =>
        Send<PvpStatusDto>(HttpMethod.Delete, "/api/pvp/queue", null, true, cancellationToken);

    public Task<BattleSnapshotDto> GetPvpBattle(string battleId, CancellationToken cancellationToken = default) =>
        Send<BattleSnapshotDto>(HttpMethod.Get, $"/api/pvp/battles/{battleId}", null, true, cancellationToken);

    public Task<BattleSnapshotDto> ChoosePvpBattleAction(string battleId, string choice, CancellationToken cancellationToken = default, int? revision = null) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, $"/api/pvp/battles/{battleId}/choose", new BattleChoiceRequest(choice, revision), true, cancellationToken);

    public Task<BattleSnapshotDto> ForfeitPvpBattle(string battleId, CancellationToken cancellationToken = default) =>
        Send<BattleSnapshotDto>(HttpMethod.Post, $"/api/pvp/battles/{battleId}/forfeit", EmptyRequest.Instance, true, cancellationToken);

    public Task<PvpStatusDto> DismissPvpBattle(CancellationToken cancellationToken = default) =>
        Send<PvpStatusDto>(HttpMethod.Delete, "/api/pvp/battle", null, true, cancellationToken);

    public void SaveSession(SessionDto session) => tokenStore.Write(session.AccessToken);

    public Task<TrainerSnapshotDto> RestParty(CancellationToken cancellationToken = default) =>
        Send<TrainerSnapshotDto>(HttpMethod.Post, "/api/trainer/rest", EmptyRequest.Instance, true, cancellationToken);

    public void Logout() => tokenStore.Clear();

    public void Dispose() => http.Dispose();

    private async Task<T> Send<T>(HttpMethod method, string path, IClientRequest? body, bool authenticated, CancellationToken cancellationToken, bool changesState = true)
    {
        if (Update.Required) throw new ApiException(426, Update.Message, "client_update_required");
        if (body is not null && (ApiRequestContracts.Find(method.Method, path) is not { } contract || contract.BodyType != body.GetType()))
            throw new InvalidOperationException("The request does not match its API contract.");
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var serverTiming = "unavailable";
        var dispatched = false;
        try
        {
            if (authenticated && Live is { CanInvoke: true } live && ServerConnection.Command(method, path, body) is { } command)
            {
                dispatched = true;
                var reply = await live.Invoke(command, cancellationToken).ConfigureAwait(false);
                object? value = typeof(T) == typeof(BattleSnapshotDto) ? reply.Battle : typeof(T) == typeof(RaidRoomDto) ? reply.Raid : reply.Pvp;
                return value is T liveResult ? liveResult : throw new HttpRequestException("The live response was incomplete.");
            }
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation(ProtocolInfo.ProtocolHeader, ProtocolInfo.Version);
            request.Headers.TryAddWithoutValidation(ProtocolInfo.ClientHeader, ClientBuildVersion.Current);
            if (authenticated)
            {
                var token = tokenStore.Read();
                if (string.IsNullOrWhiteSpace(token))
                    throw new ApiException(401, "Log in before using this feature.");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: json);

            dispatched = true;
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var requestMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (response.Headers.TryGetValues("Server-Timing", out var timings)) serverTiming = string.Join(",", timings);
            if (await Update.Observe(response, cancellationToken).ConfigureAwait(false))
                throw new ApiException(426, Update.Message, "client_update_required");
            if (!response.IsSuccessStatusCode)
            {
                ApiErrorDto? payload = null;
                try
                {
                    payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>(json, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException)
                {
                    // Non-JSON proxy errors use the HTTP status below.
                }
                throw new ApiException((int)response.StatusCode, payload?.Message ?? $"Server returned HTTP {(int)response.StatusCode}.", payload?.Error,
                    SearchCooldown.RetryDelay(payload, response.Headers.RetryAfter, DateTimeOffset.UtcNow));
            }

            var parseStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = await response.Content.ReadFromJsonAsync<T>(json, cancellationToken).ConfigureAwait(false)
                ?? throw new ApiException((int)response.StatusCode, "The server returned an empty response.");
            var parseMs = System.Diagnostics.Stopwatch.GetElapsedTime(parseStarted).TotalMilliseconds;
            if (result is TrainerSnapshotDto snapshot)
            {
                if (response.Headers.TryGetValues(ProtocolInfo.ChangeHeader, out var changes)
                    && Guid.TryParseExact(string.Join(",", changes), "D", out var change))
                    snapshotChanges.Add(snapshot, new(change.ToString("D")));
                Plugin.Log.Debug("Trainer snapshot response: {Method} / {PokemonCount} Pokemon / {Bytes} bytes / request {RequestMs:F1} ms / parse {ParseMs:F1} ms / server {ServerTiming}",
                    method.Method, snapshot.Party.Count + snapshot.Storage.Count, response.Content.Headers.ContentLength ?? -1, requestMs, parseMs, serverTiming);
            }
            return result;
        }
        catch (Exception exception) when (dispatched && authenticated && changesState && method != HttpMethod.Get && method != HttpMethod.Head
            && !cancellationToken.IsCancellationRequested && (exception is HttpRequestException or OperationCanceledException
                or ApiException { StatusCode: 408 or 502 or 503 or 504 }))
        {
            throw new UnconfirmedActionException(exception);
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsed >= 500 && !cancellationToken.IsCancellationRequested
                && (path.StartsWith("/api/battles/", StringComparison.Ordinal) || path.StartsWith("/api/pvp/", StringComparison.Ordinal) || path == "/api/me"))
                Plugin.Log.Information("Slow Magitek Monsters request: {Method} {Route} took {ElapsedMs:F0} ms / server {ServerTiming}", method.Method,
                    path.StartsWith("/api/battles/", StringComparison.Ordinal) ? path.EndsWith("/choose", StringComparison.Ordinal) ? "/api/battles/{id}/choose" : "/api/battles/{id}" : path.StartsWith("/api/pvp/battles/", StringComparison.Ordinal) ? "/api/pvp/battles/{id}" : path, elapsed, serverTiming);
        }
    }

    private Uri BuildUri(string path)
    {
        var root = Configuration.OfficialServerUrl;
        if (!Uri.TryCreate(root + path, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ApiException(0, "The configured server URL is invalid.");
        return uri;
    }
}
