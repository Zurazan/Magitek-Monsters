using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool pokedexCaughtOnly;

    private void DrawCompanionPokedex()
    {
        if (trainer is null) return;
        DrawSectionHeader("Pokédex", $"{trainer.Pokedex.Caught:N0}/{trainer.Pokedex.Total:N0} species caught / {trainer.Pokedex.ShinyCaught:N0} shiny species");
        DrawProgressBar(trainer.Pokedex.Caught / (float)Math.Max(1, trainer.Pokedex.Total), new Vector2(-1, 6 * CompanionScale), "", ExpBarBlue);
        ImGui.Spacing();
        DrawWishlistShortcut();
        DrawCompanionDexDirectory();
        DrawCollectionWishlist();
    }

    private void DrawCompanionDexDirectory()
    {
        var scale = CompanionScale;
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - 65 * scale));
        if (ImGui.InputTextWithHint("##PokedexSearch", "Species or National Dex number...", ref pokedexSearch, 80)) pokedexPage = 0;
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear")) { pokedexSearch = string.Empty; pokedexPage = 0; }
        var filterWidth = Math.Max(1, (ImGui.GetContentRegionAvail().X - 6 * scale) / 2);
        if (DrawCompanionSegment("All entries", !pokedexCaughtOnly, filterWidth)) { pokedexCaughtOnly = false; pokedexPage = 0; }
        ImGui.SameLine(0, 6 * scale);
        if (DrawCompanionSegment("Caught", pokedexCaughtOnly, filterWidth)) { pokedexCaughtOnly = true; pokedexPage = 0; }
        ImGui.Spacing();
        var caught = trainer!.Pokedex.CaughtSpecies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Registered(PokedexEntryDto entry) => entry.Registered && caught.Contains(entry.Species);
        var source = pokedexCatalog.Count > 0 ? pokedexCatalog : trainer.Pokedex.CaughtSpecies.Select((species, index) => new PokedexEntryDto
        {
            DexNumber = index + 1, Species = species, Registered = true, Methods = ["Acquisition data is synchronizing."]
        }).ToList();
        var entries = source.Where(entry => (!pokedexCaughtOnly || Registered(entry)) && (string.IsNullOrWhiteSpace(pokedexSearch)
                || entry.DexNumber.ToString().Contains(pokedexSearch, StringComparison.OrdinalIgnoreCase)
                || Registered(entry) && entry.Species.Contains(pokedexSearch, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.DexNumber).ToList();
        var layout = CompanionLayout.Selection(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y, scale);
        var columns = Math.Clamp((int)(layout.ListWidth / (132 * scale)), 2, 6);
        var rows = Math.Clamp((int)((layout.Height - 45 * scale) / (166 * scale)), 1, 4);
        var pageSize = columns * rows;
        ImGui.BeginChild("CompanionDexGrid", new Vector2(layout.ListWidth, layout.SideBySide ? layout.Height : 410 * scale), false);
        DrawPager("Pokedex", ref pokedexPage, entries.Count, pageSize);
        if (entries.Count == 0) ImGui.TextWrapped("No entries match your search.");
        else if (ImGui.BeginTable("PokedexGrid", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var entry in entries.Skip(pokedexPage * pageSize).Take(pageSize))
            {
                ImGui.TableNextColumn();
                if (!DrawPokedexEntryCard(entry, Registered(entry), Registered(entry) && entry.Species.Equals(selectedPokedexSpecies, StringComparison.OrdinalIgnoreCase))) continue;
                if (!entry.Species.Equals(selectedPokedexSpecies, StringComparison.OrdinalIgnoreCase)) pokedexFormPage = 0;
                selectedPokedexSpecies = entry.Species;
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
        if (layout.SideBySide) ImGui.SameLine(0, 12 * scale); else ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("CompanionDexInspector", new Vector2(layout.DetailWidth, layout.SideBySide ? layout.Height : 340 * scale), false);
        var selected = source.FirstOrDefault(entry => Registered(entry) && entry.Species.Equals(selectedPokedexSpecies, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            ImGui.Text("Select a registered Pokémon");
            ImGui.TextWrapped("Catch a species to unlock its details, acquisition guide and permanent forms.");
        }
        else
        {
            var size = 112 * scale;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - size) / 2));
            DrawPokemonIcon(selected.Species, false, new Vector2(size));
            DrawCenteredCompanionText(selected.Species, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            DrawCenteredCompanionText($"#{selected.DexNumber:0000} / Registered", CompanionMuted);
            DrawWishlistPin("species", selected.Species);
            var owned = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Species.Equals(selected.Species, StringComparison.OrdinalIgnoreCase) && entry.Form.Length == 0);
            if (owned is not null) DrawStarterTypeBadges(owned.Types.ToArray(), 64 * scale, scale);
            if (ImGui.BeginTabBar("CompanionDexDetails"))
            {
                if (ImGui.BeginTabItem("How to obtain"))
                {
                    foreach (var method in selected.Methods) { ImGui.TextWrapped(method); ImGui.Spacing(); }
                    if (selected.WildLocations.Count > 0)
                    {
                        ImGui.TextColored(CompanionAccent, "Surveyed territories");
                        foreach (var location in selected.WildLocations) ImGui.TextWrapped(location);
                    }
                    ImGui.EndTabItem();
                }
                if (selected.Forms.Count > 1 && ImGui.BeginTabItem("Forms"))
                {
                    DrawPokedexForms(selected);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private void DrawCompanionItemShop(bool rankTokensOnly)
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        if (!rankTokensOnly)
        {
            ImGui.TextDisabled("Competitive rewards are in Battle Frontier > BP Exchange.");
            if (DrawAccentSmallButton("Open BP Exchange", CompanionAccent)) { openBpExchange = true; mainSection = 8; }
            ImGui.Spacing();
        }
        var categories = new (string Label, string Id)[]
        {
            ("Poké Balls", "ball"), ("Medicine", "healing"), ("Evolution", "evolution"), ("Training", "training"),
            ("Held Items", "held"), ("Key Items", "key"), ("TMs", "machine"), ("Move Tutors", "tutor")
        };
        shopCategoryIndex = Math.Clamp(shopCategoryIndex, 0, categories.Length - 1);
        if (!rankTokensOnly)
        {
            ImGui.SetNextItemWidth(Math.Min(220 * scale, ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo("Category", categories[shopCategoryIndex].Label))
            {
                for (var index = 0; index < categories.Length; index++)
                    if (ImGui.Selectable(categories[index].Label, shopCategoryIndex == index)) { shopCategoryIndex = index; shopPage = 0; }
                ImGui.EndCombo();
            }
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##ShopSearch", "Search items or descriptions...", ref shopSearch, 100)) shopPage = 0;
        ImGui.SetNextItemWidth(100 * scale);
        ImGui.InputInt("Quantity", ref shopBuyQuantity);
        shopBuyQuantity = Math.Clamp(shopBuyQuantity, 1, 999);
        var items = shop.Where(entry => (entry.Currency == "rank-tokens") == rankTokensOnly
            && (rankTokensOnly || BagInventory.Pocket(entry) == categories[shopCategoryIndex].Id)
            && (string.IsNullOrWhiteSpace(shopSearch) || entry.Name.Contains(shopSearch, StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains(shopSearch, StringComparison.OrdinalIgnoreCase))).ToList();
        DrawPager("Shop", ref shopPage, items.Count, 8);
        ImGui.Spacing();
        foreach (var item in items.Skip(shopPage * 8).Take(8))
        {
            var owned = InventoryCount(item.Id);
            var quantity = item.MaxOwned.HasValue ? Math.Min(shopBuyQuantity, Math.Max(0, item.MaxOwned.Value - owned)) : shopBuyQuantity;
            var price = (long)item.Price * quantity;
            var balance = rankTokensOnly ? trainer.PvpProfile.RankTokens?.Balance ?? 0 : trainer.Currency;
            var currency = rankTokensOnly ? "RT" : "¥";
            ImGui.PushID(item.Id);
            if (ImGui.BeginTable("ShopRow", 3, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Art", ImGuiTableColumnFlags.WidthFixed, 42 * scale);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Purchase", ImGuiTableColumnFlags.WidthFixed, 110 * scale);
                ImGui.TableNextColumn(); DrawItemImage(item.Id, new Vector2(36 * scale));
                ImGui.TableNextColumn();
                ImGui.TextWrapped(item.Name);
                if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(item.AcquisitionNote))
                    ImGui.SetTooltip(item.AcquisitionNote + (item.BpPrice is int bpPrice ? $" Also available for {bpPrice:N0} BP." : string.Empty));
                ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
                ImGui.TextWrapped(item.Description);
                ImGui.PopStyleColor();
                ImGui.TextDisabled($"Owned: {owned} / {currency} {item.Price:N0} each");
                ImGui.TableNextColumn();
                ImGui.TextWrapped($"{currency} {price:N0}");
                ImGui.BeginDisabled(busy || quantity == 0 || balance < price);
                if (DrawAccentButton(quantity == 0 ? "Owned" : $"Buy x{quantity}", new Vector2(-1, 32 * scale), balance < price ? PokemonRed : CompanionAccent))
                {
                    pendingPurchase = item;
                    pendingPurchaseQuantity = quantity;
                    requestPurchase = true;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(quantity == 0 ? "You already own the maximum quantity." : balance < price ? $"You need {currency} {price - balance:N0} more." : $"Buy {quantity} for {currency} {price:N0}.");
                ImGui.EndTable();
            }
            ImGui.PopID();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        if (items.Count == 0) ImGui.TextWrapped("No items match your search.");
        DrawPurchaseConfirmation();
    }

    private void DrawPurchaseConfirmation()
    {
        if (requestPurchase) { ImGui.OpenPopup("Confirm purchase"); requestPurchase = false; }
        if (!BeginCenteredModal("Confirm purchase", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var item = pendingPurchase;
        if (item is null || trainer is null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        var price = (long)item.Price * pendingPurchaseQuantity;
        var rankTokens = item.Currency == "rank-tokens";
        var currency = rankTokens ? "RT" : "¥";
        var balance = rankTokens ? trainer.PvpProfile.RankTokens?.Balance ?? 0 : trainer.Currency;
        ImGui.TextWrapped($"Buy {pendingPurchaseQuantity} x {item.Name} for {currency} {price:N0}?");
        ImGui.TextDisabled($"Balance after purchase: {currency} {Math.Max(0, balance - price):N0}");
        ImGui.BeginDisabled(busy || balance < price || item.MaxOwned.HasValue && InventoryCount(item.Id) + pendingPurchaseQuantity > item.MaxOwned.Value);
        if (DrawAccentButton("Confirm purchase", Vector2.Zero, CompanionAccent))
        {
            var quantity = pendingPurchaseQuantity;
            ImGui.CloseCurrentPopup();
            Run(() => BuyItem(item.Id, quantity), $"Buying {item.Name}...");
        }
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
