using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly Vector4 CompanionAccent = new(0.65f, 0.76f, 1f, 1f);
    private static readonly Vector4 CompanionMuted = new(0.70f, 0.70f, 0.74f, 1f);
    private static readonly Vector4 CompanionSurface = new(0.13f, 0.13f, 0.14f, 1f);
    private static float CompanionScale => Math.Max(ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeight() / 17f);
    private string? selectedPartyPokemon;
    private string? selectedStoragePokemon;
    private string? releasePokemonId;
    private bool requestRelease;
    private ShopItemDto? pendingPurchase;
    private int pendingPurchaseQuantity;
    private bool requestPurchase;
    private string? managePokemonId;
    private bool requestPokemonManagement;
    private bool pokemonMovesOpen;

    private static void DrawClippedText(string text, float width, Vector4 color)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(1, width), ImGui.GetTextLineHeight());
        ImGui.Dummy(size);
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, min + size, true);
        draw.AddText(min, ImGui.GetColorU32(color), text);
        draw.PopClipRect();
        if (ImGui.IsItemHovered() && ImGui.CalcTextSize(text).X > width) ImGui.SetTooltip(text);
    }

    private void DrawCompanionParty()
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        DrawSectionHeader("Your party", $"{trainer.Party.Count}/6 Pokémon / First slot leads into battle");
        DrawPokemonCenter();
        ImGui.Spacing();
        if (DrawAccentSmallButton("Open PC", CompanionAccent)) mainSection = 2;
        ImGui.SameLine();
        ImGui.BeginDisabled(trainer.Party.Count == 0);
        if (DrawAccentSmallButton("Team analysis", PokemonBlue)) OpenTeamAnalysis();
        ImGui.EndDisabled();
        if (InventoryCount("expshare") > 0) ImGui.TextDisabled("EXP Share: double party EXP");
        DrawSavedTeams();
        var pokemon = trainer.Party.OrderBy(entry => entry.PartySlot).ToList();
        if (pokemon.Count == 0)
        {
            ImGui.TextWrapped("Your party is empty. Add Pokémon from PC Storage.");
            if (DrawAccentButton("Open PC Storage", Vector2.Zero, CompanionAccent)) mainSection = 2;
            DrawTeamAnalysis();
            return;
        }
        var selected = selectedPartyPokemon;
        if (!pokemon.Any(entry => entry.Id == selected)) selected = pokemon[0].Id;
        var layout = CompanionLayout.Selection(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y, scale);
        var listHeight = layout.SideBySide ? layout.Height : Math.Min(280 * scale, pokemon.Count * 132 * scale + 50 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("CompanionPokemonList", new Vector2(layout.ListWidth, Math.Max(1, listHeight)), false);
        foreach (var entry in pokemon)
        {
            if (DrawCompanionPokemonRow(entry, entry.Id == selected, pokemon)) selected = entry.Id;
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        selectedPartyPokemon = selected;
        if (layout.SideBySide) ImGui.SameLine(0, 12 * scale); else ImGui.Spacing();
        var detailHeight = layout.SideBySide ? layout.Height : 500 * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("CompanionPokemonInspector", new Vector2(layout.DetailWidth, detailHeight), false);
        DrawCompanionPokemonInspector(pokemon.First(entry => entry.Id == selected));
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        DrawReleaseConfirmation();
        DrawPokemonManagement();
        DrawTeamAnalysis();
    }

    private bool DrawCompanionPokemonRow(PokemonDto pokemon, bool selected, List<PokemonDto> ordered)
    {
        var scale = CompanionScale;
        ImGui.PushID(pokemon.Id);
        var min = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = Math.Max(124 * scale, ImGui.GetTextLineHeight() * 4 + 48 * scale);
        var actionWidth = Math.Max(96 * scale, ImGui.CalcTextSize("Down").X * 2 + 27 * scale);
        var clickWidth = Math.Max(1, width - actionWidth - 12 * scale);
        var clicked = ImGui.InvisibleButton("Select", new Vector2(clickWidth, height));
        var hover = ImGui.IsItemHovered();
        if (hover) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.17f, 0.22f, 1) : CompanionSurface), 4 * scale);
        if (selected || hover) draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(CompanionAccent with { W = selected ? 0.65f : 0.4f }), 4 * scale);
        if (selected) draw.AddRectFilled(min + new Vector2(0, 8 * scale), min + new Vector2(3 * scale, height - 8 * scale), ImGui.GetColorU32(CompanionAccent), 2 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(10, 16) * scale);
        DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(64 * scale));
        ImGui.SetCursorScreenPos(min + new Vector2(86, 9) * scale);
        ImGui.BeginGroup();
        var textWidth = Math.Max(1, clickWidth - 94 * scale);
        DrawClippedText($"{(pokemon.Shiny ? "★ " : "")}{pokemon.Nickname}{(pokemon.Favorite ? " [FAV]" : "")}", textWidth, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        DrawClippedText($"Lv. {pokemon.Level} / {(pokemon.PartySlot == 1 ? "Lead Pokémon" : $"Slot {pokemon.PartySlot}")}", textWidth, CompanionMuted);
        var vital = CompanionVital(pokemon);
        DrawClippedText(vital is null ? "HP unavailable" : $"HP {vital.Hp}/{vital.MaxHp}{HealthStatus(vital)}", textWidth, vital?.Hp == 0 ? PokemonRed : CompanionMuted);
        DrawProgressBar(vital is null ? 0 : vital.Hp / (float)Math.Max(1, vital.MaxHp), new Vector2(textWidth, 5 * scale), "", HpBarRed);
        var (progress, label) = ExperienceProgress(pokemon);
        DrawClippedText(label, textWidth, CompanionMuted);
        DrawProgressBar(progress, new Vector2(textWidth, 5 * scale), "", ExpBarBlue);
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vector2(min.X + width - actionWidth - 8 * scale, min.Y + 12 * scale));
        ImGui.BeginGroup();
        ImGui.BeginDisabled(CollectionLocked || pokemon.FusionHostId.Length > 0);
        var index = ordered.FindIndex(entry => entry.Id == pokemon.Id);
        ImGui.BeginDisabled(index <= 0);
        if (DrawAccentButton("Up", new Vector2((actionWidth - 5 * scale) / 2, 28 * scale), CompanionAccent)) Run(() => ReorderPokemon(pokemon.Id, index), $"Moving {pokemon.Nickname} up...");
        ImGui.EndDisabled(); ImGui.SameLine(0, 5 * scale);
        ImGui.BeginDisabled(index >= ordered.Count - 1);
        if (DrawAccentButton("Down", new Vector2((actionWidth - 5 * scale) / 2, 28 * scale), CompanionAccent)) Run(() => ReorderPokemon(pokemon.Id, index + 2), $"Moving {pokemon.Nickname} down...");
        ImGui.EndDisabled();
        if (DrawAccentButton("To PC", new Vector2(actionWidth, 28 * scale), PokemonOrange))
            Run(() => MovePokemon(pokemon.Id, "storage"), $"Storing {pokemon.Nickname}...");
        ImGui.EndDisabled(); ImGui.EndGroup();
        ImGui.SetCursorScreenPos(after);
        ImGui.PopID();
        return clicked;
    }

    private void DrawCompanionPokemonInspector(PokemonDto pokemon)
    {
        ImGui.PushID(pokemon.Id);
        var scale = CompanionScale;
        var portraitWidth = 112 * scale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - portraitWidth) / 2));
        DrawPokemonImage(pokemon.Appearance, pokemon.Shiny, new Vector2(portraitWidth));
        DrawCenteredCompanionText(pokemon.Nickname, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        DrawCenteredCompanionText($"{(pokemon.Nickname != pokemon.DisplaySpecies ? pokemon.DisplaySpecies + " / " : "")}Lv. {pokemon.Level}", CompanionMuted);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - (pokemon.Types.Count * 64 + Math.Max(0, pokemon.Types.Count - 1) * 5) * scale) / 2));
        DrawStarterTypeBadges(pokemon.Types.ToArray(), 64 * scale, scale);
        if (pokemon.FusionHostId.Length > 0)
        {
            var host = trainer!.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == pokemon.FusionHostId);
            ImGui.TextWrapped($"Fusion partner of {host?.Nickname ?? "another Pokémon"}. Unfuse the host to use or edit this Pokémon.");
            ImGui.PopID();
            return;
        }
        if (CollectionLocked) ImGui.TextWrapped("Finish your battle, run or matchmaking before editing this Pokémon.");
        ImGui.Spacing();
        var pending = trainer!.PendingMoves.Count(entry => entry.PokemonId == pokemon.Id);
        var segmentWidth = Math.Max(1, (ImGui.GetContentRegionAvail().X - 6 * scale) / 2);
        if (DrawCompanionSegment("Summary", !pokemonMovesOpen, segmentWidth)) pokemonMovesOpen = false;
        ImGui.SameLine(0, 6 * scale);
        if (DrawCompanionSegment(pending > 0 ? $"Moves ({pending})" : "Moves", pokemonMovesOpen, segmentWidth)) pokemonMovesOpen = true;
        ImGui.Spacing();
        if (pokemonMovesOpen)
        {
            DrawCompanionKnownMoves(pokemon);
            ImGui.BeginDisabled(CollectionLocked);
            DrawCompanionPendingMove(pokemon);
            DrawMoveRelearner(pokemon);
            DrawMoveLicenses(pokemon);
            ImGui.EndDisabled();
        }
        else
        {
            DrawCompanionPokemonSummary(pokemon);
            ImGui.BeginDisabled(CollectionLocked);
            DrawReadyEvolutionActions(pokemon);
            ImGui.EndDisabled();
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (DrawAccentButton("Manage Pokémon", new Vector2(-1, 32 * scale), CompanionAccent))
        {
            managePokemonId = pokemon.Id;
            requestedManagementTab = null;
            requestPokemonManagement = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Held items, training, evolution, forms and collection details.");
        ImGui.PopID();
    }

    private static void DrawCenteredCompanionText(string text, Vector4 color)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) / 2));
        ImGui.TextColored(color, text);
    }

    private static bool DrawCompanionSegment(string label, bool selected, float width)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? new Vector4(0.20f, 0.25f, 0.33f, 1) : new Vector4(0.16f, 0.16f, 0.18f, 1));
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? CompanionAccent : CompanionMuted);
        var clicked = ImGui.Button(label, new Vector2(width, 30 * CompanionScale));
        ImGui.PopStyleColor(2);
        return clicked;
    }

    private void DrawCompanionKnownMoves(PokemonDto pokemon)
    {
        var build = GetPokemonBuild(pokemon);
        foreach (var name in pokemon.Moves)
        {
            var move = build?.Moves.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            ImGui.TextWrapped(name);
            if (move is not null)
            {
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(move.Description);
                ImGui.TextColored(TypeColor(move.Type), $"{move.Type} / {move.Category}");
                var pp = CompanionVital(pokemon)?.Moves.FirstOrDefault(entry => entry.Id == SpriteNames.Normalize(move.Name).ToLowerInvariant());
                ImGui.TextDisabled($"Power {move.Power} / Accuracy {move.Accuracy?.ToString() ?? "Always"} / PP {(pp is null ? move.Pp.ToString() : $"{pp.Pp}/{pp.MaxPp}")}");
            }
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
    }

    private void DrawPokemonManagement()
    {
        if (requestPokemonManagement) { ImGui.OpenPopup("Manage Pokémon"); requestPokemonManagement = false; }
        if (!ImGui.IsPopupOpen("Manage Pokémon")) return;
        var scale = CompanionScale;
        var workSize = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(800 * scale, workSize.X * 0.9f), Math.Min(720 * scale, workSize.Y * 0.85f)), ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal("Manage Pokémon", ref open, ImGuiWindowFlags.None)) return;
        var pokemon = trainer?.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == managePokemonId);
        if (pokemon is null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.PushID(pokemon.Id);
        DrawSectionHeader($"{pokemon.Nickname} / Lv. {pokemon.Level}", "Held items, training and permanent collection changes");
        var locked = CollectionLocked || pokemon.FusionHostId.Length > 0;
        if (locked) ImGui.TextWrapped("Finish your battle or run, or unfuse this Pokémon, before making changes.");
        if (ImGui.BeginTabBar("PokemonManagementTabs", ImGuiTabBarFlags.FittingPolicyResizeDown))
        {
            if (ImGui.BeginTabItem("Held item", requestedManagementTab == "Held item" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                ImGui.BeginDisabled(locked); DrawHeldItemManagement(pokemon); ImGui.EndDisabled();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Training", requestedManagementTab == "Training" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                DrawTeamBuildingSummary(pokemon);
                ImGui.BeginDisabled(locked);
                DrawEvTraining(pokemon);
                ImGui.Separator();
                DrawRareCandyTraining(pokemon); DrawIvTraining(pokemon);
                DrawNatureAndAbilityTraining(pokemon); DrawTeraTraining(pokemon);
                ImGui.EndDisabled();
                DrawEvTrainingConfirmation(pokemon);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Evolution & forms", requestedManagementTab == "Evolution & forms" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                ImGui.BeginDisabled(locked);
                DrawEvolutionCare(pokemon); DrawCompanionEvolutionRoutes(pokemon); DrawAlcremieDetails(pokemon); DrawRareEvolutionDetails(pokemon);
                DrawPokemonForms(pokemon); DrawPokemonFusion(pokemon);
                ImGui.EndDisabled(); ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Collection"))
            {
                ImGui.BeginDisabled(locked);
                DrawPokemonCollection(pokemon);
                ImGui.EndDisabled();
                ImGui.EndTabItem();
            }
            requestedManagementTab = null;
            ImGui.EndTabBar();
        }
        ImGui.PopID(); ImGui.EndPopup();
    }

    private void DrawCompanionPokemonSummary(PokemonDto pokemon)
    {
        var nature = string.IsNullOrWhiteSpace(pokemon.MintNature) ? pokemon.Nature : pokemon.MintNature;
        if (ImGui.BeginTable("PokemonProperties", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 68 * CompanionScale);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            foreach (var entry in new[] { ("Nature", nature), ("Ability", pokemon.Ability), ("Held item", itemCatalog.FirstOrDefault(item => item.Id == pokemon.HeldItem)?.Name ?? (pokemon.HeldItem.Length > 0 ? pokemon.HeldItem : "None")), ("Tera type", pokemon.TeraType) })
            {
                ImGui.TableNextColumn(); ImGui.TextColored(CompanionMuted, entry.Item1);
                ImGui.TableNextColumn(); ImGui.TextWrapped(entry.Item2);
                if (entry.Item1 == "Ability") DrawAbilityTooltip(pokemon);
            }
            ImGui.EndTable();
        }
        var vital = CompanionVital(pokemon);
        if (vital is not null)
        {
            ImGui.Spacing();
            ImGui.TextColored(vital.Hp == 0 ? PokemonRed : CompanionMuted, $"HP {vital.Hp}/{vital.MaxHp}{HealthStatus(vital)}");
            DrawProgressBar(vital.Hp / (float)Math.Max(1, vital.MaxHp), new Vector2(-1, 7 * CompanionScale), "", HpBarRed);
        }
        var (progress, label) = ExperienceProgress(pokemon);
        ImGui.Spacing();
        ImGui.TextColored(CompanionMuted, "EXP");
        DrawProgressBar(progress, new Vector2(-1, 7 * CompanionScale), "", ExpBarBlue);
        ImGui.TextDisabled(label);
        var build = GetPokemonBuild(pokemon);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (build is null) ImGui.TextDisabled("Loading stats...");
        else if (ImGui.BeginTable("CompanionStats", 3, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var stat in new[] { ("hp", "HP"), ("atk", "ATK"), ("def", "DEF"), ("spa", "SP. ATK"), ("spd", "SP. DEF"), ("spe", "SPEED") })
            {
                ImGui.TableNextColumn();
                ImGui.TextColored(CompanionMuted, stat.Item2);
                ImGui.TextUnformatted(build.Stats.GetValueOrDefault(stat.Item1).ToString());
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"IV {pokemon.Ivs.GetValueOrDefault(stat.Item1)}/31 / EV {pokemon.Evs.GetValueOrDefault(stat.Item1)}/252");
            }
            ImGui.EndTable();
        }
    }

    private LeagueVitalDto? CompanionVital(PokemonDto pokemon)
    {
        if (battle is { Status: "active", FrontierId: null } && battle.PartyVitals.FirstOrDefault(entry => entry.PokemonId == pokemon.Id) is { } live) return live;
        if (LeagueBusy && trainer?.LeagueRun?.Vitals.FirstOrDefault(entry => entry.PokemonId == pokemon.Id) is { } league) return league;
        return pokemon.Vitals;
    }

    private static bool NeedsRest(PokemonDto pokemon) => pokemon.Vitals is { } vital
        && (vital.Hp < vital.MaxHp || vital.Status.Length > 0 || vital.Moves.Any(move => move.Pp < move.MaxPp));

    private static string HealthStatus(LeagueVitalDto vital) => vital.Hp == 0 ? " / Fainted" : vital.Status.Length > 0 ? $" / {vital.Status.ToUpperInvariant()}" : "";

    private void DrawCompanionPendingMove(PokemonDto pokemon)
    {
        var learning = trainer!.PendingMoves.FirstOrDefault(entry => entry.PokemonId == pokemon.Id);
        if (learning is null) return;
        ImGui.Separator();
        ImGui.TextColored(CompanionAccent, "New move available");
        ImGui.TextWrapped($"{pokemon.Nickname} can learn {learning.Move}, unlocked at level {learning.Level}. Choose a move to forget.");
        foreach (var move in pokemon.Moves)
            if (DrawAccentButton($"Forget {move}", new Vector2(-1, 28 * CompanionScale), PokemonOrange))
                Run(() => ResolveMoveLearning(learning.Id, move), $"Teaching {learning.Move}...");
        if (ImGui.Button("Keep current moves")) Run(() => ResolveMoveLearning(learning.Id, null), $"Skipping {learning.Move}...");
        ImGui.Spacing();
    }

    private void DrawCompanionEvolutionRoutes(PokemonDto pokemon)
    {
        var options = trainer!.EvolutionOptions.Where(entry => entry.PokemonId == pokemon.Id).ToArray();
        ImGui.TextColored(CompanionAccent, "Evolution routes");
        if (pokemon.HeldItem.Equals("everstone", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextWrapped("Evolution is blocked by Everstone. Remove it from Held item when you want this Pokémon to evolve.");
            return;
        }
        if (options.Length == 0) ImGui.TextWrapped("No further evolution routes are available for this Pokémon.");
        if (options.Any(entry => entry.ReadyWithoutEnvironment.HasValue)) ImGui.TextWrapped(evolutionEnvironmentLabel);
        foreach (var option in options)
        {
            DrawPokemonIcon(option.TargetAppearance.Length > 0 ? option.TargetAppearance : option.TargetSpecies, pokemon.Shiny, new Vector2(40 * CompanionScale));
            ImGui.SameLine(); ImGui.TextWrapped($"{EvolutionLabel(option.TargetSpecies)} / {(option.Ready ? "Ready" : "Requirements")}");
            ImGui.TextWrapped(option.Requirement);
            if (option.Ready) DrawEvolutionButton(pokemon, option);
            ImGui.Separator();
        }
    }

    private void DrawReleaseConfirmation()
    {
        if (requestRelease) { ImGui.OpenPopup("Release Pokémon?"); requestRelease = false; }
        if (!BeginConfirmationModal("Release Pokémon?")) return;
        var pokemon = trainer?.Storage.FirstOrDefault(entry => entry.Id == releasePokemonId);
        if (pokemon is null) { ImGui.CloseCurrentPopup(); EndConfirmationModal(); return; }
        ImGui.TextWrapped($"Release {pokemon.Nickname} permanently?");
        ImGui.TextWrapped("This cannot be undone. Its held item returns to your bag.");
        if (DrawConfirmationActions("Confirm release", PokemonRed,
            CollectionLocked || pokemon.Favorite || pokemon.FusionHostId.Length > 0 || pokemon.Fusion is not null, "Keep Pokémon"))
        {
            ImGui.CloseCurrentPopup();
            Run(() => ReleasePokemon(pokemon.Id, pokemon.Nickname), $"Releasing {pokemon.Nickname}...");
        }
        EndConfirmationModal();
    }
}
