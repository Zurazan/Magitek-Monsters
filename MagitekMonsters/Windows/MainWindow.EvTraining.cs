using System;
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
    private sealed class EvTrainingDraft(string owner, PokemonDto pokemon)
    {
        public string Owner = owner;
        public string PokemonId = pokemon.Id;
        public Dictionary<string, int> Target = BuildStats.ToDictionary(stat => stat.Id, stat => pokemon.Evs.GetValueOrDefault(stat.Id));
        public bool UseResetItem;
        public int Preset;
        public EvTrainingPlanDto? Plan;
        public string Stamp = string.Empty;
    }
    private sealed record EvTrainingConfirmation(string Owner, string PokemonId, Dictionary<string, int> Target,
        bool UseResetItem, EvTrainingPlanDto Plan, string Stamp, string RequestId);
    private EvTrainingDraft? evTrainingDraft;
    private EvTrainingConfirmation? evTrainingConfirmation;

    private void ResetEvTrainingPlanner() { evTrainingDraft = null; evTrainingConfirmation = null; }

    private EvTrainingDraft PrepareEvTraining(PokemonDto pokemon)
    {
        if (evTrainingDraft?.Owner != trainer!.User.Id || evTrainingDraft.PokemonId != pokemon.Id)
        {
            evTrainingDraft = new(trainer.User.Id, pokemon);
            evTrainingConfirmation = null;
        }
        return evTrainingDraft;
    }

    private string EvTrainingStamp(PokemonDto pokemon) => JsonSerializer.Serialize(new {
        Pokemon = pokemon, Inventory = trainer!.Inventory.OrderBy(item => item.ItemId, StringComparer.Ordinal) });

    private void DrawEvTraining(PokemonDto pokemon)
    {
        var draft = PrepareEvTraining(pokemon);
        if (draft.Plan is not null && draft.Stamp != EvTrainingStamp(pokemon)) draft.Plan = null;
        ImGui.TextColored(CompanionAccent, "EV TRAINING PLANNER");
        ImGui.TextWrapped("Choose a target spread. Preview the exact items and resulting stats before applying it.");
        string[] presets = ["Current spread", "Fast physical attacker", "Fast special attacker", "Bulky physical attacker", "Custom spread"];
        ImGui.TextUnformatted("Starting spread");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##EvPlanPreset", ref draft.Preset, presets, presets.Length))
        {
            int[] values = draft.Preset switch {
                1 => [4, 252, 0, 0, 0, 252], 2 => [4, 0, 0, 252, 0, 252], 3 => [252, 252, 0, 0, 0, 4],
                _ => BuildStats.Select(stat => draft.Preset == 0 ? pokemon.Evs.GetValueOrDefault(stat.Id) : draft.Target[stat.Id]).ToArray() };
            for (var i = 0; i < BuildStats.Length; i++) draft.Target[BuildStats[i].Id] = values[i];
            draft.Plan = null;
        }
        var columns = ImGui.GetContentRegionAvail().X >= 660 * CompanionScale;
        if (columns && ImGui.BeginTable("EvPlanColumns", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); DrawEvTrainingTargets(pokemon, draft);
            ImGui.TableNextColumn(); DrawEvTrainingItems(draft.Plan);
            ImGui.EndTable();
        }
        else
        {
            DrawEvTrainingTargets(pokemon, draft);
            ImGui.Spacing(); DrawEvTrainingItems(draft.Plan);
        }
        var total = draft.Target.Values.Sum();
        ImGui.Spacing();
        ImGui.TextColored(total > 510 ? PokemonRed : CompanionMuted, $"Target: {total}/510 EVs / 252 maximum per stat");
        if (ImGui.Checkbox("Use Fresh-Start Mochi", ref draft.UseResetItem)) draft.Plan = null;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("The preview includes one Mochi and the items needed to rebuild the entire target spread.");
        ImGui.BeginDisabled(busy || total > 510);
        if (DrawAccentSmallButton(draft.Plan is null ? "Preview training" : "Refresh preview", PokemonBlue))
            Run(() => PreviewEvTraining(pokemon), "Checking EV training...");
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(busy || draft.Plan?.CanApply != true || total > 510);
        if (DrawAccentSmallButton("Review training", PokemonGreen))
        {
            evTrainingConfirmation = new(draft.Owner, pokemon.Id, new(draft.Target), draft.UseResetItem,
                draft.Plan!, draft.Stamp, Guid.NewGuid().ToString());
            ImGui.OpenPopup("Apply EV training?");
        }
        ImGui.EndDisabled();
        ImGui.Spacing();
        DrawEvTrainingManual(pokemon);
    }

    private void DrawEvTrainingTargets(PokemonDto pokemon, EvTrainingDraft draft)
    {
        if (!ImGui.BeginTable("EvPlanTargets", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("EVs", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Target EVs", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Stats", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableHeadersRow();
        foreach (var stat in BuildStats)
        {
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(stat.Label);
            ImGui.TableNextColumn(); ImGui.TextUnformatted((draft.Plan?.CurrentEvs ?? pokemon.Evs).GetValueOrDefault(stat.Id).ToString());
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            var value = draft.Target[stat.Id];
            if (ImGui.InputInt($"##EvPlanTarget{stat.Id}", ref value, 0, 0))
            {
                draft.Target[stat.Id] = Math.Clamp(value, 0, 252); draft.Preset = 4; draft.Plan = null;
            }
            ImGui.TableNextColumn();
            if (draft.Plan is not { } plan) ImGui.TextDisabled("Preview");
            else
            {
                var before = plan.CurrentStats.GetValueOrDefault(stat.Id); var after = plan.TargetStats.GetValueOrDefault(stat.Id);
                ImGui.TextColored(before == after ? CompanionMuted : after > before ? PokemonGreen : PokemonOrange,
                    before == after ? before.ToString() : $"{before} > {after}");
            }
        }
        ImGui.EndTable();
        ImGui.TextDisabled($"Stats at Lv. {draft.Plan?.Level ?? pokemon.Level}");
    }

    private static void DrawEvTrainingItems(EvTrainingPlanDto? plan)
    {
        ImGui.TextColored(CompanionAccent, "ITEMS FROM YOUR BAG");
        if (plan is null) { ImGui.TextWrapped("Preview the plan to check item costs and shortages. No items are used by the preview."); return; }
        if (plan.Items.Count == 0) { ImGui.TextWrapped("This Pokémon already has this spread. No items needed."); return; }
        if (ImGui.BeginTable("EvPlanItems", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableHeadersRow();
            foreach (var item in plan.Items)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextWrapped(item.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Quantity.ToString());
                ImGui.TableNextColumn(); ImGui.TextColored(item.Missing > 0 ? PokemonRed : CompanionMuted, item.Owned.ToString());
            }
            ImGui.EndTable();
        }
        if (plan.CanApply) ImGui.TextColored(PokemonGreen, "All required items are owned.");
        else ImGui.TextWrapped("Missing: " + string.Join(", ", plan.Items.Where(item => item.Missing > 0).Select(item => $"{item.Missing} {item.Name}")));
        if (plan.FriendshipAfter != plan.FriendshipBefore) ImGui.TextDisabled($"Berry friendship: {plan.FriendshipBefore} > {plan.FriendshipAfter}");
    }

    private async Task PreviewEvTraining(PokemonDto pokemon)
    {
        var draft = PrepareEvTraining(pokemon);
        var stamp = EvTrainingStamp(pokemon);
        var plan = await InSession(plugin.Api.PreviewEvTraining(pokemon.Id, new(draft.Target), draft.UseResetItem, disposeCancellation.Token));
        if (!ReferenceEquals(draft, evTrainingDraft)) return;
        draft.Plan = plan; draft.Stamp = stamp;
        SetStatus(plan.CanApply ? "Training preview ready. No items used." : plan.Items.Count == 0 ? "This Pokémon already has this spread." : "Training preview ready. Some items are missing.");
    }

    private void DrawEvTrainingConfirmation(PokemonDto pokemon)
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(460 * CompanionScale, ImGui.GetMainViewport().WorkSize.X * 0.9f), 0), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Apply EV training?", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        var command = evTrainingConfirmation;
        if (command is null || command.Owner != trainer?.User.Id || command.PokemonId != pokemon.Id)
        { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.TextWrapped(string.Join(" / ", BuildStats.Where(stat => command.Target[stat.Id] > 0).Select(stat => $"{command.Target[stat.Id]} {stat.Label}")));
        ImGui.TextWrapped($"Use {command.Plan.Items.Sum(item => item.Quantity)} items from your Bag to train {pokemon.Nickname}?");
        ImGui.TextWrapped("This uses exactly the items shown in the preview.");
        var changed = command.Stamp != EvTrainingStamp(pokemon);
        var locked = CollectionLocked || pokemon.FusionHostId.Length > 0;
        if (changed) ImGui.TextColored(PokemonOrange, "Trainer data changed. Go back and preview again.");
        if (locked) ImGui.TextWrapped("Finish your battle or run before applying training.");
        ImGui.BeginDisabled(locked || changed);
        if (DrawAccentSmallButton("Confirm training", PokemonGreen))
        {
            Run(() => ApplyEvTraining(command), "Applying EV training...");
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("Go back")) { evTrainingConfirmation = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private async Task ApplyEvTraining(EvTrainingConfirmation command)
    {
        if (trainer?.User.Id != command.Owner) return;
        if (evTrainingDraft is not null) evTrainingDraft.Plan = null;
        evTrainingConfirmation = null;
        trainer = await InSession(plugin.Api.ApplyEvTraining(command.PokemonId, new(command.Target), command.UseResetItem,
            command.Plan.PreviewHash, command.RequestId));
        var pokemon = trainer.Party.Concat(trainer.Storage).FirstOrDefault(entry => entry.Id == command.PokemonId);
        RecordRecentAction("ev-plan:" + command.RequestId, "training", "Applied EV training to " + (pokemon?.Nickname ?? "Pokémon"),
            string.Join(" / ", BuildStats.Where(stat => command.Target[stat.Id] > 0).Select(stat => $"{command.Target[stat.Id]} {stat.Label}")));
        evTrainingDraft = null;
        SetStatus("EV training applied.");
    }
}
