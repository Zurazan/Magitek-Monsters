using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private BattleLogFollow? battleLogFollow;
    private IReadOnlyList<string>? battleLogSource;
    private (string Text, Vector4 Color)[] battleLogLines = [];
    private int battleLogSourceCount;
    private int pveOpponentPage;


    private void DrawCompanionBattleLog(BattleSnapshotDto current, float height)
        => DrawPermanentBattleLog(current.Id, current.Log, height);

    private void ResetBattleLog()
    {
        battleLogFollow = null; battleLogSource = null; battleLogLines = []; battleLogSourceCount = 0;
    }

    private void DrawPermanentBattleLog(string id, IReadOnlyList<string> lines, float height)
    {
        battleLogFollow ??= new();
        var newBattle = battleLogFollow.Begin(trainer?.User.Id + ":" + id);
        if (newBattle || !ReferenceEquals(battleLogSource, lines) || battleLogSourceCount != lines.Count)
        {
            battleLogSource = lines; battleLogSourceCount = lines.Count;
            battleLogLines = lines.Select(line => (Text: FormatBattleLine(line), Color: BattleLogColor(line)))
                .Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
        }
        var start = ImGui.GetCursorPosY();
        ImGui.TextColored(CompanionAccent, "Battle log");
        var following = battleLogFollow.Following;
        if (ImGui.Checkbox("Follow latest", ref following)) battleLogFollow.Following = following;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scroll up to read earlier messages. Enable Follow latest to jump back to the newest message.");
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6) * CompanionScale);
        ImGui.BeginChild("CompanionTurnHistory", new Vector2(0, Math.Max(1, height - (ImGui.GetCursorPosY() - start))), true);
        if (!newBattle) battleLogFollow.Observe(ImGui.GetScrollY(), ImGui.GetScrollMaxY(),
            ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem), ImGui.GetIO().MouseWheel, ImGui.IsMouseDown(ImGuiMouseButton.Left));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * CompanionScale));
        if (battleLogLines.Length == 0) ImGui.TextWrapped("Battle events will appear here.");
        foreach (var line in battleLogLines)
        {
            ImGui.TextColored(line.Color, line.Text);
            ImGui.Spacing();
        }
        ImGui.PopTextWrapPos();
        if (battleLogFollow.Following) ImGui.SetScrollHereY(1f);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawCompanionOnlineHeader(PvpProfileDto profile)
    {
        var season = profile.Season;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(12, 9) * CompanionScale);
        if (ImGui.BeginTable("CompanionSeasonBanner", 1, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.15f, 0.14f, 0.20f, 1)));
            ImGui.TextColored(new Vector4(0.80f, 0.75f, 0.95f, 1), season.Name);
            ImGui.TextColored(CompanionMuted, DateTimeOffset.TryParse(season.EndsAt, out var end) ? $"Season ends {end.LocalDateTime:g}" : "Open-ended preseason");
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
        if (profile.NextRankAt is int next)
        {
            var required = Math.Max(1, next - profile.CurrentRankStart);
            var earned = Math.Clamp(profile.RankPoints - profile.CurrentRankStart, 0, required);
            ImGui.Text($"Rank {profile.Rank} / {earned:N0} of {required:N0} points to the next rank");
            DrawProgressBar(earned / (float)required, new Vector2(-1, 7 * CompanionScale), "", ExpBarBlue);
        }
        else ImGui.TextColored(CompanionAccent, "Rank A / Highest rank reached");
        ImGui.TextWrapped($"Ranked {profile.RankedWins}W / {profile.RankedLosses}L / {profile.RankedDraws}D   |   Casual {profile.CasualWins}W / {profile.CasualLosses}L / {profile.CasualDraws}D");
        if (ImGui.CollapsingHeader("Season rules & rewards"))
        {
            ImGui.TextWrapped(season.Description);
            ImGui.TextWrapped($"Win +{season.WinPoints} / Draw +{season.DrawPoints} / Loss +{season.LossPoints} points");
            if (profile.RankTokens is { } tokens)
            {
                ImGui.Text($"Wallet: {tokens.Balance:N0} RT");
                ImGui.TextWrapped($"Paid this season: {tokens.SeasonPaid:N0} RT / Remaining at Rank {profile.Rank}: {tokens.SeasonRemaining:N0} RT");
                if (profile.NextRankAt is not null) ImGui.TextWrapped($"Next promotion: +{tokens.NextPromotionTokens:N0} RT / {season.PromotionRewardPercent}% paid through promotions");
                ImGui.TextWrapped("Your wallet carries across seasons. Promotion payments count toward your final reward. Rank cannot decrease this season.");
                foreach (var previous in tokens.History) ImGui.TextWrapped($"{previous.SeasonName} / Rank {previous.Rank} / {previous.TotalTokens:N0} RT total");
            }
        }
        ImGui.Spacing();
    }

    private void DrawCompanionPveDirectory()
    {
        ImGui.TextColored(CompanionMuted, "PVE LEAGUE");
        var title = "Your next challenge";
        var position = ImGui.GetCursorScreenPos();
        ImGui.Dummy(ImGui.CalcTextSize(title) * 1.2f);
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.2f, position, ImGui.GetColorU32(ImGuiCol.Text), title);
        ImGui.TextColored(CompanionMuted, "Train your team, earn badges and take on the League.");
        ImGui.Spacing();
        if (ImGui.BeginTabBar("PveLeagueSections", ImGuiTabBarFlags.FittingPolicyResizeDown))
        {
            if (ImGui.BeginTabItem("Trainer circuit")) { DrawPveTrainerBoard(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Regional Leagues")) { DrawRegionalLeagues(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Special trials")) { DrawSpecialTrials(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        DrawPveChallengeSetup();
    }
}
