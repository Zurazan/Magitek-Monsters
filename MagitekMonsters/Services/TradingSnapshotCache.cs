using System.Net.Http;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

internal sealed class TradingSnapshotCache
{
    internal sealed record Entry(string ETag, TradingSnapshotDto Snapshot);
    internal sealed record Request(long Generation, long Sequence, Entry? Basis);
    private readonly object gate = new();
    private Entry? current;
    private string? token;
    private long generation, sequence, accepted;

    public Request Begin(string accessToken)
    {
        lock (gate)
        {
            if (token != accessToken) { ResetLocked(); token = accessToken; }
            return new(generation, ++sequence, current);
        }
    }
    public void Reset() { lock (gate) { ResetLocked(); token = null; } }
    public void Invalidate(Request request) { lock (gate) if (request.Generation == generation) ResetLocked(); }
    private void ResetLocked() { current = null; generation++; sequence = accepted = 0; }
    public TradingSnapshotDto Reuse(Request request, string? etag)
    {
        lock (gate)
        {
            if (request.Generation != generation || request.Basis is not { } basis || etag is not null && etag != basis.ETag)
                throw new HttpRequestException("The server returned Trading data without a matching cached response.");
            if (request.Sequence >= accepted) { current = basis; accepted = request.Sequence; }
            return basis.Snapshot;
        }
    }
    public void Complete(Request request, string? etag, TradingSnapshotDto snapshot)
    {
        lock (gate)
        {
            if (request.Generation != generation || request.Sequence < accepted) return;
            accepted = request.Sequence;
            current = Valid(etag) ? new(etag!, snapshot) : null;
        }
    }
    private static bool Valid(string? etag)
    {
        if (etag is not { Length: 66 } || etag[0] != '"' || etag[^1] != '"') return false;
        for (var index = 1; index < etag.Length - 1; index++)
            if (etag[index] is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}
