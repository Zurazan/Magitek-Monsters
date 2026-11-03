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
        var directory = GetDexDirectory();
        var source = directory.Source; var entries = directory.Entries;
        bool Registered(PokedexEntryDto entry) => directory.Registered(entry);
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
            var owned = trainer!.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Species.Equals(selected.Species, StringComparison.OrdinalIgnoreCase) && entry.Form.Length == 0);
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

    private void DrawPurchaseConfirmation()
    {
        if (requestPurchase) { ImGui.OpenPopup("Confirm purchase"); requestPurchase = false; }
        if (!BeginConfirmationModal("Confirm purchase")) return;
        var item = pendingPurchase;
        var current = item is null ? null : shop.FirstOrDefault(entry => entry.Id == item.Id);
        if (item is null || trainer is null || purchaseOwner != trainer.User.Id)
        { pendingPurchase = null; ImGui.CloseCurrentPopup(); EndConfirmationModal(); return; }
        if (current is null || current.Price != purchaseUnitPrice || current.Currency != purchaseCurrency)
        {
            ImGui.TextWrapped("This offer changed while the confirmation was open. Review the item and price before buying again.");
            DrawConfirmationActions(null, CompanionMuted, cancelLabel: "Back to shop");
            EndConfirmationModal(); return;
        }
        var price = (long)item.Price * pendingPurchaseQuantity;
        var rankTokens = item.Currency == "rank-tokens";
        var currency = rankTokens ? "RT" : "¥";
        var balance = rankTokens ? trainer.PvpProfile.RankTokens?.Balance ?? 0 : trainer.Currency;
        var quote = MartCatalog.Quote(current, InventoryCount(item.Id), pendingPurchaseQuantity, balance);
        ImGui.TextWrapped($"Buy {pendingPurchaseQuantity} x {item.Name} for {currency} {price:N0}?");
        ImGui.TextDisabled($"Balance after purchase: {currency} {Math.Max(0, balance - price):N0}");
        if (DrawConfirmationActions("Confirm purchase", CompanionAccent,
            busy || !quote.CanBuy || quote.Quantity != pendingPurchaseQuantity))
        {
            var quantity = pendingPurchaseQuantity;
            ImGui.CloseCurrentPopup();
            Run(() => BuyItem(item.Id, quantity), $"Buying {item.Name}...");
        }
        EndConfirmationModal();
    }
}
