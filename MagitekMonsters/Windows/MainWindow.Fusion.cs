using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawPokemonFusion(PokemonDto pokemon)
    {
        var options = trainer!.FusionOptions.Where(option => option.PokemonId == pokemon.Id).ToList();
        if (options.Count == 0 && pokemon.Fusion is null || !ImGui.TreeNode($"Fusion##Fusion{pokemon.Id}")) return;
        ImGui.TextWrapped("Fusion keeps the host's training and shiny status. The partner stays locked in its PC slot until you unfuse. Tools are reusable, with one active fusion per tool.");
        if (pokemon.Fusion is { } active)
        {
            ImGui.TextColored(PokemonBlue, $"{pokemon.DisplaySpecies} | Partner: {active.PartnerName}");
            ImGui.TextDisabled($"{active.ItemName} is in use.");
            if (DrawAccentButton("Unfuse", Vector2.Zero, PokemonOrange)) ImGui.OpenPopup("Confirm unfusion");
            if (ImGui.BeginPopup("Confirm unfusion"))
            {
                ImGui.TextColored(PokemonBlue, $"Return to {active.TargetSpecies}");
                ImGui.TextWrapped($"{active.PartnerName} will become usable in its reserved PC slot. Training gained by the host stays on the host.");
                DrawFusionMovePreview(active);
                if (DrawAccentButton("Confirm unfusion", Vector2.Zero, PokemonOrange))
                {
                    var requestId = Guid.NewGuid().ToString();
                    Run(async () =>
                    {
                        trainer = await InSession(plugin.Api.UnfusePokemon(requestId, pokemon.Id, active.FusionId, disposeCancellation.Token));
                        SetStatus("Pokémon unfused. Partner unlocked.");
                    }, "Unfusing Pokémon...");
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
        }
        foreach (var option in options)
        {
            ImGui.PushID(option.RouteId);
            DrawPokemonIcon(option.TargetSpecies, pokemon.Shiny, new Vector2(48, 48));
            ImGui.SameLine(); ImGui.BeginGroup();
            ImGui.TextColored(PokemonBlue, option.TargetSpecies);
            ImGui.TextDisabled($"{option.ItemName} + {option.PartnerSpecies}");
            ImGui.EndGroup();
            var partners = trainer.Storage.Where(partner => partner.DisplaySpecies == option.PartnerSpecies
                && partner.FusionHostId.Length == 0 && partner.Fusion is null).ToList();
            if (!option.Available) ImGui.TextColored(PokemonOrange, "Buy this tool from the Poké Mart, or unfuse its current host first.");
            if (partners.Count == 0) ImGui.TextDisabled($"Send an unfused {option.PartnerSpecies} to PC storage first.");
            foreach (var partner in partners)
            {
                ImGui.PushID(partner.Id);
                DrawPokemonIcon(partner.Appearance, partner.Shiny, new Vector2(36, 36)); ImGui.SameLine();
                ImGui.BeginDisabled(!option.Available);
                if (DrawAccentButton($"Fuse with {partner.Nickname} | Lv. {partner.Level} | Box {partner.BoxNumber}, slot {partner.BoxSlot}", Vector2.Zero, PokemonBlue))
                    ImGui.OpenPopup("Confirm fusion");
                ImGui.EndDisabled();
                if (ImGui.BeginPopup("Confirm fusion"))
                {
                    ImGui.TextColored(PokemonBlue, $"{pokemon.DisplaySpecies} + {partner.DisplaySpecies} -> {option.TargetSpecies}");
                    ImGui.TextWrapped($"{partner.Nickname} will be unavailable until unfusion. Neither Pokémon is consumed. Their individual stats and history stay intact.");
                    DrawFusionMovePreview(option);
                    if (option.NeedsForgetMove)
                    {
                        ImGui.TextWrapped("Choose a move to replace:");
                        foreach (var move in pokemon.Moves)
                            if (DrawAccentButton($"Forget {move}", new Vector2(-1, 28), PokemonOrange))
                            {
                                Fuse(pokemon, partner, option, move);
                                ImGui.CloseCurrentPopup();
                            }
                    }
                    else if (DrawAccentButton("Confirm fusion", Vector2.Zero, PokemonGreen))
                    {
                        Fuse(pokemon, partner, option, null);
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            ImGui.Spacing(); ImGui.PopID();
        }
        ImGui.TreePop();
    }
    private static void DrawFusionMovePreview(FusionOptionDto option)
    {
        if (option.RemovedMoves.Count > 0) ImGui.TextColored(PokemonOrange, $"Removed: {string.Join(", ", option.RemovedMoves)}");
        if (option.AddedMoves.Count > 0) ImGui.TextColored(PokemonBlue, $"Added: {string.Join(", ", option.AddedMoves)}");
    }
    private void Fuse(PokemonDto pokemon, PokemonDto partner, FusionOptionDto option, string? forgetMove)
    {
        var requestId = Guid.NewGuid().ToString();
        Run(async () =>
        {
            trainer = await InSession(plugin.Api.FusePokemon(requestId, pokemon.Id, partner.Id, option.TargetSpecies, forgetMove, disposeCancellation.Token));
            SetStatus($"Fused into {option.TargetSpecies}.");
        }, "Fusing Pokémon...");
    }
}
