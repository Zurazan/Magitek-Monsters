using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string resultObservedBattle = string.Empty;
    private string resultTrainerBattle = string.Empty;
    private string resultMoveId = string.Empty;
    private string resultForgetMove = string.Empty;
    private string resultEvolutionPokemon = string.Empty;
    private string resultEvolutionTarget = string.Empty;
    private long? resultMoveGeneration;
    private string? resultMoveOutcome;
    private bool requestResultEvolution;
    private float resultBodyContentHeight;
    private string eventBattleId = string.Empty;

    private static bool IsEventEncounter(EncounterDto encounter) => encounter.HabitatId.StartsWith("event:", StringComparison.Ordinal)
        || encounter.Pokemon.Source == "recurring-event";

    private bool IsEventBattle(BattleSnapshotDto current) => current.Id.Length > 0 && (eventBattleId == current.Id
        || current.Kind == "wild" && trainer?.ActiveEncounter is { } encounter && encounter.Id == current.Id && IsEventEncounter(encounter));

    private void AcknowledgeBattleResultTrainer()
    {
        if (battle is { Status: not "active" } current) resultTrainerBattle = current.Id;
    }

    private void DrawBattleResults(BattleSnapshotDto current)
    {
        if (resultObservedBattle != current.Id)
        {
            resultObservedBattle = current.Id;
            resultBodyContentHeight = 0;
            CloseBattleResultMoveReview();
            resultMoveOutcome = null;
            resultEvolutionPokemon = resultEvolutionTarget = string.Empty;
            if (resultTrainerBattle != current.Id) refreshState.Trainer();
        }
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var label = current.Kind == "wild" ? "WILD ENCOUNTER" : current.FrontierId is not null ? "BATTLE FRONTIER" : current.LeagueId is not null ? "LEAGUE RUN" : "TRAINER BATTLE";
        DrawClippedText($"{label} / RESULTS", width, CompanionAccent);
        ImGui.TextColored(current.Status is "won" or "caught" ? PokemonGreen : current.Status == "lost" ? PokemonRed : PokemonOrange, BattleResultPresentation.Outcome(current.Status));
        if (current.Result?.FirstClear == true) { ImGui.SameLine(); ImGui.TextColored(PokemonGreen, "First clear"); }
        DrawClippedText($"{current.OpponentName} / {current.Turn} turns", width, CompanionMuted);
        ImGui.Spacing();
        DrawBattleResultRewards(current);
        ImGui.Spacing();
        var rematch = RematchOpponent(current);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var footerInline = width >= (rematch is null ? 290 : 400) * scale + spacing * (rematch is null ? 1 : 2);
        var partyActionsInline = rematch is not null && width >= 210 * scale + spacing;
        var footerRows = footerInline ? 1 : rematch is null || partyActionsInline ? 2 : 3;
        var footerHeight = footerRows * (31 * scale + ImGui.GetStyle().ItemSpacing.Y) + ImGui.GetTextLineHeightWithSpacing() + 20 * scale;
        var maximumBodyHeight = Math.Max(1, ImGui.GetContentRegionAvail().Y - footerHeight);
        var bodyHeight = Math.Min(maximumBodyHeight, resultBodyContentHeight > 0 ? resultBodyContentHeight : maximumBodyHeight);
        var bodyVisible = ImGui.BeginChild("BattleResultBody", new Vector2(0, bodyHeight), false);
        if (current.Result is { } result)
        {
            var ids = result.Party.Select(entry => entry.PokemonId).ToHashSet();
            var resultTrainer = resultTrainerBattle == current.Id ? trainer : null;
            var pending = resultTrainer?.PendingMoves.Where(move => ids.Contains(move.PokemonId)).ToArray() ?? [];
            var milestones = result.Capture is not null || result.CaptureAvailable || result.Badges.Count > 0
                || result.Medals.Count > 0 || result.CollectionPoints > 0 || result.Party.Any(entry => entry.LearnedMoves.Count > 0)
                || resultTrainer?.EvolutionOptions.Any(option => option.Ready && ids.Contains(option.PokemonId)) == true;
            var split = width >= 900 * scale && milestones && ImGui.BeginTable("BattleResultColumns", 2, ImGuiTableFlags.SizingStretchProp);
            if (split)
            {
                ImGui.TableSetupColumn("Party", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Milestones", ImGuiTableColumnFlags.WidthFixed, 205 * scale);
                ImGui.TableNextColumn(); DrawBattleResultParty(current, result);
                ImGui.TableNextColumn(); DrawBattleResultMilestones(current, result, ids, pending.Length > 0);
                ImGui.EndTable();
            }
            else DrawBattleResultParty(current, result);

            if (resultTrainer is not null) DrawBattleResultMoves(current.Id, resultTrainer, pending);
            if (!split)
            {
                ImGui.Spacing(); DrawBattleResultMilestones(current, result, ids, pending.Length > 0);
            }
        }
        else
        {
            ImGui.TextWrapped(current.Status == "caught" ? "Your new Pokémon is saved to your collection." : "Your battle progress is saved.");
            ImGui.TextWrapped("Return to your party to review its current levels, moves and evolution options.");
        }
        if (bodyVisible) resultBodyContentHeight = Math.Max(1, ImGui.GetCursorPosY() + 4 * scale);
        ImGui.EndChild();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawBattleResultFooter(current, rematch, footerInline, partyActionsInline);
        DrawBattleResultEvolutionPopup();
        DrawBattleConsolePopups(current);
    }

    private void DrawBattleResultRewards(BattleSnapshotDto current)
    {
        var result = current.Result;
        var won = current.Status is "won" or "caught";
        var currency = result?.Currency ?? (current.CurrencyLost > 0 ? -current.CurrencyLost : won ? current.RewardCurrency : 0);
        var bp = result?.BattlePoints ?? (won ? current.RewardBP : 0);
        var experience = result?.Party.Select(BattleResultPresentation.ExperienceGained).DefaultIfEmpty(0).Max()
            ?? (won ? current.RewardExperience * (InventoryCount("expshare") > 0 ? 2 : 1) : 0);
        if (!ImGui.BeginTable("BattleResultRewards", 3, ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn(); DrawMetricCard("ResultMoney", currency < 0 ? "Defeat fee" : "Prize money", $"{(currency > 0 ? "+" : currency < 0 ? "−" : "")}¥ {Math.Abs(currency):N0}", currency < 0 ? PokemonRed : CompanionAccent);
        ImGui.TableNextColumn();
        if (current.FrontierId is not null) DrawMetricCard("ResultBP", "BP earned", $"+{bp:N0} BP", PokemonYellow);
        else DrawMetricCard("ResultEXP", "Party EXP", $"+{experience:N0}", ExpBarBlue);
        ImGui.TableNextColumn();
        if (result?.CollectionPoints > 0) DrawMetricCard("ResultMilestone", "CP earned", $"+{result.CollectionPoints:N0} CP", PokemonYellow);
        else if (result?.Badges.Count > 0) DrawMetricCard("ResultMilestone", "Badges", result.Badges.Count.ToString(), PokemonYellow);
        else if (current.FrontierId is not null) DrawMetricCard("ResultMilestone", "Streak", trainer?.FrontierRun?.Streak.ToString() ?? "Saved", PokemonGreen);
        else DrawMetricCard("ResultMilestone", current.Kind == "wild" ? "Collection" : "Challenge", current.Status == "caught" ? result?.Capture?.NewSpecies == true ? "New species" : "Caught" : result?.FirstClear == true ? "First clear" : current.Status == "won" ? "Cleared" : "No rewards", CompanionMuted);
        ImGui.EndTable();
    }

    private void DrawBattleResultParty(BattleSnapshotDto current, BattleResultDto result)
    {
        ImGui.TextColored(CompanionMuted, current.FrontierId is not null ? "YOUR BATTLE TEAM" : "YOUR PARTY");
        var scale = CompanionScale;
        var layout = BattleResultPresentation.PartyLayout(ImGui.GetContentRegionAvail().X, scale, ImGui.GetTextLineHeight());
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4 * scale));
        if (!ImGui.BeginTable("BattleResultPartyCards", layout.Columns, ImGuiTableFlags.SizingStretchSame)) { ImGui.PopStyleVar(); return; }
        foreach (var entry in result.Party)
        {
            ImGui.TableNextColumn();
            var pokemon = trainer?.Party.Concat(trainer.Storage).FirstOrDefault(pokemon => pokemon.Id == entry.PokemonId);
            var synchronized = resultTrainerBattle == current.Id;
            var vital = synchronized && current.FrontierId is null && current.LeagueId is null ? pokemon?.Vitals ?? entry.Vitals : entry.Vitals;
            var leveled = entry.LevelAfter > entry.LevelBefore;
            ImGui.PushID(entry.PokemonId);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6) * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 3) * scale);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, leveled ? new Vector4(0.13f, 0.17f, 0.23f, 1) : CompanionSurface);
            ImGui.BeginChild("ResultPokemon", new Vector2(0, layout.CardHeight), true, FixedBattlePanel);
            var origin = ImGui.GetCursorPos();
            DrawPokemonIcon(pokemon?.Appearance ?? entry.Species, entry.Shiny, new Vector2(40 * scale));
            var textOrigin = origin + new Vector2(48 * scale, 0);
            ImGui.SetCursorPos(textOrigin);
            var available = ImGui.GetContentRegionAvail().X;
            var level = BattleResultPresentation.Level(entry, current.FrontierId is not null);
            var levelWidth = ImGui.CalcTextSize(level).X;
            DrawClippedText((entry.Shiny ? "★ " : "") + (synchronized ? pokemon?.Nickname ?? entry.Name : entry.Name), Math.Max(1, available - levelWidth - 8 * scale), Vector4.One);
            ImGui.SameLine(0, 8 * scale); ImGui.TextColored(leveled ? CompanionAccent : CompanionMuted, level);
            ImGui.SetCursorPos(textOrigin + new Vector2(0, ImGui.GetTextLineHeight() + 4 * scale));
            DrawClippedText(current.FrontierId is null ? $"+{BattleResultPresentation.ExperienceGained(entry):N0} EXP earned" : entry.Rental ? "Rental Pokémon" : "Normal party unchanged", available, CompanionMuted);
            var meterWidth = Math.Max(1, ImGui.GetWindowContentRegionMax().X - origin.X);
            if (vital is not null)
            {
                DrawBattleResultMeter(origin + new Vector2(0, layout.HealthLabelY), origin.Y + layout.HealthBarY, meterWidth,
                    $"HP {vital.Hp:N0}/{vital.MaxHp:N0}{HealthStatus(vital)}", vital.Hp / (float)Math.Max(1, vital.MaxHp),
                    vital.Hp == 0 ? PokemonRed : CompanionMuted, HpBarRed, layout.BarHeight);
            }
            else
            {
                ImGui.SetCursorPos(origin + new Vector2(0, layout.HealthLabelY));
                DrawClippedText("HP unavailable", meterWidth, CompanionMuted);
            }
            if (current.FrontierId is null)
            {
                var (ratio, next) = BattleResultPresentation.Experience(entry);
                DrawBattleResultMeter(origin + new Vector2(0, layout.ExperienceLabelY), origin.Y + layout.ExperienceBarY,
                    meterWidth, $"EXP / {next}", ratio, CompanionAccent, ExpBarBlue, layout.BarHeight);
            }
            else
            {
                ImGui.SetCursorPos(origin + new Vector2(0, layout.ExperienceLabelY));
                DrawClippedText("EXP / Not earned in Frontier", meterWidth, CompanionMuted);
            }
            ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar(2); ImGui.PopID();
        }
        ImGui.EndTable(); ImGui.PopStyleVar();
        if (current.LeagueId is not null) ImGui.TextWrapped("EXP goes to your normal party. Your League team keeps its locked levels, moves and carried HP/PP until the run ends.");
        if (current.FrontierId is not null) ImGui.TextWrapped("Frontier battles use level 50. Your normal party’s levels and health are unchanged.");
    }

    private static void DrawBattleResultMeter(Vector2 labelPosition, float barY, float width, string label, float ratio, Vector4 textColor, Vector4 fillColor, float height)
    {
        ImGui.SetCursorPos(labelPosition);
        DrawClippedText(label, width, textColor);
        ImGui.SetCursorPos(new Vector2(labelPosition.X, barY));
        DrawProgressBar(ratio, new Vector2(width, height), "", fillColor);
    }

    private void DrawBattleResultMilestones(BattleSnapshotDto current, BattleResultDto result, HashSet<string> ids, bool hasPendingMoves)
    {
        var synchronized = resultTrainerBattle == current.Id;
        if (result.Capture is { } capture)
        {
            ImGui.TextColored(capture.NewSpecies ? PokemonGreen : CompanionAccent, capture.NewSpecies ? "NEW POKÉDEX ENTRY" : "POKÉMON CAUGHT");
            DrawPokemonIcon(capture.Species, capture.Shiny, new Vector2(58 * CompanionScale));
            ImGui.TextWrapped($"{(capture.Shiny ? "★ " : "")}{capture.Name} / Lv. {capture.Level}");
            ImGui.TextWrapped(capture.InParty ? "Added to your party." : "Added to PC Storage.");
            ImGui.BeginDisabled(busy || !synchronized);
            if (DrawAccentButton("Pokémon summary", new Vector2(-1, 29 * CompanionScale), CompanionAccent))
                OpenPokemonSummary(capture.PokemonId);
            ImGui.EndDisabled(); ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        foreach (var badge in result.Badges) { ImGui.TextColored(PokemonYellow, "BADGE EARNED"); ImGui.TextWrapped(badge); ImGui.Spacing(); }
        foreach (var medal in result.Medals) { ImGui.TextColored(PokemonYellow, "MEDAL EARNED"); ImGui.TextWrapped(medal.Replace('-', ' ').Replace(':', ' ')); ImGui.Spacing(); }
        if (result.CollectionPoints > 0)
        {
            ImGui.TextColored(PokemonYellow, $"+{result.CollectionPoints:N0} COLLECTION POINTS");
            if (DrawAccentButton("Collection goals", new Vector2(-1, 29 * CompanionScale), PokemonYellow)) mainSection = 7;
            ImGui.Spacing();
        }
        if (result.CaptureAvailable)
        {
            ImGui.TextColored(PokemonGreen, "CAPTURE OPPORTUNITY");
            ImGui.TextWrapped("The boss is defeated. Catch it in the follow-up wild battle.");
            ImGui.BeginDisabled(busy || !synchronized || trainer?.ActiveEncounter is null);
            if (DrawAccentButton("Start capture battle", new Vector2(-1, 30 * CompanionScale), PokemonGreen) && trainer?.ActiveEncounter is { } encounter)
                Run(async () => { await InSession(ResumeWildBattle(encounter.Id, IsEventBattle(current))); mainSection = 0; }, "Opening your capture opportunity...");
            ImGui.EndDisabled(); ImGui.Spacing();
        }
        if (!synchronized || trainer is not { } resultTrainer)
        {
            ImGui.TextColored(CompanionMuted, "Updating your party...");
            return;
        }
        var ready = resultTrainer.EvolutionOptions.Where(option => ids.Contains(option.PokemonId) && option.Ready).GroupBy(option => option.PokemonId).ToList();
        foreach (var group in ready)
        {
            var pokemon = resultTrainer.Party.Concat(resultTrainer.Storage).FirstOrDefault(pokemon => pokemon.Id == group.Key);
            if (pokemon is null) continue;
            ImGui.PushID($"ResultEvolution{group.Key}");
            ImGui.TextColored(PokemonYellow, "EVOLUTION AVAILABLE");
            ImGui.TextWrapped(group.Count() == 1 ? $"{pokemon.Nickname} → {EvolutionLabel(group.First().TargetSpecies)}" : $"{pokemon.Nickname} / {group.Count()} routes");
            ImGui.BeginDisabled(CollectionLocked);
            if (DrawAccentButton($"Evolve {pokemon.Nickname}", new Vector2(-1, 29 * CompanionScale), PokemonYellow))
            { resultEvolutionPokemon = pokemon.Id; resultEvolutionTarget = group.First().TargetSpecies; requestResultEvolution = true; }
            ImGui.EndDisabled(); ImGui.Spacing(); ImGui.PopID();
        }
        foreach (var entry in result.Party.Where(entry => entry.LearnedMoves.Count > 0))
        {
            ImGui.TextColored(PokemonGreen, "MOVE LEARNED");
            ImGui.TextWrapped($"{entry.Name}: {string.Join(", ", entry.LearnedMoves)}");
            ImGui.Spacing();
        }
        if (ready.Count > 0 && CollectionEditRestriction is { } restriction) ImGui.TextWrapped(restriction);
        else if (ready.Count > 0) ImGui.TextWrapped("You can also evolve later from your Pokémon’s menu.");
        else if (!hasPendingMoves && resultMoveOutcome is null && result.Capture is null && !result.CaptureAvailable && result.Badges.Count == 0 && result.Medals.Count == 0)
            ImGui.TextWrapped(current.Status == "lost" ? "Your progress is saved. Rest your party before trying again." : "Party progress saved. Ready for your next challenge.");
    }

    private void DrawBattleResultFooter(BattleSnapshotDto current, OpponentDto? rematch, bool footerInline, bool partyActionsInline)
    {
        var label = IsEventBattle(current) ? "Return to Events" : current.Kind == "wild" ? "Continue adventuring" : current.FrontierId is not null ? "Return to Frontier" : current.LeagueId is not null ? "Return to League" : "Return to challengers";
        var scale = CompanionScale;
        ImGui.BeginDisabled(busy);
        var width = ImGui.GetContentRegionAvail().X;
        if (DrawAccentButton(label, new Vector2(Math.Min(width, 190 * scale), 31 * scale), CompanionAccent)) Run(ReturnFromBattle, "Returning from battle...");
        ImGui.EndDisabled();
        if (footerInline) ImGui.SameLine();
        if (rematch is not null)
        {
            var restriction = TrainerRematchRestriction(current);
            ImGui.BeginDisabled(restriction is not null);
            if (DrawAccentButton("Rematch", new Vector2(Math.Min(width, 110 * scale), 31 * scale), PokemonGreen))
                Run(() => RematchTrainer(current, rematch), $"Challenging {rematch.Name} again...");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(restriction ?? "Fight this trainer again with the same Pokémon and challenge rules.");
            if (partyActionsInline) ImGui.SameLine();
        }
        ImGui.BeginDisabled(resultTrainerBattle != current.Id || CollectionLocked || trainer?.Party.Any(NeedsRest) != true);
        if (DrawAccentButton("Heal party", new Vector2(Math.Min(width, 100 * scale), 31 * scale), CenterAccent)) Run(HealPartyAtCenter, "Healing your party at the Pokémon Center...");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Pokémon Center: free healing, revival and PP recovery outside battles, capture opportunities, runs and matchmaking.");
        DrawClippedText(current.FrontierId is not null ? "Frontier health is separate from your party." : current.LeagueId is not null && LeagueBusy ? "League HP, PP and status carry over." : "Heal here or visit the Pokémon Center in Party. Healing is free.", ImGui.GetContentRegionAvail().X, CompanionMuted);
    }

    private async Task ReturnFromBattle()
    {
        var current = battle;
        var returnToEvents = current is not null && IsEventBattle(current);
        await InSession(RefreshTrainer());
        if (current is not null && !RegionalLeaguePresentation.ResultReady(current, trainer?.LeagueRun))
        {
            refreshState.Trainer();
            SetStatus("Your League checkpoint is still updating. Try Return to League again in a moment.");
            return;
        }
        battle = null;
        eventBattleId = string.Empty;
        if (returnToEvents) mainSection = 10;
        else if (current?.FrontierId is not null) OpenFrontier();
    }

    private void CloseBattleResultMoveReview()
    {
        resultMoveId = resultForgetMove = string.Empty;
        resultMoveGeneration = null;
    }

    private void DrawBattleResultMoves(string battleId, TrainerSnapshotDto resultTrainer, PendingMoveDto[] pending)
    {
        if (!pending.Any(move => move.Id == resultMoveId)) CloseBattleResultMoveReview();
        if (resultMoveOutcome is { } outcome) DrawGoalText(outcome, PokemonGreen);
        foreach (var learning in pending)
        {
            var pokemon = resultTrainer.Party.Concat(resultTrainer.Storage).FirstOrDefault(entry => entry.Id == learning.PokemonId);
            if (pokemon is null) continue;
            ImGui.Spacing();
            DrawBattleResultMoveReview(battleId, learning, pokemon);
        }
    }

    private void DrawBattleResultMoveReview(string battleId, PendingMoveDto learning, PokemonDto pokemon)
    {
        var scale = CompanionScale;
        ImGui.PushID(learning.Id);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4) * scale);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, resultMoveId == learning.Id ? CompanionAccent with { W = .65f } : ImGui.GetStyle().Colors[(int)ImGuiCol.Border]);
        if (ImGui.BeginTable("ResultMoveReview", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            ImGui.TextColored(CompanionAccent, "NEW MOVE AVAILABLE");
            ImGui.TextWrapped($"{pokemon.Nickname} can learn {learning.Move}");
            if (resultMoveId != learning.Id)
            {
                var space = right - ImGui.GetItemRectMax().X - GoalButtonWidth("Review move");
                if (space >= ImGui.GetStyle().ItemSpacing.X) ImGui.SameLine(0, space);
                if (DrawAccentButton("Review move", Vector2.Zero, CompanionAccent))
                {
                    CloseBattleResultMoveReview();
                    resultMoveId = learning.Id;
                    ImGui.SetScrollHereY(0f);
                }
            }
            if (resultMoveId == learning.Id) DrawBattleResultMoveEditor(battleId, learning, pokemon);
            ImGui.EndTable();
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        ImGui.PopID();
    }

    private void DrawBattleResultMoveEditor(string battleId, PendingMoveDto learning, PokemonDto pokemon)
    {
        var scale = CompanionScale;
        var restriction = CollectionEditRestriction;
        var locked = busy || restriction is not null;
        var replacement = pokemon.Moves.Contains(resultForgetMove) ? resultForgetMove : null;

        ImGui.Separator();
        var split = ImGui.GetContentRegionAvail().X >= 520 * scale &&
            ImGui.BeginTable("ResultMoveDetails", 2, ImGuiTableFlags.SizingStretchSame);
        if (split) ImGui.TableNextColumn();
        ImGui.TextColored(CompanionMuted, $"Unlocked at Lv. {learning.Level}");
        DrawLearningMoveDetails(learning.Move);
        if (split) ImGui.TableNextColumn();
        else ImGui.Spacing();
        ImGui.TextUnformatted("Choose a move to replace");
        ImGui.BeginDisabled(locked);
        replacement = DrawBattleResultMoveChoices(pokemon, replacement);
        ImGui.EndDisabled();
        if (split) ImGui.EndTable();

        var feedback = restriction ?? (resultMoveGeneration == foregroundGeneration ? status : null);
        if (feedback is not null) DrawGoalText(feedback, restriction is not null || statusIsError ? PokemonRed : CompanionMuted);
        ImGui.Separator();
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var actionsWidth = GoalButtonWidth("Skip this move") + GoalButtonWidth("Decide later") + GoalButtonWidth("Learn move") + 2 * gap;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (replacement is not null)
        {
            ImGui.AlignTextToFramePadding();
            DrawGoalText($"{replacement} → {learning.Move}", CompanionAccent);
            var space = right - ImGui.GetItemRectMax().X - actionsWidth;
            if (space >= gap) ImGui.SameLine(0, space);
        }
        var extraWidth = ImGui.GetContentRegionAvail().X - actionsWidth;
        if (extraWidth > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + extraWidth);

        ImGui.BeginDisabled(locked);
        if (DrawAccentButton("Skip this move", Vector2.Zero, CompanionMuted))
            SubmitBattleResultMove(battleId, learning, pokemon.Nickname, null);
        ImGui.EndDisabled();
        SameLineIfFits("Decide later", right);
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton("Decide later", Vector2.Zero, CompanionMuted)) CloseBattleResultMoveReview();
        ImGui.EndDisabled();
        SameLineIfFits("Learn move", right);
        ImGui.BeginDisabled(locked || replacement is null);
        if (DrawAccentButton("Learn move", Vector2.Zero, PokemonGreen) && replacement is not null)
            SubmitBattleResultMove(battleId, learning, pokemon.Nickname, replacement);
        ImGui.EndDisabled();
    }

    private string? DrawBattleResultMoveChoices(PokemonDto pokemon, string? replacement)
    {
        var scale = CompanionScale;
        var columns = pokemon.Moves.Count > 1 &&
            ImGui.GetContentRegionAvail().X >= 2 * Math.Max(118 * scale, pokemon.Moves.Max(move => ImGui.CalcTextSize(move).X) + 25 * scale) ? 2 : 1;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 2) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, .5f));
        if (ImGui.BeginTable("ResultMoveChoices", columns, ImGuiTableFlags.SizingStretchSame))
        {
            var height = 2 * ImGui.GetTextLineHeight() + 8 * scale;
            foreach (var move in pokemon.Moves)
            {
                ImGui.TableNextColumn();
                var label = move;
                if (battleMoveHelp.TryGetValue(move, out var details))
                    label += $"\n{details.Type} / {(details.Power > 0 ? $"Power {details.Power}" : details.Category)}";
                if (DrawAccentButton(label, new Vector2(-1, height), replacement == move ? CompanionAccent : CompanionMuted))
                    resultForgetMove = replacement = move;
                DrawBattleMoveTooltip(move);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar(2);
        return replacement;
    }

    private void SubmitBattleResultMove(string battleId, PendingMoveDto learning, string pokemonName, string? replacement)
    {
        resultMoveGeneration = foregroundGeneration + 1;
        Run(async () =>
        {
            await ResolveMoveLearning(learning.Id, replacement);
            if (resultObservedBattle == battleId)
                resultMoveOutcome = replacement is null ? $"{pokemonName} kept its current moves." : $"{pokemonName} learned {learning.Move}.";
        }, replacement is null ? $"Skipping {learning.Move}..." : $"Teaching {learning.Move}...");
    }

    private void DrawBattleResultEvolutionPopup()
    {
        if (requestResultEvolution) { ImGui.OpenPopup("Evolve battle partner"); requestResultEvolution = false; }
        if (!BeginConfirmationModal("Evolve battle partner")) return;
        var pokemon = trainer?.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == resultEvolutionPokemon);
        if (pokemon is null) { ImGui.CloseCurrentPopup(); EndConfirmationModal(); return; }
        var routes = GetEvolutionOptions(pokemon.Id).Where(option => option.Ready).ToArray();
        var route = routes.FirstOrDefault(option => option.TargetSpecies == resultEvolutionTarget) ?? routes.FirstOrDefault();
        ImGui.TextColored(PokemonYellow, $"Evolve {pokemon.Nickname}?");
        ImGui.TextWrapped("Evolution is optional. You can keep your partner as it is and decide later.");
        if (route is not null)
        {
            if (routes.Length > 1 && ImGui.BeginCombo("Evolution", EvolutionLabel(route.TargetSpecies)))
            {
                foreach (var option in routes) if (ImGui.Selectable(EvolutionLabel(option.TargetSpecies), option == route)) resultEvolutionTarget = option.TargetSpecies;
                ImGui.EndCombo();
            }
            if (ImGui.BeginTable("ResultEvolutionSprites", 2, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn(); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(90 * CompanionScale)); ImGui.TextUnformatted(pokemon.DisplaySpecies);
                ImGui.TableNextColumn(); DrawPokemonIcon(route.TargetAppearance.Length > 0 ? route.TargetAppearance : route.TargetSpecies, pokemon.Shiny, new Vector2(90 * CompanionScale)); ImGui.TextUnformatted(EvolutionLabel(route.TargetSpecies));
                ImGui.EndTable();
            }
            ImGui.TextWrapped(route.Requirement);
        }
        else ImGui.TextWrapped("This evolution is no longer available. Check its requirements in your Pokémon’s menu.");
        if (DrawConfirmationActions("Evolve", PokemonYellow, CollectionLocked || route is null, "Not now") && route is not null)
        { var target = route.TargetSpecies; ImGui.CloseCurrentPopup(); Run(() => EvolvePokemon(pokemon.Id, target), $"Evolving {pokemon.Nickname}..."); }
        EndConfirmationModal();
    }
}
