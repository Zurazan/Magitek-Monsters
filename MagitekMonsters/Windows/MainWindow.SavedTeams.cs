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
    private enum SavedTeamEditKind { New, Rename, Replace, Delete }
    private sealed record SavedTeamEdit(string Owner, string Id, SavedTeamEditKind Kind, string Name, string[] PokemonIds);
    private SavedTeamEdit? savedTeamEdit;
    private string savedTeamEditName = string.Empty;
    private string savedTeamLibraryOwner = string.Empty;
    private bool requestSavedTeamLibrary;
    private bool closeSavedTeamLibrary;
    private bool requestSavedTeamEdit;
    private bool savedTeamEditComplete;

    private void DrawSavedTeams()
    {
        if (trainer is null) return;
        var views = SavedTeams.Resolve(trainer);
        if (DrawAccentSmallButton($"Saved teams ({views.Count})", CompanionAccent))
        {
            savedTeamLibraryOwner = trainer.User.Id;
            closeSavedTeamLibrary = requestSavedTeamEdit = savedTeamEditComplete = false;
            savedTeamEdit = null;
            requestSavedTeamLibrary = true;
        }
        ImGui.SameLine();
        var current = views.FirstOrDefault(entry => entry.Current);
        DrawClippedText(current?.Preset.Name ?? "Custom party", ImGui.GetContentRegionAvail().X, current is null ? CompanionMuted : PokemonGreen);
        ImGui.Spacing();
        DrawSavedTeamLibrary(views);
    }

    private void DrawSavedTeamLibrary(IReadOnlyList<SavedTeamView> views)
    {
        if (requestSavedTeamLibrary)
        {
            if (savedTeamLibraryOwner == trainer?.User.Id) ImGui.OpenPopup("Saved teams");
            requestSavedTeamLibrary = false;
        }
        if (!ImGui.IsPopupOpen("Saved teams")) return;
        var scale = CompanionScale;
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(840 * scale, work.X * 0.94f), Math.Min(690 * scale, work.Y * 0.9f)), ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal("Saved teams", ref open, ImGuiWindowFlags.None)) return;
        if (savedTeamLibraryOwner != trainer?.User.Id || closeSavedTeamLibrary)
        {
            closeSavedTeamLibrary = false;
            ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return;
        }
        ImGui.TextColored(CompanionAccent, $"SAVED TEAMS / {views.Count}/{SavedTeams.MaximumTeams}");
        ImGui.TextWrapped("Load a lineup from your party and PC. Stats, moves and held items stay as they are.");
        ImGui.BeginDisabled(busy || !SavedTeams.CanSaveParty(trainer!) || views.Count >= SavedTeams.MaximumTeams);
        if (DrawAccentSmallButton("Save current party", PokemonGreen)) BeginSavedTeamEdit(SavedTeamEditKind.New);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(views.Count >= SavedTeams.MaximumTeams
            ? "All 12 slots are used. Replace or delete a saved team to make room." : "Save your current party in its current order.");
        ImGui.SameLine();
        ImGui.BeginDisabled(busy || trainer!.Party.Count == 0);
        if (DrawAccentSmallButton("Copy party summary", PokemonBlue)) QueueTeamExport(trainer.Party.OrderBy(entry => entry.PartySlot).Select(entry => entry.Id));
        ImGui.EndDisabled();
        if (CollectionLocked) ImGui.TextWrapped(busy ? status : "Finish your battle, run, raid or matchmaking before switching teams.");
        var footer = ImGui.GetFrameHeight() + ImGui.GetTextLineHeight() * 2 + 22 * scale;
        if (ImGui.BeginChild("SavedTeamLibraryList", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - footer)), false))
        {
            if (views.Count == 0)
            {
                ImGui.TextColored(CompanionAccent, "No saved teams yet");
                ImGui.TextWrapped("Arrange your party, then choose Save current party. You can keep up to 12 lineups.");
            }
            var layout = SavedTeams.Layout(ImGui.GetContentRegionAvail().X, scale, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y);
            if (views.Count > 0 && ImGui.BeginTable("SavedTeamCards", layout.Columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var view in views)
                {
                    ImGui.TableNextColumn();
                    DrawSavedTeamCard(view);
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        if (statusIsError) ImGui.TextWrapped(status);
        else ImGui.TextDisabled("First slot leads. Empty slots stay empty. Loading never heals your team.");
        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
        DrawTeamAnalysis(inLibrary: true);
        DrawSavedTeamEdit();
        ImGui.EndPopup();
    }

    private void DrawSavedTeamCard(SavedTeamView view)
    {
        var scale = CompanionScale;
        var layout = SavedTeams.Layout(ImGui.GetContentRegionAvail().X, scale, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y);
        ImGui.PushID(view.Preset.Id);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, view.Current ? new Vector4(0.13f, 0.19f, 0.16f, 1) : CompanionSurface);
        ImGui.PushStyleColor(ImGuiCol.Border, view.Current ? PokemonGreen with { W = 0.6f } : new Vector4(0.25f, 0.25f, 0.28f, 1));
        if (ImGui.BeginChild("SavedTeamCard", new Vector2(0, layout.Height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var moreWidth = ImGui.CalcTextSize("More").X + ImGui.GetStyle().FramePadding.X * 2;
            DrawClippedText(view.Preset.Name, Math.Max(1, ImGui.GetContentRegionAvail().X - moreWidth - 8 * scale), CompanionAccent);
            ImGui.SameLine(0, 8 * scale);
            if (ImGui.SmallButton("More")) ImGui.OpenPopup("SavedTeamActions");
            if (ImGui.BeginPopup("SavedTeamActions"))
            {
                if (ImGui.MenuItem("Team analysis")) OpenTeamAnalysis(view.Preset.Id);
                ImGui.Separator();
                ImGui.BeginDisabled(busy || !view.Complete);
                if (ImGui.MenuItem("Copy team summary")) QueueTeamExport(view.Preset.PokemonIds);
                if (ImGui.MenuItem("Rename team")) BeginSavedTeamEdit(SavedTeamEditKind.Rename, view);
                ImGui.EndDisabled();
                ImGui.BeginDisabled(busy || !SavedTeams.CanSaveParty(trainer!));
                if (ImGui.MenuItem("Replace with current party")) BeginSavedTeamEdit(SavedTeamEditKind.Replace, view);
                ImGui.EndDisabled();
                ImGui.Separator();
                ImGui.BeginDisabled(busy);
                if (ImGui.MenuItem("Delete saved team")) BeginSavedTeamEdit(SavedTeamEditKind.Delete, view);
                ImGui.EndDisabled();
                ImGui.EndPopup();
            }
            DrawClippedText(view.Status, ImGui.GetContentRegionAvail().X, view.Current ? PokemonGreen : view.Problem is null ? CompanionMuted : PokemonOrange);
            ImGui.Spacing();
            DrawSavedTeamMembers(view.Members);
            ImGui.Spacing();
            ImGui.BeginDisabled(CollectionLocked || !view.CanLoad);
            if (DrawAccentButton(view.Current ? "Current party" : "Load team", new Vector2(-1, 30 * scale), view.Current ? CompanionMuted : PokemonGreen))
            {
                var owner = trainer!.User.Id;
                Run(() => LoadSavedTeam(owner, view.Preset.Id), "Loading saved team...");
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(view.Problem
                ?? (view.Current ? "Your party already matches this team, including its order." : busy ? "Wait for the current action to finish." : CollectionLocked
                    ? "Finish your battle, run, raid or matchmaking before switching teams." : "Load these owned Pokémon in this order."));
        }
        ImGui.EndChild(); ImGui.PopStyleColor(2); ImGui.PopStyleVar(); ImGui.PopID();
    }

    private void DrawSavedTeamMembers(IReadOnlyList<SavedTeamMember> members)
    {
        var scale = CompanionScale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2 * scale));
        if (ImGui.BeginTable("SavedTeamMembers", 6, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var member in members)
            {
                ImGui.TableNextColumn(); ImGui.BeginGroup();
                var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
                DrawClippedText(member.Slot == 1 ? "Lead" : member.Slot.ToString(), width, CompanionMuted);
                var image = Math.Max(1, Math.Min(44 * scale, width));
                var origin = ImGui.GetCursorScreenPos();
                if (member.Pokemon is { } pokemon) DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(image));
                else
                {
                    ImGui.Dummy(new Vector2(image));
                    var draw = ImGui.GetWindowDrawList();
                    draw.AddRectFilled(origin, origin + new Vector2(image), ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1)), 3 * scale);
                    var mark = member.State == SavedTeamMemberState.Missing ? "?" : "-";
                    draw.AddText(origin + (new Vector2(image) - ImGui.CalcTextSize(mark)) / 2,
                        ImGui.GetColorU32(member.State == SavedTeamMemberState.Missing ? PokemonOrange : CompanionMuted), mark);
                }
                var label = member.State switch { SavedTeamMemberState.Empty => "Empty", SavedTeamMemberState.Missing => "Missing", SavedTeamMemberState.FusionLocked => "Fused", _ => $"Lv{member.Pokemon!.Level}" };
                DrawClippedText(label, width, member.State is SavedTeamMemberState.Missing or SavedTeamMemberState.FusionLocked ? PokemonOrange : CompanionMuted);
                ImGui.EndGroup();
                if (ImGui.IsItemHovered())
                {
                    if (member.Pokemon is { } owned)
                    {
                        var location = owned.PartySlot is int slot ? $"Party slot {slot}" : $"Box {owned.BoxNumber}, slot {owned.BoxSlot}";
                        ImGui.SetTooltip($"{owned.Nickname} / {owned.DisplaySpecies} / Lv. {owned.Level}\n{location}{(member.State == SavedTeamMemberState.FusionLocked ? "\nUnfuse this Pokémon before loading the team." : "")}");
                    }
                    else ImGui.SetTooltip(member.State == SavedTeamMemberState.Empty ? "This team leaves this party slot empty." : "This Pokémon is missing or no longer belongs to you. Replace the saved team to repair it.");
                }
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void BeginSavedTeamEdit(SavedTeamEditKind kind, SavedTeamView? view = null)
    {
        if (trainer is null) return;
        var ids = kind is SavedTeamEditKind.New or SavedTeamEditKind.Replace
            ? trainer.Party.OrderBy(entry => entry.PartySlot).Select(entry => entry.Id).ToArray() : view!.Preset.PokemonIds.ToArray();
        savedTeamEditName = view?.Preset.Name ?? $"Team {trainer.Collection.TeamPresets.Count + 1}";
        savedTeamEdit = new(trainer.User.Id, view?.Preset.Id ?? Guid.NewGuid().ToString(), kind, savedTeamEditName, ids);
        savedTeamEditComplete = false;
        requestSavedTeamEdit = true;
    }

    private void DrawSavedTeamEdit()
    {
        if (requestSavedTeamEdit) { ImGui.OpenPopup("Edit saved team"); requestSavedTeamEdit = false; }
        if (!ImGui.IsPopupOpen("Edit saved team")) return;
        var scale = CompanionScale;
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(540 * scale, work.X * 0.92f), Math.Min(490 * scale, work.Y * 0.85f)), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Edit saved team", ImGuiWindowFlags.None)) return;
        var edit = savedTeamEdit;
        if (edit is null || trainer?.User.Id != edit.Owner || savedTeamEditComplete)
        {
            savedTeamEdit = null; savedTeamEditComplete = false;
            ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return;
        }
        var action = edit.Kind switch { SavedTeamEditKind.New => "Save team", SavedTeamEditKind.Rename => "Rename team", SavedTeamEditKind.Replace => "Replace team", _ => "Delete saved team" };
        ImGui.TextColored(edit.Kind == SavedTeamEditKind.Delete ? PokemonRed : CompanionAccent, action);
        if (edit.Kind is SavedTeamEditKind.New or SavedTeamEditKind.Rename)
        {
            ImGui.TextUnformatted("Team name"); ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##SavedTeamName", "Name your team...", ref savedTeamEditName, 129);
        }
        else ImGui.TextWrapped($"{action}: {edit.Name}");
        var previewTrainer = new TrainerSnapshotDto { Party = trainer.Party, Storage = trainer.Storage,
            Collection = new() { TeamPresets = [new() { Id = edit.Id, Name = edit.Name, PokemonIds = edit.PokemonIds.ToList() }] } };
        var preview = SavedTeams.Resolve(previewTrainer)[0];
        ImGui.Spacing(); DrawSavedTeamMembers(preview.Members); ImGui.Spacing();
        ImGui.TextWrapped(edit.Kind == SavedTeamEditKind.Delete ? "Only the saved lineup is deleted. Your Pokémon stay in your party and PC."
            : edit.Kind == SavedTeamEditKind.Replace ? "Replace this saved lineup with the party shown above. Its name is kept."
            : "Saves these owned Pokémon in order. Their stats, moves and held items stay as they are.");
        var error = edit.Kind == SavedTeamEditKind.Delete ? null : SavedTeams.NameError(savedTeamEditName) ?? (!preview.Complete ? preview.Problem : null);
        if (edit.Kind == SavedTeamEditKind.New && trainer.Collection.TeamPresets.Count >= SavedTeams.MaximumTeams) error = "All 12 saved team slots are used.";
        if (edit.Kind != SavedTeamEditKind.New && !trainer.Collection.TeamPresets.Any(entry => entry.Id == edit.Id)) error = "This saved team was removed. Close this window and refresh the list.";
        if (error is not null) ImGui.TextWrapped(error);
        if (statusIsError) ImGui.TextWrapped(status);
        ImGui.BeginDisabled(busy || error is not null);
        if (DrawAccentButton(action, new Vector2(-1, 30 * scale), edit.Kind == SavedTeamEditKind.Delete ? PokemonRed : PokemonGreen))
        {
            var name = savedTeamEditName.Trim();
            Run(() => CommitSavedTeam(edit, name), "Updating saved team...");
        }
        ImGui.EndDisabled();
        if (ImGui.Button("Cancel", new Vector2(-1, 0))) { savedTeamEdit = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private async Task LoadSavedTeam(string owner, string id)
    {
        if (trainer?.User.Id != owner) return;
        var result = await InSession(plugin.Api.ApplyTeam(id));
        if (trainer?.User.Id != owner) return;
        trainer = result;
        selectedPartyPokemon = result.Party.OrderBy(entry => entry.PartySlot).FirstOrDefault()?.Id;
        closeSavedTeamLibrary = true;
        SetStatus("Saved team loaded.");
    }

    private async Task CommitSavedTeam(SavedTeamEdit edit, string name)
    {
        if (trainer?.User.Id != edit.Owner) return;
        var result = edit.Kind == SavedTeamEditKind.Delete ? await InSession(plugin.Api.DeleteTeam(edit.Id))
            : await InSession(plugin.Api.SaveTeam(edit.Id, name, edit.PokemonIds));
        if (trainer?.User.Id != edit.Owner) return;
        trainer = result;
        if (ReferenceEquals(savedTeamEdit, edit)) savedTeamEditComplete = true;
        SetStatus(edit.Kind == SavedTeamEditKind.Delete ? "Saved team deleted. Your Pokémon were kept." : "Saved team updated.");
    }
}
