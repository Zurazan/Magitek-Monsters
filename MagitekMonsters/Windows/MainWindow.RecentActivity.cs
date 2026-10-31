using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private ConcurrentQueue<(string Owner, RecentActivityEntry Entry)>? pendingActivity;
    private string activityOwner = string.Empty;
    private long activitySession = -1;
    private HashSet<string>? activityBattles;
    private string activitySearch = string.Empty;
    private int activityCategory;
    private string activityClearOwner = string.Empty;
    private bool activityDirty;
    private DateTimeOffset nextActivitySave;

    private void ResetRecentActivity()
    {
        activityOwner = activitySearch = activityClearOwner = string.Empty;
        activitySession = -1;
        activityCategory = 0;
        activityBattles?.Clear();
    }

    private void ObserveTrainerActivity(TrainerSnapshotDto? previous, TrainerSnapshotDto? next)
    {
        if (next is null) return;
        if (activityOwner != next.User.Id || activitySession != sessionResponses.Generation)
        {
            ResetRecentActivity();
            activityOwner = next.User.Id;
            activitySession = sessionResponses.Generation;
            return;
        }
        QueueRecentActivity(next.User.Id, RecentActivity.TrainerChanges(previous, next, DateTimeOffset.UtcNow));
    }

    private void QueueRecentActivity(string owner, IEnumerable<RecentActivityEntry> entries)
    {
        if (string.IsNullOrEmpty(owner) || trainer?.User.Id != owner) return;
        if (pendingActivity is null) Interlocked.CompareExchange(ref pendingActivity, new(), null);
        foreach (var entry in entries) pendingActivity.Enqueue((owner, entry));
    }

    private void RecordRecentAction(string key, string kind, string summary, string detail)
    {
        if (trainer is not null) QueueRecentActivity(trainer.User.Id, [RecentActivity.Entry(key, kind, summary, detail, DateTimeOffset.UtcNow)]);
    }

    private void ObserveRecentBattle(BattleSnapshotDto? next, bool online = false, bool action = false)
    {
        if (trainer is null || next is null || string.IsNullOrEmpty(next.Id)) return;
        if (activityOwner != trainer.User.Id || activitySession != sessionResponses.Generation)
            ObserveTrainerActivity(null, trainer);
        activityBattles ??= [];
        if (next.Status == "active")
        {
            if (activityBattles.Count >= 64 && !activityBattles.Contains(next.Id)) activityBattles.Clear();
            activityBattles.Add(next.Id);
        }
        else if (action || activityBattles.Contains(next.Id))
        {
            var entries = RecentActivity.BattleEntries(next, DateTimeOffset.UtcNow, online);
            if (entries.Count > 0) activityBattles.Remove(next.Id);
            QueueRecentActivity(trainer.User.Id, entries);
        }
    }

    private void FlushRecentActivity()
    {
        plugin.Configuration.RecentActivities ??= new();
        var batches = new Dictionary<string, List<RecentActivityEntry>>();
        while (pendingActivity?.TryDequeue(out var pending) == true)
        {
            if (!batches.TryGetValue(pending.Owner, out var entries)) batches[pending.Owner] = entries = [];
            entries.Add(pending.Entry);
        }
        foreach (var (owner, entries) in batches)
            activityDirty |= RecentActivity.Record(RecentActivity.Preferences(plugin.Configuration.RecentActivities, owner), entries);
        if (!activityDirty || DateTimeOffset.UtcNow < nextActivitySave) return;
        try { plugin.Configuration.Save(); activityDirty = false; }
        catch (Exception exception)
        {
            nextActivitySave = DateTimeOffset.UtcNow.AddSeconds(30);
            Plugin.Log.Warning(exception, "Could not save recent activity.");
        }
    }

    private RecentActivityPreferences LocalRecentActivity() => RecentActivity.Preferences(plugin.Configuration.RecentActivities ??= new(), trainer!.User.Id);

    private void ClearRecentActivity(string owner)
    {
        if (trainer?.User.Id != owner) return;
        FlushRecentActivity();
        LocalRecentActivity().Entries.Clear();
        activityDirty = true;
        FlushRecentActivity();
    }

    private void DrawRecentActivity()
    {
        if (trainer is null) return;
        var history = LocalRecentActivity();
        ImGui.TextWrapped($"Last {RecentActivity.MaximumEntries} entries, saved on this PC for this trainer. Times show when this client recorded the result.");
        ImGui.TextDisabled($"{history.Entries.Count}/{RecentActivity.MaximumEntries} entries");
        string[] categories = ["All activity", "Pokémon received", "Evolutions", "Training", "Battles", "Rewards", "Milestones"];
        string[] kinds = ["", "pokemon", "evolution", "training", "battle", "reward", "milestone"];
        ImGui.SetNextItemWidth(Math.Min(190 * CompanionScale, ImGui.GetContentRegionAvail().X));
        ImGui.Combo("##ActivityCategory", ref activityCategory, categories, categories.Length);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ActivitySearch", "Search recent activity...", ref activitySearch, 100);
        ImGui.BeginDisabled(history.Entries.Count == 0);
        if (ImGui.SmallButton("Clear history")) { activityClearOwner = trainer.User.Id; ImGui.OpenPopup("Clear recent activity?"); }
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (ImGui.BeginChild("RecentActivityList", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y)), true))
        {
            var entries = history.Entries.Where(entry => (activityCategory == 0 || entry.Kind == kinds[activityCategory])
                && (string.IsNullOrWhiteSpace(activitySearch) || entry.Summary.Contains(activitySearch, StringComparison.OrdinalIgnoreCase)
                    || entry.Detail.Contains(activitySearch, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (history.Entries.Count == 0) ImGui.TextWrapped("Nothing recorded yet. New catches, evolutions, training, battles, rewards and milestones will appear here as you play.");
            else if (entries.Length == 0) ImGui.TextWrapped("No activity matches these filters.");
            else DrawRecentActivityEntries(entries);
        }
        ImGui.EndChild();
        DrawRecentActivityConfirmation();
    }

    private void DrawRecentActivityEntries(IReadOnlyList<RecentActivityEntry> entries)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * CompanionScale));
        foreach (var entry in entries)
        {
            var color = entry.Kind switch { "pokemon" => PokemonGreen, "evolution" => PokemonYellow, "training" => PokemonBlue,
                "reward" => PokemonYellow, "milestone" => PokemonOrange, _ => CompanionAccent };
            ImGui.TextDisabled(entry.RecordedAt.LocalDateTime.ToString("g"));
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(entry.Summary);
            ImGui.PopStyleColor();
            if (entry.Detail.Length > 0) ImGui.TextWrapped(entry.Detail);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        ImGui.PopTextWrapPos();
    }

    private void DrawRecentActivityConfirmation()
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(430 * CompanionScale, ImGui.GetMainViewport().WorkSize.X * 0.9f), 0), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Clear recent activity?", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (trainer?.User.Id != activityClearOwner) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.TextWrapped("Remove this trainer's local activity history? Your Pokémon, progress and rewards are kept.");
        if (DrawAccentSmallButton("Clear history", PokemonRed)) { ClearRecentActivity(activityClearOwner); ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
