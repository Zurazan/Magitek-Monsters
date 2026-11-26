using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MagitekMonsters.Services;

public sealed class BattleLogArchive
{
    public const int DefaultRetentionDays = 7;
    private const int CopyLineLimit = 20_000;
    private const string DateFormat = "yyyy-MM-dd";
    private const string Gap = "[Some battle events were skipped between updates.]";
    private static readonly HashSet<string> OmittedEvents = new(StringComparer.Ordinal)
    {
        "", "request", "split", "player", "showteam", "t:", "debug", "upkeep", "pp_update", "uhtml", "uhtmlchange"
    };

    private readonly Dictionary<string, Transcript> transcripts = new(StringComparer.Ordinal);
    private readonly Action<Exception> reportError;
    private readonly TimeProvider clock;
    private DateOnly rotationDay;
    private volatile string? lastLogPath;
    private string? lastWrittenPath;
    private (string Source, string Id, DateTimeOffset ObservedAt) lastWrittenBattle;

    public BattleLogArchive(string directory, Action<Exception> reportError, TimeProvider? clock = null, int retentionDays = DefaultRetentionDays)
    {
        this.reportError = reportError;
        this.clock = clock ?? TimeProvider.System;
        RetentionDays = retentionDays > 0 ? retentionDays : DefaultRetentionDays;
        LogDirectory = Path.Combine(directory, "BattleLogs");
        rotationDay = Today;
        var cutoff = OldestDay(rotationDay);
        PendingSave = Task.Run(() =>
        {
            ImportLegacyLogs(directory, cutoff);
            CleanSavedLogs(cutoff);
        });
    }

    public string LogDirectory { get; }
    public string? LastLogPath => lastLogPath;
    public Task PendingSave { get; private set; }
    public int RetentionDays { get; private set; }
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public void Reset() => transcripts.Clear();

    public void Maintain() => Maintain(Today);

    private void Maintain(DateOnly today)
    {
        if (today == rotationDay) return;
        rotationDay = today;
        QueueCleanup(today);
    }

    public void SetRetentionDays(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        if (RetentionDays == days) return;
        RetentionDays = days;
        rotationDay = Today;
        QueueCleanup(rotationDay);
    }

    public void Observe(string source, string id, string title, string status, int turn, int? revision,
        IReadOnlyList<string> lines, string participants)
    {
        if (!transcripts.TryGetValue(source, out var transcript) || transcript.Id != id)
        {
            transcript = new(id, clock.GetUtcNow());
            transcripts[source] = transcript;
        }
        if (transcript.Finished || revision < transcript.Revision || turn < transcript.Turn) return;

        var added = transcript.Merge(lines, turn);
        transcript.Title = title;
        transcript.Status = status;
        transcript.Turn = turn;
        transcript.Revision = revision ?? transcript.Revision;
        transcript.Participants = participants;
        if (transcript.Lines.Count == 0) return;

        transcript.Finished = status != "active";
        var append = transcript.FormatAppend(added);
        if (append.Length == 0) return;

        var today = Today;
        Maintain(today);
        transcript.DaysWritten.Add(today);
        var path = DailyLogPath(today);
        var header = transcript.FormatHeader();
        var battleKey = (source, id, transcript.ObservedAt);
        PendingSave = PendingSave.ContinueWith(_ => TryFileOperation(() =>
        {
            var exists = File.Exists(path);
            var newSection = path != lastWrittenPath || battleKey != lastWrittenBattle || !exists;
            File.AppendAllText(path, newSection ? (exists ? Environment.NewLine : "") + header + append : append);
            lastWrittenPath = lastLogPath = path;
            lastWrittenBattle = battleKey;
        }), TaskScheduler.Default);
    }

    public string? GetText(string id) => transcripts.Values.FirstOrDefault(log => log.Id == id && log.Lines.Count > 0)?.Format();

    public Task<int?> ClearSavedLogs()
    {
        var active = transcripts.Values.Where(log => !log.Finished).SelectMany(log => log.DaysWritten).ToHashSet();
        return QueueCleanup(Today, active);
    }

    private Task<int?> QueueCleanup(DateOnly today, HashSet<DateOnly>? preserve = null)
    {
        var cutoff = OldestDay(today);
        var cleanup = PendingSave.ContinueWith(_ => CleanSavedLogs(cutoff, preserve), TaskScheduler.Default);
        PendingSave = cleanup;
        return cleanup;
    }

    private int? CleanSavedLogs(DateOnly cutoff, HashSet<DateOnly>? preserve = null)
    {
        var removed = 0;
        var failed = false;
        string? newestPath = null;
        var newestDay = DateOnly.MinValue;
        if (!TryFileOperation(() =>
        {
            Directory.CreateDirectory(LogDirectory);
            foreach (var file in new DirectoryInfo(LogDirectory).EnumerateFiles("battle-*.txt"))
            {
                if (!TryGetLogDay(file.Name, out var day)) continue;
                if (day < cutoff || preserve is not null && !preserve.Contains(day))
                {
                    if (TryFileOperation(file.Delete)) { removed++; continue; }
                    failed = true;
                }
                if (newestPath is not null && day <= newestDay) continue;
                newestPath = file.FullName;
                newestDay = day;
            }
        })) return null;
        lastLogPath = newestPath;
        return failed ? null : removed;
    }

