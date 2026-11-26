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
    private static readonly StorageFilter DefaultStoragePickerFilter = new("", 0, null, 0, 0, false, false, false);

    private enum StoragePickerMode { DirectTrade, WonderTrade, Raid }

    private sealed class StoragePicker(StoragePickerMode mode, string? activityId, PokemonDto? pokemon, int box)
    {
        public StoragePickerMode Mode { get; } = mode;
        public string? ActivityId { get; } = activityId;
        public PokemonDto? Pokemon = pokemon;
        public int Box = box;
        public StorageFilter Filter = DefaultStoragePickerFilter;
        public bool OpenRequested = true;
        public bool ScrollToTop = true;
        public TrainerSnapshotDto? Source;
        public StorageFilter? AppliedFilter;
        public Dictionary<int, List<PokemonDto>> Groups = [];
        public int[] Boxes = [];
    }

    private void OpenTradePicker(bool wonder)
    {
        var active = wonder ? null : trading?.Active;
        var pokemon = active?.YourPokemon ?? TradingSelection(wonder);
        tradePicker = CreateStoragePicker(wonder ? StoragePickerMode.WonderTrade : StoragePickerMode.DirectTrade, active?.Id, pokemon);
    }

    private StoragePicker CreateStoragePicker(StoragePickerMode mode, string? activityId, PokemonDto? pokemon)
    {
        var box = pokemon is { PartySlot: null, BoxNumber: > 0 } ? pokemon.BoxNumber : Math.Max(1, storageBox);
        return new(mode, activityId, pokemon, box);
    }

    private void RefreshStoragePicker(StoragePicker picker, TrainerSnapshotDto owner)
    {
        ObserveStorageOwner();
        if (!ReferenceEquals(picker.Source, owner))
        {
            picker.Source = owner;
            if (picker.Pokemon is { } selected)
                picker.Pokemon = owner.Party.Concat(owner.Storage).FirstOrDefault(pokemon => pokemon.Id == selected.Id);
            picker.AppliedFilter = null;
        }
        var filter = picker.Filter;
        if (picker.AppliedFilter == filter) return;
        picker.AppliedFilter = filter;
        picker.Groups = filter == DefaultStoragePickerFilter ? storageBoxContents
            : StorageBrowser.Filter(owner.Storage, storageDuplicates, filter)
                .GroupBy(pokemon => pokemon.BoxNumber).ToDictionary(group => group.Key, group => group.ToList());
        picker.Boxes = picker.Groups.Keys.Order().ToArray();
    }

    private void DrawStoragePicker(ref StoragePicker? state, string title)
    {
        if (state is { OpenRequested: true }) { ImGui.OpenPopup(title); state.OpenRequested = false; }
        if (!ImGui.IsPopupOpen(title)) { state = null; return; }
        var scale = CompanionScale;
        var work = ImGui.GetWindowViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(970 * scale, work.X * .94f), Math.Min(690 * scale, work.Y * .94f)), ImGuiCond.Always);
        var open = true;
        if (!BeginCenteredModal(title, ref open, FixedBattlePanel | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize)) return;
        if (state is not { } picker || trainer is not { } owner)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        if (picker.Mode == StoragePickerMode.Raid)
            ImGui.TextWrapped("Choose one Pokémon. Its level syncs to the den.");
        var search = picker.Filter.Search;
        var clearWidth = ImGui.CalcTextSize("Clear").X + 2 * ImGui.GetStyle().FramePadding.X;
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - clearWidth - ImGui.GetStyle().ItemSpacing.X));
        var searchChanged = ImGui.InputTextWithHint("##StoragePickerSearch", "Search PC Storage...", ref search, 80);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Search by species, nickname, move or ability.");
        ImGui.SameLine();
        ImGui.BeginDisabled(search.Length == 0);
        if (DrawAccentSmallButton("Clear", CompanionMuted))
        {
            search = "";
            searchChanged = true;
        }
        ImGui.EndDisabled();
        if (searchChanged) SetStoragePickerFilter(picker, picker.Filter with { Search = search });
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (DrawAccentSmallButton("All boxes", CompanionAccent)) BrowseStorageBox(0, picker);
        if (picker.Mode is StoragePickerMode.WonderTrade or StoragePickerMode.Raid)
        {
            ImGui.SameLine();
            DrawStoragePickerFilters(picker, work.X);
        }
        RefreshStoragePicker(picker, owner);
        StorageSameLine(right, 180 * scale);
        DrawStorageBoxJump(picker.Box, picker);

        var footerHeight = 32 * scale + ImGui.GetTextLineHeightWithSpacing() + 3 * ImGui.GetStyle().ItemSpacing.Y + 4 * scale;
        var availableWidth = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize;
        var bodyFlags = StorageLayout.Measure(availableWidth, scale).SideBySide
            ? ImGuiWindowFlags.None : ImGuiWindowFlags.AlwaysVerticalScrollbar;
        if (ImGui.BeginChild("StoragePickerBody", new Vector2(0, -footerHeight), false, bodyFlags))
            DrawStoragePickerBody(picker);
        ImGui.EndChild();
        DrawStoragePickerFooter(picker);
        ImGui.EndPopup();
    }

    private void SetStoragePickerFilter(StoragePicker picker, StorageFilter filter)
    {
        picker.Filter = filter;
        BrowseStorageBox(0, picker);
    }

    private void DrawStoragePickerFilters(StoragePicker picker, float workWidth)
    {
        var filter = picker.Filter;
        var favorites = filter.Favorites;
        var shiny = filter.Shiny;
        var duplicates = filter.Duplicates;
        var type = Array.IndexOf(ChallengeTypes, filter.Type) + 1;
        var generation = filter.Generation;
        var count = (favorites ? 1 : 0) + (shiny ? 1 : 0) + (duplicates ? 1 : 0)
            + (type > 0 ? 1 : 0) + (generation > 0 ? 1 : 0);
        if (DrawAccentSmallButton(count > 0 ? $"Filters ({count})" : "Filters", count > 0 ? CompanionAccent : CompanionMuted))
            ImGui.OpenPopup("StoragePickerFilters");
        ImGui.SetNextWindowSize(new Vector2(Math.Min(330 * CompanionScale, workWidth * .9f), 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("StoragePickerFilters")) return;
        if (DrawStorageFilterOptions(ref favorites, ref shiny, ref duplicates, ref type, ref generation))
            SetStoragePickerFilter(picker, filter with { Favorites = favorites, Shiny = shiny, Duplicates = duplicates,
                Type = type > 0 ? ChallengeTypes[type - 1] : null, Generation = generation });
        if (DrawAccentSmallButton("Reset filters", CompanionMuted))
            SetStoragePickerFilter(picker, DefaultStoragePickerFilter with { Search = filter.Search });
        ImGui.EndPopup();
    }

    private void DrawStoragePickerBody(StoragePicker picker)
    {
        var scale = CompanionScale;
        var layout = StorageLayout.Measure(ImGui.GetContentRegionAvail().X, scale);
        if (picker.Box > 0 && picker.ScrollToTop) { ImGui.SetScrollY(0); picker.ScrollToTop = false; }
        if (!layout.PartyRail) DrawStoragePartyTray(picker);
        var cellHeight = Math.Min(layout.CellHeight, 62 * scale);
        var overview = picker.Box == 0;
        var height = overview ? Math.Max(150 * scale, ImGui.GetContentRegionAvail().Y - 6 * scale)
            : StorageLayout.GridHeight(cellHeight, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y, scale);
        if (layout.PartyRail)
        {
            DrawStoragePartyRail(layout.PartyWidth, height, picker);
            ImGui.SameLine(0, 10 * scale);
        }
        ImGui.BeginGroup();
        if (overview)
            DrawStorageThumbnails(picker.Groups, picker.Boxes, layout.BoxWidth, height, picker);
        else
        {
            var matches = picker.Groups.GetValueOrDefault(picker.Box) ?? [];
            DrawStorageBoxGrid(matches, layout.BoxWidth, height, cellHeight, picker);
            DrawStorageBoxShortcuts(layout.BoxWidth, matches.Count == 0, picker);
        }
        ImGui.EndGroup();
        if (layout.SideBySide) ImGui.SameLine(0, 10 * scale); else ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10 * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        var previewHeight = picker.Pokemon is null ? height : Math.Max(height, 330 * scale);
        if (ImGui.BeginChild("StoragePickerPreview", new Vector2(layout.DetailWidth, previewHeight), true))
        {
            if (picker.Pokemon is { } pokemon)
            {
                ImGui.TextWrapped(StoragePickerLocation(pokemon));
                DrawStoragePokemonDetails(pokemon);
                ImGui.TextWrapped($"Held item: {TradingItemName(pokemon.HeldItem)}");
            }
            else ImGui.TextWrapped("Select a Pokémon to preview it.");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private string StoragePickerLocation(PokemonDto pokemon) => pokemon.PartySlot is int slot
        ? $"Party slot {slot}" : $"{BoxLabel(pokemon.BoxNumber)} / Slot {pokemon.BoxSlot}";

    private string? StoragePickerPokemonRestriction(StoragePicker picker, PokemonDto? pokemon) =>
        picker.Mode == StoragePickerMode.Raid ? RaidPokemonRestriction(pokemon) : TradeOfferRestriction(pokemon);

    private string? StoragePickerRestriction(StoragePicker picker)
    {
        switch (picker.Mode)
        {
            case StoragePickerMode.Raid when picker.ActivityId != raidRoom?.Id || raidRoom is { Status: not "lobby" }:
                return "The den changed. Close and reopen the picker.";
            case StoragePickerMode.WonderTrade when trading?.Wonder is not null:
            case StoragePickerMode.DirectTrade when picker.ActivityId != trading?.Active?.Id:
                return "Trading changed. Close and reopen the picker.";
        }
        return StoragePickerPokemonRestriction(picker, picker.Pokemon);
    }

    private void DrawStoragePickerFooter(StoragePicker picker)
    {
        var reason = StoragePickerRestriction(picker);
        var pokemon = picker.Pokemon;
        var text = reason ?? (pokemon!.Nickname.Length > 0 ? pokemon.Nickname : pokemon.DisplaySpecies);
        DrawClippedText(text, ImGui.GetContentRegionAvail().X, reason is null ? CompanionAccent : pokemon is null ? CompanionMuted : PokemonOrange);
        var (label, accent) = picker.Mode switch
        {
            StoragePickerMode.Raid => ("Select for raid", PokemonBlue),
            StoragePickerMode.WonderTrade => ("Choose Pokémon", PokemonOrange),
            _ => (picker.ActivityId is null ? "Choose Pokémon" : "Update offer", CompanionAccent)
        };
        var size = new Vector2(0, 32 * CompanionScale);
        if (DrawAccentButton("Cancel", size, CompanionMuted)) ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        ImGui.BeginDisabled(reason is not null);
        if (DrawAccentButton(label, new Vector2(-1, size.Y), accent) && ApplyStoragePickerChoice(picker))
            ImGui.CloseCurrentPopup();
        ImGui.EndDisabled();
    }

    private bool ApplyStoragePickerChoice(StoragePicker picker)
    {
        if (picker.Pokemon is not { } pokemon || StoragePickerRestriction(picker) is not null) return false;
        if (picker.Mode == StoragePickerMode.Raid) ChooseRaidPokemon(pokemon);
        else if (picker.Mode == StoragePickerMode.WonderTrade) wonderPokemonId = pokemon.Id;
        else if (trading?.Active is { } active)
        {
            var tradeId = active.Id;
            var revision = active.Revision;
            var id = pokemon.Id;
            Run(() => SaveTrade(() => plugin.Api.OfferTrade(tradeId, id, revision)), "Updating trade offer...");
        }
        else tradePokemonId = pokemon.Id;
        return true;
    }
}
