using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum PokemonTrainingTool { RareCandy, BottleCap, GoldBottleCap, Evs, NatureAndAbility, Tera, Gigantamax }

    private static readonly string[] PokemonTrainingTools =
        ["Rare Candy", "Bottle Cap", "Gold Bottle Cap", "EV training", "Nature & ability", "Tera type", "Gigantamax"];

    private PokemonTrainingTool pokemonTrainingTool;
    private string? summaryHeldItem;
    private int summaryIv = -1;

    private void DrawPokemonSummaryManagement(PokemonDto pokemon, bool locked)
    {
        if (pokemonSummaryPanel == PokemonSummaryPanel.Evolution)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(CompanionAccent, "Evolution & forms");
            FocusPokemonSummarySection(PokemonSummaryPanel.Evolution);
            ImGui.BeginDisabled(locked);
            DrawEvolutionCare(pokemon);
            DrawCompanionEvolutionRoutes(pokemon);
            DrawAlcremieDetails(pokemon);
            DrawRareEvolutionDetails(pokemon);
            DrawPokemonForms(pokemon);
            DrawPokemonFusion(pokemon);
            ImGui.EndDisabled();
            if (DrawAccentButton("Close evolution details", Vector2.Zero, CompanionMuted)) ClosePokemonSummaryPanel();
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (DrawPokemonSummarySection("Held item", HeldItemName(pokemon.HeldItem), PokemonSummaryPanel.HeldItem))
        {
            ImGui.BeginDisabled(locked);
            DrawHeldItemManagement(pokemon);
            ImGui.EndDisabled();
        }
        if (DrawPokemonSummarySection("Training", "Level, EVs, IVs, nature, ability, Tera", PokemonSummaryPanel.Training))
            DrawPokemonSummaryTraining(pokemon, locked);
        if (DrawPokemonSummarySection("Collection", "Nickname, favourite, capture history", PokemonSummaryPanel.Collection))
        {
            ImGui.BeginDisabled(locked);
            DrawPokemonCollection(pokemon);
            ImGui.EndDisabled();
        }
    }

    private bool DrawPokemonSummarySection(string label, string detail, PokemonSummaryPanel panel)
    {
        var open = pokemonSummaryPanel == panel;
        ImGui.SetNextItemOpen(open, ImGuiCond.Always);
        var heading = $"{label}  /  {detail}";
        var padding = ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().FramePadding.X;
        if (ImGui.CalcTextSize(heading).X + padding > ImGui.GetContentRegionAvail().X) heading = label;
        var expanded = ImGui.CollapsingHeader($"{heading}##Summary{panel}");
        if (open != expanded)
        {
            ClosePokemonSummaryPanel();
            if (expanded) pokemonSummaryPanel = panel;
        }
        FocusPokemonSummarySection(panel);
        return expanded;
    }

    private string HeldItemName(string itemId) =>
        itemId.Length == 0 ? "None" : itemCatalog.FirstOrDefault(item => item.Id == itemId)?.Name ?? itemId;

    private void DrawHeldItemManagement(PokemonDto pokemon)
    {
        var available = GetHeldItems();
        var current = pokemon.HeldItem;
        var selected = summaryHeldItem ?? current;
        if (selected != current && selected.Length > 0 && !available.Any(item => item.Id == selected))
            selected = current;
        ImGui.TextUnformatted("Equipment");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##HeldItemSelection", HeldItemName(selected)))
        {
            if (ImGui.Selectable("None", selected.Length == 0)) summaryHeldItem = selected = string.Empty;
            if (current.Length > 0 && ImGui.Selectable($"{HeldItemName(current)} (equipped)##CurrentHeld", current == selected))
                summaryHeldItem = selected = current;
            foreach (var item in available)
            {
                if (item.Id != current && ImGui.Selectable($"{item.Name} ×{InventoryCount(item.Id)}##Held{item.Id}", item.Id == selected))
                    summaryHeldItem = selected = item.Id;
            }
            ImGui.EndCombo();
        }
        if (available.Count == 0) ImGui.TextWrapped("No held items are currently in your Bag.");
        ImGui.BeginDisabled(selected == current);
        if (DrawAccentButton("Save held item", Vector2.Zero, PokemonGreen))
        {
            var itemId = selected.Length == 0 ? null : selected;
            Run(() => SetHeldItem(pokemon.Id, itemId), itemId is null ? "Returning held item to the Bag..." : $"Equipping {HeldItemName(itemId)}...");
        }
        ImGui.EndDisabled();
        DrawMegaEvolutionGuide(pokemon);
    }

    private void DrawPokemonSummaryTraining(PokemonDto pokemon, bool locked)
    {
        var tool = (int)pokemonTrainingTool;
        ImGui.TextUnformatted("Training option");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##PokemonTrainingOption", ref tool, PokemonTrainingTools, PokemonTrainingTools.Length))
        {
            pokemonTrainingTool = (PokemonTrainingTool)tool;
            summaryIv = -1;
        }
        DrawTeamBuildingSummary(pokemon);
        ImGui.BeginDisabled(locked || busy);
        switch (pokemonTrainingTool)
        {
            case PokemonTrainingTool.RareCandy: DrawRareCandyTraining(pokemon); break;
            case PokemonTrainingTool.BottleCap: DrawIvTraining(pokemon, gold: false); break;
            case PokemonTrainingTool.GoldBottleCap: DrawIvTraining(pokemon, gold: true); break;
            case PokemonTrainingTool.Evs: DrawEvTraining(pokemon); break;
            case PokemonTrainingTool.NatureAndAbility: DrawNatureAndAbilityTraining(pokemon); break;
            case PokemonTrainingTool.Tera: DrawTeraTraining(pokemon); break;
            case PokemonTrainingTool.Gigantamax: DrawGigantamaxTraining(pokemon); break;
        }
        ImGui.EndDisabled();
        if (pokemonTrainingTool == PokemonTrainingTool.Evs) DrawEvTrainingConfirmation(pokemon);
    }

    private void DrawIvTraining(PokemonDto pokemon, bool gold)
    {
        const int maxIv = 31;
        var itemId = gold ? "goldbottlecap" : "bottlecap";
        var itemName = gold ? "Gold Bottle Cap" : "Bottle Cap";
        var owned = InventoryCount(itemId);
        ImGui.TextWrapped($"{itemName} ×{owned}");
        ImGui.TextWrapped(gold ? "Max all six IVs." : "Choose the IV to max.");

        if (gold)
        {
            if (ImGui.BeginTable("GoldIvPreview", ImGui.GetContentRegionAvail().X >= 330 * CompanionScale ? 3 : 2, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var stat in BuildStats)
                {
                    ImGui.TableNextColumn();
                    var value = pokemon.Ivs.GetValueOrDefault(stat.Id);
                    ImGui.TextWrapped(value == maxIv ? $"{stat.Label}  {maxIv}" : $"{stat.Label}  {value} → {maxIv}");
                }
                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            var label = summaryIv < 0 ? "Choose a stat" : BuildStats[summaryIv].Label;
            if (ImGui.BeginCombo("##BottleCapStat", label))
            {
                for (var index = 0; index < BuildStats.Length; index++)
                {
                    var stat = BuildStats[index];
                    var iv = pokemon.Ivs.GetValueOrDefault(stat.Id);
                    ImGui.BeginDisabled(iv >= maxIv);
                    if (ImGui.Selectable($"{stat.Label} ({iv}/{maxIv})", summaryIv == index)) summaryIv = index;
                    ImGui.EndDisabled();
                }
                ImGui.EndCombo();
            }
        }

        var anyImperfect = BuildStats.Any(stat => pokemon.Ivs.GetValueOrDefault(stat.Id) < maxIv);
        var target = !gold && summaryIv >= 0 ? BuildStats[summaryIv].Id : null;
        var canTrain = gold ? anyImperfect : target is not null && pokemon.Ivs.GetValueOrDefault(target) < maxIv;
        if (!anyImperfect) ImGui.TextDisabled("All six IVs are already perfect.");
        else if (!gold && target is not null)
        {
            var value = pokemon.Ivs.GetValueOrDefault(target);
            ImGui.TextWrapped(value >= maxIv ? "This IV is already perfect." : $"{BuildStats[summaryIv].Label} IV  {value} → {maxIv}");
        }
        ImGui.BeginDisabled(owned <= 0 || !canTrain);
        if (DrawAccentButton($"Use {itemName}", Vector2.Zero, PokemonGreen))
            Run(() => UseTrainingItem(pokemon.Id, itemId, target), $"Using {itemName}...");
        ImGui.EndDisabled();
    }
}
