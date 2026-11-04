using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string leagueTargetId = string.Empty;
    private string leagueItemId = "potion";
    private string leagueMoveId = string.Empty;
    private string leaguePocket = "Healing";
    private int leagueItemPage;

    private void DrawLeagueNextOpponent(OpponentDto? next, LeagueRunDto run)
    {
        var scale = CompanionScale;
        var line = ImGui.GetTextLineHeightWithSpacing();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var wide = width >= 600 * scale;
        var teamWidth = wide ? Math.Min(270 * scale, width * .4f) : width - 20 * scale;
        var textWidth = Math.Max(1, width - 20 * scale - (wide ? teamWidth + 12 * scale : 0));
        var team = next?.TeamSpecies.ToArray() ?? [];
        var teamRows = team.Length == 0 ? 0 : (team.Length + Math.Max(1, (int)(teamWidth / (44 * scale))) - 1) / Math.Max(1, (int)(teamWidth / (44 * scale)));
        var height = (wide ? Math.Max(line * 3, teamRows * 44 * scale) : line * 3 + teamRows * 44 * scale) + 20 * scale;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, height), ImGui.GetColorU32(CompanionSurface), 4 * scale);
        draw.AddRect(start, start + new Vector2(width, height), ImGui.GetColorU32(CompanionMuted with { W = .3f }), 4 * scale);
        ImGui.SetCursorScreenPos(start + new Vector2(10 * scale));
        DrawClippedText($"NEXT OPPONENT / FIGHT {run.Stage + 1} OF {run.OpponentIds.Count}", textWidth, CompanionMuted);
        ImGui.SetCursorScreenPos(start + new Vector2(10 * scale, 10 * scale + line));
        DrawClippedText(next is null ? "League opponent" : $"{next.Name} / Lv. {next.RecommendedLevel}{(next.Specialty.Length > 0 ? " / " + next.Specialty : "")}", textWidth, CompanionAccent);
        ImGui.SetCursorScreenPos(start + new Vector2(10 * scale, 10 * scale + line * 2));
        DrawClippedText(next?.TacticalHint ?? "Your next League fight is ready.", textWidth, CompanionMuted);
        if (ImGui.IsItemHovered() && next is not null) ImGui.SetTooltip(next.TacticalHint);
        if (team.Length > 0)
        {
            ImGui.SetCursorScreenPos(start + new Vector2(wide ? width - teamWidth - 10 * scale : 10 * scale, wide ? 10 * scale : line * 3 + 10 * scale));
            DrawPveTeamStrip(team, teamWidth);
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawLeagueCheckpointBag(LeagueRunDto run)
    {
        var scale = CompanionScale;
        var gap = 6 * scale;
        var line = ImGui.GetTextLineHeight();
        var width = ImGui.GetContentRegionAvail().X;
        var heading = ImGui.GetCursorScreenPos();
        var allowance = $"{Math.Max(0, 3 - run.CheckpointActions)} / 3 actions left";
        var allowanceWidth = ImGui.CalcTextSize(allowance).X;
        DrawClippedText("BAG / CHECKPOINT MEDICINE", Math.Max(1, width - allowanceWidth - gap), CompanionAccent);
        ImGui.SetCursorScreenPos(new(heading.X + width - allowanceWidth, heading.Y));
        ImGui.TextColored(PokemonOrange, allowance);
        ImGui.Spacing();

        var owned = GetBattleMedicine();
        var selected = owned.FirstOrDefault(item => item.Id == leagueItemId);
        if (!owned.Any(item => BattleBag.Pocket(item) == leaguePocket)) leaguePocket = selected is not null ? BattleBag.Pocket(selected) : owned.Count > 0 ? BattleBag.Pocket(owned[0]) : "Healing";
        var items = GetBattlePocket(leaguePocket);
        if (!items.Any(item => item.Id == leagueItemId)) leagueItemId = items.FirstOrDefault()?.Id ?? string.Empty;
        foreach (var pocket in BattlePockets)
        {
            if (pocket != "Healing") ImGui.SameLine();
            ImGui.BeginDisabled(GetBattlePocket(pocket).Count == 0);
            if (DrawAccentButton(pocket + "##LeaguePocket", new Vector2(ImGui.CalcTextSize(pocket).X + 16 * scale, ImGui.GetFrameHeight()),
                pocket == leaguePocket ? CompanionAccent : CompanionMuted))
            { leaguePocket = pocket; leagueItemPage = 0; }
            ImGui.EndDisabled();
        }
        items = GetBattlePocket(leaguePocket);
        if (!items.Any(item => item.Id == leagueItemId)) leagueItemId = items.FirstOrDefault()?.Id ?? string.Empty;
        selected = items.FirstOrDefault(item => item.Id == leagueItemId);

        var wide = width >= 520 * scale;
        var cardHeight = Math.Max(76 * scale, line * 3 + 16 * scale);
        var itemHeight = Math.Max(42 * scale, line * 2 + 10 * scale);
        var columns = width >= 300 * scale ? 2 : 1;
        var rows = Math.Max(1, (run.Vitals.Count + columns - 1) / columns);
        var partyHeight = rows * (cardHeight + gap) - gap;
        var pageSize = wide ? Math.Max(1, (int)((partyHeight + gap) / (itemHeight + gap))) : 4;
        ImGui.PushID("LeagueMedicinePager");
        if (items.Count > pageSize && width >= 360 * scale) ImGui.SameLine();
        DrawBattleTrayPager(items.Count, pageSize, ref leagueItemPage);
        ImGui.PopID();
        ImGui.Spacing();
        var start = ImGui.GetCursorScreenPos();
        var itemWidth = wide ? Math.Min(210 * scale, width * .3f) : (width - gap) / 2;
        var labelHeight = line + gap;
        DrawClippedText("ITEMS", wide ? itemWidth : width, CompanionMuted);
        var visible = Math.Min(pageSize, Math.Max(0, items.Count - leagueItemPage * pageSize));
        for (var slot = 0; slot < visible; slot++)
        {
            var item = items[leagueItemPage * pageSize + slot];
            ImGui.SetCursorScreenPos(start + new Vector2(wide ? 0 : slot % 2 * (itemWidth + gap), labelHeight + (wide ? slot : slot / 2) * (itemHeight + gap)));
            if (DrawBattleTrayCard("league-item-" + item.Id, item.Name, $"x{InventoryCount(item.Id):N0} / {BattleBag.Summary(item)}", itemWidth, itemHeight,
                item.Id == leagueItemId, "", $"{item.Name} x{InventoryCount(item.Id):N0}\n{BattleBag.Summary(item)}\n{item.Description}", item.Id))
            { leagueItemId = item.Id; leagueMoveId = string.Empty; }
        }
        var inventoryHeight = Math.Max(itemHeight, (wide ? visible : (visible + 1) / 2) * (itemHeight + gap) - gap);
        if (owned.Count == 0)
        {
            ImGui.SetCursorScreenPos(start + new Vector2(0, labelHeight));
            DrawBattleInfoLines("No battle medicine in your Bag. Buy supplies before your next run.", wide ? itemWidth : width, 3, CompanionMuted);
            inventoryHeight = Math.Max(inventoryHeight, line * 3 + gap);
        }

        selected = items.FirstOrDefault(item => item.Id == leagueItemId);
        var target = SelectLeagueMedicineTarget(run, selected);
        var partyX = wide ? itemWidth + gap : 0;
        var partyY = wide ? 0 : labelHeight + inventoryHeight + gap * 2;
        var partyWidth = width - partyX;
        ImGui.SetCursorScreenPos(start + new Vector2(partyX, partyY));
        DrawClippedText("CHOOSE A TARGET", partyWidth, CompanionMuted);
        var targetWidth = (partyWidth - (columns - 1) * gap) / columns;
        for (var slot = 0; slot < run.Vitals.Count; slot++)
        {
            var pokemon = run.Vitals[slot];
            var hp = BattleBag.Health(pokemon);
            var reason = selected is null ? "" : BattleBag.Reason(selected, pokemon);
            var preview = selected is null ? "" : BattleBag.Preview(selected, pokemon, leagueMoveId);
            var tooltip = $"{pokemon.Name}\n{hp.Label}\n{preview}\n" + string.Join("\n", pokemon.Moves.Select(move => $"{move.Name} / {move.Pp}/{move.MaxPp} PP"));
            ImGui.SetCursorScreenPos(start + new Vector2(partyX + slot % columns * (targetWidth + gap), partyY + labelHeight + slot / columns * (cardHeight + gap)));
            if (DrawBattleTrayCard("league-target-" + pokemon.PokemonId, pokemon.Name, hp.Label, targetWidth, cardHeight,
                leagueTargetId == pokemon.PokemonId, reason, tooltip, pokemon.Species, true, hp.Ratio, pokemon.Shiny,
                selected is null ? "Carried team" : preview))
            { leagueTargetId = pokemon.PokemonId; leagueMoveId = string.Empty; }
        }
        var bodyHeight = Math.Max(labelHeight + inventoryHeight, partyY + labelHeight + partyHeight);
        ImGui.SetCursorScreenPos(start + new Vector2(0, bodyHeight + gap));
        DrawClippedText("HP, status and PP carry over. Team builds stay locked.", width, CompanionMuted);
        target = SelectLeagueMedicineTarget(run, selected);
        if (target is not null && selected?.BattleEffect == "pp-restore") DrawLeagueMedicineMoves(target);
        ImGui.Spacing();
        DrawLeagueMedicineAction(run, selected, target);
        ImGui.Spacing();
        DrawBattleInfoLines("Uses one owned item and one action. Shared with the fight you just cleared.", width, 2, CompanionMuted);
    }

    private LeagueVitalDto? SelectLeagueMedicineTarget(LeagueRunDto run, ShopItemDto? item)
    {
        var target = run.Vitals.FirstOrDefault(pokemon => pokemon.PokemonId == leagueTargetId);
        if (target is null || item is not null && BattleBag.Reason(item, target).Length > 0)
            target = run.Vitals.FirstOrDefault(pokemon => item is null || BattleBag.Reason(item, pokemon).Length == 0);
        leagueTargetId = target?.PokemonId ?? string.Empty;
        if (item?.BattleEffect == "pp-restore" && target is not null)
        {
            var move = target.Moves.FirstOrDefault(move => move.Id == leagueMoveId && move.Pp < move.MaxPp)
                ?? target.Moves.FirstOrDefault(move => move.Pp < move.MaxPp);
            leagueMoveId = move?.Id ?? string.Empty;
        }
        else leagueMoveId = string.Empty;
        return target;
    }

    private void DrawLeagueMedicineMoves(LeagueVitalDto target)
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        var selected = target.Moves.FirstOrDefault(move => move.Id == leagueMoveId);
        if (!ImGui.BeginCombo("##LeagueMedicineMove", selected is null ? "No move needs PP" : $"{selected.Name} / PP {selected.Pp}/{selected.MaxPp}")) return;
        foreach (var move in target.Moves)
        {
            ImGui.BeginDisabled(move.Pp >= move.MaxPp);
            if (ImGui.Selectable($"{move.Name} / PP {move.Pp}/{move.MaxPp}##{move.Id}", move.Id == leagueMoveId)) leagueMoveId = move.Id;
            ImGui.EndDisabled();
        }
        ImGui.EndCombo();
    }

    private void DrawLeagueMedicineAction(LeagueRunDto run, ShopItemDto? item, LeagueVitalDto? target)
    {
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorScreenPos();
        var wide = width >= 520 * scale;
        var line = ImGui.GetTextLineHeightWithSpacing();
        var buttonHeight = Math.Max(30 * scale, ImGui.GetFrameHeight());
        var buttonWidth = Math.Min(width - 16 * scale, Math.Max(155 * scale, ImGui.CalcTextSize("Use " + item?.Name).X + 24 * scale));
        var height = Math.Max(buttonHeight, line * 2) + 16 * scale + (wide ? 0 : buttonHeight + 8 * scale);
        var preview = run.CheckpointActions >= 3 ? "No medicine actions left." : item is null ? "No medicine owned." : target is null ? "No party member needs this item." : BattleBag.Preview(item, target, leagueMoveId);
        var enabled = !busy && run.CheckpointActions < 3 && item is not null && target is not null && BattleBag.Reason(item, target).Length == 0
            && (item.BattleEffect != "pp-restore" || target.Moves.Any(move => move.Id == leagueMoveId && move.Pp < move.MaxPp));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, height), ImGui.GetColorU32(CompanionSurface), 4 * scale);
        draw.AddRect(start, start + new Vector2(width, height), ImGui.GetColorU32(CompanionMuted with { W = .3f }), 4 * scale);
        ImGui.SetCursorScreenPos(start + new Vector2(8 * scale));
        if (item is not null) DrawItemImage(item.Id, new Vector2(30 * scale));
        var textX = item is null ? 8 * scale : 44 * scale;
        var textWidth = Math.Max(1, width - textX - 8 * scale - (wide ? buttonWidth + 12 * scale : 0));
        ImGui.SetCursorScreenPos(start + new Vector2(textX, 8 * scale));
        DrawClippedText((item?.Name ?? "Checkpoint Bag") + (target is null ? "" : " > " + target.Name), textWidth, Vector4.One);
        ImGui.SetCursorScreenPos(start + new Vector2(textX, 8 * scale + line));
        DrawClippedText(preview, textWidth, enabled ? CompanionAccent : CompanionMuted);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(preview);
        ImGui.SetCursorScreenPos(start + new Vector2(wide ? width - buttonWidth - 8 * scale : 8 * scale, wide ? (height - buttonHeight) / 2 : height - buttonHeight - 8 * scale));
        ImGui.BeginDisabled(!enabled);
        if (DrawAccentButton("Use " + (item?.Name ?? "medicine") + "##LeagueUseMedicine", new Vector2(wide ? buttonWidth : width - 16 * scale, buttonHeight), CompanionAccent))
            Run(() => HealLeague(item!.BattleEffect == "pp-restore"), "Using checkpoint medicine...");
        ImGui.EndDisabled();
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, height));
    }
}
