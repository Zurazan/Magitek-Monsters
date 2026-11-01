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
    private readonly Dictionary<string, string> nicknameInputs = [];
    private string challengeTrainerId = string.Empty;
    private string? pendingClipboard;
    private BattleHelpDto? battleHelp;
    private readonly ConcurrentDictionary<string, MoveDetailsDto> battleMoveHelp = new(StringComparer.OrdinalIgnoreCase);

    private bool CollectionLocked => busy || trainer?.ActiveEncounter is not null || battle?.Status == "active"
        || LeagueBusy || FrontierBusy || trainer?.RaidLocked == true || raidRoom?.Status is "lobby" or "active" || trainer?.ActiveTrainerBattle?.Status == "active"
        || pvpStatus?.State == "queued" || pvpStatus?.Battle?.Status == "active" || HasOutgoingInvitation;
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
        if (trainer is null) return;
        var scale = CompanionScale;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.Checkbox("Favourites", ref storageFavorites)) ShowStorageOverview();
        StorageSameLine(right, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Shiny only").X);
        if (ImGui.Checkbox("Shiny only", ref storageShiny)) ShowStorageOverview();
        StorageSameLine(right, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Duplicates only").X);
        if (ImGui.Checkbox("Duplicates only", ref storageDuplicatesOnly)) ShowStorageOverview();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Same appearance and shiny variant, including copies in your party.");
        StorageSameLine(right, 150 * scale);
        ImGui.SetNextItemWidth(Math.Min(150 * scale, ImGui.GetContentRegionAvail().X));
        var types = new[] { "Any type" }.Concat(ChallengeTypes).ToArray();
        if (ImGui.Combo("##StorageType", ref storageType, types, types.Length)) ShowStorageOverview();
        StorageSameLine(right, 150 * scale);
        ImGui.SetNextItemWidth(Math.Min(150 * scale, ImGui.GetContentRegionAvail().X));
        string[] generations = ["Any generation", "Gen 1", "Gen 2", "Gen 3", "Gen 4", "Gen 5", "Gen 6", "Gen 7", "Gen 8", "Gen 9"];
        if (ImGui.Combo("##StorageGen", ref storageGeneration, generations, generations.Length)) ShowStorageOverview();
        ImGui.SetNextItemWidth(Math.Min(240 * scale, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Sort").X - 8 * scale));
        string[] sorts = ["Box order", "Species", "Level: highest first", "Newest first", "IV total: highest first", "Perfect IVs: most first"];
        if (ImGui.Combo("Sort##StorageSort", ref storageSort, sorts, sorts.Length)) storageScrollToTop = true;
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

    private void DrawPokemonCollection(PokemonDto pokemon)
    {
        ImGui.TextColored(PokemonBlue, "IDENTITY & CAPTURE HISTORY");
        ImGui.BeginDisabled(CollectionLocked);
        if (DrawAccentSmallButton(pokemon.Favorite ? $"Unmark favourite##{pokemon.Id}" : $"Mark favourite##{pokemon.Id}", PokemonYellow))
            Run(() => UpdateCollection(plugin.Api.EditPokemon(pokemon.Id, favorite: !pokemon.Favorite), "Favourite updated."), "Updating favourite...");
        if (!nicknameInputs.TryGetValue(pokemon.Id, out var nickname)) nickname = pokemon.Nickname;
        ImGui.TextDisabled("Nickname");
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Save name").X - 24 * CompanionScale));
        ImGui.InputText($"##Nickname{pokemon.Id}", ref nickname, 48);
        nicknameInputs[pokemon.Id] = nickname;
        ImGui.SameLine();
        if (DrawAccentSmallButton($"Save name##{pokemon.Id}", PokemonGreen))
        {
            var name = nickname;
            Run(() => UpdateCollection(plugin.Api.EditPokemon(pokemon.Id, name), "Nickname updated."), "Saving nickname...");
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped("Nicknames: 1-12 visible characters. Favourites cannot be released.");
        var origin = pokemon.Origin;
        var acquired = DateTimeOffset.TryParse(pokemon.CreatedAt, out var when) ? when.LocalDateTime.ToString("g") : "Unknown date";
        ImGui.TextWrapped($"Obtained: {origin?.Method ?? pokemon.Source} | {acquired}");
        ImGui.TextWrapped($"Location: {origin?.Location ?? "Not recorded"} | Capture level: {origin?.Level?.ToString() ?? "Not recorded"}");
        ImGui.TextDisabled($"Ball: {pokemon.Pokeball}");
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton($"Copy Pokémon summary##{pokemon.Id}", PokemonBlue)) QueueTeamExport([pokemon.Id]);
        ImGui.EndDisabled();
        ImGui.Separator();
        if (pokemon.PartySlot is null)
        {
            ImGui.BeginDisabled(CollectionLocked);
            if (ImGui.BeginCombo($"Move to box##{pokemon.Id}", BoxLabel(pokemon.BoxNumber)))
            {
                for (var number = 1; number <= 100; number++)
                    if (ImGui.Selectable(BoxLabel(number), pokemon.BoxNumber == number))
                    {
                        var target = number;
                        Run(() => UpdateCollection(plugin.Api.MoveToBox(pokemon.Id, target), "Pokémon moved to box."), "Moving Pokémon...");
                    }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
        }
    }

    private List<PokemonDto> OnlineOwnedPokemon() => trainer is null ? [] : Services.PvpRoster.Owned(trainer.Party, trainer.Storage);
    private string[] SelectedOnlineTeam() => Services.PvpRoster.Selected(OnlineOwnedPokemon(), pvpTeamIds).Select(pokemon => pokemon.Id).ToArray();

    private readonly HashSet<string> friendlyTeamIds = [];
    private readonly List<PvpFormatDto> friendlyFormats = [];
    private string friendlyFormatId = "gen9nationaldex";
    private string[] FriendlyTeam() => Services.PvpRoster.Selected(OnlineOwnedPokemon(), friendlyTeamIds).Select(pokemon => pokemon.Id).ToArray();

    private async Task LoadBattleHelp()
    {
        if (battleHelp is not null) return;
        battleHelp = await InSession(plugin.Api.GetBattleHelp(disposeCancellation.Token));
        battleMoveHelp.Clear();
        foreach (var move in battleHelp.Moves) battleMoveHelp[move.Name] = move;
    }

    private void DrawBattleMoveTooltip(string name)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        var label = BattleLayout.MoveLabel(name);
        if (!battleMoveHelp.TryGetValue(label.Name, out var move)) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
        ImGui.TextColored(PokemonBlue, $"{move.Name} | {move.Type} | {move.Category}");
        var power = label.Power ?? move.Power;
        ImGui.Text($"Power: {(power > 0 ? power.ToString() : "N/A")} | Accuracy: {move.Accuracy?.ToString() ?? "Always"} | Base PP: {move.Pp}");
        if (label.Power is not null)
            ImGui.TextWrapped(label.Name == "Return"
                ? "Power rises with friendship, up to 102. EV-reset berries also raise friendship."
                : "Power falls as friendship rises, from a maximum of 102.");
        ImGui.TextWrapped(move.Description);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
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
        nicknameInputs.Clear(); pendingClipboard = null;
        battleHelp = null; battleMoveHelp.Clear();
    }
}
