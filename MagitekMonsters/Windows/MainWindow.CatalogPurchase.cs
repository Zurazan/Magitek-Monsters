using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly int[] MartQuantities = [1, 10, 99, 999];
    private static readonly int[] FrontierQuantities = [1, 10, 50, 99];
    private readonly record struct CatalogCost(int UnitPrice, int? MaxOwned, int Owned, long Balance, string Currency);

    private static string CatalogPrice(string currency, long value) => currency == "BP" ? $"{value:N0} BP" : $"{currency} {value:N0}";

    private static float CatalogActionHeight => Math.Max(30 * CompanionScale, ImGui.GetFrameHeight());

    private static float CatalogFooterHeight(bool quantities) => quantities
        ? ImGui.GetTextLineHeight() * 4 + CatalogActionHeight * 2 + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 9 + 8 * CompanionScale
        : ImGui.GetTextLineHeightWithSpacing() * 2 + CatalogActionHeight + ImGui.GetStyle().ItemSpacing.Y * 4 + 8 * CompanionScale;

    private static void DrawItemCatalogDetails(ShopItemDto item, string description, string? alternatePrice)
    {
        ImGui.TextDisabled(BagInventory.PocketLabel(item));
        if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(item.AcquisitionNote)) ImGui.SetTooltip(item.AcquisitionNote);
        ImGui.Spacing();
        DrawGoalText(description, CompanionMuted);
        ImGui.Spacing();
        DrawGoalText(MartCatalog.Usage(item), CompanionMuted);
        if (alternatePrice is not null) ImGui.TextWrapped(alternatePrice);
    }

    private static int? DrawCatalogPurchase(CatalogCost cost, int[] presets, ref int quantity, bool disabled)
    {
        var limit = presets[^1];
        var capacity = MartCatalog.Capacity(cost.MaxOwned, cost.Owned, limit);
        quantity = Math.Clamp(quantity, 1, Math.Max(1, capacity));
        var available = ImGui.GetContentRegionAvail().X;
        DrawClippedText($"In bag: {cost.Owned:N0} / {CatalogPrice(cost.Currency, cost.UnitPrice)} each", available, CompanionMuted);
        DrawCatalogQuantity(presets, capacity, ref quantity, disabled || capacity == 0, available);
        var quote = MartCatalog.Quote(cost.UnitPrice, cost.MaxOwned, cost.Owned, quantity, cost.Balance, limit);
        ImGui.Spacing();
        DrawClippedText($"Total: {CatalogPrice(cost.Currency, quote.Total)}", available, PokemonYellow);
        DrawCatalogBalance(cost.Currency, quote, available);
        return DrawCatalogBuyButton(quote.AtLimit ? "Buy" : $"Buy {quote.Quantity:N0}", quote, disabled) ? quote.Quantity : null;
    }

    private static void DrawCatalogQuantity(int[] presets, int capacity, ref int quantity, bool disabled, float available)
    {
        ImGui.TextUnformatted("Quantity");
        ImGui.BeginDisabled(disabled);
        var stepWidth = Math.Min(30 * CompanionScale, available / 4);
        var fieldWidth = Math.Max(1, available - stepWidth * 2 - ImGui.GetStyle().ItemSpacing.X * 2);
        ImGui.BeginDisabled(quantity <= 1);
        if (ImGui.Button("-##Quantity", new Vector2(stepWidth, 0))) quantity--;
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputInt("##BuyQuantity", ref quantity, 0, 0);
        quantity = Math.Clamp(quantity, 1, Math.Max(1, capacity));
        ImGui.SameLine();
        ImGui.BeginDisabled(quantity >= capacity);
        if (ImGui.Button("+##Quantity", new Vector2(stepWidth, 0))) quantity++;
        ImGui.EndDisabled();
        var presetWidth = Math.Max(1, (available - ImGui.GetStyle().ItemSpacing.X * (presets.Length - 1)) / presets.Length);
        foreach (var preset in presets)
        {
            if (preset != presets[0]) ImGui.SameLine();
            ImGui.BeginDisabled(preset > capacity);
            if (DrawAccentButton(preset.ToString(), new Vector2(presetWidth, 0), quantity == preset ? CompanionAccent : CompanionMuted)) quantity = preset;
            ImGui.EndDisabled();
        }
        ImGui.EndDisabled();
    }

    private static void DrawCatalogBalance(string currency, MartQuote quote, float width) =>
        DrawClippedText(quote.AtLimit ? "Already at the ownership limit." : quote.Shortfall > 0 ? $"Need {CatalogPrice(currency, quote.Shortfall)} more."
            : $"After purchase: {CatalogPrice(currency, quote.BalanceAfter)}", width, quote.Shortfall > 0 ? PokemonRed : CompanionMuted);

    private static bool DrawCatalogBuyButton(string label, MartQuote quote, bool disabled)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, quote.Shortfall > 0 ? 1f : ImGui.GetStyle().DisabledAlpha);
        ImGui.BeginDisabled(disabled || !quote.CanBuy);
        var pressed = DrawAccentButton(label, new Vector2(-1, CatalogActionHeight), quote.Shortfall > 0 ? PokemonRed : CompanionAccent);
        ImGui.EndDisabled();
        ImGui.PopStyleVar();
        return pressed;
    }
}
