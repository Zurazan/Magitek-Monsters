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
    internal static void SameLineIfFits(string label, float right)
    {
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(label).X + 2 * ImGui.GetStyle().FramePadding.X <= right) ImGui.SameLine();
    }
    private string? selectedPartyPokemon;
    private string? releasePokemonId;
    private bool requestRelease;
    private ShopItemDto? pendingPurchase;
    private int pendingPurchaseQuantity;
    private bool requestPurchase;
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

    private void DrawCompanionStats(PokemonDto pokemon)
    {
        var build = GetPokemonBuild(pokemon);
        if (build is null) ImGui.TextWrapped(pokemon.TradeLockId is null ? "Loading stats..." : "Stats unavailable while reserved for trading.");
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

    private void DrawCompanionPendingMove(PokemonDto pokemon, bool compact = false)
    {
        var learning = trainer!.PendingMoves.FirstOrDefault(entry => entry.PokemonId == pokemon.Id);
        if (learning is null) return;
        ImGui.Separator();
        if (compact)
        {
            ImGui.TextColored(PokemonYellow, $"{learning.Move} is ready to learn");
            if (!ImGui.TreeNode($"Review move##{learning.Id}")) return;
        }
        else ImGui.TextColored(CompanionAccent, "New move available");
        DrawLearningMoveDetails(learning.Move);
        ImGui.TextWrapped($"{pokemon.Nickname} can learn {learning.Move}, unlocked at level {learning.Level}. Choose a move to forget.");
        foreach (var move in pokemon.Moves)
        {
            if (DrawAccentButton($"Forget {move}", new Vector2(-1, 28 * CompanionScale), PokemonOrange))
                Run(() => ResolveMoveLearning(learning.Id, move), $"Teaching {learning.Move}...");
            DrawBattleMoveTooltip(move);
        }
        if (ImGui.Button("Keep current moves")) Run(() => ResolveMoveLearning(learning.Id, null), $"Skipping {learning.Move}...");
        if (compact) ImGui.TreePop();
        ImGui.Spacing();
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
            BoxedReleaseLocked || StorageBrowser.ReleaseProtected(pokemon), "Keep Pokémon"))
        {
            ImGui.CloseCurrentPopup();
            Run(() => ReleasePokemon(pokemon.Id, pokemon.Nickname), $"Releasing {pokemon.Nickname}...");
        }
        EndConfirmationModal();
    }
}
