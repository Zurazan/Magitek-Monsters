namespace MagitekMonsters.Services;

public static class ProtocolInfo
{
    public const string Version = "41";
    public const string ProtocolHeader = "X-MagitekMonsters-Protocol-Version";
    public const string ClientHeader = "X-MagitekMonsters-Client-Version";
    public const string ChangeHeader = "X-MagitekMonsters-Change-Id";
    public const string CatalogHeader = "X-MagitekMonsters-Catalog-Version";
    public const string StorageVersionHeader = "X-MagitekMonsters-Storage-Version";
    public const string OptionsVersionHeader = "X-MagitekMonsters-Options-Version";
    public const string PartyOptionsVersionHeader = "X-MagitekMonsters-Party-Options-Version";
    public const string BoxedOptionsVersionHeader = "X-MagitekMonsters-Boxed-Options-Version";
    public const string OptionsDeltaHeader = "X-MagitekMonsters-Options-Delta";
    public const string OptionsDeltaVersion = "1";
    public const string StorageDeltaHeader = "X-MagitekMonsters-Storage-Delta";
    public const string StorageDeltaVersion = "1";
    public const string StorageReplacementDeltaVersion = "2";
    public const string StorageTeamDeltaVersion = "3";
}
