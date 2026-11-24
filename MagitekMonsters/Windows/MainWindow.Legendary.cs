using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<LegendaryBossDto> legendaryBosses = [];
    private List<string> legendaryRequiredIds = [];
    private readonly SanctuaryCatalog sanctuaryCatalog = new();
    private string legendarySearch = "", selectedSanctuary = "";
    private int legendaryPage, legendaryAvailability, legendaryCategory;
    private bool sanctuaryPreparation;
    private LegendaryBossDto? pendingLegendaryPermit;
    private readonly ConcurrentDictionary<string, string> legendaryRequests = new();
    private static string SanctuarySpecies(string species) => SpeciesDisplayCatalog.Name(species);

    private void ResetSanctuaries()
    {
        sanctuaryCatalog.Clear(); legendarySearch = selectedSanctuary = "";
        legendaryPage = legendaryAvailability = legendaryCategory = 0; sanctuaryPreparation = false;
        pendingLegendaryPermit = null; legendaryRequests.Clear();
    }

    private void DrawLegendary()
    {
        if (trainer is null) return;
        if (battle?.Kind == "trainer" && battle.OpponentId.StartsWith("legendary-", StringComparison.Ordinal))
        { sanctuaryPreparation = false; DrawBattleConsole(battle); return; }
        var entries = sanctuaryCatalog.Read(trainer, legendaryBosses);
        DrawSanctuaryHeading(entries); DrawSanctuaryCapture();
        if (entries.Count == 0) { ImGui.TextDisabled("Sanctuary catalog is unavailable."); return; }
        var selected = entries.FirstOrDefault(entry => entry.Boss.Id == selectedSanctuary);
        if (sanctuaryPreparation && selected is not null)
        { DrawSanctuaryPreparation(selected); DrawLegendaryPermitPopup(); return; }
        sanctuaryPreparation = false;
        var mew = entries.FirstOrDefault(entry => entry.Boss.Final);
        if (mew is not null) DrawMewSanctuaryLink(mew);
        DrawSanctuaryFilters();
        var visible = sanctuaryCatalog.Filter(legendarySearch, legendaryAvailability, legendaryCategory);
        selected = entries.FirstOrDefault(entry => entry.Boss.Id == selectedSanctuary);
        if (selected is null || !selected.Boss.Final && !visible.Contains(selected)) selectedSanctuary = visible.FirstOrDefault()?.Boss.Id ?? "";
        var scale = CompanionScale; var width = ImGui.GetContentRegionAvail().X;
        if (width >= 650 * scale && ImGui.BeginTable("SanctuaryBrowser", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Catalogue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Boss dossier", ImGuiTableColumnFlags.WidthFixed, Math.Max(285 * scale, width * .45f));
            ImGui.TableNextColumn(); DrawSanctuaryGrid(visible);
            ImGui.TableNextColumn(); DrawSanctuaryDetails(entries.FirstOrDefault(entry => entry.Boss.Id == selectedSanctuary));
            ImGui.EndTable();
        }
        else
        {
            DrawSanctuaryGrid(visible); ImGui.Spacing();
            DrawSanctuaryDetails(entries.FirstOrDefault(entry => entry.Boss.Id == selectedSanctuary));
        }
        DrawLegendaryPermitPopup();
    }

    private void DrawSanctuaryHeading(IReadOnlyList<SanctuaryEntry> entries)
    {
        if (ImGui.BeginTable("SanctuaryHeading", 2, ImGuiTableFlags.SizingStretchProp))
        {
            var label = $"{trainer!.PvpProfile.RankTokens?.Balance ?? 0:N0} RT";
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Rank Tokens", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(label).X + 8 * CompanionScale);
            ImGui.TableNextColumn(); ImGui.TextColored(CompanionAccent, "LEGENDARY SANCTUARIES");
            ImGui.TableNextColumn(); ImGui.TextColored(PokemonYellow, label); ImGui.EndTable();
        }
        ImGui.TextWrapped("Solo bosses / Bring up to six Pokémon / A fresh victory before every capture");
        SanctuaryText($"{entries.Count(entry => !entry.Boss.Final && entry.Owned)} permanent permits / {entries.Count(entry => entry.Cleared)} bosses defeated / {entries.Count(entry => entry.Caught)} caught", CompanionMuted);
        ImGui.Spacing();
    }

    private void DrawMewSanctuaryLink(SanctuaryEntry mew)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight();
        var clicked = DrawPveCardSurface("MewSanctuaryLink", Math.Max(58 * scale, 2 * line + 20 * scale), selectedSanctuary == mew.Boss.Id,
            out var min, out var max, out var after);
        var hovered = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(min + new Vector2(10, 8) * scale); DrawPokemonIcon(mew.Boss.Species, false, new Vector2(34 * scale));
        var width = Math.Max(1, max.X - min.X - 66 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(54, 8) * scale);
        DrawClippedText("Mew / Final Sanctuary", width, CompanionAccent);
        ImGui.SetCursorScreenPos(min + new Vector2(54 * scale, 12 * scale + line));
        DrawClippedText(mew.Owned ? "All current requirements complete / No permit fee" : "Complete a League, every required boss and Special Trial", width, mew.Owned ? PokemonGreen : CompanionMuted);
        if (hovered) ImGui.SetTooltip("Inspect Mew's current completion requirements. New required bosses can lock this Sanctuary again.");
        if (clicked) { selectedSanctuary = mew.Boss.Id; sanctuaryPreparation = false; }
        ImGui.SetCursorScreenPos(after);
    }

    private void DrawSanctuaryCapture()
    {
        if (trainer?.ActiveEncounter is not { Status: "active" } encounter || !encounter.HabitatId.StartsWith("legendary-", StringComparison.Ordinal)) return;
        ImGui.TextColored(PokemonGreen, "CAPTURE READY");
        ImGui.TextWrapped($"{SanctuarySpecies(encounter.Pokemon.Species)} / Lv. {encounter.Pokemon.Level}");
        SanctuaryText(DateTimeOffset.TryParse(encounter.ExpiresAt, out var expires) ? $"Expires {expires.ToLocalTime():g}" : $"Expires: {encounter.ExpiresAt}", CompanionMuted);
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton("Open capture battle##SanctuaryBanner", new Vector2(Math.Min(220 * CompanionScale, ImGui.GetContentRegionAvail().X), 32 * CompanionScale), PokemonGreen)) OpenSanctuaryCapture(encounter.Id);
        ImGui.EndDisabled(); ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
    }

    private void OpenSanctuaryCapture(string id) => Run(async () =>
        { await InSession(ResumeWildBattle(id)); mainSection = 0; }, "Opening your earned capture encounter...");

    private void DrawSanctuaryFilters()
    {
        var wide = ImGui.GetContentRegionAvail().X >= 520 * CompanionScale;
        var changed = false;
        if (!wide)
        {
            ImGui.TextDisabled("Search"); ImGui.SetNextItemWidth(-1);
            changed |= ImGui.InputTextWithHint("##SanctuarySearch", "Find a Legendary or Mythical...", ref legendarySearch, 80);
        }
        if (ImGui.BeginTable("SanctuaryFilters", wide ? 3 : 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (wide)
            {
                ImGui.TableSetupColumn("Search", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Availability", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Pokémon", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn(); ImGui.TextDisabled("Search"); ImGui.SetNextItemWidth(-1);
                changed |= ImGui.InputTextWithHint("##SanctuarySearch", "Find a Legendary or Mythical...", ref legendarySearch, 80);
            }
            ImGui.TableNextColumn(); ImGui.TextDisabled("Availability"); ImGui.SetNextItemWidth(-1);
            changed |= ImGui.Combo("##SanctuaryAvailability", ref legendaryAvailability, SanctuaryCatalog.Availability, SanctuaryCatalog.Availability.Length);
            ImGui.TableNextColumn(); ImGui.TextDisabled("Pokémon"); ImGui.SetNextItemWidth(-1);
            changed |= ImGui.Combo("##SanctuaryCategory", ref legendaryCategory, SanctuaryCatalog.Categories, SanctuaryCatalog.Categories.Length);
            ImGui.EndTable();
        }
        if (changed) legendaryPage = 0;
        ImGui.Spacing();
    }

    private void DrawSanctuaryGrid(IReadOnlyList<SanctuaryEntry> entries)
    {
        const int pageSize = 6;
        legendaryPage = Math.Clamp(legendaryPage, 0, Math.Max(0, (entries.Count - 1) / pageSize));
        var columns = ImGui.GetContentRegionAvail().X >= 285 * CompanionScale ? 2 : 1;
        if (entries.Count == 0) ImGui.TextWrapped("No Sanctuaries match your search.");
        else if (ImGui.BeginTable("SanctuaryGrid", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var entry in entries.Skip(legendaryPage * pageSize).Take(pageSize))
            { ImGui.TableNextColumn(); DrawSanctuaryTile(entry); }
            ImGui.EndTable();
        }
        DrawPager("SanctuaryPages", ref legendaryPage, entries.Count, pageSize);
        ImGui.TextDisabled($"{entries.Count} Sanctuaries");
    }

    private void DrawSanctuaryTile(SanctuaryEntry entry)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight(); var boss = entry.Boss;
        var clicked = DrawPveCardSurface("Sanctuary##" + boss.Id, Math.Max(110 * scale, 4 * line + 40 * scale), selectedSanctuary == boss.Id, out var min, out var max, out var after);
        var hovered = ImGui.IsItemHovered(); var width = max.X - min.X;
        var pending = trainer?.ActiveEncounter is { Status: "active" } encounter && encounter.HabitatId == boss.Id;
        var status = pending ? "Capture ready" : entry.Owned ? "Unlocked" : boss.Final ? "Completion required" : $"{boss.Price:N0} RT";
        ImGui.GetWindowDrawList().PushClipRect(min + new Vector2(4 * scale, 0), max - new Vector2(4 * scale, 0), true);
        ImGui.SetCursorScreenPos(min + new Vector2(8, 7) * scale); DrawPokemonIcon(boss.Species, false, new Vector2(40 * scale));
        ImGui.SetCursorScreenPos(min + new Vector2(54, 8) * scale);
        DrawClippedText(entry.Name, Math.Max(1, width - 62 * scale), ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SetCursorScreenPos(min + new Vector2(54 * scale, 12 * scale + line));
        DrawClippedText(status, Math.Max(1, width - 62 * scale), pending || entry.Owned ? PokemonGreen : PokemonYellow);
        ImGui.SetCursorScreenPos(new Vector2(min.X + 9 * scale, max.Y - 21 * scale - 2 * line));
        DrawSanctuaryTypes(entry.Types, Math.Max(1, width - 18 * scale));
        ImGui.SetCursorScreenPos(new Vector2(min.X + 9 * scale, max.Y - 9 * scale - line));
        DrawClippedText(entry.Caught ? "Caught" : "Not caught", Math.Max(1, width - 18 * scale), entry.Caught ? CompanionAccent : CompanionMuted);
        ImGui.GetWindowDrawList().PopClipRect();
        if (hovered) ImGui.SetTooltip($"{entry.Name}\n{string.Join(" / ", entry.Types)}\n{status}\n{(entry.Cleared ? "Boss defeated before" : "Boss not defeated")}\n{(entry.Caught ? "Registered in your Pokédex" : "Not caught yet")}");
        if (clicked) selectedSanctuary = boss.Id;
        ImGui.SetCursorScreenPos(after);
    }

    private void DrawSanctuaryTypes(string[] types, float width)
    {
        var scale = CompanionScale;
        DrawStarterTypeBadges(types, Math.Max(1, Math.Min(64 * scale, (width - Math.Max(0, types.Length - 1) * 5 * scale) / Math.Max(1, types.Length))), scale);
    }

    private void DrawSanctuaryDetails(SanctuaryEntry? entry)
    {
        if (entry is null) return;
        var scale = CompanionScale; var boss = entry.Boss;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X); var content = Math.Max(1, width - 20 * scale);
        var min = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup(); ImGui.Dummy(new Vector2(1, 10 * scale)); ImGui.Indent(10 * scale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);
        if (ImGui.BeginTable("SanctuaryIdentity", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 78 * scale);
            ImGui.TableNextColumn(); ImGui.TextDisabled(boss.Final ? "FINAL SANCTUARY" : entry.Category.ToUpperInvariant());
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - 4 * scale));
            SanctuaryText(entry.Name, CompanionAccent); ImGui.PopTextWrapPos();
            DrawSanctuaryTypes(entry.Types, Math.Max(1, ImGui.GetContentRegionAvail().X - 4 * scale));
            ImGui.TableNextColumn(); DrawPokemonIcon(boss.Species, false, new Vector2(74 * scale)); ImGui.EndTable();
        }
        SanctuaryText(boss.Final ? entry.Owned ? "Requirements cleared / No permit fee" : "Completion required / No permit fee"
            : entry.Owned ? "Permanent permit owned" : $"{boss.Price:N0} RT / Permanent permit", entry.Owned ? PokemonGreen : PokemonYellow);
        SanctuaryText($"{(entry.Cleared ? "Defeated before" : "Not defeated")} / {(entry.Caught ? "Caught" : "Not caught")}", entry.Caught ? CompanionAccent : CompanionMuted);
        if (boss.Final) DrawMewRequirements();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        SanctuaryText(boss.Raid is { } raid ? $"Boss Lv. 100 / {raid.HpMultiplier}x HP\nEnrage T{raid.EnrageTurn} / Limit {raid.TurnLimit} turns" : "Boss Lv. 100", CompanionMuted);
        ImGui.Spacing(); ImGui.TextColored(CompanionMuted, "BOSS MOVES");
        if (ImGui.BeginTable("SanctuaryMoves", content >= 240 * scale ? 2 : 1, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var move in boss.Moves)
            {
                ImGui.TableNextColumn();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - 4 * scale));
                ImGui.TextWrapped(move); ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Phases & counterplay"))
        {
            ImGui.TextWrapped(boss.TacticalHint);
            SanctuaryText("65% HP: bonus action on even turns. 30% HP: bonus action every turn. Each threshold cleanses status, confusion and negative stages once. Healing never resets phases.", CompanionMuted);
            SanctuaryText("Minimum HP baseline 350. Offense has a floor; defenses stay normal. Indirect damage and healing use unscaled HP. Boss PP replenishes each turn. Enrage adds 0.10x damage each turn until the time limit.", CompanionMuted);
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        SanctuaryText($"Capture Lv. {boss.CaptureLevel} / Normal stats", PokemonGreen);
        SanctuaryText("A fresh boss victory is required before every capture.", CompanionMuted);
        ImGui.Spacing(); DrawSanctuaryAction(entry, content);
        ImGui.PopTextWrapPos(); ImGui.Unindent(10 * scale); ImGui.Dummy(new Vector2(1, 10 * scale)); ImGui.EndGroup();
        ImGui.GetWindowDrawList().AddRect(min, new Vector2(min.X + width, ImGui.GetItemRectMax().Y), ImGui.GetColorU32(CompanionMuted with { W = .3f }), 3 * scale);
    }

    private static void SanctuaryText(string text, Vector4 color)
    { ImGui.PushStyleColor(ImGuiCol.Text, color); ImGui.TextWrapped(text); ImGui.PopStyleColor(); }

    private void DrawMewRequirements()
    {
        if (trainer is null) return;
        var missing = trainer.Legendary.MissingBossIds.ToHashSet(StringComparer.Ordinal);
        ImGui.Spacing();
        DrawMewRequirement("Any regional League", RegionalLeagueProgress.HasChampion(trainer.PveProgress.DefeatedOpponentIds), "A Champion victory in one region is enough.");
        var required = legendaryBosses.Where(boss => !boss.Final && legendaryRequiredIds.Contains(boss.Id)).ToArray();
        var remaining = required.Where(boss => missing.Contains(boss.Id)).ToArray();
        ImGui.Separator(); SanctuaryText($"Required Sanctuary bosses / {required.Length - remaining.Length}/{required.Length}", remaining.Length == 0 ? PokemonGreen : PokemonYellow);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, ExpBarBlue);
        ImGui.ProgressBar(required.Length == 0 ? 0 : (float)(required.Length - remaining.Length) / required.Length, new Vector2(-1, 4 * CompanionScale), "");
        ImGui.PopStyleColor();
        SanctuaryText(remaining.Length == 0 ? "Every required Sanctuary boss is defeated."
            : string.Join(", ", remaining.Take(3).Select(boss => SanctuarySpecies(boss.Species))) + (remaining.Length > 3 ? $" and {remaining.Length - 3} more." : "."), CompanionMuted);
        if (remaining.Length > 0 && DrawAccentButton("Browse uncleared bosses", new Vector2(-1, 30 * CompanionScale), CompanionAccent))
        { legendarySearch = ""; legendaryAvailability = 3; legendaryCategory = legendaryPage = 0; selectedSanctuary = remaining[0].Id; }
        var trials = legendaryRequiredIds.Where(id => id.StartsWith("trial-", StringComparison.Ordinal)).ToArray();
        var remainingTrials = trials.Where(missing.Contains).ToArray();
        ImGui.Separator();
        DrawMewRequirement($"Required Special Trials / {trials.Length - remainingTrials.Length}/{trials.Length}", remainingTrials.Length == 0,
            remainingTrials.Length == 0 ? "All required trials are complete." : string.Join(", ", remainingTrials.Select(id => specialTrials.FirstOrDefault(trial => trial.Id == id)?.Name ?? id)));
        SanctuaryText("New required bosses can lock Mew's Sanctuary again. Your permits and caught Pokémon stay yours.", CompanionMuted);
    }

    private static void DrawMewRequirement(string label, bool complete, string description)
    { SanctuaryText(label + (complete ? " / Complete" : " / Incomplete"), complete ? PokemonGreen : PokemonYellow); SanctuaryText(description, CompanionMuted); }

    private string? SanctuaryChallengeRestriction(SanctuaryEntry entry)
    {
        if (!entry.Owned) return entry.Boss.Final ? "Complete Mew's current requirements first." : "Unlock this Sanctuary's permanent permit first.";
        if (trainer is null || !RegionalLeagueProgress.HasChampion(trainer.PveProgress.DefeatedOpponentIds)) return "Complete a regional League before challenging Sanctuaries.";
        if (trainer.ActiveEncounter is not null) return "Finish your current capture encounter first.";
        if (CollectionLocked) return "Finish your battle, run, raid or matchmaking first.";
        if (trainer.Party.Count == 0) return "Add Pokémon to your party first.";
        return trainer.Party.All(pokemon => pokemon.Vitals?.Hp == 0) ? "Heal your party at the Pokémon Center first." : null;
    }

    private void DrawSanctuaryAction(SanctuaryEntry entry, float width)
    {
        var boss = entry.Boss; var balance = trainer!.PvpProfile.RankTokens?.Balance ?? 0;
        if (!entry.Owned && !boss.Final)
        {
            ImGui.BeginDisabled(busy || balance < boss.Price);
            if (DrawAccentButton($"Unlock Sanctuary / {boss.Price:N0} RT", new Vector2(width, 32 * CompanionScale), balance < boss.Price ? PokemonRed : PokemonYellow)) pendingLegendaryPermit = boss;
            ImGui.EndDisabled();
            if (balance < boss.Price) SanctuaryText($"You need {boss.Price - balance:N0} more RT.", PokemonOrange);
            return;
        }
        if (trainer.ActiveEncounter is { Status: "active" } encounter && encounter.HabitatId == boss.Id)
        {
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Open capture battle##SanctuarySelected", new Vector2(width, 32 * CompanionScale), PokemonGreen)) OpenSanctuaryCapture(encounter.Id);
            ImGui.EndDisabled(); return;
        }
        var reason = SanctuaryChallengeRestriction(entry);
        ImGui.BeginDisabled(busy || reason is not null);
        if (DrawAccentButton(!entry.Owned ? "Complete the requirements" : "Prepare challenge", new Vector2(width, 32 * CompanionScale), !entry.Owned ? CompanionMuted : PokemonRed)) sanctuaryPreparation = true;
        ImGui.EndDisabled();
        if (reason is not null) SanctuaryText(reason, CompanionMuted);
    }

    private void DrawSanctuaryPreparation(SanctuaryEntry entry)
    {
        var scale = CompanionScale; var boss = entry.Boss;
        if (DrawAccentSmallButton("< Back to Sanctuaries", CompanionAccent)) { sanctuaryPreparation = false; return; }
        ImGui.Spacing(); ImGui.TextColored(CompanionAccent, $"{entry.Name} / Boss Lv. 100");
        if (boss.Raid is { } raid) SanctuaryText($"{raid.HpMultiplier}x HP / Enrage T{raid.EnrageTurn} / Limit {raid.TurnLimit} turns", CompanionMuted);
        ImGui.TextWrapped("Current party / First slot leads / Unlimited owned medicine"); ImGui.Spacing();
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (230 * scale)), 1, 3);
        if (ImGui.BeginTable("SanctuaryParty", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var pokemon in trainer!.Party.OrderBy(pokemon => pokemon.PartySlot))
            {
                ImGui.TableNextColumn();
                var line = ImGui.GetTextLineHeight(); var height = Math.Max(92 * scale, 3 * line + 36 * scale);
                DrawPveCardSurface("SanctuaryParty##" + pokemon.Id, height, false, out var min, out var max, out var after, false);
                var width = Math.Max(1, max.X - min.X - 16 * scale);
                ImGui.SetCursorScreenPos(min + new Vector2(8, 7) * scale); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(38 * scale));
                ImGui.SetCursorScreenPos(min + new Vector2(52, 9) * scale);
                DrawClippedText(pokemon.Nickname.Length > 0 ? pokemon.Nickname : SpeciesDisplayCatalog.Name(pokemon.Appearance), Math.Max(1, width - 44 * scale), CompanionAccent);
                ImGui.SetCursorScreenPos(min + new Vector2(52 * scale, 13 * scale + line));
                DrawClippedText($"Lv. {pokemon.Level}", Math.Max(1, width - 44 * scale), CompanionMuted);
                ImGui.SetCursorScreenPos(new Vector2(min.X + 8 * scale, max.Y - 13 * scale - 2 * line));
                DrawClippedText(pokemon.Vitals is { } vitals ? $"{vitals.Hp}/{vitals.MaxHp} HP{(vitals.Status.Length > 0 ? " / " + vitals.Status : "")}" : "Party condition unavailable", width, CompanionMuted);
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, HpBarRed);
                ImGui.ProgressBar(pokemon.Vitals is { MaxHp: > 0 } hp ? Math.Clamp((float)hp.Hp / hp.MaxHp, 0, 1) : 0, new Vector2(width, 4 * scale), "");
                ImGui.PopStyleColor(); ImGui.SetCursorScreenPos(after);
            }
            ImGui.EndTable();
        }
        ImGui.Spacing(); ImGui.TextColored(CompanionMuted, "AFTER VICTORY");
        ImGui.TextWrapped($"One wild {entry.Name} encounter at level {boss.CaptureLevel}, with normal stats. Bring Poké Balls. Catching, fleeing, losing or expiry consumes the opportunity.");
        SanctuaryText("Boss victories grant no ¥ or EXP. Your permanent permit stays yours. Every new capture requires another boss victory.", CompanionMuted);
        DrawPveDefeatNotice(trainer!.Party);
        var reason = SanctuaryChallengeRestriction(entry);
        if (reason is not null) SanctuaryText(reason, PokemonOrange);
        ImGui.Spacing(); ImGui.BeginDisabled(busy || reason is not null);
        if (DrawAccentButton("Enter Sanctuary", new Vector2(Math.Min(240 * scale, ImGui.GetContentRegionAvail().X), 34 * scale), PokemonRed))
        {
            var id = boss.Id; var ids = trainer.Party.OrderBy(pokemon => pokemon.PartySlot).Select(pokemon => pokemon.Id).ToArray();
            Run(async () => { await InSession(StartBattle(id, "standard", null, ids)); trainer = await InSession(plugin.Api.GetMe()); sanctuaryPreparation = false; }, "Challenging the sanctuary raid boss...");
        }
        ImGui.EndDisabled();
    }

    private void DrawLegendaryPermitPopup()
    {
        if (pendingLegendaryPermit is not null && !ImGui.IsPopupOpen("Buy permanent permit?")) ImGui.OpenPopup("Buy permanent permit?");
        if (!BeginConfirmationModal("Buy permanent permit?")) return;
        if (pendingLegendaryPermit is { } boss)
        {
            var balance = trainer?.PvpProfile.RankTokens?.Balance ?? 0;
            var owned = trainer?.Legendary.Permits.Contains(boss.Id) == true;
            ImGui.TextWrapped($"Spend {boss.Price:N0} RT to permanently unlock {SanctuarySpecies(boss.Species)}'s sanctuary? This does not give you a Pokémon. Defeat the boss before every capture opportunity.");
            ImGui.TextColored(PokemonYellow, $"Balance: {balance:N0} RT");
            if (DrawConfirmationActions("Unlock Sanctuary", PokemonYellow, busy || owned || boss.Final || balance < boss.Price || trainer is null, onCancel: () => pendingLegendaryPermit = null))
            {
                var request = legendaryRequests.GetOrAdd(boss.Id, _ => Guid.NewGuid().ToString());
                Run(async () => { trainer = await InSession(plugin.Api.BuyLegendaryPermit(request, boss.Id)); legendaryRequests.TryRemove(boss.Id, out _); SetStatus("Permanent sanctuary permit unlocked."); }, "Purchasing the permanent permit...");
                pendingLegendaryPermit = null; ImGui.CloseCurrentPopup();
            }
        }
        EndConfirmationModal();
    }
}
