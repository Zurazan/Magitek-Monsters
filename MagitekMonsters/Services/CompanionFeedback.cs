using System;

namespace MagitekMonsters.Services;

public sealed record CompanionNotice(string Message, bool IsError, DateTimeOffset ExpiresAt);

public sealed class CompanionFeedback(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private CompanionNotice? notice;
    public CompanionNotice? Current => notice is { } current && (current.IsError || clock.GetUtcNow() < current.ExpiresAt) ? current : null;
    public void Observe(string message, bool error = false, bool quiet = false)
    {
        if (quiet || string.IsNullOrWhiteSpace(message)) return;
        if (!error && notice?.IsError == true) return;
        notice = new(message, error, error ? DateTimeOffset.MaxValue : clock.GetUtcNow().AddSeconds(5));
    }
    public void Clear() => notice = null;
}
