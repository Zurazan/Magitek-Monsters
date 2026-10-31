using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private int giftPage;
    private DateTimeOffset nextGiftRefreshAt;
    private void DrawGiftNotice()
    {
        if (trainer?.Gifts is not { Unread: > 0 } inbox) return;
        var scale = CompanionScale;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.16f, 0.14f, 0.08f, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 6) * scale);
        ImGui.BeginChild("GiftNotice", new Vector2(0, ImGui.GetFrameHeight() + 12 * scale), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (ImGui.BeginTable("GiftNoticeLayout", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Open", ImGuiTableColumnFlags.WidthFixed, 110 * scale);
            ImGui.TableNextColumn(); DrawClippedText(GiftPresentation.Notice(inbox), ImGui.GetContentRegionAvail().X, PokemonYellow);
            ImGui.TableNextColumn();
            if (DrawAccentButton("View gifts", new Vector2(-1, 0), PokemonYellow)) { mainSection = 13; giftPage = 0; }
            ImGui.EndTable();
        }
        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.PopStyleColor();
    }
    private void DrawMysteryGifts()
    {
        if (trainer is null) return;
        var inbox = trainer.Gifts;
        DrawSectionHeader("Mystery Gifts", "Already delivered to your collection. No claiming required.");
        ImGui.TextColored(PokemonYellow, $"{inbox.Unread:N0} unread / {inbox.Total:N0} received");
        ImGui.SameLine(); ImGui.BeginDisabled(busy);
        if (ImGui.SmallButton("Refresh##Gifts")) Run(RefreshGiftInbox, "Checking Mystery Gifts...");
        ImGui.EndDisabled();
        if (inbox.Entries.Count == 0) { ImGui.Spacing(); ImGui.TextWrapped("No Mystery Gifts yet. Giveaways appear here, even if you were offline when they were sent."); return; }
        if (inbox.Total > inbox.Entries.Count) ImGui.TextDisabled("Showing up to 100 receipts, with unread gifts first.");
        DrawPager("GiftInbox", ref giftPage, inbox.Entries.Count, 8);
        foreach (var gift in inbox.Entries.Skip(giftPage * 8).Take(8)) DrawGiftReceipt(gift);
    }
    private void DrawGiftReceipt(GiftReceiptDto gift)
    {
        var scale = CompanionScale;
        ImGui.PushID(gift.Id);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * scale);
        ImGui.BeginChild("GiftReceipt", new Vector2(0, 154 * scale), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (ImGui.BeginTable("GiftReceiptLayout", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthFixed, 78 * scale);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 126 * scale);
            ImGui.TableNextColumn(); DrawPokemonImage(gift.Pokemon.Appearance, gift.Pokemon.Shiny, new Vector2(72 * scale));
            ImGui.TableNextColumn();
            ImGui.TextColored(gift.SeenAt is null ? PokemonYellow : CompanionAccent, gift.SeenAt is null ? "NEW MYSTERY GIFT" : "MYSTERY GIFT / RECEIVED");
            DrawClippedText(GiftPresentation.Name(gift), ImGui.GetContentRegionAvail().X, Vector4.One);
            DrawClippedText($"{gift.Pokemon.Nature} / {gift.Pokemon.Ability}", ImGui.GetContentRegionAvail().X, CompanionMuted);
            DrawClippedText(GiftPresentation.Location(gift), ImGui.GetContentRegionAvail().X, CompanionMuted);
            if (gift.Kind == "giveaway") DrawClippedText($"Event: {gift.Reference}", ImGui.GetContentRegionAvail().X, CompanionMuted);
            if (DateTimeOffset.TryParse(gift.CreatedAt, out var received)) ImGui.TextDisabled(received.ToLocalTime().ToString("g"));
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(busy || gift.PokemonId is null);
            if (DrawAccentButton("View Pokémon", new Vector2(-1, 0), CompanionAccent)) Run(() => ViewGiftPokemon(gift), "Opening gifted Pokémon...");
            ImGui.EndDisabled();
            if (gift.SeenAt is null)
            {
                ImGui.BeginDisabled(busy);
                if (DrawAccentButton("Mark as read", new Vector2(-1, 0), PokemonYellow)) Run(() => MarkGiftRead(gift.Id), "Marking gift as read...");
                ImGui.EndDisabled();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.PopStyleColor(); ImGui.PopID();
    }
    private async Task RefreshGiftInbox()
    {
        if (trainer is null) return;
        var owner = trainer.User.Id;
        nextGiftRefreshAt = DateTimeOffset.UtcNow.AddSeconds(60);
        var inbox = await InSession(plugin.Api.GetGifts(disposeCancellation.Token));
        if (trainer?.User.Id == owner) { trainer.Gifts = inbox; SetStatus("Mystery Gifts refreshed."); }
    }
    private void BackgroundGifts()
    {
        nextGiftRefreshAt = DateTimeOffset.UtcNow.AddSeconds(60);
        RunBackground(async token =>
        {
            var inbox = await InSession(plugin.Api.GetGifts(token));
            return () => { if (trainer is not null) trainer.Gifts = inbox; };
        }, () => nextGiftRefreshAt = DateTimeOffset.UtcNow.AddSeconds(60));
    }
    private async Task MarkGiftRead(string id)
    {
        if (trainer is null) return;
        var owner = trainer.User.Id;
        var inbox = await InSession(plugin.Api.ReadGift(id, disposeCancellation.Token));
        if (trainer?.User.Id == owner) { trainer.Gifts = inbox; SetStatus("Gift marked as read. Your Pokémon was already delivered."); }
    }
    private async Task ViewGiftPokemon(GiftReceiptDto gift)
    {
        if (trainer is null) return;
        var owner = trainer.User.Id;
        if (gift.SeenAt is null) await InSession(MarkGiftRead(gift.Id));
        await InSession(RefreshTrainer());
        if (trainer is null || trainer.User.Id != owner) return;
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == gift.PokemonId);
        if (pokemon is null) { SetStatus("This Pokémon is no longer in your collection."); return; }
        mainSection = pokemon.PartySlot.HasValue ? 1 : 2;
        if (mainSection == 1) selectedPartyPokemon = pokemon.Id;
        else
        {
            ObserveStorageOwner(); selectedStoragePokemon = pokemon.Id;
            storageSearch = ""; OpenStorageBox(pokemon.BoxNumber); storageType = storageGeneration = storageSort = 0;
            storageFavorites = storageShiny = storageDuplicatesOnly = false;
        }
        SetStatus("Gifted Pokémon selected.");
    }
}
