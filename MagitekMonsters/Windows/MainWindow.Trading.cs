using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private TradingSnapshotDto? trading;
    private bool tradeRefreshRequested;
    private DateTimeOffset nextTradePollAt;
    private int tradingTab;
    private int requestTradingTab = -1;
    private string tradeTrainerCode = "";
    private string tradePokemonId = "";
    private string wonderPokemonId = "";
    private string tradePickerSearch = "";
    private int tradePickerPage;
    private int tradeHistoryPage;
    private bool requestTradePicker;
    private bool pickerForWonder;
    private bool requestTradeConfirmation;
    private string tradeConfirmationId = "";
    private int tradeConfirmationRevision;
    private bool requestWonderConfirmation;
    private System.Collections.Generic.List<LegendaryBossDto>? tradingBossSource;
    private System.Collections.Generic.HashSet<string> tradingRestricted = [];
    private TrainerSnapshotDto? tradePickerSource;
    private System.Collections.Generic.List<PokemonDto> tradePickerOwned = [];
    private System.Collections.Generic.List<PokemonDto> tradePickerFiltered = [];
    private string tradePickerQuery = "";
    private TrainerSnapshotDto? tradeSelectionSource;
    private string? cachedTradeId, cachedWonderId;
    private PokemonDto? cachedTradePokemon, cachedWonderPokemon;

    private void ResetTrading()
    {
        trading = null; tradeRefreshRequested = false; nextTradePollAt = DateTimeOffset.MinValue;
        tradeTrainerCode = tradePokemonId = wonderPokemonId = tradePickerSearch = tradeConfirmationId = "";
        tradingTab = tradePickerPage = tradeHistoryPage = 0;
        requestTradingTab = -1;
        requestTradePicker = requestTradeConfirmation = requestWonderConfirmation = false;
        tradePickerSource = null; tradePickerOwned = []; tradingBossSource = null; tradingRestricted = [];
        tradePickerFiltered = []; tradePickerQuery = "";
        tradeSelectionSource = null; cachedTradeId = cachedWonderId = null;
        cachedTradePokemon = cachedWonderPokemon = null;
    }
    private void ApplyTrading(TradingSnapshotDto next)
    {
        trading = next; tradeRefreshRequested = false;
        if (next.Active?.YourPokemon?.Id == tradePokemonId) tradePokemonId = "";
        if (next.Wonder?.Pokemon.Id == wonderPokemonId) wonderPokemonId = "";
        nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(next.Active is not null ? 5 : next.Wonder is not null ? 15 : 60);
    }
    private async Task RefreshTrading()
    {
        ApplyTrading(await InSession(plugin.Api.GetTrades(disposeCancellation.Token)));
        SetStatus("Trading refreshed.", quiet: true);
    }
    private void BackgroundTrading()
    {
        tradeRefreshRequested = false; nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(30);
        RunBackground(async token =>
        {
            var next = await InSession(plugin.Api.GetTrades(token));
            return () => ApplyTrading(next);
        }, () => nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(60));
    }
    private async Task SaveTrade(Func<Task<TradingSnapshotDto>> action, bool refreshTrainer = true)
    {
        try
        {
            ApplyTrading(await InSession(action()));
            if (refreshTrainer) await InSession(RefreshTrainer());
            SetStatus(trading?.Unread > 0 ? "Trade complete. Your new Pokémon is in PC storage." : "Trade saved.");
        }
        catch { tradeRefreshRequested = true; throw; }
    }
    private PokemonDto? TradingSelection(bool wonder)
    {
        if (!ReferenceEquals(tradeSelectionSource, trainer))
        {
            tradeSelectionSource = trainer; cachedTradeId = cachedWonderId = null;
            cachedTradePokemon = cachedWonderPokemon = null;
        }
        var id = wonder ? wonderPokemonId : tradePokemonId;
        if (trainer is null || string.IsNullOrEmpty(id)) return null;
        if ((wonder ? cachedWonderId : cachedTradeId) == id) return wonder ? cachedWonderPokemon : cachedTradePokemon;
        PokemonDto? selected = null;
        foreach (var pokemon in trainer.Party) if (pokemon.Id == id) { selected = pokemon; break; }
        if (selected is null)
            foreach (var pokemon in trainer.Storage) if (pokemon.Id == id) { selected = pokemon; break; }
        if (wonder) { cachedWonderId = id; cachedWonderPokemon = selected; }
        else { cachedTradeId = id; cachedTradePokemon = selected; }
        return selected;
    }
    private bool CanOfferPokemon(PokemonDto pokemon)
    {
        if (pokemon.TradeLockId is not null || pokemon.Fusion is not null || pokemon.FusionHostId.Length > 0) return false;
        if (!ReferenceEquals(tradingBossSource, legendaryBosses))
        {
            tradingBossSource = legendaryBosses;
            tradingRestricted = legendaryBosses.Select(boss => SpeciesDisplayCatalog.Get(boss.Species)?.BaseSpecies ?? boss.Species).ToHashSet(StringComparer.Ordinal);
            tradePickerSource = null;
        }
        var species = SpeciesDisplayCatalog.Get(pokemon.DisplaySpecies)?.BaseSpecies ?? pokemon.Species;
        return !tradingRestricted.Contains(species);
    }
    private void DrawTrading()
    {
        if (trainer is null) return;
        DrawSectionHeader("Trading", "Trade with a trainer, or send a Pokémon into Wonder Trade.");
        ImGui.TextWrapped("Offered Pokémon wait in your PC and stay reserved until the trade ends. Held items travel with them. Legendary, Mythical and fused Pokémon cannot be traded.");
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Refresh", CompanionMuted)) Run(RefreshTrading, "Checking trades...");
        ImGui.EndDisabled(); ImGui.Spacing();
        if (trading is null)
        {
            ImGui.TextDisabled("Loading Trading...");
            if (!busy && DateTimeOffset.UtcNow >= nextTradePollAt) { nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(30); Run(RefreshTrading, "Loading Trading..."); }
            return;
        }
        if (ImGui.BeginTabBar("TradingTabs", ImGuiTabBarFlags.FittingPolicyResizeDown))
        {
            if (ImGui.BeginTabItem("Direct trade", requestTradingTab == 0 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            { tradingTab = 0; DrawDirectTrade(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Wonder Trade", requestTradingTab == 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            { tradingTab = 1; DrawWonderTrade(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem($"History{(trading.Unread > 0 ? $" [{trading.Unread}]" : "")}###TradeHistory", requestTradingTab == 2 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            { tradingTab = 2; DrawTradeHistory(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
            requestTradingTab = -1;
        }
        DrawTradingPicker(); DrawTradeConfirmations();
    }
    private void DrawDirectTrade()
    {
        ImGui.Spacing();
        if (trading!.Active is not { } active)
        {
            ImGui.TextWrapped("Invite a trainer using their UID or vanity code. Both trainers review the Pokémon and confirm before ownership changes.");
            ImGui.TextWrapped("Evolution stays optional. Trade evolutions still use the Linking Cord.");
            ImGui.SetNextItemWidth(Math.Min(300 * CompanionScale, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("##TradeTrainer", "Trainer UID or vanity code", ref tradeTrainerCode, 32);
            DrawTradingChoice(false);
            var selected = TradingSelection(false);
            ImGui.BeginDisabled(busy || CollectionLocked || selected is null || !CanOfferPokemon(selected) || string.IsNullOrWhiteSpace(tradeTrainerCode)
                || selected.PartySlot is not null && trainer!.Party.Count <= 1);
            if (DrawAccentButton("Send trade invitation", new Vector2(-1, 32 * CompanionScale), CompanionAccent) && selected is not null)
            {
                var code = tradeTrainerCode.Trim(); var id = selected.Id; var request = Guid.NewGuid().ToString();
                Run(() => SaveTrade(() => plugin.Api.CreateTrade(code, id, request)), "Sending trade invitation...");
            }
            ImGui.EndDisabled();
            return;
        }
        DrawClippedText($"{active.PartnerName} / {active.PartnerUid}", ImGui.GetContentRegionAvail().X, CompanionAccent);
        if (DateTimeOffset.TryParse(active.ExpiresAt, out var expiry)) ImGui.TextDisabled($"Expires {expiry.ToLocalTime():t}");
        ImGui.TextColored(active.YouConfirmed ? PokemonGreen : PokemonYellow, active.YouConfirmed ? "You confirmed this offer" : "Review both Pokémon before confirming");
        ImGui.TextColored(active.TheyConfirmed ? PokemonGreen : CompanionMuted, active.TheyConfirmed ? "Your partner confirmed this offer" : "Waiting for your partner's confirmation");
        DrawTradingChoice(false);
        var offered = TradingSelection(false);
        ImGui.BeginDisabled(busy || CollectionLocked || offered is null || !CanOfferPokemon(offered)
            || offered.PartySlot is not null && trainer!.Party.Count <= 1);
        if (DrawAccentButton(active.YourPokemon is null ? "Offer selected Pokémon" : "Change your offer", new Vector2(-1, 30 * CompanionScale), CompanionAccent) && offered is not null)
        {
            var id = offered.Id;
            Run(() => SaveTrade(() => plugin.Api.OfferTrade(active.Id, id, active.Revision)), "Updating trade offer...");
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped("Changing either offer clears both confirmations.");
        DrawTradePair(active.YourPokemon, active.TheirPokemon);
        ImGui.BeginDisabled(busy || active.YouConfirmed || active.YourPokemon is null || active.TheirPokemon is null);
        if (DrawAccentButton("Review and confirm trade", new Vector2(-1, 32 * CompanionScale), PokemonGreen))
        { tradeConfirmationId = active.Id; tradeConfirmationRevision = active.Revision; requestTradeConfirmation = true; }
        ImGui.EndDisabled();
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Cancel trade", PokemonRed)) Run(() => SaveTrade(() => plugin.Api.CancelTrade(active.Id)), "Cancelling trade...");
        ImGui.EndDisabled();
    }
    private void DrawWonderTrade()
    {
        ImGui.Spacing();
        ImGui.TextWrapped("Deposit one Pokémon and keep playing. It will trade with another player's deposit, even while you're offline. Your new Pokémon arrives in the same PC slot. With a small pool, finding a partner may take time.");
        if (trading!.Wonder is { } deposit)
        {
            DrawClippedText("WAITING FOR A WONDER TRADE PARTNER", ImGui.GetContentRegionAvail().X, PokemonYellow);
            DrawTradingPokemon(deposit.Pokemon, "Your deposit");
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Withdraw Pokémon", new Vector2(-1, 31 * CompanionScale), PokemonOrange))
                Run(() => SaveTrade(() => plugin.Api.WithdrawWonder(deposit.Id)), "Withdrawing Wonder Trade deposit...");
            ImGui.EndDisabled();
            ImGui.TextWrapped("You can withdraw until a match completes. A completed trade cannot be undone.");
            return;
        }
        DrawTradingChoice(true);
        var selected = TradingSelection(true);
        if (selected is not null) DrawTradingPokemon(selected, "YOUR SELECTION");
        ImGui.BeginDisabled(busy || CollectionLocked || selected is null || !CanOfferPokemon(selected)
            || selected.PartySlot is not null && trainer!.Party.Count <= 1);
        if (DrawAccentButton("Deposit in Wonder Trade", new Vector2(-1, 32 * CompanionScale), PokemonOrange)) requestWonderConfirmation = true;
        ImGui.EndDisabled();
    }
    private void DrawTradingChoice(bool wonder)
    {
        var selected = TradingSelection(wonder);
        if (DrawAccentButton(selected is null ? "Choose from Party or PC" : $"Selected: {selected.DisplaySpecies} / Lv. {selected.Level}", new Vector2(-1, 30 * CompanionScale), CompanionAccent))
        { pickerForWonder = wonder; tradePickerPage = 0; requestTradePicker = true; }
        if (selected is not null)
        {
            if (selected.PartySlot is not null && trainer!.Party.Count <= 1) ImGui.TextColored(PokemonOrange, "Keep at least one Pokémon in your party.");
            else if (!CanOfferPokemon(selected)) ImGui.TextColored(PokemonOrange, "This Pokémon cannot be offered. Choose another.");
        }
    }
    private void DrawTradePair(PokemonDto? given, PokemonDto? received)
    {
        var columns = ImGui.GetContentRegionAvail().X >= 600 * CompanionScale ? 2 : 1;
        if (!ImGui.BeginTable("TradePokemonPair", columns, ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn(); DrawTradingPokemon(given, "YOU GIVE");
        ImGui.TableNextColumn(); DrawTradingPokemon(received, "YOU RECEIVE");
        ImGui.EndTable();
    }
    private void DrawTradingPokemon(PokemonDto? pokemon, string role)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeightWithSpacing();
        ImGui.PushID(role); ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10 * scale));
        ImGui.BeginChild("TradePokemon", new Vector2(0, Math.Max(350 * scale, line * 16 + 86 * scale)), true, FixedBattlePanel);
        var width = ImGui.GetContentRegionAvail().X;
        DrawClippedText(role, width, CompanionAccent);
        if (pokemon is null) ImGui.TextWrapped("Waiting for a Pokémon to be offered.");
        else
        {
            DrawPokemonImage(pokemon.Appearance, pokemon.Shiny, new Vector2(58 * scale));
            DrawClippedText($"{(pokemon.Shiny ? "★ " : "")}{pokemon.Nickname} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level}", width, Vector4.One);
            DrawClippedText(string.Join(" / ", pokemon.Types), width, CompanionAccent);
            DrawClippedText($"Nature: {pokemon.Nature}{(pokemon.MintNature.Length > 0 ? " / Mint: " + pokemon.MintNature : "")}", width, CompanionMuted);
            DrawClippedText($"Ability: {pokemon.Ability} / Gender: {pokemon.Gender}", width, CompanionMuted);
            DrawClippedText($"Held item: {(pokemon.HeldItem.Length > 0 ? pokemon.HeldItem : "None")} / Tera: {pokemon.TeraType}", width, CompanionMuted);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(scale));
            if (ImGui.BeginTable("TradeStats", 7, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn(); ImGui.TextDisabled("Stat");
                foreach (var stat in new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" }) { ImGui.TableNextColumn(); ImGui.TextDisabled(stat); }
                foreach (var (label, values) in new[] { ("IV", pokemon.Ivs), ("EV", pokemon.Evs) })
                {
                    ImGui.TableNextColumn(); ImGui.TextDisabled(label);
                    foreach (var stat in BuildStats) { ImGui.TableNextColumn(); ImGui.TextUnformatted(values.GetValueOrDefault(stat.Id).ToString()); }
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();
            foreach (var move in pokemon.Moves) DrawClippedText(move, width, Vector4.One);
            DrawClippedText($"Original Trainer: {pokemon.OriginalTrainerName} / {pokemon.OriginalTrainerUid}", width, CompanionMuted);
            DrawClippedText($"Caught: {pokemon.Origin?.Location ?? pokemon.Source} / {pokemon.Pokeball}", width, CompanionMuted);
        }
        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.PopStyleColor(); ImGui.PopID();
    }
    private void DrawTradeHistory()
    {
        var receipts = trading!.Receipts;
        if (receipts.Count == 0) { ImGui.TextWrapped("Completed trades appear here, including Wonder Trades completed while you were offline."); return; }
        DrawPager("TradeHistory", ref tradeHistoryPage, receipts.Count, 5);
        foreach (var receipt in receipts.Skip(tradeHistoryPage * 5).Take(5))
        {
            ImGui.PushID(receipt.Id);
            DrawClippedText($"{(receipt.Kind == "wonder" ? "WONDER TRADE" : "DIRECT TRADE")} / {receipt.PartnerName} ({receipt.PartnerUid})", ImGui.GetContentRegionAvail().X, receipt.SeenAt is null ? PokemonYellow : CompanionAccent);
            DrawClippedText($"Gave {receipt.Given.DisplaySpecies} / Received {(receipt.Received.Shiny ? "★ " : "")}{receipt.Received.DisplaySpecies}", ImGui.GetContentRegionAvail().X, Vector4.One);
            if (DateTimeOffset.TryParse(receipt.CreatedAt, out var at)) ImGui.TextDisabled(at.ToLocalTime().ToString("g"));
            if (ImGui.TreeNode("Review Pokémon")) { DrawTradePair(receipt.Given, receipt.Received); ImGui.TreePop(); }
            ImGui.BeginDisabled(busy);
            if (receipt.SeenAt is null && DrawAccentSmallButton("Mark as read", CompanionAccent))
                Run(() => SaveTrade(() => plugin.Api.ReadTradeReceipt(receipt.Id), refreshTrainer: false), "Marking trade as read...");
            if (receipt.SeenAt is null) ImGui.SameLine();
            if (DrawAccentSmallButton("Find Pokémon", CompanionAccent)) Run(() => ViewTradedPokemon(receipt), "Opening traded Pokémon...");
            ImGui.EndDisabled(); ImGui.Separator(); ImGui.PopID();
        }
    }
    private async Task ViewTradedPokemon(TradeReceiptDto receipt)
    {
        if (trainer is null) return;
        var owner = trainer.User.Id;
        if (receipt.SeenAt is null) ApplyTrading(await InSession(plugin.Api.ReadTradeReceipt(receipt.Id)));
        await InSession(RefreshTrainer());
        if (trainer?.User.Id != owner) return;
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == receipt.Received.Id);
        if (pokemon is null) { SetStatus("This Pokémon is no longer in your collection."); return; }
        mainSection = pokemon.PartySlot.HasValue ? 1 : 2;
        if (mainSection == 1) selectedPartyPokemon = pokemon.Id;
        else
        {
            ObserveStorageOwner(); selectedStoragePokemon = pokemon.Id;
            storageSearch = ""; OpenStorageBox(pokemon.BoxNumber); storageType = storageGeneration = storageSort = 0;
            storageFavorites = storageShiny = storageDuplicatesOnly = false;
        }
        SetStatus("Traded Pokémon selected.");
    }
    private void DrawTradingPicker()
    {
        if (requestTradePicker) { ImGui.OpenPopup("Choose trade Pokémon"); requestTradePicker = false; }
        ImGui.SetNextWindowSize(new Vector2(620, 0) * CompanionScale, ImGuiCond.Appearing);
        if (!BeginCenteredModal("Choose trade Pokémon", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.TextWrapped("Choose an eligible Pokémon from your party or any PC box. Reserved, fused, Legendary and Mythical Pokémon are excluded.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##TradeSearch", "Search species or nickname...", ref tradePickerSearch, 80)) tradePickerPage = 0;
        var rebuilt = !ReferenceEquals(tradePickerSource, trainer) || !ReferenceEquals(tradingBossSource, legendaryBosses);
        if (rebuilt)
        { tradePickerOwned = trainer!.Party.Concat(trainer.Storage).Where(CanOfferPokemon).ToList(); tradePickerSource = trainer; }
        if (rebuilt || tradePickerQuery != tradePickerSearch)
        {
            tradePickerQuery = tradePickerSearch;
            tradePickerFiltered = tradePickerOwned.Where(pokemon => pokemon.DisplaySpecies.Contains(tradePickerSearch, StringComparison.OrdinalIgnoreCase) || pokemon.Nickname.Contains(tradePickerSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var available = tradePickerFiltered;
        DrawPager("TradePick", ref tradePickerPage, available.Count, 6);
        if (available.Count == 0) ImGui.TextDisabled("No eligible Pokémon found.");
        if (ImGui.BeginTable("TradePickCards", 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var pokemon in available.Skip(tradePickerPage * 6).Take(6))
            {
                ImGui.TableNextColumn(); ImGui.PushID(pokemon.Id);
                DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(32 * CompanionScale)); ImGui.SameLine();
                if (DrawAccentButton($"{(pokemon.Shiny ? "★ " : "")}{pokemon.Nickname} / {pokemon.DisplaySpecies}\nLv. {pokemon.Level} / {pokemon.Nature} / IV {pokemon.Ivs.Values.Sum()}/186\n{(pokemon.PartySlot is int slot ? "Party " + slot : "Box " + pokemon.BoxNumber)}", new Vector2(-1, 64 * CompanionScale), CompanionAccent))
                { if (pickerForWonder) wonderPokemonId = pokemon.Id; else tradePokemonId = pokemon.Id; ImGui.CloseCurrentPopup(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{pokemon.Nickname} / {pokemon.DisplaySpecies}\n{pokemon.Nature} / {pokemon.Ability}\nIVs: {string.Join(" / ", BuildStats.Select(stat => stat.Label + " " + pokemon.Ivs.GetValueOrDefault(stat.Id)))}\nHeld item: {pokemon.HeldItem}");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup(); ImGui.EndPopup();
    }
    private void DrawTradeConfirmations()
    {
        if (requestTradeConfirmation) { ImGui.OpenPopup("Confirm this trade?"); requestTradeConfirmation = false; }
        if (BeginConfirmationModal("Confirm this trade?"))
        {
            var active = trading?.Active;
            var changed = active is null || active.Id != tradeConfirmationId || active.Revision != tradeConfirmationRevision;
            ImGui.TextWrapped(changed ? "The trade changed. Close this window and review the current offers." : $"Give {active!.YourPokemon?.DisplaySpecies} to {active.PartnerName} and receive {active.TheirPokemon?.DisplaySpecies}?");
            ImGui.TextWrapped("Once both trainers confirm, this trade is permanent. Pokémon and held items change owners together.");
            if (DrawConfirmationActions("Confirm trade", PokemonGreen, busy || changed || active?.YouConfirmed == true))
            { ImGui.CloseCurrentPopup(); var id = tradeConfirmationId; var revision = tradeConfirmationRevision; Run(() => SaveTrade(() => plugin.Api.ConfirmTrade(id, revision)), "Confirming trade..."); }
            ImGui.EndPopup();
        }
        if (requestWonderConfirmation) { ImGui.OpenPopup("Confirm Wonder Trade?"); requestWonderConfirmation = false; }
        if (BeginConfirmationModal("Confirm Wonder Trade?"))
        {
            var selected = TradingSelection(true);
            ImGui.TextWrapped($"Deposit {(selected?.Shiny == true ? "shiny " : "")}{selected?.DisplaySpecies ?? "this Pokémon"} for a random player-to-player trade?");
            ImGui.TextWrapped("You can withdraw while waiting. Once matched, the trade is permanent. The Pokémon and its held item will go to another trainer, even if you are offline.");
            if (DrawConfirmationActions("Deposit Pokémon", PokemonOrange, busy || selected is null || !CanOfferPokemon(selected)))
            { ImGui.CloseCurrentPopup(); var id = selected!.Id; var request = Guid.NewGuid().ToString(); Run(() => SaveTrade(() => plugin.Api.DepositWonder(id, request)), "Depositing Wonder Trade Pokémon..."); }
            ImGui.EndPopup();
        }
    }
}
