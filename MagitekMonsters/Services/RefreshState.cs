using System;
using System.Collections.Generic;

namespace MagitekMonsters.Services;

public sealed class RefreshState
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> known = [];
    private readonly Dictionary<string, int> knownTrainers = [];
    private readonly Dictionary<string, int> battles = [];
    private readonly Dictionary<string, int> trainers = [];
    private readonly HashSet<string> changes = [];
    private readonly HashSet<string> coveredChanges = [];
    private readonly Queue<string> recentChanges = new();
    private bool trainer;
    private long generation;

    public long Generation { get { lock (gate) return generation; } }
    public bool IsCurrent(long value) { lock (gate) return generation == value; }
    public void Invalidate() { lock (gate) generation++; }
    public void Reset() { lock (gate) { generation++; known.Clear(); knownTrainers.Clear(); battles.Clear(); trainers.Clear(); changes.Clear(); coveredChanges.Clear(); recentChanges.Clear(); trainer = false; } }
    public void Trainer(string? id = null, int? revision = null, string? changeId = null)
    {
        lock (gate)
        {
            if (Guid.TryParseExact(changeId, "D", out var change))
            {
                var key = change.ToString("D");
                if (coveredChanges.Contains(key)) return;
                if (changes.Count >= 128) { changes.Clear(); trainer = true; }
                changes.Add(key);
                return;
            }
            if (id is null || revision is null) { trainer = true; return; }
            if (!knownTrainers.TryGetValue(id, out var seen) || revision > seen)
                trainers[id] = Math.Max(trainers.GetValueOrDefault(id), revision.Value);
        }
    }
    public void CoverTrainerChange(string changeId)
    {
        if (!Guid.TryParseExact(changeId, "D", out var change)) return;
        var key = change.ToString("D");
        lock (gate)
        {
            changes.Remove(key);
            if (!coveredChanges.Add(key)) return;
            recentChanges.Enqueue(key);
            if (recentChanges.Count > 128) coveredChanges.Remove(recentChanges.Dequeue());
        }
    }
    public void Battle(string id, int? revision)
    {
        lock (gate)
        {
            if (revision is null || !known.TryGetValue(id, out var seen) || revision > seen)
                battles[id] = Math.Max(battles.GetValueOrDefault(id), revision ?? 0);
        }
    }
    public void Acknowledge(string id, int? revision, bool trainerCovered = false)
    {
        lock (gate)
        {
            if (revision is { } value)
            {
                if (!known.ContainsKey(id) && known.Count >= 64) known.Clear();
                known[id] = Math.Max(known.GetValueOrDefault(id), value);
                if (trainerCovered)
                {
                    if (!knownTrainers.ContainsKey(id) && knownTrainers.Count >= 64) knownTrainers.Clear();
                    knownTrainers[id] = Math.Max(knownTrainers.GetValueOrDefault(id), value);
                }
            }
            var seen = known.GetValueOrDefault(id);
            if (battles.TryGetValue(id, out var requested) && requested <= seen) battles.Remove(id);
            if (trainerCovered && trainers.TryGetValue(id, out requested) && requested <= seen) trainers.Remove(id);
        }
    }
    public bool TakeTrainer()
    {
        lock (gate)
        {
            var requested = trainer || trainers.Count > 0 || changes.Count > 0;
            trainer = false; trainers.Clear(); changes.Clear();
            return requested;
        }
    }
    public bool TakeBattle(string id) { lock (gate) return battles.Remove(id); }
}
