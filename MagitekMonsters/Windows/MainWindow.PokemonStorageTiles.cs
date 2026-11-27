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
    private sealed partial class PokemonStorageBrowser
    {
        private const float StorageAlertFontScale = 1.4f;
        private static readonly Vector4 StorageGrass = new(0.15f, 0.24f, 0.20f, 1);
        private static readonly Vector4 StorageSlot = new(0.20f, 0.31f, 0.26f, 1);

        private void DrawStorageBoxGrid(List<PokemonDto> filtered, float width, float height, float cellHeight)
        {
            var scale = CompanionScale;
            var box = Box;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10 * scale));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, StorageGrass);
            if (ImGui.BeginChild("StorageBoxGrid", new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                var count = $"{inventory.Count(box)}/30";
                var countWidth = ImGui.CalcTextSize(count).X;
                DrawClippedText(inventory.Label(box), Math.Max(1, ImGui.GetContentRegionAvail().X - countWidth - 8 * scale), CompanionAccent);
                ImGui.SameLine(0, 8 * scale);
                ImGui.TextColored(CompanionMuted, count);
                ImGui.Spacing();
                var slots = StorageBrowser.BoxSlots(filtered);
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2.5f * scale));
                if (ImGui.BeginTable("StorageSlots", 6, ImGuiTableFlags.SizingStretchSame))
                {
                    for (var index = 0; index < slots.Length; index++)
                    {
                        ImGui.TableNextColumn(); ImGui.PushID(index);
                        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), cellHeight);
                        DrawStorageSlot(slots[index], size, index + 1);
                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
                ImGui.PopStyleVar();
            }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
        }

        private void DrawStorageSlot(PokemonDto? pokemon, Vector2 size, int? slot)
        {
            var scale = CompanionScale;
            var min = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton("StorageSlot", size);
            var hovered = ImGui.IsItemHovered();
            var dragging = DrawStorageDragSource(pokemon, slot);
            if (slot is not null) DrawStorageDropTarget(Box, slot, pokemon);
            var selected = pokemon is not null && SelectedId == pokemon.Id;
            var marked = pokemon is not null && Marked?.Contains(pokemon.Id) == true;
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(min, min + size, ImGui.GetColorU32(selected ? new Vector4(0.23f, 0.29f, 0.38f, 1) : StorageSlot), 4 * scale);
            if (selected || hovered || marked)
                draw.AddRect(min, min + size, ImGui.GetColorU32(marked ? PokemonYellow : CompanionAccent), 4 * scale);
            if (pokemon is not null)
            {
                var image = Math.Min(Math.Min(size.X, size.Y) - 4 * scale, 68 * scale);
                image = Math.Max(1, image);
                DrawIcon(pokemon.Appearance, pokemon.Shiny, min + (size - new Vector2(image)) / 2, new Vector2(image));
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
                if (GetAlerts(pokemon.Id) != default) DrawStorageAlertCorner(min, size, scale);
                var restriction = Restriction?.Invoke(pokemon);
                if (restriction is not null && !selected)
                    draw.AddRectFilled(min, min + size, ImGui.GetColorU32(PanelSurface with { W = 0.52f }), 4 * scale);
                if (clicked && !dragging) Select(pokemon);
                if (hovered && !dragging)
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

        private void DrawStoragePokemonTooltip(PokemonDto pokemon, string? restriction = null)
        {
            ImGui.BeginTooltip();
            var gender = PokemonGenderMark(pokemon.Gender);
            ImGui.TextUnformatted($"{pokemon.Nickname} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level}{(gender is { } mark ? $" / {mark.Label}" : "")}");
            ImGui.TextUnformatted($"{string.Join(" / ", pokemon.Types)} / {pokemon.Ability}");
            ImGui.TextUnformatted($"{(pokemon.MintNature.Length > 0 ? pokemon.MintNature : pokemon.Nature)} / IV {StorageBrowser.IvTotal(pokemon)}/186 / {StorageBrowser.PerfectIvs(pokemon)} perfect");
            var copies = inventory.Duplicates.GetValueOrDefault(StorageBrowser.Variant(pokemon));
            if (copies > 1) ImGui.TextColored(CompanionAccent, $"{copies} owned copies");
            if (pokemon.Favorite) ImGui.TextColored(PokemonYellow, "Favourite / Protected from release");
            if (pokemon.Shiny) ImGui.TextColored(PokemonYellow, "Shiny");
            var (moves, evolutions) = GetAlerts(pokemon.Id);
            if (moves > 0) ImGui.TextColored(PokemonYellow, moves == 1 ? "New move available" : $"{moves} new moves available");
            if (evolutions > 0) ImGui.TextColored(PokemonYellow, "Evolution ready");
            if ((moves > 0 || evolutions > 0) && EditRestrictionMessage() is { } reason)
                ImGui.TextColored(PokemonRed, reason);
            if (restriction is not null) ImGui.TextColored(PokemonOrange, restriction);
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

        private void DrawStorageBoxShortcuts(float width, bool empty)
        {
            var scale = CompanionScale;
            var box = Box;
            var (first, last) = StorageLayout.BoxRange(box);
            var right = ImGui.GetCursorScreenPos().X + width;
            var gap = 3 * scale;
            var size = new Vector2(Math.Max(1, (width - 9 * gap) / 10), ImGui.GetFrameHeight());
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            for (var number = first; number < first + 10; number++)
            {
                if (number != first) ImGui.SameLine(0, gap);
                if (DrawCompanionSegment(number.ToString(), box == number, size.X)) OpenBox(number);
                DrawStorageDropTarget(number, null, null);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{inventory.Label(number)} / {inventory.Count(number)}/30");
            }
            ImGui.PopStyleVar();
            ImGui.BeginDisabled(first == 1);
            if (DrawAccentButton("<##PreviousBoxGroup", new Vector2(28, 24) * scale, CompanionAccent)) OpenBox(first - 10);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(first == 1 ? "First box group" : $"Boxes {first - 10}-{first - 1}");
            ImGui.SameLine();
            ImGui.TextColored(CompanionMuted, $"{first}-{last} / {StorageBrowser.BoxCount}");
            ImGui.SameLine();
            ImGui.BeginDisabled(last == StorageBrowser.BoxCount);
            if (DrawAccentButton(">##NextBoxGroup", new Vector2(28, 24) * scale, CompanionAccent)) OpenBox(last + 1);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(last == StorageBrowser.BoxCount ? "Last box group" : $"Boxes {last + 1}-{last + 10}");
            var legend = empty ? "No Pokémon match in this box." : "★ Shiny / F Favourite / ! Alert";
            StorageSameLine(right, ImGui.CalcTextSize(legend).X);
            DrawClippedText(legend, Math.Max(1, right - ImGui.GetCursorScreenPos().X), CompanionMuted);
        }

        private void DrawStorageThumbnails(float width, float height)
        {
            var scale = CompanionScale;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            if (ImGui.BeginChild("StorageBoxOverview", new Vector2(width, height), false))
            {
                var boxes = OverviewBoxes();
                if (scrollToTop) { ImGui.SetScrollY(0); scrollToTop = false; }
                if (boxes.Length == 0)
                    ImGui.TextWrapped(Compact ? "No Pokémon in PC Storage match." : "No stored Pokémon match. Clear your search or filters, or show empty boxes.");
                var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (175 * scale)), 1, 5);
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4 * scale));
                if (ImGui.BeginTable("StorageBoxThumbnails", columns, ImGuiTableFlags.SizingStretchSame))
                {
                    foreach (var number in boxes)
                    {
                        ImGui.TableNextColumn(); ImGui.PushID(number);
                        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 148 * scale);
                        if (ImGui.IsRectVisible(size)) DrawStorageBoxThumbnail(number, overviewGroups.GetValueOrDefault(number) ?? [], size);
                        else ImGui.Dummy(size);
                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
                ImGui.PopStyleVar();
            }
            ImGui.EndChild(); ImGui.PopStyleVar();
        }

        private void DrawStorageBoxThumbnail(int number, List<PokemonDto> pokemon, Vector2 size)
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
                if (GetAlerts(entry.Id) != default) { hasAlerts = true; break; }
            var countX = size.X - ImGui.CalcTextSize(count).X - 7 * scale;
            var alertX = hasAlerts ? countX - ImGui.CalcTextSize("!").X * StorageAlertFontScale - 6 * scale : countX;
            draw.PushClipRect(min + new Vector2(7 * scale), min + new Vector2(Math.Max(8 * scale, alertX - 9 * scale), ImGui.GetTextLineHeight() + 8 * scale), true);
            draw.AddText(min + new Vector2(7 * scale), ImGui.GetColorU32(CompanionAccent), inventory.Label(number));
            draw.PopClipRect();
            if (hasAlerts)
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * StorageAlertFontScale, min + new Vector2(alertX, 5 * scale), ImGui.GetColorU32(PokemonYellow), "!");
            draw.AddText(min + new Vector2(countX, 7 * scale), ImGui.GetColorU32(CompanionMuted), count);
            var slots = StorageBrowser.BoxSlots(pokemon);
            var gap = 2 * scale;
            var origin = min + new Vector2(7 * scale, ImGui.GetTextLineHeight() + 14 * scale);
            var cell = new Vector2(Math.Max(1, (size.X - 14 * scale - 5 * gap) / 6), Math.Max(1, (size.Y - ImGui.GetTextLineHeight() - 21 * scale - 4 * gap) / 5));
            for (var index = 0; index < slots.Length; index++)
            {
                var start = origin + new Vector2(index % 6 * (cell.X + gap), index / 6 * (cell.Y + gap));
                draw.AddRectFilled(start, start + cell, ImGui.GetColorU32(StorageSlot), 2 * scale);
                if (slots[index] is not { } entry) continue;
                var image = Math.Max(1, Math.Min(cell.X, cell.Y));
                DrawIcon(entry.Appearance, entry.Shiny, start + (cell - new Vector2(image)) / 2, new Vector2(image));
            }
            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip($"{inventory.Label(number)} / {pokemon.Count} matching / {inventory.Count(number)} stored");
            }
            if (clicked) OpenBox(number);
        }

        private void DrawStoragePartyRail(float width, float height)
        {
            var scale = CompanionScale;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9 * scale));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
            if (ImGui.BeginChild("StoragePartyRail", new Vector2(width, height), true, FixedBattlePanel))
            {
                ImGui.SetScrollY(0);
                var origin = ImGui.GetCursorScreenPos();
                var available = ImGui.GetContentRegionAvail().X;
                var count = $"{inventory.Owner.Party.Count}/6";
                ImGui.Dummy(new Vector2(available, ImGui.GetTextLineHeight()));
                var draw = ImGui.GetWindowDrawList();
                draw.AddText(origin, ImGui.GetColorU32(CompanionAccent), "Party");
                draw.AddText(origin + new Vector2(Math.Max(0, available - ImGui.CalcTextSize(count).X), 0), ImGui.GetColorU32(CompanionMuted), count);
                var slotHeight = StorageLayout.PartySlotHeight(ImGui.GetContentRegionAvail().Y, ImGui.GetStyle().ItemSpacing.Y, scale);
                for (var slot = 1; slot <= 6; slot++)
                    DrawStoragePartySlot(inventory.Owner.Party.FirstOrDefault(pokemon => pokemon.PartySlot == slot), slot, new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), slotHeight));
            }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
        }

        private void DrawStoragePartyTray()
        {
            var scale = CompanionScale;
            ImGui.TextColored(CompanionAccent, $"Your party / {inventory.Owner.Party.Count}/6");
            var gap = 4 * scale;
            var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - gap * 5) / 6);
            for (var slot = 1; slot <= 6; slot++)
            {
                if (slot > 1) ImGui.SameLine(0, gap);
                DrawStoragePartySlot(inventory.Owner.Party.FirstOrDefault(pokemon => pokemon.PartySlot == slot), slot, new Vector2(width, 58 * scale));
            }
        }

        private void DrawStoragePartySlot(PokemonDto? pokemon, int slot, Vector2 size)
        {
            var scale = CompanionScale;
            ImGui.PushID($"StorageParty{slot}");
            var min = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton("PartySlot", size);
            var hovered = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            var selected = pokemon is not null && SelectedId == pokemon.Id;
            draw.AddRectFilled(min, min + size, ImGui.GetColorU32(selected ? new Vector4(0.20f, 0.25f, 0.33f, 1) : CompanionSurface), 4 * scale);
            draw.AddLine(min, min + new Vector2(size.X, 0), ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Border]));
            if (pokemon is not null)
            {
                var artHeight = Math.Max(1, size.Y - ImGui.GetTextLineHeight() - 6 * scale);
                var image = Math.Max(1, Math.Min(44 * scale, Math.Min(size.X - 8 * scale, artHeight)));
                DrawIcon(pokemon.Appearance, pokemon.Shiny, min + new Vector2((size.X - image) / 2, 2 * scale + (artHeight - image) / 2), new Vector2(image));
                DrawStorageGenderMark(pokemon, min, size, scale);
                var label = $"Lv. {pokemon.Level}";
                draw.AddText(min + new Vector2(Math.Max(0, (size.X - ImGui.CalcTextSize(label).X) / 2), size.Y - ImGui.GetTextLineHeight() - 2 * scale), ImGui.GetColorU32(CompanionMuted), label);
                var restriction = Restriction?.Invoke(pokemon);
                if (restriction is not null && !selected)
                    draw.AddRectFilled(min, min + size, ImGui.GetColorU32(PanelSurface with { W = 0.52f }), 4 * scale);
                if (Compact && (selected || hovered))
                    draw.AddRect(min, min + size, ImGui.GetColorU32(CompanionAccent), 4 * scale);
                if (hovered) DrawStoragePokemonTooltip(pokemon, restriction);
                if (clicked) Select(pokemon);
            }
            else
            {
                var label = "Free";
                draw.AddText(min + (size - ImGui.CalcTextSize(label)) / 2, ImGui.GetColorU32(CompanionMuted), label);
                if (hovered) ImGui.SetTooltip(Compact ? $"Party slot {slot} is empty" : $"Party slot {slot} / Select a stored Pokémon and choose To party");
            }
            ImGui.PopID();
        }
    }
}
