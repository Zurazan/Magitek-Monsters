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
    private static readonly StorageFilter DefaultTradeFilter = new("", 0, null, 0, 0, false, false, false);

    private sealed class TradePicker(bool wonder, string? tradeId, PokemonDto? pokemon, int box)
    {
        public bool Wonder { get; } = wonder;
        public string? TradeId { get; } = tradeId;
        public PokemonDto? Pokemon = pokemon;
        public int Box = box;
        public StorageFilter Filter = DefaultTradeFilter;
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
        var box = pokemon is { PartySlot: null, BoxNumber: > 0 } ? pokemon.BoxNumber : Math.Max(1, storageBox);
        tradePicker = new(wonder, active?.Id, pokemon, box);
        requestTradePicker = true;
    }

    private void RefreshTradePicker(TradePicker picker, TrainerSnapshotDto owner)
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
        picker.Groups = filter == DefaultTradeFilter ? storageBoxContents
            : StorageBrowser.Filter(owner.Storage, storageDuplicates, filter)
                .GroupBy(pokemon => pokemon.BoxNumber).ToDictionary(group => group.Key, group => group.ToList());
        picker.Boxes = picker.Groups.Keys.Order().ToArray();
    }

    private void DrawTradingPicker()
    {
        const string title = "Choose trade Pokémon";
        if (requestTradePicker) { ImGui.OpenPopup(title); requestTradePicker = false; }
        if (!ImGui.IsPopupOpen(title)) { tradePicker = null; return; }
        var scale = CompanionScale;
        var work = ImGui.GetWindowViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(970 * scale, work.X * .94f), Math.Min(690 * scale, work.Y * .94f)), ImGuiCond.Always);
        var open = true;
        if (!BeginCenteredModal(title, ref open, FixedBattlePanel | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize)) return;
        if (tradePicker is not { } picker || trainer is not { } owner)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var search = picker.Filter.Search;
        var clearWidth = ImGui.CalcTextSize("Clear").X + 2 * ImGui.GetStyle().FramePadding.X;
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - clearWidth - ImGui.GetStyle().ItemSpacing.X));
        var searchChanged = ImGui.InputTextWithHint("##TradeSearch", "Search PC Storage...", ref search, 80);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Search by species, nickname, move or ability.");
        ImGui.SameLine();
        ImGui.BeginDisabled(search.Length == 0);
        if (DrawAccentSmallButton("Clear", CompanionMuted))
        {
            search = "";
            searchChanged = true;
        }
        ImGui.EndDisabled();
        if (searchChanged) SetTradePickerFilter(picker, picker.Filter with { Search = search });
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (DrawAccentSmallButton("All boxes", CompanionAccent)) BrowseStorageBox(0, picker);
        if (picker.Wonder)
        {
            ImGui.SameLine();
            DrawTradePickerFilters(picker, work.X);
        }
        RefreshTradePicker(picker, owner);
        StorageSameLine(right, 180 * scale);
        DrawStorageBoxJump(picker.Box, picker);

        var footerHeight = 32 * scale + ImGui.GetTextLineHeightWithSpacing() + 3 * ImGui.GetStyle().ItemSpacing.Y + 4 * scale;
        if (ImGui.BeginChild("TradePickerBody", new Vector2(0, -footerHeight), false))
            DrawTradePickerBody(picker);
        ImGui.EndChild();
        DrawTradePickerFooter(picker);
        ImGui.EndPopup();
    }

    private void SetTradePickerFilter(TradePicker picker, StorageFilter filter)
    {
        picker.Filter = filter;
        BrowseStorageBox(0, picker);
    }

    private void DrawTradePickerFilters(TradePicker picker, float workWidth)
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
            ImGui.OpenPopup("WonderTradeFilters");
        ImGui.SetNextWindowSize(new Vector2(Math.Min(330 * CompanionScale, workWidth * .9f), 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("WonderTradeFilters")) return;
        if (DrawStorageFilterOptions(ref favorites, ref shiny, ref duplicates, ref type, ref generation))
            SetTradePickerFilter(picker, filter with { Favorites = favorites, Shiny = shiny, Duplicates = duplicates,
                Type = type > 0 ? ChallengeTypes[type - 1] : null, Generation = generation });
        if (DrawAccentSmallButton("Reset filters", CompanionMuted))
            SetTradePickerFilter(picker, DefaultTradeFilter with { Search = filter.Search });
        ImGui.EndPopup();
    }

    private void DrawTradePickerBody(TradePicker picker)
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
        if (ImGui.BeginChild("TradePickerPreview", new Vector2(layout.DetailWidth, height), true))
        {
            if (picker.Pokemon is { } pokemon)
            {
                ImGui.TextWrapped(TradePickerLocation(pokemon));
                DrawStoragePokemonDetails(pokemon);
                ImGui.TextWrapped($"Held item: {TradingItemName(pokemon.HeldItem)}");
            }
            else ImGui.TextWrapped("Select a Pokémon to preview it.");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private string TradePickerLocation(PokemonDto pokemon) => pokemon.PartySlot is int slot
        ? $"Party slot {slot}" : $"{BoxLabel(pokemon.BoxNumber)} / Slot {pokemon.BoxSlot}";

    private string? TradePickerRestriction(TradePicker picker)
    {
        if (picker.Wonder ? trading?.Wonder is not null : picker.TradeId != trading?.Active?.Id)
            return "Trading changed. Close and reopen the picker.";
        return TradeOfferRestriction(picker.Pokemon);
    }

    private void DrawTradePickerFooter(TradePicker picker)
    {
        var reason = TradePickerRestriction(picker);
        var pokemon = picker.Pokemon;
        var text = reason ?? (pokemon!.Nickname.Length > 0 ? pokemon.Nickname : pokemon.DisplaySpecies);
        DrawClippedText(text, ImGui.GetContentRegionAvail().X, reason is null ? CompanionAccent : pokemon is null ? CompanionMuted : PokemonOrange);
        var label = picker.TradeId is null ? "Choose Pokémon" : "Update offer";
        var size = new Vector2(0, 32 * CompanionScale);
        if (DrawAccentButton("Cancel", size, CompanionMuted)) ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        ImGui.BeginDisabled(reason is not null);
        if (DrawAccentButton(label, new Vector2(-1, size.Y), picker.Wonder ? PokemonOrange : CompanionAccent)
            && ApplyTradeChoice(picker))
            ImGui.CloseCurrentPopup();
        ImGui.EndDisabled();
    }

    private bool ApplyTradeChoice(TradePicker picker)
    {
        if (picker.Pokemon is not { } pokemon || TradePickerRestriction(picker) is not null) return false;
        if (picker.Wonder) wonderPokemonId = pokemon.Id;
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
