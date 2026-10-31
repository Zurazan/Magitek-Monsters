using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string bagSearch = string.Empty;
    private int bagPocket;
    private int bagPage;
    private string? selectedBagItem;
    private string? bagPokemonId;
    private string? requestedManagementTab;
    private string? requestedShopTab;

    private void DrawBag()
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        var owned = BagInventory.Owned(trainer.Inventory, itemCatalog, collectionRewards);
        DrawSectionHeader("Bag", $"{owned.Count:N0} item types / {owned.Sum(entry => (long)entry.Quantity):N0} items owned");
        bagPocket = Math.Clamp(bagPocket, 0, BagInventory.Pockets.Length - 1);
        ImGui.SetNextItemWidth(Math.Min(240 * scale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Pocket", BagInventory.Pockets[bagPocket].Label))
        {
            for (var index = 0; index < BagInventory.Pockets.Length; index++)
            {
                var pocket = BagInventory.Pockets[index];
                var count = owned.Count(entry => pocket.Id.Length == 0 || BagInventory.Pocket(entry.Item) == pocket.Id);
                if (ImGui.Selectable($"{pocket.Label} ({count})", bagPocket == index)) { bagPocket = index; bagPage = 0; }
            }
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##BagSearch", "Search owned items or descriptions...", ref bagSearch, 100)) bagPage = 0;
        var entries = BagInventory.Filter(owned, BagInventory.Pockets[bagPocket].Id, bagSearch);
        if (!entries.Any(entry => entry.Item.Id == selectedBagItem)) selectedBagItem = entries.FirstOrDefault()?.Item.Id;
        ImGui.Spacing();
        var layout = CompanionLayout.Selection(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y, scale);
        var listHeight = layout.SideBySide ? layout.Height : Math.Min(340 * scale, Math.Max(120 * scale, layout.Height * 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("BagItems", new Vector2(layout.ListWidth, listHeight), false);
        DrawPager("Bag", ref bagPage, entries.Count, 12);
        if (entries.Count == 0) ImGui.TextWrapped(owned.Count == 0 ? "Your Bag is empty. Visit the Poké Mart or earn rewards to stock up." : "No owned items match this pocket and search.");
        foreach (var entry in entries.Skip(bagPage * 12).Take(12))
            if (DrawBagItemRow(entry, entry.Item.Id == selectedBagItem)) selectedBagItem = entry.Item.Id;
        ImGui.EndChild();
        ImGui.PopStyleVar();
        if (layout.SideBySide) ImGui.SameLine(0, 12 * scale); else ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("BagDetails", new Vector2(layout.DetailWidth, layout.SideBySide ? layout.Height : 360 * scale), false);
        var selected = entries.FirstOrDefault(entry => entry.Item.Id == selectedBagItem);
        if (selected is null) ImGui.TextWrapped("Select an owned item to see its details and where to use it.");
        else DrawBagDetails(selected);
        ImGui.EndChild();
        ImGui.PopStyleColor(); ImGui.PopStyleVar();
        DrawPokemonManagement();
    }

    private bool DrawBagItemRow(BagEntry entry, bool selected)
    {
        var scale = CompanionScale;
        ImGui.PushID(entry.Item.Id);
        var min = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = Math.Max(70 * scale, ImGui.GetTextLineHeight() * 2 + 26 * scale);
        var clicked = ImGui.InvisibleButton("SelectBagItem", new Vector2(width, height));
        var hover = ImGui.IsItemHovered();
        if (hover) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.17f, 0.22f, 1) : CompanionSurface), 4 * scale);
        if (selected || hover) draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(CompanionAccent with { W = selected ? 0.65f : 0.4f }), 4 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(12, 14) * scale);
        DrawItemImage(entry.Item.Id, new Vector2(40 * scale));
        var quantity = $"x{entry.Quantity:N0}";
        var quantityWidth = ImGui.CalcTextSize(quantity).X;
        ImGui.SetCursorScreenPos(min + new Vector2(64, 12) * scale);
        DrawClippedText(entry.Item.Name, Math.Max(1, width - 84 * scale - quantityWidth), ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SetCursorScreenPos(min + new Vector2(64, 36) * scale);
        DrawClippedText(BagInventory.PocketLabel(entry.Item), Math.Max(1, width - 76 * scale), CompanionMuted);
        ImGui.SetCursorScreenPos(new Vector2(min.X + width - quantityWidth - 12 * scale, min.Y + 12 * scale));
        ImGui.TextColored(CompanionAccent, quantity);
        ImGui.SetCursorScreenPos(after);
        ImGui.PopID();
        return clicked;
    }

    private void DrawBagDetails(BagEntry entry)
    {
        var item = entry.Item; var scale = CompanionScale;
        DrawItemImage(item.Id, new Vector2(64 * scale));
        ImGui.TextWrapped(item.Name);
        ImGui.TextColored(CompanionAccent, $"Owned: {entry.Quantity:N0}");
        ImGui.TextDisabled(BagInventory.PocketLabel(item));
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextWrapped(item.Description);
        if (item.AcquisitionNote.Length > 0)
        {
            ImGui.Spacing(); ImGui.TextColored(CompanionMuted, "How to obtain");
            ImGui.TextWrapped(item.AcquisitionNote);
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var action = BagInventory.Action(item);
        if (!entry.Known) { ImGui.TextWrapped("Item details will return when the catalog is available."); return; }
        if (action == BagAction.Passive)
        {
            ImGui.TextColored(PokemonGreen, "Active while owned");
            ImGui.TextWrapped("This key item works automatically. It does not need to be used or equipped.");
        }
        else if (action is BagAction.Medicine or BagAction.Catch)
        {
            var catchItem = action == BagAction.Catch;
            ImGui.TextWrapped(catchItem ? "Use from Catch during a wild battle. Boss capture opportunities still require a victory first." : "Use from Bag during PvE battles, subject to the challenge's medicine rules. Outside combat, the Pokémon Center in Party heals, revives and restores PP for free.");
            var available = battle is { Status: "active" } && (!catchItem || battle.Kind == "wild");
            ImGui.BeginDisabled(busy || !available);
            if (DrawAccentButton(catchItem ? "Open battle Catch" : "Open battle Bag", new Vector2(-1, 32 * scale), CompanionAccent)) OpenBattleFromBag(item, catchItem);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(available ? "Open the battle menu with this item selected." : catchItem ? "Start or resume a wild battle first." : "Start or resume a PvE battle first. Bag items cannot be used in PvP.");
        }
        else if (action != BagAction.None)
        {
            ImGui.TextWrapped(action switch
            {
                BagAction.Evolution => "Choose a Pokémon, then check its evolution or form requirements. The item is only consumed when you confirm a valid action.",
                BagAction.HeldItem => "Choose a Pokémon and give it a held item. Held items can be used in PvE and PvP when the format permits them.",
                BagAction.Moves => "Choose a Pokémon and open its move licenses. Compatibility and replacement choices are checked there.",
                _ => "Choose a Pokémon and open training. Stat limits and required quantities are checked before using an item."
            });
            DrawBagPokemonActions(item, action);
        }
        else ImGui.TextWrapped("This item has no direct Bag action.");
        ImGui.Spacing();
        var collectionItem = collectionRewards.Any(reward => reward.Kind == "item" && reward.Id == item.Id);
        var shopLabel = collectionItem ? "Open Collection Exchange" : item.BpOnly ? "Open BP Exchange" : item.Currency == "rank-tokens" ? "Open Rank Token Exchange" : "Open Poké Mart";
        if (DrawAccentButton(shopLabel, new Vector2(-1, 30 * scale), CompanionAccent))
        {
            if (item.BpOnly) { openBpExchange = true; mainSection = 8; }
            else if (collectionItem) { openCollectionExchange = true; mainSection = 5; }
            else
            {
                requestedShopTab = item.Currency == "rank-tokens" ? "Rank Token Exchange" : "Items";
                shopCategoryIndex = Math.Clamp(Array.FindIndex(BagInventory.Pockets, pocket => pocket.Id == BagInventory.Pocket(item)) - 1, 0, 7);
                shopSearch = item.Name; shopPage = 0; mainSection = 5;
            }
        }
    }

    private void DrawBagPokemonActions(ShopItemDto item, BagAction action)
    {
        var party = trainer!.Party.OrderBy(pokemon => pokemon.PartySlot).ToList();
        if (!party.Any(pokemon => pokemon.Id == bagPokemonId)) bagPokemonId = party.FirstOrDefault()?.Id;
        var pokemon = party.FirstOrDefault(pokemon => pokemon.Id == bagPokemonId);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##BagPokemon", pokemon is null ? "No party Pokémon" : $"{pokemon.Nickname} / Lv. {pokemon.Level}"))
        {
            foreach (var option in party)
                if (ImGui.Selectable($"{option.Nickname} / Lv. {option.Level}##{option.Id}", option.Id == bagPokemonId)) bagPokemonId = option.Id;
            ImGui.EndCombo();
        }
        pokemon = party.FirstOrDefault(pokemon => pokemon.Id == bagPokemonId);
        ImGui.BeginDisabled(CollectionLocked || pokemon is null || pokemon.FusionHostId.Length > 0);
        var tab = action switch { BagAction.Evolution => "Evolution & forms", BagAction.HeldItem => "Held item", _ => "Training" };
        if (DrawAccentButton(action == BagAction.Moves ? "Open move licenses" : $"Open {tab.ToLowerInvariant()}", new Vector2(-1, 32 * CompanionScale), CompanionAccent) && pokemon is not null)
        {
            if (action == BagAction.Moves) { selectedPartyPokemon = pokemon.Id; pokemonMovesOpen = true; mainSection = 1; }
            else { managePokemonId = pokemon.Id; requestedManagementTab = tab; requestPokemonManagement = true; }
        }
        if (item.CanBeHeld && action != BagAction.HeldItem && DrawAccentButton("Manage held item", new Vector2(-1, 30 * CompanionScale), CompanionAccent) && pokemon is not null)
        { managePokemonId = pokemon.Id; requestedManagementTab = "Held item"; requestPokemonManagement = true; }
        ImGui.EndDisabled();
        if (CollectionLocked) ImGui.TextWrapped("Finish your battle, run or matchmaking before changing Pokémon.");
        if (DrawAccentButton("Open PC Storage", new Vector2(-1, 30 * CompanionScale), CompanionAccent)) mainSection = 2;
        ImGui.TextWrapped("Manage boxed Pokémon from PC Storage.");
    }

    private void OpenBattleFromBag(ShopItemDto item, bool catchItem)
    {
        if (battle is not { Status: "active" } current) return;
        mainSection = current.Kind == "wild" ? 0 : current.FrontierId is not null ? 8
            : current.OpponentId == "legendary-zarude" ? 10 : current.OpponentId.StartsWith("legendary-", StringComparison.Ordinal) ? 9 : 3;
        battleMenu = catchItem ? BattleMenu.Catch : BattleMenu.Bag;
        battleMenuBattleId = current.Id;
        battleMenuStamp = $"{current.Id}:{current.Turn}:{current.PlayerActive?.Ident}";
        if (catchItem) battleBallId = item.Id; else battleMedicineId = item.Id;
    }
}
