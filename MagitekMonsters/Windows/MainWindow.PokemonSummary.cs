using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum PokemonSummaryPanel { None, Evolution, PendingMove, Relearn, Tutor, HeldItem, Training }

    private (string PokemonId, PokemonSummaryPanel Panel, PokemonTrainingTool Tool, string? HeldItem)? pokemonSummaryRequest;
    private string? pokemonSummaryId;
    private PokemonSummaryPanel pokemonSummaryPanel;
    private PokemonSummaryPanel pokemonSummaryFocus;
    private (string Species, string Appearance)? summaryEvolution;
    private (string LearningId, string Move)? summaryReplacement;
    private long pokemonSummaryGeneration;
    private string? summaryNickname;
    private bool focusSummaryNickname;

    private void OpenPokemonSummary(string pokemonId, PokemonSummaryPanel panel = PokemonSummaryPanel.None,
        PokemonTrainingTool tool = PokemonTrainingTool.RareCandy, string? heldItem = null) =>
        pokemonSummaryRequest = (pokemonId, panel, tool, heldItem);

    private void ResetPokemonSummary()
    {
        pokemonSummaryRequest = null;
        pokemonSummaryId = null;
        pokemonSummaryFocus = PokemonSummaryPanel.None;
        summaryNickname = null;
        focusSummaryNickname = false;
        ClosePokemonSummaryPanel();
    }

    private void DrawPokemonSummary()
    {
        const string title = "Pokémon summary";
        if (pokemonSummaryRequest is { } request)
        {
            ImGui.OpenPopup(title);
            pokemonSummaryRequest = null;
            ClosePokemonSummaryPanel();
            pokemonSummaryId = request.PokemonId;
            pokemonSummaryPanel = pokemonSummaryFocus = request.Panel;
            summaryNickname = null;
            focusSummaryNickname = false;
            pokemonTrainingTool = request.Tool;
            summaryHeldItem = request.HeldItem;
            pokemonSummaryGeneration = foregroundGeneration;
        }
        if (!ImGui.IsPopupOpen(title)) return;

        var scale = CompanionScale;
        var work = ImGui.GetWindowViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(740 * scale, work.X * 0.92f), Math.Min(650 * scale, work.Y * 0.88f)), ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal(title, ref open, ImGuiWindowFlags.NoSavedSettings)) return;
        var pokemon = ObserveStorageOwner()?.Find(pokemonSummaryId);
        if (pokemon is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.PushID(pokemon.Id);
        var locked = CollectionLocked || pokemon.TradeLockId is not null || pokemon.FusionHostId.Length > 0;
        var appearing = ImGui.IsWindowAppearing();
        var footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 3 + 1;
        if (ImGui.BeginChild("PokemonSummaryContent", new Vector2(0, -footerHeight)))
        {
            if (appearing) ImGui.SetScrollY(0);
            DrawPokemonSummaryContent(pokemon, locked);
            if (foregroundGeneration != pokemonSummaryGeneration && (statusIsError || busy))
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, statusIsError ? PokemonRed : CompanionMuted);
                ImGui.TextWrapped(status);
                ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();
        ImGui.Separator();
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (DrawAccentButton("Close", Vector2.Zero, CompanionMuted)) ImGui.CloseCurrentPopup();
        SameLineIfFits("Copy summary", right);
        ImGui.BeginDisabled(locked || busy);
        if (DrawAccentButton("Copy summary", Vector2.Zero, CompanionAccent)) QueueTeamExport([pokemon.Id]);
        ImGui.EndDisabled();
        ImGui.PopID();
        ImGui.EndPopup();
    }

    private void DrawPokemonSummaryContent(PokemonDto pokemon, bool locked)
    {
        var wide = ImGui.GetContentRegionAvail().X >= 550 * CompanionScale;
        DrawPokemonSummaryIdentity(pokemon, wide, locked);
        ImGui.Separator();
        DrawCollectionEditNotice();
        if (pokemon.TradeLockId is not null)
            ImGui.TextWrapped("Reserved for trading. Cancel or withdraw it from Trading to make changes.");
        if (pokemon.FusionHostId.Length > 0)
        {
            var host = storageInventory?.Find(pokemon.FusionHostId);
            ImGui.TextWrapped($"Fusion partner of {host?.Nickname ?? "another Pokémon"}. Unfuse the host to edit this Pokémon.");
        }
        var vital = CompanionVital(pokemon);
        if (wide)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(9, 3) * CompanionScale);
            if (ImGui.BeginTable("PokemonSummaryColumns", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch, 0.93f);
                ImGui.TableSetupColumn("Moves", ImGuiTableColumnFlags.WidthStretch, 1.07f);
                ImGui.TableNextColumn();
                DrawPokemonSummaryDetails(pokemon, vital);
                ImGui.TableNextColumn();
                DrawPokemonSummaryMoves(pokemon, vital, locked);
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();
        }
        else
        {
            DrawPokemonSummaryDetails(pokemon, vital);
            ImGui.Spacing();
            ImGui.Separator();
            DrawPokemonSummaryMoves(pokemon, vital, locked);
        }
        DrawPokemonSummaryManagement(pokemon, locked || busy);
    }

    private void DrawPokemonSummaryIdentity(PokemonDto pokemon, bool wide, bool locked)
    {
        var scale = CompanionScale;
        var art = Math.Clamp(ImGui.GetContentRegionAvail().X / 4, 1, 68 * scale);
        var location = pokemon.PartySlot is int slot ? $"Party slot {slot}" : $"Box {pokemon.BoxNumber} / Slot {pokemon.BoxSlot}";
        if (!ImGui.BeginTable("PokemonSummaryIdentity", wide ? 3 : 2, ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthFixed, art);
        ImGui.TableSetupColumn("Identity", ImGuiTableColumnFlags.WidthStretch);
        if (wide) ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(location).X);
        ImGui.TableNextColumn();
        DrawPokemonImage(pokemon.Appearance, pokemon.Shiny, new Vector2(art));
        ImGui.TableNextColumn();
        DrawPokemonSummaryName(pokemon, locked);
        ImGui.TextWrapped($"{(pokemon.Nickname.Length > 0 && pokemon.Nickname != pokemon.DisplaySpecies ? pokemon.DisplaySpecies + " / " : "")}Lv. {pokemon.Level}");
        if (pokemon.Types.Count > 0)
            DrawStarterTypeBadges(pokemon.Types.ToArray(), Math.Max(1, Math.Min(64 * scale,
                (ImGui.GetContentRegionAvail().X - (pokemon.Types.Count - 1) * 5 * scale) / pokemon.Types.Count)), scale);
        if (wide) ImGui.TableNextColumn();
        ImGui.TextColored(CompanionMuted, location);
        ImGui.EndTable();
    }

    private void DrawPokemonSummaryName(PokemonDto pokemon, bool locked)
    {
        var name = pokemon.Nickname.Length > 0 ? pokemon.Nickname : pokemon.DisplaySpecies;
        var available = ImGui.GetContentRegionAvail().X;
        var right = ImGui.GetCursorScreenPos().X + available;
        if (summaryNickname is null)
        {
            ImGui.AlignTextToFramePadding();
            DrawGenderedPokemonText(name, pokemon.Gender, CompanionAccent);
            var edit = !locked && ImGui.IsItemClicked();
            if (!locked && ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.BeginDisabled(locked);
            SameLineIfFits("Edit", right);
            edit |= DrawAccentButton("Edit##Nickname", Vector2.Zero, CompanionMuted);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit nickname");
            var star = pokemon.Favorite ? "★" : "☆";
            SameLineIfFits(star, right);
            if (DrawAccentButton($"{star}##SummaryFavourite", Vector2.Zero, pokemon.Favorite ? PokemonYellow : CompanionMuted))
                TogglePokemonFavourite(pokemon);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(pokemon.Favorite ? "Unmark favourite" : "Mark favourite");
            ImGui.EndDisabled();
            if (edit)
            {
                summaryNickname = name;
                focusSummaryNickname = true;
            }
            return;
        }

        var draft = summaryNickname;
        var mark = PokemonGenderMark(pokemon.Gender);
        var iconSize = ImGui.GetTextLineHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var genderSpace = mark is null ? 0 : iconSize + spacing;
        ImGui.BeginDisabled(locked);
        if (focusSummaryNickname && !locked)
        {
            ImGui.SetKeyboardFocusHere();
            focusSummaryNickname = false;
        }
        ImGui.SetNextItemWidth(Math.Clamp(available - genderSpace, 1, 170 * CompanionScale));
        var submitted = ImGui.InputTextWithHint("##PokemonNickname", "Nickname", ref draft, 49,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        summaryNickname = draft;
        ImGui.EndDisabled();
        if (mark is { } gender)
        {
            ImGui.SameLine();
            var position = ImGui.GetCursorScreenPos();
            var frameHeight = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(iconSize, frameHeight));
            DrawPokemonGenderMark(gender, position + new Vector2(0, (frameHeight - iconSize) / 2), iconSize);
        }

        var trimmed = draft.Trim();
        var valid = trimmed.EnumerateRunes().Count() is >= 1 and <= 12 && trimmed.IndexOfAny(['[', ']', '|']) < 0;
        var canSave = !locked && valid && trimmed != name;
        SameLineIfFits("Save", right);
        ImGui.BeginDisabled(!canSave);
        var save = DrawAccentButton("Save##Nickname", Vector2.Zero, PokemonGreen);
        ImGui.EndDisabled();
        SameLineIfFits("Cancel", right);
        if (DrawAccentButton("Cancel##Nickname", Vector2.Zero, CompanionMuted)) summaryNickname = null;
        else if ((save || submitted) && canSave)
        {
            Run(async () =>
            {
                await UpdateCollection(plugin.Api.EditPokemon(pokemon.Id, trimmed), "Nickname updated.");
                if (pokemonSummaryId == pokemon.Id && summaryNickname == draft) summaryNickname = null;
            }, "Saving nickname...");
        }

        if (!valid) ImGui.TextWrapped("Use 1-12 visible characters without brackets or pipes.");
    }

    private void DrawPokemonSummaryDetails(PokemonDto pokemon, LeagueVitalDto? vital)
    {
        ImGui.TextColored(CompanionAccent, "Summary");
        if (vital is not null)
            DrawPokemonSummaryMeter("HP", $"{vital.Hp} / {vital.MaxHp}{HealthStatus(vital)}",
                vital.Hp / (float)Math.Max(1, vital.MaxHp), HpBarRed);
        var (progress, label) = ExperienceProgress(pokemon);
        DrawPokemonSummaryMeter("EXP", label, progress, ExpBarBlue);
        ImGui.Spacing();
        if (ImGui.BeginTable("PokemonSummaryProperties", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Tera type").X);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            var nature = pokemon.MintNature.Length == 0 ? pokemon.Nature : pokemon.MintNature;
            foreach (var (key, value) in new[] { ("Nature", nature), ("Ability", pokemon.Ability), ("Tera type", pokemon.TeraType) })
            {
                ImGui.TableNextColumn();
                ImGui.TextColored(CompanionMuted, key);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(value);
                if (key == "Ability") DrawAbilityTooltip(pokemon);
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        DrawCompanionStats(pokemon);
        DrawPokemonSummaryEvolutionButton(pokemon);
    }

    private static void DrawPokemonSummaryMeter(string label, string value, float progress, Vector4 color)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        ImGui.TextUnformatted(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var gap = right - ImGui.GetItemRectMax().X - valueWidth;
        if (gap >= ImGui.GetStyle().ItemSpacing.X) ImGui.SameLine(0, gap);
        ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
        ImGui.TextWrapped(value);
        ImGui.PopStyleColor();
        DrawProgressBar(progress, new Vector2(-1, 5 * CompanionScale), "", color);
    }

    private void DrawPokemonSummaryMoves(PokemonDto pokemon, LeagueVitalDto? vital, bool locked)
    {
        var count = $"{pokemon.Moves.Count}/4";
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        ImGui.TextColored(CompanionAccent, "Moves");
        var gap = right - ImGui.GetItemRectMax().X - ImGui.CalcTextSize(count).X;
        if (gap >= ImGui.GetStyle().ItemSpacing.X) ImGui.SameLine(0, gap);
        ImGui.TextColored(CompanionMuted, count);
        DrawPokemonSummaryKnownMoves(pokemon, vital);

        ImGui.BeginDisabled(locked || busy);
        var pending = trainer!.PendingMoves.FirstOrDefault(entry => entry.PokemonId == pokemon.Id);
        if (pending is not null) DrawPokemonSummaryPendingMove(pokemon, pending);
        DrawPokemonSummaryAction("Relearn", PokemonSummaryPanel.Relearn);
        FocusPokemonSummarySection(PokemonSummaryPanel.Relearn);
        SameLineIfFits("TMs & Tutor", right);
        DrawPokemonSummaryAction("TMs & Tutor", PokemonSummaryPanel.Tutor);
        FocusPokemonSummarySection(PokemonSummaryPanel.Tutor);
        switch (pokemonSummaryPanel)
        {
            case PokemonSummaryPanel.Relearn: DrawMoveRelearner(pokemon, expanded: true); break;
            case PokemonSummaryPanel.Tutor: DrawMoveLicenses(pokemon, expanded: true); break;
        }
        ImGui.EndDisabled();
    }

    private void DrawPokemonSummaryKnownMoves(PokemonDto pokemon, LeagueVitalDto? vital)
    {
        if (pokemon.Moves.Count == 0) { ImGui.TextDisabled("No moves known."); return; }
        var build = GetPokemonBuild(pokemon);
        if (!ImGui.BeginTable("StorageKnownMoves", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH)) return;
        ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("PP", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("64/64 PP").X);
        foreach (var name in pokemon.Moves)
        {
            var move = build?.Moves.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var pp = vital?.Moves.FirstOrDefault(entry => entry.Id == SpriteNames.Normalize(name).ToLowerInvariant());
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            ImGui.TableNextColumn();
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            ImGui.TextWrapped(name);
            if (move is not null)
            {
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{move.Description}\nPower {move.Power} / Accuracy {move.Accuracy?.ToString() ?? "Always"}");
                ImGui.TextColored(TypeColor(move.Type), move.Type);
                SameLineIfFits(move.Category, right);
                ImGui.TextColored(CompanionMuted, move.Category);
            }
            ImGui.TableNextColumn();
            if (pp is not null) ImGui.TextColored(CompanionMuted, $"{pp.Pp}/{pp.MaxPp} PP");
            else if (move is not null) ImGui.TextColored(CompanionMuted, $"{move.Pp} PP");
        }
        ImGui.EndTable();
        ImGui.Spacing();
    }

    private bool DrawPokemonSummaryAction(string label, PokemonSummaryPanel panel)
    {
        if (!DrawAccentButton(label, Vector2.Zero, pokemonSummaryPanel == panel ? PokemonBlue : CompanionAccent)) return false;
        var expand = pokemonSummaryPanel != panel;
        ClosePokemonSummaryPanel();
        if (expand) pokemonSummaryPanel = panel;
        return expand;
    }

    private void ClosePokemonSummaryPanel()
    {
        pokemonSummaryPanel = PokemonSummaryPanel.None;
        summaryEvolution = null;
        summaryReplacement = null;
        summaryHeldItem = null;
        summaryIv = -1;
    }

    private void FocusPokemonSummarySection(PokemonSummaryPanel panel)
    {
        if (pokemonSummaryFocus != panel) return;
        ImGui.SetScrollHereY(0f);
        pokemonSummaryFocus = PokemonSummaryPanel.None;
    }

    private void DrawPokemonSummaryPendingMove(PokemonDto pokemon, PendingMoveDto pending)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleColor(ImGuiCol.Text, PokemonYellow);
        ImGui.TextWrapped($"{pending.Move} is ready to learn");
        ImGui.PopStyleColor();
        SameLineIfFits("Review move", right);
        DrawPokemonSummaryAction("Review move", PokemonSummaryPanel.PendingMove);
        if (pokemonSummaryPanel != PokemonSummaryPanel.PendingMove) return;

        var replacement = summaryReplacement is { } selected && selected.LearningId == pending.Id && pokemon.Moves.Contains(selected.Move)
            ? selected.Move : null;
        DrawLearningMoveDetails(pending.Move);
        ImGui.TextWrapped($"Unlocked at level {pending.Level}. Choose a move to replace.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##MoveToReplace", replacement ?? "Choose a known move"))
        {
            foreach (var move in pokemon.Moves)
            {
                if (ImGui.Selectable(move, move == replacement))
                    summaryReplacement = (pending.Id, replacement = move);
                DrawBattleMoveTooltip(move);
            }
            ImGui.EndCombo();
        }
        if (replacement is not null) DrawBattleMoveTooltip(replacement);
        ImGui.BeginDisabled(replacement is null);
        if (DrawAccentButton($"Learn {pending.Move}", Vector2.Zero, PokemonGreen))
            Run(() => ResolveMoveLearning(pending.Id, replacement), $"Teaching {pending.Move}...");
        ImGui.EndDisabled();
        SameLineIfFits("Keep current moves", right);
        if (DrawAccentButton("Keep current moves", Vector2.Zero, CompanionMuted))
            Run(() => ResolveMoveLearning(pending.Id, null), $"Skipping {pending.Move}...");
        SameLineIfFits("Cancel", right);
        if (DrawAccentButton("Cancel##PendingMove", Vector2.Zero, CompanionMuted)) ClosePokemonSummaryPanel();
        ImGui.Spacing();
    }

    private void DrawPokemonSummaryEvolutionButton(PokemonDto pokemon)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var ready = GetEvolutionOptions(pokemon.Id).Count(option => option.Ready);
        ImGui.Spacing();
        ImGui.BeginGroup();
        ImGui.TextColored(ready > 0 ? PokemonYellow : CompanionAccent, "Evolution & forms");
        if (pokemon.HeldItem.Equals("everstone", StringComparison.OrdinalIgnoreCase)) ImGui.TextDisabled("Everstone held");
        else if (ready > 0) ImGui.TextDisabled(ready == 1 ? "1 route ready" : $"{ready} routes ready");
        ImGui.EndGroup();
        SameLineIfFits("View details", right);
        if (DrawPokemonSummaryAction("View details", PokemonSummaryPanel.Evolution)) pokemonSummaryFocus = PokemonSummaryPanel.Evolution;
    }

    private void DrawCompanionEvolutionRoutes(PokemonDto pokemon)
    {
        if (pokemon.HeldItem.Equals("everstone", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextWrapped("Evolution is blocked by Everstone. Remove it from Held item when you want this Pokémon to evolve.");
            return;
        }
        var options = GetEvolutionOptions(pokemon.Id);
        if (options.Length == 0)
        {
            ImGui.TextWrapped("No further evolution routes are available for this Pokémon.");
            return;
        }
        if (options.Any(entry => entry.ReadyWithoutEnvironment.HasValue)) ImGui.TextWrapped(evolutionEnvironmentLabel);
        var selected = summaryEvolution is { } route
            ? options.FirstOrDefault(option => route == (option.TargetSpecies, option.TargetAppearance))
            : options.FirstOrDefault(option => option.Ready) ?? options[0];
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##EvolutionTarget", selected is null ? "Choose a route" : EvolutionLabel(selected.TargetSpecies)))
        {
            for (var index = 0; index < options.Length; index++)
            {
                var option = options[index];
                if (!ImGui.Selectable($"{EvolutionLabel(option.TargetSpecies)} / {(option.Ready ? "Ready" : "Requirements")}##{index}", option == selected)) continue;
                selected = option;
                summaryEvolution = (option.TargetSpecies, option.TargetAppearance);
            }
            ImGui.EndCombo();
        }
        if (selected is null) return;
        var appearance = selected.TargetAppearance.Length > 0 ? selected.TargetAppearance : selected.TargetSpecies;
        DrawPokemonIcon(appearance, pokemon.Shiny, new Vector2(42 * CompanionScale));
        ImGui.SameLine();
        ImGui.BeginGroup();
        if (SpeciesDisplayCatalog.Get(appearance)?.Types is { Length: > 0 } types)
            DrawStarterTypeBadges(types, Math.Max(1, Math.Min(58 * CompanionScale,
                (ImGui.GetContentRegionAvail().X - (types.Length - 1) * 5 * CompanionScale) / types.Length)), CompanionScale);
        ImGui.TextWrapped(selected.Requirement);
        if (selected.TargetAppearance.StartsWith("Alcremie-", StringComparison.Ordinal))
            ImGui.TextWrapped("This consumes the held Sweet. The cream and decoration are permanent.");
        ImGui.EndGroup();
        var target = selected.TargetSpecies;
        ImGui.BeginDisabled(!selected.Ready);
        if (DrawAccentButton("Confirm evolution", Vector2.Zero, PokemonGreen))
            Run(() => EvolvePokemon(pokemon.Id, target), $"Evolving {pokemon.Nickname}...");
        ImGui.EndDisabled();
    }
}
