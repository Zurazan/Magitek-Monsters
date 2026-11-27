using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MagitekMonsters.Models;

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
internal readonly record struct BattleMoveCue(int Source, int[] Targets, string Move, bool Blocked, string SourceName, string MoveName);
internal readonly record struct BattleMoveFrame(BattleMoveCue Cue, MoveEffectTrack[] Tracks, float Milliseconds, float Progress, BattleCombatantDto?[]? Actors);

internal sealed class BattleMoveEffects
{
    private const double SettleTime = .08;
    private const int MaxPendingMoves = 12;
    private sealed record Move(BattleMoveCue Cue, BattleCombatantDto?[]? Before, BattleCombatantDto?[]? After);
    private readonly record struct Playback(Move Move, MoveEffectTrack[] Tracks, float Length, double Start, double Duration)
    {
        internal double End => Start + Duration + SettleTime;
    }

    private string battle = string.Empty;
    private IReadOnlyList<string>? seen;
    private BattleCombatantDto?[]? latestActors;
    private readonly Queue<Playback> playing = new();
    private int revision;

    internal void Skip() => playing.Clear();

    internal void Reset()
    {
        battle = string.Empty;
        seen = null;
        latestActors = null;
        Skip();
        revision = 0;
    }

    internal void Observe(string id, int nextRevision, IReadOnlyList<string> log, double now, Func<BattleCombatantDto?[]>? readActors)
    {
        if (battle != id || seen is null)
        {
            battle = id;
            seen = log;
            latestActors = readActors?.Invoke();
            Skip();
            revision = nextRevision;
            return;
        }
        if (nextRevision < revision || ReferenceEquals(seen, log)) return;
        var previous = seen;
        var before = latestActors;
        latestActors = readActors?.Invoke();
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
        Advance(now);
        var nextStart = playing.Count == 0 ? now : playing.Last().End;
        foreach (var move in Moves(log, start, before).TakeLast(MaxPendingMoves))
        {
            if (playing.Count >= MaxPendingMoves) break;
            var tracks = move.Cue.Blocked ? [] : MoveEffectCatalog.Data.Value.Find(move.Cue.Move);
            var length = tracks.Length == 0 ? 220 : Math.Max(1, tracks.Max(track => track.To[0]));
            var duration = Math.Clamp(length * .00085, .30, 3);
            var clip = new Playback(move, tracks, length, nextStart, duration);
            playing.Enqueue(clip);
            nextStart = clip.End;
        }
    }

    private void Advance(double now)
    {
        while (playing.TryPeek(out var clip) && now >= clip.End) playing.Dequeue();
    }

    internal BattleMoveFrame? Frame(double now)
    {
        Advance(now);
        if (!playing.TryPeek(out var clip) || now < clip.Start) return null;
        var progress = (float)Math.Min(1, (now - clip.Start) / clip.Duration);
        var milliseconds = clip.Length * progress;
        var impact = clip.Tracks.FirstOrDefault(track => track.Name == "@target")?.From[0] ?? clip.Length * .63f;
        var actors = milliseconds < Math.Clamp(impact, clip.Length * .25f, clip.Length * .85f) ? clip.Move.Before : clip.Move.After;
        return new(clip.Move.Cue, clip.Tracks, milliseconds, progress, actors);
    }