    private void ImportLegacyLogs(string directory, DateOnly cutoff)
    {
        TryFileOperation(() =>
        {
            Directory.CreateDirectory(LogDirectory);
            var previous = new FileInfo(Path.Combine(directory, "last-battle.txt"));
            var files = new DirectoryInfo(LogDirectory).EnumerateFiles("battle-*.txt").Where(file => IsLegacyLog(file.Name));
            if (previous.Exists) files = files.Append(previous);
            foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
                TryFileOperation(() => ImportLegacyLog(file, cutoff));
        });
    }

    private void ImportLegacyLog(FileInfo file, DateOnly cutoff)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(file.LastWriteTimeUtc, clock.LocalTimeZone));
        if (day >= cutoff)
        {
            var destination = DailyLogPath(day);
            var marker = $"[Imported {file.Name}]{Environment.NewLine}";
            var existing = File.Exists(destination) ? File.ReadAllText(destination) : "";
            if (!existing.Contains(marker, StringComparison.Ordinal))
            {
                var temporary = destination + ".import";
                try
                {
                    var separator = existing.Length > 0 ? Environment.NewLine : "";
                    File.WriteAllText(temporary, existing + separator + marker + File.ReadAllText(file.FullName));
                    File.Move(temporary, destination, overwrite: true);
                }
                finally { File.Delete(temporary); }
            }
        }
        file.Delete();
    }

    private DateOnly OldestDay(DateOnly today) => DateOnly.FromDayNumber(Math.Max(0, today.DayNumber - (RetentionDays - 1)));

    private string DailyLogPath(DateOnly day) => Path.Combine(LogDirectory,
        "battle-" + day.ToString(DateFormat, CultureInfo.InvariantCulture) + ".txt");

    private static bool TryGetLogDay(string name, out DateOnly day)
    {
        day = default;
        return name.Length == 21 && name.StartsWith("battle-", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.Ordinal)
            && DateOnly.TryParseExact(name.AsSpan(7, 10), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    private static bool IsLegacyLog(string name) => name.Length == 62 && name.StartsWith("battle-", StringComparison.Ordinal)
        && name[25] == '-' && name.EndsWith(".txt", StringComparison.Ordinal)
        && DateTime.TryParseExact(name.AsSpan(7, 18), "yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        && Guid.TryParseExact(name.AsSpan(26, 32), "N", out _);

    private bool TryFileOperation(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reportError(exception);
            return false;
        }
    }

    private static bool Include(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line[0] != '|') return true;
        var end = line.IndexOf('|', 1);
        return !OmittedEvents.Contains(end == -1 ? line[1..] : line[1..end]);
    }

    private sealed class Transcript(string id, DateTimeOffset observedAt)
    {
        public string Id { get; } = id;
        public DateTimeOffset ObservedAt { get; } = observedAt;
        public HashSet<DateOnly> DaysWritten { get; } = [];
        public string Title { get; set; } = "";
        public string Status { get; set; } = "active";
        public string Participants { get; set; } = "";
        public int Turn { get; set; }
        public int? Revision { get; set; }
        public bool Finished { get; set; }
        public List<string> Lines { get; } = [];
        private bool truncated;

        public string[] Merge(IReadOnlyList<string> lines, int turn)
        {
            var incoming = lines.Where(Include).ToArray();
            if (incoming.Length == 0) return [];

            var overlap = Math.Min(Lines.Count, incoming.Length);
            while (overlap > 0)
            {
                var index = 0;
                while (index < overlap && Lines[Lines.Count - overlap + index] == incoming[index]) index++;
                if (index == overlap) break;
                overlap--;
            }
            var added = overlap == 0 && (Lines.Count > 0 || turn > 1 && !incoming.Contains("|turn|1") && !incoming.Contains("|start"))
                ? [Gap, .. incoming] : incoming[overlap..];
            Lines.AddRange(added);
            if (Lines.Count > CopyLineLimit)
            {
                Lines.RemoveRange(0, Lines.Count - CopyLineLimit);
                truncated = true;
            }
            return added;
        }

        public string FormatHeader() => $"Magitek Monsters / {Title}{Environment.NewLine}"
            + $"Battle ID {Id}{Environment.NewLine}First seen {ObservedAt:yyyy-MM-dd HH:mm:ss} UTC{Environment.NewLine}{Environment.NewLine}";

        public string FormatAppend(IReadOnlyList<string> added)
        {
            var text = new StringBuilder();
            foreach (var line in added)
            {
                var visible = BattleResultPresentation.LogLine(line, Status);
                if (visible.Length == 0) continue;
                text.AppendLine(visible);
            }
            if (Finished) AppendSummary(text.AppendLine());
            return text.ToString();
        }

        public string Format()
        {
            var text = new StringBuilder(FormatHeader());
            if (truncated) text.AppendLine("[Earlier events exceeded the clipboard limit.]");
            foreach (var line in Lines)
            {
                var visible = BattleResultPresentation.LogLine(line, Status);
                if (visible.Length > 0) text.AppendLine(visible);
            }
            AppendSummary(text.AppendLine());
            return text.ToString();
        }

        private void AppendSummary(StringBuilder text)
        {
            text.AppendLine($"Turn {Turn} / {(Finished ? BattleResultPresentation.Outcome(Status) : "In progress")}");
            if (Participants.Length > 0) text.AppendLine(Participants);
        }
    }
}
