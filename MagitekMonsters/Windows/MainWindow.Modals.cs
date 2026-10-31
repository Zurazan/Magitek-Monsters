using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
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
