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
    private static readonly (string Label, string Id)[] MartCategories =
    [
        ("Poké Balls", "ball"), ("Medicine", "healing"), ("Evolution", "evolution"), ("Training", "training"),
        ("Held items", "held"), ("Key items", "key"), ("TMs", "machine"), ("Move tutors", "tutor")
    ];
    private string selectedShopItem = string.Empty;
    private string selectedTokenShopItem = string.Empty;
    private string tokenShopSearch = string.Empty;
    private int tokenShopPage;
    private int tokenShopQuantity = 1;
    private string purchaseOwner = string.Empty;
    private int purchaseUnitPrice;
    private string purchaseCurrency = string.Empty;
    private string martInspectorItem = string.Empty;
    private int shopExchange;
    private bool shopCompact;
    private static readonly string[] ShopExchanges = ["Items", "Rank Token Exchange", "Prize Exchange", "Collection Exchange"];

    private void ResetMartCatalog()
    {
        selectedShopItem = selectedTokenShopItem = shopSearch = tokenShopSearch = string.Empty;
        shopCategoryIndex = -1; shopPage = tokenShopPage = 0; shopBuyQuantity = tokenShopQuantity = 1;
        pendingPurchase = null; requestPurchase = false; purchaseOwner = purchaseCurrency = martInspectorItem = string.Empty;
        shopExchange = 0; shopCompact = false;
    }

    private void DrawCompactShop()
    {
        var requested = Array.IndexOf(ShopExchanges, requestedShopTab);
        if (requested >= 0) shopExchange = requested;
        if (openCollectionExchange) { shopExchange = 3; openCollectionExchange = false; }
        requestedShopTab = null;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##ShopExchange", ShopExchanges[shopExchange]))
        {
            for (var index = 0; index < ShopExchanges.Length; index++)
                if (ImGui.Selectable(ShopExchanges[index], shopExchange == index)) shopExchange = index;
            ImGui.EndCombo();
        }
        if (shopExchange == 0) DrawItemShop();
        else if (shopExchange == 1) DrawItemShop(true);
        else if (shopExchange == 2) DrawPrizeExchange();
        else DrawCollectionExchange();
        DrawPurchaseConfirmation();
    }

    private void DrawCompanionItemShop(bool rankTokensOnly)
    {
        if (trainer is null) return;
        ImGui.PushID(rankTokensOnly ? "TokenCatalog" : "MoneyCatalog");
        ref var search = ref (rankTokensOnly ? ref tokenShopSearch : ref shopSearch);
        ref var page = ref (rankTokensOnly ? ref tokenShopPage : ref shopPage);
        ref var quantity = ref (rankTokensOnly ? ref tokenShopQuantity : ref shopBuyQuantity);
        ref var selected = ref (rankTokensOnly ? ref selectedTokenShopItem : ref selectedShopItem);
        var scale = CompanionScale;
        if (!rankTokensOnly)
        {
            if (DrawAccentSmallButton("BP Exchange", CompanionMuted)) { openBpExchange = true; mainSection = 8; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Competitive rewards are in Battle Frontier > BP Exchange.");
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Refresh shop", CompanionMuted)) Run(RefreshShop, "Refreshing shop...");
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (!rankTokensOnly) DrawMartCategories();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##ShopSearch", "Search items, descriptions or moves...", ref search, 100)) page = 0;
        var category = shopCategoryIndex < 0 ? "" : MartCategories[Math.Clamp(shopCategoryIndex, 0, MartCategories.Length - 1)].Id;
        var items = GetMartItems(rankTokensOnly, category, search);
        var selectedId = selected;
        if (!items.Any(item => item.Id == selectedId))
        {
            selected = items.FirstOrDefault()?.Id ?? string.Empty;
            quantity = 1;
        }
        ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
        ImGui.TextWrapped($"{items.Count:N0} items / Select an item to inspect and buy");
        ImGui.PopStyleColor();
        ImGui.Spacing();
        var layout = MartCatalog.Layout(Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * scale), ImGui.GetContentRegionAvail().Y,
            scale, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y);
        DrawMartStock(items, layout, ref page, ref selected, ref quantity);
        if (layout.SideBySide) ImGui.SameLine(0, 12 * scale); else ImGui.Spacing();
        selectedId = selected;
        DrawMartInspector(items.FirstOrDefault(item => item.Id == selectedId), layout, rankTokensOnly, ref quantity);
        ImGui.PopID();
    }

    private void DrawMartCategories()
    {
        shopCategoryIndex = Math.Clamp(shopCategoryIndex, -1, MartCategories.Length - 1);
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var previousRight = 0f;
        for (var index = -1; index < MartCategories.Length; index++)
        {
            var label = index < 0 ? "All items" : MartCategories[index].Label;
            var width = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
            if (index > -1 && previousRight + ImGui.GetStyle().ItemSpacing.X + width <= right) ImGui.SameLine();
            if (DrawAccentSmallButton(label, shopCategoryIndex == index ? CompanionAccent : CompanionMuted))
            { shopCategoryIndex = index; shopPage = 0; }
            previousRight = ImGui.GetItemRectMax().X;
        }
        ImGui.Spacing();
    }

    private void DrawMartStock(List<ShopItemDto> items, MartGrid layout, ref int page, ref string selected, ref int quantity)
    {
        var pages = Math.Max(1, (items.Count + layout.PageSize - 1) / layout.PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        ImGui.BeginChild("MartStock", new Vector2(layout.BrowseWidth, layout.BrowseHeight), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        var start = ImGui.GetCursorScreenPos();
        var gap = ImGui.GetStyle().ItemSpacing;
        var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - gap.X * (layout.Columns - 1)) / layout.Columns);
        var visible = items.Skip(page * layout.PageSize).Take(layout.PageSize).ToArray();
        for (var index = 0; index < visible.Length; index++)
        {
            var item = visible[index];
            ImGui.SetCursorScreenPos(start + new Vector2(index % layout.Columns * (width + gap.X), index / layout.Columns * (layout.CardHeight + gap.Y)));
            if (DrawMartItem(item, width, layout.CardHeight, item.Id == selected))
            { if (selected != item.Id) quantity = 1; selected = item.Id; }
        }
        if (visible.Length == 0)
        {
            ImGui.TextWrapped("No items match your search.");
            ImGui.TextWrapped("Try another category or a different search.");
        }
        ImGui.SetCursorScreenPos(start + new Vector2(0, layout.BrowseHeight - layout.PagerHeight));
        ImGui.Separator();
        DrawClippedText($"{(items.Count == 0 ? 0 : page * layout.PageSize + 1):N0} to {Math.Min(items.Count, (page + 1) * layout.PageSize):N0} of {items.Count:N0}", layout.BrowseWidth, CompanionMuted);
        var button = Math.Min(80 * CompanionScale, Math.Max(1, (layout.BrowseWidth - 48 * CompanionScale) / 2));
        ImGui.BeginDisabled(page == 0);
        if (ImGui.Button("Previous", new Vector2(button, 0))) page--;
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.TextUnformatted($"{page + 1}/{pages}"); ImGui.SameLine();
        ImGui.BeginDisabled(page >= pages - 1);
        if (ImGui.Button("Next", new Vector2(button, 0))) page++;
        ImGui.EndDisabled();
        ImGui.EndChild();
    }

    private bool DrawMartItem(ShopItemDto item, float width, float height, bool selected)
    {
        ImGui.PushID(item.Id);
        var min = ImGui.GetCursorScreenPos(); var max = min + new Vector2(width, height);
        var clicked = ImGui.InvisibleButton("##MartItem", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered(); var after = ImGui.GetCursorScreenPos();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var draw = ImGui.GetWindowDrawList(); var scale = CompanionScale;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(0.16f, 0.20f, 0.27f, 1) : CompanionSurface), 4 * scale);
        if (hovered) draw.AddRect(min, max, ImGui.GetColorU32(CompanionAccent with { W = 0.14f }), 4 * scale, ImDrawFlags.None, 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? CompanionAccent : new Vector4(0.23f, 0.24f, 0.28f, 1)), 4 * scale);
        var padding = 10 * scale; var contentWidth = Math.Max(1, width - padding * 2);
        draw.PushClipRect(min, max, true);
        ImGui.SetCursorScreenPos(min + new Vector2(padding, 5 * scale));
        DrawItemImage(item.Id, new Vector2(38 * scale));
        var price = $"{(item.Currency == "rank-tokens" ? "RT" : "¥")} {item.Price:N0}";
        var owned = $"Bag {InventoryCount(item.Id):N0}";
        var ownedWidth = ImGui.CalcTextSize(owned).X;
        var inline = ImGui.CalcTextSize(price).X + ownedWidth + 8 * scale <= contentWidth;
        var line = ImGui.GetTextLineHeight();
        ImGui.SetCursorScreenPos(new Vector2(min.X + padding, max.Y - padding - line * (inline ? 2 : 3)));
        DrawClippedText(item.Name, contentWidth, new Vector4(0.91f, 0.92f, 0.95f, 1));
        ImGui.SetCursorScreenPos(new Vector2(min.X + padding, max.Y - padding - line * (inline ? 1 : 2)));
        DrawClippedText(price, inline ? contentWidth - ownedWidth - 8 * scale : contentWidth, PokemonYellow);
        ImGui.SetCursorScreenPos(new Vector2(inline ? max.X - padding - ownedWidth : min.X + padding, max.Y - padding - line));
        DrawClippedText(owned, inline ? ownedWidth : contentWidth, CompanionMuted);
        draw.PopClipRect();
        ImGui.SetCursorScreenPos(after);
        if (hovered) ImGui.SetTooltip(item.Name + "\n" + item.Description);
        ImGui.PopID();
        return clicked;
    }

    private void DrawMartInspector(ShopItemDto? item, MartGrid layout, bool tokens, ref int quantity)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("MartPurchase", new Vector2(layout.DetailWidth, layout.DetailHeight), true,
            ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (item is null) { martInspectorItem = string.Empty; ImGui.TextWrapped("Select an item from the catalog to see its details."); }
        else
        {
            var line = ImGui.GetTextLineHeight(); var gap = ImGui.GetStyle().ItemSpacing.Y;
            var footerHeight = line * 4 + Math.Max(30 * scale, ImGui.GetFrameHeight()) * 2 + ImGui.GetFrameHeight() + gap * 9 + 8 * scale;
            var infoHeight = Math.Max(line * 2, ImGui.GetContentRegionAvail().Y - footerHeight - gap);
            ImGui.BeginChild("MartItemDetails", new Vector2(0, infoHeight), false);
            var inspectorItem = (tokens ? "tokens:" : "yen:") + item.Id;
            if (martInspectorItem != inspectorItem) { ImGui.SetScrollY(0); martInspectorItem = inspectorItem; }
            var art = Math.Min(72 * scale, Math.Max(40 * scale, infoHeight * 0.28f));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - art) / 2));
            DrawItemImage(item.Id, new Vector2(art));
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionAccent);
            ImGui.TextWrapped(item.Name);
            ImGui.PopStyleColor();
            ImGui.TextDisabled(BagInventory.PocketLabel(item));
            if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(item.AcquisitionNote)) ImGui.SetTooltip(item.AcquisitionNote);
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
            ImGui.Spacing(); ImGui.TextWrapped(item.Description);
            ImGui.Spacing(); ImGui.TextWrapped(MartCatalog.Usage(item));
            ImGui.PopStyleColor();
            if (item.BpPrice is int bp) ImGui.TextWrapped($"Also available for {bp:N0} BP in Battle Frontier.");
            ImGui.EndChild();
            ImGui.Spacing(); ImGui.Separator();
            var owned = InventoryCount(item.Id);
            var balance = tokens ? trainer!.PvpProfile.RankTokens?.Balance ?? 0 : trainer!.Currency;
            var currency = tokens ? "RT" : "¥";
            var capacity = item.MaxOwned is int maximum ? (int)Math.Clamp((long)maximum - owned, 0, 999) : 999;
            quantity = Math.Clamp(quantity, 1, Math.Max(1, capacity));
            DrawClippedText($"In bag: {owned:N0} / {currency} {item.Price:N0} each", ImGui.GetContentRegionAvail().X, CompanionMuted);
            ImGui.TextUnformatted("Quantity");
            ImGui.BeginDisabled(busy || capacity == 0);
            var available = ImGui.GetContentRegionAvail().X;
            var stepWidth = Math.Min(30 * scale, available / 4);
            var fieldWidth = Math.Max(1, available - stepWidth * 2 - ImGui.GetStyle().ItemSpacing.X * 2);
            ImGui.BeginDisabled(busy || quantity <= 1);
            if (ImGui.Button("-##Quantity", new Vector2(stepWidth, 0))) quantity--;
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.SetNextItemWidth(fieldWidth); ImGui.InputInt("##BuyQuantity", ref quantity, 0, 0);
            quantity = Math.Clamp(quantity, 1, Math.Max(1, capacity)); ImGui.SameLine();
            ImGui.BeginDisabled(busy || quantity >= capacity);
            if (ImGui.Button("+##Quantity", new Vector2(stepWidth, 0))) quantity++;
            ImGui.EndDisabled();
            var presetWidth = Math.Max(1, (available - ImGui.GetStyle().ItemSpacing.X * 3) / 4);
            foreach (var preset in new[] { 1, 10, 99, 999 })
            {
                if (preset > 1) ImGui.SameLine();
                ImGui.BeginDisabled(preset > capacity);
                if (DrawAccentButton(preset.ToString(), new Vector2(presetWidth, 0), quantity == preset ? CompanionAccent : CompanionMuted)) quantity = preset;
                ImGui.EndDisabled();
            }
            ImGui.EndDisabled();
            var quote = MartCatalog.Quote(item, owned, quantity, balance);
            ImGui.Spacing();
            DrawClippedText($"Total: {currency} {quote.Total:N0}", available, PokemonYellow);
            DrawClippedText(quote.AtLimit ? "Already at the ownership limit." : quote.Shortfall > 0 ? $"Need {currency} {quote.Shortfall:N0} more."
                : $"After purchase: {currency} {quote.BalanceAfter:N0}", available, quote.Shortfall > 0 ? PokemonRed : CompanionMuted);
            ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, quote.Shortfall > 0 ? 1f : ImGui.GetStyle().DisabledAlpha);
            ImGui.BeginDisabled(busy || !quote.CanBuy);
            if (DrawAccentButton(quote.AtLimit ? "Buy" : $"Buy {quote.Quantity:N0}", new Vector2(-1, Math.Max(30 * scale, ImGui.GetFrameHeight())),
                quote.Shortfall > 0 ? PokemonRed : CompanionAccent))
            {
                pendingPurchase = item; pendingPurchaseQuantity = quote.Quantity; requestPurchase = true;
                purchaseOwner = trainer!.User.Id; purchaseUnitPrice = item.Price; purchaseCurrency = item.Currency;
            }
            ImGui.EndDisabled();
            ImGui.PopStyleVar();
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }
}
