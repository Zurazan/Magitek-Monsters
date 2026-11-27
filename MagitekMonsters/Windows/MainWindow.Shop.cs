using System;
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
    private bool shopWasOpen;
    private bool shopRefreshRequested;
    private DateTimeOffset nextShopRefreshAt;
    private sealed record ShopPurchaseAttempt(string Owner, ShopPurchaseRequest Request);
    private ShopPurchaseAttempt? pendingShopPurchase;

    private Task BuyItem(string itemId, int quantity = 1)
    {
        if (trainer is null) return Task.CompletedTask;
        if (pendingShopPurchase is not null)
        {
            SetStatus("Confirm the pending purchase in Poké Mart first.", true);
            return Task.CompletedTask;
        }
        var item = shop?.FirstOrDefault(entry => entry.Id == itemId);
        var currency = item is null ? null : item.Currency.Length == 0 ? "yen" : item.Currency;
        return SendShopPurchase(new(trainer.User.Id, new(itemId, quantity, Guid.NewGuid().ToString(), item?.Price, currency)));
    }

    private async Task SendShopPurchase(ShopPurchaseAttempt attempt)
    {
        if (trainer?.User.Id != attempt.Owner) return;
        try { trainer = await InSession(plugin.Api.BuyItem(attempt.Request, disposeCancellation.Token)); }
        catch (UnconfirmedActionException)
        {
            pendingShopPurchase = attempt;
            throw;
        }
        catch (ApiException error) when (error.StatusCode is 400 or 403 or 404 or 409)
        {
            pendingShopPurchase = null;
            if (error.Code == "shop_price_changed") shopRefreshRequested = true;
            throw;
        }
        pendingShopPurchase = null;
        SetStatus("Purchase complete.");
    }

    private void DrawPendingShopPurchase()
    {
        if (pendingShopPurchase is not { } attempt || trainer?.User.Id != attempt.Owner) return;
        var item = shop.FirstOrDefault(entry => entry.Id == attempt.Request.ItemId)?.Name ?? attempt.Request.ItemId;
        ImGui.TextWrapped($"Purchase unconfirmed: {attempt.Request.Quantity:N0} × {item}.");
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton("Retry same purchase", Vector2.Zero, CompanionAccent))
            Run(() => SendShopPurchase(attempt), "Checking purchase...");
        ImGui.EndDisabled();
    }

    private void ObserveShopVisibility()
    {
        var open = IsOpen && trainer is not null && mainSection == 5;
        if (open && !shopWasOpen && DateTimeOffset.UtcNow >= nextShopRefreshAt)
            shopRefreshRequested = true;
        shopWasOpen = open;
    }

    private DateTimeOffset shopRetryAfter;

    private async Task RefreshShop()
    {
        shopRefreshRequested = false;
        if (ApplyShopCatalog(await InSession(plugin.Api.GetShop(disposeCancellation.Token))))
            SetStatus("Shop refreshed.", quiet: true);
    }

    private void BackgroundShop()
    {
        shopRefreshRequested = false;
        RunBackground(async token =>
        {
            var catalog = await plugin.Api.GetShop(token);
            return () => ApplyShopCatalog(catalog);
        }, () =>
        {
            shopRefreshRequested = true;
            shopRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
        }, sharedBackoff: false, deferLiveUpdates: false);
    }

    private bool ApplyShopCatalog(ShopCatalogDto catalog)
    {
        var applied = plugin.Api.Catalogs.TryApply([catalog], () =>
        {
            shop = catalog.Items;
            itemCatalog = catalog.InventoryItems.Count > 0 ? catalog.InventoryItems : shop;
            trainingItems = catalog.TrainingItems.Count > 0 ? catalog.TrainingItems : shop.Where(item => item.TrainingEffect is not null).ToList();
            nextShopRefreshAt = DateTimeOffset.UtcNow.AddSeconds(30);
            shopRetryAfter = DateTimeOffset.MinValue;
            if (pendingPurchase is { } purchase)
                pendingPurchase = shop.FirstOrDefault(item => item.Id == purchase.Id);
        });
        if (!applied) RetryCatalogs();
        return applied;
    }
}
