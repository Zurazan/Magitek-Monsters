using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private sealed class PokemonOptionIndex<T>(Func<T, string> pokemonId)
    {
        private List<T>? source;
        private int count;
        private Dictionary<string, T[]>? entries;

        public T[] Get(List<T>? options, string id)
        {
            if (options is null) { source = null; entries = null; count = 0; return []; }
            if (!ReferenceEquals(source, options) || count != options.Count)
            {
                entries = options.GroupBy(pokemonId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                source = options; count = options.Count;
            }
            return entries!.GetValueOrDefault(id) ?? [];
        }
    }

    private PokemonOptionIndex<EvolutionOptionDto>? evolutionOptionIndex;
    private PokemonOptionIndex<FormOptionDto>? formOptionIndex;
    private PokemonOptionIndex<FusionOptionDto>? fusionOptionIndex;

    private EvolutionOptionDto[] GetEvolutionOptions(string id) =>
        (evolutionOptionIndex ??= new(option => option.PokemonId)).Get(trainer?.EvolutionOptions, id);
    private FormOptionDto[] GetFormOptions(string id) =>
        (formOptionIndex ??= new(option => option.PokemonId)).Get(trainer?.FormOptions, id);
    private FusionOptionDto[] GetFusionOptions(string id) =>
        (fusionOptionIndex ??= new(option => option.PokemonId)).Get(trainer?.FusionOptions, id);
}
