using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class TrainerRosterCache
{
    internal sealed record Entry(string Owner, string StorageVersion, string OptionsVersion, List<PokemonDto> Storage,
        List<EvolutionOptionDto> Evolutions, List<FormOptionDto> Forms, List<FusionOptionDto> Fusions,
        TrainerOptionGroupsDto? OptionGroups, HashSet<string>? PartyIds);
    private sealed record OptionGroupUpdate(TrainerOptionGroupsDto Groups, HashSet<string> Party, HashSet<string>? PreviousParty,
        HashSet<string>? Incoming, HashSet<string>? Removed);
    public sealed class Request
    {
        internal Request(long generation, long sequence, Entry? basis) { Generation = generation; Sequence = sequence; Basis = basis; }
        internal long Generation { get; }
        internal long Sequence { get; }
        internal Entry? Basis { get; }
        public void ApplyHeaders(HttpRequestHeaders headers, string? storageDeltaVersion = null)
        {
            headers.TryAddWithoutValidation(ProtocolInfo.PartyOptionsVersionHeader, Basis?.OptionGroups?.PartyVersion ?? "0");
            headers.TryAddWithoutValidation(ProtocolInfo.BoxedOptionsVersionHeader, Basis?.OptionGroups?.BoxedVersion ?? "0");
            if (Basis is not { } entry) return;
            headers.TryAddWithoutValidation(ProtocolInfo.StorageVersionHeader, entry.StorageVersion);
            headers.TryAddWithoutValidation(ProtocolInfo.OptionsVersionHeader, entry.OptionsVersion);
            if (storageDeltaVersion is not null)
            {
                headers.TryAddWithoutValidation(ProtocolInfo.StorageDeltaHeader, storageDeltaVersion);
                headers.TryAddWithoutValidation(ProtocolInfo.OptionsDeltaHeader, ProtocolInfo.OptionsDeltaVersion);
            }
        }
    }
    private readonly object gate = new();
    private static readonly Comparer<PokemonDto> StorageSlotOrder = Comparer<PokemonDto>.Create(
        (left, right) => Position(left).CompareTo(Position(right)));
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
        if (metadata.StorageDelta is { } delta)
        {
            if (metadata.StorageIncluded || basis?.Owner != snapshot.User.Id || basis.StorageVersion != delta.BaseVersion
                || basis.StorageVersion == metadata.StorageVersion || snapshot.Storage.Count != 0)
                throw new TrainerRosterCacheException();
            snapshot.Storage = ApplyStorageDelta(basis.Storage, delta);
        }
        else if (!metadata.StorageIncluded)
        {
            if (basis?.Owner != snapshot.User.Id || basis.StorageVersion != metadata.StorageVersion || snapshot.Storage.Count != 0)
                throw new TrainerRosterCacheException();
            snapshot.Storage = basis.Storage;
        }
        HashSet<string>? partyIds = null;
        if (metadata.OptionGroups is { } groups)
        {
            var grouped = ReadOptionGroupUpdate(snapshot, metadata, groups, basis);
            partyIds = grouped.Party;
            snapshot.EvolutionOptions = ApplyOptionGroups(snapshot.EvolutionOptions, basis?.Evolutions, grouped, static option => option.PokemonId);
            snapshot.FormOptions = ApplyOptionGroups(snapshot.FormOptions, basis?.Forms, grouped, static option => option.PokemonId);
            snapshot.FusionOptions = ApplyOptionGroups(snapshot.FusionOptions, basis?.Fusions, grouped, static option => option.PokemonId);
        }
        else if (metadata.OptionsDeltaBaseVersion is { } optionsBase)
        {
            if (metadata.OptionsIncluded || basis?.Owner != snapshot.User.Id || basis.OptionsVersion != optionsBase
                || metadata.OptionsVersion == optionsBase || metadata.StorageDelta is not { } membership
                || membership.AddedPokemon is null && membership.RemovedPokemonIds is null || snapshot.Party is null)
                throw new TrainerRosterCacheException();
            var incoming = membership.AddedPokemon?.Select(pokemon => pokemon.Id).ToHashSet(StringComparer.Ordinal);
            var removed = membership.RemovedPokemonIds is { } ids ? new HashSet<string>(ids, StringComparer.Ordinal) : null;
            var party = new HashSet<string>(snapshot.Party.ConvertAll(pokemon => pokemon.Id), StringComparer.Ordinal);
            snapshot.EvolutionOptions = ApplyOptions(basis.Evolutions, snapshot.EvolutionOptions, incoming, removed, party, static option => option.PokemonId);
            snapshot.FormOptions = ApplyOptions(basis.Forms, snapshot.FormOptions, incoming, removed, party, static option => option.PokemonId);
            snapshot.FusionOptions = ApplyOptions(basis.Fusions, snapshot.FusionOptions, incoming, removed, party, static option => option.PokemonId);
        }
        else if (!metadata.OptionsIncluded)
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
                snapshot.EvolutionOptions, snapshot.FormOptions, snapshot.FusionOptions, metadata.OptionGroups, partyIds);
            accepted = request.Sequence;
        }
    }

    private static OptionGroupUpdate ReadOptionGroupUpdate(TrainerSnapshotDto snapshot, TrainerRosterCacheDto metadata, TrainerOptionGroupsDto groups, Entry? basis)
    {
        var boxedBase = groups.BoxedDeltaBaseVersion;
        if (!Valid(groups.PartyVersion) || !Valid(groups.BoxedVersion) || metadata.OptionsDeltaBaseVersion is not null
            || metadata.OptionsIncluded != (groups.PartyIncluded && groups.BoxedIncluded) || snapshot.Party is null)
            throw new TrainerRosterCacheException();
        if ((!groups.PartyIncluded || !groups.BoxedIncluded) && (basis?.Owner != snapshot.User.Id || basis.PartyIds is null))
            throw new TrainerRosterCacheException();
        if (!groups.PartyIncluded && basis?.OptionGroups?.PartyVersion != groups.PartyVersion
            || !groups.BoxedIncluded && basis?.OptionGroups?.BoxedVersion != (boxedBase ?? groups.BoxedVersion))
            throw new TrainerRosterCacheException();
        HashSet<string>? incoming = null;
        HashSet<string>? removed = null;
        if (boxedBase is not null)
        {
            if (groups.BoxedIncluded || boxedBase == groups.BoxedVersion || metadata.StorageDelta is not { } membership
                || membership.AddedPokemon is null && membership.RemovedPokemonIds is null)
                throw new TrainerRosterCacheException();
            incoming = membership.AddedPokemon?.Select(pokemon => pokemon.Id).ToHashSet(StringComparer.Ordinal);
            if (membership.RemovedPokemonIds is { } ids) removed = new(ids, StringComparer.Ordinal);
        }
        var party = new HashSet<string>(snapshot.Party.ConvertAll(pokemon => pokemon.Id), StringComparer.Ordinal);
        return new(groups, party, basis?.PartyIds, incoming, removed);
    }

    private static List<T> ApplyOptionGroups<T>(List<T> sent, List<T>? previous, OptionGroupUpdate update, Func<T, string> pokemonId) where T : class
    {
        var groups = update.Groups;
        if (groups.PartyIncluded && groups.BoxedIncluded) return sent;
        var hasBoxedDelta = groups.BoxedDeltaBaseVersion is not null;
        if (!groups.PartyIncluded && !groups.BoxedIncluded && !hasBoxedDelta)
            return sent.Count == 0 ? previous! : throw new TrainerRosterCacheException();
        var party = new List<T>();
        var boxed = new List<T>();
        foreach (var option in sent)
        {
            if (option is null) throw new TrainerRosterCacheException();
            if (update.Party.Contains(pokemonId(option)))
            {
                if (!groups.PartyIncluded) throw new TrainerRosterCacheException();
                party.Add(option);
            }
            else
            {
                if (!groups.BoxedIncluded && !hasBoxedDelta) throw new TrainerRosterCacheException();
                boxed.Add(option);
            }
        }
        if (!groups.PartyIncluded) party = previous!.FindAll(option => update.PreviousParty!.Contains(pokemonId(option)));
        if (!groups.BoxedIncluded)
        {
            var previousBoxed = previous!.FindAll(option => !update.PreviousParty!.Contains(pokemonId(option)));
            boxed = hasBoxedDelta ? ApplyOptions(previousBoxed, boxed, update.Incoming, update.Removed, update.Party, pokemonId) : previousBoxed;
        }
        party.AddRange(boxed);
        return party;
    }

    private static List<T> ApplyOptions<T>(List<T> previous, List<T> additions, HashSet<string>? incoming,
        HashSet<string>? removed, HashSet<string> party, Func<T, string> pokemonId) where T : class
    {
        if (additions.Count == 0 && removed is null) return previous;
        foreach (var option in additions)
            if (option is null || incoming?.Contains(pokemonId(option)) != true) throw new TrainerRosterCacheException();
        var sorted = additions.Count <= 1 || incoming?.Count == 1 ? additions : additions.OrderBy(pokemonId, StringComparer.Ordinal).ToList();
        var result = new List<T>(previous.Count + additions.Count);
        var index = 0;
        foreach (var option in previous)
        {
            var id = pokemonId(option);
            if (removed?.Contains(id) == true) continue;
            while (index < sorted.Count && !party.Contains(id) && string.CompareOrdinal(pokemonId(sorted[index]), id) < 0)
                result.Add(sorted[index++]);
            result.Add(option);
        }
        for (; index < sorted.Count; index++) result.Add(sorted[index]);
        return result;
    }

    private static List<PokemonDto> ApplyStorageDelta(List<PokemonDto> storage, TrainerStorageDeltaDto delta)
    {
        if (delta.TeamPlacement || delta.AddedPokemon is not null || delta.RemovedPokemonIds is not null)
            return ApplyStorageMembership(storage, delta);
        if (delta.UpdatedPokemon is { } edits)
        {
            if (delta.Positions is not { Count: 0 }) throw new TrainerRosterCacheException();
            return ApplyStorageEdits(storage, edits);
        }
        if (delta.Positions is not { Count: > 0 and <= StorageLimits.TotalCapacity } || delta.Positions.Count > storage.Count)
            throw new TrainerRosterCacheException();
        var positions = new Dictionary<string, StoragePositionDto>(delta.Positions.Count, StringComparer.Ordinal);
        var destinations = new HashSet<int>();
        foreach (var position in delta.Positions)
        {
            if (position is null || string.IsNullOrEmpty(position.PokemonId)
                || position.BoxNumber is < 1 or > StorageLimits.BoxCount || position.BoxSlot is < 1 or > StorageLimits.BoxCapacity
                || !positions.TryAdd(position.PokemonId, position)
                || !destinations.Add(StoragePositions.Position(position.BoxNumber, position.BoxSlot)))
                throw new TrainerRosterCacheException();
        }
        var stationary = new List<PokemonDto>(storage.Count - positions.Count);
        var moved = new List<PokemonDto>(positions.Count);
        var previousSlot = 0;
        foreach (var pokemon in storage)
        {
            var slot = Position(pokemon);
            if (slot <= previousSlot) throw new TrainerRosterCacheException();
            previousSlot = slot;
            if (positions.Remove(pokemon.Id, out var position))
            {
                if (pokemon.PartySlot is not null) throw new TrainerRosterCacheException();
                moved.Add(pokemon.WithStoragePosition(position.BoxNumber, position.BoxSlot));
                continue;
            }
            if (destinations.Contains(slot)) throw new TrainerRosterCacheException();
            stationary.Add(pokemon);
        }
        if (positions.Count != 0) throw new TrainerRosterCacheException();
        moved.Sort(StorageSlotOrder);
        if (stationary.Count == 0) return moved;
        var updated = new List<PokemonDto>(storage.Count);
        var nextMove = 0;
        foreach (var pokemon in stationary)
        {
            while (nextMove < moved.Count && Position(moved[nextMove]) < Position(pokemon)) updated.Add(moved[nextMove++]);
            updated.Add(pokemon);
        }
        for (; nextMove < moved.Count; nextMove++) updated.Add(moved[nextMove]);
        return updated;
    }

    private static List<PokemonDto> ApplyStorageEdits(List<PokemonDto> storage, IReadOnlyList<PokemonDto> edits)
    {
        if (edits.Count is < 1 or > StorageLimits.MaximumBatch || edits.Count > storage.Count)
            throw new TrainerRosterCacheException();
        var updated = new List<PokemonDto>(storage);
        foreach (var pokemon in edits)
        {
            if (pokemon is null || pokemon.PartySlot is not null || pokemon.BoxNumber is < 1 or > StorageLimits.BoxCount
                || pokemon.BoxSlot is < 1 or > StorageLimits.BoxCapacity || string.IsNullOrEmpty(pokemon.Id))
                throw new TrainerRosterCacheException();
            var index = storage.BinarySearch(pokemon, StorageSlotOrder);
            if (index < 0) throw new TrainerRosterCacheException();
            var before = storage[index];
            if (!ReferenceEquals(before, updated[index]) || before.Id != pokemon.Id || before.PartySlot is not null
                || before.Appearance != pokemon.Appearance || before.Shiny != pokemon.Shiny
                || before.TradeLockId != pokemon.TradeLockId || before.FusionHostId != pokemon.FusionHostId)
                throw new TrainerRosterCacheException();
            updated[index] = pokemon;
        }
        return updated;
    }

    private static List<PokemonDto> ApplyStorageMembership(List<PokemonDto> storage, TrainerStorageDeltaDto delta)
    {
        if (delta.Positions is not { Count: 0 } || delta.UpdatedPokemon is not null) throw new TrainerRosterCacheException();
        if (delta.TeamPlacement) return ApplyTeamMembership(storage, delta);
        if (delta.AddedPokemon is { } additions)
        {
            if (additions.Count != 1) throw new TrainerRosterCacheException();
            var pokemon = additions[0];
            if (pokemon is null || pokemon.PartySlot is not null
                || pokemon.BoxNumber is < 1 or > StorageLimits.BoxCount || pokemon.BoxSlot is < 1 or > StorageLimits.BoxCapacity
                || string.IsNullOrEmpty(pokemon.Id) || storage.Exists(entry => entry.Id == pokemon.Id))
                throw new TrainerRosterCacheException();
            var index = storage.BinarySearch(pokemon, StorageSlotOrder);
            if (delta.RemovedPokemonIds is { } outgoing)
            {
                if (outgoing.Count != 1 || index < 0 || storage[index].Id != outgoing[0] || storage[index].PartySlot is not null)
                    throw new TrainerRosterCacheException();
                var traded = new List<PokemonDto>(storage);
                traded[index] = pokemon;
                return traded;
            }
            if (index >= 0 || storage.Count >= StorageLimits.TotalCapacity) throw new TrainerRosterCacheException();
            var withCatch = new List<PokemonDto>(storage.Count + 1);
            withCatch.AddRange(storage);
            withCatch.Insert(~index, pokemon);
            return withCatch;
        }
        return RemoveStorageMembers(storage, delta.RemovedPokemonIds, StorageLimits.MaximumBatch);
    }

    private static List<PokemonDto> ApplyTeamMembership(List<PokemonDto> storage, TrainerStorageDeltaDto delta)
    {
        if (delta.AddedPokemon is { Count: 0 or > 6 } || delta.RemovedPokemonIds is { Count: 0 or > 6 }
            || delta.AddedPokemon is null && delta.RemovedPokemonIds is null)
            throw new TrainerRosterCacheException();
        var result = delta.RemovedPokemonIds is null ? new List<PokemonDto>(storage) : RemoveStorageMembers(storage, delta.RemovedPokemonIds, 6);
        foreach (var pokemon in delta.AddedPokemon ?? [])
        {
            if (pokemon is null || pokemon.PartySlot is not null
                || pokemon.BoxNumber is < 1 or > StorageLimits.BoxCount || pokemon.BoxSlot is < 1 or > StorageLimits.BoxCapacity
                || string.IsNullOrEmpty(pokemon.Id) || result.Count >= StorageLimits.TotalCapacity
                || storage.Exists(entry => entry.Id == pokemon.Id) || result.Exists(entry => entry.Id == pokemon.Id))
                throw new TrainerRosterCacheException();
            var index = result.BinarySearch(pokemon, StorageSlotOrder);
            if (index >= 0) throw new TrainerRosterCacheException();
            result.Insert(~index, pokemon);
        }
        return result;
    }

    private static List<PokemonDto> RemoveStorageMembers(List<PokemonDto> storage, IReadOnlyList<string>? removed, int limit)
    {
        if (removed is not { Count: > 0 } || removed.Count > limit || removed.Count > storage.Count)
            throw new TrainerRosterCacheException();
        var removeIds = new HashSet<string>(removed, StringComparer.Ordinal);
        if (removeIds.Count != removed.Count || removeIds.Contains("") || removeIds.Contains(null!)) throw new TrainerRosterCacheException();
        var remaining = new List<PokemonDto>(storage.Count - removed.Count);
        foreach (var pokemon in storage)
        {
            if (!removeIds.Remove(pokemon.Id)) remaining.Add(pokemon);
            else if (pokemon.PartySlot is not null) throw new TrainerRosterCacheException();
        }
        if (removeIds.Count != 0) throw new TrainerRosterCacheException();
        return remaining;
    }

    private static int Position(PokemonDto pokemon) => StoragePositions.Position(pokemon.BoxNumber, pokemon.BoxSlot);

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
