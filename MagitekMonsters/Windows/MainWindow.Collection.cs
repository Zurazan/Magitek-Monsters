using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string boxName = string.Empty;
    private int storageBox = 1;
    private int storageType;
    private int storageGeneration;
    private int storageSort;
    private bool storageFavorites;
    private bool storageShiny;
    private string challengeTrainerId = string.Empty;
    private string? pendingClipboard;
    private BattleHelpDto? battleHelp;
    private readonly ConcurrentDictionary<string, MoveDetailsDto> battleMoveHelp = new(StringComparer.OrdinalIgnoreCase);

    private bool CollectionLocked => busy || CollectionEditRestriction is not null;
    private string? CollectionEditRestriction
    {
        get
        {
            if (battle?.Status == "active" || trainer?.ActiveTrainerBattle?.Status == "active" || pvpStatus?.Battle?.Status == "active")
                return "Finish the active battle to evolve Pokémon or change moves.";
            if (trainer?.ActiveEncounter is not null) return "Finish or flee your wild encounter before editing Pokémon.";
            if (LeagueBusy) return "Finish or end your League run before editing Pokémon.";
            if (FrontierBusy) return "Finish or end your Frontier run before editing Pokémon.";
            if (trainer?.RaidLocked == true || raidRoom?.Status is "lobby" or "active") return "Finish the raid or leave its lobby before editing Pokémon.";
            if (pvpStatus?.State == "queued") return "Leave matchmaking before editing Pokémon.";
            if (HasOutgoingInvitation) return "Cancel your outgoing battle invitation before editing Pokémon.";
            return null;
        }
    }

    private void DrawCollectionEditNotice()
    {
        if (CollectionEditRestriction is not { } reason) return;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 6) * CompanionScale);
        if (ImGui.BeginTable("CollectionEditNotice", 1, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.22f, 0.13f, 0.15f, 1)));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.66f, 0.69f, 1));
            ImGui.TextWrapped("Pokémon editing locked");
            ImGui.TextWrapped(reason);
            ImGui.PopStyleColor();
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
    }

    private bool HasOutgoingInvitation => pvpStatus?.Challenges.Any(entry => entry.FromUserId == trainer?.User.Id) == true;
    private string BoxLabel(int number) => trainer?.Collection.BoxNames.TryGetValue(number.ToString(), out var name) == true
        ? $"Box {number}: {name}" : $"Box {number}";

    private async Task UpdateCollection(Task<TrainerSnapshotDto> request, string message)
    {
        var owner = trainer?.User.Id;
        var result = await InSession(request);
        if (owner is null || trainer?.User.Id != owner) return;
        trainer = result;
        SetStatus(message);
    }

    private void QueueTeamExport(IEnumerable<string> ids)
    {
        var selected = ids.ToArray();
        var owner = trainer?.User.Id;
        Run(async () =>
        {
            var exported = await InSession(plugin.Api.ExportTeam(selected));
            if (owner is null || trainer?.User.Id != owner) return;
            Interlocked.Exchange(ref pendingClipboard, exported.Text);
            SetStatus("Team summary copied. Exports do not import or create Pokémon.");
        }, "Preparing team summary...");
    }

    private void DrawStorageFilters()
    {
        if (DrawStorageFilterOptions(ref storageFavorites, ref storageShiny, ref storageDuplicatesOnly, ref storageType, ref storageGeneration))
            ShowStorageOverview();
        var scale = CompanionScale;
        ImGui.SetNextItemWidth(Math.Min(240 * scale, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Sort").X - 8 * scale));
        string[] sorts = ["Box order", "Species", "Level: highest first", "Newest first", "IV total: highest first", "Perfect IVs: most first"];
        if (ImGui.Combo("Sort##StorageSort", ref storageSort, sorts, sorts.Length)) storageScrollToTop = true;
    }

    private static bool DrawStorageFilterOptions(ref bool favorites, ref bool shiny, ref bool duplicates, ref int type, ref int generation)
    {
        var scale = CompanionScale;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var changed = ImGui.Checkbox("Favourites", ref favorites);
        StorageSameLine(right, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Shiny only").X);
        changed |= ImGui.Checkbox("Shiny only", ref shiny);
        StorageSameLine(right, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Duplicates only").X);
        changed |= ImGui.Checkbox("Duplicates only", ref duplicates);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Same appearance and shiny variant, including copies in your party.");
        StorageSameLine(right, 150 * scale);
        ImGui.SetNextItemWidth(Math.Min(150 * scale, ImGui.GetContentRegionAvail().X));
        var types = new[] { "Any type" }.Concat(ChallengeTypes).ToArray();
        changed |= ImGui.Combo("##StorageType", ref type, types, types.Length);
        StorageSameLine(right, 150 * scale);
        ImGui.SetNextItemWidth(Math.Min(150 * scale, ImGui.GetContentRegionAvail().X));
        string[] generations = ["Any generation", "Gen 1", "Gen 2", "Gen 3", "Gen 4", "Gen 5", "Gen 6", "Gen 7", "Gen 8", "Gen 9"];
        return ImGui.Combo("##StorageGen", ref generation, generations, generations.Length) || changed;
    }

    private List<PokemonDto> FilterStorage()
    {
        var filter = new StorageFilter(storageSearch, storageBox, storageType == 0 ? null : ChallengeTypes[storageType - 1], storageGeneration,
            storageSort, storageFavorites, storageShiny, storageDuplicatesOnly);
        if (ReferenceEquals(storageFilteredTrainer, trainer) && filter == storageCachedFilter) return storageFilteredPokemon;
        storageFilteredTrainer = trainer; storageCachedFilter = filter;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        storageFilteredPokemon = StorageBrowser.Filter(trainer!.Storage, storageDuplicates, filter);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= 4) Plugin.Log.Debug("PC filter: {PokemonCount} Pokemon in {ElapsedMs:F1} ms", trainer.Storage.Count, elapsed);
        return storageFilteredPokemon;
    }

    private void TogglePokemonFavourite(PokemonDto pokemon) =>
        Run(() => UpdateCollection(plugin.Api.EditPokemon(pokemon.Id, favorite: !pokemon.Favorite), "Favourite updated."), "Updating favourite...");

    private string[] SelectedOnlineTeam() => SelectedOnlinePokemon(pvpTeamIds).Select(pokemon => pokemon.Id).ToArray();

    private readonly HashSet<string> friendlyTeamIds = [];
    private readonly List<PvpFormatDto> friendlyFormats = [];
    private string friendlyFormatId = "gen9nationaldex";
    private string[] FriendlyTeam() => SelectedOnlinePokemon(friendlyTeamIds).Select(pokemon => pokemon.Id).ToArray();

    private async Task LoadBattleHelp()
    {
        if (battleHelp is not null) return;
        ApplyBattleHelp(await InSession(plugin.Api.GetBattleHelp(disposeCancellation.Token)));
    }

    private void ApplyBattleHelp(BattleHelpDto next)
    {
        if (!plugin.Api.Catalogs.TryApply([next], () =>
        {
            battleHelp = next;
            battleMoveHelp.Clear();
            foreach (var move in next.Moves) battleMoveHelp[move.Name] = move;
        })) RetryCatalogs();
    }

    private void DrawBattleMoveTooltip(string name, BattleTypePreview? preview = null, BattleMoveDto? selectedMove = null)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        var label = BattleLayout.MoveLabel(name);
        battleMoveHelp.TryGetValue(label.Name, out var move);
        if (move is null && preview is null) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
        if (move is not null) DrawMoveDetails(move, label.Power, showName: true);
        else ImGui.TextUnformatted(label.ButtonText);
        if (preview is not null)
        {
            ImGui.Separator();
            ImGui.TextColored(PokemonBlue, $"Type matchup / {preview.Badge}");
            ImGui.TextWrapped(selectedMove is null ? preview.Description : BattleLayout.MovePpLabel(selectedMove) + "\n" + preview.Description);
        }
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private void DrawLearningMoveDetails(string name)
    {
        if (battleMoveHelp.TryGetValue(name, out var move)) DrawMoveDetails(move);
        else ImGui.TextDisabled("Move details unavailable.");
    }

    private static void DrawMoveDetails(MoveDetailsDto move, int? currentPower = null, bool showName = false)
    {
        var heading = $"{move.Type} / {move.Category}";
        ImGui.TextColored(TypeColor(move.Type), showName ? $"{move.Name} / {heading}" : heading);
        var power = currentPower ?? move.Power;
        var accuracy = move.Accuracy is int percent ? $"{percent}%" : "Always";
        ImGui.TextWrapped($"Power {(power > 0 ? power.ToString() : "N/A")} / Accuracy {accuracy} / PP {move.Pp}");
        if (currentPower is not null)
            ImGui.TextWrapped(move.Name.Equals("Return", StringComparison.OrdinalIgnoreCase)
                ? "Power rises with friendship, up to 102. EV-reset berries also raise friendship."
                : "Power falls as friendship rises, from a maximum of 102.");
        if (!string.IsNullOrWhiteSpace(move.Description)) ImGui.TextWrapped(move.Description);
    }

    private static void DrawBattleGuide()
    {
        if (!ImGui.CollapsingHeader("Battle conditions & field guide")) return;
        ImGui.TextWrapped("Burn: damage each turn and usually halves physical damage. Paralysis: halves Speed and sometimes prevents a move. Poison: damage each turn. Toxic poison: damage increases each turn. Sleep: prevents most actions until waking. Freeze: prevents most actions until thawing.");
        ImGui.TextWrapped("Confusion can cause self-damage. Flinch prevents a move that turn. Stat changes last until switching, usually from -6 to +6 stages. Switching clears most temporary effects, but not major status conditions.");
        ImGui.TextWrapped("Rain boosts Water and weakens Fire; sun does the reverse. Sandstorm damages most non-Rock/Ground/Steel Pokémon. Snow boosts Ice-type Defense. Weather normally lasts five turns; abilities and held items can change this.");
        ImGui.TextWrapped("Reflect reduces physical damage; Light Screen reduces special damage. Hazards trigger on switching. Tailwind doubles your side's Speed. Trick Room makes slower Pokémon act first within each priority bracket. Terrain affects grounded Pokémon and can boost moves or protect against status.");
        ImGui.TextDisabled("Hover moves for Showdown descriptions. Your Pokémon's abilities are explained in their build summary.");
    }

    private void DrawAbilityTooltip(PokemonDto pokemon)
    {
        if (!ImGui.IsItemHovered() || battleHelp is null) return;
        var ability = battleHelp.Abilities.FirstOrDefault(entry => entry.Name.Equals(pokemon.Ability, StringComparison.OrdinalIgnoreCase));
        if (ability is not null) ImGui.SetTooltip($"{ability.Name}: {ability.Description}");
    }

    private void ResetCollectionView()
    {
        pendingClipboard = null;
        battleHelp = null; battleMoveHelp.Clear();
    }
}
