using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public enum SavedTeamMemberState { Ready, Empty, Missing, FusionLocked }
public sealed record SavedTeamMember(int Slot, PokemonDto? Pokemon, SavedTeamMemberState State);
public sealed record SavedTeamView(TeamPresetDto Preset, IReadOnlyList<SavedTeamMember> Members,
    bool Current, bool Complete, bool CanLoad, string Status, string? Problem);

public static class SavedTeams
{
    public const int MaximumTeams = 12;
    public const int MaximumNameLength = 32;

    public static IReadOnlyList<SavedTeamView> Resolve(TrainerSnapshotDto trainer)
    {
        var owned = trainer.Party.Concat(trainer.Storage).DistinctBy(entry => entry.Id).ToDictionary(entry => entry.Id);
        var party = trainer.Party.OrderBy(entry => entry.PartySlot).Select(entry => entry.Id).ToArray();
        return trainer.Collection.TeamPresets.Select(preset =>
        {
            var ids = preset.PokemonIds;
            var members = Enumerable.Range(0, 6).Select(index =>
            {
                if (index >= ids.Count) return new SavedTeamMember(index + 1, null, SavedTeamMemberState.Empty);
                if (!owned.TryGetValue(ids[index], out var pokemon)) return new SavedTeamMember(index + 1, null, SavedTeamMemberState.Missing);
                return new SavedTeamMember(index + 1, pokemon, pokemon.FusionHostId.Length > 0 ? SavedTeamMemberState.FusionLocked : SavedTeamMemberState.Ready);
            }).ToList();
            var valid = ids.Count is >= 1 and <= 6 && ids.Distinct().Count() == ids.Count;
            var missing = members.Count(entry => entry.State == SavedTeamMemberState.Missing);
            var fused = members.Count(entry => entry.State == SavedTeamMemberState.FusionLocked);
            var complete = valid && missing == 0 && fused == 0;
            var current = complete && ids.SequenceEqual(party);
            var storedAfterLoading = trainer.Storage.Count + trainer.Party.Count - ids.Count;
            var full = complete && storedAfterLoading > StorageBrowser.BoxCount * StorageBrowser.BoxCapacity;
            var problem = !valid ? "This preset needs 1-6 different Pokémon. Replace it with your current party to repair it."
                : missing > 0 || fused > 0 ? string.Join(" / ", new[] {
                    missing > 0 ? $"{missing} missing Pokémon" : null,
                    fused > 0 ? $"{fused} fusion-locked Pokémon" : null }.OfType<string>())
                : full ? "Free PC space before loading a smaller team." : null;
            var boxed = members.Count(entry => entry.State == SavedTeamMemberState.Ready && entry.Pokemon?.PartySlot is null);
            var status = current ? "Current party" : problem ?? $"Ready / {ids.Count}/6 members{(boxed > 0 ? $" / {boxed} from PC" : "")}";
            return new SavedTeamView(preset, members, current, complete, complete && !full && !current, status, problem);
        }).ToList();
    }

    public static string? NameError(string name)
    {
        var runes = name.Trim().EnumerateRunes().ToArray();
        return runes.Length is < 1 or > MaximumNameLength || runes.Any(rune => rune.Value is '[' or ']' or '|'
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            ? "Use 1-32 visible characters without brackets or pipes." : null;
    }

    public static bool CanSaveParty(TrainerSnapshotDto trainer) => trainer.Party.Count is >= 1 and <= 6
        && trainer.Party.Select(entry => entry.Id).Distinct().Count() == trainer.Party.Count
        && trainer.Party.All(entry => entry.FusionHostId.Length == 0);

    public static (int Columns, float Height) Layout(float width, float scale, float lineHeight, float spacing)
    {
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 4) : 1;
        width = float.IsFinite(width) ? Math.Max(1, width) : 1;
        lineHeight = float.IsFinite(lineHeight) ? Math.Max(1, lineHeight) : 17 * scale;
        spacing = float.IsFinite(spacing) ? Math.Max(0, spacing) : 5 * scale;
        return (width >= 760 * scale ? 2 : 1, 4 * lineHeight + 112 * scale + 7 * spacing);
    }
}
