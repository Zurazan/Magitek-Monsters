using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly BattleLogArchive battleLogs;
    private int retentionDays;
    private Task<int?>? clearingBattleLogs;
    private string? battleLogStatus;

    public ConfigWindow(Plugin plugin) : base("Magitek Monsters Settings###MagitekMonstersSettings")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 210),
            MaximumSize = new Vector2(800, 420)
        };
        configuration = plugin.Configuration;
        battleLogs = plugin.BattleLogs;
        retentionDays = battleLogs.RetentionDays;
    }

    public void Dispose() { }

    public override void PreDraw() => MainWindow.PushPokemonTheme();
    public override void PostDraw() => MainWindow.PopPokemonTheme();

    public override void Draw()
    {
        var notifications = configuration.NotificationsEnabled;
        if (ImGui.Checkbox("Dalamud notifications", ref notifications)) { configuration.NotificationsEnabled = notifications; configuration.Save(); }
        var typeHints = configuration.MoveTypeHintsEnabled;
        if (ImGui.Checkbox("Move type effectiveness badges", ref typeHints)) { configuration.MoveTypeHintsEnabled = typeHints; configuration.Save(); }
        var moveAnimations = configuration.BattleMoveAnimationsEnabled;
        if (ImGui.Checkbox("Battle move animations", ref moveAnimations)) { configuration.BattleMoveAnimationsEnabled = moveAnimations; configuration.Save(); }
        ImGui.Spacing();
        DrawBattleLogs();
        ImGui.Spacing();
        if (ImGui.Button("Forget login on this computer"))
        {
            configuration.ProtectedAccessToken = string.Empty;
            configuration.Save();
        }
    }

    private void DrawBattleLogs()
    {
        const string confirm = "Clear saved battle logs?";
        if (clearingBattleLogs is { IsCompleted: true } cleanup)
        {
            battleLogStatus = cleanup.IsCompletedSuccessfully ? cleanup.Result switch
            {
                null => "Some battle logs could not be removed.",
                0 => "No saved battle logs to clear.",
                1 => "Cleared 1 daily log.",
                var count => $"Cleared {count} daily logs."
            } : "Could not clear saved battle logs.";
            clearingBattleLogs = null;
        }

        ImGui.Separator();
        ImGui.Text("Battle logs");
        DrawLogRetention();
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.Button("Open logs folder")) OpenLogPath(battleLogs.LogDirectory, directory: true);
        MainWindow.SameLineIfFits("Open latest log", right);
        var latest = battleLogs.LastLogPath;
        ImGui.BeginDisabled(latest is null);
        if (ImGui.Button("Open latest log") && latest is not null) OpenLogPath(latest);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(latest is null ? "There are no saved battle logs yet." : "Open the newest daily battle log.");

        MainWindow.SameLineIfFits("Clear saved logs", right);
        ImGui.BeginDisabled(clearingBattleLogs is not null);
        if (ImGui.Button("Clear saved logs")) ImGui.OpenPopup(confirm);
        ImGui.EndDisabled();
        if (clearingBattleLogs is not null) ImGui.TextUnformatted("Clearing saved logs...");
        else if (battleLogStatus is not null) ImGui.TextWrapped(battleLogStatus);

        if (!MainWindow.BeginConfirmationModal(confirm)) return;
        ImGui.TextWrapped("Delete the daily log files on this computer? Files with an ongoing battle will be kept.");
        if (MainWindow.DrawConfirmationActions("Clear logs", new Vector4(1f, 0.35f, 0.37f, 1)))
        {
            clearingBattleLogs = battleLogs.ClearSavedLogs();
            ImGui.CloseCurrentPopup();
        }
        MainWindow.EndConfirmationModal();
    }

    private void OpenLogPath(string path, bool directory = false)
    {
        try
        {
            if (directory) Directory.CreateDirectory(path);
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            battleLogStatus = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            battleLogStatus = directory ? "Could not open the battle logs folder." : "Could not open the latest battle log.";
            Plugin.Log.Warning(exception, battleLogStatus);
        }
    }

    private void DrawLogRetention()
    {
        const string confirm = "Shorten log retention?";
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Keep logs for");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(7 * ImGui.GetFontSize());
        ImGui.InputInt("##BattleLogRetentionDays", ref retentionDays, 0, 0);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Enter at least 1 day. Today counts as the first day.");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("days");
        ImGui.SameLine();
        ImGui.BeginDisabled(retentionDays < 1 || retentionDays == battleLogs.RetentionDays);
        if (ImGui.Button("Apply##BattleLogRetention"))
        {
            if (retentionDays < battleLogs.RetentionDays) ImGui.OpenPopup(confirm);
            else ApplyLogRetention();
        }
        ImGui.EndDisabled();

        if (!MainWindow.BeginConfirmationModal(confirm)) return;
        var period = retentionDays == 1 ? "1 day" : $"{retentionDays} days";
        ImGui.TextWrapped($"Keep logs for {period}? Daily logs outside that period will be deleted.");
        if (MainWindow.DrawConfirmationActions("Apply retention", new Vector4(1f, 0.35f, 0.37f, 1),
            onCancel: () => retentionDays = battleLogs.RetentionDays))
        {
            ApplyLogRetention();
            ImGui.CloseCurrentPopup();
        }
        MainWindow.EndConfirmationModal();
    }

    private void ApplyLogRetention()
    {
        configuration.BattleLogRetentionDays = retentionDays;
        configuration.Save();
        battleLogs.SetRetentionDays(retentionDays);
    }
}
