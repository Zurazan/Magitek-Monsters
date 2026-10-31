using System.Linq;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static string EvolutionLabel(string species) => species switch
    {
        "Maushold" => "Maushold (Family of Three)", "Maushold-Four" => "Maushold (Family of Four)",
        "Dudunsparce" => "Dudunsparce (Two-Segment)", "Dudunsparce-Three-Segment" => "Dudunsparce (Three-Segment)",
        _ => species
    };

    private void DrawRareEvolutionDetails(PokemonDto pokemon)
    {
        if (pokemon.Species is "Maushold" or "Dudunsparce")
        {
            ImGui.TextColored(PokemonBlue, EvolutionLabel(pokemon.DisplaySpecies));
            ImGui.TextDisabled("Permanent form. It cannot be restyled.");
        }
        else if (pokemon.Species is "Tandemaus" or "Dunsparce")
        {
            var option = trainer?.EvolutionOptions.FirstOrDefault(entry => entry.PokemonId == pokemon.Id);
            if (option is null) return;
            ImGui.TextColored(PokemonBlue, $"Fixed evolution: {EvolutionLabel(option.TargetSpecies)}");
            ImGui.TextWrapped("Rare forms occur in 1% of Pokémon. This Pokémon's outcome cannot be rerolled by waiting, training or reconnecting.");
        }
    }
}
