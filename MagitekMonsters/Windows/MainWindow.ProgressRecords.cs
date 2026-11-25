using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool DrawProgressRecordRow(string id, string name, string summary, string artwork, Vector4 statusColor,
        bool earned, bool expanded, float? progress)
    {
        var scale = CompanionScale;
        var pad = 8 * scale;
        var art = 30 * scale;
        var textX = art + pad * 2;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - textX - pad * 3);
        var titleHeight = ImGui.CalcTextSize(name, false, width).Y;
        var summaryY = pad + titleHeight + 2 * scale;
        var meterY = summaryY + ImGui.CalcTextSize(summary, false, width).Y;
        var height = Math.Max(art + pad * 2, meterY + pad + (progress.HasValue ? 7 * scale : 0));
        var clicked = DrawPveCardSurface(id, height, expanded, out var min, out var max, out var after);
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, max, true);
        DrawAchievementIconAt(artwork, min + new Vector2(pad, (height - art) / 2), new Vector2(art), earned);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(textX, pad),
            ImGui.GetColorU32(expanded ? CompanionAccent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), name, width);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(textX, summaryY),
            ImGui.GetColorU32(statusColor), summary, width);
        if (progress is float value)
        {
            ImGui.SetCursorScreenPos(min + new Vector2(textX, meterY + 3 * scale));
            DrawProgressBar(value, new Vector2(width, 4 * scale), "", earned ? PokemonGreen : ExpBarBlue);
        }
        var arrow = new Vector2(max.X - pad * 1.4f, min.Y + height / 2);
        var tip = 3 * scale;
        var color = ImGui.GetColorU32(CompanionMuted);
        if (expanded)
            draw.AddTriangleFilled(arrow + new Vector2(-tip, -tip / 2), arrow + new Vector2(tip, -tip / 2), arrow + new Vector2(0, tip), color);
        else
            draw.AddTriangleFilled(arrow + new Vector2(-tip / 2, -tip), arrow + new Vector2(-tip / 2, tip), arrow + new Vector2(tip, 0), color);
        draw.PopClipRect();
        ImGui.SetCursorScreenPos(after);
        return clicked;
    }

    private static void DrawRecordMetrics(string id, int columns, params (string Label, string Value, Vector4 Color)[] metrics)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 5) * CompanionScale);
        if (ImGui.BeginTable(id, columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
        {
            foreach (var metric in metrics)
            {
                ImGui.TableNextColumn();
                DrawGoalText(metric.Label, CompanionMuted);
                DrawGoalText(metric.Value, metric.Color);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawTrainerBattleRecords()
    {
        if (trainer is not { } current) return;
        var profile = current.PvpProfile;
        var season = string.IsNullOrWhiteSpace(profile.Season.Name) ? "Current season" : profile.Season.Name;
        var columns = ImGui.GetContentRegionAvail().X >= 640 * CompanionScale ? 3 : 1;
        if (!ImGui.BeginTable("BattleRecordCards", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) return;
        ImGui.TableNextColumn();
        DrawBattleRecordCard("Trainer battles", "Lifetime", current.PveProgress.TotalWins, current.PveProgress.TotalLosses, "Attempts", current.PveProgress.TotalAttempts);
        ImGui.TableNextColumn();
        DrawBattleRecordCard("Ranked", $"{season} / Rank {profile.Rank}", profile.RankedWins, profile.RankedLosses, "Draws", profile.RankedDraws);
        ImGui.TableNextColumn();
        DrawBattleRecordCard("Casual", season, profile.CasualWins, profile.CasualLosses, "Draws", profile.CasualDraws);
        ImGui.EndTable();
    }

    private static void DrawBattleRecordCard(string name, string context, int wins, int losses, string thirdLabel, int thirdValue)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 6) * CompanionScale);
        if (ImGui.BeginTable(name, 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            DrawGoalText(name, CompanionAccent);
            DrawGoalText(context, CompanionMuted);
            DrawRecordMetrics("BattleTotals", 3, ("Wins", $"{wins:N0}", PokemonGreen), ("Losses", $"{losses:N0}", PokemonRed),
                (thirdLabel, $"{thirdValue:N0}", ImGui.GetStyle().Colors[(int)ImGuiCol.Text]));
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }
}
