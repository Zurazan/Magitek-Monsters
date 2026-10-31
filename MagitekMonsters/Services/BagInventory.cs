using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public enum BagAction { None, Medicine, Catch, Training, Evolution, HeldItem, Moves, Passive }
public sealed record BagEntry(ShopItemDto Item, int Quantity, bool Known);

public static class BagInventory
{
    public static readonly (string Label, string Id)[] Pockets =
    [
        ("All items", ""), ("Poké Balls", "ball"), ("Medicine", "healing"), ("Evolution", "evolution"),
        ("Training", "training"), ("Held items", "held"), ("Key items", "key"),
        ("TMs", "machine"), ("Move tutors", "tutor"), ("Other", "other")
    ];

    public static string Pocket(ShopItemDto item) => item.Id == "gimmighoulcoin" ? "evolution"
        : Pockets.Any(pocket => pocket.Id.Length > 0 && pocket.Id == item.Category) ? item.Category : "other";

    public static string PocketLabel(ShopItemDto item) => Pockets.First(pocket => pocket.Id == Pocket(item)).Label;

    public static List<BagEntry> Owned(IEnumerable<InventoryEntryDto> inventory, IEnumerable<ShopItemDto> catalog, IEnumerable<CollectionRewardDto>? rewards = null)
    {
        var rewardItems = (rewards ?? []).Where(reward => reward.Kind == "item").Select(reward => new ShopItemDto
        {
            Id = reward.Id, Name = reward.Name, Description = reward.Description, Category = "key",
            AcquisitionNote = $"Collection Exchange reward: {reward.Points:N0} CP."
        });
        var definitions = catalog.Concat(rewardItems).GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return inventory.Where(entry => entry.Quantity > 0 && !string.IsNullOrWhiteSpace(entry.ItemId)).Select(entry =>
        {
            var known = definitions.TryGetValue(entry.ItemId, out var item);
            return new BagEntry(item ?? new ShopItemDto
            {
                Id = entry.ItemId, Name = entry.ItemId, Category = "other",
                Description = "Item details are unavailable. Your owned quantity is still shown."
            }, entry.Quantity, known);
        }).OrderBy(entry => entry.Item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Item.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<BagEntry> Filter(IEnumerable<BagEntry> entries, string pocket, string search)
    {
        search = search.Trim();
        return entries.Where(entry => (pocket.Length == 0 || Pocket(entry.Item) == pocket)
            && (search.Length == 0 || entry.Item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Item.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Item.Description.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static BagAction Action(ShopItemDto item)
    {
        if (item.Id is "expshare" or "shinycharm") return BagAction.Passive;
        if (item.Id is "rotomcatalog" or "meteorite" or "revealglass" or "gracidea" or "prisonbottle"
            or "zygardecube" or "dnasplicers" or "nsolarizer" or "nlunarizer" or "reinsofunity") return BagAction.Evolution;
        return Pocket(item) switch
        {
            "ball" => BagAction.Catch, "healing" => BagAction.Medicine, "evolution" => BagAction.Evolution,
            "training" => BagAction.Training, "held" => BagAction.HeldItem,
            "machine" or "tutor" => BagAction.Moves, _ => BagAction.None
        };
    }
}
