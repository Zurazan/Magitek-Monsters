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
    private bool storageSelecting;
    private string storageOwner = string.Empty;
    private string storageComparePin = string.Empty;
    private StorageInventory? storageInventory;
    private PokemonStorageBrowser? storageBrowser;
    private StorageBatchAttempt? pendingStorageBatch;
    private StorageBatchAttempt? storageReleaseAttempt;
    private bool requestStorageRelease;
    private bool confirmStorageRelease;
    private sealed record StorageBatchAttempt(string Owner, string RequestId, string Action, string[] Ids, int Box, bool Favorite)
    {
        public StorageSlotPlacement? Placement { get; init; }
        public int[]? ExpectedSlots { get; init; }
        public StorageSortLayout? Layout { get; init; }
    }

    private PokemonStorageBrowser StorageView => storageBrowser ??= new(
        DrawPokemonIconAt, DrawStoragePokemonDetails, GetPokemonAlerts, () => CollectionEditRestriction, compact: false)
        { Marked = storageSelection, OnSelect = SelectStoragePokemon, OnPlace = PlaceStoragePokemon };

    private StorageInventory? ObserveStorageOwner()
    {
        if (trainer is not { } owner) return null;
        if (storageOwner != owner.User.Id)
        {
            storageOwner = owner.User.Id;
            storageBrowser = null;
            storageInventory = null;
            storageSelection.Clear(); storageComparePin = string.Empty;
            pendingStorageBatch = storageReleaseAttempt = null;
            storageSelecting = requestStorageRelease = requestStorageComparison = false;
            storageComparisonLeft = storageComparisonRight = string.Empty;
            boxName = string.Empty;
        }
        if (!ReferenceEquals(storageInventory?.Owner, owner))
        {
            if (storageInventory is { } inventory && ReferenceEquals(inventory.Owner.Storage, owner.Storage)) inventory.UpdateOwner(owner);
            else if (storageInventory?.TryUpdateStorage(owner) != true)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                storageInventory = new(owner);
                if (storageSelection.Count > 0) StorageBrowser.Synchronize(storageSelection, owner.Storage);
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (elapsed >= 4) Plugin.Log?.Debug("PC index: {PokemonCount} Pokemon in {ElapsedMs:F1} ms", owner.Storage.Count, elapsed);
            }
            else if (owner.RosterCache?.StorageDelta?.RemovedPokemonIds is { } removed)
            {
                storageSelection.ExceptWith(removed);
                if (StorageView.Selected is null) StorageView.SelectedId = null;
            }
            if (storageComparePin.Length > 0 && storageInventory.Find(storageComparePin) is null) storageComparePin = string.Empty;
        }
        StorageView.Bind(storageInventory!);
        return storageInventory;
    }

    private void InspectStoredPokemon(PokemonDto pokemon)
    {
        ObserveStorageOwner();
        StorageView.SetFilter(StorageSearches.Empty with { Box = Math.Clamp(pokemon.BoxNumber, 1, StorageBrowser.BoxCount) });
        StorageView.SelectedId = pokemon.Id;
    }

    private void DrawStorageTools(List<PokemonDto> filtered)
    {
        var scale = CompanionScale;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (storageComparePin.Length > 0)
        {
            var pinned = storageInventory?.Find(storageComparePin);
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
        if (StorageView.Box > 0 && DrawAccentSmallButton("Select box", CompanionAccent)) StorageBrowser.Select(storageSelection, filtered);
        var selectLabel = filtered.Count > StorageLimits.MaximumBatch ? "Select matches (max 100)" : "Select all matches";
        if (StorageView.Box == 0 && DrawAccentSmallButton(selectLabel, CompanionAccent)) StorageBrowser.Select(storageSelection, filtered);
        StorageSameLine(right, ImGui.CalcTextSize("Clear selection").X + 2 * ImGui.GetStyle().FramePadding.X);
        if (DrawAccentSmallButton("Clear selection", CompanionMuted)) storageSelection.Clear();
        ImGui.EndDisabled();
        var (selected, hidden) = GetStorageSelection(filtered);
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
        var room = 6 - trainer!.Party.Count;
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

    private void PlaceStoragePokemon(string id, StorageSlotPlacement placement)
    {
        if (CollectionLocked || pendingStorageBatch is not null || trainer is not { } current) return;
        var attempt = new StorageBatchAttempt(current.User.Id, Guid.NewGuid().ToString(), "place", [id], placement.ToBox, false)
            { Placement = placement };
        Run(() => ExecuteStorageBatch(attempt), placement.OccupantId is null ? "Moving Pokémon..." : "Swapping Pokémon...");
    }

    private void SortStorageBox(int box, StorageSort order)
    {
        if (CollectionLocked || pendingStorageBatch is not null || trainer is not { } current) return;
        var contents = box == 0 ? current.Storage : storageInventory?.Boxes.GetValueOrDefault(box);
        if (contents is not { Count: > 0 }) return;
        var ordered = StorageBrowser.Sort(contents, order).ToArray();
        var requestId = Guid.NewGuid().ToString();
        StorageBatchAttempt attempt;
        if (box == 0)
        {
            var occupants = ordered.Select(pokemon => (Position: StoragePositions.Position(pokemon.BoxNumber, pokemon.BoxSlot), Pokemon: Guid.Parse(pokemon.Id))).ToArray();
            var layout = new StorageSortLayout(StoragePositions.Fingerprint(occupants), occupants.Select(occupant => occupant.Position).ToArray());
            attempt = new(current.User.Id, requestId, "sort-all", [], box, false) { Layout = layout };
        }
        else
        {
            attempt = new(current.User.Id, requestId, "sort", ordered.Select(pokemon => pokemon.Id).ToArray(), box, false)
                { ExpectedSlots = ordered.Select(pokemon => pokemon.BoxSlot).ToArray() };
        }
        Run(() => ExecuteStorageBatch(attempt), box == 0 ? "Sorting all PC boxes..." : $"Sorting {BoxLabel(box)}...");
    }

    private async Task ExecuteStorageBatch(StorageBatchAttempt attempt)
    {
        if (trainer?.User.Id != attempt.Owner) return;
        pendingStorageBatch = attempt;
        try
        {
            var request = attempt switch
            {
                { Placement: { } placement } => plugin.Api.PlaceStoragePokemon(attempt.RequestId, attempt.Ids[0], placement, disposeCancellation.Token),
                { Layout: { } layout } => plugin.Api.SortAllStorage(attempt.RequestId, layout, disposeCancellation.Token),
                { ExpectedSlots: { } slots } => plugin.Api.SortStorageBox(attempt.RequestId, attempt.Box, attempt.Ids, slots, disposeCancellation.Token),
                _ => plugin.Api.StorageBatch(attempt.RequestId, attempt.Action, attempt.Ids, attempt.Box, attempt.Favorite, disposeCancellation.Token)
            };
            await InSession(UpdateCollection(request, "PC storage updated."));
        }
        catch (ApiException exception) when (exception.Code == "mutation_committed")
        {
            pendingStorageBatch = null;
            if (!await InSession(RecoverAction(true))) return;
        }
        catch (ApiException exception) when (exception.Code == "storage_layout_unavailable")
        {
            pendingStorageBatch = null;
            SetStatus(exception.Message, true);
            return;
        }
        catch (ApiException exception) when (exception.StatusCode is 400 or 403 or 404 or 409)
        {
            pendingStorageBatch = null;
            refreshState.Trainer();
            throw;
        }
        if (trainer?.User.Id != attempt.Owner) return;
        pendingStorageBatch = null;
        if (attempt.Placement is { } placed)
        {
            var toParty = placed.ToBox == StorageLimits.PartyLocation;
            if (toParty || StorageView.Box == placed.ToBox) StorageView.SelectedId = attempt.Ids[0];
            var location = toParty ? "Party" : BoxLabel(placed.ToBox);
            SetStatus(placed.OccupantId is null ? $"Moved to {location} / slot {placed.ToSlot}." : "Pokémon swapped.");
            return;
        }
        if (attempt.ExpectedSlots is not null || attempt.Layout is not null)
        {
            SetStatus(attempt.Box == 0 ? "Sorted all PC boxes." : $"Sorted {BoxLabel(attempt.Box)}.");
            return;
        }
        if (attempt.Action is "party" or "release") storageSelection.ExceptWith(attempt.Ids);
        var message = attempt.Action switch { "party" => "added to your party", "box" => $"moved to {BoxLabel(attempt.Box)}", "release" => "released", _ => attempt.Favorite ? "marked as favourites" : "unmarked as favourites" };
        SetStatus($"{attempt.Ids.Length} Pokémon {message}.");
    }

    private void DrawStorageReleasePopup()
    {
        if (requestStorageRelease) { ImGui.OpenPopup("Release selected Pokémon?"); requestStorageRelease = false; }
        var scale = CompanionScale;
        if (!BeginConfirmationModal("Release selected Pokémon?")) return;
        var attempt = storageReleaseAttempt;
        if (attempt is null || trainer?.User.Id != attempt.Owner) { ImGui.CloseCurrentPopup(); EndConfirmationModal(); return; }
        var selected = trainer.Storage.Where(pokemon => attempt.Ids.Contains(pokemon.Id)).ToList();
        var valid = selected.Count == attempt.Ids.Length && selected.All(pokemon => !StorageBrowser.ReleaseProtected(pokemon));
        ImGui.TextColored(PokemonRed, $"Permanently release {attempt.Ids.Length} Pokémon?");
        ImGui.TextWrapped("This cannot be undone. Held items return to your bag. Pokédex registrations stay recorded.");
        if (selected.Any(pokemon => pokemon.Shiny)) ImGui.TextColored(PokemonYellow, $"Includes {selected.Count(pokemon => pokemon.Shiny)} shiny Pokémon.");
        ImGui.BeginChild("StorageReleaseList", new Vector2(0, Math.Min(190 * scale, ImGui.GetWindowViewport().WorkSize.Y * 0.3f)), true);
        foreach (var pokemon in selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, pokemon.Shiny ? PokemonYellow : CompanionMuted);
            ImGui.TextWrapped($"{(pokemon.Shiny ? "★ " : "")}{pokemon.Nickname} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level} / Box {pokemon.BoxNumber}");
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();
        ImGui.BeginGroup();
        ImGui.Checkbox("##ConfirmStorageRelease", ref confirmStorageRelease);
        ImGui.SameLine(); ImGui.TextWrapped("I understand this is permanent");
        ImGui.EndGroup();
        if (!valid) ImGui.TextWrapped("The selection changed or contains protected Pokémon. Close this window and review it.");
        if (DrawConfirmationActions($"Release {attempt.Ids.Length} Pokémon", PokemonRed,
            CollectionLocked || !valid || !confirmStorageRelease || pendingStorageBatch is not null, "Keep Pokémon"))
        { ImGui.CloseCurrentPopup(); Run(() => ExecuteStorageBatch(attempt), "Releasing selected Pokémon..."); }
        EndConfirmationModal();
    }
}
