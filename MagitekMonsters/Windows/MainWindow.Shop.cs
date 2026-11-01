using System;
using System.Linq;
using System.Threading.Tasks;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool shopWasOpen;
    private bool shopRefreshRequested;
    private DateTimeOffset nextShopRefreshAt;

    private void ObserveShopVisibility()
    {
        var open = IsOpen && trainer is not null && mainSection == 5;
        if (open && !shopWasOpen && DateTimeOffset.UtcNow >= nextShopRefreshAt)
            shopRefreshRequested = true;
        shopWasOpen = open;
    }

    private async Task RefreshShop()
    {
        shopRefreshRequested = false;
        nextShopRefreshAt = DateTimeOffset.UtcNow.AddSeconds(30);
        ApplyShopCatalog(await InSession(plugin.Api.GetShop(disposeCancellation.Token)));
        SetStatus("Shop refreshed.", quiet: true);
    }

    private void ApplyShopCatalog(ShopCatalogDto catalog)
    {
        shop = catalog.Items;
        itemCatalog = catalog.InventoryItems.Count > 0 ? catalog.InventoryItems : shop;
        trainingItems = catalog.TrainingItems.Count > 0 ? catalog.TrainingItems : shop.Where(item => item.TrainingEffect is not null).ToList();
        nextShopRefreshAt = DateTimeOffset.UtcNow.AddSeconds(30);
        if (pendingPurchase is { } purchase)
            pendingPurchase = shop.FirstOrDefault(item => item.Id == purchase.Id);
    }
}
