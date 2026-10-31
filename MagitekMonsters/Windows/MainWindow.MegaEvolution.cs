using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawMegaEvolutionGuide(PokemonDto pokemon)
    {
        var routes = SpeciesDisplayCatalog.Megas(pokemon.DisplaySpecies).ToArray();
        if (routes.Length == 0 || !ImGui.TreeNode($"Mega Evolution##MegaGuide{pokemon.Id}")) return;
        ImGui.TextWrapped("One Mega Evolution per battle. Choose it alongside a move; the form does not persist after battle.");
        foreach (var route in routes)
        {
            ImGui.PushID(route.Species);
            ImGui.Separator();
            DrawPokemonIcon(route.Species, pokemon.Shiny, new Vector2(54, 54) * CompanionScale);
            if (ImGui.IsItemHovered() && !plugin.Assets.HasExactPokemon(route.Species, pokemon.Shiny))
                ImGui.SetTooltip("Dedicated Mega artwork is unavailable. The base sprite is shown as a fallback.");
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextWrapped(route.Species);
            var types = SpeciesDisplayCatalog.Get(route.Species)?.Types ?? [];
            DrawStarterTypeBadges(types, 60 * CompanionScale, CompanionScale);
            ImGui.EndGroup();
            ImGui.TextWrapped(route.Requirement);
            var matches = SpeciesDisplayCatalog.MegaMatches(route, pokemon.DisplaySpecies, pokemon.Ability, pokemon.Moves);
            if (!matches)
                ImGui.TextColored(PokemonOrange, "Form, ability or move requirement not met.");
            else if (route.ItemId.Length == 0)
                ImGui.TextColored(PokemonGreen, "Required move learned. No Mega Stone needed.");
            else if (pokemon.HeldItem == route.ItemId)
                ImGui.TextColored(PokemonGreen, route.Ability.Length > 0 ? "Equipped. The in-battle transformation is still required." : "Compatible Mega Stone equipped.");
            else if (InventoryCount(route.ItemId) > 0)
            {
                if (DrawAccentButton($"Hold {route.ItemName}##MegaEquip", Vector2.Zero, PokemonBlue))
                    Run(() => SetHeldItem(pokemon.Id, route.ItemId), $"Giving {route.ItemName} to {pokemon.Nickname}...");
            }
            else
            {
                var item = itemCatalog.FirstOrDefault(entry => entry.Id == route.ItemId);
                ImGui.TextWrapped(item?.BpOnly == true ? $"Obtain {route.ItemName} from the Battle Frontier BP exchange."
                    : $"Obtain {route.ItemName} from Poké Mart.");
            }
            ImGui.PopID();
        }
        ImGui.TextWrapped("Online battles follow their selected format. A Mega Stone being available here does not make it legal in every format.");
        ImGui.TreePop();
    }
}
