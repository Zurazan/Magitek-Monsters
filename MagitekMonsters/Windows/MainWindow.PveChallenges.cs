using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly (string Id, string Label, string Description)[] PveChallenges =
    [
        ("standard", "Standard", "Normal rewards and progression. No challenge restrictions."),
        ("no-bag", "No Bag", "Win without using medicine. Held items are allowed."),
        ("monotype", "Monotype", "Every selected Pokémon must share your chosen type before battle."),
        ("limited-party", "Limited Party", "Win with at most three Pokémon."),
        ("level-cap", "Level Cap", "No selected Pokémon may exceed the trainer's recommended level.")
    ];
    private static readonly string[] ChallengeTypes = ["Normal", "Fire", "Water", "Electric", "Grass", "Ice", "Fighting", "Poison", "Ground", "Flying", "Psychic", "Bug", "Rock", "Ghost", "Dragon", "Dark", "Steel", "Fairy"];
    private string? pveSetupOpponentId;
    private bool openPveSetup;
    private string? pveSetupCircuitId;
    private int pveChallengeIndex;
    private int pveTypeIndex;
    private readonly HashSet<string> pveTeamIds = [];

    private void OpenPveChallenge(string opponentId, string? circuitId = null)
    {
        pveSetupOpponentId = opponentId;
        pveSetupCircuitId = circuitId;
        pveChallengeIndex = 0;
        pveTeamIds.Clear();
        foreach (var pokemon in trainer?.Party ?? []) pveTeamIds.Add(pokemon.Id);
        openPveSetup = true;
    }

    private void DrawPveChallengeSetup()
    {
        if (openPveSetup) { ImGui.OpenPopup("Challenge preparation"); openPveSetup = false; }
        var scale = CompanionScale;
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(760 * scale, work.X * 0.9f), Math.Min(760 * scale, work.Y * 0.85f)), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Challenge preparation", ImGuiWindowFlags.None)) return;
        var opponent = opponents.FirstOrDefault(entry => entry.Id == pveSetupOpponentId);
        if (opponent is null || trainer is null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.TextColored(CompanionMuted, "BATTLE PREPARATION");
        ImGui.TextColored(CompanionAccent, opponent.Name);
        ImGui.BeginChild("ChallengePreparationBody", new Vector2(0, -58 * scale), false);
        ImGui.TextWrapped(opponent.Description);
        ImGui.TextWrapped(opponent.TacticalHint);
        ImGui.TextColored(CompanionMuted, $"Lv. {opponent.RecommendedLevel} / Difficulty {opponent.Difficulty}/5 / IVs {opponent.DefaultIvs}/31");
        var multiplier = InventoryCount("expshare") > 0 ? 2 : 1;
        ImGui.TextColored(ExpBarBlue, $"¥ {opponent.RewardCurrency:N0} / {opponent.RewardExperience * multiplier:N0} party EXP{(multiplier > 1 ? " (x2)" : "")}");
        DrawPveTypedTeam("ChallengeOpponentTeam", opponent.TeamSpecies.ToArray());
        ImGui.Separator();
        var cleared = RegionalLeaguePresentation.Clears(trainer).Contains(opponent.Id);
        if (!cleared) pveChallengeIndex = 0;
        if (ImGui.BeginCombo("Mode", PveChallenges[pveChallengeIndex].Label))
        {
            for (var index = 0; index < PveChallenges.Length; index++)
            {
                ImGui.BeginDisabled(index > 0 && !cleared);
                if (ImGui.Selectable(PveChallenges[index].Label, index == pveChallengeIndex)) pveChallengeIndex = index;
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }
        var challenge = PveChallenges[pveChallengeIndex];
        ImGui.TextWrapped(challenge.Description);
        if (!cleared) ImGui.TextColored(PokemonYellow, "Medal challenges unlock after your first normal victory.");
        if (challenge.Id == "monotype" && ImGui.BeginCombo("Shared type", ChallengeTypes[pveTypeIndex]))
        {
            for (var index = 0; index < ChallengeTypes.Length; index++)
                if (ImGui.Selectable(ChallengeTypes[index], index == pveTypeIndex)) pveTypeIndex = index;
            ImGui.EndCombo();
        }
        var bagLimit = challenge.Id == "no-bag" ? 0 : opponent.BagLimit;
        ImGui.TextColored(PokemonOrange, bagLimit is int limit ? $"Medicine limit: {limit} actions. Held items remain allowed." : "Medicine: unrestricted. Each use consumes a turn.");
        ImGui.TextWrapped("Real supplies, normal rewards. No bonus currency or EXP for medals. EXP is shared with your whole party.");
        ImGui.Separator();
        ImGui.TextColored(PokemonBlue, "BATTLE TEAM (party order is preserved)");
        DrawPveTeamSelection("ChallengeOwnedTeam");
        var chosen = trainer.Party.OrderBy(pokemon => pokemon.PartySlot).Where(pokemon => pveTeamIds.Contains(pokemon.Id)).ToList();
        DrawPveDefeatNotice(chosen);
        var invalid = PvePresentation.TeamError(chosen, challenge.Id, opponent.RecommendedLevel);
        if (invalid is not null) ImGui.TextColored(PokemonRed, invalid);
        if (challenge.Id == "monotype") ImGui.TextDisabled("The server checks type eligibility before starting. Temporary battle forms do not change it.");
        var medalId = challenge.Id == "monotype" ? $"monotype:{ChallengeTypes[pveTypeIndex]}" : challenge.Id;
        if (trainer.PveProgress.Medals.Any(medal => medal.OpponentId == opponent.Id && medal.MedalId == medalId))
            ImGui.TextColored(PokemonYellow, "MEDAL ALREADY EARNED. You can still challenge again.");
        ImGui.EndChild();
        ImGui.Separator();
        var access = RegionalPreparationLock(opponent);
        ImGui.BeginDisabled(busy || CollectionLocked || invalid is not null || access is not null);
        if (DrawAccentButton("Start battle", new Vector2(190, 34) * scale, PokemonRed))
        {
            var id = opponent.Id;
            var mode = challenge.Id;
            var type = mode == "monotype" ? ChallengeTypes[pveTypeIndex] : null;
            var ids = chosen.Select(pokemon => pokemon.Id).ToArray();
            var circuitId = pveSetupCircuitId;
            ImGui.CloseCurrentPopup();
            Run(() => StartPreparedBattle(id, mode, type, ids, circuitId), $"Challenging {opponent.Name}...");
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 34) * scale)) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static string MedalLabel(string id) => id switch
    {
        "no-bag" => "No Bag", "limited-party" => "Limited Party", "level-cap" => "Level Cap",
        _ => id.StartsWith("monotype:", StringComparison.Ordinal) ? $"Monotype: {id[9..]}" : id
    };
}
