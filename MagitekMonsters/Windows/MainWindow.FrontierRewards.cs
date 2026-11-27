using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum FrontierRewardGroup { Exchange, Beasts, Research }
    private sealed record FrontierItem(FrontierRewardDto Reward, ShopItemDto? Item, string Pocket)
    {
        public string Description => string.IsNullOrWhiteSpace(Reward.Description) ? Item?.Description ?? "" : Reward.Description;
    }

    private static readonly (string Label, string Id)[] ParadoxEras = [("Both eras", ""), ("Ancient", "Ancient"), ("Future", "Future")];
    private FrontierRewardGroup frontierRewardGroup;
    private ShopCatalogBrowser<FrontierItem>? frontierItems;
    private ShopCatalogBrowser<FrontierRewardDto>? frontierBeasts, frontierResearch;
    private List<FrontierRewardDto>? frontierRewardSource;
    private List<ShopItemDto>? frontierItemSource;
    private List<FrontierItem> frontierItemStock = [];
    private List<FrontierRewardDto> frontierBeastStock = [], frontierResearchStock = [];
    private (string Label, string Id)[] frontierItemCategories = [];

    private ShopCatalogBrowser<FrontierItem> FrontierItems => frontierItems ??= new(offer => offer.Reward.Id, DrawCatalogArtwork);
    private ShopCatalogBrowser<FrontierRewardDto> FrontierBeasts => frontierBeasts ??= new(reward => reward.Id, DrawCatalogArtwork);
    private ShopCatalogBrowser<FrontierRewardDto> FrontierResearch => frontierResearch ??= new(reward => reward.Id, DrawCatalogArtwork);

    private int ParadoxWaitSeconds => DateTimeOffset.TryParse(trainer?.Frontier.NextParadoxAt, out var available)
        ? Math.Max(0, (int)Math.Ceiling((available - DateTimeOffset.UtcNow).TotalSeconds)) : 0;

    private void ResetFrontierRewards()
    {
        frontierRewardGroup = FrontierRewardGroup.Exchange;
        frontierItems = null;
        frontierBeasts = frontierResearch = null;
        frontierRewardSource = null;
        frontierItemSource = null;
        frontierItemStock = [];
        frontierBeastStock = frontierResearchStock = [];
        frontierItemCategories = [];
        pendingFrontierPurchase = null;
    }

    private void DrawFrontierRewards(TrainerSnapshotDto current)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        DrawFrontierRewardGroup("BP Exchange", FrontierRewardGroup.Exchange);
        SameLineIfFits("Ultra Beasts", right);
        DrawFrontierRewardGroup("Ultra Beasts", FrontierRewardGroup.Beasts);
        SameLineIfFits("Paradox Research", right);
        DrawFrontierRewardGroup("Paradox Research", FrontierRewardGroup.Research);
        ImGui.Separator();
        DrawPendingFrontierPurchase();
        UpdateFrontierStock(current.Frontier.Rewards);
        var research = frontierRewardGroup == FrontierRewardGroup.Research;
        ImGui.PushID((int)frontierRewardGroup);
        if (frontierRewardGroup == FrontierRewardGroup.Exchange) DrawFrontierItemCatalog(current.Frontier.Balance);
        else
        {
            if (research) DrawFrontierResearchEncounter(current);
            DrawFrontierPokemonCatalog(current.Frontier, research);
        }
        ImGui.PopID();
    }

    private void DrawFrontierRewardGroup(string label, FrontierRewardGroup group)
    {
        if (DrawAccentButton(label, Vector2.Zero, frontierRewardGroup == group ? CompanionAccent : CompanionMuted))
            frontierRewardGroup = group;
    }

    private void UpdateFrontierStock(List<FrontierRewardDto> rewards)
    {
        if (ReferenceEquals(frontierRewardSource, rewards) && ReferenceEquals(frontierItemSource, itemCatalog)) return;
        var definitions = itemCatalog.ToLookup(item => item.Id, StringComparer.OrdinalIgnoreCase);
        frontierItemStock = rewards.Where(reward => reward is { Kind: "item", ItemId: not null }).Select(reward =>
        {
            var item = definitions[reward.ItemId!].FirstOrDefault();
            return new FrontierItem(reward, item, item is null ? "other" : BagInventory.Pocket(item));
        }).ToList();
        var pockets = frontierItemStock.Select(offer => offer.Pocket).ToHashSet();
        frontierItemCategories = BagInventory.Pockets.Where(category => category.Id.Length == 0 || pockets.Contains(category.Id)).ToArray();
        frontierBeastStock = rewards.Where(reward => reward.Kind == "pokemon").ToList();
        frontierResearchStock = rewards.Where(reward => reward.Kind == "permit").ToList();
        frontierRewardSource = rewards;
        frontierItemSource = itemCatalog;
    }

    private void DrawFrontierItemCatalog(int balance)
    {
        var browser = FrontierItems;
        if (frontierItemCategories.Length > 1) browser.DrawCategories(frontierItemCategories);
        browser.DrawSearch("Search BP items, descriptions or moves...");
        var stock = browser.Filter(frontierItemStock, static (offer, words, category) =>
            (category.Length == 0 || category == offer.Pocket) && MartCatalog.Matches(words, offer.Reward.Name, offer.Description, offer.Item?.Move));
        browser.Draw(stock, "items", Tile, Details, Buy, CatalogFooterHeight(quantities: true));

        CatalogTile Tile(FrontierItem offer) => new(offer.Reward.Name, offer.Description, offer.Reward.ItemId, null,
            CatalogPrice("BP", offer.Reward.CurrentPrice),
            offer.Reward.Unlocked ? $"Bag {InventoryCount(offer.Reward.ItemId!):N0}" : "Locked", CompanionMuted);

        static void Details(FrontierItem offer)
        {
            if (offer.Item is not { } item)
            {
                DrawGoalText(offer.Description, CompanionMuted);
                return;
            }
            DrawItemCatalogDetails(item, offer.Description,
                item is { BpOnly: false, Price: > 0, Currency: "yen" or "" } ? $"Also available for ¥ {item.Price:N0} in Poké Mart." : null);
        }

        void Buy(FrontierItem offer)
        {
            var reward = offer.Reward;
            var cost = new CatalogCost(reward.CurrentPrice, offer.Item?.MaxOwned, InventoryCount(reward.ItemId!), balance, "BP");
            if (DrawCatalogPurchase(cost, FrontierQuantities, ref browser.Quantity, busy || !reward.Unlocked || pendingFrontierPurchase is not null) is int quantity)
                Run(() => BuyFrontierReward(reward.Id, quantity), $"Redeeming {reward.Name}...");
        }
    }

    private void DrawFrontierPokemonCatalog(FrontierProgressDto progress, bool research)
    {
        var browser = research ? FrontierResearch : FrontierBeasts;
        if (research) browser.DrawCategories(ParadoxEras);
        browser.DrawSearch(research ? "Search Paradox Pokémon..." : "Search Ultra Beasts...");
        var stock = browser.Filter(research ? frontierResearchStock : frontierBeastStock, static (reward, words, era) =>
            (era.Length == 0 || reward.Era == era) && MartCatalog.Matches(words, reward.Name, reward.Description, reward.Species));
        browser.Draw(stock, "rewards", Tile, Details, Actions, CatalogFooterHeight(quantities: false));

        CatalogTile Tile(FrontierRewardDto reward)
        {
            var owned = research && reward.Purchased > 0;
            var status = owned ? "Permit owned" : !reward.Unlocked ? "Locked" : reward.CurrentPrice > progress.Balance ? "Need more BP" : "Available";
            return new(reward.Name, reward.Description, null, reward.Species,
                owned ? "Free to hunt" : CatalogPrice("BP", reward.CurrentPrice), status,
                owned ? PokemonGreen : !reward.Unlocked ? CompanionMuted : reward.CurrentPrice > progress.Balance ? PokemonYellow : CompanionAccent);
        }

        void Details(FrontierRewardDto reward)
        {
            if (research)
            {
                DrawGoalText($"{reward.Era} / {reward.Progress:N0}/{reward.LifetimeWins:N0} lifetime wins", CompanionAccent);
                DrawGoalText("Buy once to open repeat level-50 hunts with your normal party. Hunts share a five-minute cooldown.", CompanionMuted);
                return;
            }
            DrawGoalText($"Lifetime wins: {reward.Progress:N0}/{reward.LifetimeWins:N0}", CompanionAccent);
            var achievement = progress.Achievements.FirstOrDefault(entry => entry.Id == reward.AchievementId);
            if (achievement is not null)
                DrawGoalText($"Or {achievement.Name}: {achievement.Progress:N0}/{achievement.Target:N0}", CompanionAccent);
            DrawGoalText("Either route unlocks purchase permanently.", CompanionMuted);
            if (!string.IsNullOrWhiteSpace(reward.Description)) DrawGoalText(reward.Description, CompanionMuted);
            if (reward.Species == "Poipole") DrawGoalText("Poipole evolves when it knows Dragon Pulse.", CompanionMuted);
        }

        void Actions(FrontierRewardDto reward)
        {
            var width = ImGui.GetContentRegionAvail().X;
            if (research && reward.Purchased > 0)
            {
                var wait = ParadoxWaitSeconds;
                var blocked = CollectionEditRestriction is not null;
                DrawClippedText(blocked ? "Finish your current activity first." : wait > 0 ? $"Ready in {wait / 60}:{wait % 60:00}" : "Permanent permit owned",
                    width, blocked ? PokemonOrange : wait > 0 ? PokemonYellow : PokemonGreen);
                ImGui.BeginDisabled(busy || blocked || wait > 0);
                if (DrawAccentButton("Hunt", new Vector2(-1, CatalogActionHeight), CompanionAccent))
                    Run(() => StartParadoxHunt(reward.Id), $"Locating {reward.Species}...");
                ImGui.EndDisabled();
                return;
            }
            var quote = MartCatalog.Quote(reward.CurrentPrice, null, 0, 1, progress.Balance, 1);
            var restricted = !research && CollectionEditRestriction is not null;
            DrawClippedText(CatalogPrice("BP", reward.CurrentPrice), width, PokemonYellow);
            if (restricted || !reward.Unlocked)
                DrawClippedText(restricted ? "Finish your current activity first." : "Complete the unlock requirement first.", width, PokemonOrange);
            else DrawCatalogBalance("BP", quote, width);
            var label = research ? "Buy permit" : reward.Purchased > 0 ? "Buy copy" : "Buy";
            if (DrawCatalogBuyButton(label, quote, busy || restricted || !reward.Unlocked || pendingFrontierPurchase is not null))
                Run(() => BuyFrontierReward(reward.Id), $"Redeeming {reward.Name}...");
        }
    }

    private void DrawFrontierResearchEncounter(TrainerSnapshotDto current)
    {
        if (current.ActiveEncounter is not { Status: "active" } encounter) return;
        DrawGoalText($"Saved wild encounter: {encounter.Pokemon.Species}", PokemonOrange);
        if (DrawAccentButton("Resume wild encounter", Vector2.Zero, CompanionAccent))
            Run(async () => { await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0; }, "Restoring your wild battle...");
    }
}
