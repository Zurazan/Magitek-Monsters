using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MagitekMonsters.Services;

internal sealed class MoveEffectCatalog
{
    internal static readonly Lazy<MoveEffectCatalog> Data = new(Load);

    public Dictionary<string, int> Moves { get; init; } = new(StringComparer.Ordinal);
    public MoveEffectTrack[][] Clips { get; init; } = [];
    public Dictionary<string, MoveEffectImage> Effects { get; init; } = new(StringComparer.Ordinal);

    internal MoveEffectTrack[] Find(string move) => Moves.TryGetValue(move, out var index) ? Clips[index] : [];

    private static MoveEffectCatalog Load()
    {
        using var stream = typeof(MoveEffectCatalog).Assembly.GetManifestResourceStream("MagitekMonsters.MoveEffects.json");
        if (stream is null) return new();
        try { return JsonSerializer.Deserialize<MoveEffectCatalog>(stream, JsonSerializerOptions.Web) ?? new(); }
        catch (Exception error) when (error is IOException or JsonException)
        {
            Plugin.Log?.Warning(error, "Could not load battle move effects.");
            return new();
        }
    }
}

internal sealed record MoveEffectTrack(string Name, float[] From, float[] To, string Ease);
internal sealed record MoveEffectImage(string? File, float W, float H);
internal readonly record struct BattleMoveCue(int Source, int[] Targets, string Move, bool Blocked, string SourceName);
internal readonly record struct BattleMoveFrame(BattleMoveCue Cue, MoveEffectTrack[] Tracks, float Milliseconds, float Progress);

internal sealed class BattleMoveEffects
{
    private readonly record struct Playback(BattleMoveCue Cue, MoveEffectTrack[] Tracks, float Length, double Start, double Duration);

    private string battle = string.Empty;
    private IReadOnlyList<string>? seen;
    private Playback[] playing = [];
    private int revision;

    internal void Reset()
    {
        battle = string.Empty;
        seen = null;
        playing = [];
        revision = 0;
    }

    internal void Observe(string id, int nextRevision, IReadOnlyList<string> log, double now, bool finished)
    {
        if (battle != id || seen is null)
        {
            battle = id;
            seen = log;
            playing = [];
            revision = nextRevision;
            return;
        }
        if (nextRevision < revision || ReferenceEquals(seen, log)) return;
        var previous = seen;
        seen = log;
        revision = nextRevision;
        if (previous.Count == 0 || log.Count == 0) return;

        var start = Math.Min(previous.Count, log.Count);
        while (start > 0)
        {
            var i = 0;
            while (i < start && previous[previous.Count - start + i] == log[i]) i++;
            if (i == start) break;
            start--;
        }
        if (start == 0 || start == log.Count) return;
        var cues = Moves(log, start).TakeLast(finished ? 1 : 6).ToArray();
        if (cues.Length == 0) return;

        var batch = new Playback[cues.Length];
        var available = finished ? .65 : Math.Min(.95, 1.8 / cues.Length);
        for (var i = 0; i < cues.Length; i++)
        {
            var cue = cues[i];
            var tracks = cue.Blocked ? [] : MoveEffectCatalog.Data.Value.Find(cue.Move);
            var length = tracks.Length == 0 ? 220 : Math.Max(1, tracks.Max(track => track.To[0]));
            var duration = Math.Clamp(length * .0009, .23, available);
            batch[i] = new(cue, tracks, length, now, duration);
            now += duration;
        }
        playing = batch;
    }

    internal BattleMoveFrame? Frame(double now)
    {
        foreach (var clip in playing)
        {
            var elapsed = now - clip.Start;
            if (elapsed < 0 || elapsed >= clip.Duration) continue;
            var progress = (float)(elapsed / clip.Duration);
            return new(clip.Cue, clip.Tracks, clip.Length * progress, progress);
        }
        return null;
    }

    private static List<BattleMoveCue> Moves(IReadOnlyList<string> log, int start)
    {
        var cues = new List<BattleMoveCue>();
        for (var i = start; i < log.Count; i++)
        {
            var line = log[i];
            if (line.StartsWith("|move|", StringComparison.Ordinal))
            {
                var parts = line.Split('|');
                if (parts.Length < 5 || Slot(parts[2]) is not int source) continue;
                var target = Slot(parts[4]) ?? source;
                var spread = parts.Skip(5).FirstOrDefault(part => part.StartsWith("[spread] ", StringComparison.Ordinal));
                var targets = spread is null ? [target] : spread[9..].Split(',').Select(value => Slot(value.Trim()))
                    .OfType<int>().Distinct().ToArray();
                var name = parts[2][(parts[2].IndexOf(':') + 1)..].Trim();
                cues.Add(new(source, targets.Length == 0 ? [target] : targets, SpriteNames.Normalize(parts[3]).ToLowerInvariant(), false, name));
            }
            else if (cues.Count > 0 && (line.StartsWith("|-miss|", StringComparison.Ordinal) || line.StartsWith("|-immune|", StringComparison.Ordinal)
                || line.StartsWith("|-fail|", StringComparison.Ordinal)
                || line.StartsWith("|-activate|", StringComparison.Ordinal) && line.Contains("|move: Protect", StringComparison.Ordinal)))
            {
                var cue = cues[^1];
                if (cue.Targets.Length == 1) cues[^1] = cue with { Blocked = true };
            }
        }
        return cues;
    }

    private static int? Slot(string value)
    {
        if (value.Length < 2 || value[0] != 'p' || value[1] is not ('1' or '2')) return null;
        var slot = value.Length >= 3 && value[2] is >= 'a' and <= 'd' ? value[2] - 'a' : 0;
        return (value[1] - '1') * 4 + slot;
    }
}
