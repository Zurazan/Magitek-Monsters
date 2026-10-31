using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class SearchCooldown(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private long started;
    private double duration;

    public int RemainingSeconds => (int)Math.Ceiling(Math.Max(0, duration - clock.GetElapsedTime(started).TotalSeconds));

    public void Observe(double seconds)
    {
        duration = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 3600) : 0;
        started = clock.GetTimestamp();
    }

    public static double? RetryDelay(ApiErrorDto? error, RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        var seconds = error?.RetryAfterSeconds ?? header?.Delta?.TotalSeconds ?? (header?.Date - now)?.TotalSeconds;
        if (seconds is { } explicitSeconds && double.IsFinite(explicitSeconds)) return Math.Clamp(explicitSeconds, 0, 3600);
        if (error?.Error is not ("search_cooldown" or "paradox_cooldown" or "event_cooldown")) return null;
        var match = Regex.Match(error.Message, @"\bin ([0-9]+(?:\.[0-9]+)?) seconds?\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 3600) : null;
    }
}
