using System;
using System.Collections.Generic;

namespace MagitekMonsters.Services;

public readonly record struct AdventurePoolView(int[] SpeciesIndices, int TargetIndex);

public static class AdventurePoolFilter
{
    public static AdventurePoolView Apply(IReadOnlyList<string> species, bool notCaughtOnly, int targetIndex, Func<string, bool> caught,
        bool wishlistOnly = false, Func<string, bool>? wishlisted = null)
    {
        var visible = new List<int>(species.Count);
        var targetVisible = targetIndex == 0;
        for (var index = 0; index < species.Count; index++)
        {
            if (notCaughtOnly && caught(species[index])) continue;
            if (wishlistOnly && wishlisted?.Invoke(species[index]) != true) continue;
            visible.Add(index);
            if (targetIndex == index + 1) targetVisible = true;
        }
        return new AdventurePoolView(visible.ToArray(), targetVisible ? targetIndex : 0);
    }
}
