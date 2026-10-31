using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly (string Id, string Label, string Description)[] BuildStats =
    [
        ("hp", "HP", "How much damage this Pokémon can take before fainting."),
        ("atk", "Attack", "Strength of physical attacks."),
        ("def", "Defense", "Resistance to physical attacks."),
        ("spa", "Sp. Atk", "Strength of special attacks."),
        ("spd", "Sp. Def", "Resistance to special attacks."),
        ("spe", "Speed", "Usually determines who moves first within the same priority bracket.")
    ];
    private readonly ConcurrentDictionary<string, (string Stamp, PokemonBuildDto Build)> pokemonBuilds = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> buildRetryAt = new();
    private readonly Dictionary<string, string> moveLicenseSearches = new();
    private readonly Dictionary<string, int> moveLicensePages = new();
    private readonly Dictionary<string, string> teraSelections = new();
    private int evStatIndex;
    private int evTrainingQuantity = 1;
    private string shopSearch = string.Empty;
    private int shopPage;
    private int shopBuyQuantity = 1;

    private PokemonBuildDto? GetPokemonBuild(PokemonDto pokemon)
    {
        var stamp = JsonSerializer.Serialize(pokemon);
        if (pokemonBuilds.TryGetValue(pokemon.Id, out var cached) && cached.Stamp == stamp)
            return cached.Build;
        if (!busy && (!buildRetryAt.TryGetValue(pokemon.Id, out var retryAt) || DateTimeOffset.UtcNow >= retryAt))
        {
            buildRetryAt[pokemon.Id] = DateTimeOffset.UtcNow.AddSeconds(15);
            Run(async () =>
            {
                var build = await InSession(plugin.Api.GetPokemonBuild(pokemon.Id, disposeCancellation.Token));
                pokemonBuilds[pokemon.Id] = (stamp, build);
                buildRetryAt.TryRemove(pokemon.Id, out _);
                SetStatus("Pokémon build loaded.");
            }, "Loading Pokémon build...");
        }
        return null;
    }

    private void DrawTeamBuildingSummary(PokemonDto pokemon)
    {
        var build = GetPokemonBuild(pokemon);
        if (build is null)
        {
            ImGui.TextDisabled("Loading stats, abilities and compatible moves...");
            return;
        }
        if (ImGui.TreeNode($"Stats & build summary##Summary{pokemon.Id}"))
        {
            ImGui.TextColored(PokemonBlue, string.Join(" / ", build.Types));
            ImGui.TextColored(TypeColor(build.TeraType), $"Tera type: {build.TeraType}");
            ImGui.Text($"Nature effects: {build.EffectiveNature}");
            ImGui.TextDisabled(build.NatureEffect);
            if (!string.IsNullOrWhiteSpace(pokemon.MintNature))
                ImGui.TextDisabled($"Original nature: {pokemon.Nature}. Mint effects remain through evolution.");
            ImGui.Text($"Ability: {pokemon.Ability}");
            ImGui.TextWrapped(build.AbilityDescription);
            if (ImGui.BeginTable($"BuildStats{pokemon.Id}", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableSetupColumn("Stat");
                ImGui.TableSetupColumn($"Lv. {pokemon.Level}");
                ImGui.TableSetupColumn("Lv. 50");
                ImGui.TableSetupColumn("IV / 31");
                ImGui.TableSetupColumn("EV / 252");
                ImGui.TableHeadersRow();
                foreach (var stat in BuildStats)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(stat.Label);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(stat.Description);
                    ImGui.TableNextColumn(); ImGui.Text(build.Stats.GetValueOrDefault(stat.Id).ToString());
                    ImGui.TableNextColumn(); ImGui.Text(build.Level50Stats.GetValueOrDefault(stat.Id).ToString());
                    ImGui.TableNextColumn(); ImGui.Text(pokemon.Ivs.GetValueOrDefault(stat.Id).ToString());
                    ImGui.TableNextColumn(); ImGui.Text(pokemon.Evs.GetValueOrDefault(stat.Id).ToString());
                }
                ImGui.EndTable();
            }
            ImGui.TextDisabled("Stats before held-item and battle effects. PvP level depends on the format.");
            ImGui.TextWrapped("IVs are individual values. EVs are training points: every 4 EVs contribute to a stat before level scaling. EV limits are 252 per stat and 510 total.");
            ImGui.TreePop();
        }

        ImGui.TextColored(PokemonBlue, "KNOWN MOVES");
        foreach (var move in build.Moves)
        {
            ImGui.TextWrapped($"{move.Name}  |  {move.Type} / {move.Category}  |  Power {move.Power}  |  Accuracy {move.Accuracy?.ToString() ?? "Always"}  |  Base PP {move.Pp}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(move.Description);
        }
    }

    private void DrawTeraTraining(PokemonDto pokemon)
    {
        var build = GetPokemonBuild(pokemon);
        var current = build?.TeraType ?? pokemon.TeraType;
        ImGui.PushStyleColor(ImGuiCol.Text, TypeColor(current));
        var open = ImGui.TreeNode($"Tera type: {current}##Tera{pokemon.Id}");
        ImGui.PopStyleColor();
        if (!open) return;
        if (build is null)
        {
            ImGui.TextDisabled("Loading legal Tera types...");
            ImGui.TreePop();
            return;
        }
        if (build.AllowedTeraTypes.Count <= 1)
        {
            ImGui.TextWrapped($"This Pokémon requires {current} Tera. Its species or held form determines the type; no shard is needed.");
            ImGui.TreePop();
            return;
        }
        var selected = teraSelections.GetValueOrDefault(pokemon.Id, current);
        if (!build.AllowedTeraTypes.Contains(selected)) selected = current;
        ImGui.TextWrapped("One Tera Shard changes this Pokémon's type for formats that allow Terastallization. It does not change the Pokémon's normal typing.");
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo($"New type##TeraType{pokemon.Id}", selected))
        {
            foreach (var type in build.AllowedTeraTypes)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, TypeColor(type));
                if (ImGui.Selectable(type, selected == type)) selected = type;
                ImGui.PopStyleColor();
            }
            ImGui.EndCombo();
        }
        teraSelections[pokemon.Id] = selected;
        ImGui.TextColored(TypeColor(current), $"Current: {current}");
        ImGui.SameLine(); ImGui.TextColored(TypeColor(selected), $"New: {selected}");
        ImGui.TextDisabled($"Tera Shards owned: {InventoryCount("terashard")}  |  Mart price: ¥75,000");
        ImGui.BeginDisabled(selected == current || InventoryCount("terashard") < 1);
        if (DrawAccentButton($"Change Tera Type##ChangeTera{pokemon.Id}", Vector2.Zero, TypeColor(selected)))
            ImGui.OpenPopup($"Confirm Tera change##{pokemon.Id}");
        ImGui.EndDisabled();
        if (BeginConfirmationModal($"Confirm Tera change##{pokemon.Id}"))
        {
            ImGui.TextWrapped($"Change {pokemon.Nickname}'s Tera type from {current} to {selected}? This consumes one Tera Shard.");
            if (DrawConfirmationActions("Confirm change", PokemonGreen, CollectionLocked))
            {
                var target = selected;
                Run(async () =>
                {
                    trainer = await InSession(plugin.Api.UseTeraShard(pokemon.Id, target, disposeCancellation.Token));
                    SetStatus($"Tera type changed to {target}.");
                }, "Changing Tera type...");
                ImGui.CloseCurrentPopup();
            }
            EndConfirmationModal();
        }
        ImGui.TreePop();
    }

    private void DrawEvTrainingManual(PokemonDto pokemon)
    {
        if (!ImGui.TreeNode($"Manual EV training & reset##EV{pokemon.Id}")) return;
        var total = pokemon.Evs.Values.Sum();
        ImGui.TextColored(PokemonBlue, $"EVs: {total} / 510   |   Available: {510 - total}");
        DrawProgressBar(total / 510f, new Vector2(-1, 18), $"{total} / 510 EVs", PokemonBlue);
        ImGui.TextWrapped(string.Join("   ", BuildStats.Select(stat => $"{stat.Label}: {pokemon.Evs.GetValueOrDefault(stat.Id)}")));
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo($"Stat##EVStat{pokemon.Id}", BuildStats[evStatIndex].Label))
        {
            for (var index = 0; index < BuildStats.Length; index++)
                if (ImGui.Selectable(BuildStats[index].Label, evStatIndex == index)) evStatIndex = index;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt($"Quantity##EVQuantity{pokemon.Id}", ref evTrainingQuantity);
        evTrainingQuantity = Math.Clamp(evTrainingQuantity, 1, 99);
        var selectedStat = BuildStats[evStatIndex].Id;
        var items = trainingItems.Where(item => item.TrainingEffect is { Kind: "ev" } && item.TrainingEffect.Stat == selectedStat && InventoryCount(item.Id) > 0).ToList();
        if (items.Count == 0) ImGui.TextDisabled("Buy vitamins, feathers or EV-reset berries in the Poké Mart.");
        foreach (var item in items)
        {
            var amount = item.TrainingEffect!.Amount;
            var available = amount > 0 ? Math.Min(252 - pokemon.Evs.GetValueOrDefault(selectedStat), 510 - total) : pokemon.Evs.GetValueOrDefault(selectedStat);
            var maximum = (int)Math.Ceiling(available / (double)Math.Abs(amount));
            if (amount < 0)
            {
                var friendship = pokemon.EvolutionProgress?.Friendship ?? 50;
                var evs = available;
                maximum = 0;
                while (maximum < 99 && (friendship < 255 || evs > 0))
                {
                    friendship = Math.Min(255, friendship + (friendship < 100 ? 10 : friendship < 200 ? 5 : 1));
                    evs = Math.Max(0, evs + amount);
                    maximum++;
                }
            }
            var canUse = maximum > 0 && evTrainingQuantity <= maximum && InventoryCount(item.Id) >= evTrainingQuantity;
            ImGui.BeginDisabled(!canUse);
            var quantity = evTrainingQuantity;
            var change = Math.Sign(amount) * Math.Min(Math.Max(0, available), quantity * Math.Abs(amount));
            if (DrawAccentButton($"Use {quantity} x {item.Name} ({change:+0;-0;0} EVs{(amount < 0 ? ", + friendship" : "")})##EVUse{pokemon.Id}{item.Id}", Vector2.Zero, amount > 0 ? PokemonGreen : PokemonOrange))
                Run(() => UseTrainingItem(pokemon.Id, item.Id, null, quantity), "Applying EV training...");
            ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.TextDisabled($"Owned: {InventoryCount(item.Id)}");
        }
        if (InventoryCount("freshstartmochi") > 0)
        {
            ImGui.BeginDisabled(total == 0);
            if (DrawAccentButton($"Reset all EVs##ResetEV{pokemon.Id}", Vector2.Zero, PokemonOrange))
                ImGui.OpenPopup($"Confirm EV reset##{pokemon.Id}");
            ImGui.EndDisabled();
            if (BeginConfirmationModal($"Confirm EV reset##{pokemon.Id}"))
            {
                ImGui.TextWrapped($"Reset all {total} EVs on {pokemon.Nickname}? This uses one Fresh-Start Mochi.");
                if (DrawConfirmationActions("Confirm reset", PokemonRed, CollectionLocked))
                {
                    Run(() => UseTrainingItem(pokemon.Id, "freshstartmochi", null), "Resetting EVs...");
                    ImGui.CloseCurrentPopup();
                }
                EndConfirmationModal();
            }
        }
        ImGui.TreePop();
    }

    private void DrawNatureAndAbilityTraining(PokemonDto pokemon)
    {
        if (!ImGui.TreeNode($"Nature Mints & abilities##NatureAbility{pokemon.Id}")) return;
        var build = GetPokemonBuild(pokemon);
        if (build is null)
        {
            ImGui.TextDisabled("Waiting for build information.");
            ImGui.TreePop();
            return;
        }
        ImGui.TextWrapped($"Nature effects: {build.EffectiveNature} ({build.NatureEffect})");
        var mints = trainingItems.Where(item => item.TrainingEffect is { Kind: "mint" } && InventoryCount(item.Id) > 0).ToList();
        if (mints.Count == 0) ImGui.TextDisabled("Exchange BP for a Nature Mint at the Battle Frontier to change stat effects.");
        foreach (var mint in mints)
        {
            ImGui.BeginDisabled(mint.TrainingEffect!.Nature == build.EffectiveNature || mint.TrainingEffect.Nature == "Serious" && build.NatureEffect == "Neutral stat effects");
            if (DrawAccentButton($"Use {mint.Name}##Mint{pokemon.Id}{mint.Id}", Vector2.Zero, PokemonBlue))
                Run(() => UseTrainingItem(pokemon.Id, mint.Id, null), "Applying Mint...");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(mint.Description);
        }
        ImGui.Spacing();
        ImGui.TextWrapped($"Current ability: {pokemon.Ability}");
        ImGui.TextWrapped(build.AbilityDescription);
        foreach (var ability in build.Abilities)
        {
            ImGui.TextDisabled($"{ability.Name}{(ability.Hidden ? " (Hidden)" : "")}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(ability.Description);
        }
        var otherNormal = build.Abilities.FirstOrDefault(ability => !ability.Hidden && ability.Name != pokemon.Ability)?.Name;
        ImGui.BeginDisabled(!build.CanUseCapsule || InventoryCount("abilitycapsule") <= 0);
        if (DrawAccentButton($"Capsule: {otherNormal ?? "Unavailable"}##Capsule{pokemon.Id}", Vector2.Zero, PokemonGreen))
            Run(() => UseTrainingItem(pokemon.Id, "abilitycapsule", null), "Switching normal ability...");
        ImGui.EndDisabled();
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(build.PatchAbility) || build.PatchAbility == pokemon.Ability || InventoryCount("abilitypatch") <= 0);
        if (DrawAccentButton($"Patch: {build.PatchAbility ?? "Unavailable"}##Patch{pokemon.Id}", Vector2.Zero, PokemonYellow))
            Run(() => UseTrainingItem(pokemon.Id, "abilitypatch", null), "Applying Ability Patch...");
        ImGui.EndDisabled();
        ImGui.TextDisabled($"Capsules: {InventoryCount("abilitycapsule")}   |   Patches: {InventoryCount("abilitypatch")}");
        ImGui.TreePop();
    }

    private void DrawMoveLicenses(PokemonDto pokemon)
    {
        if (!ImGui.TreeNode($"TMs & Move Tutor##Licenses{pokemon.Id}")) return;
        var build = GetPokemonBuild(pokemon);
        if (build is null)
        {
            ImGui.TextDisabled("Waiting for compatible moves.");
            ImGui.TreePop();
            return;
        }
        var search = moveLicenseSearches.GetValueOrDefault(pokemon.Id, string.Empty);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint($"##MoveSearch{pokemon.Id}", "Search compatible moves...", ref search, 80))
        {
            moveLicenseSearches[pokemon.Id] = search;
            moveLicensePages[pokemon.Id] = 0;
        }
        ImGui.TextWrapped("Reusable licenses are purchased once per trainer. Tutors also cover supported egg moves. Choose a known move to replace when you have four moves. Showdown checks the complete resulting build before teaching.");
        var compatible = build.TeachableItemIds.ToHashSet();
        var items = itemCatalog.Where(item => compatible.Contains(item.Id) && (string.IsNullOrWhiteSpace(search) || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => InventoryCount(item.Id) > 0).ThenBy(item => item.Name).ToList();
        var page = moveLicensePages.GetValueOrDefault(pokemon.Id);
        DrawPager($"MoveLicenses{pokemon.Id}", ref page, items.Count, 8);
        moveLicensePages[pokemon.Id] = page;
        foreach (var item in items.Skip(page * 8).Take(8))
        {
            var priceLabel = item.BpOnly ? $"{item.BpPrice:N0} BP" : $"¥ {item.Price:N0}";
            if (!ImGui.TreeNode($"{item.Name} ({(InventoryCount(item.Id) > 0 ? "Owned" : priceLabel)})##Teach{pokemon.Id}{item.Id}")) continue;
            ImGui.TextWrapped(item.Description);
            if (!string.IsNullOrWhiteSpace(item.AcquisitionNote)) ImGui.TextWrapped(item.AcquisitionNote);
            if (InventoryCount(item.Id) <= 0)
            {
                if (!item.BpOnly)
                {
                    ImGui.BeginDisabled(trainer is null || trainer.Currency < item.Price);
                    if (DrawAccentButton($"Buy for ¥ {item.Price:N0}##LicenseBuy{pokemon.Id}{item.Id}", Vector2.Zero, trainer is not null && trainer.Currency >= item.Price ? PokemonGreen : PokemonRed))
                        Run(() => BuyItem(item.Id), "Buying move license...");
                    ImGui.EndDisabled();
                }
                if (item.BpPrice is { } bpPrice)
                {
                    ImGui.BeginDisabled(trainer is null || trainer.Frontier.Balance < bpPrice);
                    if (DrawAccentButton($"Buy for {bpPrice:N0} BP##LicenseBP{pokemon.Id}{item.Id}", Vector2.Zero, trainer is not null && trainer.Frontier.Balance >= bpPrice ? PokemonBlue : PokemonRed))
                        Run(() => BuyFrontierReward($"item-{item.Id}"), "Buying move license with BP...");
                    ImGui.EndDisabled();
                }
            }
            else if (pokemon.Moves.Count < 4)
            {
                if (DrawAccentButton($"Teach {item.Move}##TeachFree{pokemon.Id}{item.Id}", Vector2.Zero, PokemonBlue))
                    Run(() => TeachMove(pokemon.Id, item.Id, null), $"Teaching {item.Move}...");
            }
            else
            {
                foreach (var knownMove in pokemon.Moves)
                    if (DrawAccentSmallButton($"Replace {knownMove}##TeachReplace{pokemon.Id}{item.Id}{knownMove}", PokemonOrange))
                        Run(() => TeachMove(pokemon.Id, item.Id, knownMove), $"Teaching {item.Move}...");
            }
            ImGui.TreePop();
        }
        if (items.Count == 0) ImGui.TextDisabled("No compatible unlearned moves match your search.");
        ImGui.TreePop();
    }

    private async Task TeachMove(string pokemonId, string itemId, string? forgetMove)
    {
        trainer = await InSession(plugin.Api.TeachMove(pokemonId, itemId, forgetMove, disposeCancellation.Token));
        SetStatus($"Learned {itemCatalog.FirstOrDefault(item => item.Id == itemId)?.Move ?? "the selected move"}. The license remains available.");
    }
}
