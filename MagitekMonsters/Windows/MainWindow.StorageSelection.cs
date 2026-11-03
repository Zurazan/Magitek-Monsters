using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<PokemonDto>? selectionStorage;
    private List<PokemonDto>? selectionFilter;
    private HashSet<string>? selectionIds;
    private List<PokemonDto>? selectedStorage;
    private int selectedStorageHidden;

    private (List<PokemonDto> Selected, int Hidden) GetStorageSelection(List<PokemonDto> filtered)
    {
        if (selectedStorage is not null && ReferenceEquals(selectionStorage, trainer!.Storage)
            && ReferenceEquals(selectionFilter, filtered) && selectionIds!.SetEquals(storageSelection))
            return (selectedStorage, selectedStorageHidden);
        return BuildStorageSelection(filtered);
    }

    private (List<PokemonDto> Selected, int Hidden) BuildStorageSelection(List<PokemonDto> filtered)
    {
        selectionStorage = trainer!.Storage; selectionFilter = filtered;
        selectionIds ??= []; selectionIds.Clear(); selectionIds.UnionWith(storageSelection);
        selectedStorage = StorageBrowser.Selected(trainer.Storage, storageSelection);
        var visible = filtered.Select(pokemon => pokemon.Id).ToHashSet();
        selectedStorageHidden = selectedStorage.Count(pokemon => !visible.Contains(pokemon.Id));
        return (selectedStorage, selectedStorageHidden);
    }

    private void ResetStorageSelectionCache()
    {
        selectionStorage = selectionFilter = selectedStorage = null; selectionIds = null; selectedStorageHidden = 0;
    }
}
