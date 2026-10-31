using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string storageSearchesOwner = string.Empty;
    private StorageSearchPreferences? storageSearchPreferences;
    private string activeStorageSearch = string.Empty;
    private string storageSearchLibraryOwner = string.Empty;
    private bool requestStorageSearchLibrary;
    private bool requestStorageSearchEdit;
    private string storageSearchEditName = string.Empty;
    private string storageSearchEditError = string.Empty;
    private bool storageSearchReplaceFilters;
    private StorageSearchEdit? storageSearchEdit;
    private sealed record StorageSearchEdit(string Owner, string? Id, StorageFilter Saved, StorageFilter Current);

    private void ResetStorageSearches()
    {
        storageSearchesOwner = activeStorageSearch = storageSearchLibraryOwner = storageSearchEditName = storageSearchEditError = string.Empty;
        storageSearchPreferences = null; storageSearchEdit = null;
        requestStorageSearchLibrary = requestStorageSearchEdit = storageSearchReplaceFilters = false;
    }

    private StorageSearchPreferences LocalStorageSearches()
    {
        if (storageSearchesOwner != trainer!.User.Id || storageSearchPreferences is null)
        {
            ResetStorageSearches(); storageSearchesOwner = trainer.User.Id;
            storageSearchPreferences = StorageSearches.Preferences(plugin.Configuration.StorageSearches ??= new(), storageSearchesOwner);
        }
        return storageSearchPreferences;
    }

    private StorageFilter CaptureStorageSearch() => StorageSearches.Normalize(new(storageSearch, 0,
        storageType > 0 && storageType <= ChallengeTypes.Length ? ChallengeTypes[storageType - 1] : null,
        storageGeneration, storageSort, storageFavorites, storageShiny, storageDuplicatesOnly));

    private void ApplyStorageSearch(string owner, string? id)
    {
        if (trainer?.User.Id != owner) return;
        var preferences = LocalStorageSearches();
        var saved = id is null ? null : preferences.Searches.FirstOrDefault(search => search.Id == id);
        if (id is not null && saved is null) return;
        var filter = StorageSearches.Normalize(saved?.Filter);
        storageSearch = filter.Search;
        storageType = filter.Type is null ? 0 : Array.IndexOf(ChallengeTypes, filter.Type) + 1;
        storageGeneration = filter.Generation; storageSort = filter.Sort;
        storageFavorites = filter.Favorites; storageShiny = filter.Shiny; storageDuplicatesOnly = filter.Duplicates;
        activeStorageSearch = id ?? string.Empty;
        ShowStorageOverview();
        SetStatus(saved is null ? "PC search and filters reset." : $"Loaded saved search: {saved.Name}.");
    }

    private void BeginStorageSearchEdit(string? id)
    {
        if (trainer is null) return;
        var preferences = LocalStorageSearches();
        var saved = id is null ? null : preferences.Searches.FirstOrDefault(search => search.Id == id);
        if (id is not null && saved is null) return;
        var current = CaptureStorageSearch();
        storageSearchEdit = new(trainer.User.Id, id, saved?.Filter ?? current, current);
        storageSearchEditName = saved?.Name ?? string.Empty;
        storageSearchEditError = string.Empty; storageSearchReplaceFilters = false;
        requestStorageSearchEdit = true;
    }

    private bool SaveStorageSearch()
    {
        var edit = storageSearchEdit;
        if (edit is null || trainer?.User.Id != edit.Owner) return false;
        var preferences = LocalStorageSearches();
        storageSearchEditError = StorageSearches.Save(preferences, edit.Id, storageSearchEditName,
            storageSearchReplaceFilters ? edit.Current : edit.Saved) ?? string.Empty;
        if (storageSearchEditError.Length > 0) return false;
        plugin.Configuration.Save();
        activeStorageSearch = preferences.Searches.First(search => search.Name == storageSearchEditName.Trim()).Id;
        SetStatus(edit.Id is null ? "PC search saved locally." : "Saved PC search updated.");
        storageSearchEdit = null; requestStorageSearchEdit = false;
        return true;
    }

    private bool RemoveStorageSearch(string owner, string id)
    {
        if (trainer?.User.Id != owner || !StorageSearches.Remove(LocalStorageSearches(), id)) return false;
        plugin.Configuration.Save();
        if (activeStorageSearch == id) activeStorageSearch = string.Empty;
        storageSearchEdit = null; requestStorageSearchEdit = false;
        SetStatus("Saved search removed. Current PC filters kept.");
        return true;
    }

    private void DrawStorageSearchToolbar()
    {
        var preferences = LocalStorageSearches();
        var active = preferences.Searches.FirstOrDefault(search => search.Id == activeStorageSearch);
        var label = active is null ? "Saved searches..." : active.Name + (active.Filter == CaptureStorageSearch() ? "" : " (modified)");
        var scale = CompanionScale; var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var width = Math.Min(250 * scale, ImGui.GetContentRegionAvail().X);
        var position = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        ImGui.SetNextItemWidth(width);
        var expanded = ImGui.BeginCombo("##SavedStorageSearches", "");
        var padding = ImGui.GetStyle().FramePadding;
        draw.PushClipRect(position + padding, position + new Vector2(Math.Max(padding.X + 1, width - ImGui.GetFrameHeight()), ImGui.GetFrameHeight()), true);
        draw.AddText(position + padding, ImGui.GetColorU32(ImGuiCol.Text), label);
        draw.PopClipRect();
        if (expanded)
        {
            if (ImGui.Selectable("All Pokémon / reset filters")) ApplyStorageSearch(trainer!.User.Id, null);
            ImGui.Separator();
            if (preferences.Searches.Count == 0) ImGui.TextDisabled("No saved searches yet");
            foreach (var search in preferences.Searches)
            {
                var min = ImGui.GetCursorScreenPos(); var available = ImGui.GetContentRegionAvail().X;
                if (ImGui.Selectable("##SavedSearch" + search.Id, search.Id == activeStorageSearch, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight())))
                    ApplyStorageSearch(trainer!.User.Id, search.Id);
                var list = ImGui.GetWindowDrawList();
                list.PushClipRect(min, min + new Vector2(Math.Max(1, available), ImGui.GetTextLineHeight()), true);
                list.AddText(min, ImGui.GetColorU32(ImGuiCol.Text), search.Name); list.PopClipRect();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(StorageSearches.Description(search.Filter));
            }
            ImGui.EndCombo();
        }
        StorageSameLine(right, ImGui.CalcTextSize("Save search...").X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.BeginDisabled(preferences.Searches.Count >= StorageSearches.MaximumSearches);
        if (DrawStorageToolbarButton("Save search...", CompanionAccent)) BeginStorageSearchEdit(null);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip($"Save the current search, filters and sort order across all boxes. Up to {StorageSearches.MaximumSearches} saved searches per trainer on this PC.");
        StorageSameLine(right, ImGui.CalcTextSize("Manage...").X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.BeginDisabled(preferences.Searches.Count == 0);
        if (DrawStorageToolbarButton("Manage...", CompanionMuted)) { storageSearchLibraryOwner = trainer!.User.Id; requestStorageSearchLibrary = true; }
        ImGui.EndDisabled();
    }

    private void DrawStorageSearchPopups()
    {
        if (requestStorageSearchLibrary) { ImGui.OpenPopup("Saved PC searches"); requestStorageSearchLibrary = false; }
        if (ImGui.IsPopupOpen("Saved PC searches"))
        {
            var scale = CompanionScale; var work = ImGui.GetMainViewport().WorkSize;
            ImGui.SetNextWindowSize(new Vector2(Math.Min(660 * scale, work.X * 0.94f), Math.Min(540 * scale, work.Y * 0.9f)), ImGuiCond.Appearing);
            var open = true;
            if (BeginCenteredModal("Saved PC searches", ref open, ImGuiWindowFlags.None))
            {
                if (trainer?.User.Id != storageSearchLibraryOwner) ImGui.CloseCurrentPopup();
                else
                {
                    var preferences = LocalStorageSearches();
                    DrawSectionHeader("Saved PC searches", $"{preferences.Searches.Count}/{StorageSearches.MaximumSearches} saved on this PC for this trainer");
                    ImGui.TextWrapped("Searches cover all boxes. Edit a name, replace its filters or remove the saved search.");
                    if (ImGui.BeginChild("StorageSearchLibraryList", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing())), false))
                        DrawStorageSearchEntries(preferences);
                    ImGui.EndChild();
                    if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
                    DrawStorageSearchEdit();
                }
                ImGui.EndPopup();
            }
        }
        else DrawStorageSearchEdit();
    }

    private void DrawStorageSearchEntries(StorageSearchPreferences preferences)
    {
        if (preferences.Searches.Count == 0) ImGui.TextWrapped("No saved searches. Set your PC filters and choose Save search.");
        foreach (var search in preferences.Searches.ToArray())
        {
            ImGui.PushID(search.Id);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * CompanionScale));
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionAccent); ImGui.TextWrapped(search.Name); ImGui.PopStyleColor();
            ImGui.TextWrapped(StorageSearches.Description(search.Filter));
            ImGui.PopTextWrapPos();
            if (DrawAccentSmallButton("Use search", PokemonBlue)) { ApplyStorageSearch(trainer!.User.Id, search.Id); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (DrawAccentSmallButton("Edit...", CompanionMuted)) BeginStorageSearchEdit(search.Id);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.PopID();
        }
    }

    private void DrawStorageSearchEdit()
    {
        if (requestStorageSearchEdit) { ImGui.OpenPopup("Save PC search"); requestStorageSearchEdit = false; }
        if (!ImGui.IsPopupOpen("Save PC search")) return;
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(480 * CompanionScale, ImGui.GetMainViewport().WorkSize.X * 0.92f), 0), ImGuiCond.Appearing);
        if (!BeginCenteredModal("Save PC search", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        var edit = storageSearchEdit;
        if (edit is null || trainer?.User.Id != edit.Owner) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.TextUnformatted(edit.Id is null ? "Save current PC search" : "Edit saved PC search");
        ImGui.TextWrapped($"Saved on this PC for this trainer. Names can use up to {StorageSearches.MaximumNameLength} characters.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##StorageSearchName", "Search name...", ref storageSearchEditName, 256);
        if (edit.Id is not null && edit.Saved != edit.Current) ImGui.Checkbox("Replace with current PC filters", ref storageSearchReplaceFilters);
        ImGui.TextWrapped(StorageSearches.Description(storageSearchReplaceFilters ? edit.Current : edit.Saved));
        ImGui.TextWrapped("Applies across all boxes. Results update as your collection changes.");
        if (storageSearchEditError.Length > 0) ImGui.TextWrapped(storageSearchEditError);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(storageSearchEditName));
        if (DrawAccentSmallButton(edit.Id is null ? "Save search" : "Save changes", PokemonGreen) && SaveStorageSearch()) ImGui.CloseCurrentPopup();
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("Cancel")) { storageSearchEdit = null; ImGui.CloseCurrentPopup(); }
        if (edit.Id is not null)
        {
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            if (DrawAccentSmallButton("Delete saved search", PokemonRed) && RemoveStorageSearch(edit.Owner, edit.Id)) ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
