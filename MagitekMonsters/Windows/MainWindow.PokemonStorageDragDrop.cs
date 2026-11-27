using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private sealed partial class PokemonStorageBrowser
    {
        private const string StoragePayload = "MAGIMON_PC_SLOT";
        private const int StoragePayloadSize = 24;
        private sealed record StorageSwap(string Id, int Box, int Slot, string Name);
        private StorageSwap? swap;

        public bool ChoosingSwap => swap is not null;

        private bool CanDrag => DragEnabled && OnPlace is not null
            && Filter is { Type: null, Generation: 0, Favorites: false, Shiny: false, Duplicates: false }
            && string.IsNullOrWhiteSpace(Filter.Search);

        private static bool SlotLocked(PokemonDto? pokemon) =>
            pokemon is { TradeLockId: not null } or { FusionHostId.Length: > 0 };

        private static bool CanSwapPokemon(PokemonDto? pokemon) =>
            pokemon is { PartySlot: null, BoxNumber: >= 1 and <= StorageBrowser.BoxCount, BoxSlot: >= 1 and <= StorageBrowser.BoxCapacity }
            && !SlotLocked(pokemon);

        public void DrawSwapButton(float right)
        {
            const string start = "Swap slots...";
            const string cancel = "Cancel swap";
            var choosing = ChoosingSwap;
            var pokemon = Selected;
            var width = Math.Max(ImGui.CalcTextSize(start).X, ImGui.CalcTextSize(cancel).X) + ImGui.GetStyle().FramePadding.X * 2;
            StorageSameLine(right, width);
            var size = new Vector2(Math.Max(1, Math.Min(width, ImGui.GetContentRegionAvail().X)), 32 * CompanionScale);
            ImGui.BeginDisabled(!choosing && (!DragEnabled || pokemon?.BoxNumber != Box || !CanSwapPokemon(pokemon)));
            if (DrawAccentButton((choosing ? cancel : start) + "###StorageSwap", size, choosing ? PokemonYellow : CompanionAccent))
            {
                if (choosing) CancelSwap();
                else if (pokemon is not null)
                    swap = new(pokemon.Id, pokemon.BoxNumber, pokemon.BoxSlot, pokemon.Nickname.Length > 0 ? pokemon.Nickname : pokemon.DisplaySpecies);
            }
            ImGui.EndDisabled();
            if (!choosing && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Select a Pokémon in a box to swap its slot with another Pokémon.");
        }

        public void CancelSwap() => swap = null;

        private PokemonDto? FindSwapSource(StorageSwap source) =>
            inventory.Boxes.GetValueOrDefault(source.Box)?.Find(pokemon => pokemon.Id == source.Id);

        public bool CancelInvalidSwap()
        {
            if (swap is not { } source) return false;
            var current = FindSwapSource(source);
            if (current?.BoxSlot == source.Slot && CanSwapPokemon(current)) return false;
            CancelSwap();
            return true;
        }

        private void DrawSwapPreview()
        {
            if (swap is not { } source) return;
            ImGui.TextColored(PokemonYellow, "Choose a swap target");
            ImGui.Spacing();
            if (FindSwapSource(source) is { } pokemon)
            {
                var width = ImGui.GetContentRegionAvail().X;
                var image = new Vector2(Math.Clamp(width, 1, 72 * CompanionScale));
                DrawIcon(pokemon.Appearance, pokemon.Shiny, ImGui.GetCursorScreenPos() + new Vector2((width - image.X) / 2, 0), image);
                ImGui.Dummy(image);
            }
            ImGui.TextWrapped(source.Name);
            ImGui.TextWrapped($"From {inventory.Label(source.Box)} / Slot {source.Slot}");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped(DragEnabled ? "Click a highlighted Pokémon here or open another box. Their slots will swap."
                : EditRestrictionMessage() ?? "Wait for the current action to finish.");
        }

        private bool CanSwapWith(PokemonDto? pokemon) => DragEnabled && swap is { } source
            && pokemon?.Id != source.Id && CanSwapPokemon(pokemon);

        private void SwapWith(PokemonDto pokemon)
        {
            if (!CanSwapWith(pokemon) || swap is not { } source || CancelInvalidSwap()) return;
            CancelSwap();
            OnPlace?.Invoke(source.Id, new(source.Box, source.Slot, pokemon.BoxNumber, pokemon.BoxSlot, pokemon.Id));
        }

        private bool DrawStorageDragSource(PokemonDto? pokemon, int? slot)
        {
            if (ChoosingSwap || !CanDrag || slot is null || pokemon is null || SlotLocked(pokemon)
                || !Guid.TryParse(pokemon.Id, out var id) || !ImGui.BeginDragDropSource()) return false;
            Span<byte> payload = stackalloc byte[StoragePayloadSize];
            id.TryWriteBytes(payload);
            BinaryPrimitives.WriteInt32LittleEndian(payload[16..], Box);
            BinaryPrimitives.WriteInt32LittleEndian(payload[20..], slot.Value);
            ImGui.SetDragDropPayload(StoragePayload, payload, ImGuiCond.Once);
            ImGui.TextUnformatted(pokemon.Nickname.Length > 0 ? pokemon.Nickname : pokemon.DisplaySpecies);
            ImGui.TextDisabled("Drop on a slot or box number");
            ImGui.EndDragDropSource();
            return true;
        }

        private unsafe void DrawStorageDropTarget(int box, int? slot, PokemonDto? occupant)
        {
            if (ChoosingSwap || !CanDrag || SlotLocked(occupant) || slot is null && (box == Box || inventory.Count(box) >= StorageBrowser.BoxCapacity)
                || !ImGui.BeginDragDropTarget()) return;
            var payload = ImGui.AcceptDragDropPayload(StoragePayload, ImGuiDragDropFlags.None);
            if (!payload.IsNull && payload.DataSize == StoragePayloadSize && payload.IsDelivery())
            {
                var data = new ReadOnlySpan<byte>(payload.Data, StoragePayloadSize);
                var id = new Guid(data[..16]).ToString();
                var fromBox = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
                var fromSlot = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
                var slots = inventory.Boxes.GetValueOrDefault(box);
                var toSlot = slot ?? Array.FindIndex(StorageBrowser.BoxSlots(slots ?? []), pokemon => pokemon is null) + 1;
                if (toSlot > 0 && (fromBox != box || fromSlot != toSlot))
                    OnPlace?.Invoke(id, new(fromBox, fromSlot, box, toSlot, occupant?.Id));
            }
            ImGui.EndDragDropTarget();
        }
    }
}
