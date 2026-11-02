using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public readonly record struct MartQuote(int Quantity, long Total, long BalanceAfter, long Shortfall, bool AtLimit, bool CanBuy);
public readonly record struct MartGrid(bool SideBySide, float BrowseWidth, float DetailWidth, float BrowseHeight, float DetailHeight,
    int Columns, int Rows, float CardHeight, float PagerHeight, int PageSize);

public static class MartCatalog
{
    public static string Usage(ShopItemDto item) => BagInventory.Action(item) switch
    {
        BagAction.Passive => "Active while owned. No need to equip or use it.",
        BagAction.HeldItem => "Equip from your Pokémon's Held item menu. Online format restrictions apply.",
        BagAction.Catch => item.GuaranteedCapture ? "Use from Catch during wild battles. Sanctuary unlocks and boss victories are still required."
            : "Use from Catch during wild battles.",
        BagAction.Medicine => "PvE only. Use from Bag during battle, subject to the challenge's medicine rules.",
        BagAction.Evolution => "Check a compatible Pokémon's Evolution or Forms menu.",
        BagAction.Training => "Choose a Pokémon in Party or PC storage to use this item.",
        BagAction.Moves => "Reusable move licence. Teach compatible Pokémon from their Moves menu.",
        _ => "Check the item's description for compatible Pokémon."
    };

    public static List<ShopItemDto> Filter(IEnumerable<ShopItemDto> items, bool tokens, string category, string search)
    {
        var words = search.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return items.Where(item => !item.BpOnly && (tokens ? item.Currency == "rank-tokens" : item.Currency is "yen" or "")
            && (tokens || category.Length == 0 || BagInventory.Pocket(item) == category)
            && words.All(word => item.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(word, StringComparison.OrdinalIgnoreCase)
                || (item.Move?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
    }

    public static MartQuote Quote(ShopItemDto item, int owned, int requested, long balance)
    {
        var remaining = item.MaxOwned is int maximum ? Math.Clamp((long)maximum - Math.Max(0, owned), 0, 999) : 999;
        var quantity = (int)Math.Min(Math.Clamp(requested, 1, 999), remaining);
        var total = (long)Math.Max(0, item.Price) * quantity;
        var funds = Math.Max(0, balance);
        return new(quantity, total, Math.Max(0, funds - total), Math.Max(0, total - funds), remaining == 0,
            quantity > 0 && item.Price >= 0 && total <= funds);
    }

    public static MartGrid Layout(float width, float height, float scale, float line, float gap)
    {
        width = Math.Max(1, width); height = Math.Max(1, height); scale = Math.Max(0.5f, scale);
        var side = width >= 620 * scale;
        var detail = side ? Math.Clamp(width * 0.32f, 236 * scale, 290 * scale) : width;
        var browse = side ? width - detail - 12 * scale : width;
        var card = Math.Max(108 * scale, 44 * scale + 3 * line + 14 * scale);
        var columns = Math.Clamp((int)((browse + gap) / (148 * scale + gap)), 1, 4);
        var available = side ? Math.Max(345 * scale, height) : Math.Min(Math.Max(height * 0.45f, card + 50 * scale), 320 * scale);
        var pager = Math.Max(64 * scale, 2 * line + 3 * gap + 12 * scale);
        var rows = Math.Clamp((int)((available - pager + gap) / (card + gap)), 1, 4);
        var browseHeight = rows * (card + gap) - gap + pager;
        var detailHeight = side ? Math.Max(345 * scale, height) : 440 * scale;
        return new(side, browse, detail, browseHeight, detailHeight, columns, rows, card, pager, columns * rows);
    }
}
