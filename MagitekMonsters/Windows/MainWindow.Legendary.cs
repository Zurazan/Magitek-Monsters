using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<LegendaryBossDto> legendaryBosses = [];
    private string legendarySearch = string.Empty;
    private int legendaryPage;
    private bool legendaryOwnedOnly;
    private LegendaryBossDto? pendingLegendaryPermit;
    private readonly ConcurrentDictionary<string, string> legendaryRequests = new();
    private static string SanctuarySpecies(string species) => species.EndsWith("-Galar", StringComparison.Ordinal)
        ? $"Galarian {species[..^6]}" : species;

    private void DrawLegendary()
    {
        if (trainer is null) return;
        if (battle?.Kind == "trainer" && battle.OpponentId.StartsWith("legendary-", StringComparison.Ordinal))
        { DrawBattleConsole(battle); return; }
        DrawSectionHeader("LEGENDARY SANCTUARIES", "Single raid bosses / Bring up to six Pokémon / A fresh victory for every capture");
        var progress = trainer.Legendary;
        var balance = trainer.PvpProfile.RankTokens?.Balance ?? 0;
        if (ImGui.BeginTable("SanctuaryMetrics", 3, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); DrawMetricCard("SanctuaryRT", "Rank Tokens", $"{balance:N0} RT", PokemonYellow);
            ImGui.TableNextColumn(); DrawMetricCard("SanctuaryPermits", "Permanent permits", progress.Permits.Count.ToString(), PokemonBlue);
            ImGui.TableNextColumn(); DrawMetricCard("SanctuaryCompletion", "Required boss clears", $"{progress.CompletedBosses}/{progress.RequiredBosses}", PokemonGreen);
            ImGui.EndTable();
        }
        ImGui.TextWrapped("Buy a permit once with ranked RT. Defeat the raid boss for one normal wild capture encounter. Catching, fleeing, losing or expiry consumes that opportunity. Boss victories grant no ¥ or EXP. Use normal balls or a guaranteed Master Ball from the Mart's RT Exchange. Raid buffs never carry over to captured Pokémon.");
        ImGui.TextColored(progress.MewAvailable ? PokemonGreen : PokemonOrange, progress.MewAvailable ? "MEW SANCTUARY AVAILABLE" : "MEW: COMPLETE EVERY CURRENTLY REQUIRED BOSS");
        ImGui.TextWrapped("Mew has no permit fee. Complete any regional League, the required Sanctuaries and Special Trials. You do not need every region's badges or Champions. Your caught Pokémon and permanent permits stay yours.");
        if (ImGui.CollapsingHeader("Remaining Mew requirements"))
        {
            var legacyLeague = opponents.Where(entry => entry.Legacy && entry.RematchOf is null).Select(entry => entry.Id).ToHashSet();
            if (progress.MissingBossIds.Any(legacyLeague.Contains)) ImGui.TextDisabled("Complete any regional League");
            foreach (var id in progress.MissingBossIds.Where(id => !legacyLeague.Contains(id)))
                ImGui.TextDisabled(SanctuarySpecies(legendaryBosses.FirstOrDefault(entry => entry.Id == id)?.Species ?? specialTrials.FirstOrDefault(entry => entry.Id == id)?.Name ?? opponents.FirstOrDefault(entry => entry.Id == id)?.Name ?? id));
        }
        if (trainer.ActiveEncounter is { Status: "active" } encounter && encounter.HabitatId.StartsWith("legendary-", StringComparison.Ordinal))
        {
            ImGui.Separator();
            ImGui.TextColored(PokemonGreen, $"CAPTURE OPPORTUNITY: {SanctuarySpecies(encounter.Pokemon.Species)} / Lv. {encounter.Pokemon.Level}");
            ImGui.TextDisabled($"Expires: {encounter.ExpiresAt}");
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Open capture battle", new Vector2(200, 34), PokemonGreen))
                Run(async () => { await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0; }, "Opening your earned capture encounter...");
            ImGui.EndDisabled();
        }
        if (legendaryBosses.Count == 0) { ImGui.TextDisabled("Sanctuary catalog is unavailable."); return; }
        ImGui.Spacing(); ImGui.SetNextItemWidth(280);
        if (ImGui.InputTextWithHint("##LegendarySearch", "Find a Legendary or Mythical...", ref legendarySearch, 80)) legendaryPage = 0;
        ImGui.SameLine();
        if (ImGui.Checkbox("Unlocked only", ref legendaryOwnedOnly)) legendaryPage = 0;
        var visible = legendaryBosses.Where(entry => (entry.Species.Contains(legendarySearch, StringComparison.OrdinalIgnoreCase)
            || SanctuarySpecies(entry.Species).Contains(legendarySearch, StringComparison.OrdinalIgnoreCase))
            && (!legendaryOwnedOnly || (entry.Final ? progress.MewAvailable : progress.Permits.Contains(entry.Id)))).ToList();
        DrawPager("SanctuaryPages", ref legendaryPage, visible.Count, 6);
        foreach (var boss in visible.Skip(legendaryPage * 6).Take(6))
        {
            ImGui.PushID(boss.Id);
            var unlocked = boss.Final ? progress.MewAvailable : progress.Permits.Contains(boss.Id);
            if (ImGui.BeginTable("SanctuaryCard", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 68);
                ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow(); ImGui.TableNextColumn(); DrawPokemonIcon(boss.Species, false, new Vector2(60, 60));
                ImGui.TableNextColumn(); ImGui.TextColored(boss.Final ? PokemonBlue : PokemonYellow, $"#{boss.DexNumber:D4} {SanctuarySpecies(boss.Species)}");
                ImGui.SameLine(); ImGui.TextDisabled(progress.DefeatedBossIds.Contains(boss.Id) ? "BOSS CLEARED" : boss.Category);
                if (boss.Species.EndsWith("-Galar", StringComparison.Ordinal))
                    ImGui.TextColored(PokemonBlue, "Regional form / Separate permit / Caught separately");
                ImGui.TextWrapped(string.Join(" / ", boss.Moves));
                if (boss.Raid is { } raid)
                    ImGui.TextColored(PokemonOrange, $"Lv. 100 / {raid.HpMultiplier}x HP / Enrage: {raid.EnrageTurn} turns / Time limit: {raid.TurnLimit}");
                if (boss.CaptureLevel > 0) ImGui.TextColored(PokemonGreen, $"Capture Lv. {boss.CaptureLevel} / Normal stats");
                ImGui.TextDisabled("Up to six Pokémon / Unlimited owned medicine / Four boss moves");
                if (ImGui.CollapsingHeader("Raid rules and counterplay"))
                {
                    ImGui.TextWrapped(boss.TacticalHint);
                    ImGui.TextWrapped("Phase 2 after crossing 65% HP: bonus action on even turns. Phase 3 after crossing 30%: bonus action every turn. Healing cannot undo a crossed threshold. Both phases cleanse status, confusion and negative stages once. HP uses a minimum baseline of 350. Offense has a floor before stages; defenses stay normal. Indirect damage and healing use unscaled HP. Boss PP replenishes each turn. Enrage adds 0.10x damage per turn until the hard time limit.");
                }
                if (!unlocked && !boss.Final)
                {
                    ImGui.BeginDisabled(busy || balance < boss.Price);
                    if (DrawAccentButton($"Buy permit: {boss.Price:N0} RT", new Vector2(230, 32), balance >= boss.Price ? PokemonYellow : PokemonRed))
                        pendingLegendaryPermit = boss;
                    ImGui.EndDisabled();
                }
                else
                {
                    ImGui.BeginDisabled(busy || !unlocked || CollectionLocked || !MagitekMonsters.Contracts.RegionalLeagueProgress.HasChampion(trainer.PveProgress.DefeatedOpponentIds));
                    DrawPveDefeatNotice(trainer.Party);
                    if (DrawAccentButton("Challenge raid boss", new Vector2(230, 32), PokemonRed))
                    {
                        var id = boss.Id;
                        var ids = trainer.Party.Select(entry => entry.Id).ToArray();
                        Run(async () => { await InSession(StartBattle(id, "standard", null, ids)); trainer = await InSession(plugin.Api.GetMe()); }, "Challenging the sanctuary raid boss...");
                    }
                    ImGui.EndDisabled();
                }
                ImGui.EndTable();
            }
            ImGui.Separator(); ImGui.PopID();
        }
        if (!MagitekMonsters.Contracts.RegionalLeagueProgress.HasChampion(trainer.PveProgress.DefeatedOpponentIds)) ImGui.TextColored(PokemonOrange, "Sanctuary challenges require a League Champion victory.");
        DrawLegendaryPermitPopup();
    }

    private void DrawLegendaryPermitPopup()
    {
        if (pendingLegendaryPermit is not null && !ImGui.IsPopupOpen("Buy permanent permit?")) ImGui.OpenPopup("Buy permanent permit?");
        if (!BeginConfirmationModal("Buy permanent permit?")) return;
        if (pendingLegendaryPermit is { } boss)
        {
            ImGui.TextWrapped($"Spend {boss.Price:N0} RT to permanently unlock {SanctuarySpecies(boss.Species)}'s sanctuary? This does not give you a Pokémon. You must defeat the raid boss before every capture opportunity.");
            if (DrawConfirmationActions("Confirm purchase", PokemonYellow, busy, onCancel: () => pendingLegendaryPermit = null))
            {
                var request = legendaryRequests.GetOrAdd(boss.Id, _ => Guid.NewGuid().ToString());
                Run(async () => { trainer = await InSession(plugin.Api.BuyLegendaryPermit(request, boss.Id)); legendaryRequests.TryRemove(boss.Id, out _); SetStatus("Permanent sanctuary permit unlocked."); }, "Purchasing the permanent permit...");
                pendingLegendaryPermit = null; ImGui.CloseCurrentPopup();
            }
        }
        EndConfirmationModal();
    }
}
