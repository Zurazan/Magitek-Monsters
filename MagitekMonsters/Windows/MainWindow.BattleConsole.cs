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
    private BattleMenu battleMenu;
    private string battleMenuStamp = string.Empty;
    private string battleMenuBattleId = string.Empty;
    private string battleMedicineId = string.Empty;
    private string battleBallId = string.Empty;
    private int battleMedicineTarget;
    private int battleMedicineMove;
    private const ImGuiWindowFlags FixedBattlePanel = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    private void DrawBattleConsole(BattleSnapshotDto current, bool pvp = false)
    {
        var forced = current.Request?.ForceSwitch?.Any(value => value) == true;
        var stamp = $"{current.Id}:{current.Turn}:{current.PlayerActive?.Ident}";
        battleMenu = BattleLayout.NextMenu(battleMenu, battleMenuBattleId != current.Id, battleMenuStamp != stamp, forced, current.Kind == "wild");
        battleMenuStamp = stamp;
        battleMenuBattleId = current.Id;
        var scale = CompanionScale;
        var available = ImGui.GetContentRegionAvail();
        var layout = BattleLayout.Console(available.X, available.Y, scale, current.GameType == "doubles", !pvp && current.Status == "active" && battleMenu is BattleMenu.Bag or BattleMenu.Catch);
        var origin = ImGui.GetCursorPos();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("BattleConsoleMain", new Vector2(layout.MainWidth, available.Y), false, FixedBattlePanel);
        if (current.Status != "active" && !pvp) DrawBattleResults(current);
        else if (pvp && current.Status == "active" && (current.Request?.TeamPreview == true || current.Turn == 0 && current.PlayerActive is null)) DrawOnlinePreview(current);
        else DrawBattleConsoleMain(current, pvp, layout, available.Y);
        ImGui.EndChild();
        ImGui.SetCursorPos(origin + new Vector2(layout.MainWidth + layout.Gap, 0));
        ImGui.BeginChild("BattleHistoryRail", new Vector2(layout.LogWidth, available.Y), false, FixedBattlePanel);
        DrawCompanionBattleLog(current, available.Y);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawBattleConsoleMain(BattleSnapshotDto current, bool pvp, BattleConsoleLayout layout, float height)
    {
        var scale = CompanionScale;
        ImGui.BeginChild("BattleConsoleHeader", new Vector2(0, layout.HeaderHeight), false, FixedBattlePanel);
        DrawBattleConsoleHeader(current, pvp);
        DrawBattleConsolePopups(current);
        ImGui.EndChild();
        ImGui.SetCursorPosY(layout.HeaderHeight + layout.Gap);
        DrawConsoleField(current, layout.FieldHeight);
        ImGui.SetCursorPosY(layout.HeaderHeight + layout.Gap + layout.FieldHeight + layout.Gap);
        ImGui.BeginChild("BattleLatest", new Vector2(0, layout.LatestHeight), false, FixedBattlePanel);
        var latest = !string.IsNullOrWhiteSpace(current.Error) ? current.Error
            : busy ? "Resolving your action..." : current.Request?.Wait == true ? "Waiting for the next turn..." : "Choose your next action.";
        DrawClippedText(latest, ImGui.GetContentRegionAvail().X, string.IsNullOrWhiteSpace(current.Error) ? CompanionMuted : PokemonRed);
        ImGui.EndChild();
        ImGui.SetCursorPosY(Math.Min(height - layout.ActionHeight,
            layout.HeaderHeight + layout.FieldHeight + layout.LatestHeight + 3 * layout.Gap));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 3) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 1) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 2) * scale);
        ImGui.BeginChild("BattleCommandDeck", new Vector2(0, layout.ActionHeight), true, FixedBattlePanel);
        DrawConsoleCommands(current, pvp);
        ImGui.EndChild();
        ImGui.PopStyleVar(4);
    }

    private void DrawBattleConsoleHeader(BattleSnapshotDto current, bool pvp)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var buttons = new List<string> { "Help" };
        if (current.OpponentTeamSheet.Count > 0) buttons.Add("Team sheet");
        var toolsWidth = buttons.Sum(label => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X);
        var state = current.Status == "active" ? current.Request?.TeamPreview == true ? "Team preview" : $"Turn {current.Turn}" : current.Status.ToUpperInvariant();
        var urgent = false;
        if (pvp && current.Status == "active" && DateTimeOffset.TryParse(current.ChoiceDeadline, out var deadline))
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalSeconds));
            urgent = seconds <= 20;
            state += $" / {seconds / 60}:{seconds % 60:00}";
        }
        var label = current.Kind == "wild" ? "WILD ENCOUNTER" : pvp ? current.Friendly ? "FRIENDLY BATTLE" : "ONLINE BATTLE" : "TRAINER BATTLE";
        DrawClippedText($"{label} / {current.OpponentName}", Math.Max(1, width - toolsWidth), CompanionAccent);
        ImGui.SameLine();
        if (DrawAccentSmallButton("Help", CompanionMuted)) ImGui.OpenPopup("Battle help");
        if (current.OpponentTeamSheet.Count > 0)
        {
            ImGui.SameLine(); if (DrawAccentSmallButton("Team sheet", PokemonOrange)) ImGui.OpenPopup("Battle team sheet");
        }
        var rules = pvp ? current.FormatName ?? (current.GameType == "doubles" ? "Doubles" : "Singles")
            : current.Rules is { } policy ? $"{policy.Label} / Bag {(policy.BagLimit is int limit ? $"{Math.Max(0, limit - current.BagActionsUsed)}/{limit}" : "unrestricted")}" : "PvE / Bag allowed";
        if (current.Raid is { } raid)
            rules = $"Phase {raid.Phase} / {Math.Max(0, raid.TurnLimit - raid.CompletedTurns)} turns left / {(raid.CompletedTurns >= raid.EnrageTurn ? "ENRAGED" : $"Enrage in {raid.EnrageTurn - raid.CompletedTurns}")}";
        DrawClippedText($"{state} / {rules}", width, urgent || current.Status is "lost" or "error" ? PokemonRed : CompanionMuted);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{state}\n{rules}\n{(current.Raid is { } boss ? $"{boss.HpMultiplier}x boss HP / Bonus actions: {boss.BonusActions}" : "Hover moves for their full descriptions.")}");
    }

    private void DrawConsoleField(BattleSnapshotDto current, float height)
    {
        if (current.Request?.TeamPreview == true) { DrawConsoleOpponentPreview(current, height); return; }
        var doubles = current.GameType == "doubles";
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 0) * CompanionScale);
        if (ImGui.BeginTable("ConsoleCombatants", doubles ? 4 : 2, ImGuiTableFlags.SizingStretchSame))
        {
            for (var side = 0; side < 2; side++)
                for (var slot = 0; slot < (doubles ? 2 : 1); slot++)
                {
                    ImGui.TableNextColumn();
                    var combatant = side == 0 ? doubles ? current.PlayerActives.ElementAtOrDefault(slot) : current.PlayerActive
                        : doubles ? current.OpponentActives.ElementAtOrDefault(slot) : current.OpponentActive;
                    DrawConsoleCombatant(combatant, side == 0, slot, doubles, height, current.Kind == "wild", current.Status == "caught");
                }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawConsoleCombatant(BattleCombatantDto? pokemon, bool ours, int slot, bool doubles, float height, bool wild, bool captured)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 5) * CompanionScale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 2) * CompanionScale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ours ? new Vector4(0.11f, 0.14f, 0.19f, 1) : new Vector4(0.17f, 0.12f, 0.13f, 1));
        ImGui.BeginChild($"ConsolePokemon-{ours}-{slot}", new Vector2(0, height), true, FixedBattlePanel);
        var width = ImGui.GetContentRegionAvail().X;
        var role = ours ? doubles ? $"YOUR SLOT {slot + 1}" : "YOUR POKÉMON" : wild ? "WILD POKÉMON" : doubles ? $"FOE SLOT {slot + 1}" : "OPPONENT";
        if (height < 110 * CompanionScale)
        {
            DrawCompactTrayCombatant(pokemon, ours, wild, captured, role, width);
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar(2);
            return;
        }
        if (wild && !ours && pokemon is not null)
        {
            var caught = captured || IsAdventureSpeciesCaught(pokemon.Species);
            var label = caught ? "CAUGHT" : "NOT CAUGHT";
            var badgeWidth = ImGui.CalcTextSize(label).X + 14 * CompanionScale;
            if (ImGui.BeginTable("WildCardHeader", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Collection", ImGuiTableColumnFlags.WidthFixed, Math.Min(width, badgeWidth));
                ImGui.TableNextColumn(); DrawClippedText(role, ImGui.GetContentRegionAvail().X, PokemonOrange);
                ImGui.TableNextColumn(); DrawConsoleCaughtBadge(label, caught, captured);
                ImGui.EndTable();
            }
        }
        else DrawClippedText(role, width, ours ? CompanionAccent : PokemonOrange);
        if (pokemon is null) DrawClippedText("Waiting for Pokémon...", width, CompanionMuted);
        else
        {
            var line = ImGui.GetTextLineHeight();
            var types = SpeciesDisplayCatalog.Get(pokemon.Species)?.Types ?? [];
            if (pokemon.TeraType is { Length: > 0 } tera && tera != "Stellar") types = [tera];
            var badges = BattleLayout.Types(width, CompanionScale, types.Length);
            var nameHeight = badges.Stacked ? line + badges.Height + 2 * CompanionScale : Math.Max(line, badges.Height);
            var art = Math.Max(1, Math.Min(width, ImGui.GetContentRegionAvail().Y - nameHeight - line - 6 * CompanionScale - 4 * ImGui.GetStyle().ItemSpacing.Y));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - art) / 2));
            DrawPokemonBattleImage(pokemon.Species, pokemon.Shiny, new Vector2(art));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{pokemon.Name} / {pokemon.Species} / Lv. {pokemon.Level}\n{string.Join(" / ", SpeciesDisplayCatalog.Get(pokemon.Species)?.Types ?? [])}\n{pokemon.Condition}");
            var mega = SpeciesDisplayCatalog.MegaEvolutions.Any(route => route.Species == pokemon.Species);
            var name = $"{(pokemon.Shiny ? "★ " : "")}{(mega ? pokemon.Species : pokemon.Name)} / Lv. {pokemon.Level}";
            if (types.Length == 0 || badges.Stacked)
            {
                DrawClippedText(name, width, Vector4.One);
                if (types.Length > 0) DrawConsoleTypeRow(types, badges);
            }
            else if (ImGui.BeginTable("CombatantNameTypes", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Types", ImGuiTableColumnFlags.WidthFixed, badges.Width);
                ImGui.TableNextColumn(); DrawClippedText(name, ImGui.GetContentRegionAvail().X, Vector4.One);
                ImGui.TableNextColumn(); DrawConsoleTypeRow(types, badges);
                ImGui.EndTable();
            }
            var (ratio, hp) = ParseBattleHp(pokemon.Condition);
            DrawProgressBar(ratio, new Vector2(-1, 6 * CompanionScale), "", HpBarRed);
            var status = pokemon.Condition.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(value => value != "fnt").ToArray();
            DrawClippedText($"{hp}{(status.Length > 0 ? $" / {string.Join(" ", status).ToUpperInvariant()}" : "")}", width, pokemon.Fainted ? PokemonRed : status.Length > 0 ? PokemonOrange : CompanionMuted);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(); ImGui.PopStyleVar(2);
    }

    private void DrawConsoleTypeRow(string[] types, BattleTypeLayout layout)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("CombatantTypes", new Vector2(layout.Width, Math.Max(layout.Height, ImGui.GetTextLineHeight())), false, FixedBattlePanel);
        DrawStarterTypeBadges(types, layout.BadgeWidth, CompanionScale);
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private void DrawConsoleCaughtBadge(string label, bool caught, bool captured)
    {
        var color = caught ? PokemonGreen : PokemonYellow;
        var size = new Vector2(Math.Max(1, Math.Min(ImGui.GetContentRegionAvail().X, ImGui.CalcTextSize(label).X + 14 * CompanionScale)), ImGui.GetTextLineHeight() + 4 * CompanionScale);
        var position = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(position, position + size, ImGui.GetColorU32(color with { W = 0.16f }), 3 * CompanionScale);
        draw.AddRect(position, position + size, ImGui.GetColorU32(color with { W = 0.75f }), 3 * CompanionScale);
        draw.PushClipRect(position, position + size, true);
        draw.AddText(position + new Vector2(7, 2) * CompanionScale, ImGui.GetColorU32(color), label);
        draw.PopClipRect();
        ImGui.Dummy(size);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(captured ? "Caught in this battle." : caught ? "Already registered in your Pokédex." : "Not yet registered in your Pokédex.");
    }

    private void DrawConsoleCommands(BattleSnapshotDto current, bool pvp)
    {
        var exit = current.Status == "active" && current.Kind != "wild";
        if (exit) ImGui.BeginChild("ConsoleActionContent", new Vector2(0, -ImGui.GetFrameHeight() - 6 * CompanionScale), false, FixedBattlePanel);
        DrawConsoleActionContent(current, pvp);
        if (exit) { ImGui.EndChild(); DrawConsoleExit(current, pvp); }
    }

    private void DrawConsoleActionContent(BattleSnapshotDto current, bool pvp)
    {
        if (current.Status != "active") { DrawConsoleResult(current, pvp); return; }
        var request = current.Request;
        var available = request is not null && request.Wait != true;
        var ready = !busy && available;
        var forced = request?.ForceSwitch?.Any(value => value) == true;
        if (pvp && current.GameType == "doubles")
        {
            ImGui.BeginDisabled(!ready);
            if (available) DrawDoublesChoices(current);
            else DrawClippedText(busy ? "Resolving your choices..." : "Waiting for the other Trainer...", ImGui.GetContentRegionAvail().X, PokemonYellow);
            ImGui.EndDisabled();
            return;
        }
        ImGui.BeginDisabled(!ready);
        var previousMenu = battleMenu;
        DrawConsoleMenu(current, pvp, forced);
        ImGui.Spacing();
        if (previousMenu is not (BattleMenu.Bag or BattleMenu.Catch) && battleMenu is BattleMenu.Bag or BattleMenu.Catch)
        {
            DrawClippedText("Opening your Bag...", ImGui.GetContentRegionAvail().X, CompanionMuted);
            ImGui.EndDisabled();
            return;
        }
        var party = request?.Side?.Pokemon ?? [];
        if (!available)
            DrawClippedText(busy ? "Resolving your choice..." : pvp ? "Waiting for the other Trainer..." : "Waiting for the battle server...", ImGui.GetContentRegionAvail().X, PokemonYellow);
        else if (battleMenu == BattleMenu.Switch) DrawConsoleSwitches(current, pvp, party, forced);
        else if (battleMenu == BattleMenu.Bag) DrawConsoleMedicine(current, party);
        else if (battleMenu == BattleMenu.Catch) DrawConsoleCatch(current);
        else DrawConsoleMoves(current, pvp);
        ImGui.EndDisabled();
    }

    private void DrawConsoleMenu(BattleSnapshotDto current, bool pvp, bool forced)
    {
        var menus = new List<(BattleMenu Menu, string Label, Vector4 Color)> { (BattleMenu.Fight, "Fight", CompanionAccent), (BattleMenu.Switch, forced ? "Replacement" : "Switch", PokemonOrange) };
        if (!pvp) menus.Add((BattleMenu.Bag, "Bag", PokemonGreen));
        if (!pvp && current.Kind == "wild") menus.Add((BattleMenu.Catch, "Catch", PokemonYellow));
        var columns = menus.Count + (!pvp && current.Kind == "wild" ? 1 : 0);
        if (!ImGui.BeginTable("ConsoleMenu", columns, ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var menu in menus)
        {
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(forced && menu.Menu != BattleMenu.Switch);
            if (DrawAccentButton(menu.Label, new Vector2(-1, 27 * CompanionScale), battleMenu == menu.Menu ? menu.Color : CompanionMuted)) battleMenu = menu.Menu;
            ImGui.EndDisabled();
        }
        if (!pvp && current.Kind == "wild")
        {
            ImGui.TableNextColumn(); ImGui.BeginDisabled(forced);
            if (DrawAccentButton("Run", new Vector2(-1, 27 * CompanionScale), PokemonRed)) Run(() => ChooseBattle("run"), "Trying to escape...");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Try to escape. A failed attempt spends your turn; repeated attempts improve your odds.");
            ImGui.EndDisabled();
        }
        ImGui.EndTable();
    }

    private void DrawConsoleMoves(BattleSnapshotDto current, bool pvp)
    {
        var active = current.Request?.Active?.FirstOrDefault();
        DrawBattleModifier(current, active, pvp);
        var cardHeight = Math.Clamp((ImGui.GetContentRegionAvail().Y - 36 * CompanionScale) / 2, 35 * CompanionScale, 52 * CompanionScale);
        if (active is null || !ImGui.BeginTable("ConsoleMoves", 2, ImGuiTableFlags.SizingStretchSame)) return;
        for (var index = 0; index < active.Moves.Count; index++)
        {
            ImGui.TableNextColumn();
            var move = active.Moves[index];
            var zSelected = battleModifier == "zmove";
            var zMove = zSelected ? active.CanZMove?.ElementAtOrDefault(index) : null;
            var maxMove = !pvp && battleModifier == "dynamax" ? active.MaxMoves?.MaxMoves.ElementAtOrDefault(index) : null;
            ImGui.BeginDisabled(maxMove is not null ? !BattleLayout.HasUsablePp(move) || maxMove.Disabled : battleModifier == "dynamax" || !BattleLayout.CanMove(move, zSelected, zMove));
            if (DrawTypedMoveButton($"SingleMove{index}", maxMove?.Move ?? zMove?.Move ?? move.Move, move, cardHeight, false,
                maxMove?.TypeHints ?? (zSelected ? zMove?.TypeHints : move.TypeHints), zSelected || maxMove is not null ? "" : battleModifier, 1))
            {
                var suffix = BattleModifierSuffix(active, index);
                var command = $"move {index + 1}{suffix}";
                Run(() => pvp ? ChoosePvpBattle(command) : ChooseBattle(command), $"Using {BattleLayout.MoveLabel(maxMove?.Move ?? zMove?.Move ?? move.Move).Name}...");
            }
            ImGui.EndDisabled();
        }
        ImGui.EndTable();
    }

    private bool DrawTypedMoveButton(string id, string name, BattleMoveDto move, float height, bool selected, MoveTypeHintsDto? hints, string modifier = "", int target = 0)
    {
        var preview = plugin.Configuration.MoveTypeHintsEnabled ? BattleTypeHints.Preview(hints, modifier, target, includeDescription: false) : null;
        var label = BattleLayout.MoveLabel(name);
        battleMoveHelp.TryGetValue(label.Name, out var details);
        var accent = preview is { Type.Length: > 0 } ? TypeColor(preview.Type) : details is null ? CompanionAccent : TypeColor(details.Type);
        PushAccentButtonStyle(selected ? PokemonGreen : accent);
        var clicked = ImGui.Button($"##{id}", new Vector2(-1, height));
        ImGui.PopStyleVar(); ImGui.PopStyleColor(5);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min + new Vector2(6, 0), max - new Vector2(6, 0), true);
        var disabled = !BattleLayout.HasUsablePp(move) || move.Disabled && name == move.Move;
        var twoRows = height >= 2 * ImGui.GetTextLineHeight() + 8 * CompanionScale;
        var badgeSize = preview is null ? Vector2.Zero : ImGui.CalcTextSize(preview.Badge) + new Vector2(10, 2) * CompanionScale;
        var showBadge = preview is not null && badgeSize.X + 16 * CompanionScale < max.X - min.X;
        if (!twoRows && ImGui.CalcTextSize(label.ButtonText).X + badgeSize.X + 27 * CompanionScale > max.X - min.X) showBadge = false;
        var badgeBelow = twoRows && max.X - min.X < 170 * CompanionScale;
        var badgeMin = new Vector2(max.X - badgeSize.X - 7 * CompanionScale, badgeBelow ? max.Y - ImGui.GetTextLineHeight() - 5 * CompanionScale : min.Y + 4 * CompanionScale);
        draw.PushClipRect(min + new Vector2(6, 0), new Vector2(showBadge && !badgeBelow ? badgeMin.X - 5 * CompanionScale : max.X - 6 * CompanionScale, max.Y), true);
        draw.AddText(min + new Vector2(9, 4) * CompanionScale, ImGui.GetColorU32(disabled ? CompanionMuted : selected ? PokemonGreen : Vector4.One), label.ButtonText);
        draw.PopClipRect();
        if (twoRows)
        {
            draw.PushClipRect(min + new Vector2(6, 0), new Vector2(showBadge && badgeBelow ? badgeMin.X - 5 * CompanionScale : max.X - 6 * CompanionScale, max.Y), true);
            draw.AddText(min + new Vector2(9 * CompanionScale, height - ImGui.GetTextLineHeight() - 4 * CompanionScale), ImGui.GetColorU32(disabled ? CompanionMuted : accent), $"{(preview is { Type.Length: > 0 } ? preview.Type : details?.Type ?? "Move")} / {BattleLayout.MovePpLabel(move)}");
            draw.PopClipRect();
        }
        if (showBadge && preview is not null)
        {
            var color = preview.Kind switch { BattleTypeHintKind.SuperEffective => PokemonGreen, BattleTypeHintKind.Resisted => PokemonOrange,
                BattleTypeHintKind.Immune => PokemonRed, _ => CompanionMuted };
            if (disabled) color = CompanionMuted;
            draw.AddRectFilled(badgeMin, badgeMin + badgeSize, ImGui.GetColorU32(CompanionSurface), 3 * CompanionScale);
            draw.AddText(badgeMin + new Vector2(5, 0) * CompanionScale, ImGui.GetColorU32(color), preview.Badge);
        }
        draw.PopClipRect();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawBattleMoveTooltip(name, preview is null ? null : BattleTypeHints.Preview(hints, modifier, target), move);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (preview is null && move.Pp is null && move.Maxpp is null) ImGui.SetTooltip($"{label.ButtonText}\n{BattleLayout.MovePpLabel(move)}. This action does not spend additional PP.");
            else if (preview is null && details is null) ImGui.SetTooltip($"{label.ButtonText}\n{BattleLayout.MovePpLabel(move)}");
        }
        return clicked;
    }

    private void DrawConsoleSwitches(BattleSnapshotDto current, bool pvp, List<BattleSidePokemonDto> party, bool forced)
    {
        var active = current.Request?.Active?.FirstOrDefault();
        var reviving = forced && party.Any(pokemon => pokemon.Reviving);
        DrawClippedText(reviving ? "Choose a fainted Pokémon to revive" : forced ? "Choose your replacement" : active?.Trapped == true ? "Your active Pokémon is trapped" : "Switching uses your turn", ImGui.GetContentRegionAvail().X, CompanionMuted);
        if (!ImGui.BeginTable("ConsoleSwitches", 3, ImGuiTableFlags.SizingStretchSame)) return;
        for (var index = 0; index < party.Count; index++)
        {
            ImGui.TableNextColumn();
            var pokemon = party[index];
            ImGui.BeginDisabled(!BattleLayout.CanSwitch(pokemon, forced, active?.Trapped == true, reviving));
            if (DrawAccentButton($"{CleanIdent(pokemon.Ident)}\n{pokemon.Condition}##ConsoleSwitch{index}", new Vector2(-1, 46 * CompanionScale), pokemon.Active ? CompanionAccent : PokemonOrange))
            {
                var command = $"switch {index + 1}";
                Run(() => pvp ? ChoosePvpBattle(command) : ChooseBattle(command), $"Switching to {CleanIdent(pokemon.Ident)}...");
            }
            ImGui.EndDisabled();
        }
        ImGui.EndTable();
    }

    private static string BallModifier(BallEffectDto effect) => effect.Guaranteed ? "Guaranteed"
        : effect.CatchRateAdjustment != 0 ? $"{effect.CatchRateAdjustment:+0;-0} catch rate{(effect.Multiplier != 1 ? $" / {effect.Multiplier:0.##}x" : "")}"
        : $"{effect.Multiplier:0.##}x catch modifier";

    private void DrawConsoleExit(BattleSnapshotDto current, bool pvp)
    {
        if (current.Kind == "wild") return;
        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeight() - 7 * CompanionScale);
        ImGui.BeginDisabled(busy);
        if (!pvp) DrawTrainerForfeit();
        else
        {
            if (DrawAccentSmallButton("Forfeit match", PokemonRed)) ImGui.OpenPopup("Forfeit online battle?");
            if (BeginConfirmationModal("Forfeit online battle?"))
            {
                ImGui.TextWrapped("This ends the match as a loss. Are you sure?");
                DrawOnlineForfeitNotice();
                if (DrawConfirmationActions("Forfeit", PokemonRed, busy)) { ImGui.CloseCurrentPopup(); Run(ForfeitPvpBattle, "Forfeiting the match..."); }
                EndConfirmationModal();
            }
        }
        ImGui.EndDisabled();
    }

    private void DrawConsoleResult(BattleSnapshotDto current, bool pvp)
    {
        DrawClippedText(current.Status == "caught" ? "POKÉMON CAUGHT" : current.Status == "won" ? "VICTORY" : current.Status == "lost" ? "DEFEAT" : current.Status.ToUpperInvariant(), ImGui.GetContentRegionAvail().X, current.Status is "won" or "caught" ? PokemonGreen : PokemonOrange);
        if (!pvp)
        {
            var victory = current.Status is "won" or "caught";
            var experience = victory ? current.RewardExperience * (InventoryCount("expshare") > 0 ? 2 : 1) : 0;
            var result = current.Status == "lost"
                ? current.CurrencyLost > 0 ? $"Lost ¥ {current.CurrencyLost:N0} / No EXP earned"
                    : current.FrontierId is not null ? "No BP earned / No ¥ lost" : "No ¥ lost / No EXP earned"
                : $"Party EXP: {experience:N0} / ¥ {(victory ? current.RewardCurrency : 0):N0}{(current.FrontierId is not null ? $" / {(victory ? current.RewardBP : 0)} BP" : "")}";
            DrawClippedText(result, ImGui.GetContentRegionAvail().X, current.CurrencyLost > 0 ? PokemonRed : ExpBarBlue);
        }
        var label = pvp ? "Return to matchmaking" : current.Kind == "wild" ? "Continue adventuring" : current.FrontierId is not null ? "Return to Tower entry" : "Return to challenger list";
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton(label, new Vector2(-1, 34 * CompanionScale), CompanionAccent))
        {
            if (pvp) Run(DismissPvpBattle, "Closing the finished match...");
            else { battle = null; Run(RefreshTrainer, "Refreshing your Trainer..."); }
        }
        ImGui.EndDisabled();
    }

    private void DrawBattleConsolePopups(BattleSnapshotDto current)
    {
        ImGui.SetNextWindowSize(new Vector2(520, 420) * CompanionScale, ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Battle help"))
        {
            ImGui.TextWrapped("Pick a move, switch Pokémon, use medicine or throw a ball. Moves and switches spend your turn. Bag and catch actions are only available in PvE.");
            DrawBattleGuide(); ImGui.EndPopup();
        }
        ImGui.SetNextWindowSize(new Vector2(520, 480) * CompanionScale, ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Battle team sheet"))
        {
            foreach (var pokemon in current.OpponentTeamSheet)
            {
                ImGui.TextColored(CompanionAccent, pokemon.Species);
                ImGui.TextWrapped($"{pokemon.Ability} / {pokemon.Item} / Tera {pokemon.TeraType}");
                ImGui.TextWrapped(string.Join(" / ", pokemon.Moves)); ImGui.Separator();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawConsoleOpponentPreview(BattleSnapshotDto current, float height)
    {
        var species = current.Log.Where(line => line.StartsWith("|poke|p2|", StringComparison.Ordinal)).Select(line => line.Split('|')[3].Split(',')[0]).ToArray();
        ImGui.BeginChild("ConsoleOpponentPreview", new Vector2(0, height), true, FixedBattlePanel);
        DrawClippedText("OPPONENT'S TEAM / Choose your leads below", ImGui.GetContentRegionAvail().X, PokemonOrange);
        if (species.Length > 0 && ImGui.BeginTable("ConsolePreviewSprites", species.Length, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var name in species)
            {
                ImGui.TableNextColumn();
                DrawPokemonIcon(name, false, new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, Math.Max(20, height - 2 * ImGui.GetTextLineHeight() - 14 * CompanionScale))));
                DrawClippedText(name, ImGui.GetContentRegionAvail().X, CompanionMuted);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }
}
