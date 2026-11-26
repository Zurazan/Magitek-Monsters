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
    private string TradingItemName(string id) => id.Length == 0 ? "None" : itemCatalog.FirstOrDefault(item => item.Id == id)?.Name ?? id;

    private void DrawTradingPokemon(PokemonDto? pokemon, string role)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight(); var gap = 4 * scale;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X); var content = Math.Max(1, width - 16 * scale);
        var name = pokemon is null ? "Waiting for a Pokémon" : (pokemon.Shiny ? "Shiny " : "")
            + (pokemon.Nickname.Length > 0 ? pokemon.Nickname : SpeciesDisplayCatalog.Name(pokemon.Appearance));
        var textWidth = Math.Max(1, content - 50 * scale);
        var header = Math.Max(42 * scale, ImGui.CalcTextSize(name, false, textWidth).Y + line + gap);
        var traits = pokemon is null ? "Your partner's offer appears here." : $"{pokemon.Nature} / {pokemon.Ability}";
        var held = "Held item: " + (pokemon is null ? "None" : TradingItemName(pokemon.HeldItem));
        var height = pokemon is null ? Math.Max(126 * scale, ImGui.CalcTextSize(name, false, content).Y + ImGui.CalcTextSize(traits, false, content).Y + line + 32 * scale)
            : 16 * scale + line + header + Math.Max(14 * scale, line) + ImGui.CalcTextSize(traits, false, content).Y + ImGui.CalcTextSize(held, false, content).Y + 5 * gap;
        DrawPveCardSurface("TradePokemon##" + role, height, false, out var min, out _, out var after, false);
        var y = min.Y + 8 * scale;
        At(role, CompanionAccent, content); y += line + gap;
        if (pokemon is null)
        {
            At(name, CompanionMuted, content); y += ImGui.CalcTextSize(name, false, content).Y + gap;
            At(traits, CompanionMuted, content);
        }
        else
        {
            ImGui.SetCursorScreenPos(new Vector2(min.X + 8 * scale, y)); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(42 * scale));
            ImGui.SetCursorScreenPos(new Vector2(min.X + 58 * scale, y)); Wrapped(name, textWidth, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            ImGui.SetCursorScreenPos(new Vector2(min.X + 58 * scale, y + ImGui.CalcTextSize(name, false, textWidth).Y + gap));
            Wrapped($"Lv. {pokemon.Level}", textWidth, CompanionMuted);
            y += header + gap;
            ImGui.SetCursorScreenPos(new Vector2(min.X + 8 * scale, y));
            DrawStarterTypeBadges(pokemon.Types.ToArray(), Math.Max(1, Math.Min(60 * scale, (content - Math.Max(0, pokemon.Types.Count - 1) * 5 * scale) / Math.Max(1, pokemon.Types.Count))), scale);
            y += Math.Max(14 * scale, line) + gap;
            At(traits, CompanionMuted, content); y += ImGui.CalcTextSize(traits, false, content).Y + gap;
            At(held, CompanionMuted, content);
        }
        ImGui.SetCursorScreenPos(after);

        void At(string text, Vector4 color, float available)
        { ImGui.SetCursorScreenPos(new Vector2(min.X + 8 * scale, y)); Wrapped(text, available, color); }
        static void Wrapped(string text, float available, Vector4 color)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + available); ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(text); ImGui.PopStyleColor(); ImGui.PopTextWrapPos();
        }
    }

    private void DrawTradeInspection(PokemonDto pokemon)
    {
        DrawGenderedPokemonText(pokemon.DisplaySpecies, pokemon.Gender, CompanionAccent);
        TradeText($"Nature: {pokemon.Nature}{(pokemon.MintNature.Length > 0 ? " / Mint: " + pokemon.MintNature : "")}", CompanionMuted);
        TradeText($"Ability: {pokemon.Ability} / Tera: {pokemon.TeraType}", CompanionMuted);
        TradeText("Held item: " + TradingItemName(pokemon.HeldItem), CompanionMuted);
        var compact = ImGui.GetContentRegionAvail().X < 310 * CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2, 2) * CompanionScale);
        if (ImGui.BeginTable("TradeStats", compact ? 3 : 7, ImGuiTableFlags.SizingStretchSame))
        {
            if (compact)
            {
                ImGui.TableNextColumn(); ImGui.TextDisabled("Stat"); ImGui.TableNextColumn(); ImGui.TextDisabled("IV"); ImGui.TableNextColumn(); ImGui.TextDisabled("EV");
                foreach (var stat in BuildStats)
                {
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(stat.Label);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(pokemon.Ivs.GetValueOrDefault(stat.Id).ToString());
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(pokemon.Evs.GetValueOrDefault(stat.Id).ToString());
                }
            }
            else
            {
                ImGui.TableNextColumn(); ImGui.TextDisabled("Stat");
                foreach (var stat in BuildStats)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(stat.Id switch { "hp" => "HP", "atk" => "Atk", "def" => "Def", "spa" => "SpA", "spd" => "SpD", _ => "Spe" });
                }
                foreach (var (label, values) in new[] { ("IV", pokemon.Ivs), ("EV", pokemon.Evs) })
                {
                    ImGui.TableNextColumn(); ImGui.TextDisabled(label);
                    foreach (var stat in BuildStats) { ImGui.TableNextColumn(); ImGui.TextUnformatted(values.GetValueOrDefault(stat.Id).ToString()); }
                }
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        if (ImGui.BeginTable("TradeMoves", ImGui.GetContentRegionAvail().X >= 300 * CompanionScale ? 2 : 1, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var move in pokemon.Moves) { ImGui.TableNextColumn(); TradeText(move, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]); }
            ImGui.EndTable();
        }
        TradeText($"Original Trainer: {pokemon.OriginalTrainerName} / {pokemon.OriginalTrainerUid}", CompanionMuted);
        TradeText($"Ball: {TradingItemName(pokemon.Pokeball)}", CompanionMuted);
    }

}
