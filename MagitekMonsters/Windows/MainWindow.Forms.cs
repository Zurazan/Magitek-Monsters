using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawPokemonForms(PokemonDto pokemon)
    {
        var options = trainer!.FormOptions.Where(option => option.PokemonId == pokemon.Id).ToList();
        if (options.Count == 0 || !ImGui.TreeNode($"Forms##Forms{pokemon.Id}")) return;
        ImGui.TextColored(PokemonBlue, $"Current form: {pokemon.DisplaySpecies}");
        ImGui.TextWrapped("Form changes keep this Pokémon's training, shiny status and history. Held-item forms follow equipment. Battle-only transformations are not selectable here.");
        if (ImGui.BeginTable($"FormChoices{pokemon.Id}", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Form", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 125);
            foreach (var option in options)
            {
                ImGui.PushID($"{option.TargetSpecies}:{option.TargetAbility}");
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                DrawPokemonIcon(option.TargetSpecies, pokemon.Shiny, new Vector2(40, 40));
                ImGui.SameLine(); ImGui.BeginGroup();
                ImGui.TextColored(option.Current ? PokemonGreen : PokemonBlue, option.TargetSpecies);
                if (option.TargetAbility.Length > 0) ImGui.TextColored(PokemonOrange, option.TargetAbility);
                ImGui.TextDisabled(option.Mode == "trial" ? "Permanent trial unlock / Free optional change" : option.Mode == "automatic" ? "Automatic form" : option.Mode == "style" ? "Free grooming  |  Permanent trim" : option.ItemId.Length == 0 ? "Remove held item" : $"{option.ItemName}  |  {(option.Mode == "held" ? "Held" : option.Mode == "tool" ? "Reusable" : "Consumed")}");
                if (option.Condition.Length > 0) ImGui.TextWrapped(option.Condition);
                if (option.DayOnly) ImGui.TextDisabled("Requires Eorzean daytime: 06:00 to 18:00");
                ImGui.EndGroup(); ImGui.TableNextColumn();
                var daytime = evolutionEnvironment?.EorzeaMinute is >= 360 and < 1080;
                ImGui.BeginDisabled(option.Current || !option.Available || option.DayOnly && !daytime);
                if (DrawAccentButton(option.Current ? "Active" : option.Mode == "automatic" ? "Automatic" : option.Mode == "style" ? "Groom" : "Change form", new Vector2(-1, 28), PokemonBlue))
                    ImGui.OpenPopup("Confirm form change");
                ImGui.EndDisabled();
                if (option.Mode != "automatic" && !option.Current && !option.Available && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(option.Mode == "trial" ? "Complete the Battle Bond Trial in PvE > Special Trials first." : $"Buy {option.ItemName} from the Poké Mart first.");
                if (ImGui.BeginPopup("Confirm form change"))
                {
                    ImGui.TextColored(PokemonBlue, $"{pokemon.DisplaySpecies} -> {option.TargetSpecies}");
                    if (option.TargetAbility.Length > 0) ImGui.TextColored(PokemonOrange, $"Ability: {option.TargetAbility}");
                    ImGui.TextWrapped(option.Mode == "held"
                        ? "This equips the required item, or removes it for the base form. The old held item returns to your bag."
                        : option.Mode == "trial" ? "Free optional change after your trial victory. Training, moves, identity and shiny status stay. Returning to regular Greninja restores its saved ability slot. Ranked formats still enforce their own legality."
                        : option.Mode == "style" ? "Free grooming. No items or currency are used. This trim stays until you change it."
                        : option.Mode == "tool" ? "Your form-changing tool is reusable and stays in your bag." : "This uses one Nectar from your bag.");
                    if (option.RemovedMoves.Count > 0) ImGui.TextColored(PokemonOrange, $"Removed: {string.Join(", ", option.RemovedMoves)}");
                    foreach (var change in option.MoveChanges) ImGui.TextColored(PokemonOrange, change);
                    if (option.SignatureMove.Length > 0) ImGui.TextWrapped($"Appliance move: {option.SignatureMove}");
                    if (option.NeedsForgetMove)
                    {
                        ImGui.TextWrapped("Choose a move to replace:");
                        foreach (var move in pokemon.Moves)
                            if (DrawAccentButton($"Forget {move}", new Vector2(-1, 28), PokemonOrange))
                            {
                                ChangeForm(pokemon, option, move);
                                ImGui.CloseCurrentPopup();
                            }
                    }
                    else if (DrawAccentButton("Confirm change", Vector2.Zero, PokemonGreen))
                    {
                        ChangeForm(pokemon, option, null);
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.Spacing();
                    if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.TreePop();
    }
    private void ChangeForm(PokemonDto pokemon, FormOptionDto option, string? forgetMove)
    {
        Run(async () =>
        {
            var (environment, _) = EvolutionEnvironment.Read(CurrentEvolutionTheme);
            trainer = await InSession(plugin.Api.ChangeForm(pokemon.Id, option.TargetSpecies, option.TargetAbility.Length > 0 ? option.TargetAbility : null, forgetMove, environment, disposeCancellation.Token));
            SetStatus($"Form changed to {option.TargetSpecies}{(option.TargetAbility.Length > 0 ? $" ({option.TargetAbility})" : "")}.");
        }, "Changing form...");
    }
}
