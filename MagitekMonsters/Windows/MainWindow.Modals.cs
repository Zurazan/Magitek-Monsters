using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private const int ConfirmationStyleCount = 7;

    private static void PrepareConfirmationModal()
    {
        var scale = CompanionScale;
        var work = ImGui.GetWindowViewport().WorkSize;
        var width = MathF.Floor(Math.Min(460 * scale, work.X * 0.9f));
        ImGui.SetNextWindowSize(new Vector2(width, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, work.Y * 0.9f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 14) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowTitleAlign, new Vector2(0, 0.5f));
    }

    private static bool BeginConfirmationModal(string name)
    {
        PrepareConfirmationModal();
        if (BeginCenteredModal(name, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.PushTextWrapPos(0);
            return true;
        }
        ImGui.PopStyleVar(ConfirmationStyleCount);
        return false;
    }

    private static void EndConfirmationModal()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
        ImGui.PopStyleVar(ConfirmationStyleCount);
    }

    private static bool DrawConfirmationActions(string? confirmLabel, Vector4 accent, bool disabled = false,
        string cancelLabel = "Cancel", Action? onCancel = null)
    {
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var scale = CompanionScale;
        var available = MathF.Floor(ImGui.GetContentRegionAvail().X);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var labelWidth = Math.Max(ImGui.CalcTextSize(confirmLabel ?? cancelLabel).X, ImGui.CalcTextSize(cancelLabel).X);
        var stacked = confirmLabel is null || (available - gap) / 2 < labelWidth + ImGui.GetStyle().FramePadding.X * 2;
        var size = new Vector2(stacked ? available : MathF.Floor((available - gap) / 2), Math.Max(34 * scale, ImGui.GetFrameHeight()));
        if (DrawAccentButton(cancelLabel, size, CompanionMuted))
        {
            onCancel?.Invoke();
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.IsWindowAppearing()) ImGui.SetItemDefaultFocus();
        if (confirmLabel is null) return false;
        if (!stacked) ImGui.SameLine(0, gap);
        ImGui.BeginDisabled(disabled);
        var confirmed = DrawAccentButton(confirmLabel, size, accent);
        ImGui.EndDisabled();
        return confirmed;
    }

    private static void CenterNextModal(string name)
    {
        if (!ImGui.IsPopupOpen(name)) return;
        var viewport = ImGui.GetWindowViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize / 2, ImGuiCond.Always, new Vector2(0.5f));
    }

    private static bool BeginCenteredModal(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        CenterNextModal(name);
        return ImGui.BeginPopupModal(name, flags | ImGuiWindowFlags.NoMove);
    }

    private static bool BeginCenteredModal(string name, ref bool open, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        CenterNextModal(name);
        return ImGui.BeginPopupModal(name, ref open, flags | ImGuiWindowFlags.NoMove);
    }
}
