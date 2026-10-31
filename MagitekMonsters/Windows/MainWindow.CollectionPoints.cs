using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<CollectionRewardDto> collectionRewards = [];
    private int objectiveCategory;
    private int objectiveState;
    private int objectivePage;
    private int rewardGeneration;
    private int rewardPage;
    private readonly ConcurrentDictionary<string, string> redemptionRequests = new();

    private void DrawCollectionGoals()
    {
        if (trainer is null) return;
        var progress = trainer.CollectionPoints;
        DrawSectionHeader("COLLECTION GOALS", "Complete Pokédex objectives to earn Collection Points");
        DrawWishlistShortcut();
        if (ImGui.BeginTable("CollectionMetrics", 3, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); DrawMetricCard("CollectionBalance", "Available CP", progress.Balance.ToString("N0"), PokemonBlue);
            ImGui.TableNextColumn(); DrawMetricCard("CollectionObjectives", "Goals completed", $"{progress.CompletedObjectives}/{progress.Objectives.Count}", PokemonGreen);
            ImGui.TableNextColumn(); DrawMetricCard("CollectionRegistered", "Genuine registrations", progress.RegisteredSpecies.ToString("N0"), PokemonYellow);
            ImGui.EndTable();
        }
        ImGui.TextWrapped("Points are awarded automatically, once per objective. Registered species stay recorded after release. The admin Pokédex completion toggle does not count.");
        ImGui.TextDisabled($"Lifetime earned: {progress.TotalEarned:N0} CP | Starters unlocked: {progress.UnlockedStarters.Count}/27");
        if (DrawAccentSmallButton("Open Collection Exchange", PokemonBlue)) { mainSection = 5; openCollectionExchange = true; }
        ImGui.Spacing();
        string[] categories = ["All objectives", "Species milestones", "Type collections", "Evolution families", "Generation surveys"];
        string[] categoryIds = ["", "species", "type", "family", "generation"];
        string[] states = ["In progress", "Completed", "All"];
        ImGui.SetNextItemWidth(210);
        if (ImGui.Combo("##ObjectiveCategory", ref objectiveCategory, categories, categories.Length)) objectivePage = 0;
        ImGui.SameLine(); ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##ObjectiveState", ref objectiveState, states, states.Length)) objectivePage = 0;
        var objectives = progress.Objectives.Where(entry => (objectiveCategory == 0 || entry.Category == categoryIds[objectiveCategory])
            && (objectiveState == 2 || entry.Completed == (objectiveState == 1)))
            .OrderBy(entry => entry.Completed).ThenByDescending(entry => (double)entry.Progress / Math.Max(1, entry.Target))
            .ThenBy(entry => entry.Category).ThenBy(entry => entry.Target).ToList();
        DrawPager("CollectionObjectives", ref objectivePage, objectives.Count, 8);
        if (objectives.Count == 0) ImGui.TextDisabled("No objectives match these filters.");
        foreach (var objective in objectives.Skip(objectivePage * 8).Take(8))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, objective.Completed ? PokemonGreen : CompanionAccent);
            ImGui.TextWrapped(objective.Name);
            ImGui.PopStyleColor();
            DrawWishlistPin("objective", objective.Id);
            ImGui.SameLine(); ImGui.TextDisabled(objective.Completed ? $"+{objective.Points} CP awarded" : $"Reward: {objective.Points} CP");
            DrawProgressBar((float)objective.Progress / Math.Max(1, objective.Target), new Vector2(-1, 19),
                $"{objective.Progress:N0} / {objective.Target:N0}", objective.Completed ? PokemonGreen : ExpBarBlue);
            if (objective.Completed && DateTimeOffset.TryParse(objective.EarnedAt, out var earnedAt))
                ImGui.TextDisabled($"Completed {earnedAt.LocalDateTime:g}");
            else ImGui.TextWrapped(objective.Category == "family" ? "Register every canonical member of an obtainable evolution family." : "Different species count once. Alternate forms and repeat catches do not add progress.");
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        DrawCollectionWishlist();
    }

    private bool openCollectionExchange;

    private void DrawCollectionExchange()
    {
        if (trainer is null) return;
        var progress = trainer.CollectionPoints;
        DrawSectionHeader("COLLECTION EXCHANGE", $"{progress.Balance:N0} CP available");
        ImGui.TextWrapped("Unlock a starter once with CP. Extra normal level-5 copies use ¥. CP cannot be bought, traded or earned from battles.");
        if (DrawAccentSmallButton("View collection objectives", PokemonBlue)) mainSection = 7;
        if (collectionRewards.Count == 0)
        {
            ImGui.TextDisabled("The collection reward catalog is loading. It retries automatically.");
            return;
        }
        foreach (var reward in collectionRewards.Where(entry => entry.Kind == "item")) DrawCollectionReward(reward);
        foreach (var reward in collectionRewards.Where(entry => entry.Kind == "pokemon")) DrawCollectionReward(reward);
        ImGui.Spacing();
        ImGui.TextColored(PokemonBlue, "STARTER COLLECTION");
        ImGui.TextDisabled($"{progress.UnlockedStarters.Count}/27 permanently unlocked. Your original partner is already unlocked.");
        string[] generations = ["All generations", "Gen 1", "Gen 2", "Gen 3", "Gen 4", "Gen 5", "Gen 6", "Gen 7", "Gen 8", "Gen 9"];
        ImGui.SetNextItemWidth(180);
        if (ImGui.Combo("##RewardGeneration", ref rewardGeneration, generations, generations.Length)) rewardPage = 0;
        var starters = collectionRewards.Where(entry => entry.Kind == "starter" && (rewardGeneration == 0 || entry.Generation == rewardGeneration)).ToList();
        DrawPager("StarterRewards", ref rewardPage, starters.Count, 6);
        foreach (var reward in starters.Skip(rewardPage * 6).Take(6)) DrawCollectionReward(reward);
    }

    private void DrawCollectionReward(CollectionRewardDto reward)
    {
        var progress = trainer!.CollectionPoints;
        var unlocked = reward.Kind == "starter" && reward.Species is not null && progress.UnlockedStarters.Contains(reward.Species);
        var itemOwned = reward.Kind == "item" && InventoryCount(reward.Id) > 0;
        var claimed = progress.ClaimedFormRewards.Contains(reward.Id);
        var missingForms = reward.RequiredForms.Where(name => !trainer.Pokedex.CaughtForms.Contains(name)).ToList();
        var eligible = progress.RegisteredSpecies >= reward.RequiredSpecies && missingForms.Count == 0;
        var affordable = unlocked ? trainer.Currency >= reward.CopyPrice.GetValueOrDefault() : progress.Balance >= reward.Points;
        if (ImGui.BeginTable($"RewardLayout{reward.Id}", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 66);
            ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 138);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            if (reward.Species is not null) DrawPokemonIcon(reward.Species, false, new Vector2(60, 60));
            else DrawItemImage(reward.Id, new Vector2(48, 48));
            ImGui.TableNextColumn();
            ImGui.TextColored(itemOwned || unlocked || claimed ? PokemonGreen : PokemonBlue, reward.Name);
            if (reward.Species is not null) ImGui.TextDisabled($"Generation {reward.Generation} | {reward.Type} | Lv. 5");
            ImGui.TextWrapped(reward.Description);
            ImGui.TableNextColumn();
            ImGui.TextColored(unlocked ? PokemonYellow : PokemonBlue, reward.Kind == "pokemon" ? "FREE REWARD" : unlocked ? $"¥ {reward.CopyPrice:N0}" : $"{reward.Points:N0} CP");
            if (reward.RequiredForms.Count > 0)
            {
                ImGui.TextWrapped($"{reward.RequiredForms.Count - missingForms.Count}/{reward.RequiredForms.Count} forms registered");
                if (missingForms.Count > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip("Missing: " + string.Join(", ", missingForms));
            }
            if (reward.RequiredSpecies > 0) ImGui.TextWrapped($"{progress.RegisteredSpecies:N0}/{reward.RequiredSpecies:N0} species");
            if (itemOwned || claimed) ImGui.TextColored(PokemonGreen, claimed ? "CLAIMED" : "OWNED / ACTIVE");
            else
            {
                ImGui.BeginDisabled(CollectionLocked || !eligible || !affordable);
                if (DrawAccentButton(reward.Kind == "pokemon" ? $"Claim##{reward.Id}" : unlocked ? $"Buy copy##{reward.Id}" : $"Unlock##{reward.Id}", new Vector2(-1, 32), eligible && affordable ? PokemonGreen : PokemonRed))
                    Run(() => RedeemCollectionReward(reward.Id, unlocked ? "copy" : "unlock"), $"Redeeming {reward.Name}...");
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(CollectionLocked ? "Finish battles, matchmaking or outgoing invitations first." : !eligible ? missingForms.Count > 0 ? "Missing: " + string.Join(", ", missingForms) : "Register enough genuine species first." : !affordable ? unlocked ? "Not enough ¥." : "Not enough Collection Points." : reward.Kind == "pokemon" ? "Claim one free Pokémon. No repeat copies, even after release." : unlocked ? "Buy another normal level-5 copy with ¥." : "Spend CP to unlock this reward permanently.");
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        ImGui.Spacing();
    }

    private async Task RedeemCollectionReward(string rewardId, string mode)
    {
        var owner = trainer!.User.Id;
        var key = $"{owner}:{rewardId}:{mode}";
        var requestId = redemptionRequests.GetOrAdd(key, _ => Guid.NewGuid().ToString());
        var result = await InSession(plugin.Api.RedeemCollectionReward(requestId, rewardId, mode, disposeCancellation.Token));
        redemptionRequests.TryRemove(key, out _);
        if (trainer?.User.Id != owner) return;
        trainer = result;
        RecordRecentAction("collection-reward:" + requestId, "reward", "Received collection reward",
            collectionRewards.FirstOrDefault(reward => reward.Id == rewardId)?.Name ?? rewardId);
        await InSession(LoadPokedexCatalog());
        SetStatus("Collection reward received. Points and unlocks saved.");
    }
}
