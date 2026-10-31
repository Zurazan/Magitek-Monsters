using System;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public static class FactoryPresentation
{
    public static int Columns(float width, float scale) => Math.Clamp((int)(Math.Max(1, width) / (310 * Math.Max(0.5f, scale))), 1, 3);
    public static string SwapError(FrontierRunDto run, string outgoing, string incoming)
    {
        if (outgoing.Length == 0 || incoming.Length == 0) return "Choose one of your rentals and one opponent rental.";
        var replacement = run.Offers.FirstOrDefault(entry => entry.Id == incoming);
        if (!run.Team.Any(entry => entry.Id == outgoing) || replacement is null) return "This offer is no longer available.";
        var others = run.Team.Where(entry => entry.Id != outgoing).Select(entry => entry.Pokemon).ToList();
        static string Species(PokemonDto pokemon) => SpeciesDisplayCatalog.Get(pokemon.Appearance)?.BaseSpecies ?? pokemon.Species;
        static string Item(PokemonDto pokemon) => new(pokemon.HeldItem.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (others.Any(pokemon => Species(pokemon) == Species(replacement.Pokemon))) return "Your team cannot repeat a species.";
        var item = Item(replacement.Pokemon);
        if (item.Length > 0 && others.Any(pokemon => Item(pokemon) == item)) return "Your team cannot repeat a held item.";
        return string.Empty;
    }
}
