using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool requestStorageComparison;
    private bool storageComparisonCurrentLevel;
    private string storageComparisonLeft = string.Empty;
    private string storageComparisonRight = string.Empty;
    private string storageComparisonSearch = string.Empty;

    private void OpenStorageComparison(string left, string right)
    {
        storageComparisonLeft = left;
        storageComparisonRight = right;
        storageComparisonSearch = string.Empty;
        requestStorageComparison = true;
    }

    private void DrawStorageComparison()
    {
        if (requestStorageComparison) { ImGui.OpenPopup("Compare Pokémon"); requestStorageComparison = false; }
        var scale = CompanionScale;
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(780 * scale, work.X * 0.94f), Math.Min(720 * scale, work.Y * 0.9f)), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Compare Pokémon", ImGuiWindowFlags.None)) return;
        var owned = trainer?.Party.Concat(trainer.Storage).OrderBy(pokemon => pokemon.DisplaySpecies, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pokemon => pokemon.Nickname, StringComparer.OrdinalIgnoreCase).ThenBy(pokemon => pokemon.Id, StringComparer.Ordinal).ToList() ?? [];
        var left = owned.FirstOrDefault(pokemon => pokemon.Id == storageComparisonLeft);
        var right = owned.FirstOrDefault(pokemon => pokemon.Id == storageComparisonRight);
        ImGui.TextColored(CompanionAccent, "COMPARE YOUR POKÉMON");
        ImGui.Checkbox("Use current levels", ref storageComparisonCurrentLevel);
        ImGui.TextWrapped(storageComparisonCurrentLevel ? "Actual stats at each Pokémon’s current level." : "Stats calculated at level 50 for both Pokémon. Their levels are unchanged.");
        ImGui.BeginChild("StorageComparisonBody", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - 42 * scale)), false);
        if (ImGui.BeginTable("StorageComparisonPortraits", 2, ImGuiTableFlags.SizingStretchSame))
        {
            for (var side = 0; side < 2; side++)
            {
                ImGui.TableNextColumn(); ImGui.PushID(side);
                var pokemon = side == 0 ? left : right;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ComparisonPokemon", pokemon is null ? "Choose Pokémon" : $"{pokemon.Nickname} / {pokemon.DisplaySpecies}"))
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##CompareSearch", "Search species or nickname...", ref storageComparisonSearch, 80);
                    var other = side == 0 ? right?.Id : left?.Id;
                    var matches = owned.Where(entry => entry.Id != other && (storageComparisonSearch.Length == 0 || entry.DisplaySpecies.Contains(storageComparisonSearch, StringComparison.OrdinalIgnoreCase) || entry.Nickname.Contains(storageComparisonSearch, StringComparison.OrdinalIgnoreCase))).ToList();
                    foreach (var entry in matches.Take(100))
                    {
                        var location = entry.PartySlot is int slot ? $"Party {slot}" : $"Box {entry.BoxNumber}, slot {entry.BoxSlot}";
                        if (ImGui.Selectable($"{entry.Nickname} / {entry.DisplaySpecies} / Lv. {entry.Level} / {location}##{entry.Id}", pokemon?.Id == entry.Id))
                        {
                            if (side == 0) storageComparisonLeft = entry.Id; else storageComparisonRight = entry.Id;
                            storageComparisonSearch = string.Empty;
                        }
                    }
                    if (matches.Count > 100) ImGui.TextDisabled("First 100 matches. Refine your search.");
                    ImGui.EndCombo();
                }
                if (pokemon is not null)
                {
                    DrawPokemonImage(pokemon.Appearance, pokemon.Shiny, new Vector2(78 * scale));
                    ImGui.TextWrapped($"{(pokemon.Shiny ? "★ " : "")}{pokemon.DisplaySpecies} / Lv. {pokemon.Level}");
                    DrawStarterTypeBadges(pokemon.Types.ToArray(), 64 * scale, scale);
                    ImGui.TextWrapped($"Nature: {(pokemon.MintNature.Length > 0 ? pokemon.MintNature + " (mint)" : pokemon.Nature)}");
                    ImGui.TextWrapped($"Ability: {pokemon.Ability}");
                    if (ImGui.IsItemHovered()) DrawAbilityTooltip(pokemon);
                    ImGui.TextWrapped($"Held: {itemCatalog.FirstOrDefault(item => item.Id == pokemon.HeldItem)?.Name ?? (pokemon.HeldItem.Length > 0 ? pokemon.HeldItem : "None")}");
                    ImGui.TextWrapped($"Tera: {pokemon.TeraType} / {pokemon.Gender}");
                    ImGui.TextColored(CompanionMuted, $"IV total {StorageBrowser.IvTotal(pokemon)}/186 / {StorageBrowser.PerfectIvs(pokemon)} perfect");
                    ImGui.TextColored(CompanionMuted, $"EV total {pokemon.Evs.Values.Sum()}/510");
                    ImGui.TextWrapped(string.Join(" / ", pokemon.Moves));
                }
                else ImGui.TextWrapped("This Pokémon is no longer available. Choose another from your party or PC.");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        if (left is not null && right is not null)
        {
            var leftBuild = GetPokemonBuild(left);
            var rightBuild = GetPokemonBuild(right);
            ImGui.TextColored(CompanionAccent, storageComparisonCurrentLevel ? "CURRENT STATS / IVs / EVs" : "LEVEL-50 STATS / IVs / EVs");
            if (ImGui.BeginTable("StorageCompareStats", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthFixed, Math.Min(86 * scale, ImGui.GetContentRegionAvail().X * 0.24f));
                ImGui.TableSetupColumn(left.Nickname, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(right.Nickname, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var stat in BuildStats)
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextWrapped(stat.Label);
                    foreach (var pair in new[] { (Pokemon: left, Build: leftBuild), (Pokemon: right, Build: rightBuild) })
                    {
                        ImGui.TableNextColumn();
                        var stats = storageComparisonCurrentLevel ? pair.Build?.Stats : pair.Build?.Level50Stats;
                        ImGui.TextColored(CompanionAccent, stats is null ? "Loading..." : stats.GetValueOrDefault(stat.Id).ToString("N0"));
                        ImGui.TextColored(CompanionMuted, $"IV {pair.Pokemon.Ivs.GetValueOrDefault(stat.Id)}/31 / EV {pair.Pokemon.Evs.GetValueOrDefault(stat.Id)}");
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TextWrapped("IV total is a sorting aid. Low Attack or Speed can be intentional.");
        }
        ImGui.EndChild(); ImGui.Separator();
        if (ImGui.Button("Close", new Vector2(100, 30) * scale)) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
