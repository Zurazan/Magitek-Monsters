using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly HashSet<string> storageSelection = [];
    private Dictionary<string, int> storageDuplicates = new(StringComparer.OrdinalIgnoreCase);
    private bool storageSelecting;
    private bool storageDuplicatesOnly;
    private string storageOwner = string.Empty;
    private string storageComparePin = string.Empty;
    private TrainerSnapshotDto? storageIndexedTrainer;
    private TrainerSnapshotDto? storageFilteredTrainer;
    private StorageFilter? storageCachedFilter;
    private List<PokemonDto> storageFilteredPokemon = [];
    private StorageBatchAttempt? pendingStorageBatch;
    private StorageBatchAttempt? storageReleaseAttempt;
    private bool requestStorageRelease;
    private bool confirmStorageRelease;
    private sealed record StorageBatchAttempt(string Owner, string RequestId, string Action, string[] Ids, int Box, bool Favorite);

    private void ObserveStorageOwner()
    {
        if (trainer is null) return;
        if (storageOwner != trainer.User.Id)
        {
            storageOwner = trainer.User.Id;
            storageIndexedTrainer = storageFilteredTrainer = null;
            storageCachedFilter = null; storageFilteredPokemon = [];
            storageSelection.Clear(); storageComparePin = string.Empty;
            pendingStorageBatch = storageReleaseAttempt = null;
            storageSelecting = requestStorageRelease = requestStorageComparison = false;
            storageComparisonLeft = storageComparisonRight = string.Empty;
            storageBox = 1; storageSearch = boxName = string.Empty;
            storageType = storageGeneration = storageSort = 0;
            storageFavorites = storageShiny = storageDuplicatesOnly = false;
            selectedStoragePokemon = null;
            storageScrollToTop = true; storageShowEmptyBoxes = false;
            requestStorageSummary = false; storageSummaryPokemon = string.Empty;
        }
        if (ReferenceEquals(storageIndexedTrainer, trainer)) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        storageIndexedTrainer = trainer;
        StorageBrowser.Synchronize(storageSelection, trainer.Storage);
        if (!trainer.Party.Concat(trainer.Storage).Any(pokemon => pokemon.Id == storageComparePin)) storageComparePin = string.Empty;
        storageDuplicates = StorageBrowser.Duplicates(trainer.Party.Concat(trainer.Storage));
        storageBoxContents = trainer.Storage.GroupBy(pokemon => pokemon.BoxNumber)
            .ToDictionary(group => group.Key, group => group.OrderBy(pokemon => pokemon.BoxSlot).ToList());
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= 4) Plugin.Log.Debug("PC index: {PokemonCount} Pokemon in {ElapsedMs:F1} ms", trainer.Storage.Count, elapsed);
    }

    private void DrawStorageTools(List<PokemonDto> filtered)
    {
        var scale = CompanionScale;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (storageComparePin.Length > 0)
        {
            var pinned = trainer!.Party.Concat(trainer.Storage).FirstOrDefault(pokemon => pokemon.Id == storageComparePin);
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionAccent);
            ImGui.TextWrapped($"Pinned: {pinned?.Nickname ?? "Pokémon"} / Choose another to compare");
            ImGui.PopStyleColor();
            StorageSameLine(right, ImGui.CalcTextSize("Unpin").X + 2 * ImGui.GetStyle().FramePadding.X);
            if (ImGui.SmallButton("Unpin")) storageComparePin = string.Empty;
        }
        if (!busy && pendingStorageBatch is { } pending)
        {
            ImGui.TextWrapped($"A PC action did not return a complete response: {pending.Action} / {pending.Ids.Length} Pokémon.");
            ImGui.BeginDisabled(CollectionLocked);
            if (DrawAccentSmallButton("Retry same action", CompanionAccent)) Run(() => ExecuteStorageBatch(pending), "Retrying the PC action...");
            ImGui.EndDisabled(); StorageSameLine(right, ImGui.CalcTextSize("Refresh PC").X + 2 * ImGui.GetStyle().FramePadding.X);
            if (DrawAccentSmallButton("Refresh PC", CompanionMuted)) Run(async () => { await InSession(RefreshTrainer()); pendingStorageBatch = null; }, "Refreshing PC storage...");
        }
        if (!storageSelecting) return;
        ImGui.BeginDisabled(busy);
        if (storageBox > 0 && DrawAccentSmallButton("Select box", CompanionAccent)) StorageBrowser.Select(storageSelection, filtered);
        var selectLabel = filtered.Count > StorageLimits.MaximumBatch ? "Select matches (max 100)" : "Select all matches";
        if (storageBox == 0 && DrawAccentSmallButton(selectLabel, CompanionAccent)) StorageBrowser.Select(storageSelection, filtered);
        StorageSameLine(right, ImGui.CalcTextSize("Clear selection").X + 2 * ImGui.GetStyle().FramePadding.X);
        if (DrawAccentSmallButton("Clear selection", CompanionMuted)) storageSelection.Clear();
        ImGui.EndDisabled();
        var selected = StorageBrowser.Selected(trainer!.Storage, storageSelection);
        var visibleIds = filtered.Select(pokemon => pokemon.Id).ToHashSet();
        var hidden = selected.Count(pokemon => !visibleIds.Contains(pokemon.Id));
        var selectionLabel = $"{selected.Count}/{StorageLimits.MaximumBatch} selected{(hidden > 0 ? $" / {hidden} in other boxes or filters" : "")}";
        StorageSameLine(right, ImGui.CalcTextSize(selectionLabel).X);
        ImGui.PushStyleColor(ImGuiCol.Text, CompanionAccent);
        ImGui.TextWrapped(selectionLabel);
        ImGui.PopStyleColor();
        if (selected.Count == 0) return;
        ImGui.BeginDisabled(busy || selected.Count != 2);
        if (DrawAccentSmallButton("Compare selected", CompanionAccent) && selected.Count == 2) OpenStorageComparison(selected[0].Id, selected[1].Id);
        ImGui.EndDisabled();
        StorageSameLine(right, 190 * scale);
        ImGui.SetNextItemWidth(Math.Min(190 * scale, ImGui.GetContentRegionAvail().X));
        DrawStorageMovePicker("##StorageBatchMove", selected);
        StorageSameLine(right, ImGui.CalcTextSize("More actions").X + 2 * ImGui.GetStyle().FramePadding.X);
        if (DrawAccentSmallButton("More actions", CompanionMuted)) ImGui.OpenPopup("StorageBatchActions");
        if (!ImGui.BeginPopup("StorageBatchActions")) return;
        ImGui.BeginDisabled(CollectionLocked || pendingStorageBatch is not null);
        var room = 6 - trainer.Party.Count;
        ImGui.BeginDisabled(selected.Count > room);
        if (DrawAccentSmallButton("To party", CompanionAccent)) { StartStorageBatch("party", selected); ImGui.CloseCurrentPopup(); }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip($"{room} party slots free. Every selected Pokémon must fit.");
        if (DrawAccentSmallButton("Favourite", PokemonYellow)) { StartStorageBatch("favorite", selected, favorite: true); ImGui.CloseCurrentPopup(); }
        if (DrawAccentSmallButton("Unmark favourites", CompanionMuted)) { StartStorageBatch("favorite", selected, favorite: false); ImGui.CloseCurrentPopup(); }
        var protectedCount = selected.Count(StorageBrowser.ReleaseProtected);
        ImGui.BeginDisabled(protectedCount > 0);
        if (DrawAccentSmallButton($"Release {selected.Count} Pokémon...", PokemonRed))
        {
            storageReleaseAttempt = NewStorageAttempt("release", selected);
            confirmStorageRelease = false; requestStorageRelease = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled(); ImGui.EndDisabled();
        if (protectedCount > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, PokemonYellow);
            ImGui.TextWrapped($"{protectedCount} protected: favourites or fused Pokémon. Release is unavailable.");
            ImGui.PopStyleColor();
            ImGui.BeginDisabled(busy);
            if (DrawAccentSmallButton("Deselect protected", CompanionMuted)) storageSelection.ExceptWith(selected.Where(StorageBrowser.ReleaseProtected).Select(pokemon => pokemon.Id));
            ImGui.EndDisabled();
        }
        ImGui.EndPopup();
    }

    private static void StorageSameLine(float right, float nextWidth)
    {
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextWidth <= right) ImGui.SameLine();
    }

    private StorageBatchAttempt NewStorageAttempt(string action, IEnumerable<PokemonDto> selected, int box = 1, bool favorite = true) =>
        new(trainer!.User.Id, Guid.NewGuid().ToString(), action, selected.Select(pokemon => pokemon.Id).ToArray(), box, favorite);

    private void StartStorageBatch(string action, List<PokemonDto> selected, int box = 1, bool favorite = true)
    {
        var attempt = NewStorageAttempt(action, selected, box, favorite);
        Run(() => ExecuteStorageBatch(attempt), $"Updating {selected.Count} stored Pokémon...");
    }

    private async Task ExecuteStorageBatch(StorageBatchAttempt attempt)
    {
        if (trainer?.User.Id != attempt.Owner) return;
        pendingStorageBatch = attempt;
        try
        {
            await InSession(UpdateCollection(plugin.Api.StorageBatch(attempt.RequestId, attempt.Action, attempt.Ids, attempt.Box, attempt.Favorite, disposeCancellation.Token), "PC storage updated."));
        }
        catch (ApiException exception) when (exception.Code == "mutation_committed")
        {
            pendingStorageBatch = null;
            if (!await InSession(RecoverAction(true))) return;
        }
        catch (ApiException exception) when (exception.StatusCode is 400 or 403 or 404 or 409)
        {
            pendingStorageBatch = null;
            refreshState.Trainer();
            throw;
        }
        if (trainer?.User.Id != attempt.Owner) return;
        pendingStorageBatch = null;
        if (attempt.Action is "party" or "release") storageSelection.ExceptWith(attempt.Ids);
        var message = attempt.Action switch { "party" => "added to your party", "box" => $"moved to {BoxLabel(attempt.Box)}", "release" => "released", _ => attempt.Favorite ? "marked as favourites" : "unmarked as favourites" };
        SetStatus($"{attempt.Ids.Length} Pokémon {message}.");
    }

    private void DrawStorageReleasePopup()
    {
        if (requestStorageRelease) { ImGui.OpenPopup("Release selected Pokémon?"); requestStorageRelease = false; }
        var scale = CompanionScale;
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(520 * scale, work.X * 0.92f), Math.Min(490 * scale, work.Y * 0.85f)), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Release selected Pokémon?", ImGuiWindowFlags.None)) return;
        var attempt = storageReleaseAttempt;
        if (attempt is null || trainer?.User.Id != attempt.Owner) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        var selected = trainer.Storage.Where(pokemon => attempt.Ids.Contains(pokemon.Id)).ToList();
        var valid = selected.Count == attempt.Ids.Length && selected.All(pokemon => !StorageBrowser.ReleaseProtected(pokemon));
        ImGui.TextColored(PokemonRed, $"Permanently release {attempt.Ids.Length} Pokémon?");
        ImGui.TextWrapped("This cannot be undone. Held items return to your bag. Pokédex registrations stay recorded.");
        if (selected.Any(pokemon => pokemon.Shiny)) ImGui.TextColored(PokemonYellow, $"Includes {selected.Count(pokemon => pokemon.Shiny)} shiny Pokémon.");
        ImGui.BeginChild("StorageReleaseList", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - 130 * scale)), true);
        foreach (var pokemon in selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, pokemon.Shiny ? PokemonYellow : CompanionMuted);
            ImGui.TextWrapped($"{(pokemon.Shiny ? "★ " : "")}{pokemon.Nickname} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level} / Box {pokemon.BoxNumber}");
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();
        ImGui.Checkbox("I understand this is permanent", ref confirmStorageRelease);
        if (!valid) ImGui.TextWrapped("The selection changed or contains protected Pokémon. Close this window and review it.");
        ImGui.BeginDisabled(CollectionLocked || !valid || !confirmStorageRelease || pendingStorageBatch is not null);
        if (DrawAccentButton($"Release {attempt.Ids.Length} Pokémon", new Vector2(-1, 30 * scale), PokemonRed))
        { ImGui.CloseCurrentPopup(); Run(() => ExecuteStorageBatch(attempt), "Releasing selected Pokémon..."); }
        ImGui.EndDisabled();
        if (ImGui.Button("Keep Pokémon")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
