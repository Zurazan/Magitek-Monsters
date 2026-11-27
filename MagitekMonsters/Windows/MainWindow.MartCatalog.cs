using System;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly (string Label, string Id)[] MartCategories = BagInventory.Pockets[..^1];
    private ShopCatalogBrowser<ShopItemDto>? moneyCatalog, tokenCatalog;
    private ShopCatalogBrowser<ShopItemDto> MoneyCatalog => moneyCatalog ??= new(item => item.Id, DrawCatalogArtwork);
    private ShopCatalogBrowser<ShopItemDto> TokenCatalog => tokenCatalog ??= new(item => item.Id, DrawCatalogArtwork);
    private string purchaseOwner = string.Empty;
    private int purchaseUnitPrice;
    private string purchaseCurrency = string.Empty;
    private int shopExchange;
    private bool shopCompact;
    private static readonly string[] ShopExchanges = ["Items", "Rank Token Exchange", "Prize Exchange", "Collection Exchange"];

    private void ResetMartCatalog()
    {
        moneyCatalog = tokenCatalog = null;
        pendingPurchase = null; requestPurchase = false; purchaseOwner = purchaseCurrency = string.Empty;
        pendingShopPurchase = null;
        shopExchange = 0; shopCompact = false;
        prizeExchange = collectionExchange = null;
        pendingExchangePurchase = null;
        pendingPrizePurchase = null;
        requestExchangePurchase = false;
        openCollectionExchange = false;
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
        if (shopExchange == 0) DrawCompanionItemShop(rankTokensOnly: false);
        else if (shopExchange == 1) DrawCompanionItemShop(true);
        else if (shopExchange == 2) DrawPrizeExchange();
        else DrawCollectionExchange();
        DrawPurchaseConfirmation();
    }

    private void DrawCompanionItemShop(bool rankTokensOnly)
    {
        if (trainer is not { } current) return;
        var browser = rankTokensOnly ? TokenCatalog : MoneyCatalog;
        var currency = rankTokensOnly ? "RT" : "¥";
        var balance = rankTokensOnly ? current.PvpProfile.RankTokens?.Balance ?? 0 : current.Currency;
        ImGui.PushID(rankTokensOnly ? "TokenCatalog" : "MoneyCatalog");
        if (!rankTokensOnly)
        {
            if (DrawAccentSmallButton("BP Exchange", CompanionMuted)) OpenFrontier(exchange: true);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Competitive rewards are in Battle Frontier > BP Exchange.");
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Refresh shop", CompanionMuted)) Run(RefreshShop, "Refreshing shop...");
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (!rankTokensOnly) browser.DrawCategories(MartCategories);
        browser.DrawSearch("Search items, descriptions or moves...");
        var items = GetMartItems(rankTokensOnly, browser.Category, browser.Search);
        browser.Draw(items, "items", Tile, Details, Buy, CatalogFooterHeight(quantities: true));
        ImGui.PopID();

        CatalogTile Tile(ShopItemDto item) => new(item.Name, item.Description, item.Id, null, CatalogPrice(currency, item.Price),
            $"Bag {InventoryCount(item.Id):N0}", CompanionMuted);

        static void Details(ShopItemDto item) => DrawItemCatalogDetails(item, item.Description,
            item.BpPrice is int bp ? $"Also available for {bp:N0} BP in Battle Frontier." : null);

        void Buy(ShopItemDto item)
        {
            var cost = new CatalogCost(item.Price, item.MaxOwned, InventoryCount(item.Id), balance, currency);
            if (DrawCatalogPurchase(cost, MartQuantities, ref browser.Quantity, busy || pendingShopPurchase is not null) is not int quantity) return;
            pendingPurchase = item;
            pendingPurchaseQuantity = quantity;
            requestPurchase = true;
            purchaseOwner = current.User.Id;
            purchaseUnitPrice = item.Price;
            purchaseCurrency = item.Currency;
        }
    }
}
