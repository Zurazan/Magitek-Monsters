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
    private static readonly Vector4 FrontierPanelSurface = new(0.133f, 0.141f, 0.169f, 1f);
    private static readonly Vector4 FrontierTileSurface = new(0.147f, 0.159f, 0.193f, 1f);
    private static readonly Vector4 FrontierPanelBorder = new(0.212f, 0.235f, 0.282f, 1f);
    private bool frontierFactory;
    private int frontierBracket;
    private int frontierMode;
    private bool frontierTeamModeChosen;
    private int frontierRentalIndex;
    private readonly HashSet<string> frontierTeamIds = [];

    private static string FrontierFacilityLabel(bool factory) => factory ? "Battle Factory" : "Battle Tower";
    private static string FrontierBracketLabel(bool standard) => standard ? "Standard" : "Accessible";

    private void DrawFrontierRuns(TrainerSnapshotDto current, FrontierRunDto? saved)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8) * scale);
        if (saved is not null)
        {
            DrawFrontierPanel("FrontierSavedRun", new Vector2(10) * scale, () =>
            {
                var drafting = saved.Facility == "factory" && saved.Phase == "draft" && saved.Status != "active";
                DrawFrontierRunSummary(saved, drafting);
                if (saved.Status == "active") DrawSavedFrontierFight(saved);
                else if (saved.Facility == "factory") DrawFactory(saved, drafting);
                else DrawTowerCheckpoint(saved);
                DrawFrontierAbandonPopup();
            });
            ImGui.PopStyleVar();
            return;
        }

        DrawGoalText("Level 50 · 3 Pokémon · BP rewards", CompanionMuted);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4 * scale, 0));
        if (ImGui.BeginTable("FrontierFacilities", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableNextColumn();
            if (DrawFrontierFacility(false, "Bring your party or borrow a prepared team.")) frontierFactory = false;
            ImGui.TableNextColumn();
            if (DrawFrontierFacility(true, "Draft rentals. Swap one after each win.")) frontierFactory = true;
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        DrawFrontierSetup(current);
        if (current.FrontierRun is { } last)
            DrawGoalText($"Last run · {FrontierFacilityLabel(last.Facility == "factory")} · {last.Streak:N0} wins · {(last.Status == "abandoned" ? "Ended" : "Lost streak")}", CompanionMuted);
        ImGui.PopStyleVar();
    }

    private static void DrawFrontierHeading(string title, float textScale, string? detail = null, Vector4? detailColor = null)
    {
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var titleSize = ImGui.CalcTextSize(title) * textScale;
        ImGui.Dummy(titleSize);
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * textScale, position,
            ImGui.GetColorU32(ImGuiCol.Text), title);
        if (detail is null) return;

        var color = detailColor ?? CompanionMuted;
        if (titleSize.X + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(detail).X <= width)
            DrawGoalRightText(position + new Vector2(width, (titleSize.Y - ImGui.GetTextLineHeight()) / 2), detail, color);
        else
            DrawGoalText(detail, color);
    }

    private static void DrawFrontierPanel(string id, Vector2 padding, Action content)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, padding);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, FrontierPanelBorder);
        if (ImGui.BeginTable(id, 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(FrontierPanelSurface));
            content();
            ImGui.EndTable();
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private bool DrawFrontierFacility(bool factory, string description)
    {
        var scale = CompanionScale;
        var pad = 9 * scale;
        var cardWidth = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var width = Math.Max(1, cardWidth - pad * 2);
        var name = FrontierFacilityLabel(factory);
        const float headingScale = 1.12f;
        var titleHeight = ImGui.CalcTextSize(name, false, width / headingScale).Y * headingScale;
        var height = 2 * pad + titleHeight + 3 * scale + ImGui.CalcTextSize(description, false, width).Y;
        var clicked = ImGui.InvisibleButton(factory ? "FrontierFactory" : "FrontierTower", new Vector2(cardWidth, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var selected = frontierFactory == factory;
        var accent = factory ? WalletAccent(WalletCurrency.BattlePoints) : CompanionAccent;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(Vector4.Lerp(FrontierPanelSurface, accent, selected ? 0.12f : hovered ? 0.06f : 0)), 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? accent : FrontierPanelBorder), 4 * scale);
        var top = min + new Vector2(pad);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * headingScale, top, ImGui.GetColorU32(accent), name, width);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), top + new Vector2(0, titleHeight + 3 * scale),
            ImGui.GetColorU32(CompanionMuted), description, width);
        return clicked;
    }

    private void DrawFrontierSetup(TrainerSnapshotDto current)
    {
        var progress = current.Frontier;
        if (!progress.StandardUnlocked) frontierBracket = 0;
        if (!frontierTeamModeChosen) frontierMode = !progress.RentalUnlocked && progress.OwnedUnlocked ? 1 : 0;

        var scale = CompanionScale;
        var wide = ImGui.GetContentRegionAvail().X >= 540 * scale;
        string? rentalId = null;
        string[] ownedIds = [];
        var panelPadding = new Vector2(10) * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4 * scale, wide ? 0 : 4 * scale));
        if (ImGui.BeginTable("FrontierSetup", wide ? 2 : 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableNextColumn();
            DrawFrontierPanel("FrontierDifficulty", panelPadding, () => DrawFrontierDifficulty(progress.StandardUnlocked));
            ImGui.TableNextColumn();
            DrawFrontierPanel("FrontierTeamSetup", panelPadding, () =>
            {
                if (frontierFactory) DrawFactorySetup(progress.RentalUnlocked);
                else DrawFrontierTeamSetup(current, out rentalId, out ownedIds);
            });
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        DrawFrontierPanel("FrontierLaunch", new Vector2(10, 7) * scale, () => DrawFrontierLaunch(progress.RentalUnlocked, rentalId, ownedIds));
    }

    private void DrawFrontierLaunch(bool rentalsUnlocked, string? rentalId, string[] ownedIds)
    {
        var ready = frontierFactory ? rentalsUnlocked : frontierMode == 0 ? rentalId is not null : ownedIds.Length == 3;
        var bracket = frontierBracket == 0 ? "accessible" : "standard";
        var teamType = frontierMode == 0 ? "Borrow a team" : "Use my party";
        var summary = $"{FrontierFacilityLabel(frontierFactory)} · {FrontierBracketLabel(frontierBracket == 1)}";
        if (!frontierFactory) summary += $" · {teamType}";
        var button = frontierFactory ? "Begin rental draft" : "Start Tower run";
        var available = ImGui.GetContentRegionAvail().X;
        var buttonSize = new Vector2(GoalButtonWidth(button) + 8 * CompanionScale, Math.Max(32 * CompanionScale, ImGui.GetFrameHeight()));
        var textSize = ImGui.CalcTextSize(summary);
        if (textSize.X + ImGui.GetStyle().ItemSpacing.X + buttonSize.X <= available)
        {
            var start = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(textSize.X, buttonSize.Y));
            ImGui.GetWindowDrawList().AddText(start + new Vector2(0, (buttonSize.Y - textSize.Y) / 2), ImGui.GetColorU32(CompanionMuted), summary);
            ImGui.SameLine(0, available - textSize.X - buttonSize.X);
        }
        else
        {
            DrawGoalText(summary, CompanionMuted);
            buttonSize.X = Math.Max(1, available);
        }
        ImGui.BeginDisabled(!ready || CollectionLocked);
        if (DrawAccentButton(button, buttonSize, CompanionAccent))
        {
            if (frontierFactory) Run(() => BeginFactoryDraft(bracket), "Preparing your six Factory rentals...");
            else
            {
                var mode = frontierMode == 0 ? "rental" : "owned";
                Run(() => EnterFrontier(bracket, mode, rentalId ?? string.Empty, ownedIds), "Entering the Battle Tower...");
            }
        }
        ImGui.EndDisabled();
        if (CollectionEditRestriction is not null)
            DrawGoalText("Finish your current activity or leave matchmaking before starting a run.", PokemonOrange);
    }

    private void DrawFrontierDifficulty(bool standardUnlocked)
    {
        DrawFrontierHeading("Difficulty", 1.12f);
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (DrawAccentButton("Accessible", Vector2.Zero, frontierBracket == 0 ? CompanionAccent : CompanionMuted)) frontierBracket = 0;
        SameLineIfFits("Standard", right);
        ImGui.BeginDisabled(!standardUnlocked);
        if (DrawAccentButton("Standard", Vector2.Zero, frontierBracket == 1 ? CompanionAccent : CompanionMuted)) frontierBracket = 1;
        ImGui.EndDisabled();
        var description = frontierBracket == 0 ? "Forgiving opponents" : "Expert opponents";
        var payout = frontierPayouts?.GetValueOrDefault(frontierBracket == 0 ? "accessible" : "standard");
        DrawGoalText(payout is null ? description : $"{description} / {payout.Minimum}-{payout.Maximum} base BP per win", CompanionMuted);
        if (!standardUnlocked) DrawGoalText("Defeat a Champion to unlock Standard.", PokemonYellow);
        if (ImGui.TreeNode("Run rules"))
        {
            ImGui.TextWrapped("Level 50 singles. Each team brings three distinct species and held items cannot repeat. Restricted Legendaries and Mythicals are excluded.");
            ImGui.TextWrapped("Held items and Mega Evolution are allowed. Bag items are disabled. Your team heals between fights, and a loss or draw ends the streak.");
            ImGui.TextWrapped(payout is null ? "A boss every seventh fight awards bonus BP." : $"A boss every seventh fight awards +{payout.CaptainBonus} BP.");
            ImGui.TreePop();
        }
    }

    private static void DrawFactorySetup(bool rentalsUnlocked)
    {
        DrawFrontierHeading("Rental draft", 1.12f);
        DrawGoalText("Pick three from six rentals. Your first pick leads.", CompanionMuted);
        DrawGoalText("After a win, keep your team or swap one member for an opponent's rental.", CompanionMuted);
        if (!rentalsUnlocked) DrawGoalText("Defeat all five Rookie trainers to unlock Factory rentals.", PokemonYellow);
    }

    private void DrawFrontierTeamSetup(TrainerSnapshotDto current, out string? rentalId, out string[] ownedIds)
    {
        if (frontierRentals.Count > 0) frontierRentalIndex = Math.Clamp(frontierRentalIndex, 0, frontierRentals.Count - 1);
        int? selectedCount = null;
        if (frontierMode == 0 && current.Frontier.RentalUnlocked && frontierRentals.Count > 0)
            selectedCount = FrontierRentalTeam(frontierRentals[frontierRentalIndex]).Count;
        else if (frontierMode == 1 && current.Frontier.OwnedUnlocked)
            selectedCount = current.Party.Count(pokemon => pokemon.Level >= 50 && frontierTeamIds.Contains(pokemon.Id));
        DrawFrontierHeading("Team", 1.12f, selectedCount is { } count ? $"{count}/3 selected" : null);
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (DrawAccentButton("Borrow a team", Vector2.Zero, frontierMode == 0 ? CompanionAccent : CompanionMuted))
        { frontierMode = 0; frontierTeamModeChosen = true; }
        SameLineIfFits("Use my party", right);
        if (DrawAccentButton("Use my party", Vector2.Zero, frontierMode == 1 ? CompanionAccent : CompanionMuted))
        { frontierMode = 1; frontierTeamModeChosen = true; }
        var rental = frontierMode == 0;
        rentalId = null;
        ownedIds = [];
        if (rental && !current.Frontier.RentalUnlocked)
        {
            DrawGoalText("Defeat all five Rookie trainers to borrow a team.", PokemonYellow);
            return;
        }
        if (!rental && !current.Frontier.OwnedUnlocked)
        {
            DrawGoalText("Train three party Pokémon to level 50 to use your own team.", PokemonYellow);
            return;
        }
        if (!rental)
        {
            ownedIds = DrawFrontierParty(current.Party);
            return;
        }

        if (frontierRentals.Count == 0) { DrawGoalText("Loading rental teams...", CompanionMuted); return; }
        ImGui.TextUnformatted("Rental team");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##FrontierRentalTeam", frontierRentals[frontierRentalIndex].Name))
        {
            for (var index = 0; index < frontierRentals.Count; index++)
                if (ImGui.Selectable(frontierRentals[index].Name, frontierRentalIndex == index)) frontierRentalIndex = index;
            ImGui.EndCombo();
        }
        var selected = frontierRentals[frontierRentalIndex];
        var team = FrontierRentalTeam(selected);
        if (team.Count == 3) rentalId = selected.Id;
        DrawFrontierTeam(team);
        if (ImGui.TreeNode("Rental team notes"))
        {
            ImGui.TextWrapped(selected.Description);
            ImGui.TextWrapped("Borrowed Pokémon stay out of your party, PC and Pokédex.");
            ImGui.TreePop();
        }
    }

    private IReadOnlyList<PokemonDto> FrontierRentalTeam(FrontierRentalDto rental) =>
        frontierBracket == 0 ? rental.Team : rental.StandardTeam;

    private string[] DrawFrontierParty(List<PokemonDto> party)
    {
        var ordered = party.OrderBy(pokemon => pokemon.PartySlot).ToArray();
        frontierTeamIds.RemoveWhere(id => !ordered.Any(pokemon => pokemon.Id == id && pokemon.Level >= 50));
        DrawGoalText("Pick three. Party order sets your lead.", CompanionMuted);
        if (ordered.Length == 0) DrawGoalText("Add level-50 Pokémon to your party first.", PokemonYellow);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3) * CompanionScale);
        if (ImGui.BeginTable("FrontierParty", FactoryPresentation.Columns(ImGui.GetContentRegionAvail().X, CompanionScale), ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            var lead = ordered.FirstOrDefault(pokemon => frontierTeamIds.Contains(pokemon.Id));
            foreach (var pokemon in ordered)
            {
                ImGui.TableNextColumn();
                ImGui.PushID(pokemon.Id);
                var selected = frontierTeamIds.Contains(pokemon.Id);
                var caption = pokemon.Level < 50 ? $"Lv. {pokemon.Level} / Needs 50" : pokemon == lead ? "Lead" : $"Lv. {pokemon.Level}";
                if (DrawFrontierMember(pokemon, caption, true, selected, pokemon.Level < 50 || !selected && frontierTeamIds.Count >= 3))
                {
                    if (selected) frontierTeamIds.Remove(pokemon.Id);
                    else frontierTeamIds.Add(pokemon.Id);
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        return ordered.Where(pokemon => frontierTeamIds.Contains(pokemon.Id)).Select(pokemon => pokemon.Id).ToArray();
    }

    private void DrawFrontierTeam(IReadOnlyList<PokemonDto> team)
    {
        if (team.Count == 0) return;
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (88 * CompanionScale)), 1, 3);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3) * CompanionScale);
        if (ImGui.BeginTable("FrontierTeam", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            for (var index = 0; index < team.Count; index++)
            {
                ImGui.TableNextColumn();
                ImGui.PushID(index);
                DrawFrontierMember(team[index], index == 0 ? "Lead" : "Lv. 50", false, false);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private bool DrawFrontierMember(PokemonDto pokemon, string caption, bool selectable, bool selected, bool disabled = false)
    {
        var scale = CompanionScale;
        var pad = 7 * scale;
        var cardWidth = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var width = Math.Max(1, cardWidth - pad * 2);
        var art = Math.Min(width, 46 * scale);
        var name = string.IsNullOrWhiteSpace(pokemon.Nickname) ? pokemon.DisplaySpecies : pokemon.Nickname;
        var nameSize = ImGui.CalcTextSize(name, false, width);
        var captionSize = ImGui.CalcTextSize(caption, false, width);
        var height = art + nameSize.Y + captionSize.Y + 2 * pad + 6 * scale;
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(cardWidth, height);
        var clicked = false;
        if (selectable)
        {
            ImGui.BeginDisabled(disabled);
            clicked = ImGui.InvisibleButton("FrontierMember", size);
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.Dummy(size);
        }
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        if (hovered && selectable && !disabled) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (hovered)
        {
            var item = itemCatalog.FirstOrDefault(entry => entry.Id == pokemon.HeldItem)?.Name ?? pokemon.HeldItem;
            static string Stats(Dictionary<string, int> values) => string.Join("/", new[] { "hp", "atk", "def", "spa", "spd", "spe" }.Select(key => values.GetValueOrDefault(key)));
            ImGui.SetTooltip($"{pokemon.DisplaySpecies}\n{pokemon.Nature} / {pokemon.Ability}\nHeld item: {(item.Length == 0 ? "None" : item)}\n{string.Join(" / ", pokemon.Moves)}\nIVs (HP/Atk/Def/SpA/SpD/Spe): {Stats(pokemon.Ivs)}\nEVs: {Stats(pokemon.Evs)}");
        }
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(selected ? Vector4.Lerp(FrontierTileSurface, CompanionAccent, 0.16f) : FrontierTileSurface), 4 * scale);
        draw.AddRect(min, min + size, ImGui.GetColorU32(selected || hovered && selectable && !disabled ? CompanionAccent : FrontierPanelBorder), 4 * scale);
        DrawPokemonIconAt(pokemon.Appearance, pokemon.Shiny, min + new Vector2((cardWidth - art) / 2, pad), new Vector2(art));
        var nameY = pad + art + 3 * scale;
        var namePosition = min + new Vector2(Math.Max(pad, (cardWidth - nameSize.X) / 2), nameY);
        var captionPosition = min + new Vector2(Math.Max(pad, (cardWidth - captionSize.X) / 2), nameY + nameSize.Y + 3 * scale);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), namePosition, ImGui.GetColorU32(disabled ? CompanionMuted : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), name, width);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), captionPosition, ImGui.GetColorU32(selected ? PokemonGreen : CompanionMuted), caption, width);
        return clicked;
    }

    private static void DrawFrontierRunSummary(FrontierRunDto run, bool drafting)
    {
        var factory = run.Facility == "factory";
        var round = run.Streak + 1;
        var checkpoint = run.Status == "between" && !drafting;
        var boss = round % 7 == 0;
        DrawGoalText($"{FrontierFacilityLabel(factory)} / {FrontierBracketLabel(run.Bracket == "standard")}", CompanionAccent);
        var heading = drafting ? "Choose your three rentals" : checkpoint ? $"Next: Fight {round:N0}" : $"Fight {round:N0} saved";
        if (!drafting && boss) heading += " / Tower Captain";
        DrawGoalText(heading, !drafting && boss ? PokemonYellow : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        DrawGoalText(drafting ? "Saved draft / First pick leads" : $"{run.Streak:N0} wins{(checkpoint ? " / Team healed" : "")}", checkpoint ? PokemonGreen : CompanionMuted);
        ImGui.Spacing();
    }

    private void DrawSavedFrontierFight(FrontierRunDto run)
    {
        DrawFrontierTeam(run.Team.Select(entry => entry.Pokemon).ToArray());
        DrawFrontierRunActions("Resume fight", () => Run(async () => battle = await InSession(plugin.Api.GetBattle(run.BattleId)), "Restoring the Frontier fight..."));
    }

    private void DrawTowerCheckpoint(FrontierRunDto run)
    {
        DrawFrontierTeam(run.Team.Select(entry => entry.Pokemon).ToArray());
        DrawFrontierRunActions("Continue streak", () => Run(ContinueFrontier, "Starting the next Tower fight..."));
    }

    private void DrawFrontierRunActions(string primary, Action activate)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton(primary, new Vector2(0, 34 * CompanionScale), CompanionAccent)) activate();
        SameLineIfFits("End run", right);
        if (DrawAccentButton("End run", new Vector2(0, 34 * CompanionScale), PokemonOrange)) ImGui.OpenPopup("End Frontier run?");
        ImGui.EndDisabled();
    }
}
