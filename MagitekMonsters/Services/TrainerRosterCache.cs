using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class TrainerRosterCache
{
    internal sealed record Entry(string Owner, string StorageVersion, string OptionsVersion, List<PokemonDto> Storage,
        List<EvolutionOptionDto> Evolutions, List<FormOptionDto> Forms, List<FusionOptionDto> Fusions);
    public sealed class Request
    {
        internal Request(long generation, long sequence, Entry? basis) { Generation = generation; Sequence = sequence; Basis = basis; }
        internal long Generation { get; }
        internal long Sequence { get; }
        internal Entry? Basis { get; }
        public void ApplyHeaders(HttpRequestHeaders headers)
        {
            if (Basis is not { } entry) return;
            headers.TryAddWithoutValidation(ProtocolInfo.StorageVersionHeader, entry.StorageVersion);
            headers.TryAddWithoutValidation(ProtocolInfo.OptionsVersionHeader, entry.OptionsVersion);
        }
    }
    private readonly object gate = new();
    private Entry? current;
    private string? token;
    private long generation, sequence, accepted;

    public Request Begin(string accessToken, bool full = false, Request? expected = null)
    {
        lock (gate)
        {
            if (expected is not null && (expected.Generation != generation || token != accessToken)) throw new TrainerRosterCacheException();
            if (token != accessToken) { ResetLocked(); token = accessToken; }
            return new(generation, ++sequence, full ? null : current);
        }
    }
    public bool IsCurrent(Request request) { lock (gate) return request.Generation == generation; }
    public void Reset() { lock (gate) { ResetLocked(); token = null; } }
    private void ResetLocked() { generation++; current = null; accepted = sequence = 0; }

    public void Complete(Request request, TrainerSnapshotDto snapshot)
    {
        var metadata = snapshot.RosterCache;
        if (metadata is null)
        {
            lock (gate) if (request.Generation == generation && request.Sequence >= accepted) { current = null; accepted = request.Sequence; }
            return;
        }
        if (metadata.Version != 1 || !Valid(metadata.StorageVersion) || !Valid(metadata.OptionsVersion) || string.IsNullOrEmpty(snapshot.User.Id))
            throw new TrainerRosterCacheException();
        if (snapshot.Storage is null || snapshot.EvolutionOptions is null || snapshot.FormOptions is null || snapshot.FusionOptions is null)
            throw new TrainerRosterCacheException();
        var basis = request.Basis;
        if (!metadata.StorageIncluded)
        {
            if (basis?.Owner != snapshot.User.Id || basis.StorageVersion != metadata.StorageVersion || snapshot.Storage.Count != 0)
                throw new TrainerRosterCacheException();
            snapshot.Storage = basis.Storage;
        }
        if (!metadata.OptionsIncluded)
        {
            if (basis?.Owner != snapshot.User.Id || basis.OptionsVersion != metadata.OptionsVersion
                || snapshot.EvolutionOptions.Count != 0 || snapshot.FormOptions.Count != 0 || snapshot.FusionOptions.Count != 0)
                throw new TrainerRosterCacheException();
            snapshot.EvolutionOptions = basis.Evolutions; snapshot.FormOptions = basis.Forms; snapshot.FusionOptions = basis.Fusions;
        }
        lock (gate)
        {
            if (request.Generation != generation || request.Sequence < accepted) return;
            current = new(snapshot.User.Id, metadata.StorageVersion, metadata.OptionsVersion, snapshot.Storage,
                snapshot.EvolutionOptions, snapshot.FormOptions, snapshot.FusionOptions);
            accepted = request.Sequence;
        }
    }
    private static bool Valid(string value)
    {
        if (value is not { Length: 64 }) return false;
        foreach (var letter in value) if (letter is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}

public sealed class TrainerRosterCacheException : Exception
{
    public TrainerRosterCacheException() : base("Trainer data needs a full refresh.") { }
}
