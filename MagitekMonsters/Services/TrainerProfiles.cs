using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class TrainerProfiles
{
    private sealed record Entry(Task<TrainerProfileDto> Task, DateTimeOffset Created);
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider time;
    public TrainerProfiles(TimeProvider? time = null) => this.time = time ?? TimeProvider.System;

    public Task<TrainerProfileDto> Get(string id, Func<Task<TrainerProfileDto>> fetch)
    {
        id = id.Trim();
        TaskCompletionSource<TrainerProfileDto> completion;
        lock (gate)
        {
            if (cards.TryGetValue(id, out var entry)
                && (!entry.Task.IsCompleted || time.GetUtcNow() - entry.Created < TimeSpan.FromMinutes(1))) return entry.Task;
            if (cards.Count >= 32) cards.Remove(cards.MinBy(pair => pair.Value.Created).Key);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cards[id] = new(completion.Task, time.GetUtcNow());
        }
        _ = Fetch();
        return completion.Task;

        async Task Fetch()
        {
            try { completion.TrySetResult(await fetch().ConfigureAwait(false)); }
            catch (Exception exception)
            {
                lock (gate)
                    if (cards.TryGetValue(id, out var entry) && entry.Task == completion.Task) cards.Remove(id);
                completion.TrySetException(exception);
            }
        }
    }

    public void Invalidate(string id) { lock (gate) cards.Remove(id); }
    public void Clear() { lock (gate) cards.Clear(); }
}
