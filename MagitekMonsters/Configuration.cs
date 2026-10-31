using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using MagitekMonsters.Services;

namespace MagitekMonsters;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const string OfficialServerUrl = "https://magitekmonsters.lightless.media";

    public int Version { get; set; } = 1;

    // Stored with Windows DPAPI.
    public string ProtectedAccessToken { get; set; } = string.Empty;

    public Dictionary<string, TrainerCardPreferences> TrainerCards { get; set; } = new();
    public Dictionary<string, CollectionWishlistPreferences> CollectionWishlists { get; set; } = new();
    public Dictionary<string, RecentActivityPreferences> RecentActivities { get; set; } = new();
    public Dictionary<string, StorageSearchPreferences> StorageSearches { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
