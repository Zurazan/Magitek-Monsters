using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string wishlistOwner = string.Empty;
    private string wishlistPopupOwner = string.Empty;
    private string wishlistSearch = string.Empty;
    private string wishlistMessage = string.Empty;
    private CollectionWishlistPreferences? collectionWishlist;
    private bool requestCollectionWishlist;
    private bool adventureWishlistOnly;

    private void ResetCollectionWishlist()
    {
        wishlistOwner = wishlistPopupOwner = wishlistSearch = wishlistMessage = string.Empty;
        collectionWishlist = null;
        requestCollectionWishlist = adventureWishlistOnly = false;
    }

    private CollectionWishlistPreferences LocalWishlist()
    {
        if (wishlistOwner != trainer!.User.Id || collectionWishlist is null)
        {
            ResetCollectionWishlist();
            wishlistOwner = trainer.User.Id;
            plugin.Configuration.CollectionWishlists ??= new();
            collectionWishlist = CollectionWishlist.Preferences(plugin.Configuration.CollectionWishlists, wishlistOwner);
        }
        return collectionWishlist;
    }

    private void ToggleCollectionWish(string owner, string kind, string id)
    {
        if (trainer?.User.Id != owner) return;
        var preferences = LocalWishlist();
        var removing = CollectionWishlist.Contains(preferences, kind, id);
        var species = kind == "form" ? pokedexCatalog.FirstOrDefault(entry => entry.Forms.Contains(id, StringComparer.OrdinalIgnoreCase))?.Species
            ?? SpeciesDisplayCatalog.Get(id)?.BaseSpecies ?? "" : "";
        if (!CollectionWishlist.Toggle(preferences, kind, id, species)) { wishlistMessage = $"Your wishlist has {CollectionWishlist.MaximumPins} pins. Remove one to make room."; return; }
        plugin.Configuration.Save();
        wishlistMessage = removing ? "Removed from your wishlist." : "Added to your wishlist.";
        SetStatus(wishlistMessage);
    }

    private void DrawWishlistPin(string kind, string id, string? label = null)
    {
        if (trainer is null) return;
        var preferences = LocalWishlist();
        var pinned = CollectionWishlist.Contains(preferences, kind, id);
        ImGui.PushID("WishlistPin:" + kind + ":" + id);
        ImGui.BeginDisabled(!pinned && preferences.Pins.Count >= CollectionWishlist.MaximumPins);
        if (ImGui.SmallButton(label ?? (pinned ? "Unpin" : "Pin"))) ToggleCollectionWish(trainer.User.Id, kind, id);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(pinned ? "Remove from your local collection wishlist." : "Pin to your local collection wishlist.");
        ImGui.PopID();
    }

    private void DrawWishlistShortcut()
    {
        if (trainer is null) return;
        var preferences = LocalWishlist();
        if (DrawAccentSmallButton($"Wishlist ({preferences.Pins.Count})", PokemonYellow)) requestCollectionWishlist = true;
    }

    private void AddWishlistSpecies()
    {
        if (trainer is null) return;
        var preferences = LocalWishlist();
        var entry = CollectionWishlist.FindSpecies(wishlistSearch, pokedexCatalog);
        if (entry is null)
        {
            wishlistMessage = pokedexCatalog.Count == 0 ? "Pokédex data is unavailable. Try again when it has loaded."
                : "Enter a species name or National Dex number.";
            return;
        }
        if (!CollectionWishlist.Add(preferences, entry.Kind, entry.Id))
        { wishlistMessage = $"Your wishlist has {CollectionWishlist.MaximumPins} pins. Remove one to make room."; return; }
        plugin.Configuration.Save();
        wishlistSearch = string.Empty;
        wishlistMessage = $"{entry.Id} is on your wishlist.";
    }

    private void DrawCollectionWishlist(IReadOnlyList<string>? areaSpecies = null)
    {
        if (trainer is null) return;
        var preferences = LocalWishlist();
        if (requestCollectionWishlist) { wishlistPopupOwner = trainer.User.Id; ImGui.OpenPopup("Collection wishlist"); requestCollectionWishlist = false; }
        if (!ImGui.IsPopupOpen("Collection wishlist")) return;
        var scale = CompanionScale; var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(680 * scale, work.X * 0.94f), Math.Min(690 * scale, work.Y * 0.9f)), ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal("Collection wishlist", ref open, ImGuiWindowFlags.None)) return;
        if (wishlistPopupOwner != trainer.User.Id) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        DrawSectionHeader("Collection wishlist", $"{preferences.Pins.Count}/{CollectionWishlist.MaximumPins} pins / Saved on this PC for this trainer");
        ImGui.TextWrapped("Pin a species by name or Dex number, including Pokémon you haven't caught. Pin forms and goals from their own pages.");
        var pinWidth = ImGui.CalcTextSize("Pin species").X + 18 * scale;
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - pinWidth - ImGui.GetStyle().ItemSpacing.X));
        var enter = ImGui.InputTextWithHint("##WishlistSpecies", "Species name or #Dex number", ref wishlistSearch, 80, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine(); ImGui.BeginDisabled(string.IsNullOrWhiteSpace(wishlistSearch));
        if (ImGui.Button("Pin species", new Vector2(pinWidth, 0)) || enter && !string.IsNullOrWhiteSpace(wishlistSearch)) AddWishlistSpecies();
        ImGui.EndDisabled();
        if (wishlistMessage.Length > 0) ImGui.TextWrapped(wishlistMessage);
        ImGui.TextWrapped(areaSpecies is null ? "Survey an area in Adventure to check which pins are in its encounter pool."
            : "Availability below uses this area's surveyed encounter pool.");
        ImGui.Spacing();
        var footer = ImGui.GetFrameHeight() + ImGui.GetTextLineHeight() * 2 + 20 * scale;
        if (ImGui.BeginChild("CollectionWishlistList", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - footer)), false))
            DrawCollectionWishlistEntries(CollectionWishlist.Resolve(preferences, trainer, areaSpecies));
        ImGui.EndChild();
        ImGui.TextWrapped("Completed pins stay until you remove them. Pins never change encounter odds or rewards.");
        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawCollectionWishlistEntries(IReadOnlyList<CollectionWishView> entries)
    {
        if (entries.Count == 0) { ImGui.TextWrapped("Your wishlist is empty. Add a species above, or pin a form or collection goal."); return; }
        foreach (var entry in entries)
        {
            ImGui.PushID(entry.Pin.Kind + ":" + entry.Pin.Id);
            if (ImGui.BeginTable("WishlistEntry", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Entry", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 78 * CompanionScale);
                ImGui.TableNextColumn();
                var kind = entry.Pin.Kind switch { "form" => "Form", "objective" => "Goal", _ => "Species" };
                ImGui.TextColored(entry.Completed ? PokemonGreen : CompanionAccent, kind);
                ImGui.TextWrapped(entry.Name);
                ImGui.TextWrapped(entry.Progress);
                if (entry.Availability != WishlistAvailability.None)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, entry.Availability == WishlistAvailability.FormUnconfirmed ? PokemonOrange : PokemonYellow);
                    ImGui.TextWrapped(entry.Availability switch { WishlistAvailability.FormHere => "Form in this area", WishlistAvailability.FormUnconfirmed => "Species here / form unconfirmed", _ => "Species in this area" });
                    ImGui.PopStyleColor();
                }
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Remove")) ToggleCollectionWish(trainer!.User.Id, entry.Pin.Kind, entry.Pin.Id);
                ImGui.EndTable();
            }
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.PopID();
        }
    }

    private string AdventureWishlistLabel(string appearance)
    {
        if (trainer is null) return string.Empty;
        return CollectionWishlist.MatchAppearance(LocalWishlist(), appearance) switch {
            WishlistAvailability.SpeciesHere => "Wishlisted species", WishlistAvailability.FormHere => "Wishlisted form",
            WishlistAvailability.FormUnconfirmed => "Wishlist: form unconfirmed", _ => string.Empty };
    }
}
