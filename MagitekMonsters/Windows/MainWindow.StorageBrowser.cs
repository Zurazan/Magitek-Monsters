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
    private const float StorageAlertFontScale = 1.4f;
    private static readonly Vector4 StorageGrass = new(0.15f, 0.24f, 0.20f, 1);
    private static readonly Vector4 StorageSlot = new(0.20f, 0.31f, 0.26f, 1);
    private Dictionary<int, List<PokemonDto>> storageBoxContents = [];
    private bool storageScrollToTop;
    private bool storageShowEmptyBoxes;

    private void DrawStorageBrowser()
    {
        if (trainer is null) return;
        ObserveStorageOwner();
        DrawSectionHeader("PC Storage", $"{trainer.Storage.Count:N0} boxed / Party {trainer.Party.Count}/6");
        DrawStorageToolbar();
        var filtered = FilterStorage();
        DrawStorageTools(filtered);
        if (storageBox == 0)
        {
            DrawStorageOverview(filtered);
            DrawStoragePartyTray();
        }
        else
        {
            var pokemon = trainer.Party.Concat(filtered).FirstOrDefault(entry => entry.Id == selectedStoragePokemon);
            if (pokemon is null) selectedStoragePokemon = filtered.FirstOrDefault()?.Id;
            var layout = StorageLayout.Measure(ImGui.GetContentRegionAvail().X, CompanionScale);
            var height = StorageLayout.GridHeight(layout.CellHeight, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y, CompanionScale);
            if (!layout.PartyRail) DrawStoragePartyTray();
            if (layout.PartyRail)
            {
                DrawStoragePartyRail(layout.PartyWidth, height);
                ImGui.SameLine(0, 10 * CompanionScale);
            }
            ImGui.BeginGroup();
            DrawStorageBoxGrid(filtered, layout.BoxWidth, height, layout.CellHeight);
            DrawStorageBoxShortcuts(layout.BoxWidth, filtered.Count == 0);
            ImGui.EndGroup();
            if (layout.SideBySide) ImGui.SameLine(0, 10 * CompanionScale); else ImGui.Spacing();
            DrawStorageInspector(layout.DetailWidth, Math.Max(height, 480 * CompanionScale));
        }
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
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - 66 * scale));
        if (ImGui.InputTextWithHint("##StorageSearch", "Search your whole PC: species, nickname, move or ability...", ref storageSearch, 80))
            ShowStorageOverview();
        ImGui.SameLine();
        if (DrawStorageToolbarButton("Clear", CompanionMuted)) { storageSearch = string.Empty; storageScrollToTop = true; }
        DrawStorageSearchToolbar();
        if (DrawStorageToolbarButton("All boxes", CompanionAccent))
            ShowStorageOverview();
        StorageSameLine(right, 180 * scale);
        DrawStorageBoxJump(storageBox);
        var active = (storageFavorites ? 1 : 0) + (storageShiny ? 1 : 0) + (storageDuplicatesOnly ? 1 : 0)
                     + (storageType > 0 ? 1 : 0) + (storageGeneration > 0 ? 1 : 0);
        var filterLabel = active > 0 ? $"Filters ({active})" : "Filters";
        StorageSameLine(right, ImGui.CalcTextSize(filterLabel).X + 16 * scale);
        if (DrawStorageToolbarButton(filterLabel, active > 0 ? CompanionAccent : CompanionMuted)) ImGui.OpenPopup("StorageFilters");
        ImGui.SetNextWindowSize(new Vector2(330 * scale, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("StorageFilters"))
        {
            DrawStorageFilters();
            if (DrawAccentSmallButton("Reset filters", CompanionMuted))
            {
                storageFavorites = storageShiny = storageDuplicatesOnly = false;
                storageType = storageGeneration = storageSort = 0;
                storageScrollToTop = true;
            }
            ImGui.EndPopup();
        }
        var selectLabel = storageSelecting ? "Done selecting" : "Multi-select";
        StorageSameLine(right, ImGui.CalcTextSize(selectLabel).X + 16 * scale);
        ImGui.BeginDisabled(busy);
        if (DrawStorageToolbarButton(selectLabel, storageSelecting ? CompanionAccent : CompanionMuted))
        { storageSelecting = !storageSelecting; if (!storageSelecting) storageSelection.Clear(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click sprites to inspect. Shift-click to start selecting several Pokémon. Selections stay when switching boxes.");
        ImGui.EndDisabled();
        if (storageBox > 0)
        {
            StorageSameLine(right, ImGui.CalcTextSize("Rename box").X + 16 * scale);
            if (DrawStorageToolbarButton("Rename box", CompanionMuted))
            {
                boxName = trainer!.Collection.BoxNames.GetValueOrDefault(storageBox.ToString(), string.Empty);
                ImGui.OpenPopup("StorageBoxName");
            }
            if (ImGui.BeginPopup("StorageBoxName"))
            {
                ImGui.InputTextWithHint("##BoxName", "Box name...", ref boxName, 96);
                ImGui.BeginDisabled(busy || string.IsNullOrWhiteSpace(boxName));
                if (DrawAccentSmallButton("Save name", CompanionAccent))
                {
                    var number = storageBox; var name = boxName;
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

    private void DrawStorageBoxJump(int box, TradePicker? picker = null)
    {
        ImGui.SetNextItemWidth(Math.Max(1, Math.Min(180 * CompanionScale, ImGui.GetContentRegionAvail().X)));
        if (!ImGui.BeginCombo("##StorageJump", box == 0 ? "Jump to box..." : BoxLabel(box))) return;
        for (var number = 1; number <= StorageBrowser.BoxCount; number++)
            if (ImGui.Selectable($"{BoxLabel(number)} ({StorageBoxCount(number)}/30)##Jump{number}", box == number))
                BrowseStorageBox(number, picker);
        ImGui.EndCombo();
    }

    private int StorageBoxCount(int number) => storageBoxContents.TryGetValue(number, out var contents) ? contents.Count : 0;

    private void ShowStorageOverview()
    {
        storageBox = 0;
        storageScrollToTop = true;
    }

    private void OpenStorageBox(int number)
    {
        storageBox = Math.Clamp(number, 1, StorageBrowser.BoxCount);
        storageScrollToTop = true;
        boxName = trainer?.Collection.BoxNames.GetValueOrDefault(storageBox.ToString(), string.Empty) ?? string.Empty;
        if (!storageBoxContents.TryGetValue(storageBox, out var contents)) { selectedStoragePokemon = null; return; }
        if (!contents.Any(pokemon => pokemon.Id == selectedStoragePokemon)) selectedStoragePokemon = contents.FirstOrDefault()?.Id;
    }

    private void BrowseStorageBox(int number, TradePicker? picker)
    {
        if (picker is null) OpenStorageBox(number);
        else
        {
            picker.Box = number;
            picker.ScrollToTop = true;
        }
    }

    private void DrawStorageBoxGrid(List<PokemonDto> filtered, float width, float height, float cellHeight, TradePicker? picker = null)
    {
        var scale = CompanionScale;
        var box = picker?.Box ?? storageBox;
        var preserveSlots = picker is not null || storageSort == 0;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10 * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, StorageGrass);
        if (ImGui.BeginChild("StorageBoxGrid", new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var count = $"{StorageBoxCount(box)}/30";
            var countWidth = ImGui.CalcTextSize(count).X;
            DrawClippedText(BoxLabel(box), Math.Max(1, ImGui.GetContentRegionAvail().X - countWidth - 8 * scale), CompanionAccent);
            ImGui.SameLine(0, 8 * scale);
            ImGui.TextColored(CompanionMuted, count);
            ImGui.Spacing();
            var slots = StorageBrowser.BoxSlots(filtered, preserveSlots);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2.5f * scale));
            if (ImGui.BeginTable("StorageSlots", 6, ImGuiTableFlags.SizingStretchSame))
            {
                for (var index = 0; index < slots.Length; index++)
                {
                    ImGui.TableNextColumn(); ImGui.PushID(index);
                    var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), cellHeight);
                    DrawStorageSlot(slots[index], size, preserveSlots ? index + 1 : null, picker);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }

    private void DrawStorageSlot(PokemonDto? pokemon, Vector2 size, int? slot, TradePicker? picker = null)
    {
        var scale = CompanionScale;
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("StorageSlot", size);
        var hovered = ImGui.IsItemHovered();
        var selected = pokemon is not null && (picker is null ? selectedStoragePokemon : picker.Pokemon?.Id) == pokemon.Id;
        var marked = picker is null && pokemon is not null && storageSelection.Contains(pokemon.Id);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(selected ? new Vector4(0.23f, 0.29f, 0.38f, 1) : StorageSlot), 4 * scale);
        if (selected || hovered || marked)
            draw.AddRect(min, min + size, ImGui.GetColorU32(marked ? PokemonYellow : CompanionAccent), 4 * scale);
        if (pokemon is not null)
        {
            var image = Math.Min(Math.Min(size.X, size.Y) - 4 * scale, 68 * scale);
            image = Math.Max(1, image);
            DrawPokemonIconAt(pokemon.Appearance, pokemon.Shiny, min + (size - new Vector2(image)) / 2, new Vector2(image));
            var genderWidth = DrawStorageGenderMark(pokemon, min, size, scale);
            var marks = (pokemon.Shiny ? "★" : "") + (pokemon.Favorite ? "F" : "") + (pokemon.FusionHostId.Length > 0 ? "L" : "") + (pokemon.TradeLockId is not null ? " T" : "");
            if (marks.Length > 0)
            {
                var left = Math.Min(size.X, genderWidth + 2 * scale);
                draw.PushClipRect(min + new Vector2(left, 0), min + size, true);
                var position = min + new Vector2(size.X - ImGui.CalcTextSize(marks).X - 3 * scale, 2 * scale);
                draw.AddText(position, ImGui.GetColorU32(PokemonYellow), marks);
                draw.PopClipRect();
            }
            if (marked) draw.AddText(min + new Vector2(3 * scale, Math.Max(0, size.Y - ImGui.GetTextLineHeight())), ImGui.GetColorU32(PokemonYellow), "✓");
            if (GetPokemonAlerts(pokemon.Id) != default) DrawStorageAlertCorner(min, size, scale);
            var restriction = picker is null ? null : TradeOfferRestriction(pokemon);
            if (restriction is not null && !selected)
                draw.AddRectFilled(min, min + size, ImGui.GetColorU32(PanelSurface with { W = 0.52f }), 4 * scale);
            if (clicked && picker is not null) picker.Pokemon = pokemon;
            else if (clicked)
            {
                selectedStoragePokemon = pokemon.Id;
                if (!busy && (storageSelecting || ImGui.GetIO().KeyShift) && pokemon.FusionHostId.Length == 0 && pokemon.TradeLockId is null)
                {
                    storageSelecting = true;
                    if (!storageSelection.Remove(pokemon.Id)) StorageBrowser.Select(storageSelection, [pokemon]);
                }
            }
            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                DrawStoragePokemonTooltip(pokemon, restriction);
            }
        }
        else if (hovered && slot is int number) ImGui.SetTooltip($"Slot {number} / Empty or filtered out");
    }

    private static void DrawStorageAlertCorner(Vector2 origin, Vector2 size, float scale)
    {
        var length = Math.Min(26 * scale, Math.Min(size.X, size.Y) * 0.38f);
        if (length < 6 * scale) return;
        var corner = origin + size - new Vector2(scale);
        var radius = Math.Min(3 * scale, length / 3);
        var draw = ImGui.GetWindowDrawList();
        draw.PathLineTo(corner - new Vector2(0, length));
        draw.PathArcTo(corner - new Vector2(radius), radius, 0, MathF.PI / 2);
        draw.PathLineTo(corner - new Vector2(length, 0));
        draw.PathFillConvex(ImGui.GetColorU32(PokemonYellow with { W = 0.9f }));

        if (length < 12 * scale) return;
        var fontSize = Math.Min(ImGui.GetFontSize(), length * 0.56f);
        var textSize = ImGui.CalcTextSize("!") * (fontSize / ImGui.GetFontSize());
        var center = corner - new Vector2(length * 0.26f);
        draw.AddText(ImGui.GetFont(), fontSize, center - textSize / 2, ImGui.GetColorU32(PanelSurface), "!");
    }

    private void DrawStoragePokemonTooltip(PokemonDto pokemon, string? tradeRestriction = null)
    {
        ImGui.BeginTooltip();
        var gender = PokemonGenderMark(pokemon.Gender);
        ImGui.TextUnformatted($"{pokemon.Nickname} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level}{(gender is { } mark ? $" / {mark.Label}" : "")}");
        ImGui.TextUnformatted($"{string.Join(" / ", pokemon.Types)} / {pokemon.Ability}");
        ImGui.TextUnformatted($"{(pokemon.MintNature.Length > 0 ? pokemon.MintNature : pokemon.Nature)} / IV {StorageBrowser.IvTotal(pokemon)}/186 / {StorageBrowser.PerfectIvs(pokemon)} perfect");
        var copies = storageDuplicates.GetValueOrDefault(StorageBrowser.Variant(pokemon));
        if (copies > 1) ImGui.TextColored(CompanionAccent, $"{copies} owned copies");
        if (pokemon.Favorite) ImGui.TextColored(PokemonYellow, "Favourite / Protected from release");
        if (pokemon.Shiny) ImGui.TextColored(PokemonYellow, "Shiny");
        var (moves, evolutions) = GetPokemonAlerts(pokemon.Id);
        if (moves > 0) ImGui.TextColored(PokemonYellow, moves == 1 ? "New move available" : $"{moves} new moves available");
        if (evolutions > 0) ImGui.TextColored(PokemonYellow, "Evolution ready");
        if ((moves > 0 || evolutions > 0) && CollectionEditRestriction is { } reason)
            ImGui.TextColored(PokemonRed, reason);
        if (tradeRestriction is not null) ImGui.TextColored(PokemonOrange, tradeRestriction);
        else
        {
            if (pokemon.TradeLockId is not null) ImGui.TextColored(PokemonOrange, "Reserved for trading / Open Trading to cancel or withdraw.");
            if (pokemon.FusionHostId.Length > 0) ImGui.TextWrapped("Fusion partner / Unfuse the host to manage this Pokémon.");
        }
        ImGui.EndTooltip();
    }

    private static float DrawStorageGenderMark(PokemonDto pokemon, Vector2 origin, Vector2 size, float scale)
    {
        if (PokemonGenderMark(pokemon.Gender) is not { } mark) return 0;
        var edge = Math.Min(16 * scale, Math.Min(size.X, size.Y) * .4f);
        DrawPokemonGenderMark(mark, origin + new Vector2(2 * scale), edge);
        return edge + 2 * scale;
    }

    private void DrawStorageBoxShortcuts(float width, bool empty, TradePicker? picker = null)
    {
        var scale = CompanionScale;
        var box = picker?.Box ?? storageBox;
        var (first, last) = StorageLayout.BoxRange(box);
        var right = ImGui.GetCursorScreenPos().X + width;
        var gap = 3 * scale;
        var size = new Vector2(Math.Max(1, (width - 9 * gap) / 10), ImGui.GetFrameHeight());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        for (var number = first; number < first + 10; number++)
        {
            if (number != first) ImGui.SameLine(0, gap);
            if (DrawCompanionSegment(number.ToString(), box == number, size.X)) BrowseStorageBox(number, picker);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{BoxLabel(number)} / {StorageBoxCount(number)}/30");
        }
        ImGui.PopStyleVar();
        ImGui.BeginDisabled(first == 1);
        if (DrawAccentButton("<##PreviousBoxGroup", new Vector2(28, 24) * scale, CompanionAccent)) BrowseStorageBox(first - 10, picker);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(first == 1 ? "First box group" : $"Boxes {first - 10}-{first - 1}");
        ImGui.SameLine();
        ImGui.TextColored(CompanionMuted, $"{first}-{last} / {StorageBrowser.BoxCount}");
        ImGui.SameLine();
        ImGui.BeginDisabled(last == StorageBrowser.BoxCount);
        if (DrawAccentButton(">##NextBoxGroup", new Vector2(28, 24) * scale, CompanionAccent)) BrowseStorageBox(last + 1, picker);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(last == StorageBrowser.BoxCount ? "Last box group" : $"Boxes {last + 1}-{last + 10}");
        var legend = empty ? "No Pokémon match in this box." : "★ Shiny / F Favourite / ! Alert";
        StorageSameLine(right, ImGui.CalcTextSize(legend).X);
        DrawClippedText(legend, Math.Max(1, right - ImGui.GetCursorScreenPos().X), CompanionMuted);
    }

    private void DrawStorageOverview(List<PokemonDto> filtered)
    {
        var scale = CompanionScale;
        if (ImGui.Checkbox("Show empty boxes", ref storageShowEmptyBoxes)) storageScrollToTop = true;
        var (groups, boxes) = GetStorageOverview(filtered);
        ImGui.SameLine(); ImGui.TextColored(CompanionMuted, $"{filtered.Count:N0} Pokémon / {boxes.Length} boxes");
        var height = Math.Max(240 * scale, ImGui.GetContentRegionAvail().Y - 100 * scale);
        DrawStorageThumbnails(groups, boxes, 0, height);
    }

    private void DrawStorageThumbnails(Dictionary<int, List<PokemonDto>> groups, int[] boxes, float width, float height, TradePicker? picker = null)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("StorageBoxOverview", new Vector2(width, height), false))
        {
            if (picker is { ScrollToTop: true }) { ImGui.SetScrollY(0); picker.ScrollToTop = false; }
            else if (picker is null && storageScrollToTop) { ImGui.SetScrollY(0); storageScrollToTop = false; }
            if (boxes.Length == 0)
                ImGui.TextWrapped(picker is null ? "No stored Pokémon match. Clear your search or filters, or show empty boxes." : "No Pokémon in PC Storage match.");
            var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (175 * scale)), 1, 5);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4 * scale));
            if (ImGui.BeginTable("StorageBoxThumbnails", columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var number in boxes)
                {
                    ImGui.TableNextColumn(); ImGui.PushID(number);
                    var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 148 * scale);
                    if (ImGui.IsRectVisible(size)) DrawStorageBoxThumbnail(number, groups.GetValueOrDefault(number) ?? [], size, picker);
                    else ImGui.Dummy(size);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();
        }
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private void DrawStorageBoxThumbnail(int number, List<PokemonDto> pokemon, Vector2 size, TradePicker? picker = null)
    {
        var scale = CompanionScale;
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("OpenStorageBox", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(hovered ? new Vector4(0.20f, 0.25f, 0.33f, 1) : CompanionSurface), 4 * scale);
        if (hovered) draw.AddRect(min, min + size, ImGui.GetColorU32(CompanionAccent), 4 * scale);
        var count = $"{pokemon.Count}/30";
        var hasAlerts = false;
        foreach (var entry in pokemon)
            if (GetPokemonAlerts(entry.Id) != default) { hasAlerts = true; break; }
        var countX = size.X - ImGui.CalcTextSize(count).X - 7 * scale;
        var alertX = hasAlerts ? countX - ImGui.CalcTextSize("!").X * StorageAlertFontScale - 6 * scale : countX;
        draw.PushClipRect(min + new Vector2(7 * scale), min + new Vector2(Math.Max(8 * scale, alertX - 9 * scale), ImGui.GetTextLineHeight() + 8 * scale), true);
        draw.AddText(min + new Vector2(7 * scale), ImGui.GetColorU32(CompanionAccent), BoxLabel(number));
        draw.PopClipRect();
        if (hasAlerts)
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * StorageAlertFontScale, min + new Vector2(alertX, 5 * scale), ImGui.GetColorU32(PokemonYellow), "!");
        draw.AddText(min + new Vector2(countX, 7 * scale), ImGui.GetColorU32(CompanionMuted), count);
        var slots = StorageBrowser.BoxSlots(pokemon, picker is not null || storageSort == 0);
        var gap = 2 * scale;
        var origin = min + new Vector2(7 * scale, ImGui.GetTextLineHeight() + 14 * scale);
        var cell = new Vector2(Math.Max(1, (size.X - 14 * scale - 5 * gap) / 6), Math.Max(1, (size.Y - ImGui.GetTextLineHeight() - 21 * scale - 4 * gap) / 5));
        for (var index = 0; index < slots.Length; index++)
        {
            var start = origin + new Vector2(index % 6 * (cell.X + gap), index / 6 * (cell.Y + gap));
            draw.AddRectFilled(start, start + cell, ImGui.GetColorU32(StorageSlot), 2 * scale);
            if (slots[index] is not { } entry) continue;
            var image = Math.Max(1, Math.Min(cell.X, cell.Y));
            DrawPokemonIconAt(entry.Appearance, entry.Shiny, start + (cell - new Vector2(image)) / 2, new Vector2(image));
        }
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip($"{BoxLabel(number)} / {pokemon.Count} matching / {StorageBoxCount(number)} stored");
        }
        if (clicked) BrowseStorageBox(number, picker);
    }

    private void DrawStoragePartyRail(float width, float height, TradePicker? picker = null)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9 * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        if (ImGui.BeginChild("StoragePartyRail", new Vector2(width, height), true, FixedBattlePanel))
        {
            ImGui.SetScrollY(0);
            var origin = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail().X;
            var count = $"{trainer!.Party.Count}/6";
            ImGui.Dummy(new Vector2(available, ImGui.GetTextLineHeight()));
            var draw = ImGui.GetWindowDrawList();
            draw.AddText(origin, ImGui.GetColorU32(CompanionAccent), "Party");
            draw.AddText(origin + new Vector2(Math.Max(0, available - ImGui.CalcTextSize(count).X), 0), ImGui.GetColorU32(CompanionMuted), count);
            var slotHeight = StorageLayout.PartySlotHeight(ImGui.GetContentRegionAvail().Y, ImGui.GetStyle().ItemSpacing.Y, scale);
            for (var slot = 1; slot <= 6; slot++)
                DrawStoragePartySlot(trainer.Party.FirstOrDefault(pokemon => pokemon.PartySlot == slot), slot, new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), slotHeight), picker);
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }

    private void DrawStoragePartyTray(TradePicker? picker = null)
    {
        var scale = CompanionScale;
        ImGui.TextColored(CompanionAccent, $"Your party / {trainer!.Party.Count}/6");
        var gap = 4 * scale;
        var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - gap * 5) / 6);
        for (var slot = 1; slot <= 6; slot++)
        {
            if (slot > 1) ImGui.SameLine(0, gap);
            DrawStoragePartySlot(trainer.Party.FirstOrDefault(pokemon => pokemon.PartySlot == slot), slot, new Vector2(width, 58 * scale), picker);
        }
    }

    private void DrawStoragePartySlot(PokemonDto? pokemon, int slot, Vector2 size, TradePicker? picker)
    {
        var scale = CompanionScale;
        ImGui.PushID($"StorageParty{slot}");
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("PartySlot", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var selected = pokemon is not null && (picker is null ? selectedStoragePokemon : picker.Pokemon?.Id) == pokemon.Id;
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(selected ? new Vector4(0.20f, 0.25f, 0.33f, 1) : CompanionSurface), 4 * scale);
        draw.AddLine(min, min + new Vector2(size.X, 0), ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Border]));
        if (pokemon is not null)
        {
            var artHeight = Math.Max(1, size.Y - ImGui.GetTextLineHeight() - 6 * scale);
            var image = Math.Max(1, Math.Min(44 * scale, Math.Min(size.X - 8 * scale, artHeight)));
            DrawPokemonIconAt(pokemon.Appearance, pokemon.Shiny, min + new Vector2((size.X - image) / 2, 2 * scale + (artHeight - image) / 2), new Vector2(image));
            DrawStorageGenderMark(pokemon, min, size, scale);
            var label = $"Lv. {pokemon.Level}";
            draw.AddText(min + new Vector2(Math.Max(0, (size.X - ImGui.CalcTextSize(label).X) / 2), size.Y - ImGui.GetTextLineHeight() - 2 * scale), ImGui.GetColorU32(CompanionMuted), label);
            var restriction = picker is null ? null : TradeOfferRestriction(pokemon);
            if (restriction is not null && !selected)
                draw.AddRectFilled(min, min + size, ImGui.GetColorU32(PanelSurface with { W = 0.52f }), 4 * scale);
            if (picker is not null && (selected || hovered))
                draw.AddRect(min, min + size, ImGui.GetColorU32(CompanionAccent), 4 * scale);
            if (hovered) DrawStoragePokemonTooltip(pokemon, restriction);
            if (clicked && picker is not null) picker.Pokemon = pokemon;
            else if (clicked)
            {
                selectedStoragePokemon = pokemon.Id;
                if (storageBox == 0) OpenPokemonSummary(pokemon.Id);
            }
        }
        else
        {
            var label = "Free";
            draw.AddText(min + (size - ImGui.CalcTextSize(label)) / 2, ImGui.GetColorU32(CompanionMuted), label);
            if (hovered) ImGui.SetTooltip(picker is null ? $"Party slot {slot} / Select a stored Pokémon and choose To party" : $"Party slot {slot} is empty");
        }
        ImGui.PopID();
    }

    private void DrawStorageInspector(float width, float height)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        if (ImGui.BeginChild("StorageInspector", new Vector2(width, height), true))
        {
            var pokemon = trainer!.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == selectedStoragePokemon);
            if (pokemon is null) ImGui.TextWrapped("Select a Pokémon to see its details.");
            else
            {
                ImGui.PushID(pokemon.Id);
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
                DrawStoragePokemonDetails(pokemon);
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                ImGui.BeginDisabled(CollectionLocked || pendingStorageBatch is not null || pokemon.FusionHostId.Length > 0 || pokemon.TradeLockId is not null);
                ImGui.BeginDisabled(pokemon.PartySlot is null && trainer.Party.Count >= 6);
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
                ImGui.PopID();
            }
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
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
        ImGui.BeginDisabled(CollectionLocked || pendingStorageBatch is not null || StorageBrowser.ReleaseProtected(pokemon));
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
                var free = StorageBrowser.BoxCapacity - StorageBoxCount(number);
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
