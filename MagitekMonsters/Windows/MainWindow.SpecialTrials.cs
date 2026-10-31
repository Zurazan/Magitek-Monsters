using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<SpecialTrialDto> specialTrials = [];

    private void DrawSpecialTrials()
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        ImGui.TextColored(CompanionAccent, "Permanent challenges. Special rewards.");
        ImGui.TextColored(CompanionMuted, "No RT fee or event deadline");
        ImGui.TextWrapped("Unlock special Pokémon through collecting and battling. Both challenges count toward Mew's current boss-completion requirements.");
        if (trainer.ActiveEncounter is { Status: "active", HabitatId: "trial-bloodmoon" } encounter)
        {
            ImGui.TextColored(PokemonGreen, "BLOODMOON CAPTURE OPPORTUNITY READY");
            ImGui.TextWrapped("Catch, flee, defeat or expiry consumes this opportunity. Another capture needs another raid victory.");
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Open capture battle", new Vector2(220, 34), PokemonGreen))
                Run(async () => { await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0; }, "Opening Bloodmoon capture...");
            ImGui.EndDisabled();
        }
        if (specialTrials.Count == 0) { ImGui.TextDisabled("Special trial catalog is unavailable."); return; }
        foreach (var trial in specialTrials)
        {
            ImGui.PushID(trial.Id);
            ImGui.Spacing();
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(12, 12) * scale);
            if (ImGui.BeginTable("SpecialTrial", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 76 * scale);
                ImGui.TableSetupColumn("Challenge", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
                DrawPokemonIcon(trial.Species, false, new Vector2(68, 68) * scale);
                ImGui.TableNextColumn();
                var cleared = trainer.SpecialTrials.DefeatedTrialIds.Contains(trial.Id);
                ImGui.TextColored(cleared ? PokemonGreen : PokemonBlue, trial.Name);
                ImGui.SameLine(); ImGui.TextDisabled(cleared ? "CLEARED" : "UNCLEARED");
                ImGui.TextWrapped(trial.Description);
                ImGui.TextColored(PokemonOrange, $"Lv. {trial.RecommendedLevel} / {(trial.BagLimit is int limit ? $"{limit} medicine actions" : "Unlimited owned medicine")} / No ¥ or EXP reward");
                var missing = trial.RequiredForms.Where(name => !trainer.Pokedex.CaughtForms.Contains(name)).ToList();
                if (trial.RequiredForms.Count > 0)
                    ImGui.TextColored(missing.Count == 0 ? PokemonGreen : PokemonYellow, missing.Count == 0 ? "Bear family registered. Repeat hunts unlocked." : "Still needed: " + string.Join(", ", missing));
                var leadReady = trial.LeadSpecies.Length == 0 || trainer.Party.OrderBy(pokemon => pokemon.PartySlot).FirstOrDefault()?.DisplaySpecies == trial.LeadSpecies;
                if (!leadReady) ImGui.TextColored(PokemonYellow, "Put regular Greninja first in your party to lead this trial.");
                if (trial.Id == "trial-battle-bond" && trainer.SpecialTrials.BattleBondUnlocked)
                    ImGui.TextColored(PokemonGreen, "BATTLE BOND UNLOCKED: open an owned Greninja's Forms menu.");
                DrawPveTypedTeam("SpecialTrialTeam", trial.TeamSpecies.ToArray());
                ImGui.TextWrapped(trial.TacticalHint);
                DrawPveDefeatNotice(trainer.Party);
                ImGui.BeginDisabled(busy || CollectionLocked || missing.Count > 0 || !leadReady || trainer.Party.Count == 0);
                if (DrawAccentButton(trial.Id == "trial-bloodmoon" ? "Challenge Bloodmoon raid" : "Challenge Bond Master", new Vector2(Math.Min(250 * scale, ImGui.GetContentRegionAvail().X), 34 * scale), PokemonRed))
                {
                    var id = trial.Id; var ids = trainer.Party.OrderBy(pokemon => pokemon.PartySlot).Select(pokemon => pokemon.Id).ToArray();
                    Run(async () => { await InSession(StartBattle(id, "standard", null, ids)); trainer = await InSession(plugin.Api.GetMe()); }, "Starting special trial...");
                }
                ImGui.EndDisabled();
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();
            ImGui.PopID();
        }
    }
}
