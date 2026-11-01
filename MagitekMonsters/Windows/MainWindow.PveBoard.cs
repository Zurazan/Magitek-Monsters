using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string? selectedPveOpponent;

    private void DrawPveTrainerBoard()
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        var circuits = PvePresentation.Circuits;
        pveTierIndex = Math.Clamp(pveTierIndex, 0, circuits.Length - 1);
        var defeated = trainer.PveProgress.DefeatedOpponentIds.ToHashSet();
        ImGui.SetNextItemWidth(150 * scale);
        if (ImGui.BeginCombo("Circuit", circuits[pveTierIndex].Label))
        {
            for (var index = 0; index < circuits.Length; index++)
                if (ImGui.Selectable(circuits[index].Label, pveTierIndex == index)) { pveTierIndex = index; pveOpponentPage = 0; }
            ImGui.EndCombo();
        }
        var visible = opponents.Where(entry => entry.RegionId is null && !entry.Legacy && entry.RematchOf is null
            && (circuits[pveTierIndex].Id == "all" || entry.Tier == circuits[pveTierIndex].Id)).ToArray();
        ImGui.Spacing();
        ImGui.TextColored(CompanionMuted, $"{circuits[pveTierIndex].Label} circuit / {visible.Count(entry => defeated.Contains(entry.Id))} of {visible.Length} cleared");
        DrawPager("PveOpponents", ref pveOpponentPage, visible.Length, 6);
        var page = visible.Skip(pveOpponentPage * 6).Take(6).ToArray();
        if (!page.Any(entry => entry.Id == selectedPveOpponent)) selectedPveOpponent = page.FirstOrDefault()?.Id;
        var layout = PvePresentation.Board(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize,
            ImGui.GetContentRegionAvail().Y, scale, ImGui.GetTextLineHeight(), page.Length,
            page.Select(entry => entry.TeamSpecies.Count).DefaultIfEmpty(0).Max());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("PveTrainerGrid", new Vector2(0, layout.GridHeight), false))
        {
            if (page.Length == 0) ImGui.TextWrapped("No trainers in this circuit.");
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4) * scale);
            if (ImGui.BeginTable("TrainerBoard", layout.Columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var opponent in page)
                {
                    ImGui.TableNextColumn();
                    var reason = PvePresentation.LockReason(opponent, defeated, opponents);
                    if (DrawPveTrainerCard(opponent, defeated.Contains(opponent.Id), reason, layout.CardHeight))
                        selectedPveOpponent = opponent.Id;
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();
        }
        ImGui.EndChild(); ImGui.PopStyleVar();
        var selected = page.FirstOrDefault(entry => entry.Id == selectedPveOpponent);
        if (selected is not null) DrawPveSelectedTrainer(selected, PvePresentation.LockReason(selected, defeated, opponents), layout.ActionHeight);
    }

    private bool DrawPveTrainerCard(OpponentDto opponent, bool cleared, string? reason, float height)
    {
        var clicked = DrawPveCardSurface($"Trainer##{opponent.Id}", height, selectedPveOpponent == opponent.Id,
            out var min, out var max, out var after);
        var scale = CompanionScale;
        var width = max.X - min.X - 24 * scale;
        var line = ImGui.GetTextLineHeight();
        ImGui.GetWindowDrawList().PushClipRect(min + new Vector2(5 * scale, 0), max - new Vector2(5 * scale, 0), true);
        ImGui.SetCursorScreenPos(min + new Vector2(12, 10) * scale);
        ImGui.BeginGroup();
        DrawClippedText(opponent.Name, width, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        DrawClippedText($"{(cleared ? "CLEARED" : reason is null ? "READY" : "LOCKED")} / Lv. {opponent.RecommendedLevel} / {opponent.TeamSize} Pokémon",
            width, cleared ? PokemonGreen : reason is null ? CompanionAccent : CompanionMuted);
        DrawClippedText($"{opponent.TrainerClass} / Difficulty {opponent.Difficulty}/5", width, CompanionMuted);
        DrawPveTeamStrip(opponent.TeamSpecies.ToArray(), width);
        ImGui.SetCursorScreenPos(new Vector2(min.X + 12 * scale, max.Y - 10 * scale - line));
        var multiplier = InventoryCount("expshare") > 0 ? 2 : 1;
        DrawClippedText($"¥ {opponent.RewardCurrency:N0}    {opponent.RewardExperience * multiplier:N0} party EXP{(multiplier > 1 ? " (x2)" : "")}", width, ExpBarBlue);
        ImGui.EndGroup();
        ImGui.GetWindowDrawList().PopClipRect();
        ImGui.SetCursorScreenPos(after);
        return clicked;
    }

    private static bool DrawPveCardSurface(string id, float height, bool selected, out Vector2 min, out Vector2 max, out Vector2 after, bool interactive = true)
    {
        var scale = CompanionScale;
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), height);
        var clicked = false;
        if (interactive) clicked = ImGui.InvisibleButton(id, size); else ImGui.Dummy(size);
        var hovered = interactive && ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        min = ImGui.GetItemRectMin(); max = ImGui.GetItemRectMax(); after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(0.16f, 0.20f, 0.27f, 1)
            : hovered ? new Vector4(0.15f, 0.17f, 0.21f, 1) : CompanionSurface), 4 * scale);
        if (hovered) draw.AddRect(min, max, ImGui.GetColorU32(CompanionAccent with { W = 0.14f }), 4 * scale, ImDrawFlags.None, 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? CompanionAccent with { W = 0.75f }
            : new Vector4(0.23f, 0.24f, 0.28f, 1)), 4 * scale);
        draw.AddRectFilled(min + new Vector2(0, 4 * scale), new Vector2(min.X + 3 * scale, max.Y - 4 * scale),
            ImGui.GetColorU32(selected ? CompanionAccent : new Vector4(0.28f, 0.30f, 0.35f, 1)), scale);
        return clicked;
    }

    private void DrawPveTeamStrip(string[] team, float width)
    {
        var scale = CompanionScale;
        var start = ImGui.GetCursorScreenPos();
        var columns = Math.Max(1, (int)(width / (44 * scale)));
        for (var index = 0; index < team.Length; index++)
        {
            ImGui.SetCursorScreenPos(start + new Vector2(index % columns * 44, index / columns * 44) * scale);
            DrawPokemonIcon(team[index], false, new Vector2(40 * scale));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{SpeciesDisplayCatalog.Name(team[index])}\n{string.Join(" / ", SpeciesDisplayCatalog.Get(team[index])?.Types ?? [])}");
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(Math.Max(1, width), Math.Max(1, (team.Length + columns - 1) / columns) * 44 * scale));
    }

    private void DrawPveSelectedTrainer(OpponentDto opponent, string? reason, float height)
    {
        var scale = CompanionScale;
        var stacked = ImGui.GetContentRegionAvail().X < 580 * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        if (ImGui.BeginChild("SelectedTrainer", new Vector2(0, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (ImGui.BeginTable("SelectedTrainerActions", stacked ? 1 : 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Trainer", ImGuiTableColumnFlags.WidthStretch);
                if (!stacked) ImGui.TableSetupColumn("Prepare", ImGuiTableColumnFlags.WidthFixed, 155 * scale);
                ImGui.TableNextColumn();
                DrawClippedText(opponent.Name, ImGui.GetContentRegionAvail().X, CompanionAccent);
                DrawClippedText(reason ?? $"Medicine: {(opponent.BagLimit is int limit ? $"{limit} actions" : "unrestricted")}",
                    ImGui.GetContentRegionAvail().X, reason is null ? CompanionMuted : PokemonYellow);
                DrawClippedText($"{trainer!.PveProgress.Medals.Count(entry => entry.OpponentId == opponent.Id)}/21 medals / {(trainer.PveProgress.DefeatedOpponentIds.Contains(opponent.Id) ? "Medal challenges unlocked" : "Clear once to unlock medals")}", ImGui.GetContentRegionAvail().X, CompanionMuted);
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(busy || CollectionLocked || reason is not null || trainer.Party.Count == 0);
                if (DrawAccentButton("Prepare battle", new Vector2(-1, 34 * scale), CompanionAccent)) OpenPveChallenge(opponent.Id);
                ImGui.EndDisabled(); ImGui.EndTable();
            }
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }

    private void DrawPveTypedTeam(string id, string[] species)
    {
        var scale = CompanionScale;
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (160 * scale)), 1, 4);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4) * scale);
        if (ImGui.BeginTable(id, columns, ImGuiTableFlags.SizingStretchSame))
        {
            for (var index = 0; index < species.Length; index++)
            {
                ImGui.TableNextColumn();
                DrawPveCardSurface($"{id}##{index}", 94 * scale + ImGui.GetTextLineHeight(), false, out var min, out var max, out var after, false);
                var width = Math.Max(1, max.X - min.X - 20 * scale);
                ImGui.SetCursorScreenPos(min + new Vector2(10, 6) * scale);
                ImGui.BeginGroup();
                DrawPokemonIcon(species[index], false, new Vector2(48 * scale));
                DrawClippedText(SpeciesDisplayCatalog.Name(species[index]), width, CompanionMuted);
                var types = SpeciesDisplayCatalog.Get(species[index])?.Types ?? [];
                DrawStarterTypeBadges(types, Math.Max(1, Math.Min(60 * scale, (width - 5 * scale) / Math.Max(1, types.Length))), scale);
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(after);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawPveTeamSelection(string id)
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        var columns = ImGui.GetContentRegionAvail().X >= 500 * scale ? 2 : 1;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4) * scale);
        if (ImGui.BeginTable(id, columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var pokemon in trainer.Party.OrderBy(entry => entry.PartySlot))
            {
                ImGui.TableNextColumn();
                var selected = pveTeamIds.Contains(pokemon.Id);
                if (DrawPveCardSurface($"{id}##{pokemon.Id}", 82 * scale + 3 * ImGui.GetTextLineHeight(), selected, out var min, out var max, out var after))
                { if (selected) pveTeamIds.Remove(pokemon.Id); else pveTeamIds.Add(pokemon.Id); }
                ImGui.SetCursorScreenPos(min + new Vector2(10, 10) * scale);
                DrawPokemonIcon(pokemon.DisplaySpecies, pokemon.Shiny, new Vector2(42 * scale));
                var width = Math.Max(1, max.X - min.X - 72 * scale);
                ImGui.SetCursorScreenPos(min + new Vector2(62, 10) * scale);
                ImGui.BeginGroup();
                DrawClippedText($"{pokemon.Nickname} / Lv. {pokemon.Level}", width, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                DrawClippedText($"{(pveTeamIds.Contains(pokemon.Id) ? "IN TEAM" : "NOT SELECTED")} / Slot {pokemon.PartySlot}", width, CompanionMuted);
                ImGui.EndGroup();
                var fullWidth = Math.Max(1, max.X - min.X - 20 * scale);
                ImGui.SetCursorScreenPos(min + new Vector2(10 * scale, 18 * scale + 2 * ImGui.GetTextLineHeight()));
                DrawClippedText($"Held item: {itemCatalog.FirstOrDefault(entry => entry.Id == pokemon.HeldItem)?.Name ?? (pokemon.HeldItem.Length == 0 ? "None" : pokemon.HeldItem)}", fullWidth, CompanionMuted);
                ImGui.SetCursorScreenPos(new Vector2(min.X + 10 * scale, max.Y - 55 * scale));
                DrawStarterTypeBadges(pokemon.Types.ToArray(), Math.Max(1, Math.Min(60 * scale, (fullWidth - 5 * scale) / Math.Max(1, pokemon.Types.Count))), scale);
                var vital = CompanionVital(pokemon);
                ImGui.SetCursorScreenPos(new Vector2(min.X + 10 * scale, max.Y - 34 * scale));
                DrawClippedText(vital is null ? "HP unavailable" : $"HP {vital.Hp}/{vital.MaxHp}{HealthStatus(vital)}", fullWidth, vital?.Hp == 0 ? PokemonOrange : CompanionMuted);
                ImGui.SetCursorScreenPos(new Vector2(min.X + 10 * scale, max.Y - 11 * scale));
                DrawProgressBar(vital is null ? 0 : vital.Hp / (float)Math.Max(1, vital.MaxHp), new Vector2(fullWidth, 5 * scale), "", HpBarRed);
                ImGui.SetCursorScreenPos(after);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }
}
