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
    private void DrawCompanionParty()
    {
        if (trainer is null) return;
        var party = trainer.Party.OrderBy(pokemon => pokemon.PartySlot).ToArray();
        var savedTeams = GetSavedTeams();
        DrawPartyToolbar(party.Length, savedTeams);
        DrawPokemonCenter();

        if (party.Length == 0)
            ImGui.TextWrapped("Your party is empty. Add Pokémon from PC Storage.");
        else
        {
            DrawPartySheet(party);
            var selected = Array.FindIndex(party, pokemon => pokemon.Id == selectedPartyPokemon);
            if (selected < 0) selected = 0;
            selectedPartyPokemon = party[selected].Id;
            DrawPartyInspector(party, selected);
        }

        DrawSavedTeamLibrary(savedTeams);
        DrawReleaseConfirmation();
        DrawPokemonManagement();
        DrawTeamAnalysis();
    }

    private void DrawPartyToolbar(int count, IReadOnlyList<SavedTeamView> savedTeams)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var right = ImGui.GetCursorScreenPos().X + width;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var savedLabel = $"Saved teams ({savedTeams.Count})";
        var toolbarWidth = PartyButtonWidth("PC Storage") + PartyButtonWidth(savedLabel)
            + PartyButtonWidth("Team analysis") + 2 * spacing;
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Party");
        ImGui.SameLine();
        ImGui.TextColored(CompanionMuted, $"{count}/6 Pokémon");
        ImGui.EndGroup();
        var headingWidth = ImGui.GetItemRectSize().X;
        if (headingWidth + toolbarWidth + spacing <= width)
            ImGui.SameLine(0, width - headingWidth - toolbarWidth);
        if (DrawAccentButton("PC Storage", Vector2.Zero, CompanionAccent)) mainSection = 2;
        SameLineIfFits(savedLabel, right);
        DrawSavedTeamButton(savedTeams);
        SameLineIfFits("Team analysis", right);
        ImGui.BeginDisabled(count == 0);
        if (DrawAccentButton("Team analysis", Vector2.Zero, CompanionAccent)) OpenTeamAnalysis();
        ImGui.EndDisabled();
        if (InventoryCount("expshare") > 0) ImGui.TextDisabled("EXP Share: double party EXP");
    }

    private void DrawPartySheet(PokemonDto[] party)
    {
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var columns = width >= 540 * scale ? 3 : width >= 295 * scale ? 2 : 1;
        if (!party.Any(pokemon => pokemon.Id == selectedPartyPokemon)) selectedPartyPokemon = party[0].Id;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 3) * scale);
        if (ImGui.BeginTable("PartySheet", columns, ImGuiTableFlags.SizingStretchSame))
        {
            for (var index = 0; index < 6; index++)
            {
                ImGui.TableNextColumn();
                if (index >= party.Length)
                {
                    DrawPartySlot(null, index);
                    continue;
                }
                var pokemon = party[index];
                if (DrawPartySlot(pokemon, index) && selectedPartyPokemon != pokemon.Id)
                {
                    selectedPartyPokemon = pokemon.Id;
                    pokemonMovesOpen = false;
                }
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private bool DrawPartySlot(PokemonDto? pokemon, int index)
    {
        var scale = CompanionScale;
        var pad = 7 * scale;
        var art = 40 * scale;
        var line = ImGui.GetTextLineHeight();
        var gap = 3 * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var textWidth = Math.Max(1, width - art - 3 * pad);
        var name = pokemon is null ? $"Empty slot {index + 1}" : PartyPokemonName(pokemon);
        var role = pokemon is null ? "" : $"{(index == 0 ? "Lead" : $"Slot {index + 1}")} / Lv. {pokemon.Level}";
        var vital = pokemon is null ? null : CompanionVital(pokemon);
        var condition = vital is not null ? PartyHealthStatus(vital) : "";
        var hasMove = pokemon is not null && trainer!.PendingMoves.Any(move => move.PokemonId == pokemon.Id);
        var notice = hasMove && condition.Length > 0 ? $"{condition} / Move ready" : hasMove ? "Move ready" : condition;
        var nameHeight = ImGui.CalcTextSize(name, false, textWidth).Y;
        var roleHeight = ImGui.CalcTextSize(role, false, textWidth).Y;
        var noticeHeight = notice.Length > 0 ? ImGui.CalcTextSize(notice, false, textWidth).Y : line;
        var headingHeight = Math.Max(art, Math.Max(line, nameHeight) + Math.Max(line, roleHeight) + noticeHeight + 2 * gap);
        var height = 2 * pad + headingHeight + line * 2 + 2 * gap;
        var selected = pokemon is not null && selectedPartyPokemon == pokemon.Id;
        ImGui.PushID(index);
        var clicked = DrawPveCardSurface("PartySlot", height, selected, out var min, out _, out var after, pokemon is not null);
        if (pokemon is null)
        {
            var nameWidth = ImGui.CalcTextSize(name).X;
            ImGui.GetWindowDrawList().AddText(min + new Vector2(Math.Max(pad, (width - nameWidth) / 2), (height - line) / 2),
                ImGui.GetColorU32(CompanionMuted), name);
            ImGui.PopID();
            return false;
        }

        var (experience, experienceLabel) = ExperienceProgress(pokemon);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{name}\n{role}\n{experienceLabel}");
        DrawPokemonIconAt(pokemon.Appearance, pokemon.Shiny, min + new Vector2(pad, pad), new Vector2(art));
        var content = min + new Vector2(art + 2 * pad, pad);
        var draw = ImGui.GetWindowDrawList();
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), content, ImGui.GetColorU32(selected ? CompanionAccent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), name, textWidth);
        content.Y += nameHeight + gap;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), content, ImGui.GetColorU32(index == 0 ? CompanionAccent : CompanionMuted), role, textWidth);
        content.Y += roleHeight + gap;
        if (notice.Length > 0)
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), content, ImGui.GetColorU32(hasMove ? PokemonYellow : CenterAccent), notice, textWidth);

        var hp = vital is null ? "HP unavailable" : $"HP {vital.Hp}/{vital.MaxHp}";
        var xp = pokemon.Level >= 100 ? "MAX LEVEL" : $"EXP {(int)(experience * 100)}%";
        var labelWidth = Math.Max(60 * scale, Math.Max(ImGui.CalcTextSize(hp).X, ImGui.CalcTextSize(xp).X));
        var start = min + new Vector2(pad, pad + headingHeight + gap);
        DrawPartyMeter(start, width - 2 * pad, labelWidth, hp, vital is null ? 0 : vital.Hp / (float)Math.Max(1, vital.MaxHp), HpBarRed);
        DrawPartyMeter(start + new Vector2(0, line + gap), width - 2 * pad, labelWidth, xp, experience, ExpBarBlue);
        ImGui.SetCursorScreenPos(after);
        ImGui.PopID();
        return clicked;
    }

    private static string PartyHealthStatus(LeagueVitalDto vital) => vital.Hp == 0 ? "Fainted" : vital.Status.ToLowerInvariant() switch
    {
        "psn" or "tox" => "Poisoned",
        "brn" => "Burned",
        "par" => "Paralyzed",
        "slp" => "Asleep",
        "frz" => "Frozen",
        _ => vital.Status
    };

    private static void DrawPartyMeter(Vector2 origin, float width, float labelWidth, string label, float ratio, Vector4 fill)
    {
        var scale = CompanionScale;
        var barWidth = width - labelWidth - 6 * scale;
        var draw = ImGui.GetWindowDrawList();
        var labelX = origin.X + Math.Max(0, width - ImGui.CalcTextSize(label).X);
        draw.AddText(new Vector2(labelX, origin.Y), ImGui.GetColorU32(CompanionMuted), label);
        if (barWidth < 1) return;
        var height = 4 * scale;
        var min = origin + new Vector2(0, (ImGui.GetTextLineHeight() - height) / 2);
        draw.AddRectFilled(min, min + new Vector2(barWidth, height), ImGui.GetColorU32(new Vector4(.035f, .045f, .07f, 1)), scale);
        if (ratio > 0)
            draw.AddRectFilled(min, min + new Vector2(barWidth * Math.Clamp(ratio, 0, 1), height), ImGui.GetColorU32(fill), scale);
    }

    private void DrawPartyInspector(PokemonDto[] party, int index)
    {
        var pokemon = party[index];
        var scale = CompanionScale;
        ImGui.PushID(pokemon.Id);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(9, 7) * scale);
        if (ImGui.BeginTable("PartyInspector", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            var pending = trainer!.PendingMoves.Count(move => move.PokemonId == pokemon.Id);
            var locked = CollectionLocked || pokemon.FusionHostId.Length > 0 || pokemon.TradeLockId is not null;
            DrawPartyInspectorHeader(pokemon, pending);
            ImGui.Separator();
            if (pokemon.FusionHostId.Length > 0)
                ImGui.TextWrapped("This Pokémon is a fusion partner. Unfuse the host to edit it.");
            else if (pokemonMovesOpen)
            {
                DrawPartyKnownMoves(pokemon);
                ImGui.BeginDisabled(locked);
                DrawCompanionPendingMove(pokemon, compact: true);
                DrawMoveRelearner(pokemon);
                DrawMoveLicenses(pokemon);
                ImGui.EndDisabled();
            }
            else
            {
                DrawPartyProperties(pokemon);
                if (ImGui.CollapsingHeader("Stats")) DrawCompanionStats(pokemon);
                ImGui.BeginDisabled(locked);
                DrawReadyEvolutionActions(pokemon);
                ImGui.EndDisabled();
            }
            ImGui.Separator();
            DrawPartyOrderControls(party, index, locked);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.PopID();
    }

    private void DrawPartyInspectorHeader(PokemonDto pokemon, int pending)
    {
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var right = ImGui.GetCursorScreenPos().X + width;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var movesLabel = pending > 0 ? $"Moves ({pending})" : "Moves";
        var toolsWidth = PartyButtonWidth("Summary") + PartyButtonWidth(movesLabel) + PartyButtonWidth("Manage") + 2 * spacing;
        ImGui.BeginGroup();
        DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(44 * scale));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextWrapped(PartyPokemonName(pokemon));
        if (pokemon.Types.Count > 0)
            DrawStarterTypeBadges(pokemon.Types.ToArray(), Math.Max(1, Math.Min(58 * scale,
                (ImGui.GetContentRegionAvail().X - (pokemon.Types.Count - 1) * 5 * scale) / pokemon.Types.Count)), scale);
        ImGui.EndGroup();
        ImGui.EndGroup();
        var headingWidth = ImGui.GetItemRectSize().X;
        if (headingWidth + toolsWidth + spacing <= width)
            ImGui.SameLine(0, width - headingWidth - toolsWidth);
        if (DrawAccentButton("Summary", Vector2.Zero, pokemonMovesOpen ? CompanionMuted : CompanionAccent)) pokemonMovesOpen = false;
        SameLineIfFits(movesLabel, right);
        if (DrawAccentButton(movesLabel + "###PartyMoves", Vector2.Zero,
            pokemonMovesOpen ? CompanionAccent : pending > 0 ? PokemonYellow : CompanionMuted)) pokemonMovesOpen = true;
        SameLineIfFits("Manage", right);
        if (DrawAccentButton("Manage", Vector2.Zero, CompanionAccent))
        {
            managePokemonId = pokemon.Id;
            requestedManagementTab = null;
            requestPokemonManagement = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Manage held items, training, evolution, and forms.");
    }

    private void DrawPartyProperties(PokemonDto pokemon)
    {
        var columns = ImGui.GetContentRegionAvail().X >= 465 * CompanionScale ? 4 : 2;
        if (!ImGui.BeginTable("PartyProperties", columns, ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn();
        ImGui.TextDisabled("Ability");
        ImGui.TextWrapped(pokemon.Ability);
        DrawAbilityTooltip(pokemon);
        ImGui.TableNextColumn();
        ImGui.TextDisabled("Nature");
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(pokemon.MintNature) ? pokemon.Nature : pokemon.MintNature);
        ImGui.TableNextColumn();
        ImGui.TextDisabled("Held item");
        if (pokemon.HeldItem.Length > 0)
        {
            DrawItemImage(pokemon.HeldItem, new Vector2(16 * CompanionScale));
            ImGui.SameLine(0, 4 * CompanionScale);
        }
        ImGui.TextWrapped(itemCatalog.FirstOrDefault(item => item.Id == pokemon.HeldItem)?.Name
            ?? (pokemon.HeldItem.Length == 0 ? "None" : pokemon.HeldItem));
        ImGui.TableNextColumn();
        ImGui.TextDisabled("Tera type");
        ImGui.TextWrapped(pokemon.TeraType);
        ImGui.EndTable();
    }

    private void DrawPartyKnownMoves(PokemonDto pokemon)
    {
        var build = GetPokemonBuild(pokemon);
        var vital = CompanionVital(pokemon);
        var columns = ImGui.GetContentRegionAvail().X >= 280 * CompanionScale ? 2 : 1;
        if (ImGui.BeginTable("PartyKnownMoves", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.Borders))
        {
            foreach (var name in pokemon.Moves)
            {
                ImGui.TableNextColumn();
                var move = build?.Moves.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var moveId = SpriteNames.Normalize(name).ToLowerInvariant();
                var pp = vital?.Moves.FirstOrDefault(entry => entry.Id == moveId);
                ImGui.TextWrapped(name);
                if (move is not null && ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{move.Description}\nPower {move.Power} / Accuracy {move.Accuracy?.ToString() ?? "Always"}");
                var detail = pp is null ? (move is null ? "" : $"{move.Pp} PP") : $"{pp.Pp}/{pp.MaxPp} PP";
                if (move is not null)
                {
                    ImGui.TextColored(TypeColor(move.Type), move.Type);
                    SameLineIfFits(detail, ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X);
                }
                if (detail.Length > 0) ImGui.TextDisabled(detail);
            }
            ImGui.EndTable();
        }
    }

    private void DrawPartyOrderControls(PokemonDto[] party, int index, bool locked)
    {
        var pokemon = party[index];
        var width = ImGui.GetContentRegionAvail().X;
        var right = ImGui.GetCursorScreenPos().X + width;
        ImGui.BeginGroup();
        DrawPartyOrderButton(index == 0 ? "Current lead" : "Set as lead", 0, index == 0, CompanionAccent);
        if (index > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip($"Swap with {party[0].Nickname}, the current lead.");
        SameLineIfFits("Move up", right);
        DrawPartyOrderButton("Move up", index - 1, index == 0, CompanionMuted);
        SameLineIfFits("Move down", right);
        DrawPartyOrderButton("Move down", index + 1, index == party.Length - 1, CompanionMuted);
        ImGui.EndGroup();
        var orderWidth = ImGui.GetItemRectSize().X;
        var storageWidth = PartyButtonWidth("Send to PC");
        if (orderWidth + ImGui.GetStyle().ItemSpacing.X + storageWidth <= width)
            ImGui.SameLine(0, width - orderWidth - storageWidth);

        ImGui.BeginDisabled(locked || party.Length == 1);
        if (DrawAccentButton("Send to PC", Vector2.Zero, PokemonOrange))
            Run(() => MovePokemon(pokemon.Id, "storage"), $"Storing {pokemon.Nickname}...");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (locked) ImGui.SetTooltip("This Pokémon cannot be moved right now.");
            else if (party.Length == 1) ImGui.SetTooltip("Keep at least one Pokémon in your party.");
        }

        void DrawPartyOrderButton(string label, int target, bool isBoundary, Vector4 accent)
        {
            var slot = isBoundary ? null : party[target].PartySlot;
            ImGui.BeginDisabled(locked || slot is null);
            if (DrawAccentButton(label, Vector2.Zero, accent) && slot is int partySlot)
                Run(() => ReorderPokemon(pokemon.Id, partySlot), $"Reordering {pokemon.Nickname}...");
            ImGui.EndDisabled();
        }
    }

    private static string PartyPokemonName(PokemonDto pokemon) =>
        $"{(pokemon.Shiny ? "★ " : "")}{(pokemon.Nickname.Length == 0 ? pokemon.DisplaySpecies : pokemon.Nickname)}{(pokemon.Favorite ? " [FAV]" : "")}";

    private static float PartyButtonWidth(string label) => ImGui.CalcTextSize(label, true).X + 2 * ImGui.GetStyle().FramePadding.X;
}
