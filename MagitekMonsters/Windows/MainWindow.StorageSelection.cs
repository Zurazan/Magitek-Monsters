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
    private HashSet<string>? selectionVisibleIds;
    private List<PokemonDto>? selectedStorage;
    private int selectedStorageHidden;

    private (List<PokemonDto> Selected, int Hidden) GetStorageSelection(List<PokemonDto> filtered)
    {
        if (selectedStorage is not null && ReferenceEquals(selectionStorage, trainer!.Storage)
            && ReferenceEquals(selectionFilter, filtered) && selectionIds!.SetEquals(storageSelection))
            return (selectedStorage, selectedStorageHidden);

        if (!ReferenceEquals(selectionFilter, filtered))
        {
            selectionFilter = filtered;
            selectionVisibleIds = null;
        }
        selectionStorage = trainer!.Storage;
        selectionIds ??= []; selectionIds.Clear(); selectionIds.UnionWith(storageSelection);
        selectedStorage = StorageBrowser.Selected(storageSelection, storageInventory!.Find);
        selectedStorageHidden = 0;
        if (selectedStorage.Count > 0)
        {
            selectionVisibleIds ??= filtered.Select(pokemon => pokemon.Id).ToHashSet();
            selectedStorageHidden = selectedStorage.Count(pokemon => !selectionVisibleIds.Contains(pokemon.Id));
        }
        return (selectedStorage, selectedStorageHidden);
    }

    private void ResetStorageSelectionCache()
    {
        selectionStorage = selectionFilter = selectedStorage = null;
        selectionIds = selectionVisibleIds = null;
        selectedStorageHidden = 0;
    }
}
