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
    private bool FrontierBusy => trainer?.FrontierRun?.Status is "active" or "between";
    private List<FrontierRentalDto> frontierRentals = [];
    private int frontierBracket;
    private int frontierMode;
    private int frontierRentalIndex;
    private int frontierRewardPage;
    private bool openBpExchange;
    private int frontierAchievementPage;
    private int paradoxPage;
    private int paradoxEra;
    private string paradoxSearch = string.Empty;
    private string frontierRewardSearch = string.Empty;
    private readonly HashSet<string> frontierTeamIds = [];
    private readonly ConcurrentDictionary<string, string> frontierRequests = new();
    private string factoryOpenedRun = string.Empty;

    private void DrawFrontier()
    {
        if (trainer is null) return;
        if (battle?.FrontierId is not null) { DrawBattleConsole(battle); return; }
        if (FrontierBusy && trainer.FrontierRun!.Facility == "factory" && factoryOpenedRun != trainer.FrontierRun.Id)
        { factoryOpenedRun = trainer.FrontierRun.Id; openFactory = true; }
        DrawSectionHeader("BATTLE FRONTIER", "Level-50 singles / Rental or owned teams / Permanent collection progress");
        var progress = trainer.Frontier;
        if (ImGui.BeginTable("FrontierMetrics", 3, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); DrawMetricCard("FrontierBP", "Battle Points", $"{progress.Balance:N0} BP", PokemonYellow);
            ImGui.TableNextColumn(); DrawMetricCard("FrontierLifetime", "Lifetime victories", progress.LifetimeWins.ToString("N0"), PokemonBlue);
            ImGui.TableNextColumn(); DrawMetricCard("FrontierStreak", "Saved run streak", FrontierBusy ? trainer.FrontierRun!.Streak.ToString("N0") : "No active run", PokemonGreen);
            ImGui.EndTable();
        }
        ImGui.TextDisabled($"Personal bests: Accessible {progress.BestAccessible} / Standard {progress.BestStandard}");
        if (!ImGui.BeginTabBar("FrontierTabs")) return;
        if (ImGui.BeginTabItem("Battle Tower")) { DrawFrontierTower(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Battle Factory", openFactory ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        { openFactory = false; DrawFactory(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Ultra Beast unlocks")) { DrawFrontierBeasts(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Paradox Research")) { DrawParadoxResearch(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("BP Exchange", openBpExchange ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        { openBpExchange = false; DrawFrontierItems(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Achievements")) { DrawFrontierAchievements(); ImGui.EndTabItem(); }
        ImGui.EndTabBar();
    }

    private void DrawFrontierTower()
    {
        var run = trainer!.FrontierRun;
        if (FrontierBusy && run!.Facility == "factory")
        {
            ImGui.TextWrapped("Your saved rental draft or streak is in the Battle Factory.");
            if (DrawAccentButton("Open Battle Factory", new Vector2(220, 34), PokemonBlue)) openFactory = true;
            return;
        }
        if (FrontierBusy)
        {
            ImGui.TextColored(PokemonYellow, $"{run!.Bracket.ToUpperInvariant()} / {run.TeamMode.ToUpperInvariant()} / STREAK {run.Streak}");
            if (run.Status == "active")
            {
                if (battle?.Id == run.BattleId) DrawPve();
                else if (DrawAccentButton("Resume saved fight", new Vector2(210, 34), PokemonBlue))
                    Run(async () => battle = await InSession(plugin.Api.GetBattle(run.BattleId)), "Restoring the Frontier fight...");
                return;
            }
            ImGui.TextColored(PokemonGreen, "VICTORY SAVED");
            if (battle?.Id == run.BattleId && battle.Status == "won") ImGui.TextColored(PokemonYellow, $"+{battle.RewardBP} BP, including any new achievement bonuses");
            ImGui.TextWrapped("HP, status and PP are fully restored for the next fight. Builds and team order remain locked. Earned BP, achievements and collection unlocks are already saved.");
            DrawFrontierTeam(run.Team.Select(entry => entry.Pokemon).ToList());
            var boss = (run.Streak + 1) % 7 == 0;
            ImGui.TextColored(boss ? PokemonOrange : PokemonBlue, boss ? "NEXT: TOWER CAPTAIN / MILESTONE BOSS" : $"NEXT: FIGHT {run.Streak + 1}");
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Continue streak", new Vector2(180, 34), PokemonRed)) Run(ContinueFrontier, "Starting the next Tower fight...");
            ImGui.SameLine();
            if (DrawAccentButton("End run", new Vector2(140, 34), PokemonOrange)) ImGui.OpenPopup("End Frontier run?");
            ImGui.EndDisabled();
            DrawFrontierAbandonPopup();
            return;
        }
        if (battle?.FrontierId is not null)
        {
            DrawPve();
            return;
        }
        if (run is not null) ImGui.TextDisabled($"Last run: {run.Status.ToUpperInvariant()} / {run.Streak} victories. Permanent progress retained.");
        ImGui.TextWrapped("Three Pokémon, level 50, no bag items. Held items and Mega Evolution are enabled. Full healing between fights. A loss or draw ends the streak; disconnecting pauses it. No ¥ or EXP rewards here.");
        ImGui.TextWrapped("Species and held items cannot repeat. Restricted Legendary and Mythical Pokémon are excluded; Ultra Beasts are allowed.");
        ImGui.Spacing();
        if (DrawAccentButton("Accessible", new Vector2(150, 36), frontierBracket == 0 ? PokemonBlue : PokemonBlue with { W = 0.45f })) frontierBracket = 0;
        ImGui.SameLine(); ImGui.BeginDisabled(!trainer.Frontier.StandardUnlocked);
        if (DrawAccentButton("Standard", new Vector2(150, 36), frontierBracket == 1 ? PokemonRed : PokemonRed with { W = 0.45f })) frontierBracket = 1;
        ImGui.EndDisabled();
        if (!trainer.Frontier.StandardUnlocked) ImGui.TextDisabled("Standard unlocks after defeating the Champion. No collection unlock requires it.");
        ImGui.TextWrapped(frontierBracket == 0 ? "Accessible: forgiving opponents, capped difficulty and 1-3 base BP per win. Every seventh fight is a boss (+3 BP)." : "Standard: expert AI and competitive teams, 3-7 base BP per win. Every seventh fight is a boss (+8 BP).");
        if (!trainer.Frontier.RentalUnlocked && trainer.Frontier.OwnedUnlocked) frontierMode = 1;
        ImGui.Spacing();
        ImGui.BeginDisabled(!trainer.Frontier.RentalUnlocked);
        if (ImGui.RadioButton("Borrow a prepared team", frontierMode == 0)) frontierMode = 0;
        ImGui.EndDisabled(); ImGui.SameLine(); ImGui.BeginDisabled(!trainer.Frontier.OwnedUnlocked);
        if (ImGui.RadioButton("Use my Pokémon", frontierMode == 1)) frontierMode = 1;
        ImGui.EndDisabled();
        if (!trainer.Frontier.RentalUnlocked) ImGui.TextDisabled("Rentals: defeat all five Rookie trainers once. No owned level-50 team required.");
        if (!trainer.Frontier.OwnedUnlocked) ImGui.TextDisabled("Owned teams: train three Pokémon to level 50. No Gym or Champion clear required.");
        var ready = frontierMode == 0 ? trainer.Frontier.RentalUnlocked : trainer.Frontier.OwnedUnlocked;
        string rentalId = string.Empty;
        if (frontierMode == 0)
        {
            if (frontierRentals.Count == 0) { ImGui.TextDisabled("Rental catalog is loading."); ready = false; }
            else
            {
                frontierRentalIndex = Math.Clamp(frontierRentalIndex, 0, frontierRentals.Count - 1);
                var rental = frontierRentals[frontierRentalIndex]; rentalId = rental.Id;
                ImGui.SetNextItemWidth(260);
                if (ImGui.BeginCombo("Rental selection", rental.Name))
                {
                    for (var index = 0; index < frontierRentals.Count; index++) if (ImGui.Selectable(frontierRentals[index].Name, frontierRentalIndex == index)) frontierRentalIndex = index;
                    ImGui.EndCombo();
                }
                ImGui.TextWrapped(rental.Description);
                ImGui.TextDisabled("Borrowed Pokémon never enter your party, PC or Pokédex.");
                DrawFrontierTeam(frontierBracket == 0 ? rental.Team : rental.StandardTeam);
            }
        }
        else
        {
            frontierTeamIds.RemoveWhere(id => !trainer.Party.Any(entry => entry.Id == id));
            ImGui.TextDisabled("Select exactly three level-50+ party members. Current party order determines the lead.");
            foreach (var pokemon in trainer.Party)
            {
                var selected = frontierTeamIds.Contains(pokemon.Id);
                ImGui.BeginDisabled(pokemon.Level < 50);
                if (ImGui.Checkbox($"{pokemon.Nickname} / {pokemon.DisplaySpecies} Lv. {pokemon.Level}##Frontier{pokemon.Id}", ref selected))
                { if (selected) frontierTeamIds.Add(pokemon.Id); else frontierTeamIds.Remove(pokemon.Id); }
                ImGui.EndDisabled();
            }
            ready &= frontierTeamIds.Count == 3 && trainer.Party.Where(entry => frontierTeamIds.Contains(entry.Id)).All(entry => entry.Level >= 50);
        }
        ImGui.BeginDisabled(busy || CollectionLocked || !ready || frontierBracket == 1 && !trainer.Frontier.StandardUnlocked);
        if (DrawAccentButton("Enter the Tower", new Vector2(200, 36), PokemonRed))
        {
            var selectedRental = rentalId;
            var ids = trainer.Party.Where(entry => frontierTeamIds.Contains(entry.Id)).Select(entry => entry.Id).ToArray();
            var bracket = frontierBracket == 0 ? "accessible" : "standard"; var mode = frontierMode == 0 ? "rental" : "owned";
            Run(() => EnterFrontier(bracket, mode, selectedRental, ids), "Entering the Battle Tower...");
        }
        ImGui.EndDisabled();
        if (CollectionLocked) ImGui.TextDisabled("Finish your existing battle, League run, matchmaking or outgoing invitation first.");
    }

    private void DrawFrontierTeam(List<PokemonDto> team)
    {
        foreach (var pokemon in team)
        {
            ImGui.PushID(pokemon.DisplaySpecies);
            if (ImGui.BeginTable("FrontierTeamMember", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow(); ImGui.TableNextColumn(); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(54, 54));
                ImGui.TableNextColumn(); ImGui.TextColored(PokemonBlue, $"{pokemon.Nickname} / {pokemon.DisplaySpecies} Lv. 50");
                ImGui.TextWrapped($"{pokemon.Nature} / {pokemon.Ability} / {itemCatalog.FirstOrDefault(entry => entry.Id == pokemon.HeldItem)?.Name ?? pokemon.HeldItem}");
                ImGui.TextWrapped(string.Join(" / ", pokemon.Moves));
                ImGui.EndTable();
            }
            ImGui.PopID();
        }
    }

    private void DrawFrontierAbandonPopup()
    {
        if (!BeginConfirmationModal("End Frontier run?")) return;
        ImGui.TextWrapped("End this streak? Earned BP, achievements, personal bests and Ultra Beast unlocks remain saved. The next run starts at zero.");
        if (DrawConfirmationActions("End run", PokemonOrange, busy, "Keep playing")) { ImGui.CloseCurrentPopup(); Run(EndFrontier, "Ending the Frontier run..."); }
        EndConfirmationModal();
    }

    private void DrawFrontierBeasts()
    {
        ImGui.TextWrapped("Complete either the battle achievement OR the lifetime-win objective to unlock each BP purchase permanently. Both team modes and brackets count towards lifetime wins. Losing never removes progress.");
        ImGui.TextDisabled("Poipole evolves into Naganadel when it knows Dragon Pulse. Use its move submenu to relearn or teach it.");
        foreach (var reward in trainer!.Frontier.Rewards.Where(entry => entry.Kind == "pokemon")) DrawFrontierReward(reward);
    }
    private void DrawFrontierItems()
    {
        ImGui.TextWrapped("Competitive supplies and reusable move licenses. These are alternatives to existing ¥ purchases; BP cannot be exchanged for ¥ or CP.");
        ImGui.SetNextItemWidth(280);
        if (ImGui.InputTextWithHint("##FrontierItemSearch", "Search BP rewards...", ref frontierRewardSearch, 80)) frontierRewardPage = 0;
        var rewards = trainer!.Frontier.Rewards.Where(entry => entry.Kind == "item" && entry.Name.Contains(frontierRewardSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        DrawPager("FrontierItems", ref frontierRewardPage, rewards.Count, 6);
        foreach (var reward in rewards.Skip(frontierRewardPage * 6).Take(6)) DrawFrontierReward(reward);
    }
    private int ParadoxWaitSeconds => DateTimeOffset.TryParse(trainer?.Frontier.NextParadoxAt, out var available)
        ? Math.Max(0, (int)Math.Ceiling((available - DateTimeOffset.UtcNow).TotalSeconds)) : 0;
    private void DrawParadoxResearch()
    {
        ImGui.TextWrapped("Buy a species permit once, then hunt it in targeted level-50 wild battles. Your normal party, balls and medicine apply. Rentals and either Frontier bracket count towards permit requirements.");
        ImGui.TextWrapped("Permits survive failed catches, defeats, releases and seasons. Repeat hunts cost no BP. Normal shiny odds and the Shiny Charm apply. Koraidon and Miraidon are not research rewards.");
        ImGui.TextColored(PokemonBlue, ParadoxWaitSeconds > 0 ? $"Research cooldown: {ParadoxWaitSeconds / 60}:{ParadoxWaitSeconds % 60:00}" : "Research ready / Shared five-minute encounter cooldown");
        if (trainer?.ActiveEncounter is { Status: "active" } encounter)
        {
            ImGui.TextColored(PokemonOrange, $"Saved wild encounter: {encounter.Pokemon.Species}");
            if (DrawAccentButton("Resume wild encounter", new Vector2(200, 32), PokemonBlue))
                Run(async () => { await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0; }, "Restoring your wild battle...");
        }
        ImGui.SetNextItemWidth(230);
        if (ImGui.InputTextWithHint("##ParadoxSearch", "Search Paradox Pokémon...", ref paradoxSearch, 80)) paradoxPage = 0;
        ImGui.SameLine(); ImGui.SetNextItemWidth(120);
        string[] eras = ["Both eras", "Ancient", "Future"];
        if (ImGui.Combo("##ParadoxEra", ref paradoxEra, eras, eras.Length)) paradoxPage = 0;
        var permits = trainer!.Frontier.Rewards.Where(entry => entry.Kind == "permit" && entry.Name.Contains(paradoxSearch, StringComparison.OrdinalIgnoreCase)
            && (paradoxEra == 0 || entry.Era == eras[paradoxEra])).ToList();
        DrawPager("ParadoxPermits", ref paradoxPage, permits.Count, 6);
        foreach (var permit in permits.Skip(paradoxPage * 6).Take(6)) DrawFrontierReward(permit);
    }
    private void DrawFrontierReward(FrontierRewardDto reward)
    {
        ImGui.PushID(reward.Id);
        ImGui.Separator();
        if (ImGui.BeginTable("FrontierReward", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Purchase", ImGuiTableColumnFlags.WidthFixed, 116);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            if (reward.Species is not null) DrawPokemonIcon(reward.Species, false, new Vector2(58, 58));
            else DrawItemImage(reward.ItemId ?? string.Empty, new Vector2(44, 44));
            ImGui.TableNextColumn(); ImGui.TextColored(reward.Unlocked ? PokemonGreen : PokemonBlue, reward.Name);
            if (ImGui.IsItemHovered() && itemCatalog.FirstOrDefault(entry => entry.Id == reward.ItemId) is { } sourceItem
                && !string.IsNullOrWhiteSpace(sourceItem.AcquisitionNote))
                ImGui.SetTooltip(sourceItem.AcquisitionNote + (!sourceItem.BpOnly ? $" Also available for ¥ {sourceItem.Price:N0} in the Mart." : string.Empty));
            if (reward.Kind == "permit")
            {
                ImGui.TextColored(PokemonBlue, $"{reward.Era} / Lifetime wins {reward.Progress}/{reward.LifetimeWins}");
                ImGui.TextDisabled(reward.Purchased > 0 ? "PERMANENT PERMIT OWNED" : "No high streak or Standard clear required.");
            }
            else if (reward.Species is not null)
            {
                var achievement = trainer!.Frontier.Achievements.FirstOrDefault(entry => entry.Id == reward.AchievementId);
                ImGui.TextWrapped($"Lifetime victories {reward.Progress}/{reward.LifetimeWins} OR {achievement?.Name}: {achievement?.Progress}/{achievement?.Target}");
                ImGui.TextDisabled(reward.Unlocked ? "PURCHASE ACCESS PERMANENTLY UNLOCKED" : "Either route unlocks access. No victories are spent.");
            }
            ImGui.TextWrapped(reward.Description);
            ImGui.TableNextColumn(); ImGui.TextColored(PokemonYellow, reward.Kind == "permit" && reward.Purchased > 0 ? "No repeat fee" : $"{reward.CurrentPrice} BP");
            if (reward.Kind == "permit" && reward.Purchased > 0)
            {
                ImGui.BeginDisabled(CollectionLocked || ParadoxWaitSeconds > 0);
                if (DrawAccentButton("Hunt", new Vector2(-1, 32), PokemonBlue)) Run(() => StartParadoxHunt(reward.Id), $"Locating {reward.Species}...");
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(CollectionLocked ? "Finish or resume your saved battle, run or matchmaking first." : ParadoxWaitSeconds > 0 ? "Wait for the shared research cooldown." : "Start a targeted level-50 catch battle using your normal party.");
                ImGui.EndTable(); ImGui.PopID(); ImGui.Spacing(); return;
            }
            var item = itemCatalog.FirstOrDefault(entry => entry.Id == reward.ItemId);
            var owned = reward.Species is null && item?.MaxOwned is int max && InventoryCount(item.Id) >= max;
            var affordable = trainer!.Frontier.Balance >= reward.CurrentPrice;
            var locked = busy || !reward.Unlocked || owned || reward.Kind == "pokemon" && CollectionLocked;
            ImGui.BeginDisabled(locked);
            if (DrawAccentButton(reward.Kind == "permit" ? "Permit" : reward.Purchased > 0 && reward.Species is not null ? "Buy copy" : "Buy", new Vector2(-1, 32), affordable ? PokemonGreen : PokemonRed))
                Run(() => BuyFrontierReward(reward.Id), $"Redeeming {reward.Name}...");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(owned ? "You already own this reusable item." : !reward.Unlocked ? "Complete the listed unlock requirement first." : reward.Kind == "pokemon" && CollectionLocked ? "Finish your battle or run before receiving Pokémon." : !affordable ? "Not enough Battle Points." : "Spend BP. Purchases are saved and retry-safe.");
            ImGui.EndTable();
        }
        ImGui.PopID(); ImGui.Spacing();
    }
    private void DrawFrontierAchievements()
    {
        ImGui.TextWrapped("Achievements and their BP bonuses are awarded automatically once. They never reset with a loss or season. Some also unlock Ultra Beast purchases early.");
        var achievements = trainer!.Frontier.Achievements;
        DrawPager("FrontierAchievements", ref frontierAchievementPage, achievements.Count, 7);
        foreach (var achievement in achievements.Skip(frontierAchievementPage * 7).Take(7))
        {
            ImGui.TextColored(achievement.Completed ? PokemonGreen : PokemonBlue, achievement.Name);
            ImGui.SameLine(); ImGui.TextDisabled(achievement.Completed ? $"+{achievement.Bonus} BP awarded" : $"One-time bonus: {achievement.Bonus} BP");
            DrawProgressBar((float)achievement.Progress / Math.Max(1, achievement.Target), new Vector2(-1, 18), $"{achievement.Progress}/{achievement.Target}", achievement.Completed ? PokemonGreen : PokemonBlue);
            ImGui.Spacing();
        }
    }
    private void DrawFrontierProfile()
    {
        if (trainer is null || !ImGui.CollapsingHeader("Frontier records and achievements")) return;
        var progress = trainer.Frontier;
        ImGui.TextColored(PokemonYellow, $"{progress.Balance:N0} BP / Lifetime earned {progress.TotalEarned:N0} BP");
        ImGui.TextDisabled($"Victories {progress.LifetimeWins:N0} / Best Accessible {progress.BestAccessible} / Best Standard {progress.BestStandard}");
        DrawFrontierAchievements();
        if (DrawAccentSmallButton("Open Battle Frontier", PokemonBlue)) mainSection = 8;
        ImGui.Spacing();
    }
    private async Task EnterFrontier(string bracket, string mode, string rentalId, string[] ids)
    {
        var refresh = true;
        try { battle = await InSession(plugin.Api.StartFrontier(bracket, mode, rentalId, ids)); megaEvolveSelected = false; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Frontier run saved. Choose your action.");
    }
    private async Task ContinueFrontier()
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.NextFrontier(run.Id, run.Revision)); megaEvolveSelected = false; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Next Frontier fight ready. Your team has been fully healed.");
    }
    private async Task EndFrontier()
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { await InSession(plugin.Api.AbandonFrontier(run.Id, run.Revision)); battle = null; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Streak ended. Earned BP and permanent progress retained.");
    }
    private async Task BuyFrontierReward(string rewardId)
    {
        var owner = trainer!.User.Id; var key = $"{owner}:{rewardId}";
        var requestId = frontierRequests.GetOrAdd(key, _ => Guid.NewGuid().ToString());
        var result = await InSession(plugin.Api.RedeemFrontierReward(requestId, rewardId, 1));
        frontierRequests.TryRemove(key, out _);
        if (trainer?.User.Id != owner) return;
        trainer = result;
        RecordRecentAction("frontier-reward:" + requestId, "reward", "Received Frontier reward", trainer.Frontier.Rewards.FirstOrDefault(reward => reward.Id == rewardId)?.Name ?? rewardId);
        await InSession(LoadPokedexCatalog()); SetStatus("Frontier reward received. BP and purchase history saved.");
    }
    private async Task StartParadoxHunt(string permitId)
    {
        EncounterDto encounter;
        var refresh = true;
        try { encounter = await InSession(plugin.Api.StartParadoxResearch(permitId)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0;
        SetStatus(encounter.Pokemon.Shiny ? $"A shiny {encounter.Pokemon.Species} appeared!" : $"Research encounter: {encounter.Pokemon.Species}. Your permit remains permanent.");
    }
}
