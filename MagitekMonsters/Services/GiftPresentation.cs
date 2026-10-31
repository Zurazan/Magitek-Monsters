using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public static class GiftPresentation
{
    public static string Name(GiftReceiptDto gift) => $"{(gift.Pokemon.Shiny ? "Shiny " : "")}{gift.Pokemon.DisplaySpecies} / Lv. {gift.Pokemon.Level}";
    public static string Location(GiftReceiptDto gift) => gift.PokemonId is null ? "Delivered earlier. This Pokémon is no longer in your collection."
        : gift.PartySlot is { } slot ? $"In your party / Slot {slot}" : $"In your PC / Box {gift.BoxNumber}, slot {gift.BoxSlot}";
    public static string Notice(GiftInboxDto inbox) => inbox.Unread == 1 ? "Mystery Gift received!" : $"{inbox.Unread:N0} new Mystery Gifts received!";
}
