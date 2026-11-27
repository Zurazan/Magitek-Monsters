using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly (string Label, StorageSort Order)[] StorageSortOptions =
    [
        ("Species A to Z", StorageSort.Species), ("Type", StorageSort.Type),
        ("Level: highest first", StorageSort.LevelHigh), ("Level: lowest first", StorageSort.LevelLow),
        ("Generation", StorageSort.Generation), ("Newest first", StorageSort.Newest),
        ("IV total: highest first", StorageSort.IvTotal), ("Perfect IVs: most first", StorageSort.PerfectIvs)
    ];

    private void DrawStorageBrowser()
    {
        if (trainer is null) return;
        ObserveStorageOwner();
        DrawSectionHeader("PC Storage", $"{trainer.Storage.Count:N0} boxed / Party {trainer.Party.Count}/6");
        if (CanReleaseDuringWild)
        {
            ImGui.TextWrapped("You can release a boxed Pokémon while your encounter is active.");
            if (DrawAccentSmallButton("Return to encounter", CompanionAccent)) mainSection = 0;
        }
        DrawStorageToolbar();
        DrawStorageTools(StorageView.Matches);
        StorageView.DragEnabled = !CollectionLocked && !storageSelecting && pendingStorageBatch is null;
        StorageView.DrawBody(DrawStorageInspectorHeader, DrawStorageInspectorActions);
        DrawReleaseConfirmation();
        DrawStorageReleasePopup();
        DrawStorageComparison();
        DrawStorageSearchPopups();
    }

    private void DrawStorageToolbar()
    {
        var scale = CompanionScale;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8 * scale, Math.Max(2 * scale, (32 * scale - ImGui.GetTextLineHeight()) / 2)));
        StorageView.DrawSearch();
        DrawStorageSearchToolbar();
        StorageView.DrawAllBoxes();
        StorageSameLine(right, 180 * scale);
        StorageView.DrawBoxJump();
        StorageView.DrawFilters(right, ImGui.GetWindowViewport().WorkSize.X);
        DrawStorageSort(right);
        var selectLabel = storageSelecting ? "Done selecting" : "Multi-select";
        StorageSameLine(right, ImGui.CalcTextSize(selectLabel).X + 16 * scale);
        ImGui.BeginDisabled(busy);
        if (DrawStorageToolbarButton(selectLabel, storageSelecting ? CompanionAccent : CompanionMuted))
        { storageSelecting = !storageSelecting; if (!storageSelecting) storageSelection.Clear(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click sprites to inspect. Shift-click to start selecting several Pokémon. Selections stay when switching boxes.");
        ImGui.EndDisabled();
        if (StorageView.Box > 0)
        {
            StorageSameLine(right, ImGui.CalcTextSize("Rename box").X + 16 * scale);
            if (DrawStorageToolbarButton("Rename box", CompanionMuted))
            {
                boxName = trainer!.Collection.BoxNames.GetValueOrDefault(StorageView.Box.ToString(), string.Empty);
                ImGui.OpenPopup("StorageBoxName");
            }
            if (ImGui.BeginPopup("StorageBoxName"))
            {
                ImGui.InputTextWithHint("##BoxName", "Box name...", ref boxName, 96);
                ImGui.BeginDisabled(busy || string.IsNullOrWhiteSpace(boxName));
                if (DrawAccentSmallButton("Save name", CompanionAccent))
                {
                    var number = StorageView.Box; var name = boxName;
                    Run(() => UpdateCollection(plugin.Api.RenameBox(number, name), "Box renamed."), "Renaming box...");
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndDisabled(); ImGui.EndPopup();
            }
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
    }

    private static bool DrawStorageToolbarButton(string label, Vector4 accent) => DrawAccentButton(label, new Vector2(0, 32 * CompanionScale), accent);

    private void DrawStorageSort(float right)
    {
        const string label = "Sort box...";
        var box = StorageView.Box;
        StorageSameLine(right, ImGui.CalcTextSize(label).X + 16 * CompanionScale);
        ImGui.BeginDisabled(box == 0 || storageInventory?.Count(box) is not > 0 || CollectionLocked || pendingStorageBatch is not null);
        if (DrawStorageToolbarButton(label, CompanionAccent)) ImGui.OpenPopup("SortStorageBox");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(box == 0 ? "Open a box to sort it." : "Save a new slot order for every Pokémon in this box. Locked slots stay in place.");
        if (!ImGui.BeginPopup("SortStorageBox")) return;
        ImGui.TextDisabled("Saves this box's slot order");
        foreach (var (option, order) in StorageSortOptions)
            if (ImGui.Selectable(option)) SortStorageBox(box, order);
        ImGui.EndPopup();
    }

    private void SelectStoragePokemon(PokemonDto pokemon)
    {
        if (pokemon.PartySlot is not null)
        {
            if (StorageView.Box == 0) OpenPokemonSummary(pokemon.Id);
            return;
        }
        if (busy || (!storageSelecting && !ImGui.GetIO().KeyShift) || pokemon.FusionHostId.Length > 0 || pokemon.TradeLockId is not null)
            return;
        storageSelecting = true;
        if (!storageSelection.Remove(pokemon.Id)) StorageBrowser.Select(storageSelection, [pokemon]);
    }

    private void DrawStorageInspectorHeader(PokemonDto pokemon)
    {
        var scale = CompanionScale;
        var favouriteWidth = 28 * scale;
        var location = pokemon.PartySlot is int slot ? $"Party slot {slot}" : $"Box {pokemon.BoxNumber} / {pokemon.BoxSlot}";
        DrawClippedText(location, Math.Max(1, ImGui.GetContentRegionAvail().X - favouriteWidth - 6 * scale), CompanionMuted);
        ImGui.SameLine(0, 6 * scale);
        ImGui.BeginDisabled(CollectionLocked || pendingStorageBatch is not null || pokemon.FusionHostId.Length > 0 || pokemon.TradeLockId is not null);
        if (DrawAccentButton("★##StorageFavourite", new Vector2(favouriteWidth, 24 * scale), pokemon.Favorite ? PokemonYellow : CompanionMuted))
            TogglePokemonFavourite(pokemon);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(pokemon.Favorite ? "Unmark favourite" : "Mark favourite");
        DrawCollectionEditNotice();
    }

    private void DrawStorageInspectorActions(PokemonDto pokemon)
    {
        var scale = CompanionScale;
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.BeginDisabled(CollectionLocked || pendingStorageBatch is not null || pokemon.FusionHostId.Length > 0 || pokemon.TradeLockId is not null);
        ImGui.BeginDisabled(pokemon.PartySlot is null && trainer!.Party.Count >= 6);
        if (DrawAccentButton(pokemon.PartySlot is null ? "To party" : "To PC", new Vector2(-1, 30 * scale), CompanionAccent))
            Run(() => MovePokemon(pokemon.Id, pokemon.PartySlot is null ? "party" : "storage"), $"Moving {pokemon.Nickname}...");
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (pokemon.PartySlot is null)
        {
            ImGui.SetNextItemWidth(-1);
            DrawStorageMovePicker("##StorageMove", [pokemon]);
        }
        if (DrawAccentButton("Summary & moves", new Vector2(-1, 28 * scale), CompanionAccent))
            OpenPokemonSummary(pokemon.Id);
        DrawStoragePokemonActions(pokemon);
        if (pokemon.FusionHostId.Length > 0) ImGui.TextWrapped("Fusion partner / Unfuse the host to manage this Pokémon.");
        if (pokemon.TradeLockId is not null)
        {
            ImGui.TextColored(PokemonOrange, "Reserved for trading");
            if (DrawAccentSmallButton("Open Trading", CompanionAccent)) mainSection = 16;
        }
    }

    private void DrawStoragePokemonDetails(PokemonDto pokemon)
    {
        var scale = CompanionScale;
        var portrait = Math.Min(100 * scale, ImGui.GetContentRegionAvail().X);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - portrait) / 2));
        DrawPokemonImage(pokemon.Appearance, pokemon.Shiny, new Vector2(portrait));
        DrawGenderedPokemonText(pokemon.Nickname, pokemon.Gender, CompanionAccent, centered: true);
        DrawClippedText($"{(pokemon.Nickname == pokemon.DisplaySpecies ? "" : pokemon.DisplaySpecies + " / ")}Lv. {pokemon.Level}", ImGui.GetContentRegionAvail().X, CompanionMuted);
        ImGui.Spacing();
        DrawStarterTypeBadges(pokemon.Types.ToArray(), 60 * scale, scale);
        ImGui.TextWrapped($"Ability: {pokemon.Ability}");
        if (ImGui.IsItemHovered()) DrawAbilityTooltip(pokemon);
        ImGui.TextWrapped($"Nature: {(pokemon.MintNature.Length > 0 ? pokemon.MintNature + " (mint)" : pokemon.Nature)}");
        ImGui.TextColored(CompanionMuted, $"IV {StorageBrowser.IvTotal(pokemon)}/186 / {StorageBrowser.PerfectIvs(pokemon)} perfect");
        if (pokemon.Favorite || pokemon.Shiny) ImGui.TextColored(PokemonYellow, $"{(pokemon.Shiny ? "★ Shiny  " : "")}{(pokemon.Favorite ? "Favourite" : "")}");
    }

    private void DrawStoragePokemonActions(PokemonDto pokemon)
    {
        var compareLabel = storageComparePin == pokemon.Id ? "Unpin" : "Compare";
        var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2);
        var labelWidth = Math.Max(ImGui.CalcTextSize(compareLabel).X, ImGui.CalcTextSize("Release...").X);
        var stacked = width < labelWidth + ImGui.GetStyle().FramePadding.X * 2;
        var size = new Vector2(stacked ? -1 : width, 28 * CompanionScale);
        if (DrawAccentButton(compareLabel, size, CompanionAccent))
        {
            if (storageComparePin == pokemon.Id) storageComparePin = string.Empty;
            else if (storageComparePin.Length == 0) storageComparePin = pokemon.Id;
            else OpenStorageComparison(storageComparePin, pokemon.Id);
        }
        if (!stacked) ImGui.SameLine();
        ImGui.BeginDisabled(BoxedReleaseLocked || pendingStorageBatch is not null || pokemon.PartySlot is not null || StorageBrowser.ReleaseProtected(pokemon));
        if (DrawAccentButton("Release...", size, PokemonRed))
        { releasePokemonId = pokemon.Id; requestRelease = true; }
        ImGui.EndDisabled();
    }

    private void DrawStorageMovePicker(string id, List<PokemonDto> selected)
    {
        ImGui.BeginDisabled(CollectionLocked || pendingStorageBatch is not null || selected.Count == 0 || selected.Any(pokemon => pokemon.FusionHostId.Length > 0 || pokemon.TradeLockId is not null));
        if (ImGui.BeginCombo(id, "Move to box..."))
        {
            for (var number = 1; number <= StorageBrowser.BoxCount; number++)
            {
                var free = StorageBrowser.BoxCapacity - storageInventory!.Count(number);
                var incoming = selected.Count(pokemon => pokemon.BoxNumber != number);
                ImGui.BeginDisabled(incoming == 0 || incoming > free);
                if (ImGui.Selectable($"{BoxLabel(number)} / {free} free##Move{number}")) StartStorageBatch("box", selected, number);
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

}
