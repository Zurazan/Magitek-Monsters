using System;
using System.Collections.Generic;
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
    private static readonly string[] PrizeFilters = ["All prizes", "Can afford", "Not caught"];
    private static readonly string[] PrizeOrders = ["Price: low to high", "Name: A to Z"];
    private static readonly string[] CollectionFilters = ["All rewards", "Starters", "Special rewards", "Unlocked starters"];
    private static readonly string[] RewardGenerations = ["All generations", "Gen 1", "Gen 2", "Gen 3", "Gen 4", "Gen 5", "Gen 6", "Gen 7", "Gen 8", "Gen 9"];
    private ExchangeBrowse? prizeExchange, collectionExchange;
    private ExchangePurchase? pendingExchangePurchase;
    private PrizePurchaseAttempt? pendingPrizePurchase;
    private bool requestExchangePurchase;

    private sealed class ExchangeBrowse
    {
        public string Search = "";
        public string Selected = "";
        public int Filter, Order, Page;
    }

    private sealed record ExchangeEntry(string Id, string Name, string? Species, int Level, string Description, string[] Types, string Origin, CollectionRewardDto? Reward);
    private sealed record ExchangeOffer(ExchangeEntry Entry, int Cost, bool UsesYen, string Action, string? Mode, bool Complete, string? Requirement, string Status)
    {
        public string Price => Entry.Reward?.Kind == "pokemon" && Cost == 0 ? "Free reward" : FormatAmount(Cost);
        public string FormatAmount(long amount) => UsesYen ? $"¥ {amount:N0}" : $"{amount:N0} CP";
    }
    private sealed record ExchangePurchase(string Owner, string Id, string? Mode, int Cost, bool UsesYen, string? Species, int Level);
    private sealed record PrizePurchaseAttempt(string Owner, string Name, PrizePurchaseRequest Request);

    private void DrawPrizeExchange() => DrawRewardExchange(false, prizeExchange ??= new());
    private void DrawCollectionExchange() => DrawRewardExchange(true, collectionExchange ??= new());

    private void DrawRewardExchange(bool collection, ExchangeBrowse browse)
    {
        if (trainer is not { } current) return;
        ImGui.PushID(collection ? "CollectionExchange" : "PrizeExchange");
        var scale = CompanionScale;
        var balance = collection ? $"{current.CollectionPoints.Balance:N0} CP / ¥ {current.Currency:N0} available" : $"¥ {current.Currency:N0} available";
        ExchangeText(balance, collection ? CompanionAccent : PokemonYellow);
        if (collection)
        {
            ExchangeText("Unlock starters and redeem rewards with Collection Points earned from Pokédex objectives.", CompanionMuted);
            if (DrawAccentSmallButton("View collection objectives", CompanionAccent)) mainSection = 7;
        }
        else
        {
            ExchangeText("Buy prize Pokémon with ¥. Each purchase gives you one Pokémon.", CompanionMuted);
            DrawPendingPrizePurchase();
        }
        ImGui.Spacing();

        if (collection ? collectionRewards.Count == 0 : prizePokemon.Count == 0)
        {
            ExchangeText("The reward catalogue is loading. It retries automatically.", CompanionMuted);
            ImGui.PopID();
            return;
        }

        DrawExchangeFilters(collection, browse);
        var offers = GetExchangeOffers(collection, browse);
        const int pageSize = 6;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var wide = availableWidth >= 640 * scale;
        if (wide && ImGui.BeginTable("ExchangeBrowser", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Catalogue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Reward details", ImGuiTableColumnFlags.WidthFixed, availableWidth * .43f);
            ImGui.TableNextColumn();
            var selected = DrawExchangeGrid(offers, browse, pageSize, 2);
            ImGui.TableNextColumn();
            DrawExchangeInspector(selected);
            ImGui.EndTable();
        }
        else
        {
            var columns = Math.Clamp((int)(availableWidth / (145 * scale)), 1, 3);
            var selected = DrawExchangeGrid(offers, browse, pageSize, columns);
            ImGui.Spacing();
            DrawExchangeInspector(selected);
        }
        if (collection)
        {
            ImGui.Spacing();
            var total = collectionRewards.Count(reward => reward.Kind == "starter");
            ExchangeText($"{current.CollectionPoints.UnlockedStarters.Count:N0}/{total:N0} starters permanently unlocked", CompanionMuted);
        }
        DrawExchangeConfirmation(collection);
        ImGui.PopID();
    }

    private static void DrawExchangeFilters(bool collection, ExchangeBrowse browse)
    {
        var wide = ImGui.GetContentRegionAvail().X >= 500 * CompanionScale;
        var changed = false;
        if (!wide) changed = Search();
        if (ImGui.BeginTable("ExchangeFilters", wide ? 3 : 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (wide)
            {
                ImGui.TableSetupColumn("Search", ImGuiTableColumnFlags.WidthStretch, 1.3f);
                ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Order", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn();
                changed |= Search();
            }
            ImGui.TableNextColumn();
            ImGui.TextDisabled("Show");
            ImGui.SetNextItemWidth(-1);
            var filters = collection ? CollectionFilters : PrizeFilters;
            changed |= ImGui.Combo("##Show", ref browse.Filter, filters, filters.Length);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(collection ? "Generation" : "Order");
            ImGui.SetNextItemWidth(-1);
            var orders = collection ? RewardGenerations : PrizeOrders;
            changed |= ImGui.Combo("##Order", ref browse.Order, orders, orders.Length);
            ImGui.EndTable();
        }
        if (changed) browse.Page = 0;
        ImGui.Spacing();

        bool Search()
        {
            ImGui.TextDisabled("Search");
            ImGui.SetNextItemWidth(-1);
            return ImGui.InputTextWithHint("##Search", "Find a reward...", ref browse.Search, 80);
        }
    }

    private List<ExchangeOffer> GetExchangeOffers(bool collection, ExchangeBrowse browse)
    {
        var search = browse.Search.Trim();
        bool Matches(ExchangeEntry entry) => entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Species?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

        if (!collection)
        {
            var offers = prizePokemon.Select(PrizeOffer).Where(offer => Matches(offer.Entry) && (browse.Filter switch
            {
                1 => offer.Cost <= ExchangeBalance(offer),
                2 => offer.Status == "Not caught",
                _ => true
            }));
            return (browse.Order == 1 ? offers.OrderBy(offer => offer.Entry.Name, StringComparer.OrdinalIgnoreCase)
                : offers.OrderBy(offer => offer.Cost).ThenBy(offer => offer.Entry.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        return collectionRewards.Where(reward => browse.Order == 0 || reward.Generation == browse.Order)
            .Select(CollectionOffer).Where(offer => Matches(offer.Entry) && (browse.Filter switch
            {
                1 => offer.Entry.Reward!.Kind == "starter",
                2 => offer.Entry.Reward!.Kind != "starter",
                3 => offer.Mode == "copy",
                _ => true
            }))
            .OrderBy(offer => offer.Entry.Reward!.Kind switch { "item" => 0, "pokemon" => 1, _ => 2 }).ToList();
    }

    private ExchangeOffer PrizeOffer(PrizePokemonDto prize)
    {
        var entry = new ExchangeEntry(prize.Id, SpeciesDisplayCatalog.Name(prize.Species), prize.Species, prize.Level,
            prize.Description, SpeciesDisplayCatalog.Get(prize.Species)?.Types ?? [], prize.Origin, null);
        return new(entry, prize.Price, true, "Buy", null, false, null, IsSpeciesRegistered(prize.Species) ? "Caught" : "Not caught");
    }

    private ExchangeOffer CollectionOffer(CollectionRewardDto reward)
    {
        var progress = trainer!.CollectionPoints;
        var starter = reward.Kind == "starter";
        var copy = starter && reward.Species is not null && progress.UnlockedStarters.Contains(reward.Species);
        var claimed = reward.Kind == "pokemon" && progress.ClaimedFormRewards.Contains(reward.Id);
        var owned = reward.Kind == "item" && InventoryCount(reward.Id) > 0;
        var forms = reward.RequiredForms.Count;
        var found = reward.RequiredForms.Count(IsFormRegistered);
        var unmet = progress.RegisteredSpecies < reward.RequiredSpecies
            ? $"Register {reward.RequiredSpecies - progress.RegisteredSpecies:N0} more genuine species."
            : found < forms ? "Register the remaining forms first." : null;
        var status = claimed ? "Claimed" : owned ? "Owned / Active" : copy ? "Permanently unlocked"
            : forms > 0 ? $"{found}/{forms} forms registered"
            : reward.RequiredSpecies > 0 ? $"{Math.Min(progress.RegisteredSpecies, reward.RequiredSpecies):N0}/{reward.RequiredSpecies:N0} species"
            : starter ? "Starter unlock" : "Available";
        var action = claimed ? "Claimed" : owned ? "Owned / Active" : copy ? "Buy copy"
            : reward.Kind == "pokemon" ? "Claim reward" : "Unlock";
        var types = reward.Species is { } species ? SpeciesDisplayCatalog.Get(species)?.Types ?? [] : [];
        if (types.Length == 0 && reward.Type is { Length: > 0 } type) types = [type];
        var entry = new ExchangeEntry(reward.Id, reward.Name, reward.Species, 5, reward.Description, types, "", reward);
        return new(entry, copy ? reward.CopyPrice.GetValueOrDefault() : reward.Points, copy, action,
            copy ? "copy" : "unlock", owned || claimed, unmet, status);
    }

    private ExchangeOffer? DrawExchangeGrid(List<ExchangeOffer> offers, ExchangeBrowse browse, int pageSize, int columns)
    {
        browse.Page = Math.Clamp(browse.Page, 0, Math.Max(0, (offers.Count - 1) / pageSize));
        var page = offers.Skip(browse.Page * pageSize).Take(pageSize).ToArray();
        if (!page.Any(offer => offer.Entry.Id == browse.Selected)) browse.Selected = page.FirstOrDefault()?.Entry.Id ?? "";
        if (page.Length == 0) ExchangeText("No rewards match these filters.", CompanionMuted);
        else if (ImGui.BeginTable("RewardGrid", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var offer in page)
            {
                ImGui.TableNextColumn();
                if (DrawExchangeTile(offer, browse.Selected == offer.Entry.Id)) browse.Selected = offer.Entry.Id;
            }
            ImGui.EndTable();
        }

        var pageBefore = browse.Page;
        DrawPager("RewardPages", ref browse.Page, offers.Count, pageSize);
        if (pageBefore != browse.Page) browse.Selected = offers.ElementAtOrDefault(browse.Page * pageSize)?.Entry.Id ?? "";
        return offers.FirstOrDefault(offer => offer.Entry.Id == browse.Selected);
    }

    private bool DrawExchangeTile(ExchangeOffer offer, bool selected)
    {
        var entry = offer.Entry;
        var scale = CompanionScale;
        var pad = 8 * scale;
        var gap = 4 * scale;
        var line = ImGui.GetTextLineHeight();
        var content = Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * pad);
        var art = 38 * scale;
        var copy = Math.Max(1, content - art - gap);
        var nameHeight = ImGui.CalcTextSize(entry.Name, false, copy).Y;
        var headerHeight = Math.Max(art, nameHeight + gap + ImGui.CalcTextSize(offer.Price, false, copy).Y);
        var badgeHeight = Math.Max(line, 14 * scale);
        var statusHeight = ImGui.CalcTextSize(offer.Status, false, content).Y;
        var height = 2 * pad + headerHeight + badgeHeight + statusHeight + 2 * gap;
        var clicked = DrawPveCardSurface("Reward##" + entry.Id, height, selected, out var min, out _, out var after);
        var hovered = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(min + new Vector2(pad));
        DrawExchangeArt(entry, art);
        ImGui.SetCursorScreenPos(min + new Vector2(pad + art + gap, pad));
        ExchangeText(entry.Name, ImGui.GetStyle().Colors[(int)ImGuiCol.Text], copy);
        ImGui.SetCursorScreenPos(min + new Vector2(pad + art + gap, pad + nameHeight + gap));
        ExchangeText(offer.Price, offer.UsesYen ? PokemonYellow : CompanionAccent, copy);
        ImGui.SetCursorScreenPos(min + new Vector2(pad, pad + headerHeight + gap));
        DrawExchangeTypes(entry.Types, content);
        ImGui.SetCursorScreenPos(min + new Vector2(pad, pad + headerHeight + badgeHeight + 2 * gap));
        ExchangeText(offer.Status, ExchangeStatusColor(offer), content);
        if (hovered) ImGui.SetTooltip($"{entry.Name}\n{offer.Price}\n{offer.Status}");
        ImGui.SetCursorScreenPos(after);
        return clicked;
    }

    private void DrawExchangeInspector(ExchangeOffer? offer)
    {
        if (offer is null) return;
        var entry = offer.Entry;
        var reward = entry.Reward;
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(11, 10) * scale);
        if (ImGui.BeginTable("ExchangeInspector", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            DrawExchangeIdentity(entry);
            var facts = entry.Species is null ? offer.Status : $"Lv. {entry.Level} / {offer.Status}";
            ExchangeText(facts, ExchangeStatusColor(offer));
            if (entry.Description.Length > 0)
            {
                ImGui.Spacing();
                ExchangeText(entry.Description, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            }
            if (entry.Origin.Length > 0) ExchangeText("Original prize source / " + entry.Origin, CompanionMuted);
            if (reward is not null) DrawExchangeRequirements(reward);

            ImGui.Spacing();
            ImGui.Separator();
            ExchangeText(offer.Price, offer.UsesYen ? PokemonYellow : CompanionAccent);
            var reason = ExchangeRestriction(offer);
            var accent = offer.Complete ? PokemonGreen : reason is not null ? PokemonRed : offer.UsesYen ? PokemonYellow : PokemonGreen;
            ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, 1f);
            ImGui.BeginDisabled(reason is not null || offer.Complete);
            if (DrawAccentButton(offer.Action, new Vector2(-1, Math.Max(32 * scale, ImGui.GetFrameHeight())), accent))
            {
                pendingExchangePurchase = new(trainer!.User.Id, entry.Id, offer.Mode, offer.Cost, offer.UsesYen, entry.Species, entry.Level);
                requestExchangePurchase = true;
            }
            ImGui.EndDisabled();
            ImGui.PopStyleVar();
            if (!offer.Complete && reason is not null) ExchangeText(reason, PokemonRed);
            if (entry.Species is not null && !offer.Complete)
                ExchangeText("Goes to your party if there is space, otherwise PC Storage.", CompanionMuted);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawExchangeIdentity(ExchangeEntry entry)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 3) * scale);
        if (ImGui.BeginTable("RewardIdentity", 2, ImGuiTableFlags.SizingStretchProp))
        {
            var art = Math.Min(72 * scale, ImGui.GetContentRegionAvail().X * .32f);
            ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, art);
            ImGui.TableNextColumn();
            var category = entry.Reward?.Kind switch
            {
                "starter" => $"STARTER COLLECTION / GEN {entry.Reward.Generation}",
                "item" => "PERMANENT KEY ITEM",
                "pokemon" => "SPECIAL COLLECTION REWARD",
                _ => "PRIZE POKÉMON"
            };
            ExchangeText(category, CompanionMuted);
            ExchangeText(entry.Name, CompanionAccent);
            DrawExchangeTypes(entry.Types, ImGui.GetContentRegionAvail().X);
            ImGui.TableNextColumn();
            DrawExchangeArt(entry, art);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawExchangeRequirements(CollectionRewardDto reward)
    {
        if (reward.RequiredSpecies > 0)
        {
            var registered = Math.Min(trainer!.CollectionPoints.RegisteredSpecies, reward.RequiredSpecies);
            ImGui.Spacing();
            ExchangeText($"Genuine species registrations / {registered:N0}/{reward.RequiredSpecies:N0}", CompanionAccent);
            DrawProgressBar((float)registered / reward.RequiredSpecies, new Vector2(-1, 5 * CompanionScale), "", ExpBarBlue);
        }
        if (reward.RequiredForms.Count == 0) return;
        ImGui.Spacing();
        ExchangeText("Required registered forms", CompanionAccent);
        foreach (var group in reward.RequiredForms.GroupBy(form => SpeciesDisplayCatalog.Get(form)?.BaseSpecies ?? form))
        {
            var missing = group.Where(form => !IsFormRegistered(form)).ToArray();
            var count = group.Count();
            var found = count - missing.Length;
            if (ImGui.BeginTable("Forms##" + group.Key, 2, ImGuiTableFlags.SizingStretchProp))
            {
                var art = 30 * CompanionScale;
                ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, art);
                ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn();
                DrawPokemonIcon(group.Key, false, new Vector2(art));
                ImGui.TableNextColumn();
                ExchangeText($"{SpeciesDisplayCatalog.Name(group.Key)} / {found}/{count}", found == count ? PokemonGreen : CompanionMuted);
                DrawProgressBar((float)found / count, new Vector2(-1, 4 * CompanionScale), "", ExpBarBlue);
                ImGui.EndTable();
            }
            if (missing.Length > 0) ExchangeText("Missing: " + string.Join(", ", missing.Select(RequiredFormLabel)), CompanionMuted);
        }
    }

    private static string RequiredFormLabel(string species)
    {
        var entry = SpeciesDisplayCatalog.Get(species);
        if (entry?.BaseSpecies is "Flabébé" or "Flabébé" or "Floette" or "Florges")
            return entry.Form.Length == 0 ? "Red Flower" : $"{entry.Form} Flower";
        return SpeciesDisplayCatalog.Name(species);
    }

    private void DrawExchangeConfirmation(bool collection)
    {
        if (requestExchangePurchase)
        {
            requestExchangePurchase = false;
            ImGui.OpenPopup("Confirm reward");
        }
        if (!BeginConfirmationModal("Confirm reward")) return;
        var quote = pendingExchangePurchase;
        var offer = quote is not null && quote.Owner == trainer?.User.Id ? FindExchangeOffer(collection, quote.Id) : null;
        var current = offer is not null && quote is not null && quote.Mode == offer.Mode && quote.Cost == offer.Cost
            && quote.UsesYen == offer.UsesYen && quote.Species == offer.Entry.Species && quote.Level == offer.Entry.Level;
        if (!current || offer is null)
        {
            ImGui.TextWrapped("This offer changed. Review the current reward before confirming.");
            DrawConfirmationActions(null, CompanionMuted, cancelLabel: "Back to exchange", onCancel: () => pendingExchangePurchase = null);
            EndConfirmationModal();
            return;
        }

        DrawExchangeIdentity(offer.Entry);
        ExchangeText($"Cost: {offer.Price}", offer.UsesYen ? PokemonYellow : CompanionAccent);
        var reason = ExchangeRestriction(offer);
        if (!offer.Complete && reason is null && offer.Cost > 0)
            ExchangeText($"Remaining balance: {offer.FormatAmount(ExchangeBalance(offer) - offer.Cost)}", CompanionMuted);
        if (reason is not null) ExchangeText(reason, PokemonRed);
        if (DrawConfirmationActions(offer.Action, offer.UsesYen ? PokemonYellow : PokemonGreen, offer.Complete || reason is not null,
            onCancel: () => pendingExchangePurchase = null))
        {
            pendingExchangePurchase = null;
            ImGui.CloseCurrentPopup();
            if (offer.Mode is { } mode)
                Run(() => RedeemCollectionReward(offer.Entry.Id, mode), $"Redeeming {offer.Entry.Name}...");
            else
            {
                var attempt = new PrizePurchaseAttempt(trainer!.User.Id, offer.Entry.Name,
                    new(offer.Entry.Species!, Guid.NewGuid().ToString(), offer.Cost, offer.Entry.Level));
                Run(() => SendPrizePurchase(attempt), $"Purchasing {attempt.Name}...");
            }
        }
        EndConfirmationModal();
    }

    private ExchangeOffer? FindExchangeOffer(bool collection, string id)
    {
        if (collection) return collectionRewards.FirstOrDefault(reward => reward.Id == id) is { } reward ? CollectionOffer(reward) : null;
        return prizePokemon.FirstOrDefault(prize => prize.Id == id) is { } prize ? PrizeOffer(prize) : null;
    }

    private long ExchangeBalance(ExchangeOffer offer) => offer.UsesYen ? trainer!.Currency : trainer!.CollectionPoints.Balance;

    private string? ExchangeRestriction(ExchangeOffer offer)
    {
        if (offer.Complete) return offer.Status;
        if (offer.Requirement is not null) return offer.Requirement;
        if (offer.Mode is null && pendingPrizePurchase is not null) return "Confirm the pending prize purchase first.";
        var shortfall = offer.Cost - ExchangeBalance(offer);
        if (shortfall > 0) return $"You need {offer.FormatAmount(shortfall)} more.";
        return offer.Mode is null ? busy ? "Wait for the current action to finish." : null
            : CollectionLocked ? "Finish your current battle, run or matchmaking first." : null;
    }

    private async Task SendPrizePurchase(PrizePurchaseAttempt attempt)
    {
        if (trainer?.User.Id != attempt.Owner) return;
        try
        {
            var next = await InSession(plugin.Api.RedeemPrizePokemon(attempt.Request, disposeCancellation.Token));
            RequestPokedexIfChanged(trainer?.Pokedex, next.Pokedex);
            trainer = next;
        }
        catch (UnconfirmedActionException)
        {
            pendingPrizePurchase = attempt;
            throw;
        }
        catch (ApiException error) when (error.StatusCode is 400 or 403 or 404 or 409)
        {
            pendingPrizePurchase = null;
            if (error.Code == "prize_offer_changed") RetryCatalogs();
            throw;
        }
        pendingPrizePurchase = null;
        SetStatus($"{attempt.Name} purchased. Check your party or PC.");
    }

    private void DrawPendingPrizePurchase()
    {
        if (pendingPrizePurchase is not { } attempt || trainer?.User.Id != attempt.Owner) return;
        ExchangeText($"Purchase unconfirmed / {attempt.Name} / ¥ {attempt.Request.ExpectedPrice:N0}", PokemonYellow);
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Retry same purchase", CompanionAccent))
            Run(() => SendPrizePurchase(attempt), "Checking prize purchase...");
        ImGui.EndDisabled();
    }

    private static Vector4 ExchangeStatusColor(ExchangeOffer offer) => offer.Complete || offer.Mode == "copy" || offer.Status == "Caught"
        ? PokemonGreen : offer.Requirement is not null ? PokemonYellow : CompanionMuted;

    private void DrawExchangeArt(ExchangeEntry entry, float size)
    {
        if (entry.Species is { } species) DrawPokemonIcon(species, false, new Vector2(size));
        else DrawItemImage(entry.Id, new Vector2(size));
    }

    private void DrawExchangeTypes(string[] types, float width)
    {
        if (types.Length == 0) return;
        var scale = CompanionScale;
        DrawStarterTypeBadges(types, Math.Max(1, Math.Min(60 * scale, (width - (types.Length - 1) * 5 * scale) / types.Length)), scale);
    }

    private static void ExchangeText(string text, Vector4 color, float? width = null)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, width ?? ImGui.GetContentRegionAvail().X));
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
    }
}
