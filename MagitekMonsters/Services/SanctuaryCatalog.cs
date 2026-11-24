using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record SanctuaryEntry(LegendaryBossDto Boss, string Name, string[] Types, bool Owned, bool Cleared, bool Caught)
{
    public string Category => Boss.Category.Contains("Mythical", StringComparison.OrdinalIgnoreCase) ? "Mythical" : "Legendary";
}

public sealed class SanctuaryCatalog
{
    public static readonly string[] Availability = ["All Sanctuaries", "Unlocked", "Permit needed", "Not defeated"];
    public static readonly string[] Categories = ["All Pokémon", "Legendary", "Mythical"];
    private TrainerSnapshotDto? trainerSource;
    private IReadOnlyList<LegendaryBossDto>? bossSource;
    private IReadOnlyList<SanctuaryEntry> entries = [];
    private IReadOnlyList<SanctuaryEntry>? filtered;
    private string? search;
    private int availability, category;

    public IReadOnlyList<SanctuaryEntry> Read(TrainerSnapshotDto trainer, IReadOnlyList<LegendaryBossDto> bosses)
    {
        if (ReferenceEquals(trainerSource, trainer) && ReferenceEquals(bossSource, bosses)) return entries;
        trainerSource = trainer; bossSource = bosses; filtered = null;
        var permits = trainer.Legendary.Permits.ToHashSet(StringComparer.Ordinal);
        var defeated = trainer.Legendary.DefeatedBossIds.ToHashSet(StringComparer.Ordinal);
        var forms = trainer.Pokedex.CaughtForms.Select(SpriteNames.Normalize).ToHashSet();
        var species = trainer.Pokedex.CaughtSpecies.Select(SpriteNames.Normalize).ToHashSet();
        return entries = bosses.Select(boss => new SanctuaryEntry(boss, SpeciesDisplayCatalog.Name(boss.Species),
            SpeciesDisplayCatalog.Get(boss.Species)?.Types ?? [], boss.Final ? trainer.Legendary.MewAvailable : permits.Contains(boss.Id),
            defeated.Contains(boss.Id), SpeciesDisplayCatalog.IsCaught(boss.Species, forms, species))).ToArray();
    }

    public IReadOnlyList<SanctuaryEntry> Filter(string query, int available, int kind)
    {
        query = query.Trim();
        if (filtered is not null && search == query && availability == available && category == kind) return filtered;
        search = query; availability = available; category = kind;
        return filtered = entries.Where(entry => (query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Boss.Species.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (kind == 0 || entry.Category == Categories[Math.Clamp(kind, 0, Categories.Length - 1)])
            && (available switch { 1 => entry.Owned, 2 => !entry.Owned && !entry.Boss.Final, 3 => !entry.Cleared && !entry.Boss.Final, _ => true })).ToArray();
    }

    public void Clear() { trainerSource = null; bossSource = null; entries = []; filtered = null; search = null; }
}
