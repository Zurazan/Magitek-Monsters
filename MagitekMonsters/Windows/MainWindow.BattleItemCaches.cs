using System;
using System.Collections.Generic;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly string[] BattlePockets = ["Healing", "Status", "PP"];
    private List<ShopItemDto>? battleItemCatalog;
    private List<InventoryEntryDto>? battleItemInventory;
    private List<ShopItemDto> battleMedicine = [], battleHealing = [], battleStatus = [], battlePp = [], battleBalls = [], battleHeld = [];

    private void EnsureBattleItems()
    {
        var inventory = trainer?.Inventory;
        if (ReferenceEquals(battleItemCatalog, itemCatalog) && ReferenceEquals(battleItemInventory, inventory)) return;
        battleItemCatalog = itemCatalog; battleItemInventory = inventory;
        battleMedicine = []; battleHealing = []; battleStatus = []; battlePp = []; battleBalls = []; battleHeld = [];
        if (inventory is null) return;
        foreach (var item in itemCatalog)
        {
            if (InventoryCount(item.Id) <= 0) continue;
            if (item.Category == "ball") battleBalls.Add(item);
            if (item.Category == "held" || item.CanBeHeld) battleHeld.Add(item);
            if (item.Category != "healing" || string.IsNullOrWhiteSpace(item.BattleEffect)) continue;
            battleMedicine.Add(item);
            switch (BattleBag.Pocket(item))
            {
                case "Status": battleStatus.Add(item); break;
                case "PP": battlePp.Add(item); break;
                default: battleHealing.Add(item); break;
            }
        }
    }
    private List<ShopItemDto> GetBattleMedicine() { EnsureBattleItems(); return battleMedicine; }
    private List<ShopItemDto> GetBattlePocket(string pocket)
    {
        EnsureBattleItems();
        return pocket switch { "Status" => battleStatus, "PP" => battlePp, _ => battleHealing };
    }
    private List<ShopItemDto> GetBattleBalls() { EnsureBattleItems(); return battleBalls; }
    private List<ShopItemDto> GetHeldItems() { EnsureBattleItems(); return battleHeld; }
    private void ResetBattleItemCaches()
    {
        battleItemCatalog = null; battleItemInventory = null;
        battleMedicine = []; battleHealing = []; battleStatus = []; battlePp = []; battleBalls = []; battleHeld = [];
        inventoryCountSource = null; inventoryCounts = new(StringComparer.Ordinal);
    }
}
