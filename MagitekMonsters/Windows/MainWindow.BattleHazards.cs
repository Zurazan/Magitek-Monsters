using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static void DrawBattleCardStatus(string status, float width, Vector4 color, IReadOnlyList<BattleHazardDto>? hazards, string? side = null)
    {
        if (hazards is not { Count: > 0 })
        {
            DrawClippedText(status, width, color);
            return;
        }

        var gap = ImGui.GetStyle().ItemSpacing.X;
        var shortLabel = $"!{hazards.Count}";
        var available = Math.Max(ImGui.CalcTextSize(shortLabel).X, width - ImGui.CalcTextSize(status).X - gap);
        var label = BattleHazardLabel(hazards, available, shortLabel);
        var labelWidth = Math.Min(width, ImGui.CalcTextSize(label).X);
        if (width > labelWidth + gap)
        {
            DrawClippedText(status, width - labelWidth - gap, color);
            ImGui.SameLine();
        }

        DrawClippedText(label, labelWidth, PokemonOrange);
        if (side is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(BattleHazardDetails(side, hazards));
    }

    private static string BattleHazardLabel(IReadOnlyList<BattleHazardDto> hazards, float width, string shortLabel)
    {
        var names = hazards.Select(hazard =>
        {
            var (name, maxLayers, _) = DescribeBattleHazard(hazard);
            return maxLayers > 1 ? $"{name} ×{hazard.Layers}" : name;
        }).ToArray();
        for (var count = names.Length; count > 0; count--)
        {
            var label = string.Join(" · ", names, 0, count);
            if (count < names.Length) label += $" +{names.Length - count}";
            if (ImGui.CalcTextSize(label).X <= width) return label;
        }

        var summary = $"Hazards ({hazards.Count})";
        return ImGui.CalcTextSize(summary).X <= width ? summary : shortLabel;
    }

    private static string BattleHazardDetails(string side, IReadOnlyList<BattleHazardDto> hazards)
    {
        return $"{side} hazards\n" + string.Join("\n\n", hazards.Select(hazard =>
        {
            var (name, maxLayers, effect) = DescribeBattleHazard(hazard);
            if (maxLayers > 1) name += $" ({hazard.Layers}/{maxLayers} layers)";
            return effect.Length > 0 ? $"{name}\n{effect}" : name;
        }));
    }

    private static (string Name, int MaxLayers, string Effect) DescribeBattleHazard(BattleHazardDto hazard) => hazard.Id switch
    {
        "stealthrock" => ("Stealth Rock", 1, "Rock-type damage on switch-in."),
        "spikes" => ("Spikes", 3, "Damages grounded Pokémon on switch-in."),
        "toxicspikes" => ("Toxic Spikes", 2, hazard.Layers == 2 ? "Can badly poison grounded switch-ins." : "Can poison grounded switch-ins."),
        "stickyweb" => ("Sticky Web", 1, "Can lower Speed of grounded switch-ins."),
        "gmaxsteelsurge" => ("G-Max Steelsurge", 1, "Steel-type damage on switch-in."),
        _ => (hazard.Id, 1, "")
    };
}
