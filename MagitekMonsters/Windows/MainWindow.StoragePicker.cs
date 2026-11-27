using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum StoragePickerMode { DirectTrade, WonderTrade, Raid }

    private sealed class StoragePicker(StoragePickerMode mode, string? activityId, PokemonStorageBrowser browser)
    {
        public StoragePickerMode Mode { get; } = mode;
        public string? ActivityId { get; } = activityId;
        public FriendDenInviteDto? RaidInvite { get; init; }
        public int MinimumLevel { get; init; }
        public PokemonStorageBrowser Browser { get; } = browser;
        public bool OpenRequested = true;
    }

    private void OpenTradePicker(bool wonder)
    {
        var active = wonder ? null : trading?.Active;
        var pokemon = active?.YourPokemon ?? TradingSelection(wonder);
        tradePicker = CreateStoragePicker(wonder ? StoragePickerMode.WonderTrade : StoragePickerMode.DirectTrade, active?.Id, pokemon);
    }

    private StoragePicker CreateStoragePicker(StoragePickerMode mode, string? activityId, PokemonDto? pokemon, FriendDenInviteDto? raidInvite = null, int minimumLevel = 0)
    {
        ObserveStorageOwner();
        var browser = new PokemonStorageBrowser(DrawPokemonIconAt, DrawStoragePokemonDetails, GetPokemonAlerts,
            () => CollectionEditRestriction, compact: true)
        {
            Restriction = mode == StoragePickerMode.Raid
                ? candidate => RaidPokemonRestriction(candidate, minimumLevel)
                : TradeOfferRestriction,
            SelectedId = pokemon?.Id
        };
        if (storageInventory is { } source) browser.Bind(source);
        browser.OpenBox(pokemon is { PartySlot: null, BoxNumber: > 0 } ? pokemon.BoxNumber : Math.Max(1, StorageView.Box));
        return new(mode, activityId, browser) { RaidInvite = raidInvite, MinimumLevel = minimumLevel };
    }

    private void RefreshStoragePicker(StoragePicker picker)
    {
        ObserveStorageOwner();
        if (storageInventory is { } source) picker.Browser.Bind(source);
    }

    private void DrawStoragePicker(ref StoragePicker? state, string title)
    {
        if (state is { OpenRequested: true }) { ImGui.OpenPopup(title); state.OpenRequested = false; }
        if (!ImGui.IsPopupOpen(title)) { state = null; return; }
        var scale = CompanionScale;
        var work = ImGui.GetWindowViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(970 * scale, work.X * .94f), Math.Min(690 * scale, work.Y * .94f)), ImGuiCond.Always);
        var open = true;
        if (!BeginCenteredModal(title, ref open, FixedBattlePanel | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize)) return;
        if (state is not { } picker || trainer is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        if (picker.Mode == StoragePickerMode.Raid)
            ImGui.TextWrapped(picker.MinimumLevel > 0 ? $"Choose a Pokémon at Lv. {picker.MinimumLevel} or higher."
                : "Choose one Pokémon. Its level syncs to the den.");
        RefreshStoragePicker(picker);
        var browser = picker.Browser;
        browser.DrawSearch();
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        browser.DrawAllBoxes();
        if (picker.Mode is StoragePickerMode.WonderTrade or StoragePickerMode.Raid)
            browser.DrawFilters(right, work.X);
        StorageSameLine(right, 180 * scale);
        browser.DrawBoxJump();

        var footerHeight = 32 * scale + ImGui.GetTextLineHeightWithSpacing() + 3 * ImGui.GetStyle().ItemSpacing.Y + 4 * scale;
        var availableWidth = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize;
        var bodyFlags = StorageLayout.Measure(availableWidth, scale).SideBySide
            ? ImGuiWindowFlags.None : ImGuiWindowFlags.AlwaysVerticalScrollbar;
        if (ImGui.BeginChild("StoragePickerBody", new Vector2(0, -footerHeight), false, bodyFlags))
            browser.DrawBody(afterDetails: DrawPickerHeldItem);
        ImGui.EndChild();
        DrawStoragePickerFooter(picker);
        ImGui.EndPopup();
    }

    private void DrawPickerHeldItem(PokemonDto pokemon) =>
        ImGui.TextWrapped($"Held item: {TradingItemName(pokemon.HeldItem)}");

    private string? StoragePickerRestriction(StoragePicker picker)
    {
        switch (picker.Mode)
        {
            case StoragePickerMode.Raid when picker.ActivityId != raidRoom?.Id || raidRoom is { Status: not "lobby" }:
                return "The den changed. Close and reopen the picker.";
            case StoragePickerMode.WonderTrade when trading?.Wonder is not null:
            case StoragePickerMode.DirectTrade when picker.ActivityId != trading?.Active?.Id:
                return "Trading changed. Close and reopen the picker.";
        }
        if (picker.RaidInvite is { } invite)
        {
            if (invite.ExpiresAt <= DateTimeOffset.UtcNow) return "This den invitation expired.";
            if (CollectionEditRestriction is not null) return "Finish your current activity before joining.";
        }
        return picker.Browser.Restriction!(picker.Browser.Selected);
    }

    private void DrawStoragePickerFooter(StoragePicker picker)
    {
        var reason = StoragePickerRestriction(picker);
        var pokemon = picker.Browser.Selected;
        var text = reason ?? (pokemon!.Nickname.Length > 0 ? pokemon.Nickname : pokemon.DisplaySpecies);
        DrawClippedText(text, ImGui.GetContentRegionAvail().X, reason is null ? CompanionAccent : pokemon is null ? CompanionMuted : PokemonOrange);
        var (label, accent) = picker.Mode switch
        {
            StoragePickerMode.Raid when picker.RaidInvite is not null => ("Join den", PokemonBlue),
            StoragePickerMode.Raid => ("Select for raid", PokemonBlue),
            StoragePickerMode.WonderTrade => ("Choose Pokémon", PokemonOrange),
            _ => (picker.ActivityId is null ? "Choose Pokémon" : "Update offer", CompanionAccent)
        };
        var size = new Vector2(0, 32 * CompanionScale);
        if (DrawAccentButton("Cancel", size, CompanionMuted)) ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        ImGui.BeginDisabled(reason is not null);
        if (DrawAccentButton(label, new Vector2(-1, size.Y), accent) && ApplyStoragePickerChoice(picker))
            ImGui.CloseCurrentPopup();
        ImGui.EndDisabled();
    }

    private bool ApplyStoragePickerChoice(StoragePicker picker)
    {
        if (trainer is null) return false;
        RefreshStoragePicker(picker);
        if (picker.Browser.Selected is not { } pokemon || StoragePickerRestriction(picker) is not null) return false;
        if (picker.RaidInvite is { } invite) JoinInvitedDen(invite, pokemon);
        else if (picker.Mode == StoragePickerMode.Raid) ChooseRaidPokemon(pokemon);
        else if (picker.Mode == StoragePickerMode.WonderTrade) wonderPokemonId = pokemon.Id;
        else if (trading?.Active is { } active)
        {
            var tradeId = active.Id;
            var revision = active.Revision;
            var id = pokemon.Id;
            RunTrade(() => plugin.Api.OfferTrade(tradeId, id, revision), "Updating trade offer...", "Trade offer updated.", activeTradeId: tradeId);
        }
        else tradePokemonId = pokemon.Id;
        return true;
    }
}
