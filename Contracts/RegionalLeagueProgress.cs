using System;
using System.Collections.Generic;
using System.Linq;

namespace MagitekMonsters.Contracts;

public static class RegionalLeagueProgress
{
    public static bool HasChampion(IEnumerable<string> clears, string? regionId = null) => clears.Any(id =>
        id == "champion-eidolon" && (regionId is null or "kanto")
        || id.Split('-', 4) is ["regional", var region, "champion", { Length: > 0 }] && (regionId is null || region == regionId));
}
