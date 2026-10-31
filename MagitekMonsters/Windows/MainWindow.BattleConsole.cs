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
        var scale = CompanionScale;
        var available = ImGui.GetContentRegionAvail();
        var layout = BattleLayout.Console(available.X, available.Y, scale, current.GameType == "doubles");
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
        ImGui.SetCursorPosY(height - layout.ActionHeight);
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
        var stamp = $"{current.Id}:{current.Turn}:{current.PlayerActive?.Ident}";
        var forced = request?.ForceSwitch?.Any(value => value) == true;
        battleMenu = BattleLayout.NextMenu(battleMenu, battleMenuBattleId != current.Id, battleMenuStamp != stamp, forced, current.Kind == "wild");
        if (battleMenuStamp != stamp) megaEvolveSelected = false;
        battleMenuStamp = stamp;
        battleMenuBattleId = current.Id;
        if (pvp && current.GameType == "doubles")
        {
            ImGui.BeginDisabled(!ready);
            if (available) DrawDoublesChoices(current);
            else DrawClippedText(busy ? "Resolving your choices..." : "Waiting for the other Trainer...", ImGui.GetContentRegionAvail().X, PokemonYellow);
            ImGui.EndDisabled();
            return;
        }
        ImGui.BeginDisabled(!ready);
        DrawConsoleMenu(current, pvp, forced);
        ImGui.Spacing();
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
        if (pvp) DrawPvpModifier(current, active);
        else if (active?.CanMegaEvo == true) ImGui.Checkbox("Mega Evolve", ref megaEvolveSelected);
        else megaEvolveSelected = false;
        var cardHeight = Math.Clamp((ImGui.GetContentRegionAvail().Y - 36 * CompanionScale) / 2, 35 * CompanionScale, 52 * CompanionScale);
        if (active is null || !ImGui.BeginTable("ConsoleMoves", 2, ImGuiTableFlags.SizingStretchSame)) return;
        for (var index = 0; index < active.Moves.Count; index++)
        {
            ImGui.TableNextColumn();
            var move = active.Moves[index];
            var zSelected = pvp && pvpModifier == "zmove";
            var zMove = zSelected ? active.CanZMove?.ElementAtOrDefault(index) : null;
            ImGui.BeginDisabled(!BattleLayout.CanMove(move, zSelected, zMove));
            if (DrawConsoleMoveButton($"SingleMove{index}", zMove?.Move ?? move.Move, move, cardHeight, false))
            {
                var suffix = pvp ? PvpModifierSuffix(active, index) : megaEvolveSelected ? " mega" : "";
                var command = $"move {index + 1}{suffix}";
                Run(() => pvp ? ChoosePvpBattle(command) : ChooseBattle(command), $"Using {zMove?.Move ?? move.Move}...");
            }
            ImGui.EndDisabled();
        }
        ImGui.EndTable();
    }

    private bool DrawConsoleMoveButton(string id, string name, BattleMoveDto move, float height, bool selected)
    {
        battleMoveHelp.TryGetValue(name, out var details);
        var accent = details is null ? CompanionAccent : TypeColor(details.Type);
        PushAccentButtonStyle(selected ? PokemonGreen : accent);
        var clicked = ImGui.Button($"##{id}", new Vector2(-1, height));
        ImGui.PopStyleVar(); ImGui.PopStyleColor(5);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min + new Vector2(6, 0), max - new Vector2(6, 0), true);
        var disabled = !BattleLayout.HasUsablePp(move) || move.Disabled && name == move.Move;
        draw.AddText(min + new Vector2(9, 4) * CompanionScale, ImGui.GetColorU32(disabled ? CompanionMuted : selected ? PokemonGreen : Vector4.One), name);
        if (height >= 2 * ImGui.GetTextLineHeight() + 8 * CompanionScale)
            draw.AddText(min + new Vector2(9 * CompanionScale, height - ImGui.GetTextLineHeight() - 4 * CompanionScale), ImGui.GetColorU32(disabled ? CompanionMuted : accent), $"{details?.Type ?? "Move"} / {BattleLayout.MovePpLabel(move)}");
        draw.PopClipRect();
        DrawBattleMoveTooltip(name);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (move.Pp is null && move.Maxpp is null) ImGui.SetTooltip($"{name}\n{BattleLayout.MovePpLabel(move)}. This action does not spend additional PP.");
            else if (details is null) ImGui.SetTooltip($"{name}\n{BattleLayout.MovePpLabel(move)}");
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

    private void DrawConsoleMedicine(BattleSnapshotDto current, List<BattleSidePokemonDto> party)
    {
        if (current.Rules?.BagLimit is int limit && current.BagActionsUsed >= limit)
        { DrawClippedText(limit == 0 ? "Bag disabled by this challenge" : "Medicine limit reached", ImGui.GetContentRegionAvail().X, PokemonOrange); return; }
        var items = itemCatalog.Where(item => item.Category == "healing" && !string.IsNullOrWhiteSpace(item.BattleEffect) && InventoryCount(item.Id) > 0).ToList();
        if (items.Count == 0) { ImGui.TextDisabled("No battle medicine in your Bag."); return; }
        var item = items.FirstOrDefault(item => item.Id == battleMedicineId) ?? items[0];
        battleMedicineId = item.Id;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##BattleMedicine", $"{item.Name} x{InventoryCount(item.Id)}"))
        {
            foreach (var option in items) if (ImGui.Selectable($"{option.Name} x{InventoryCount(option.Id)}", option.Id == item.Id)) { battleMedicineId = option.Id; battleMedicineMove = 0; }
            ImGui.EndCombo();
        }
        item = items.First(option => option.Id == battleMedicineId);
        var targets = party.Select((pokemon, index) => (Pokemon: pokemon, Index: index)).Where(target => CanUseBattleItem(item, target.Pokemon)).ToArray();
        if (targets.Length == 0) { ImGui.TextDisabled("No valid target for this item."); return; }
        var target = targets.FirstOrDefault(target => target.Index == battleMedicineTarget, targets[0]);
        battleMedicineTarget = target.Index;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##MedicineTarget", $"Use on {CleanIdent(target.Pokemon.Ident)} / {target.Pokemon.Condition}"))
        {
            foreach (var option in targets) if (ImGui.Selectable($"{CleanIdent(option.Pokemon.Ident)} / {option.Pokemon.Condition}", option.Index == target.Index)) { battleMedicineTarget = option.Index; battleMedicineMove = 0; }
            ImGui.EndCombo();
        }
        target = targets.First(option => option.Index == battleMedicineTarget);
        var suffix = "";
        if (item.BattleEffect == "pp-restore")
        {
            var moves = target.Pokemon.PpMoves.Select((move, index) => (Move: move, Index: index)).Where(move => move.Move.Pp < move.Move.MaxPp).ToArray();
            var move = moves.FirstOrDefault(move => move.Index == battleMedicineMove, moves[0]);
            battleMedicineMove = move.Index;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##MedicineMove", $"{move.Move.Name} / PP {move.Move.Pp}/{move.Move.MaxPp}"))
            {
                foreach (var option in moves) if (ImGui.Selectable($"{option.Move.Name} / PP {option.Move.Pp}/{option.Move.MaxPp}", option.Index == move.Index)) battleMedicineMove = option.Index;
                ImGui.EndCombo();
            }
            suffix = $" {battleMedicineMove + 1}";
        }
        if (DrawAccentButton($"Use {item.Name}##UseMedicine", new Vector2(-1, 28 * CompanionScale), PokemonGreen))
        {
            var command = $"item {item.Id} {target.Index + 1}{suffix}";
            Run(() => ChooseBattle(command), $"Using {item.Name}...");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Uses one item and consumes your turn. Medicine is only available in PvE.");
    }

    private void DrawConsoleCatch(BattleSnapshotDto current)
    {
        if (current.CaptureShakes is < 4)
            DrawClippedText("BROKE FREE / Capture failed. You can throw another ball.", ImGui.GetContentRegionAvail().X, PokemonOrange);
        var balls = itemCatalog.Where(item => item.Category == "ball" && InventoryCount(item.Id) > 0).ToList();
        if (balls.Count == 0) { ImGui.TextDisabled("No Poké Balls in your Bag."); return; }
        var ball = balls.FirstOrDefault(item => item.Id == battleBallId) ?? balls[0];
        battleBallId = ball.Id;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##ConsoleBall", $"{ball.Name} x{InventoryCount(ball.Id)}"))
        {
            foreach (var option in balls) if (ImGui.Selectable($"{option.Name} x{InventoryCount(option.Id)}", option.Id == ball.Id)) battleBallId = option.Id;
            ImGui.EndCombo();
        }
        ball = balls.First(item => item.Id == battleBallId);
        DrawItemImage(ball.Id, new Vector2(34, 34) * CompanionScale); ImGui.SameLine();
        if (DrawAccentButton($"Throw {ball.Name}", new Vector2(-1, 34 * CompanionScale), PokemonGreen))
        {
            var command = $"ball {ball.Id}";
            Run(() => ChooseBattle(command), $"Throwing a {ball.Name}...");
        }
        DrawClippedText("Lower HP and status improve your odds. Throwing uses your turn.", ImGui.GetContentRegionAvail().X, CompanionMuted);
    }

    private void DrawConsoleExit(BattleSnapshotDto current, bool pvp)
    {
        if (current.Kind == "wild") return;
        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeight() - 7 * CompanionScale);
        ImGui.BeginDisabled(busy);
        if (!pvp) DrawTrainerForfeit();
        else
        {
            if (DrawAccentSmallButton("Forfeit match", PokemonRed)) ImGui.OpenPopup("Forfeit online battle?");
            if (BeginCenteredModal("Forfeit online battle?", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped("This ends the match as a loss. Are you sure?");
                DrawOnlineForfeitNotice();
                if (DrawAccentSmallButton("Forfeit", PokemonRed)) { ImGui.CloseCurrentPopup(); Run(ForfeitPvpBattle, "Forfeiting the match..."); }
                ImGui.SameLine(); if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
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
