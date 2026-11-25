using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum FrontierAchievementView { Next, All, Completed }

    private FrontierAchievementView frontierAchievementView;
    private int frontierAchievementPage;
    private string? expandedFrontierAchievementId;

    private void DrawFrontierProfile()
    {
        if (trainer is not { } current || !ImGui.CollapsingHeader("Frontier records and achievements")) return;
        var progress = current.Frontier;
        var columns = ImGui.GetContentRegionAvail().X >= 500 * CompanionScale ? 4 : 2;
        DrawRecordMetrics("FrontierRecords", columns, ("Lifetime wins", $"{progress.LifetimeWins:N0}", CompanionAccent),
            ("Best Accessible streak", $"{progress.BestAccessible:N0}", CompanionAccent),
            ("Best Standard streak", $"{progress.BestStandard:N0}", CompanionAccent),
            ("Lifetime BP", $"{progress.TotalEarned:N0}", WalletAccent(WalletCurrency.BattlePoints)));
        if (DrawAccentSmallButton("Open Battle Frontier", CompanionAccent)) OpenFrontier();
        ImGui.Separator();
        DrawFrontierAchievements();
        ImGui.Spacing();
    }

    private void DrawFrontierAchievements()
    {
        if (trainer is not { } current) return;
        var progress = current.Frontier;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        DrawFrontierAchievementFilter("Next up", FrontierAchievementView.Next);
        SameLineIfFits("All goals", right);
        DrawFrontierAchievementFilter("All goals", FrontierAchievementView.All);
        var completedLabel = $"Completed ({progress.Achievements.Count(entry => entry.Completed):N0})";
        SameLineIfFits(completedLabel, right);
        DrawFrontierAchievementFilter(completedLabel, FrontierAchievementView.Completed);

        var achievements = FilterFrontierAchievements(progress.Achievements, frontierAchievementView);
        const int pageSize = 5;
        if (achievements.Length > pageSize)
        {
            var pages = (achievements.Length + pageSize - 1) / pageSize;
            frontierAchievementPage = Math.Clamp(frontierAchievementPage, 0, pages - 1);
            var previousPage = frontierAchievementPage;
            var pageLabel = $"Page {frontierAchievementPage + 1:N0} / {pages:N0}";
            ImGui.BeginDisabled(frontierAchievementPage == 0);
            if (DrawAccentSmallButton("‹ Prev##FrontierGoals", CompanionAccent)) frontierAchievementPage--;
            ImGui.EndDisabled();
            SameLineIfFits(pageLabel, right);
            DrawGoalText(pageLabel, CompanionMuted);
            SameLineIfFits("Next ›", right);
            ImGui.BeginDisabled(frontierAchievementPage == pages - 1);
            if (DrawAccentSmallButton("Next ›##FrontierGoals", CompanionAccent)) frontierAchievementPage++;
            ImGui.EndDisabled();
            if (previousPage != frontierAchievementPage) expandedFrontierAchievementId = null;
        }
        else frontierAchievementPage = 0;
        if (achievements.Length == 0)
        {
            DrawGoalText(frontierAchievementView switch
            {
                FrontierAchievementView.Completed => "No Frontier achievements completed yet.",
                FrontierAchievementView.Next when progress.Achievements.Count > 0 => "All Frontier achievements completed.",
                _ => "Frontier achievements are unavailable."
            }, CompanionMuted);
            return;
        }

        foreach (var achievement in achievements.Skip(frontierAchievementPage * pageSize).Take(pageSize))
        {
            var earned = achievement.Completed;
            var expanded = expandedFrontierAchievementId == achievement.Id;
            var shownWins = Math.Clamp(achievement.Progress, 0, Math.Max(0, achievement.Target));
            var summary = earned ? $"Awarded {achievement.Bonus:N0} BP" : $"{shownWins:N0}/{achievement.Target:N0} wins / +{achievement.Bonus:N0} BP";
            ImGui.PushID(achievement.Id);
            if (DrawProgressRecordRow("FrontierAchievement", achievement.Name, summary, "frontier:" + achievement.Id,
                earned ? PokemonGreen : PokemonYellow, earned, expanded, earned ? 1f : shownWins / (float)Math.Max(1, achievement.Target)))
                expandedFrontierAchievementId = expanded ? null : achievement.Id;
            if (expandedFrontierAchievementId == achievement.Id)
            {
                DrawGoalText(earned && DateTimeOffset.TryParse(achievement.EarnedAt, out var date)
                    ? $"Reward added {date.LocalDateTime:g}." : earned ? "Reward added to your balance." : "The BP bonus is awarded automatically on completion.", CompanionMuted);
                foreach (var reward in progress.Rewards.Where(reward => reward.AchievementId == achievement.Id && reward.Kind == "pokemon"))
                    DrawGoalText(reward.Unlocked ? $"{reward.Name} is unlocked in Ultra Beast rewards." : $"Also unlocks {reward.Name} for purchase.", CompanionAccent);
            }
            ImGui.PopID();
        }
    }

    private void DrawFrontierAchievementFilter(string label, FrontierAchievementView view)
    {
        if (!DrawAccentButton(label, Vector2.Zero, frontierAchievementView == view ? CompanionAccent : CompanionMuted)
            || frontierAchievementView == view) return;
        frontierAchievementView = view;
        frontierAchievementPage = 0;
        expandedFrontierAchievementId = null;
    }

    private static FrontierAchievementDto[] FilterFrontierAchievements(IEnumerable<FrontierAchievementDto> achievements, FrontierAchievementView view)
    {
        if (view == FrontierAchievementView.All) return achievements.ToArray();
        if (view == FrontierAchievementView.Completed) return achievements.Where(entry => entry.Completed).ToArray();
        return achievements.Where(entry => !entry.Completed).GroupBy(entry => FrontierAchievementTrack(entry.Id), StringComparer.Ordinal)
            .Select(track => track.MinBy(entry => entry.Target)!)
            .OrderByDescending(entry => entry.Progress / (double)Math.Max(1, entry.Target)).ToArray();
    }

    private static string FrontierAchievementTrack(string id)
    {
        var separator = id.LastIndexOf('-');
        return separator > 0 ? id[..separator] : id;
    }
}
