using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawTrading()
    {
        if (trainer is null) return;
        DrawSectionHeader("Trading", "Direct trades, Wonder Trade and recent arrivals in one place.");
        if (trading is null)
        {
            ImGui.TextDisabled("Loading Trading...");
            if (!busy && DateTimeOffset.UtcNow >= nextTradePollAt)
            { nextTradePollAt = DateTimeOffset.UtcNow.AddSeconds(30); Run(RefreshTrading, "Loading Trading..."); }
            return;
        }
        var wide = ImGui.GetContentRegionAvail().X >= 650 * CompanionScale;
        if (ImGui.BeginTable("TradingDesk", wide ? 2 : 1, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Direct trade", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            if (wide) ImGui.TableSetupColumn("Wonder Trade", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextColumn(); DrawTradingSection(false);
            ImGui.TableNextColumn(); DrawTradingSection(true);
            ImGui.EndTable();
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var revealHistory = requestTradeHistory;
        if (revealHistory) { ImGui.SetNextItemOpen(true, ImGuiCond.Always); requestTradeHistory = false; }
        tradeHistoryOpen = ImGui.CollapsingHeader($"Recent trades{(trading.Unread > 0 ? $" / {trading.Unread} new" : "")}###TradeHistory");
        if (tradeHistoryOpen)
        {
            if (revealHistory) ImGui.SetScrollHereY(.25f);
            DrawTradeHistory();
        }
        if (ImGui.CollapsingHeader("Trading rules"))
        {
            TradeText("Offered Pokémon are reserved in your PC. Held items travel with them. Legendary, Mythical and fused Pokémon cannot be traded.", CompanionMuted);
            TradeText("Both trainers confirm the same offers before ownership changes. Changing either offer clears both confirmations. Completed trades are permanent.", CompanionMuted);
            TradeText("Evolution stays optional. Trade evolutions still use the Linking Cord.", CompanionMuted);
        }
        DrawStoragePicker(ref tradePicker, "Choose trade Pokémon"); DrawTradeConfirmations();
    }

    private void DrawTradingSection(bool wonder)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 9) * CompanionScale);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, CompanionMuted with { W = .3f });
        if (ImGui.BeginTable(wonder ? "WonderTradeSection" : "DirectTradeSection", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(.115f, .12f, .135f, 1)));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X));
            TradeText(wonder ? "WONDER TRADE" : "DIRECT TRADE", wonder ? PokemonOrange : CompanionAccent);
            ImGui.Spacing();
            if (wonder) DrawWonderTrade(); else DrawDirectTrade();
            ImGui.PopTextWrapPos(); ImGui.EndTable();
        }
        ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }

    private void DrawDirectTrade()
    {
        var scale = CompanionScale;
        if (trading!.Active is not { } active)
        {
            ImGui.TextDisabled("Trainer UID or vanity code"); ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##TradeTrainer", "Enter a trainer code", ref tradeTrainerCode, 32);
            ImGui.BeginDisabled(busy);
            if (DrawAccentSmallButton("Choose friend##Trade", CompanionAccent)) OpenFriendPicker("trade");
            ImGui.EndDisabled(); ImGui.Spacing();
            var selected = TradingSelection(false);
            if (selected is not null) DrawTradingPokemon(selected, "YOUR OFFER");
            DrawTradingChoice(false);
            var reason = TradeOfferRestriction(selected);
            ImGui.BeginDisabled(reason is not null || string.IsNullOrWhiteSpace(tradeTrainerCode));
            if (DrawAccentButton("Send trade invitation", new Vector2(-1, 32 * scale), CompanionAccent) && selected is not null)
            {
                var code = tradeTrainerCode.Trim(); var id = selected.Id; var request = Guid.NewGuid().ToString();
                Run(() => SaveTrade(() => plugin.Api.CreateTrade(code, id, request)), "Sending trade invitation...");
            }
            ImGui.EndDisabled();
            TradeText(reason ?? "Your partner reviews the offer before either of you confirms.", CompanionMuted);
            return;
        }
        TradeText(active.PartnerName, CompanionAccent);
        TradeText(active.PartnerUid + (DateTimeOffset.TryParse(active.ExpiresAt, out var expiry) ? $" / Expires {expiry.ToLocalTime():t}" : ""), CompanionMuted);
        DrawTradePair(active.YourPokemon, active.TheirPokemon);
        if (ImGui.TreeNode("Inspect stats & moves"))
        { DrawTradeInspectionPair(active.YourPokemon, active.TheirPokemon); ImGui.TreePop(); }
        TradeText(active.YouConfirmed ? "You confirmed" : "Your confirmation needed", active.YouConfirmed ? PokemonGreen : CompanionMuted);
        TradeText(active.TheyConfirmed ? "Your partner confirmed" : "Waiting for your partner", active.TheyConfirmed ? PokemonGreen : CompanionMuted);
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        DrawTradingChoice(false);
        SameLineIfFits("Cancel trade", right);
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Cancel trade", PokemonRed))
        { tradeCancellationId = active.Id; requestTradeCancellation = true; }
        ImGui.EndDisabled();
        ImGui.BeginDisabled(busy || CollectionLocked || active.YouConfirmed || active.YourPokemon is null || active.TheirPokemon is null);
        if (DrawAccentButton(active.YouConfirmed ? "Waiting for partner" : "Review & confirm", new Vector2(-1, 32 * scale), PokemonGreen))
        { tradeConfirmationId = active.Id; tradeConfirmationRevision = active.Revision; requestTradeConfirmation = true; }
        ImGui.EndDisabled(); TradeText("Changing either offer clears both confirmations.", CompanionMuted);
    }

    private void DrawWonderTrade()
    {
        TradeText("Send one Pokémon. Receive another player's Pokémon, even while offline.", CompanionMuted);
        ImGui.Spacing();
        if (trading!.Wonder is { } deposit)
        {
            DrawTradingPokemon(deposit.Pokemon, "YOUR DEPOSIT");
            TradeText("Waiting for a match", PokemonOrange);
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Withdraw Pokémon", new Vector2(-1, 32 * CompanionScale), PokemonOrange))
                Run(() => SaveTrade(() => plugin.Api.WithdrawWonder(deposit.Id)), "Withdrawing Wonder Trade deposit...");
            ImGui.EndDisabled();
            TradeText("Keep playing. You can withdraw until matched. Your new Pokémon arrives in the same PC slot.", CompanionMuted);
            return;
        }
        var selected = TradingSelection(true);
        if (selected is not null) DrawTradingPokemon(selected, "YOUR SELECTION");
        DrawTradingChoice(true);
        var reason = TradeOfferRestriction(selected);
        ImGui.BeginDisabled(reason is not null);
        if (DrawAccentButton("Deposit Pokémon", new Vector2(-1, 32 * CompanionScale), PokemonOrange)) requestWonderConfirmation = true;
        ImGui.EndDisabled();
        TradeText(reason ?? "You can withdraw while waiting. Once matched, the trade is permanent.", CompanionMuted);
    }

    private string? TradeOfferRestriction(PokemonDto? pokemon)
    {
        if (CollectionLocked) return "Finish your battle, run or matchmaking before offering Pokémon.";
        if (pokemon is null) return "Choose a Pokémon from your party or PC Storage.";
        if (pokemon.TradeLockId is not null) return "This Pokémon is already reserved for trading.";
        if (pokemon.Fusion is not null || pokemon.FusionHostId.Length > 0) return "Unfuse this Pokémon before trading it.";
        if (IsRestrictedTradeSpecies(pokemon)) return "Legendary and Mythical Pokémon cannot be traded.";
        return pokemon.PartySlot is not null && trainer!.Party.Count <= 1 ? "Keep at least one Pokémon in your party." : null;
    }

    private void DrawTradingChoice(bool wonder)
    {
        ImGui.BeginDisabled(busy || CollectionLocked);
        if (DrawAccentSmallButton(wonder ? "Choose Pokémon##Wonder" : trading?.Active is null ? "Choose Pokémon##Direct" : "Change offer", CompanionAccent))
            OpenTradePicker(wonder);
        ImGui.EndDisabled();
    }

    private void DrawTradePair(PokemonDto? given, PokemonDto? received)
    {
        var columns = ImGui.GetContentRegionAvail().X >= 330 * CompanionScale ? 2 : 1;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 3) * CompanionScale);
        if (ImGui.BeginTable("TradePokemonPair", columns, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); DrawTradingPokemon(given, "YOU GIVE");
            ImGui.TableNextColumn(); DrawTradingPokemon(received, "YOU RECEIVE");
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawTradeInspectionPair(PokemonDto? given, PokemonDto? received)
    {
        if (!ImGui.BeginTable("TradeInspectionPair", ImGui.GetContentRegionAvail().X >= 620 * CompanionScale ? 2 : 1, ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn(); if (given is not null) { ImGui.PushID("Given"); DrawTradeInspection(given); ImGui.PopID(); }
        ImGui.TableNextColumn(); if (received is not null) { ImGui.PushID("Received"); DrawTradeInspection(received); ImGui.PopID(); }
        ImGui.EndTable();
    }

    private void DrawTradeHistory()
    {
        var receipts = trading!.Receipts;
        if (receipts.Count == 0) { TradeText("Completed trades appear here, including Wonder Trades completed while you were offline.", CompanionMuted); return; }
        DrawPager("TradeHistory", ref tradeHistoryPage, receipts.Count, 5);
        foreach (var receipt in receipts.Skip(tradeHistoryPage * 5).Take(5))
        {
            ImGui.PushID(receipt.Id);
            if (ImGui.BeginTable("TradeReceipt", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Pokémon", ImGuiTableColumnFlags.WidthFixed, 38 * CompanionScale);
                ImGui.TableSetupColumn("Receipt", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn(); DrawPokemonIcon(receipt.Received.Appearance, receipt.Received.Shiny, new Vector2(34 * CompanionScale));
                ImGui.TableNextColumn();
                TradeText($"{(receipt.Kind == "wonder" ? "Wonder Trade" : "Direct trade")} / {receipt.PartnerName} ({receipt.PartnerUid})", receipt.SeenAt is null ? PokemonYellow : CompanionAccent);
                TradeText($"Gave {receipt.Given.DisplaySpecies} / Received {(receipt.Received.Shiny ? "Shiny " : "")}{receipt.Received.DisplaySpecies}", ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                if (DateTimeOffset.TryParse(receipt.CreatedAt, out var at)) TradeText(at.ToLocalTime().ToString("g"), CompanionMuted);
                ImGui.EndTable();
            }
            if (ImGui.TreeNode("Inspect Pokémon"))
            { DrawTradePair(receipt.Given, receipt.Received); DrawTradeInspectionPair(receipt.Given, receipt.Received); ImGui.TreePop(); }
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            ImGui.BeginDisabled(busy);
            if (receipt.SeenAt is null && DrawAccentSmallButton("Mark as read", CompanionAccent))
                Run(() => SaveTrade(() => plugin.Api.ReadTradeReceipt(receipt.Id), refreshTrainer: false), "Marking trade as read...");
            if (receipt.SeenAt is null) SameLineIfFits("Find Pokémon", right);
            if (DrawAccentSmallButton("Find Pokémon", CompanionAccent)) Run(() => ViewTradedPokemon(receipt), "Opening traded Pokémon...");
            ImGui.EndDisabled(); ImGui.Separator(); ImGui.PopID();
        }
    }

    private static void TradeText(string text, Vector4 color)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X));
        ImGui.PushStyleColor(ImGuiCol.Text, color); ImGui.TextWrapped(text); ImGui.PopStyleColor(); ImGui.PopTextWrapPos();
    }
}
