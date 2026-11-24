using System;
using Dalamud.Bindings.ImGui;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool requestTradeCancellation;
    private string tradeCancellationId = "";

    private bool TradeConfirmationChanged => trading?.Active is not { } active || active.Id != tradeConfirmationId || active.Revision != tradeConfirmationRevision;

    private void DrawTradeConfirmations()
    {
        if (requestTradeConfirmation) { ImGui.OpenPopup("Confirm this trade?"); requestTradeConfirmation = false; }
        if (BeginConfirmationModal("Confirm this trade?"))
        {
            var active = trading?.Active; var changed = TradeConfirmationChanged;
            if (changed) ImGui.TextWrapped("The trade changed. Close this window and review the current offers.");
            else if (active is not null)
            {
                DrawTradePair(active.YourPokemon, active.TheirPokemon);
                ImGui.TextWrapped($"Give {active.YourPokemon?.DisplaySpecies} to {active.PartnerName} and receive {active.TheirPokemon?.DisplaySpecies}?");
            }
            ImGui.TextWrapped("Once both trainers confirm, this trade is permanent. Pokémon and held items change owners together.");
            if (DrawConfirmationActions("Confirm trade", PokemonGreen, busy || CollectionLocked || changed || active?.YouConfirmed == true || active?.YourPokemon is null || active.TheirPokemon is null))
            {
                ImGui.CloseCurrentPopup(); var id = tradeConfirmationId; var revision = tradeConfirmationRevision;
                Run(() => SaveTrade(() => plugin.Api.ConfirmTrade(id, revision)), "Confirming trade...");
            }
            EndConfirmationModal();
        }
        if (requestWonderConfirmation) { ImGui.OpenPopup("Confirm Wonder Trade?"); requestWonderConfirmation = false; }
        if (BeginConfirmationModal("Confirm Wonder Trade?"))
        {
            var selected = TradingSelection(true);
            if (selected is not null) DrawTradingPokemon(selected, "YOU DEPOSIT");
            ImGui.TextWrapped("You can withdraw while waiting. Once matched, the trade is permanent. The Pokémon and its held item will go to another trainer, even if you are offline.");
            if (DrawConfirmationActions("Deposit Pokémon", PokemonOrange, TradeOfferRestriction(selected) is not null || trading?.Wonder is not null))
            {
                ImGui.CloseCurrentPopup(); var id = selected!.Id; var request = Guid.NewGuid().ToString();
                Run(() => SaveTrade(() => plugin.Api.DepositWonder(id, request)), "Depositing Wonder Trade Pokémon...");
            }
            EndConfirmationModal();
        }
        if (requestTradeCancellation) { ImGui.OpenPopup("Cancel this trade?"); requestTradeCancellation = false; }
        if (BeginConfirmationModal("Cancel this trade?"))
        {
            var changed = trading?.Active?.Id != tradeCancellationId;
            ImGui.TextWrapped(changed ? "This trade is no longer open." : "Cancel this trade? Your reserved Pokémon returns to your control. Neither offer is exchanged.");
            if (DrawConfirmationActions("Cancel trade", PokemonRed, busy || changed, "Keep trading"))
            { ImGui.CloseCurrentPopup(); var id = tradeCancellationId; Run(() => SaveTrade(() => plugin.Api.CancelTrade(id)), "Cancelling trade..."); }
            EndConfirmationModal();
        }
    }
}
