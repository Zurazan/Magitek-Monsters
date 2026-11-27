using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly record struct CatalogTile(string Name, string Description, string? ItemId, string? Species,
        string Price, string Status, Vector4 StatusColor);

    private sealed class ShopCatalogBrowser<T>(Func<T, string> key, Action<CatalogTile, Vector2> drawArtwork) where T : class
    {
        public string Search = string.Empty;
        public string Category = string.Empty;
        public string Selected = string.Empty;
        public int Quantity = 1;
        private int page;
        private string? inspected;
        private IReadOnlyList<T>? filterSource;
        private List<T> filtered = [];
        private string? appliedSearch, appliedCategory;

        public void Focus(string id, string search, string category)
        {
            Search = search;
            Category = category;
            page = 0;
            inspected = null;
            Select(id);
            Quantity = 1;
        }

        public void DrawSearch(string hint)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##CatalogSearch", hint, ref Search, 100)) page = 0;
        }

        public void DrawCategories(IReadOnlyList<(string Label, string Id)> categories)
        {
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            for (var index = 0; index < categories.Count; index++)
            {
                var (label, id) = categories[index];
                if (index > 0) SameLineIfFits(label, right);
                if (DrawAccentSmallButton(label, Category == id ? CompanionAccent : CompanionMuted))
                {
                    Category = id;
                    page = 0;
                }
            }
            ImGui.Spacing();
        }

        public IReadOnlyList<T> Filter(IReadOnlyList<T> stock, Func<T, string[], string, bool> matches)
        {
            if (ReferenceEquals(filterSource, stock) && appliedSearch == Search && appliedCategory == Category) return filtered;
            filterSource = stock;
            appliedSearch = Search;
            appliedCategory = Category;
            var words = MartCatalog.SearchWords(Search);
            filtered = stock.Where(item => matches(item, words, Category)).ToList();
            return filtered;
        }

        public void Draw(IReadOnlyList<T> stock, string noun, Func<T, CatalogTile> describe, Action<T> details, Action<T> actions, float footerHeight)
        {
            var scale = CompanionScale;
            ImGui.TextColored(CompanionMuted, $"{stock.Count:N0} {noun}");
            ImGui.Spacing();
            var layout = MartCatalog.Layout(Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * scale), ImGui.GetContentRegionAvail().Y,
                scale, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y);
            var selected = DrawStock(stock, noun, describe, layout);
            if (layout.SideBySide) ImGui.SameLine(0, 12 * scale); else ImGui.Spacing();
            DrawInspector(selected, describe, details, actions, layout, footerHeight);
        }

        private T? DrawStock(IReadOnlyList<T> stock, string noun, Func<T, CatalogTile> describe, MartGrid layout)
        {
            var pages = Math.Max(1, (stock.Count + layout.PageSize - 1) / layout.PageSize);
            page = Math.Clamp(page, 0, pages - 1);
            T? selected = null;
            var selectedIndex = -1;
            for (var index = 0; index < stock.Count; index++)
            {
                if (key(stock[index]) != Selected) continue;
                selectedIndex = index;
                selected = stock[index];
                break;
            }
            // Keep a preselected offer visible when opening from a shortcut.
            if (inspected is null && selectedIndex >= 0) page = selectedIndex / layout.PageSize;
            var first = page * layout.PageSize;
            var last = Math.Min(stock.Count, first + layout.PageSize);
            if ((selectedIndex < first || selectedIndex >= last) && first < last)
            {
                selected = stock[first];
                Select(key(selected));
            }
            else if (stock.Count == 0) Select(string.Empty);

            ImGui.BeginChild("MartStock", new Vector2(layout.BrowseWidth, layout.BrowseHeight), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            var start = ImGui.GetCursorScreenPos();
            var gap = ImGui.GetStyle().ItemSpacing;
            var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - gap.X * (layout.Columns - 1)) / layout.Columns);
            for (var index = first; index < last; index++)
            {
                var item = stock[index];
                var offset = index - first;
                ImGui.SetCursorScreenPos(start + new Vector2(offset % layout.Columns * (width + gap.X), offset / layout.Columns * (layout.CardHeight + gap.Y)));
                ImGui.PushID(key(item));
                if (DrawTile(describe(item), width, layout.CardHeight, key(item) == Selected))
                {
                    Select(key(item));
                    selected = item;
                }
                ImGui.PopID();
            }
            if (stock.Count == 0) ImGui.TextWrapped($"No {noun} match your filters.");
            ImGui.SetCursorScreenPos(start + new Vector2(0, layout.BrowseHeight - layout.PagerHeight));
            ImGui.Separator();
            DrawClippedText($"{(stock.Count == 0 ? 0 : first + 1):N0} to {last:N0} of {stock.Count:N0}", layout.BrowseWidth, CompanionMuted);
            var button = Math.Min(80 * CompanionScale, Math.Max(1, (layout.BrowseWidth - 48 * CompanionScale) / 2));
            var previousPage = page;
            ImGui.BeginDisabled(page == 0);
            if (ImGui.Button("Previous", new Vector2(button, 0))) page--;
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.TextUnformatted($"{page + 1}/{pages}"); ImGui.SameLine();
            ImGui.BeginDisabled(page >= pages - 1);
            if (ImGui.Button("Next", new Vector2(button, 0))) page++;
            ImGui.EndDisabled();
            ImGui.EndChild();
            if (page != previousPage)
            {
                selected = stock[page * layout.PageSize];
                Select(key(selected));
            }
            return selected;
        }

        private void Select(string id)
        {
            if (Selected == id) return;
            Selected = id;
            Quantity = 1;
        }

        private bool DrawTile(CatalogTile tile, float width, float height, bool selected)
        {
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            var clicked = ImGui.InvisibleButton("##CatalogTile", new Vector2(width, height));
            var hovered = ImGui.IsItemHovered();
            var after = ImGui.GetCursorScreenPos();
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var draw = ImGui.GetWindowDrawList();
            var scale = CompanionScale;
            draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(0.16f, 0.20f, 0.27f, 1) : CompanionSurface), 4 * scale);
            if (hovered) draw.AddRect(min, max, ImGui.GetColorU32(CompanionAccent with { W = 0.14f }), 4 * scale, ImDrawFlags.None, 4 * scale);
            draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? CompanionAccent : new Vector4(0.23f, 0.24f, 0.28f, 1)), 4 * scale);
            var pad = 10 * scale;
            var contentWidth = Math.Max(1, width - pad * 2);
            draw.PushClipRect(min, max, true);
            ImGui.SetCursorScreenPos(min + new Vector2(pad, 5 * scale));
            drawArtwork(tile, new Vector2(38 * scale));
            var statusWidth = ImGui.CalcTextSize(tile.Status).X;
            var inline = ImGui.CalcTextSize(tile.Price).X + statusWidth + 8 * scale <= contentWidth;
            var line = ImGui.GetTextLineHeight();
            ImGui.SetCursorScreenPos(new Vector2(min.X + pad, max.Y - pad - line * (inline ? 2 : 3)));
            DrawClippedText(tile.Name, contentWidth, new Vector4(0.91f, 0.92f, 0.95f, 1));
            ImGui.SetCursorScreenPos(new Vector2(min.X + pad, max.Y - pad - line * (inline ? 1 : 2)));
            DrawClippedText(tile.Price, inline ? contentWidth - statusWidth - 8 * scale : contentWidth, PokemonYellow);
            ImGui.SetCursorScreenPos(new Vector2(inline ? max.X - pad - statusWidth : min.X + pad, max.Y - pad - line));
            DrawClippedText(tile.Status, inline ? statusWidth : contentWidth, tile.StatusColor);
            draw.PopClipRect();
            ImGui.SetCursorScreenPos(after);
            if (hovered) ImGui.SetTooltip(tile.Name + "\n" + tile.Description);
            return clicked;
        }

        private void DrawInspector(T? selected, Func<T, CatalogTile> describe, Action<T> details, Action<T> actions,
            MartGrid layout, float footerHeight)
        {
            var scale = CompanionScale;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 * scale));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
            ImGui.BeginChild("MartPurchase", new Vector2(layout.DetailWidth, layout.DetailHeight), true,
                ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (selected is null)
            {
                inspected = null;
                ImGui.TextWrapped("Select an offer to see its details.");
            }
            else
            {
                var infoHeight = Math.Max(ImGui.GetTextLineHeight() * 2, ImGui.GetContentRegionAvail().Y - footerHeight - ImGui.GetStyle().ItemSpacing.Y);
                ImGui.BeginChild("MartItemDetails", new Vector2(0, infoHeight), false);
                var selectedId = key(selected);
                if (inspected != selectedId)
                {
                    ImGui.SetScrollY(0);
                    inspected = selectedId;
                }
                var tile = describe(selected);
                var art = Math.Min(72 * scale, Math.Max(40 * scale, infoHeight * 0.28f));
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - art) / 2));
                drawArtwork(tile, new Vector2(art));
                DrawGoalText(tile.Name, CompanionAccent);
                details(selected);
                ImGui.EndChild();
                ImGui.Spacing();
                ImGui.Separator();
                actions(selected);
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }
    }

    private void DrawCatalogArtwork(CatalogTile tile, Vector2 size)
    {
        if (tile.Species is { Length: > 0 } species)
        {
            DrawPokemonIconAt(species, false, ImGui.GetCursorScreenPos(), size);
            ImGui.Dummy(size);
        }
        else DrawItemImage(tile.ItemId ?? string.Empty, size);
    }
}
