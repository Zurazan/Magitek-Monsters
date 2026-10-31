using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static string SweetDecoration(string appearance)
    {
        if (!appearance.StartsWith("Alcremie-", StringComparison.Ordinal)) return string.Empty;
        var decoration = appearance[(appearance.LastIndexOf('-') + 1)..];
        return decoration is "Strawberry" or "Love" or "Berry" or "Clover" or "Flower" or "Star" or "Ribbon" ? decoration : string.Empty;
    }

    private void DrawAlcremieBadge(string appearance, Vector2 size)
    {
        DrawAlcremieBadgeAt(appearance, size, ImGui.GetItemRectMax());
    }

    private void DrawAlcremieBadgeAt(string appearance, Vector2 size, Vector2 max)
    {
        var decoration = SweetDecoration(appearance);
        if (decoration.Length == 0) return;
        var path = plugin.Assets.Item(decoration.ToLowerInvariant() + "sweet");
        if (path.Length == 0) return;
        var texture = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        if (texture is null) return;
        var edge = Math.Clamp(Math.Min(size.X, size.Y) * 0.4f, 14f, 32f);
        var min = max - new Vector2(edge, edge);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.08f, 0.1f, 0.15f, 0.92f)), 4f);
        draw.AddImage(texture.Handle, min, max);
        if (ImGui.IsMouseHoveringRect(min, max)) ImGui.SetTooltip(decoration + " Sweet decoration");
    }

    private void DrawAlcremieDetails(PokemonDto pokemon)
    {
        if (pokemon.Species != "Alcremie" || !ImGui.TreeNode($"Cream & decoration##Alcremie{pokemon.Id}")) return;
        DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(64, 64));
        ImGui.SameLine(); ImGui.BeginGroup();
        ImGui.TextColored(PokemonBlue, pokemon.Form.Length == 0 ? "Vanilla Cream" : pokemon.Form.Replace('-', ' '));
        ImGui.TextColored(PokemonYellow, pokemon.AlcremieDecoration + " Sweet");
        ImGui.TextWrapped("Chosen when Milcery evolved. Cream and decoration are permanent; held items do not change them.");
        ImGui.EndGroup();
        ImGui.TreePop();
    }

    private void DrawEvolutionButton(PokemonDto pokemon, EvolutionOptionDto evolution)
    {
        var id = $"{pokemon.Id}{evolution.TargetSpecies}";
        if (DrawAccentButton($"EVOLVE → {EvolutionLabel(evolution.TargetSpecies)}##Evolve{id}", Vector2.Zero, PokemonYellow))
        {
            if (evolution.TargetAppearance.Length == 0)
                Run(() => EvolvePokemon(pokemon.Id, evolution.TargetSpecies), $"Evolving {pokemon.Nickname}...");
            else ImGui.OpenPopup($"Confirm Alcremie##{id}");
        }
        if (!ImGui.BeginPopup($"Confirm Alcremie##{id}")) return;
        DrawPokemonIcon(evolution.TargetAppearance, pokemon.Shiny, new Vector2(64, 64));
        ImGui.TextColored(PokemonBlue, evolution.TargetAppearance["Alcremie-".Length..].Replace('-', ' '));
        ImGui.TextWrapped("This consumes the held Sweet. Both cream and decoration are permanent.");
        if (DrawAccentButton("Confirm evolution", Vector2.Zero, PokemonGreen))
        {
            Run(() => EvolvePokemon(pokemon.Id, evolution.TargetSpecies), $"Evolving {pokemon.Nickname}...");
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