    private static List<Move> Moves(IReadOnlyList<string> log, int start, BattleCombatantDto?[]? before)
    {
        var moves = new List<Move>();
        var actors = before is null ? null : (BattleCombatantDto?[])before.Clone();
        Move? active = null;
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
                var cue = new BattleMoveCue(source, targets.Length == 0 ? [target] : targets, SpriteNames.Normalize(parts[3]).ToLowerInvariant(), false, Name(parts[2]), parts[3]);
                active = new(cue, actors is null ? null : (BattleCombatantDto?[])actors.Clone(), actors is null ? null : (BattleCombatantDto?[])actors.Clone());
                moves.Add(active);
            }
            else if (active is not null && (line.StartsWith("|-miss|", StringComparison.Ordinal) || line.StartsWith("|-immune|", StringComparison.Ordinal)
                || line.StartsWith("|-fail|", StringComparison.Ordinal)
                || line.StartsWith("|-activate|", StringComparison.Ordinal) && line.Contains("|move: Protect", StringComparison.Ordinal)))
            {
                if (active.Cue.Targets.Length == 1) moves[^1] = active = active with { Cue = active.Cue with { Blocked = true } };
            }
            else if (line.StartsWith("|turn|", StringComparison.Ordinal)) active = null;
            else if (actors is not null) ApplyFieldLine(line, actors, active?.After);
        }
        return moves;
    }

    private static string Name(string ident) => ident[(ident.IndexOf(':') + 1)..].Trim();

    private static void ApplyFieldLine(string line, BattleCombatantDto?[] actors, BattleCombatantDto?[]? impact)
    {
        if (!line.StartsWith('|')) return;
        var parts = line.Split('|');
        if (parts.Length < 3 || Slot(parts[2]) is not int slot) return;
        var name = Name(parts[2]);
        var value = parts.ElementAtOrDefault(3) ?? "";
        var actor = actors[slot];
        if (parts[1] == "swap" && int.TryParse(value, out var position) && position is >= 0 and < 4)
        {
            var other = slot / 4 * 4 + position;
            (actors[slot], actors[other]) = (actors[other], actors[slot]);
            return;
        }
        if (parts[1] is "switch" or "drag" or "replace" && parts.Length >= 5)
        {
            var details = value.Split(',', StringSplitOptions.TrimEntries);
            var level = 100;
            foreach (var part in details)
                if (part.StartsWith('L') && int.TryParse(part.AsSpan(1), out var parsed)) { level = parsed; break; }
            actors[slot] = new() { Ident = parts[2], Name = name, Species = details[0], Shiny = details.Contains("shiny"), Active = true,
                Level = level, Condition = parts[4] };
            return;
        }
        if (actor is null || actor.Name != name) return;
        if (parts[1] is "detailschange" or "-formechange" or "-terastallize")
        {
            var tera = parts[1] == "-terastallize";
            actors[slot] = CopyActor(actor, actor.Condition, tera ? actor.Species : value.Split(',')[0], tera ? value : actor.TeraType);
            return;
        }
        var condition = parts[1] switch
        {
            "-damage" or "-heal" or "-sethp" when value.Length > 0 => value,
            "faint" => "0 fnt",
            "-status" when value.Length > 0 => actor.Condition.Split(' ')[0] + " " + value,
            "-curestatus" => actor.Condition.Split(' ')[0],
            _ => null,
        };
        if (condition is null) return;
        var changed = CopyActor(actor, condition, actor.Species, actor.TeraType);
        actors[slot] = changed;
        if (impact?[slot] is { } previous && previous.Ident == actor.Ident)
            impact[slot] = CopyActor(previous, condition, previous.Species, previous.TeraType);
        if (parts[1] == "-sethp" && parts.Length > 5 && Slot(parts[4]) is not null)
            ApplyFieldLine($"|-sethp|{parts[4]}|{parts[5]}", actors, impact);
    }

    private static BattleCombatantDto CopyActor(BattleCombatantDto actor, string condition, string species, string teraType) => new()
    {
        Ident = actor.Ident, Name = actor.Name, Species = species, Level = actor.Level, Shiny = actor.Shiny, TeraType = teraType,
        Condition = condition, Fainted = condition.EndsWith(" fnt", StringComparison.Ordinal), Active = actor.Active,
    };

    private static int? Slot(string value)
    {
        if (value.Length < 2 || value[0] != 'p' || value[1] is not ('1' or '2') || value.Length > 2 && value[2] is not (>= 'a' and <= 'd' or ':')) return null;
        var slot = value.Length >= 3 && value[2] != ':' ? value[2] - 'a' : 0;
        return (value[1] - '1') * 4 + slot;
    }
}
