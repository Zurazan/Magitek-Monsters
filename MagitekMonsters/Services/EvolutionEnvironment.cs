using Dalamud.Game;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record EvolutionEnvironment(int EorzeaMinute, string Weather, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? TerritoryTheme, long EorzeaDay)
{
    public static unsafe (EvolutionEnvironment? Environment, string Label) Read(string? territoryTheme = null)
    {
        if (!Plugin.ClientState.IsLoggedIn) return (null, "Log into FFXIV to read evolution conditions.");
        var framework = Framework.Instance();
        var weatherManager = WeatherManager.Instance();
        if (framework == null || weatherManager == null) return (null, "FFXIV conditions unavailable.");
        var time = framework->ClientTime.EorzeaTime;
        if (time <= 0) return (null, "FFXIV conditions unavailable.");
        var minute = (int)(time % 86400 / 60);
        var weatherId = weatherManager->GetCurrentWeather();
        if (weatherId == 0) return (null, "FFXIV weather unavailable.");
        var name = Plugin.DataManager.GetExcelSheet<Weather>(ClientLanguage.English).GetRow(weatherId).Name.ExtractText();
        var weather = name switch { "Rain" => "rain", "Showers" => "showers", _ => "other" };
        var period = minute >= 360 && minute < 1080 ? "Day" : "Night";
        if (minute >= 960 && minute < 1200) period += " / Dusk";
        return (new EvolutionEnvironment(minute, weather, territoryTheme, time / 86400), $"FFXIV: {minute / 60:00}:{minute % 60:00} ET | {period} | {name} | {territoryTheme ?? "Survey area"} | Moon cycle {(time / 86400) % 8 + 1}/8");
    }

    public bool Matches(string? requiredTime, string? requiredWeather, List<EvolutionEnvironmentCheckDto>? checks = null)
    {
        var timeMatches = requiredTime switch
        {
            null => true,
            "day" => EorzeaMinute >= 360 && EorzeaMinute < 1080,
            "night" => EorzeaMinute < 360 || EorzeaMinute >= 1080,
            "dusk" => EorzeaMinute >= 960 && EorzeaMinute < 1200,
            _ => false
        };
        if (!timeMatches || !(requiredWeather is null || requiredWeather == "rain" && Weather is "rain" or "showers")) return false;
        foreach (var check in checks ?? [])
        {
            if (check.Values.Count == 0 || check.Kind == "theme" && string.IsNullOrEmpty(TerritoryTheme)) return false;
            var matches = check.Kind switch
            {
                "theme" => check.Values.Contains(TerritoryTheme!),
                "moon" => check.Values.Contains((EorzeaDay % 8).ToString()),
                _ => false
            };
            if (check.Negate ? matches : !matches) return false;
        }
        return true;
    }
}
