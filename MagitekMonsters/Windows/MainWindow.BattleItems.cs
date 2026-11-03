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
    private string battlePocket = "Healing";
    private int battleItemPage;
    private int battleBallPage;

    private void DrawCompactTrayCombatant(BattleCombatantDto? pokemon, bool ours, bool wild, bool captured, string role, float width)
    {
        var caught = pokemon is not null && (captured || IsAdventureSpeciesCaught(pokemon.Species));
        DrawClippedText(wild && !ours ? caught ? "CAUGHT" : "NOT CAUGHT" : role, width,
            wild && !ours ? caught ? PokemonGreen : PokemonYellow : CompanionAccent);
        if (pokemon is null) { DrawClippedText("Waiting...", width, CompanionMuted); return; }
        var hp = BattleBag.Health(pokemon.Condition);
        var origin = ImGui.GetCursorScreenPos();
        var art = Math.Min(34 * CompanionScale, ImGui.GetContentRegionAvail().Y - 2 * CompanionScale);
        if (width > 145 * CompanionScale && art > 0)
        {
            DrawPokemonBattleImage(pokemon.Species, pokemon.Shiny, new Vector2(art));
            ImGui.SetCursorScreenPos(origin + new Vector2(art + 5 * CompanionScale, 0));
        }
        var textWidth = width - (ImGui.GetCursorScreenPos().X - origin.X);
        var textX = ImGui.GetCursorScreenPos().X;
        DrawClippedText($"{pokemon.Name} / Lv. {pokemon.Level}", textWidth, Vector4.One);
        ImGui.SetCursorScreenPos(new(textX, origin.Y + ImGui.GetTextLineHeight() + 2 * CompanionScale));
        DrawClippedText(hp.Label, textWidth, CompanionMuted);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{pokemon.Species}\n{string.Join(" / ", SpeciesDisplayCatalog.Get(pokemon.Species)?.Types ?? [])}\n{hp.Label}");
        ImGui.SetCursorScreenPos(new(origin.X, origin.Y + Math.Max(art, ImGui.GetTextLineHeight() * 2 + 2 * CompanionScale)));
        DrawProgressBar(hp.Ratio, new Vector2(-1, 4 * CompanionScale), "", HpBarRed);
    }

    private void DrawConsoleMedicine(BattleSnapshotDto current, List<BattleSidePokemonDto> party)
    {
        if (current.Rules?.BagLimit is int limit && current.BagActionsUsed >= limit)
        {
            DrawClippedText(limit == 0 ? "Bag disabled by this challenge" : "Medicine limit reached", ImGui.GetContentRegionAvail().X, PokemonOrange);
            return;
        }
        var owned = GetBattleMedicine();
        if (owned.Count == 0) { ImGui.TextDisabled("No battle medicine in your Bag."); return; }
        var scale = CompanionScale;
        var start = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail();
        var line = ImGui.GetTextLineHeight(); var gap = 5 * scale;
        var frame = ImGui.GetFrameHeight();
        var wide = size.X >= 560 * scale && size.Y >= 240 * scale;
        var selected = owned.FirstOrDefault(item => item.Id == battleMedicineId);
        if (!owned.Any(item => BattleBag.Pocket(item) == battlePocket)) battlePocket = BattleBag.Pocket(selected ?? owned[0]);
        var items = GetBattlePocket(battlePocket);
        if (!items.Any(item => item.Id == battleMedicineId)) battleMedicineId = items[0].Id;

        foreach (var pocket in BattlePockets)
        {
            if (pocket != "Healing") ImGui.SameLine();
            ImGui.BeginDisabled(GetBattlePocket(pocket).Count == 0);
            if (DrawAccentButton(pocket + "##BattlePocket", new Vector2(ImGui.CalcTextSize(pocket).X + 16 * scale, frame),
                battlePocket == pocket ? CompanionAccent : CompanionMuted))
            { battlePocket = pocket; battleItemPage = 0; }
            ImGui.EndDisabled();
        }
        items = GetBattlePocket(battlePocket);
        if (!items.Any(item => item.Id == battleMedicineId)) battleMedicineId = items[0].Id;
        selected = items.First(item => item.Id == battleMedicineId);
        var pp = selected.BattleEffect == "pp-restore";
        var footerHeight = (wide ? Math.Max(frame, line * 2 + gap) + 16 * scale : frame + line + gap) + (pp ? frame + gap : 0);
        var heading = wide ? line + gap : 0;
        var bodyTop = start.Y + frame + gap + heading;
        var footerY = start.Y + size.Y - footerHeight;
        var bodyHeight = Math.Max(1, footerY - bodyTop - gap);
        var itemHeight = wide ? Math.Max(38 * scale, line * 2 + 10 * scale) : line + 12 * scale;
        var rowLimit = Math.Max(76 * scale, line * 3 + 16 * scale);
        var hintHeight = wide ? line + gap : 0;
        var gridHeight = Math.Min(bodyHeight - hintHeight, rowLimit * 3 + gap * 2);
        var pageSize = wide ? Math.Max(1, (int)((gridHeight + gap) / (itemHeight + gap))) : 2;
        ImGui.SameLine();
        DrawBattleTrayPager(items.Count, pageSize, ref battleItemPage);
        var itemWidth = wide ? Math.Min(210 * scale, size.X * .34f) : (size.X - gap) / 2;
        if (wide)
        {
            ImGui.SetCursorScreenPos(new(start.X, bodyTop - heading));
            DrawClippedText("ITEMS", itemWidth, CompanionMuted);
            ImGui.SetCursorScreenPos(new(start.X + itemWidth + gap, bodyTop - heading));
            DrawClippedText("CHOOSE A TARGET", size.X - itemWidth - gap, CompanionMuted);
        }
        for (var slot = 0; slot < pageSize; slot++)
        {
            var index = battleItemPage * pageSize + slot;
            if (index >= items.Count) break;
            var item = items[index];
            ImGui.SetCursorScreenPos(new(start.X + (wide ? 0 : slot * (itemWidth + gap)), bodyTop + (wide ? slot * (itemHeight + gap) : 0)));
            if (DrawBattleTrayCard("medicine-" + item.Id, item.Name, $"x{InventoryCount(item.Id):N0}" + (wide ? " / " + BattleBag.Summary(item) : ""), itemWidth, itemHeight,
                item.Id == battleMedicineId, "", $"{item.Name} x{InventoryCount(item.Id):N0}\n{BattleBag.Summary(item)}\n{item.Description}", item.Id))
            { battleMedicineId = item.Id; battleMedicineMove = 0; }
        }
        selected = items.First(item => item.Id == battleMedicineId);
        var firstValid = party.FindIndex(pokemon => CanUseBattleItem(selected, pokemon));
        if (battleMedicineTarget < 0 || battleMedicineTarget >= party.Count || !CanUseBattleItem(selected, party[battleMedicineTarget])) battleMedicineTarget = firstValid;
        var targetX = wide ? start.X + itemWidth + gap : start.X;
        var targetY = wide ? bodyTop : bodyTop + itemHeight + gap;
        var columns = wide ? 2 : 3;
        var rows = wide ? 3 : 2;
        var targetWidth = (size.X - (targetX - start.X) - (columns - 1) * gap) / columns;
        var targetHeight = Math.Max(1, Math.Min(rowLimit, (footerY - gap - hintHeight - targetY - (rows - 1) * gap) / rows));
        for (var slot = 0; slot < Math.Min(6, party.Count); slot++)
        {
            var pokemon = party[slot]; var hp = BattleBag.Health(pokemon.Condition);
            var reason = BattleBag.Reason(selected, pokemon);
            var name = CleanIdent(pokemon.Ident);
            var species = pokemon.Details.Split(',')[0];
            ImGui.SetCursorScreenPos(new(targetX + slot % columns * (targetWidth + gap), targetY + slot / columns * (targetHeight + gap)));
            if (DrawBattleTrayCard("medicine-target-" + slot, name, hp.Label, targetWidth, targetHeight,
                battleMedicineTarget == slot, reason, $"{name}\n{hp.Label}\n{(reason.Length > 0 ? reason : BattleBag.Preview(selected, pokemon, BattleBag.MoveSlot(pokemon, battleMedicineMove)))}",
                species, true, hp.Ratio, pokemon.Details.Split(',').Any(part => part.Trim() == "shiny"), pokemon.Active ? "ACTIVE" : "BENCH"))
            { battleMedicineTarget = slot; battleMedicineMove = 0; }
        }
        if (wide)
        {
            var gridEnd = targetY + rows * (targetHeight + gap);
            ImGui.SetCursorScreenPos(new(targetX, gridEnd));
            DrawClippedText("One item uses your turn. Choose any eligible party member.", size.X - itemWidth - gap, CompanionMuted);
            footerY = Math.Min(footerY, gridEnd + line + gap * 2);
        }
        var target = party.ElementAtOrDefault(battleMedicineTarget);
        if (target is not null) battleMedicineMove = BattleBag.MoveSlot(target, battleMedicineMove);
        ImGui.SetCursorScreenPos(new(start.X, footerY));
        if (selected.BattleEffect == "pp-restore")
        {
            ImGui.SetNextItemWidth(-1);
            var move = target?.PpMoves.ElementAtOrDefault(battleMedicineMove);
            if (ImGui.BeginCombo("##MedicineMove", move is null ? "No move needs PP" : $"{move.Name} / PP {move.Pp}/{move.MaxPp}"))
            {
                if (target is not null)
                    for (var index = 0; index < target.PpMoves.Count; index++)
                    {
                        var option = target.PpMoves[index];
                        ImGui.BeginDisabled(option.Pp >= option.MaxPp);
                        if (ImGui.Selectable($"{option.Name} / PP {option.Pp}/{option.MaxPp}##{index}", battleMedicineMove == index)) battleMedicineMove = index;
                        ImGui.EndDisabled();
                    }
                ImGui.EndCombo();
            }
        }
        var preview = target is null ? "No party member needs this item" : BattleBag.Preview(selected, target, battleMedicineMove);
        var command = target is null ? null : BattleBag.Command(selected, target, battleMedicineTarget, battleMedicineMove);
        var actionY = ImGui.GetCursorScreenPos().Y;
        var buttonWidth = Math.Min(size.X * .4f, Math.Max(130 * scale, ImGui.CalcTextSize("Use " + selected.Name).X + 20 * scale));
        if (wide)
        {
            var actionHeight = Math.Max(frame, line * 2 + gap) + 16 * scale;
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(new(start.X, actionY), new(start.X + size.X, actionY + actionHeight), ImGui.GetColorU32(CompanionSurface), 4 * scale);
            draw.AddRect(new(start.X, actionY), new(start.X + size.X, actionY + actionHeight), ImGui.GetColorU32(CompanionMuted with { W = .3f }), 4 * scale);
            ImGui.SetCursorScreenPos(new(start.X + 8 * scale, actionY + 8 * scale));
            DrawClippedText(selected.Name + (target is null ? " / Choose a target" : " > " + CleanIdent(target.Ident)), size.X - buttonWidth - 28 * scale, Vector4.One);
            ImGui.SetCursorScreenPos(new(start.X + 8 * scale, actionY + 8 * scale + line + gap));
            DrawClippedText(preview, size.X - buttonWidth - 28 * scale, target is null ? PokemonOrange : CompanionAccent);
            ImGui.SetCursorScreenPos(new(start.X + size.X - buttonWidth - 8 * scale, actionY + (actionHeight - frame) / 2));
        }
        else DrawClippedText(preview, size.X, target is null ? PokemonOrange : CompanionAccent);
        ImGui.BeginDisabled(command is null);
        if (DrawAccentButton($"Use {selected.Name}##UseMedicine", new Vector2(wide ? buttonWidth : -1, frame), CompanionAccent) && command is not null)
            Run(() => ChooseBattle(command), $"Using {selected.Name}...");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"{selected.Name} x{InventoryCount(selected.Id):N0}\n{preview}\nUses one item and your turn. PvE only.");
    }

    private void DrawConsoleCatch(BattleSnapshotDto current)
    {
        var scale = CompanionScale; var gap = 5 * scale;
        var start = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail();
        var frame = ImGui.GetFrameHeight(); var line = ImGui.GetTextLineHeight();
        var balls = GetBattleBalls();
        var layout = BattleLayout.CatchTray(size.X, size.Y, scale, line, frame, balls.Count);
        var failed = current.CaptureShakes is < 4;
        var draw = ImGui.GetWindowDrawList();
        if (failed)
        {
            draw.AddRectFilled(start, start + new Vector2(size.X, layout.HeaderHeight), ImGui.GetColorU32(PokemonOrange with { W = .10f }), 3 * scale);
            draw.AddRectFilled(start, start + new Vector2(3 * scale, layout.HeaderHeight), ImGui.GetColorU32(PokemonOrange));
        }
        ImGui.SetCursorScreenPos(start + new Vector2(8, 6) * scale);
        DrawClippedText(failed ? "BROKE FREE / Choose your next ball" : "POKÉ BALLS / Choose a ball", size.X - 16 * scale, failed ? PokemonOrange : CompanionAccent);
        var top = start.Y + layout.HeaderHeight + gap;
        if (balls.Count == 0)
        {
            ImGui.SetCursorScreenPos(new(start.X, top));
            DrawClippedText("No Poké Balls in your Bag.", size.X, CompanionMuted);
            return;
        }
        if (!balls.Any(item => item.Id == battleBallId)) battleBallId = balls[0].Id;
        ImGui.SetCursorScreenPos(new(start.X, top));
        DrawBattleTrayPager(balls.Count, layout.PageSize, ref battleBallPage);
        var cardsTop = top + layout.PagerHeight;
        for (var slot = 0; slot < layout.PageSize; slot++)
        {
            var index = battleBallPage * layout.PageSize + slot;
            if (index >= balls.Count) break;
            var ball = balls[index];
            var bonus = current.BallOptions.FirstOrDefault(option => option.ItemId == ball.Id);
            ImGui.SetCursorScreenPos(new(start.X + slot % layout.Columns * (layout.CardWidth + gap), cardsTop + slot / layout.Columns * (layout.CardHeight + gap)));
            if (DrawBattleBallCard(ball, bonus, layout.CardWidth, layout.CardHeight, ball.Id == battleBallId, layout.Wide)) battleBallId = ball.Id;
        }
        var selected = balls.First(ball => ball.Id == battleBallId);
        var effect = current.BallOptions.FirstOrDefault(option => option.ItemId == selected.Id);
        var effectLabel = effect is null ? "No current bonus information" : (effect.BonusActive && !effect.Guaranteed ? "Bonus active / " : "") + BallModifier(effect);
        var detail = effect?.Detail ?? selected.Description;
        var infoX = layout.Wide ? start.X + layout.GridWidth + gap * 2 : start.X;
        var infoY = layout.Wide ? top : top + layout.BodyHeight - layout.InfoHeight;
        ImGui.SetCursorScreenPos(new(infoX, infoY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8 * scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("BattleBallInfo", new Vector2(layout.InfoWidth, layout.InfoHeight), true, FixedBattlePanel);
        var infoWidth = ImGui.GetContentRegionAvail().X;
        if (layout.Wide)
        {
            DrawClippedText(selected.Name, infoWidth, PokemonOrange);
            DrawBattleInfoLines(selected.Description, infoWidth, 2, CompanionMuted);
            ImGui.Spacing();
        }
        DrawClippedText(effectLabel, infoWidth, effect?.BonusActive == true || effect?.Guaranteed == true ? PokemonGreen : PokemonYellow);
        if (layout.Wide) DrawBattleInfoLines(detail, infoWidth, 2, CompanionMuted);
        else DrawClippedText(detail, infoWidth, CompanionMuted);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{detail}\n\n{selected.Description}");
        if (layout.Wide && current.OpponentActive is { } target)
        {
            ImGui.Spacing();
            DrawClippedText(target.Name, infoWidth, PokemonOrange);
            var hp = BattleBag.Health(target.Condition);
            DrawProgressBar(hp.Ratio, new Vector2(-1, 5 * scale), "", HpBarRed);
            DrawClippedText(hp.Label, infoWidth, CompanionMuted);
            var caught = IsAdventureSpeciesCaught(target.Species);
            DrawClippedText(caught ? "ALREADY CAUGHT" : "NOT CAUGHT YET", infoWidth, caught ? PokemonGreen : PokemonYellow);
        }
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
        var footerY = top + layout.BodyHeight + gap;
        draw.AddRectFilled(new(start.X, footerY), new(start.X + size.X, footerY + layout.FooterHeight), ImGui.GetColorU32(CompanionSurface), 4 * scale);
        draw.AddRect(new(start.X, footerY), new(start.X + size.X, footerY + layout.FooterHeight), ImGui.GetColorU32(CompanionMuted with { W = .3f }), 4 * scale);
        var buttonWidth = Math.Min(size.X * .45f, Math.Max(140 * scale, ImGui.CalcTextSize("Throw " + selected.Name).X + 20 * scale));
        var summaryWidth = Math.Max(1, size.X - buttonWidth - 28 * scale);
        ImGui.SetCursorScreenPos(new(start.X + 8 * scale, footerY + 8 * scale));
        DrawClippedText($"{selected.Name} / x{InventoryCount(selected.Id):N0}", summaryWidth, Vector4.One);
        ImGui.SetCursorScreenPos(new(start.X + 8 * scale, footerY + 8 * scale + line + gap));
        DrawClippedText("Uses one ball and your turn", summaryWidth, CompanionMuted);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Uses one owned ball and consumes your turn. Lower HP and status improve capture odds.");
        ImGui.SetCursorScreenPos(new(start.X + size.X - buttonWidth - 8 * scale, footerY + (layout.FooterHeight - frame) / 2));
        var buttonLabel = ImGui.CalcTextSize("Throw " + selected.Name).X + 12 * scale <= buttonWidth ? "Throw " + selected.Name : "Throw";
        if (DrawAccentButton(buttonLabel + "##ThrowBall", new Vector2(buttonWidth, frame), PokemonOrange))
        {
            var command = $"ball {selected.Id}";
            Run(() => ChooseBattle(command), $"Throwing a {selected.Name}...");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{selected.Name}\n{effectLabel}\n{detail}");
        ImGui.SetCursorScreenPos(new(start.X, footerY + layout.FooterHeight));
    }

    private bool DrawBattleBallCard(ShopItemDto ball, BallEffectDto? effect, float width, float height, bool selected, bool wide)
    {
        if (!wide) return DrawBattleTrayCard("ball-" + ball.Id, ball.Name, $"x{InventoryCount(ball.Id):N0}", width, height, selected, "", ball.Description, ball.Id, accent: PokemonOrange);
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight();
        var min = ImGui.GetCursorScreenPos(); var max = min + new Vector2(width, height);
        var clicked = ImGui.InvisibleButton("##Ball-" + ball.Id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered(); var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(.24f, .17f, .10f, 1) : CompanionSurface), 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? PokemonOrange : CompanionMuted with { W = .35f }), 4 * scale);
        draw.PushClipRect(min, max, true);
        ImGui.SetCursorScreenPos(min + new Vector2(7 * scale));
        DrawItemImage(ball.Id, new Vector2(32 * scale));
        var quantity = $"x{InventoryCount(ball.Id):N0}";
        var quantityWidth = Math.Min(width - 12 * scale, ImGui.CalcTextSize(quantity).X);
        ImGui.SetCursorScreenPos(new(max.X - quantityWidth - 7 * scale, min.Y + 8 * scale));
        DrawClippedText(quantity, quantityWidth, PokemonYellow);
        ImGui.SetCursorScreenPos(new(min.X + 7 * scale, min.Y + 40 * scale));
        DrawClippedText(ball.Name, width - 14 * scale, Vector4.One);
        ImGui.SetCursorScreenPos(new(min.X + 7 * scale, min.Y + 40 * scale + line + 3 * scale));
        DrawClippedText(effect is null ? "Select to inspect" : BallModifier(effect), width - 14 * scale,
            effect?.BonusActive == true || effect?.Guaranteed == true ? PokemonGreen : CompanionMuted);
        draw.PopClipRect(); ImGui.SetCursorScreenPos(after);
        if (hovered) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip($"{ball.Name} x{InventoryCount(ball.Id):N0}\n{ball.Description}\n{effect?.Detail}"); }
        return clicked;
    }

    private void DrawBattleInfoLines(string text, float width, int maximum, Vector4 color)
    {
        var remaining = text.ReplaceLineEndings(" ").Trim();
        for (var row = 0; row < maximum && remaining.Length > 0; row++)
        {
            if (row == maximum - 1 || ImGui.CalcTextSize(remaining).X <= width)
            { DrawClippedText(remaining, width, color); break; }
            var count = 1;
            while (count < remaining.Length && ImGui.CalcTextSize(remaining[..(count + 1)]).X <= width) count++;
            var space = remaining.LastIndexOf(' ', Math.Max(0, count - 1), count);
            if (space > 0) count = space;
            DrawClippedText(remaining[..count], width, color);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
            remaining = remaining[count..].TrimStart();
        }
    }

    private void DrawBattleTrayPager(int count, int pageSize, ref int page)
    {
        var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 0, pages - 1);
        if (pages == 1) return;
        ImGui.PushID("BattleTrayPager");
        ImGui.BeginDisabled(page == 0);
        if (ImGui.Button("<")) page--;
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.TextDisabled($"{page + 1}/{pages}"); ImGui.SameLine();
        ImGui.BeginDisabled(page + 1 >= pages);
        if (ImGui.Button(">")) page++;
        ImGui.EndDisabled(); ImGui.PopID();
    }

    private bool DrawBattleTrayCard(string id, string name, string detail, float width, float height, bool selected,
        string reason, string tooltip, string icon, bool pokemon = false, float? hp = null, bool shiny = false, string tag = "", Vector4? accent = null)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight();
        height = Math.Min(height, Math.Max(76 * scale, line * 3 + 16 * scale));
        var min = ImGui.GetCursorScreenPos(); var max = min + new Vector2(Math.Max(1, width), Math.Max(1, height));
        ImGui.BeginDisabled(reason.Length > 0);
        var clicked = ImGui.InvisibleButton("##" + id, max - min);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var after = ImGui.GetCursorScreenPos();
        var color = selected || hovered ? accent ?? CompanionAccent : CompanionMuted with { W = .35f };
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(.14f, .18f, .25f, 1) : CompanionSurface), 3 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(color), 3 * scale);
        draw.PushClipRect(min, max, true);
        var art = width > 125 * scale && (!pokemon || height >= 36 * scale) ? Math.Min(34 * scale, height - 6 * scale) : 0;
        if (art > 0)
        {
            ImGui.SetCursorScreenPos(min + new Vector2(4 * scale));
            if (pokemon) DrawPokemonIcon(icon, shiny, new Vector2(art)); else DrawItemImage(icon, new Vector2(art));
        }
        var x = min.X + 6 * scale + (art > 0 ? art + 3 * scale : 0);
        var textWidth = Math.Max(1, max.X - x - 5 * scale);
        var twoLines = height >= line * 2 + (hp.HasValue ? 12 : 6) * scale;
        ImGui.SetCursorScreenPos(new(x, min.Y + 3 * scale));
        var quantityWidth = !twoLines && !pokemon ? Math.Min(textWidth * .45f, ImGui.CalcTextSize(detail).X) : 0;
        DrawClippedText(name, Math.Max(1, textWidth - quantityWidth - (quantityWidth > 0 ? 4 * scale : 0)), reason.Length > 0 ? CompanionMuted : Vector4.One);
        if (quantityWidth > 0)
        {
            ImGui.SetCursorScreenPos(new(max.X - 5 * scale - quantityWidth, min.Y + 3 * scale));
            DrawClippedText(detail, quantityWidth, PokemonYellow);
        }
        if (twoLines)
        {
            ImGui.SetCursorScreenPos(new(x, min.Y + line + 5 * scale));
            DrawClippedText(detail, textWidth, pokemon ? CompanionMuted : PokemonYellow);
            if (pokemon && height >= line * 3 + 16 * scale)
            {
                ImGui.SetCursorScreenPos(new(x, min.Y + line * 2 + 7 * scale));
                DrawClippedText(reason.Length > 0 ? reason : tag, textWidth, reason.Length > 0 || tag != "ACTIVE" ? CompanionMuted : CompanionAccent);
            }
        }
        if (hp is float ratio)
        {
            var barMin = new Vector2(min.X + 5 * scale, max.Y - 6 * scale);
            var barMax = new Vector2(max.X - 5 * scale, max.Y - 3 * scale);
            draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(new Vector4(.04f, .05f, .07f, 1)), scale);
            draw.AddRectFilled(barMin, new(barMin.X + (barMax.X - barMin.X) * Math.Clamp(ratio, 0, 1), barMax.Y), ImGui.GetColorU32(HpBarRed), scale);
        }
        draw.PopClipRect();
        ImGui.SetCursorScreenPos(after);
        ImGui.EndDisabled();
        if (hovered) { ImGui.SetMouseCursor(reason.Length == 0 ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.NotAllowed); ImGui.SetTooltip(tooltip); }
        return clicked;
    }
}
