using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public readonly record struct OnlineLobbyLayout(bool SideBySide, float SetupWidth, float TeamWidth, float SetupHeight, float TeamHeight, float Gap);

public static class PvpPresentation
{
    public static int BattleLevel(PvpFormatDto format) => Math.Clamp(format.AdjustLevel ?? format.AdjustLevelDown ?? format.MaxLevel, 1, 100);
    public static string TeamRule(PvpFormatDto format) => format.PickedTeamSize is int picked
        ? $"Register {format.MinTeamSize}-{format.MaxTeamSize} / Pick {picked} for battle"
        : $"Register {format.MinTeamSize}-{format.MaxTeamSize} / All registered Pokémon battle";
    public static string LevelRule(PvpFormatDto format) => $"All battle Pokémon sync to Lv. {BattleLevel(format)}";

    public static OnlineLobbyLayout Lobby(float width, float height, float scale)
    {
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        height = float.IsFinite(height) ? Math.Max(1, height) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4f) : 1;
        var gap = Math.Min(10 * scale, Math.Min(width, height) / 4);
        var wide = width >= 540 * scale;
        var setupWidth = wide ? Math.Min(260 * scale, width * 0.38f) : width;
        var setupHeight = wide ? height : Math.Min(210 * scale, height * 0.4f);
        return new(wide, setupWidth, wide ? Math.Max(1, width - setupWidth - gap) : width,
            setupHeight, wide ? height : Math.Max(1, height - setupHeight - gap), gap);
    }

    public static void PrepareOrder(List<int> order, int rosterCount, int chosenCount, bool newBattle)
    {
        rosterCount = Math.Clamp(rosterCount, 0, 6);
        chosenCount = Math.Clamp(chosenCount, 0, rosterCount);
        if (newBattle) { order.Clear(); if (chosenCount == rosterCount) order.AddRange(Enumerable.Range(1, rosterCount)); }
        var seen = new HashSet<int>();
        order.RemoveAll(slot => slot < 1 || slot > rosterCount || !seen.Add(slot));
        if (order.Count > chosenCount) order.RemoveRange(chosenCount, order.Count - chosenCount);
    }

    public static void Pick(List<int> order, int slot, int rosterCount, int chosenCount)
    {
        if (slot < 1 || slot > rosterCount || chosenCount < 1 || chosenCount > rosterCount) return;
        var index = order.IndexOf(slot);
        if (chosenCount == rosterCount) { if (index >= 0) order.RemoveAt(index); order.Insert(0, slot); }
        else if (index >= 0) order.RemoveAt(index);
        else if (order.Count < chosenCount) order.Add(slot);
    }

    public static void Move(List<int> order, int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= order.Count || target < 0 || target >= order.Count || Math.Abs(direction) != 1) return;
        (order[index], order[target]) = (order[target], order[index]);
    }
}
