using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MagitekMonsters.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Magitek Monsters Settings###MagitekMonstersSettings")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(470, 160),
            MaximumSize = new Vector2(800, 420)
        };
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw() => MainWindow.PushPokemonTheme();
    public override void PostDraw() => MainWindow.PopPokemonTheme();

    public override void Draw()
    {
        ImGui.Text("Official Magitek Monsters service");
        ImGui.TextColored(new Vector4(0.65f, 0.76f, 1f, 1f), Configuration.OfficialServerUrl);
        ImGui.TextWrapped("Pokémon, inventory, currency, captures, and battle rewards are stored securely by the Magitek Monsters service.");
        ImGui.Spacing();
        var notifications = configuration.NotificationsEnabled;
        if (ImGui.Checkbox("Dalamud notifications", ref notifications)) { configuration.NotificationsEnabled = notifications; configuration.Save(); }
        ImGui.TextWrapped("Invitations, gifts, raids and connection alerts. No notification sounds.");
        ImGui.Spacing();
        if (ImGui.Button("Forget login on this computer"))
        {
            configuration.ProtectedAccessToken = string.Empty;
            configuration.Save();
        }
    }
}
