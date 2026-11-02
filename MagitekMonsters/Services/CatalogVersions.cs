using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MagitekMonsters.Services;

public sealed class CatalogVersions
{
    private sealed record Reply(string? Revision);
    private readonly object gate = new();
    private readonly ConditionalWeakTable<object, Reply> replies = new();
    private long sequence;
    private long observed;
    private long reset;
    private long generation;
    private string? revision;

    public long Generation { get { lock (gate) return generation; } }
    public string? Revision { get { lock (gate) return revision; } }
    public long BeginRequest() { lock (gate) return ++sequence; }

    public void Observe(string? value, long request)
    {
        var normalized = Normalize(value);
        if (normalized is null) return;
        lock (gate)
        {
            if (request < observed) return;
            observed = request;
            if (revision == normalized) return;
            revision = normalized;
            generation++;
        }
    }

    public void Greeting(string? value)
    {
        lock (gate)
        {
            observed = ++sequence;
            var normalized = Normalize(value);
            if (revision == normalized) return;
            revision = normalized;
            generation++;
        }
    }

    public void Record(object response, string? value, long request)
    {
        lock (gate) { if (request >= reset) replies.Add(response, new(Normalize(value))); }
    }

    public bool TryApply(IEnumerable<object> responses, Action apply)
    {
        lock (gate)
        {
            foreach (var response in responses)
                if (!replies.TryGetValue(response, out var reply) || reply.Revision != revision) return false;
            apply();
            return true;
        }
    }

    public void Reset()
    {
        lock (gate) { reset = observed = ++sequence; revision = null; generation++; replies.Clear(); }
    }

    private static string? Normalize(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : null;
}
