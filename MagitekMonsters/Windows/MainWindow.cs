using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow : Window, IDisposable
{
    private const int PokemonThemeColorCount = 41;
    private const int PokemonThemeStyleCount = 11;
    private static readonly Vector4 PokemonRed = new(0.88f, 0.15f, 0.18f, 1f);
    private static readonly Vector4 PokemonBlue = new(0.18f, 0.48f, 0.91f, 1f);
    private static readonly Vector4 PokemonYellow = new(1f, 0.80f, 0.18f, 1f);
    private static readonly Vector4 PokemonGreen = new(0.25f, 0.76f, 0.40f, 1f);
    private static readonly Vector4 PokemonOrange = new(1f, 0.52f, 0.16f, 1f);
    private static readonly Vector4 HpBarRed = new(0.88f, 0.16f, 0.19f, 1f);
    private static readonly Vector4 ExpBarBlue = new(0.15f, 0.48f, 0.92f, 1f);
    private static readonly Vector4 PanelSurface = new(0.12f, 0.12f, 0.13f, 1f);

    private readonly Plugin plugin;
    private EvolutionEnvironment? evolutionEnvironment;
    private string evolutionEnvironmentLabel = string.Empty;
    private DateTimeOffset nextEnvironmentReadAt = DateTimeOffset.MinValue;
    private readonly SearchCooldown searchCooldown = new();
    private TrainerSnapshotDto? trainerState;
    private TrainerSnapshotDto? trainer
    {
        get => trainerState;
        set
        {
            if (trainerState?.User.Id != value?.User.Id) { searchCooldown.Observe(0); giftPage = 0; headerFeedback.Clear(); ResetTrainerProfiles(); ResetEvTrainingPlanner(); ResetTeamAnalysis(); ResetCollectionWishlist(); ResetRecentActivity(); ResetStorageSearches(); ResetBattleLog(); ResetRegionalLeagues(); }
            ObserveTrainerActivity(trainerState, value);
            trainerState = value;
            if (value is not null) trainerProfiles.Invalidate(value.User.TrainerId);
            if (value is not null && plugin.Api?.SnapshotChangeId(value) is { } change) refreshState.CoverTrainerChange(change);
            nextGiftRefreshAt = DateTimeOffset.UtcNow.AddSeconds(60);
            if (value?.SearchCooldown is { } cooldown) searchCooldown.Observe(cooldown.RemainingSeconds);
        }
    }
    private List<StarterDto> starters = [];
    private List<ShopItemDto> shop = [];
    private List<ShopItemDto> itemCatalog = [];
    private List<ShopItemDto> trainingItems = [];
    private List<PrizePokemonDto> prizePokemon = [];
    private List<OpponentDto> opponents = [];
    private List<PokedexEntryDto> pokedexCatalog = [];
    private HabitatDto? habitat;
    private BattleSnapshotDto? battle;
    private PvpStatusDto? pvpStatus;
    private AuthorizationFlowDto? authorizationFlow;
    private string status = "Ready.";
    private bool statusIsError;
    private bool initialized;
    private bool busy;
    private int starterIndex;
    private int starterGeneration = 1;
    private bool confirmStarter;
    private int targetIndex;
    private int shopCategoryIndex;
    private int pveTierIndex;
    private int pvpModeIndex = 1;
    private int mainSection;
    private string pokedexSearch = string.Empty;
    private int pokedexPage;
    private string? selectedPokedexSpecies;
    private string storageSearch = string.Empty;
    private readonly HashSet<string> pvpTeamIds = [];
    private bool megaEvolveSelected;
    private volatile int onlineTrainers = -1;
    private uint loadedTerritory = uint.MaxValue;
    private string? catalogError;
    private DateTimeOffset nextCatalogRetryAt = DateTimeOffset.MinValue;
    private volatile bool serverPvpRefreshRequested;
    private DateTimeOffset nextBattlePollAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextPvpPollAt = DateTimeOffset.MinValue;
    private DateTimeOffset trainerIdCopiedUntil = DateTimeOffset.MinValue;
    private readonly CancellationTokenSource disposeCancellation = new();

    public MainWindow(Plugin plugin)
        : base("Magitek Monsters###MagitekMonstersMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(780, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Size = new Vector2(980, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        plugin.Connection.EventReceived += OnServerEvent;
        plugin.Connection.StateReceived += OnLiveState;
        Plugin.Framework.Update += TrackEvolutionTravel;
    }

    internal static void PushPokemonTheme()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.91f, 0.91f, 0.93f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.60f, 0.60f, 0.65f, 1f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.09f, 0.09f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.09f, 0.09f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelSurface);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.20f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.105f, 0.105f, 0.115f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.13f, 0.13f, 0.14f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.115f, 0.115f, 0.125f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.14f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.13f, 0.16f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.28f, 0.52f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.11f, 0.22f, 0.41f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.18f, 0.20f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.19f, 0.23f, 0.30f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.16f, 0.16f, 0.17f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.14f, 0.25f, 0.47f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.17f, 0.20f, 0.26f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, new Vector4(0.16f, 0.15f, 0.16f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, new Vector4(0.17f, 0.19f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, PokemonBlue);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.24f, 0.24f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.30f, 0.30f, 0.31f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.34f, 0.34f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, PokemonBlue with { W = 0.70f });
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.29f, 0.29f, 0.31f, 0.47f));
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, PokemonBlue with { W = 0.65f });
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, PokemonBlue with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, PokemonBlue);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.19f, 0.19f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, new Vector4(0.31f, 0.31f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(0.23f, 0.23f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, PokemonBlue with { W = 0.50f });
    }

    internal static void PopPokemonTheme()
    {
        ImGui.PopStyleColor(PokemonThemeColorCount);
        ImGui.PopStyleVar(PokemonThemeStyleCount);
    }

    private static void DrawSectionHeader(string title, string subtitle)
    {
        ImGui.TextUnformatted(title);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
            ImGui.TextWrapped(subtitle);
            ImGui.PopStyleColor();
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void DrawProgressBar(float value, Vector2 size, string label, Vector4 fillColor)
    {
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, fillColor);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogramHovered, fillColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.035f, 0.045f, 0.07f, 1f));
        ImGui.ProgressBar(Math.Clamp(value, 0f, 1f), size, label);
        ImGui.PopStyleColor(3);
    }

    private static void PushAccentButtonStyle(Vector4 accent)
    {
        var baseColor = Vector4.Lerp(new Vector4(0.105f, 0.105f, 0.12f, 1f), accent, 0.12f);
        var hoverColor = Vector4.Lerp(new Vector4(0.12f, 0.12f, 0.135f, 1f), accent, 0.24f);
        var activeColor = Vector4.Lerp(new Vector4(0.10f, 0.10f, 0.12f, 1f), accent, 0.24f);
        ImGui.PushStyleColor(ImGuiCol.Button, baseColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeColor);
        ImGui.PushStyleColor(ImGuiCol.Border, accent with { W = 0.38f });
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Lerp(new Vector4(0.93f, 0.93f, 0.95f, 1), accent, 0.70f) with { W = 1 });
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
    }

    private static bool DrawAccentButton(string label, Vector2 size, Vector4 accent)
    {
        PushAccentButtonStyle(accent);
        if (size.X == 0)
        {
            var visibleLabel = label.Split("##", StringSplitOptions.None)[0];
            size.X = Math.Max(1, Math.Min(ImGui.CalcTextSize(visibleLabel).X + ImGui.GetStyle().FramePadding.X * 2, ImGui.GetContentRegionAvail().X));
        }
        var pressed = ImGui.Button(label, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
        return pressed;
    }

    private static bool DrawAccentSmallButton(string label, Vector4 accent)
    {
        PushAccentButtonStyle(accent);
        var pressed = ImGui.SmallButton(label);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
        return pressed;
    }

    private static void DrawMetricCard(string id, string label, string value, Vector4 accent)
    {
        ImGui.BeginChild(id, new Vector2(0, 58 * CompanionScale), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawClippedText(label.ToUpperInvariant(), ImGui.GetContentRegionAvail().X, CompanionMuted);
        DrawClippedText(value, ImGui.GetContentRegionAvail().X, accent);
        ImGui.EndChild();
    }

    private static void DrawPager(string id, ref int page, int totalItems, int pageSize)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Clamp(page, 0, pageCount - 1);

        if (page > 0)
        {
            if (ImGui.SmallButton($"‹ Previous##{id}"))
                page--;
        }
        else
        {
            ImGui.TextDisabled("‹ Previous");
        }

        ImGui.SameLine();
        ImGui.Text($"Page {page + 1:N0} / {pageCount:N0}");
        ImGui.SameLine();
        if (page + 1 < pageCount)
        {
            if (ImGui.SmallButton($"Next ›##{id}"))
                page++;
        }
        else
        {
            ImGui.TextDisabled("Next ›");
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{totalItems:N0} result{(totalItems == 1 ? string.Empty : "s")}");
    }

    public void Dispose()
    {
        sessionResponses.Invalidate();
        ResetTrainerProfiles();
        ResetEvTrainingPlanner();
        ResetTeamAnalysis();
        ResetCollectionWishlist();
        FlushRecentActivity();
        ResetRecentActivity();
        ResetStorageSearches();
        ResetBattleLog();
        foregroundGeneration++;
        plugin.Connection.EventReceived -= OnServerEvent;
        plugin.Connection.StateReceived -= OnLiveState;
        Plugin.Framework.Update -= TrackEvolutionTravel;
        CancelBackgroundRefresh();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    public override void PreDraw() => PushPokemonTheme();

    public override void PostDraw() => PopPokemonTheme();

    internal void UpdateBackground()
    {
        while (backgroundResults.TryDequeue(out var result)) result();
        ApplyLiveUpdates();
        if (battle?.Status == "active") ObserveRecentBattle(battle);
        if (pvpStatus?.Battle?.Status == "active") ObserveRecentBattle(pvpStatus.Battle, true);
        FlushRecentActivity();
        if (plugin.Api.Update.Required)
        {
            CancelBackgroundRefresh();
            if (plugin.Connection.State != ServerConnectionState.Disconnected) plugin.Connection.Stop();
            UpdateNotifications();
            return;
        }
        if (!initialized && !IsOpen) { UpdateNotifications(); return; }
        if (IsOpen && DateTimeOffset.UtcNow >= nextEnvironmentReadAt)
        {
            nextEnvironmentReadAt = DateTimeOffset.UtcNow.AddSeconds(1);
            (evolutionEnvironment, evolutionEnvironmentLabel) = EvolutionEnvironment.Read(CurrentEvolutionTheme);
        }
        if (trainer is not null)
            foreach (var option in trainer.EvolutionOptions.Where(option => option.ReadyWithoutEnvironment.HasValue))
                option.Ready = option.ReadyWithoutEnvironment == true
                               && (option.EnvironmentAlternatives.Count > 0
                                   ? option.EnvironmentAlternatives.Any(route => evolutionEnvironment?.Matches(route.RequiredTime, route.RequiredWeather, route.EnvironmentChecks) == true)
                                   : evolutionEnvironment?.Matches(option.RequiredTime, option.RequiredWeather, option.EnvironmentChecks) == true);
        ObserveShopVisibility();
        if (!initialized)
        {
            initialized = true;
            Run(Initialize, "Connecting to Magitek Monsters...");
        }
        else if (catalogError is not null && !busy && DateTimeOffset.UtcNow >= nextCatalogRetryAt && battle?.Status != "active" && pvpStatus?.Battle?.Status != "active")
        {
            nextCatalogRetryAt = DateTimeOffset.UtcNow.AddSeconds(5);
            Run(() => LoadCatalogs(), "Loading Magitek Monsters game data...");
        }
        else if (!busy && !backgroundRefreshing && trainer is not null && DateTimeOffset.UtcNow >= backgroundRetryAfter)
        {
            if (connectionSyncGeneration != 0 && plugin.Connection.State == ServerConnectionState.Syncing
                && plugin.Connection.Generation == connectionSyncGeneration)
                BackgroundConnectionSync();
            else if ((raidRefreshRequested || DateTimeOffset.UtcNow >= nextRaidPollAt) && (IsOpen && mainSection == 12 || trainer.ActiveRaidId is not null || raidRoom?.Status is "lobby" or "active"))
            {
                raidRefreshRequested = false; BackgroundRaids();
            }
            else if (battle is { Status: "active" } current && (refreshState.TakeBattle(current.Id)
                || (plugin.Connection.CanInvoke || current.Request is null || current.Request.Wait == true) && DateTimeOffset.UtcNow >= nextBattlePollAt))
                BackgroundBattle();
            else if (serverPvpRefreshRequested || pvpStatus is not null
                && (pvpStatus.State == "queued" || pvpStatus.Challenges.Count > 0
                    || pvpStatus.Battle is { Status: "active" } match && (plugin.Connection.CanInvoke || match.Request is null || match.Request.Wait == true))
                && DateTimeOffset.UtcNow >= nextPvpPollAt)
            {
                serverPvpRefreshRequested = false;
                BackgroundPvp();
            }
            else if (battle?.Status != "active" && pvpStatus?.Battle?.Status != "active" && refreshState.TakeTrainer())
            {
                BackgroundTrainer();
            }
            else if (IsOpen && mainSection == 5 && shopRefreshRequested)
                Run(RefreshShop, "Refreshing shop...");
            else if (DateTimeOffset.UtcNow >= nextGiftRefreshAt && battle?.Status != "active" && pvpStatus?.Battle?.Status != "active" && raidRoom?.Status != "active")
                BackgroundGifts();
        }

        UpdateNotifications();
    }

    public override void Draw()
    {
        if (plugin.Api.Update.Required)
        {
            ImGui.Spacing();
            ImGui.TextColored(PokemonOrange, "CLIENT UPDATE REQUIRED");
            ImGui.TextWrapped(plugin.Api.Update.Message);
            ImGui.Spacing();
            ImGui.TextDisabled($"Installed version: {ClientBuildVersion.Current}");
            if (DrawAccentButton("Open Plugin Installer", new Vector2(220, 36) * CompanionScale, PokemonBlue))
                Plugin.CommandManager.ProcessCommand("/xlplugins");
            return;
        }
        var clipboard = Interlocked.Exchange(ref pendingClipboard, null);
        if (clipboard is not null) ImGui.SetClipboardText(clipboard);
        RestoreTrainerBattle();
        ImGui.BeginChild("MagitekMonstersContent", Vector2.Zero, false, trainer is not null
            ? ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None);
        if (trainer is null)
            DrawLogin();
        else if (!trainer.StarterChosen)
            DrawStarterSelection();
        else
            DrawGameShell();
        ImGui.EndChild();
    }

    private void DrawGameShell()
    {
        if (trainer is null) return;
        if (DrawCompanionHeader()) { Disconnect(); return; }
        DrawCompanionError();
        DrawGiftNotice();
        var scale = CompanionScale;
        var available = ImGui.GetContentRegionAvail();
        var bodyHeight = Math.Max(1, available.Y);
        var navigationWidth = Math.Min(160 * scale, available.X * 0.25f);
        var gap = 8 * scale;
        var partyIds = trainer.Party.Select(entry => entry.Id).ToHashSet();
        var partyAlerts = trainer.PendingMoves.Count(entry => partyIds.Contains(entry.PokemonId))
                          + trainer.EvolutionOptions.Count(entry => partyIds.Contains(entry.PokemonId) && entry.Ready);
        var storageAlerts = trainer.PendingMoves.Count(entry => !partyIds.Contains(entry.PokemonId))
                            + trainer.EvolutionOptions.Count(entry => !partyIds.Contains(entry.PokemonId) && entry.Ready);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 12) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.105f, 0.105f, 0.114f, 1));
        if (ImGui.BeginChild("CompanionNavigation", new Vector2(navigationWidth, bodyHeight), false))
        {
            ImGui.TextColored(CompanionMuted, "JOURNEY");
            DrawNavigationItem("Adventure", 0, 32 * scale);
            DrawNavigationItem("Trainer Card", 14, 32 * scale);
            DrawNavigationItem($"Party {trainer.Party.Count}/6{NavigationAlert(partyAlerts)}", 1, 32 * scale);
            DrawNavigationItem($"PC Storage{NavigationAlert(storageAlerts)}", 2, 32 * scale);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(CompanionMuted, "BATTLE");
            DrawNavigationItem("PvE League", 3, 32 * scale);
            DrawNavigationItem("Battle Frontier", 8, 32 * scale);
            DrawNavigationItem("Cooperative Dens", 12, 32 * scale);
            DrawNavigationItem("Sanctuaries", 9, 32 * scale);
            DrawNavigationItem("Online Battles", 4, 32 * scale);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(CompanionMuted, "COLLECTION");
            DrawNavigationItem("Poké Mart", 5, 32 * scale);
            DrawNavigationItem("Currencies", 15, 32 * scale);
            DrawNavigationItem("Bag", 11, 32 * scale);
            DrawNavigationItem("Pokédex", 6, 32 * scale);
            DrawNavigationItem("Collection goals", 7, 32 * scale);
            DrawNavigationItem("Events", 10, 32 * scale);
            DrawNavigationItem($"Mystery Gifts{(trainer.Gifts.Unread > 0 ? $" [{trainer.Gifts.Unread}]" : "")}", 13, 32 * scale);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(); ImGui.PopStyleVar();
        ImGui.SameLine(0, gap);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        var consoleVisible = mainSection == 4 && pvpStatus?.Battle is not null
            || mainSection == 0 && battle?.Kind == "wild"
            || mainSection == 3 && battle is not null && battle.Kind != "wild"
            || mainSection == 8 && battle?.FrontierId is not null
            || mainSection == 9 && battle?.Kind == "trainer" && battle.OpponentId.StartsWith("legendary-", StringComparison.Ordinal)
            || mainSection == 10 && battle is { Kind: "trainer", OpponentId: "legendary-zarude" };
        if (ImGui.BeginChild("CompanionPage", new Vector2(Math.Max(1, available.X - navigationWidth - gap), bodyHeight), false,
                consoleVisible || mainSection == 4 || mainSection == 12 && raidRoom?.Status == "active" ? FixedBattlePanel : ImGuiWindowFlags.None))
        {
            switch (mainSection)
            {
                case 0: DrawAdventure(); break;
                case 1: DrawParty(); break;
                case 2: DrawStorage(); break;
                case 3: DrawPve(); break;
                case 4: DrawPvp(); break;
                case 5: DrawShop(); break;
                case 6: DrawCompanionPokedex(); break;
                case 7: DrawCollectionGoals(); break;
                case 8: DrawFrontier(); break;
                case 9: DrawLegendary(); break;
                case 10: DrawRecurringEvents(); break;
                case 11: DrawBag(); break;
                case 12: DrawRaids(); break;
                case 13: DrawMysteryGifts(); break;
                case 14: DrawTrainerCard(); break;
                case 15: DrawCurrencies(); break;
                default: mainSection = 0; DrawAdventure(); break;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        DrawUidChoicePrompt();
    }

    private void DrawNavigationItem(string label, int section, float height)
    {
        var selected = mainSection == section;
        ImGui.PushStyleColor(ImGuiCol.Button, selected
            ? CompanionAccent with { W = 0.12f }
            : Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected
            ? CompanionAccent with { W = 0.18f }
            : new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, CompanionAccent with { W = 0.22f });
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.06f, 0.5f));
        var itemWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        if (ImGui.Button($"{label}##MainSection{section}", new Vector2(itemWidth, height)))
            mainSection = section;
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        if (!selected)
            return;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRectFilled(min, new Vector2(min.X + 3 * CompanionScale, max.Y), ImGui.GetColorU32(CompanionAccent));
    }

    private static string NavigationAlert(int count) => count > 0 ? $"  [{count}]" : string.Empty;

    private void DrawCopyableTrainerId(string trainerId, float? availableWidth = null)
    {
        if (string.IsNullOrWhiteSpace(trainerId))
        {
            ImGui.TextDisabled("TRAINER ID PENDING");
            return;
        }

        var copied = DateTimeOffset.UtcNow < trainerIdCopiedUntil;
        var size = new Vector2(availableWidth ?? ImGui.CalcTextSize(trainerId).X + 10, ImGui.GetTextLineHeight());
        ImGui.PushStyleColor(ImGuiCol.Text, copied ? PokemonGreen : CompanionAccent);
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, PokemonBlue with { W = 0.22f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, PokemonBlue with { W = 0.34f });
        var pressed = ImGui.Selectable($"{trainerId}##CopyTrainerId", false, ImGuiSelectableFlags.None, size);
        ImGui.PopStyleColor(4);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(copied ? "Trainer ID copied." : "Click to copy Trainer ID");
        }

        if (!pressed)
            return;

        ImGui.SetClipboardText(trainerId);
        trainerIdCopiedUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        SetStatus($"Trainer ID {trainerId} copied to clipboard.", quiet: true);
    }


    private void DrawLogin()
    {
        DrawSectionHeader("Trainer login", "Link your verified FFXIV character with XIVAuth");
        ImGui.TextWrapped("Catch, train and battle Pokémon inside this companion window while you play FFXIV.");
        ImGui.TextWrapped("Your game data lives on the official service. This computer stores only your protected session token.");
        ImGui.Spacing();
        ImGui.BeginDisabled(busy || authorizationFlow is not null);
        if (DrawAccentButton("Sign in with XIVAuth", new Vector2(190, 36) * CompanionScale, CompanionAccent))
            Run(StartXivAuth, "Starting XIVAuth login...");
        ImGui.EndDisabled();
        if (authorizationFlow is not null)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Choose a verified FFXIV character on XIVAuth. Waiting for the server callback.");
            if (DrawAccentButton("Open authorization page again", Vector2.Zero, CompanionAccent))
                Util.OpenLink(authorizationFlow.AuthorizationUrl);
        }
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, statusIsError ? PokemonRed : CompanionMuted);
        ImGui.TextWrapped(status);
        ImGui.PopStyleColor();
    }

    private void DrawStarterSelection()
    {
        DrawStarterOnboarding();
    }

    private void DrawAdventure()
    {
        if (LeagueBusy || FrontierBusy || battle is { Kind: "trainer", Status: "active" })
        {
            DrawSectionHeader("ADVENTURE PAUSED", "Your battle or checkpoint is saved");
            ImGui.TextWrapped("Resume or end the saved run in its battle section. Disconnecting does not reset progress.");
            if (DrawAccentButton(FrontierBusy ? "Resume Battle Frontier" : "Resume PvE League", new Vector2(210, 34), PokemonBlue)) mainSection = FrontierBusy ? 8 : 3;
            return;
        }
        if (battle?.Kind == "wild")
        {
            DrawWildBattle(battle);
            return;
        }
        if (trainer?.ActiveEncounter is { Status: "active" } activeEncounter)
        {
            DrawEncounter(activeEncounter);
            return;
        }
        var territoryId = (uint)Plugin.ClientState.TerritoryType;
        var territoryContext = CurrentTerritoryContext(territoryId);
        DrawAdventureFieldGuide(territoryId, territoryContext);
    }

    private void DrawEncounter(EncounterDto encounter)
    {
        var pokemon = encounter.Pokemon;
        var name = pokemon.Shiny ? $"★ {pokemon.DisplaySpecies}" : pokemon.DisplaySpecies;
        ImGui.TextColored(pokemon.Shiny ? new Vector4(1f, 0.75f, 0.15f, 1f) : Vector4.One, $"A wild {name} appeared!");
        DrawPokemonImage(pokemon.Appearance, pokemon.Shiny, new Vector2(256, 256));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.Text($"Level {pokemon.Level}");
        ImGui.Text($"Nature effects: {(string.IsNullOrWhiteSpace(pokemon.MintNature) ? pokemon.Nature : pokemon.MintNature)}");
        ImGui.Text($"Ability: {pokemon.Ability}");
        ImGui.Text($"Capture attempts: {encounter.Attempts}");
        ImGui.Spacing();
        ImGui.TextWrapped("Fight, weaken and catch it, or try to Run. Failed throws and escape attempts spend your turn.");
        if (DrawAccentButton("Resume wild battle", new Vector2(180, 32), PokemonBlue))
            Run(() => ResumeWildBattle(encounter.Id), "Resuming the wild battle...");
        ImGui.EndGroup();
    }

    private void DrawWildBattle(BattleSnapshotDto current) => DrawBattleConsole(current);

    private void DrawParty()
    {
        DrawCompanionParty();
    }

    private void DrawStorage()
    {
        DrawStorageBrowser();
    }

    private void DrawPve()
    {
        if (FrontierBusy && mainSection != 8)
        {
            ImGui.TextWrapped("Your saved run is in Battle Frontier. Continue or end it before starting another challenge.");
            if (DrawAccentButton("Open Battle Frontier", new Vector2(200, 34), PokemonBlue)) mainSection = 8;
            return;
        }
        if (battle is { Status: not "active", Kind: "trainer" }) { DrawBattleConsole(battle); return; }
        if (DrawLeagueCheckpoint()) return;
        if (battle?.Kind == "wild")
        {
            ImGui.TextWrapped("Your wild battle is in Adventure. Finish it before challenging the League.");
            if (DrawAccentButton("Open wild battle", new Vector2(180, 32), PokemonBlue))
                mainSection = 0;
            return;
        }
        if (battle is null)
        {
            DrawCompanionPveDirectory();
            return;
        }

        DrawBattleConsole(battle);
    }

    private static bool CanUseBattleItem(ShopItemDto item, BattleSidePokemonDto pokemon)
    {
        var fainted = pokemon.Condition.EndsWith(" fnt", StringComparison.Ordinal);
        var reviving = item.BattleEffect is "revive" or "max-revive";
        if (reviving)
            return fainted;
        if (fainted)
            return false;
        if (item.BattleEffect is "pp-restore" or "pp-restore-all") return pokemon.PpMoves.Any(move => move.Pp < move.MaxPp);

        var conditionParts = pokemon.Condition.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hpParts = conditionParts.FirstOrDefault()?.Split('/') ?? [];
        var missingHp = hpParts.Length == 2
                        && int.TryParse(hpParts[0], out var currentHp)
                        && int.TryParse(hpParts[1], out var maximumHp)
                        && currentHp < maximumHp;
        var hasStatus = conditionParts.Length > 1;
        return missingHp || item.BattleEffect == "full-heal" && hasStatus;
    }

    private void DrawPvp()
    {
        if (trainer is null)
            return;
        if (pvpStatus is null)
        {
            ImGui.TextDisabled("Loading PvP status...");
            if (!busy)
                Run(RefreshPvp, "Loading PvP status...");
            return;
        }

        trainer.PvpProfile = pvpStatus.Profile;
        var profile = pvpStatus.Profile;
        var season = profile.Season;
        if (pvpStatus.Battle is not null)
        {
            DrawPvpBattle(pvpStatus.Battle);
            return;
        }
        DrawOnlineLobbyHeader(profile);
        if (pvpStatus.State == "queued")
        {
            var queuedFormat = season.Formats.FirstOrDefault(format => format.Id == pvpStatus.QueuedFormatId);
            ImGui.TextColored(new Vector4(1f, 0.82f, 0.2f, 1f), $"SEARCHING - {pvpStatus.QueuedMode?.ToUpperInvariant()}  |  {queuedFormat?.Name}");
            ImGui.TextWrapped("Your selected team is locked on the server while matchmaking searches for another connected Trainer.");
            ImGui.TextDisabled("You can keep playing FFXIV while the queue is active. The match will appear here automatically.");
            if (DrawAccentButton("Leave queue", new Vector2(140, 34), PokemonOrange))
                Run(LeavePvpQueue, "Leaving the PvP queue...");
            return;
        }

        DrawOnlineLobby(season);
    }

    private void DrawPvpBattle(BattleSnapshotDto current) => DrawBattleConsole(current, true);

    private void DrawShop()
    {
        if (trainer is null)
            return;
        DrawSectionHeader("POKÉ MART", "Supplies, training items, and special Pokémon");
        DrawCurrencyGuideShortcut();
        ImGui.Spacing();

        if (!ImGui.BeginTabBar("MagitekMonstersShopTabs"))
            return;
        if (ImGui.BeginTabItem("Items", requestedShopTab == "Items" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            DrawItemShop();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Rank Token Exchange", requestedShopTab == "Rank Token Exchange" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            ImGui.TextWrapped("Spend ranked-season tokens on guaranteed captures. Master Balls do not skip Sanctuary boss fights or unlock requirements.");
            ImGui.Spacing();
            DrawItemShop(true);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Prize Exchange"))
        {
            DrawPrizeExchange();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Collection Exchange", openCollectionExchange ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            openCollectionExchange = false;
            DrawCollectionExchange();
            ImGui.EndTabItem();
        }
        requestedShopTab = null;
        ImGui.EndTabBar();
    }

    private void DrawItemShop(bool rankTokensOnly = false)
    {
        DrawCompanionItemShop(rankTokensOnly);
    }

    private void DrawPrizeExchange()
    {
        if (trainer is null)
            return;
        ImGui.TextWrapped("These Pokémon are obtained from the Prize Exchange instead of wild encounters. Purchases go to your party when space is available, otherwise directly to PC storage.");
        ImGui.Spacing();
        foreach (var prize in prizePokemon)
        {
            if (ImGui.BeginTable($"PrizePokemonLayout{prize.Id}", 3, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Art", ImGuiTableColumnFlags.WidthFixed, 84);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 105);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawPokemonIcon(prize.Species, false, new Vector2(72, 72));
                ImGui.TableNextColumn();
                ImGui.TextWrapped($"{prize.Species} / Lv. {prize.Level} / ¥ {prize.Price:N0}");
                ImGui.TextWrapped(prize.Description);
                ImGui.TextWrapped(prize.Origin);
                ImGui.TableNextColumn();
                if (trainer.Currency < prize.Price)
                {
                    ImGui.BeginDisabled();
                    DrawAccentButton($"Redeem##{prize.Id}", new Vector2(94, 32), PokemonRed);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip($"You need ¥ {(prize.Price - trainer.Currency):N0} more.");
                }
                else if (DrawAccentButton($"Redeem##{prize.Id}", new Vector2(94, 32), CompanionAccent))
                    Run(() => RedeemPrizePokemon(prize.Species), $"Redeeming {prize.Species}...");
                ImGui.EndTable();
            }
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    private bool DrawPokedexEntryCard(PokedexEntryDto entry, bool registered, bool selected)
    {
        var scale = CompanionScale;
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 158 * scale);
        var clicked = registered && ImGui.InvisibleButton($"##Dex{entry.DexNumber}", size);
        if (!registered) ImGui.Dummy(size);
        var hovered = registered && ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (!registered && ImGui.IsItemHovered()) ImGui.SetTooltip("Catch this species to unlock its details.");
        var min = ImGui.GetItemRectMin();
        var draw = ImGui.GetWindowDrawList();
        var border = selected || hovered ? CompanionAccent : new Vector4(0.22f, 0.22f, 0.25f, 1);
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.17f, 0.22f, 1) : CompanionSurface), 4 * scale);
        if (hovered) draw.AddRect(min - Vector2.One, min + size + Vector2.One, ImGui.GetColorU32(CompanionAccent with { W = 0.2f }), 4 * scale, ImDrawFlags.None, 4 * scale);
        if (selected || hovered) draw.AddRect(min, min + size, ImGui.GetColorU32(border), 4 * scale);
        DrawOnboardingText(min, size.X, 8 * scale, $"#{entry.DexNumber:0000}", CompanionMuted, scale);
        if (registered)
        {
            var after = ImGui.GetCursorScreenPos();
            var portrait = Math.Max(1, Math.Min(78 * scale, size.X - 16 * scale));
            ImGui.SetCursorScreenPos(min + new Vector2((size.X - portrait) / 2, 26 * scale));
            DrawPokemonIcon(entry.Species, false, new Vector2(portrait));
            ImGui.SetCursorScreenPos(after);
            DrawOnboardingText(min, size.X, 112 * scale, entry.Species, ImGui.GetStyle().Colors[(int)ImGuiCol.Text], scale);
            DrawOnboardingText(min, size.X, 134 * scale, selected ? "Selected" : "Registered", selected ? CompanionAccent : CompanionMuted, scale);
        }
        else
        {
            DrawOnboardingText(min, size.X, 51 * scale, "?", CompanionMuted, scale);
            DrawOnboardingText(min, size.X, 95 * scale, "Undiscovered", CompanionMuted, scale);
        }
        return clicked;
    }


    private void DrawReadyEvolutionActions(PokemonDto pokemon)
    {
        if (trainer is null) return;
        foreach (var evolution in trainer.EvolutionOptions.Where(entry => entry.PokemonId == pokemon.Id && entry.Ready))
        {
            ImGui.Spacing();
            ImGui.TextColored(CompanionAccent, "Evolution available");
            DrawEvolutionButton(pokemon, evolution);
            ImGui.TextWrapped(evolution.Requirement);
        }
    }


    private void DrawMoveRelearner(PokemonDto pokemon)
    {
        if (!ImGui.TreeNode($"Move Relearner  ({pokemon.RelearnableMoves.Count})##Relearn{pokemon.Id}"))
            return;
        if (pokemon.RelearnableMoves.Count == 0)
        {
            ImGui.TextDisabled("No earlier level-up moves are currently available.");
            ImGui.TreePop();
            return;
        }

        ImGui.TextDisabled("Relearn any level-up move this Pokémon has already unlocked.");
        foreach (var move in pokemon.RelearnableMoves)
        {
            if (pokemon.Moves.Count < 4)
            {
                if (DrawAccentButton($"Relearn {move}##Relearn{pokemon.Id}{move}", Vector2.Zero, PokemonBlue))
                    Run(() => RelearnMove(pokemon.Id, move, null), $"Relearning {move}...");
                continue;
            }

            if (!ImGui.TreeNode($"Relearn {move}##ChooseForgotten{pokemon.Id}{move}"))
                continue;
            ImGui.TextDisabled("Choose the move to forget:");
            foreach (var knownMove in pokemon.Moves)
            {
                if (DrawAccentSmallButton($"{knownMove}##RelearnForget{pokemon.Id}{move}{knownMove}", PokemonOrange))
                    Run(() => RelearnMove(pokemon.Id, move, knownMove), $"Relearning {move}...");
                ImGui.SameLine();
            }
            ImGui.NewLine();
            ImGui.TreePop();
        }
        ImGui.TreePop();
    }

    private void DrawHeldItemManagement(PokemonDto pokemon)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.65f, 0.85f, 1f, 1f), "HELD ITEM");
        if (string.IsNullOrWhiteSpace(pokemon.HeldItem))
        {
            ImGui.TextDisabled("None - held items take effect when the next battle starts.");
        }
        else
        {
            var held = itemCatalog.FirstOrDefault(item => item.Id == pokemon.HeldItem);
            DrawItemImage(pokemon.HeldItem, new Vector2(34, 34));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(held?.Name ?? pokemon.HeldItem);
            if (DrawAccentSmallButton($"Take item##Unequip{pokemon.Id}", PokemonOrange))
                Run(() => SetHeldItem(pokemon.Id, null), $"Taking {held?.Name ?? pokemon.HeldItem} from {pokemon.Nickname}...");
            ImGui.EndGroup();
        }

        DrawMegaEvolutionGuide(pokemon);
        if (!ImGui.TreeNode($"Choose from bag##HeldItemBag{pokemon.Id}"))
            return;
        var available = itemCatalog.Where(item => (item.Category == "held" || item.CanBeHeld) && InventoryCount(item.Id) > 0).ToList();
        if (available.Count == 0)
        {
            ImGui.TextDisabled("No held items are currently in your bag.");
        }
        else
        {
            foreach (var item in available)
            {
                DrawItemImage(item.Id, new Vector2(30, 30));
                ImGui.SameLine();
                if (DrawAccentButton($"Give {item.Name}  ×{InventoryCount(item.Id)}##Equip{pokemon.Id}{item.Id}", Vector2.Zero, PokemonGreen))
                    Run(() => SetHeldItem(pokemon.Id, item.Id), $"Giving {item.Name} to {pokemon.Nickname}...");
            }
        }
        ImGui.TreePop();
    }

    private void DrawRareCandyTraining(PokemonDto pokemon)
    {
        var rareCandies = InventoryCount("rarecandy");
        if (rareCandies <= 0)
            return;

        ImGui.Spacing();
        if (pokemon.Level >= 100)
        {
            ImGui.TextDisabled($"Rare Candy ×{rareCandies}  •  Maximum level reached");
            return;
        }
        if (DrawAccentButton($"Use Rare Candy  ×{rareCandies}##RareCandy{pokemon.Id}", Vector2.Zero, PokemonBlue))
            Run(() => UseRareCandy(pokemon.Id), $"Raising {pokemon.Nickname} to level {pokemon.Level + 1}...");
        ImGui.TextDisabled($"Lv. {pokemon.Level} → {pokemon.Level + 1}");
    }

    private void DrawIvTraining(PokemonDto pokemon)
    {
        if (!ImGui.TreeNode($"IVs & Hyper Training##{pokemon.Id}"))
            return;

        var stats = new (string Label, string Id)[]
        {
            ("HP", "hp"), ("Atk", "atk"), ("Def", "def"), ("SpA", "spa"), ("SpD", "spd"), ("Spe", "spe")
        };
        ImGui.TextWrapped(string.Join("   ", stats.Select(stat => $"{stat.Label} {pokemon.Ivs.GetValueOrDefault(stat.Id)}")));

        var bottleCaps = InventoryCount("bottlecap");
        if (bottleCaps > 0)
        {
            ImGui.TextDisabled($"Bottle Caps: {bottleCaps} - choose one imperfect IV");
            if (ImGui.BeginTable($"HyperTrainingStats{pokemon.Id}", 3, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var stat in stats.Where(stat => pokemon.Ivs.GetValueOrDefault(stat.Id) < 31))
                {
                    ImGui.TableNextColumn();
                    if (DrawAccentButton($"Max {stat.Label}##Train{pokemon.Id}{stat.Id}", new Vector2(-1, 28 * CompanionScale), CompanionAccent))
                        Run(() => UseTrainingItem(pokemon.Id, "bottlecap", stat.Id), $"Hyper Training {pokemon.Nickname}'s {stat.Label} IV...");
                }
                ImGui.EndTable();
            }
        }

        var goldCaps = InventoryCount("goldbottlecap");
        if (goldCaps > 0 && pokemon.Ivs.Values.Any(value => value < 31))
        {
            if (DrawAccentButton($"Use Gold Bottle Cap ({goldCaps})##GoldTrain{pokemon.Id}", Vector2.Zero, PokemonGreen))
                Run(() => UseTrainingItem(pokemon.Id, "goldbottlecap", null), $"Perfecting {pokemon.Nickname}'s IVs...");
        }
        ImGui.TreePop();
    }

    private void DrawPokemonImage(string species, bool shiny, Vector2 size)
    {
        DrawFrontSprite(species, shiny, size);
        DrawAlcremieBadge(species, size);
    }

    private void DrawPokemonBattleImage(string species, bool shiny, Vector2 size)
    {
        var animatedTexture = plugin.AnimatedSprites.GetCurrentFrame(species, shiny);
        if (animatedTexture is { } frame)
        {
            DrawFittedSprite(frame.Texture, frame.Region, size);
            return;
        }

        DrawFrontSprite(species, shiny, size);
    }

    private void DrawPokemonIcon(string species, bool shiny, Vector2 size)
    {
        DrawFrontSprite(species, shiny, size);
        DrawAlcremieBadge(species, size);
    }

    private void DrawItemImage(string itemId, Vector2 size)
    {
        var iconId = plugin.Assets.HasItem(itemId) ? itemId : itemCatalog.FirstOrDefault(item => item.Id == itemId)?.IconId ?? itemId;
        var path = plugin.Assets.Item(iconId);
        if (path.Length == 0)
        {
            ImGui.Dummy(size);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Item artwork is not available in the asset pack.");
            return;
        }
        var texture = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        if (texture is not null)
            ImGui.Image(texture.Handle, size);
        else
            ImGui.Dummy(size);
    }

    private int InventoryCount(string itemId) =>
        trainer?.Inventory.FirstOrDefault(entry => entry.ItemId == itemId)?.Quantity ?? 0;

    private async Task Initialize()
    {
        await InSession(LoadCatalogs(false));

        if (plugin.Api.HasSession)
        {
            try
            {
                trainer = await InSession(plugin.Api.GetMe());
                await InSession(MigrateTrainerCard());
                await InSession(LoadPokedexCatalog());
                plugin.Connection.Start();
            }
            catch (ApiException exception) when (exception.StatusCode == 401)
            {
                plugin.Api.Logout();
                trainer = null;
            }
        }

        if (catalogError is null)
            SetStatus(trainer is null ? "Log in to begin." : "Trainer data loaded.", quiet: true);
        else
            SetStatus("Connected. Retrying unavailable game data automatically.", true);
    }

    private async Task LoadCatalogs(bool includePokedex = true)
    {
        var starterTask = plugin.Api.GetStarters();
        var shopTask = plugin.Api.GetShop();
        var collectionRewardTask = plugin.Api.GetCollectionRewards();
        var frontierTask = plugin.Api.GetFrontierCatalog();
        var legendaryTask = plugin.Api.GetLegendaryCatalog();
        var specialTrialTask = plugin.Api.GetSpecialTrials();
        var prizeTask = plugin.Api.GetPrizePokemon();
        var opponentTask = plugin.Api.GetOpponents();
        var friendlyTask = plugin.Api.GetFriendlyFormats();
        var pokedexTask = includePokedex && plugin.Api.HasSession ? plugin.Api.GetPokedexCatalog() : null;
        var catalogTasks = new List<Task> { starterTask, shopTask, prizeTask, opponentTask, collectionRewardTask, frontierTask, legendaryTask, specialTrialTask, friendlyTask };
        if (pokedexTask is not null)
            catalogTasks.Add(pokedexTask);
        try
        {
            await InSession(Task.WhenAll(catalogTasks));
        }
        catch (StaleSessionException) { throw; }
        catch
        {
            // Collect individual catalog failures below.
        }

        var failures = new List<string>();
        if (starterTask.IsCompletedSuccessfully)
        {
            starters = (await InSession(starterTask)).Starters;
            if (starters.Count == 0)
                failures.Add("The server returned an empty starter catalog.");
        }
        else
        {
            failures.Add($"Starters: {TaskFailure(starterTask)}");
        }

        if (shopTask.IsCompletedSuccessfully)
        {
            ApplyShopCatalog(await InSession(shopTask));
        }
        else
            failures.Add($"Shop: {TaskFailure(shopTask)}");

        if (collectionRewardTask.IsCompletedSuccessfully)
            collectionRewards = (await InSession(collectionRewardTask)).Rewards;
        else
            failures.Add($"Collection Exchange: {TaskFailure(collectionRewardTask)}");
        if (frontierTask.IsCompletedSuccessfully) frontierRentals = (await InSession(frontierTask)).Rentals;
        else failures.Add($"Frontier: {TaskFailure(frontierTask)}");
        if (legendaryTask.IsCompletedSuccessfully) legendaryBosses = (await InSession(legendaryTask)).Bosses;
        else failures.Add($"Sanctuaries: {TaskFailure(legendaryTask)}");
        if (specialTrialTask.IsCompletedSuccessfully) specialTrials = (await InSession(specialTrialTask)).Trials;
        else failures.Add($"Special Trials: {TaskFailure(specialTrialTask)}");

        if (prizeTask.IsCompletedSuccessfully)
            prizePokemon = (await InSession(prizeTask)).Pokemon;
        else
            failures.Add($"Prize Exchange: {TaskFailure(prizeTask)}");

        if (opponentTask.IsCompletedSuccessfully)
        {
            var catalog = await InSession(opponentTask);
            opponents = catalog.Opponents; regionalLeagues = catalog.Regions;
        }
        else
            failures.Add($"PvE: {TaskFailure(opponentTask)}");
        if (friendlyTask.IsCompletedSuccessfully) { friendlyFormats.Clear(); friendlyFormats.AddRange((await InSession(friendlyTask)).Formats); }
        else failures.Add($"Friendly formats: {TaskFailure(friendlyTask)}");

        if (pokedexTask?.IsCompletedSuccessfully == true)
            pokedexCatalog = (await InSession(pokedexTask)).Pokemon;
        else if (pokedexTask is not null)
            failures.Add($"Pokédex: {TaskFailure(pokedexTask)}");

        catalogError = failures.Count == 0 ? null : string.Join("  ", failures);
        nextCatalogRetryAt = catalogError is null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddSeconds(5);
        if (catalogError is null)
            SetStatus("Game data loaded.", quiet: true);
        else
            Plugin.Log.Warning("Magitek Monsters catalog loading was incomplete: {CatalogError}", catalogError);
    }

    private async Task LoadPokedexCatalog()
    {
        await InSession(LoadBattleHelp());
        pokedexCatalog = (await InSession(plugin.Api.GetPokedexCatalog(disposeCancellation.Token))).Pokemon;
        if (selectedPokedexSpecies is not null
            && !pokedexCatalog.Any(entry => entry.Registered
                && entry.Species.Equals(selectedPokedexSpecies, StringComparison.OrdinalIgnoreCase)))
            selectedPokedexSpecies = null;
    }

    private static string TaskFailure(Task task) =>
        task.Exception?.GetBaseException().Message
        ?? (task.IsCanceled ? "Request cancelled." : "Request did not complete.");

    private static TerritoryContextInfo CurrentTerritoryContext(uint territoryId)
    {
        var localizedName = $"Territory #{territoryId}";
        var englishName = string.Empty;
        var regionName = string.Empty;
        var zoneName = string.Empty;
        try
        {
            var territory = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow(territoryId);
            if (territory.PlaceName.IsValid)
            {
                var name = territory.PlaceName.Value.Name.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    localizedName = name;
            }

            var englishTerritory = Plugin.DataManager.GetExcelSheet<TerritoryType>(ClientLanguage.English).GetRow(territoryId);
            if (englishTerritory.PlaceName.IsValid)
            {
                englishName = englishTerritory.PlaceName.Value.Name.ToString().Trim();
            }
            if (englishTerritory.PlaceNameRegion.IsValid)
            {
                regionName = englishTerritory.PlaceNameRegion.Value.Name.ToString().Trim();
            }
            if (englishTerritory.PlaceNameZone.IsValid)
            {
                zoneName = englishTerritory.PlaceNameZone.Value.Name.ToString().Trim();
            }
        }
        catch
        {
            // Some territories have no place-name row.
        }
        if (string.IsNullOrWhiteSpace(englishName))
            englishName = localizedName;
        return new TerritoryContextInfo(localizedName, englishName, regionName, zoneName);
    }

    private readonly record struct TerritoryContextInfo(
        string LocalizedName,
        string EnglishName,
        string RegionName,
        string ZoneName);

    private async Task StartXivAuth()
    {
        authorizationFlow = await InSession(plugin.Api.StartXivAuth(disposeCancellation.Token));
        Util.OpenLink(authorizationFlow.AuthorizationUrl);
        SetStatus("Waiting for the XIVAuth callback...");

        var expiresAt = DateTimeOffset.TryParse(authorizationFlow.ExpiresAt, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow.AddMinutes(10);
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await InSession(Task.Delay(TimeSpan.FromSeconds(2), disposeCancellation.Token));
            var result = await InSession(plugin.Api.PollXivAuth(authorizationFlow.LoginId, disposeCancellation.Token));
            if (result.Status == "complete" && result.Session is not null)
            {
                plugin.Api.SaveSession(result.Session);
                sessionResponses.Invalidate();
                ResetTrainerProfiles();
                ResetEvTrainingPlanner();
                ResetTeamAnalysis();
                ResetCollectionWishlist();
                ResetRecentActivity();
                ResetStorageSearches();
                ResetBattleLog();
                plugin.Connection.Start();
                authorizationFlow = null;
                trainer = await InSession(plugin.Api.GetMe());
                await InSession(MigrateTrainerCard());
                await InSession(LoadPokedexCatalog());
                SetStatus("XIVAuth character login complete.");
                return;
            }
            if (result.Status == "failed")
                throw new ApiException(0, result.Message ?? "XIVAuth login could not be completed.");
        }
        authorizationFlow = null;
        throw new ApiException(0, "The XIVAuth login request expired.");
    }

    private async Task ChooseStarter(string species)
    {
        trainer = await InSession(plugin.Api.SelectStarter(species));
        await InSession(LoadPokedexCatalog());
        SetStatus($"{species} joined your party!");
    }

    private async Task LoadHabitat(uint territoryId, TerritoryContextInfo context)
    {
        habitat = await InSession(plugin.Api.GetHabitat(
            territoryId, context.LocalizedName, context.EnglishName, context.RegionName, context.ZoneName));
        pokedexCatalog = (await InSession(plugin.Api.GetPokedexCatalog())).Pokemon;
        loadedTerritory = territoryId;
        targetIndex = 0;
        SetStatus($"Surveyed {habitat.Name}.");
    }

    private async Task Search(uint territoryId, TerritoryContextInfo context, string? target)
    {
        var encounter = await InSession(plugin.Api.Search(
            territoryId, context.LocalizedName, context.EnglishName, context.RegionName, context.ZoneName, target));
        trainer!.ActiveEncounter = encounter;
        refreshState.Trainer();
        await InSession(ResumeWildBattle(encounter.Id));
        SetStatus(encounter.Pokemon.Shiny ? $"A shiny {encounter.Pokemon.Species} appeared!" : $"A wild {encounter.Pokemon.Species} appeared!");
    }

    private async Task ResumeWildBattle(string encounterId)
    {
        ApplyBattleResponse(await InSession(plugin.Api.StartWildBattle(encounterId)));
        megaEvolveSelected = false;
        nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(1);
        SetStatus("Wild battle ready. Choose your action.");
    }

    private async Task MovePokemon(string pokemonId, string destination)
    {
        trainer = await InSession(plugin.Api.MovePokemon(pokemonId, destination));
        SetStatus(destination == "party" ? "Pokémon added to the party." : "Pokémon sent to PC storage.");
    }

    private async Task ReleasePokemon(string pokemonId, string nickname)
    {
        trainer = await InSession(plugin.Api.ReleasePokemon(pokemonId));
        SetStatus($"{nickname} was released.");
    }

    private async Task ReorderPokemon(string pokemonId, int partySlot)
    {
        trainer = await InSession(plugin.Api.MovePokemon(pokemonId, "party", partySlot));
        var lead = trainer.Party.OrderBy(pokemon => pokemon.PartySlot).FirstOrDefault();
        SetStatus(lead is null ? "Party order updated." : $"Party order updated. {lead.Nickname} will lead.");
    }

    private async Task BuyItem(string itemId, int quantity = 1)
    {
        trainer = await InSession(plugin.Api.BuyItem(itemId, quantity, disposeCancellation.Token));
        SetStatus("Purchase complete.");
    }

    private async Task RedeemPrizePokemon(string species)
    {
        var joinsParty = trainer is not null && trainer.Party.Count < 6;
        trainer = await InSession(plugin.Api.RedeemPrizePokemon(species));
        await InSession(LoadPokedexCatalog());
        SetStatus(joinsParty ? $"{species} joined your party!" : $"{species} was sent to PC storage.");
    }

    private async Task UseTrainingItem(string pokemonId, string itemId, string? stat, int quantity = 1)
    {
        trainer = await InSession(plugin.Api.UseTrainingItem(pokemonId, itemId, stat, quantity));
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == pokemonId);
        RecordRecentAction("training:" + Guid.NewGuid(), "training", "Trained " + (pokemon?.Nickname ?? "Pokémon"),
            $"Used {quantity} x {itemCatalog.FirstOrDefault(item => item.Id == itemId)?.Name ?? itemId}");
        SetStatus($"Used {quantity} x {itemCatalog.FirstOrDefault(item => item.Id == itemId)?.Name ?? itemId}.");
    }

    private async Task UseRareCandy(string pokemonId)
    {
        trainer = await InSession(plugin.Api.UseRareCandy(pokemonId));
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == pokemonId);
        RecordRecentAction("candy:" + Guid.NewGuid(), "training", "Used a Rare Candy", pokemon is null ? "" : $"{pokemon.Nickname} / Lv. {pokemon.Level}");
        SetStatus(pokemon is null ? "Rare Candy used." : $"{pokemon.Nickname} grew to level {pokemon.Level}!");
    }

    private async Task EvolvePokemon(string pokemonId, string targetSpecies)
    {
        (evolutionEnvironment, evolutionEnvironmentLabel) = EvolutionEnvironment.Read(CurrentEvolutionTheme);
        trainer = await InSession(plugin.Api.EvolvePokemon(pokemonId, targetSpecies, evolutionEnvironment));
        await InSession(LoadPokedexCatalog());
        var evolved = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == pokemonId);
        SetStatus(evolved is null ? $"Evolution into {targetSpecies} complete!" : $"Congratulations! Your Pokémon evolved into {evolved.Species}!");
    }

    private async Task SetHeldItem(string pokemonId, string? itemId)
    {
        trainer = await InSession(plugin.Api.SetHeldItem(pokemonId, itemId));
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == pokemonId);
        var item = itemId is null ? null : itemCatalog.FirstOrDefault(entry => entry.Id == itemId);
        SetStatus(itemId is null
            ? $"{pokemon?.Nickname ?? "Pokémon"} is no longer holding an item."
            : $"{pokemon?.Nickname ?? "Pokémon"} is now holding {item?.Name ?? itemId}.");
    }

    private async Task ResolveMoveLearning(string learningId, string? forgetMove)
    {
        trainer = await InSession(plugin.Api.ResolveMoveLearning(learningId, forgetMove));
        SetStatus(forgetMove is null ? "The move was not learned." : "The new move was learned.");
    }

    private async Task RelearnMove(string pokemonId, string move, string? forgetMove)
    {
        trainer = await InSession(plugin.Api.RelearnMove(pokemonId, move, forgetMove));
        SetStatus(forgetMove is null ? $"Relearned {move}." : $"Forgot {forgetMove} and relearned {move}.");
    }

    private Task StartBattle(string opponentId, string challenge, string? monotype, string[] pokemonIds) =>
        StartPreparedBattle(opponentId, challenge, monotype, pokemonIds, null);

    private async Task StartPreparedBattle(string opponentId, string challenge, string? monotype, string[] pokemonIds, string? circuitId)
    {
        var current = await InSession(plugin.Api.StartBattle(opponentId, challenge, monotype, pokemonIds, circuitId: circuitId));
        ApplyBattleResponse(current);
        megaEvolveSelected = false;
        nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(1);
        SetStatus(current.Request is not null && current.Request.Wait != true ? "Battle started. Choose your move." : "Battle started. Preparing the field...");
    }

    private async Task ChooseBattle(string choice)
    {
        if (battle is null)
            return;
        try
        {
            ApplyBattleResponse(await InSession(plugin.Api.ChooseBattleAction(battle.Id, choice, battle.Revision)), choice);
        }
        catch (ApiException exception) when (exception.StatusCode == 409)
        {
            ApplyBattleResponse(await InSession(plugin.Api.GetBattle(battle.Id)));
            refreshState.Trainer();
            throw;
        }
        megaEvolveSelected = false;
        nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(1);
        SetStatus(battle.Status == "lost" && battle.CurrencyLost > 0 ? $"Defeat. Lost ¥ {battle.CurrencyLost:N0}."
            : battle.Status != "active" ? $"Battle ended: {battle.Status}."
            : battle.Request is not null && battle.Request.Wait != true ? "Choose your next action."
            : "Resolving the turn...");
    }


    private async Task RefreshPvp()
    {
        pvpStatus = await InSession(plugin.Api.GetPvpStatus());
        ObserveRecentBattle(pvpStatus.Battle, true);
        if (trainer is not null)
            trainer.PvpProfile = pvpStatus.Profile;
        nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(pvpStatus.State == "queued" ? 2 : 1);
        if (pvpStatus.State == "battle" && pvpStatus.Battle?.Status != "active")
            SetStatus($"PvP match ended: {pvpStatus.Battle?.Status ?? "complete"}.");
        else if (pvpStatus.State == "battle" && pvpStatus.Battle?.Request is not null && pvpStatus.Battle.Request.Wait != true)
            SetStatus("PvP match ready. Choose your action.");
        else if (pvpStatus.State == "queued")
            SetStatus("Searching for another Trainer...");
        else
            SetStatus("PvP status synchronized.", quiet: true);
    }

    private async Task JoinPvpQueue()
    {
        var mode = pvpModeIndex == 1 ? "ranked" : "casual";
        pvpTeamError = string.Empty;
        try
        {
            pvpStatus = await InSession(plugin.Api.JoinPvpQueue(mode, selectedPvpFormat,
                SelectedOnlineTeam()));
        }
        catch (ApiException exception) when (exception.StatusCode == 400)
        {
            pvpTeamError = exception.Message;
            SetStatus("Team rejected. See the validation details in Online Battles.", true);
            return;
        }
        if (trainer is not null)
            trainer.PvpProfile = pvpStatus.Profile;
        nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(1);
        SetStatus(pvpStatus.State == "battle" ? "Opponent found. The PvP match is starting!" : $"Joined the {mode} queue.");
    }

    private async Task LeavePvpQueue()
    {
        pvpStatus = await InSession(plugin.Api.LeavePvpQueue());
        SetStatus("Left the PvP queue.");
    }

    private async Task ChoosePvpBattle(string choice)
    {
        if (pvpStatus?.Battle is null)
            return;
        try { pvpStatus.Battle = await InSession(plugin.Api.ChoosePvpBattleAction(pvpStatus.Battle.Id, choice, revision: pvpStatus.Battle.Revision)); }
        catch (ApiException error) when (error.Code == "stale_battle_choice")
        {
            pvpStatus.Battle = await InSession(plugin.Api.GetPvpBattle(pvpStatus.Battle.Id));
            SetStatus("The battle changed. Choose from the current actions."); return;
        }
        ObserveRecentBattle(pvpStatus.Battle, true, true);
        megaEvolveSelected = false;
        nextPvpPollAt = DateTimeOffset.UtcNow.AddSeconds(1);
        if (pvpStatus.Battle.Status != "active")
            await InSession(RefreshPvp());
        else
            SetStatus(pvpStatus.Battle.Request is not null && pvpStatus.Battle.Request.Wait != true
                ? "Choose your next PvP action."
                : "Waiting for the other Trainer...");
    }

    private async Task ForfeitPvpBattle()
    {
        if (pvpStatus?.Battle is null)
            return;
        pvpStatus.Battle = await InSession(plugin.Api.ForfeitPvpBattle(pvpStatus.Battle.Id));
        ObserveRecentBattle(pvpStatus.Battle, true, true);
        await InSession(RefreshPvp());
        SetStatus("The PvP match was forfeited.");
    }

    private async Task DismissPvpBattle()
    {
        pvpStatus = await InSession(plugin.Api.DismissPvpBattle());
        SetStatus("Ready for another PvP match.");
    }

    private async Task RefreshTrainer()
    {
        if (!plugin.Api.HasSession)
        {
            trainer = null;
            SetStatus("Log in to begin.");
            return;
        }
        refreshState.TakeTrainer();
        var previousCaught = trainer?.Pokedex.Caught ?? -1;
        trainer = await InSession(plugin.Api.GetMe());
        AcknowledgeBattleResultTrainer();
        if (trainer.Pokedex.Caught != previousCaught)
            await InSession(LoadPokedexCatalog());
        SetStatus("Trainer data refreshed.", quiet: true);
    }

    private void Run(Func<Task> action, string progress)
    {
        if (busy)
            return;
        CancelBackgroundRefresh();
        headerFeedback.Clear();
        busy = true;
        foregroundGeneration++;
        status = progress;
        statusIsError = false;
        _ = RunGuarded(action);
    }

    private async Task RunGuarded(Func<Task> action)
    {
        var generation = foregroundGeneration;
        try
        {
            await action();
        }
        catch (StaleSessionException) { }
        catch (Exception) when (generation != foregroundGeneration || disposeCancellation.IsCancellationRequested) { }
        catch (ApiException exception) when (exception.StatusCode == 426)
        {
            plugin.Api.Update.Require(exception.Message);
            SetStatus(exception.Message, true);
        }
        catch (ApiException exception) when (exception.Code == "mutation_committed")
        {
            await RecoverAction(true);
        }
        catch (UnconfirmedActionException)
        {
            await RecoverAction(false);
        }
        catch (ApiException exception) when (exception.StatusCode == 429)
        {
            if (exception.Code == "search_cooldown" && exception.RetryAfterSeconds is { } seconds) searchCooldown.Observe(seconds);
            SetStatus(exception.Message);
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(exception, "Magitek Monsters operation failed");
            SetStatus(exception.Message, true);
        }
        finally
        {
            if (generation == foregroundGeneration) busy = false;
        }
    }

    private void SetStatus(string message, bool isError = false, bool quiet = false)
    {
        status = message;
        statusIsError = isError;
        headerFeedback.Observe(message, isError, quiet);
    }

    private void Disconnect(bool leavePvp = true)
    {
        Task? cleanup = !leavePvp ? null : pvpStatus?.State == "queued"
            ? plugin.Api.LeavePvpQueue()
            : pvpStatus?.Battle?.Status == "active"
                ? plugin.Api.ForfeitPvpBattle(pvpStatus.Battle.Id)
                : null;
        if (cleanup is not null)
        {
            _ = cleanup.ContinueWith(task => Plugin.Log.Warning(task.Exception, "Magitek Monsters could not cleanly leave PvP during disconnect"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        CancelBackgroundRefresh();
        refreshState.Reset();
        plugin.Api.Logout();
        sessionResponses.Invalidate();
        foregroundGeneration++;
        busy = false;
        Interlocked.Exchange(ref pendingClipboard, null);
        pendingTravel = null;
        travelReportBusy = evolutionHabitatBusy = false;
        lastTravelPosition = null;
        travelDistance = 0;
        travelPartyKey = string.Empty;
        plugin.Connection.Stop();
        connectionSyncGeneration = 0;
        trainer = null;
        selectedPartyPokemon = null;
        selectedStoragePokemon = null;
        releasePokemonId = null;
        requestRelease = false;
        pendingPurchase = null;
        requestPurchase = false;
        managePokemonId = null;
        requestPokemonManagement = false;
        requestedManagementTab = null;
        savedTeamEdit = null;
        savedTeamEditName = string.Empty;
        savedTeamLibraryOwner = string.Empty;
        requestSavedTeamLibrary = requestSavedTeamEdit = closeSavedTeamLibrary = savedTeamEditComplete = false;
        requestedShopTab = null;
        selectedBagItem = null;
        bagPokemonId = null;
        bagSearch = string.Empty;
        bagPocket = 0;
        bagPage = 0;
        pokemonMovesOpen = false;
        ResetCollectionView();
        pendingLegendaryPermit = null;
        legendaryRequests.Clear();
        recurringSchedule = null;
        eventLoadedAt = DateTimeOffset.MinValue;
        nextEventRefresh = DateTimeOffset.MinValue;
        eventRequests.Clear();
        eventPage = 0;
        pokedexCatalog.Clear();
        selectedPokedexSpecies = null;
        habitat = null;
        battle = null;
        pvpStatus = null;
        raidRoom = null; raidBoard = null; raidPokemonId = ""; raidJoinCode = ""; raidRefreshRequested = false;
        pendingRaidChoice = null; raidCaptureRequest = null; raidTargetMove = null;
        pvpTeamIds.Clear();
        friendlyTeamIds.Clear();
        onlineTeamInitialized = false;
        friendlyTeamInitialized = false;
        requestOnlineRosterPicker = false;
        onlineRosterFormat = null;
        onlineRosterSearch = string.Empty;
        onlineView = 1;
        onlineInvitationId = null;
        previewBattleId = string.Empty;
        previewOrder.Clear();
        previewRoster = [];
        previewChosenCount = 0;
        pvpTeamError = string.Empty;
        serverPvpRefreshRequested = false;
        authorizationFlow = null;
        onlineTrainers = -1;
        megaEvolveSelected = false;
        trainerIdCopiedUntil = DateTimeOffset.MinValue;
        SetStatus("Disconnected from Magitek Monsters.");
    }

    private static string FormatBattleLine(string line) => FormatBattleLine(line, "p1");

    private static string FormatBattleLine(string line, string ownSide)
    {
        string BattleActor(string ident) => MainWindow.BattleActor(ident, ownSide);
        var parts = line.Split('|');
        if (parts.Length < 2)
            return line;
        return parts[1] switch
        {
            "start" => "The battle began!",
            "turn" when parts.Length > 2 => $"- Turn {parts[2]} -",
            "move" when parts.Length > 3 => $"{BattleActor(parts[2])} used {parts[3]}!",
            "switch" when parts.Length > 3 => parts[2].StartsWith(ownSide, StringComparison.Ordinal)
                ? $"You sent out {CleanIdent(parts[2])}!"
                : $"The opponent sent out {CleanIdent(parts[2])}!",
            "-damage" when parts.Length > 3 => $"{BattleActor(parts[2])} now has {parts[3]} HP.",
            "-heal" when parts.Length > 3 => $"{BattleActor(parts[2])} recovered to {parts[3]} HP.",
            "faint" when parts.Length > 2 => $"{BattleActor(parts[2])} fainted!",
            "-supereffective" when parts.Length > 2 => "It's super effective!",
            "-resisted" when parts.Length > 2 => "It's not very effective...",
            "-miss" when parts.Length > 2 => $"{BattleActor(parts[2])} avoided the attack.",
            "-crit" when parts.Length > 2 => "A critical hit!",
            "-status" when parts.Length > 3 => $"{BattleActor(parts[2])} is now {parts[3].ToUpperInvariant()}.",
            "-curestatus" when parts.Length > 3 => $"{BattleActor(parts[2])} recovered from {parts[3].ToUpperInvariant()}.",
            "-mega" when parts.Length > 3 => $"{BattleActor(parts[2])} Mega Evolved into {parts[3]}!",
            "-burst" when parts.Length > 3 => $"{BattleActor(parts[2])} transformed into {parts[3]}!",
            "-terastallize" when parts.Length > 3 => $"{BattleActor(parts[2])} Terastallized into the {parts[3]} type!",
            "-zpower" when parts.Length > 2 => $"{BattleActor(parts[2])} is using its Z-Power!",
            "-weather" when parts.Length > 2 => parts[2] == "none" ? "The weather cleared." : $"Weather: {parts[2]}. {WeatherHint(parts[2])}",
            "-sidestart" when parts.Length > 3 => $"{CleanIdent(parts[2])}: {parts[3]} started. {FieldHint(parts[3])}",
            "-sideend" when parts.Length > 3 => $"{CleanIdent(parts[2])}: {parts[3]} ended.",
            "-fieldstart" when parts.Length > 2 => $"{parts[2]} started. {FieldHint(parts[2])}",
            "-fieldend" when parts.Length > 2 => $"{parts[2]} ended.",
            "-boost" when parts.Length > 4 => $"{BattleActor(parts[2])}: {parts[3]} rose {parts[4]} stage(s).",
            "-unboost" when parts.Length > 4 => $"{BattleActor(parts[2])}: {parts[3]} fell {parts[4]} stage(s).",
            "-immune" when parts.Length > 2 => $"{BattleActor(parts[2])} is immune!",
            "message" when parts.Length > 2 => parts[2],
            "win" when parts.Length > 2 => $"{parts[2]} won the battle!",
            "tie" => "The battle ended in a draw.",
            _ => string.Empty
        };
    }

    private static string WeatherHint(string weather) => weather switch
    {
        "RainDance" => "Water attacks are stronger; Fire attacks are weaker.",
        "SunnyDay" => "Fire attacks are stronger; Water attacks are weaker.",
        "Sandstorm" => "Sand chips most Pokémon; Rock types gain special bulk.",
        "Snow" => "Ice types gain physical bulk.",
        _ => string.Empty
    };

    private static string FieldHint(string effect) => effect.Replace("move: ", string.Empty) switch
    {
        "Reflect" => "Physical damage is reduced.", "Light Screen" => "Special damage is reduced.",
        "Aurora Veil" => "Physical and special damage are reduced.",
        "Trick Room" => "Slower Pokémon act first within the same priority bracket.",
        "Tailwind" => "This side's Speed is doubled.",
        "Stealth Rock" or "Spikes" => "Switching in causes damage.",
        "Toxic Spikes" => "Grounded switch-ins can be poisoned.", "Sticky Web" => "Grounded switch-ins lose Speed.",
        _ => string.Empty
    };

    private static string CleanIdent(string value)
    {
        var colon = value.IndexOf(':');
        return colon >= 0 ? value[(colon + 1)..].Trim() : value.Trim();
    }

    private static string BattleActor(string ident, string ownSide = "p1")
    {
        var name = CleanIdent(ident);
        if (ident.StartsWith(ownSide, StringComparison.Ordinal))
            return $"Your {name}";
        if (ident.StartsWith("p1", StringComparison.Ordinal) || ident.StartsWith("p2", StringComparison.Ordinal))
            return $"Foe {name}";
        return name;
    }

    private static (float Ratio, string Label) ParseBattleHp(string condition)
    {
        if (condition.EndsWith(" fnt", StringComparison.Ordinal))
            return (0f, "0 HP");
        var hp = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0/100";
        var slash = hp.IndexOf('/');
        if (slash > 0 && int.TryParse(hp[..slash], out var current) && int.TryParse(hp[(slash + 1)..], out var maximum) && maximum > 0)
            return (Math.Clamp((float)current / maximum, 0f, 1f), $"{current} / {maximum} HP");
        return (0f, condition);
    }

    private static (float Ratio, string Label) ExperienceProgress(PokemonDto pokemon)
    {
        if (pokemon.Level >= 100)
            return (1f, $"{pokemon.Experience:N0} total XP  •  MAX LEVEL");
        var levelFloor = pokemon.Level * pokemon.Level * pokemon.Level;
        var nextLevel = pokemon.Level + 1;
        var nextFloor = nextLevel * nextLevel * nextLevel;
        var earnedThisLevel = Math.Clamp(pokemon.Experience - levelFloor, 0, nextFloor - levelFloor);
        var requiredThisLevel = Math.Max(1, nextFloor - levelFloor);
        return ((float)earnedThisLevel / requiredThisLevel, $"{earnedThisLevel:N0} / {requiredThisLevel:N0} XP to Lv. {nextLevel}");
    }

    private static Vector4 BattleLogColor(string line)
    {
        if (line.Contains("|p1", StringComparison.Ordinal))
            return new Vector4(0.65f, 0.82f, 1f, 1f);
        if (line.Contains("|p2", StringComparison.Ordinal))
            return new Vector4(1f, 0.68f, 0.58f, 1f);
        if (line.StartsWith("|turn|", StringComparison.Ordinal))
            return new Vector4(1f, 0.82f, 0.2f, 1f);
        return new Vector4(0.88f, 0.88f, 0.88f, 1f);
    }

    private static Vector4 TypeColor(string type) => type switch
    {
        "Grass" => new Vector4(0.35f, 0.85f, 0.35f, 1f),
        "Fire" => new Vector4(1f, 0.35f, 0.2f, 1f),
        "Water" => new Vector4(0.25f, 0.55f, 1f, 1f),
        "Bug" => new Vector4(0.65f, 0.78f, 0.25f, 1f),
        "Dark" => new Vector4(0.68f, 0.59f, 0.55f, 1f),
        "Dragon" => new Vector4(0.65f, 0.46f, 1f, 1f),
        "Electric" => new Vector4(1f, 0.83f, 0.2f, 1f),
        "Fairy" => new Vector4(1f, 0.65f, 0.85f, 1f),
        "Fighting" => new Vector4(0.95f, 0.45f, 0.35f, 1f),
        "Flying" => new Vector4(0.65f, 0.73f, 1f, 1f),
        "Ghost" => new Vector4(0.65f, 0.55f, 0.86f, 1f),
        "Ground" => new Vector4(0.88f, 0.72f, 0.42f, 1f),
        "Ice" => new Vector4(0.55f, 0.92f, 0.95f, 1f),
        "Normal" => new Vector4(0.8f, 0.8f, 0.7f, 1f),
        "Poison" => new Vector4(0.86f, 0.45f, 0.85f, 1f),
        "Psychic" => new Vector4(1f, 0.42f, 0.65f, 1f),
        "Rock" => new Vector4(0.8f, 0.73f, 0.4f, 1f),
        "Steel" => new Vector4(0.72f, 0.76f, 0.85f, 1f),
        "Stellar" => new Vector4(1f, 0.9f, 0.55f, 1f),
        _ => Vector4.One
    };

    private void OnServerEvent(string eventName, string data)
    {
        if (eventName == "hello") QueueConnectionSync();
        if (eventName == "raid.updated") raidRefreshRequested = true;
        if (eventName is "trainer.updated" or "encounter.updated" or "battle.updated")
        {
            try
            {
                using var update = JsonDocument.Parse(data);
                var id = update.RootElement.TryGetProperty("battleId", out var battleId) && battleId.ValueKind == JsonValueKind.String ? battleId.GetString() : null;
                var revision = update.RootElement.TryGetProperty("revision", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? (int?)number : null;
                var change = update.RootElement.TryGetProperty("changeId", out var receipt) && receipt.ValueKind == JsonValueKind.String ? receipt.GetString() : null;
                if (eventName == "battle.updated") { if (id is not null) refreshState.Battle(id, revision); }
                else refreshState.Trainer(id, revision, change);
            }
            catch (JsonException) { refreshState.Trainer(); }
        }
        if (eventName is "pvp.queue.updated" or "pvp.match.found" or "pvp.battle.updated" or "pvp.challenge.updated")
            serverPvpRefreshRequested = true;
        if (eventName is "hello" or "presence.updated")
        {
            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.TryGetProperty("onlineTrainers", out var count) && count.TryGetInt32(out var online))
                    onlineTrainers = online;
            }
            catch (JsonException exception)
            {
                Plugin.Log.Warning(exception, "Magitek Monsters received malformed presence data");
            }
        }
    }

    private static Vector4 ConnectionColor(ServerConnectionState connectionState) => connectionState switch
    {
        ServerConnectionState.Connected => new Vector4(0.3f, 0.9f, 0.4f, 1f),
        ServerConnectionState.Connecting or ServerConnectionState.Reconnecting or ServerConnectionState.Syncing => new Vector4(1f, 0.8f, 0.25f, 1f),
        ServerConnectionState.Unauthorized or ServerConnectionState.Incompatible => new Vector4(1f, 0.3f, 0.3f, 1f),
        _ => new Vector4(0.55f, 0.55f, 0.55f, 1f)
    };
}
