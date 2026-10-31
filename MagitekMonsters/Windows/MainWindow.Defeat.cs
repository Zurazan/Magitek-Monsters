using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawPveDefeatNotice(IEnumerable<PokemonDto> team, bool wild = false)
        => DrawPveDefeatNotice(PveLossRules.Cap(team.Select(pokemon => pokemon.Level).DefaultIfEmpty(1).Max()), wild);

    private void DrawPveDefeatNotice(int cap, bool wild = false)
    {
        if (trainer is null || cap <= 0) return;
        ImGui.PushStyleColor(ImGuiCol.Text, PokemonOrange);
        ImGui.TextWrapped($"{(wild ? "Defeat" : "Defeat or forfeit")}: ¥ {PveLossRules.Loss(trainer.Currency, cap):N0} at your current balance (5%, capped at ¥ {cap:N0}).");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rounded down, minimum ¥1 when you have money. Successfully escaping a wild battle costs nothing.");
        ImGui.PopStyleColor();
    }
}
