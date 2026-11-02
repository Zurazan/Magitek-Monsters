using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class ServerMetadataDto
{
    public int ProtocolVersion { get; set; }
    public string MinimumClientVersion { get; set; } = "";
    public string RealtimeTransport { get; set; } = "";
    public string? CatalogVersion { get; set; }
    public int OnlineTrainers { get; set; } = -1;
    public ServerCapabilitiesDto Capabilities { get; set; } = new();
    public ServerFeaturesDto Features { get; set; } = new();
}
public sealed class ServerCapabilitiesDto
{
    public int ContractVersion { get; set; }
    public PvpCapabilitiesDto Pvp { get; set; } = new();
    public ShopCapabilitiesDto Shop { get; set; } = new();
    public StorageCapabilitiesDto Storage { get; set; } = new();
}
public sealed class PvpCapabilitiesDto
{
    public bool FormatSpecificTeams { get; set; }
    public bool BagItemsAllowed { get; set; }
    public bool HeldItemsAllowed { get; set; }
    public int MaximumSeasonFormats { get; set; }
    public string RulesVersion { get; set; } = "";
    public List<PvpFormatDto> Formats { get; set; } = [];
}
public sealed class ShopCapabilitiesDto
{
    public int MaximumPurchaseQuantity { get; set; }
}
public sealed class StorageCapabilitiesDto
{
    public int PartyCapacity { get; set; }
    public int MaximumBatchSize { get; set; }
}
public sealed class ServerFeaturesDto
{
    public bool XivAuthAuthorizationCodePkce { get; set; }
    public bool ServerEvents { get; set; }
    public bool PveShowdown { get; set; }
    public bool PveProgression { get; set; }
    public bool PersistentTrainerBattles { get; set; }
    public bool LeagueRuns { get; set; }
    public bool PveRematches { get; set; }
    public bool PvePpMedicine { get; set; }
    public bool BattleFrontier { get; set; }
    public bool BattlePoints { get; set; }
    public bool ParadoxResearch { get; set; }
    public bool ExactTerritoryWildPools { get; set; }
    public bool ThematicTerritoryWildPools { get; set; }
    public bool StarterFamiliesReserved { get; set; }
    public bool PrizeExchange { get; set; }
    public bool Pvp { get; set; }
    public bool PvpFormatSpecificTeams { get; set; }
    public bool FriendlyFormats { get; set; }
    public bool PvpRankedZToA { get; set; }
    public bool RedisCoordination { get; set; }
    public bool PvpBattleCommandJournal { get; set; }
    public bool TrainerIds { get; set; }
    public bool PokemonRelease { get; set; }
    public bool MoveRelearning { get; set; }
    public bool PokedexAcquisitionGuide { get; set; }
    public bool CollectionPoints { get; set; }
    public bool PersistentFormChanges { get; set; }
    public bool PokemonFusion { get; set; }
    public bool WildAppearances { get; set; }
    public bool PokedexFormCollection { get; set; }
    public bool StarterExchange { get; set; }
    public bool ConfigurablePvpSeasons { get; set; }
    public bool DiscordAdmin { get; set; }
    public bool LegendarySanctuaries { get; set; }
    public bool RankTokenPermits { get; set; }
    public bool MewCompletionGate { get; set; }
    public bool RecurringEvents { get; set; }
    public bool CooperativeRaids { get; set; }
}
