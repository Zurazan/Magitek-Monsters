using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MagitekMonsters.Services;
using MagitekMonsters.Windows;

namespace MagitekMonsters;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    private static readonly string[] Commands = ["/magimon", "/mm", "/magitekmonsters"];

    public Configuration Configuration { get; }
    public ApiClient Api { get; }
    public AssetCatalog Assets { get; }
    public AnimatedPokemonSprites AnimatedSprites { get; }
    public ServerConnection Connection { get; }

    public readonly WindowSystem WindowSystem = new("MagitekMonsters");
    private ConfigWindow ConfigWindow { get; }
    private MainWindow MainWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var assemblyDirectory = PluginInterface.AssemblyLocation.Directory?.FullName
                                ?? Path.GetDirectoryName(PluginInterface.AssemblyLocation.FullName)
                                ?? string.Empty;
        var tokenStore = new ProtectedTokenStore(Configuration);
        Api = new ApiClient(Configuration, tokenStore);
        Connection = new ServerConnection(tokenStore.Read, Api.Update, new Uri(Configuration.OfficialServerUrl + "/api/events"),
            exception => Log.Warning(exception, "Magitek Monsters server event connection was interrupted."));
        Assets = new AssetCatalog(assemblyDirectory);
        AnimatedSprites = new AnimatedPokemonSprites(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "AnimatedSprites"), Assets);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        foreach (var command in Commands)
        {
            CommandManager.AddHandler(command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Magitek Monsters."
            });
        }

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("Magitek Monsters initialized. Server: {ServerUrl}", Configuration.OfficialServerUrl);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        AnimatedSprites.Dispose();
        Connection.Dispose();
        Api.Dispose();
        foreach (var command in Commands)
            CommandManager.RemoveHandler(command);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
