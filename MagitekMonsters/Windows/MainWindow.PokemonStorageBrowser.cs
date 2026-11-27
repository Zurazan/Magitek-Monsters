using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private sealed class StorageInventory
    {
        private static readonly PokemonDto?[] EmptySlots = new PokemonDto?[StorageLimits.BoxCapacity];
        private readonly ConditionalWeakTable<List<PokemonDto>, PokemonDto?[]> boxSlots = new();
        private readonly Dictionary<string, PokemonDto> boxedById;
        private Dictionary<string, int> partyVariants;
        public TrainerSnapshotDto Owner { get; private set; }
        public Dictionary<string, int> Duplicates { get; }
        public Dictionary<int, List<PokemonDto>> Boxes { get; }
        public int DuplicateRevision { get; private set; }

        public StorageInventory(TrainerSnapshotDto owner)
        {
            Owner = owner;
            boxedById = owner.Storage.DistinctBy(pokemon => pokemon.Id).ToDictionary(pokemon => pokemon.Id, StringComparer.Ordinal);
            Boxes = owner.Storage.Where(pokemon => pokemon.PartySlot is null).GroupBy(pokemon => pokemon.BoxNumber)
                .ToDictionary(group => group.Key, group => group.OrderBy(pokemon => pokemon.BoxSlot).ToList());
            Duplicates = StorageBrowser.Duplicates(boxedById.Values);
            partyVariants = PartyVariants(owner.Party);
            foreach (var (variant, count) in partyVariants) ChangeCount(Duplicates, variant, count);
        }

        public void UpdateOwner(TrainerSnapshotDto owner)
        {
            if (!ReferenceEquals(Owner.Party, owner.Party))
            {
                var next = PartyVariants(owner.Party);
                if (next.Count != partyVariants.Count || next.Any(entry => partyVariants.GetValueOrDefault(entry.Key) != entry.Value))
                {
                    foreach (var (variant, count) in partyVariants) ChangeCount(Duplicates, variant, -count);
                    foreach (var (variant, count) in next) ChangeCount(Duplicates, variant, count);
                    partyVariants = next;
                    DuplicateRevision++;
                }
            }
            Owner = owner;
        }

        public bool TryUpdateStorage(TrainerSnapshotDto owner)
        {
            var delta = owner.RosterCache?.StorageDelta;
            if (delta is null || Owner.RosterCache?.StorageVersion != delta.BaseVersion) return false;
            var affected = new HashSet<int>();
            if (delta.UpdatedPokemon is { } edits)
                foreach (var pokemon in edits) affected.Add(pokemon.BoxNumber);
            if (delta.AddedPokemon is { } added)
                foreach (var pokemon in added) affected.Add(pokemon.BoxNumber);
            if (delta.RemovedPokemonIds is { } removed)
                foreach (var id in removed)
                {
                    if (!boxedById.TryGetValue(id, out var pokemon)) return false;
                    affected.Add(pokemon.BoxNumber);
                }
            foreach (var position in delta.Positions)
            {
                if (!boxedById.TryGetValue(position.PokemonId, out var before)) return false;
                affected.Add(before.BoxNumber);
                affected.Add(position.BoxNumber);
            }
            if (delta.AddedPokemon is not null || delta.RemovedPokemonIds is not null)
            {
                if (delta.RemovedPokemonIds is { } removedIds)
                    foreach (var id in removedIds)
                        if (boxedById.Remove(id, out var pokemon)) ChangeCount(Duplicates, StorageBrowser.Variant(pokemon), -1);
                if (delta.AddedPokemon is { } newPokemon)
                    foreach (var pokemon in newPokemon) ChangeCount(Duplicates, StorageBrowser.Variant(pokemon), 1);
                DuplicateRevision++;
            }
            foreach (var box in affected)
            {
                var start = FirstAtOrAfterBox(owner.Storage, box);
                var end = FirstAtOrAfterBox(owner.Storage, box + 1);
                if (start == end) { Boxes.Remove(box); continue; }
                var boxContents = owner.Storage.GetRange(start, end - start);
                Boxes[box] = boxContents;
                foreach (var pokemon in boxContents) boxedById[pokemon.Id] = pokemon;
            }
            UpdateOwner(owner);
            return true;
        }

        private static int FirstAtOrAfterBox(List<PokemonDto> storage, int box)
        {
            var first = 0;
            var last = storage.Count;
            while (first < last)
            {
                var middle = first + (last - first) / 2;
                if (storage[middle].BoxNumber < box) first = middle + 1;
                else last = middle;
            }
            return first;
        }

        private Dictionary<string, int> PartyVariants(List<PokemonDto> party)
        {
            var variants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pokemon in party.DistinctBy(pokemon => pokemon.Id))
            {
                if (boxedById.TryGetValue(pokemon.Id, out var boxed)) ChangeCount(variants, StorageBrowser.Variant(boxed), -1);
                ChangeCount(variants, StorageBrowser.Variant(pokemon), 1);
            }
            return variants;
        }

        private static void ChangeCount(Dictionary<string, int> variants, string key, int delta)
        {
            var next = variants.GetValueOrDefault(key) + delta;
            if (next == 0) variants.Remove(key); else variants[key] = next;
        }

        public PokemonDto? Find(string? id)
        {
            if (id is null) return null;
            foreach (var pokemon in Owner.Party)
                if (pokemon.Id == id) return pokemon;
            return boxedById.GetValueOrDefault(id);
        }
        public int Count(int box) => Boxes.TryGetValue(box, out var contents) ? contents.Count : 0;
        public PokemonDto?[] Slots(List<PokemonDto>? pokemon) => pokemon is { Count: > 0 }
            ? boxSlots.GetValue(pokemon, static entries => StorageBrowser.BoxSlots(entries)) : EmptySlots;
        public string Label(int box) => BoxLabel(Owner, box);
    }

    private sealed partial class PokemonStorageBrowser(
        Action<string, bool, Vector2, Vector2> drawIcon,
        Action<PokemonDto> drawDetails,
        Func<string, (int Moves, int Evolutions)> getAlerts,
        Func<string?> editRestriction,
        bool compact)
    {
        private Action<string, bool, Vector2, Vector2> DrawIcon { get; } = drawIcon;
        private Func<string, (int Moves, int Evolutions)> GetAlerts { get; } = getAlerts;
        private Func<string?> EditRestrictionMessage { get; } = editRestriction;
        private bool Compact { get; } = compact;
        private static readonly string[] Types = ["Any type", .. ChallengeTypes];
        private static readonly string[] Generations = ["Any generation", "Gen 1", "Gen 2", "Gen 3", "Gen 4", "Gen 5", "Gen 6", "Gen 7", "Gen 8", "Gen 9"];
        private StorageInventory inventory = null!;
        private StorageFilter? appliedFilter;
        private List<PokemonDto>? appliedSource;
        private int appliedDuplicateRevision;
        private List<PokemonDto> matches = [];
        private List<PokemonDto>? overviewSource;
        private Dictionary<int, List<PokemonDto>> overviewGroups = [];
        private int[] overviewBoxes = [];
        private bool showEmptyBoxes;
        private bool cachedShowEmptyBoxes;
        private bool scrollToTop = true;

        public StorageFilter Filter { get; private set; } = StorageSearches.Empty with { Box = 1 };
        public int Box => Filter.Box;
        public string? SelectedId { get; set; }
        public PokemonDto? Selected => inventory?.Find(SelectedId);
        public ISet<string>? Marked { get; init; }
        public Action<PokemonDto>? OnSelect { get; init; }
        public Action<string, StorageSlotPlacement>? OnPlace { get; init; }
        public bool DragEnabled { get; set; }
        public Func<PokemonDto?, string?>? Restriction { get; init; }

        public void Bind(StorageInventory source)
        {
            if (ReferenceEquals(inventory, source)) return;
            inventory = source;
            appliedFilter = null;
            if (Selected is null) SelectedId = null;
        }

        public void SetFilter(StorageFilter filter)
        {
            Filter = filter;
            scrollToTop = true;
        }

        public void OpenBox(int number)
        {
            var box = Math.Clamp(number, 0, StorageBrowser.BoxCount);
            SetFilter(Filter with { Box = box });
            if (Compact || box == 0 || ChoosingSwap) return;
            var contents = inventory.Boxes.GetValueOrDefault(box);
            if (contents?.Any(pokemon => pokemon.Id == SelectedId) != true)
                SelectedId = contents?.FirstOrDefault()?.Id;
        }

        public List<PokemonDto> Matches
        {
            get
            {
                var source = Box > 0 ? inventory.Boxes.GetValueOrDefault(Box) : inventory.Owner.Storage;
                if (appliedFilter == Filter && ReferenceEquals(appliedSource, source)
                    && (!Filter.Duplicates || appliedDuplicateRevision == inventory.DuplicateRevision)) return matches;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                matches = source is null ? [] : Box > 0 && (Filter with { Box = 0 }) == StorageSearches.Empty
                    ? source : StorageBrowser.Filter(source, inventory.Duplicates, Filter);
                appliedFilter = Filter;
                appliedSource = source;
                appliedDuplicateRevision = inventory.DuplicateRevision;
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (elapsed >= 4) Plugin.Log?.Debug("PC filter: {PokemonCount} Pokemon in {ElapsedMs:F1} ms", inventory.Owner.Storage.Count, elapsed);
                return matches;
            }
        }

        public void DrawSearch()
        {
            var search = Filter.Search;
            var width = Compact ? ImGui.CalcTextSize("Clear").X + 2 * ImGui.GetStyle().FramePadding.X + ImGui.GetStyle().ItemSpacing.X
                : 66 * CompanionScale;
            ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - width));
            if (ImGui.InputTextWithHint("##StorageSearch", Compact ? "Search PC Storage..." : "Search your whole PC: species, nickname, move or ability...", ref search, 80))
                SetFilter(Filter with { Search = search, Box = 0 });
            if (Compact && ImGui.IsItemHovered()) ImGui.SetTooltip("Search by species, nickname, move or ability.");
            ImGui.SameLine();
            ImGui.BeginDisabled(Compact && search.Length == 0);
            if (ToolbarButton("Clear", CompanionMuted))
                SetFilter(Filter with { Search = "", Box = Compact ? 0 : Box });
            ImGui.EndDisabled();
        }

        public void DrawAllBoxes()
        {
            if (ToolbarButton("All boxes", CompanionAccent)) OpenBox(0);
        }

        public void DrawBoxJump()
        {
            ImGui.SetNextItemWidth(Math.Max(1, Math.Min(180 * CompanionScale, ImGui.GetContentRegionAvail().X)));
            if (!ImGui.BeginCombo("##StorageJump", Box == 0 ? "Jump to box..." : inventory.Label(Box))) return;
            for (var number = 1; number <= StorageBrowser.BoxCount; number++)
                if (ImGui.Selectable($"{inventory.Label(number)} ({inventory.Count(number)}/30)##Jump{number}", Box == number))
                    OpenBox(number);
            ImGui.EndCombo();
        }

        public void DrawFilters(float right, float workWidth)
        {
            var filter = Filter;
            var favorites = filter.Favorites;
            var shiny = filter.Shiny;
            var duplicates = filter.Duplicates;
            var type = Array.IndexOf(ChallengeTypes, filter.Type) + 1;
            var generation = filter.Generation;
            var count = (favorites ? 1 : 0) + (shiny ? 1 : 0) + (duplicates ? 1 : 0)
                + (type > 0 ? 1 : 0) + (generation > 0 ? 1 : 0);
            var label = count > 0 ? $"Filters ({count})" : "Filters";
            var scale = CompanionScale;
            StorageSameLine(right, ImGui.CalcTextSize(label).X + (Compact ? 2 * ImGui.GetStyle().FramePadding.X : 16 * scale));
            if (ToolbarButton(label, count > 0 ? CompanionAccent : CompanionMuted)) ImGui.OpenPopup("StorageFilters");
            ImGui.SetNextWindowSize(new Vector2(Math.Min(330 * scale, workWidth * .9f), 0), ImGuiCond.Appearing);
            if (!ImGui.BeginPopup("StorageFilters")) return;
            if (DrawFilterOptions(ref favorites, ref shiny, ref duplicates, ref type, ref generation))
                SetFilter(filter with { Box = 0, Favorites = favorites, Shiny = shiny, Duplicates = duplicates,
                    Type = type > 0 ? ChallengeTypes[type - 1] : null, Generation = generation });
            if (DrawAccentSmallButton("Reset filters", CompanionMuted))
                SetFilter(StorageSearches.Empty with { Search = filter.Search, Box = Compact ? 0 : Box });
            ImGui.EndPopup();
        }

        private bool ToolbarButton(string label, Vector4 accent) =>
            Compact ? DrawAccentSmallButton(label, accent) : DrawStorageToolbarButton(label, accent);

        private static bool DrawFilterOptions(ref bool favorites, ref bool shiny, ref bool duplicates, ref int type, ref int generation)
        {
            var scale = CompanionScale;
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            var changed = ImGui.Checkbox("Favourites", ref favorites);
            StorageSameLine(right, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Shiny only").X);
            changed |= ImGui.Checkbox("Shiny only", ref shiny);
            StorageSameLine(right, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("Duplicates only").X);
            changed |= ImGui.Checkbox("Duplicates only", ref duplicates);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Same appearance and shiny variant, including copies in your party.");
            StorageSameLine(right, 150 * scale);
            ImGui.SetNextItemWidth(Math.Min(150 * scale, ImGui.GetContentRegionAvail().X));
            changed |= ImGui.Combo("##StorageType", ref type, Types, Types.Length);
            StorageSameLine(right, 150 * scale);
            ImGui.SetNextItemWidth(Math.Min(150 * scale, ImGui.GetContentRegionAvail().X));
            return ImGui.Combo("##StorageGen", ref generation, Generations, Generations.Length) || changed;
        }

        public void DrawBody(Action<PokemonDto>? beforeDetails = null, Action<PokemonDto>? afterDetails = null)
        {
            var overview = Box == 0;
            var scale = CompanionScale;
            var filtered = Matches;
            if (overview && !Compact)
            {
                if (ImGui.Checkbox("Show empty boxes", ref showEmptyBoxes)) scrollToTop = true;
                ImGui.SameLine();
                DrawClippedText(swap is { } source ? $"Swapping {source.Name} / {(source.PartyOnly ? "Pick a party slot" : "Pick a party slot or open a box")}" : $"{filtered.Count:N0} Pokémon / {OverviewBoxes().Length} boxes",
                    ImGui.GetContentRegionAvail().X, ChoosingSwap ? PokemonYellow : CompanionMuted);
                DrawStorageThumbnails(0, Math.Max(240 * scale, ImGui.GetContentRegionAvail().Y - 100 * scale));
                DrawStoragePartyTray();
                return;
            }
            if (!Compact && !ChoosingSwap && !inventory.Owner.Party.Concat(filtered).Any(pokemon => pokemon.Id == SelectedId))
                SelectedId = filtered.FirstOrDefault()?.Id;
            if (!overview && Compact && scrollToTop)
            {
                ImGui.SetScrollY(0);
                scrollToTop = false;
            }
            var layout = StorageLayout.Measure(ImGui.GetContentRegionAvail().X, scale);
            if (!layout.PartyRail) DrawStoragePartyTray();
            var cellHeight = Compact ? Math.Min(layout.CellHeight, 62 * scale) : layout.CellHeight;
            var height = overview ? Math.Max(150 * scale, ImGui.GetContentRegionAvail().Y - 6 * scale)
                : StorageLayout.GridHeight(cellHeight, ImGui.GetTextLineHeight(), ImGui.GetStyle().ItemSpacing.Y, scale);
            if (layout.PartyRail)
            {
                DrawStoragePartyRail(layout.PartyWidth, height);
                ImGui.SameLine(0, 10 * scale);
            }
            ImGui.BeginGroup();
            if (overview) DrawStorageThumbnails(layout.BoxWidth, height);
            else
            {
                DrawStorageBoxGrid(filtered, layout.BoxWidth, height, cellHeight);
                DrawStorageBoxShortcuts(layout.BoxWidth, filtered.Count == 0);
            }
            ImGui.EndGroup();
            if (layout.SideBySide) ImGui.SameLine(0, 10 * scale); else ImGui.Spacing();
            var selected = Selected;
            var minimumPreviewHeight = Compact ? 330 * scale : 480 * scale;
            var previewHeight = Compact && selected is null ? height : Math.Max(height, minimumPreviewHeight);
            DrawPreview(selected, layout.DetailWidth, previewHeight, beforeDetails, afterDetails);
        }

        private int[] OverviewBoxes()
        {
            var filtered = Matches;
            if (ReferenceEquals(overviewSource, filtered) && cachedShowEmptyBoxes == showEmptyBoxes) return overviewBoxes;
            if (!ReferenceEquals(overviewSource, filtered))
            {
                overviewSource = filtered;
                overviewGroups = Filter == StorageSearches.Empty ? inventory.Boxes
                    : filtered.GroupBy(pokemon => pokemon.BoxNumber).ToDictionary(group => group.Key, group => group.ToList());
            }
            cachedShowEmptyBoxes = showEmptyBoxes;
            overviewBoxes = showEmptyBoxes ? Enumerable.Range(1, StorageBrowser.BoxCount).ToArray() : overviewGroups.Keys.Order().ToArray();
            return overviewBoxes;
        }

        private void DrawPreview(PokemonDto? pokemon, float width, float height, Action<PokemonDto>? beforeDetails, Action<PokemonDto>? afterDetails)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2((Compact ? 10 : 12) * CompanionScale));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
            if (ImGui.BeginChild(Compact ? "StoragePickerPreview" : "StorageInspector", new Vector2(width, height), true))
            {
                if (ChoosingSwap) DrawSwapPreview();
                else if (pokemon is not null)
                {
                    ImGui.PushID(pokemon.Id);
                    if (beforeDetails is not null) beforeDetails(pokemon);
                    else ImGui.TextWrapped(pokemon.PartySlot is int slot ? $"Party slot {slot}" : $"{inventory.Label(pokemon.BoxNumber)} / Slot {pokemon.BoxSlot}");
                    drawDetails(pokemon);
                    afterDetails?.Invoke(pokemon);
                    ImGui.PopID();
                }
                else ImGui.TextWrapped("Select a Pokémon to see its details.");
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }

        private void Select(PokemonDto pokemon)
        {
            SelectedId = pokemon.Id;
            OnSelect?.Invoke(pokemon);
        }
    }
}
