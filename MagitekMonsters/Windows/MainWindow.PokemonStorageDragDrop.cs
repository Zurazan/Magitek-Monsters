using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

        private bool CanDrag => DragEnabled && OnPlace is not null
            && Filter is { Type: null, Generation: 0, Favorites: false, Shiny: false, Duplicates: false }
            && string.IsNullOrWhiteSpace(Filter.Search);

        private static bool SlotLocked(PokemonDto? pokemon) =>
            pokemon is { TradeLockId: not null } or { FusionHostId.Length: > 0 };

        private bool DrawStorageDragSource(PokemonDto? pokemon, int? slot)
        {
            if (!CanDrag || slot is null || pokemon is null || SlotLocked(pokemon)
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
            if (!CanDrag || SlotLocked(occupant) || slot is null && (box == Box || inventory.Count(box) >= StorageBrowser.BoxCapacity)
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
