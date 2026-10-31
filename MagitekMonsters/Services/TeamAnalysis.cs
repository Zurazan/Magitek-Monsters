using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed record TeamAnalysisAttack(string Name, string Type, string Id);
public sealed record TeamAnalysisMember(string Id, string Name, string Species, string Ability, string[] Types,
    IReadOnlyList<TeamAnalysisAttack> Attacks, IReadOnlyList<string> Notes, int UnknownMoves);
public sealed record TeamDefenseValue(TeamAnalysisMember Member, double? Multiplier, string Source);
public sealed record TeamDefenseRow(string Type, IReadOnlyList<TeamDefenseValue> Members)
{
    public int Weak => Members.Count(value => value.Multiplier > 1);
    public int Resistant => Members.Count(value => value.Multiplier is > 0 and < 1);
    public int Immune => Members.Count(value => value.Multiplier == 0);
}
public sealed record TeamCoverageValue(TeamAnalysisMember Member, string Move, double Multiplier);
public sealed record TeamCoverageRow(string Type, IReadOnlyList<TeamCoverageValue> Members)
{
    public int SuperEffective => Members.Count(value => value.Multiplier > 1);
    public double Best => Members.Count == 0 ? 0 : Members.Max(value => value.Multiplier);
}
public sealed record TeamAnalysisReport(IReadOnlyList<TeamAnalysisMember> Members, IReadOnlyList<TeamDefenseRow> Defense,
    IReadOnlyList<TeamCoverageRow> Offense, int MissingMembers, int UnknownDefenders, int UnknownMoves);

public static class TeamAnalysis
{
    private sealed record TypeRow(string Name, string Strong, string Resisted, string Immune);
    private static readonly TypeRow[] Chart = [
        new("Bug", "Dark Grass Psychic", "Fairy Fighting Fire Flying Ghost Poison Steel", ""),
        new("Dark", "Ghost Psychic", "Dark Fairy Fighting", ""),
        new("Dragon", "Dragon", "Steel", "Fairy"),
        new("Electric", "Flying Water", "Dragon Electric Grass", "Ground"),
        new("Fairy", "Dark Dragon Fighting", "Fire Poison Steel", ""),
        new("Fighting", "Dark Ice Normal Rock Steel", "Bug Fairy Flying Poison Psychic", "Ghost"),
        new("Fire", "Bug Grass Ice Steel", "Dragon Fire Rock Water", ""),
        new("Flying", "Bug Fighting Grass", "Electric Rock Steel", ""),
        new("Ghost", "Ghost Psychic", "Dark", "Normal"),
        new("Grass", "Ground Rock Water", "Bug Dragon Fire Flying Grass Poison Steel", ""),
        new("Ground", "Electric Fire Poison Rock Steel", "Bug Grass", "Flying"),
        new("Ice", "Dragon Flying Grass Ground", "Fire Ice Steel Water", ""),
        new("Normal", "", "Rock Steel", "Ghost"),
        new("Poison", "Fairy Grass", "Ghost Ground Poison Rock", "Steel"),
        new("Psychic", "Fighting Poison", "Psychic Steel", "Dark"),
        new("Rock", "Bug Fire Flying Ice", "Fighting Ground Steel", ""),
        new("Steel", "Fairy Ice Rock", "Electric Fire Steel Water", ""),
        new("Water", "Fire Ground Rock", "Dragon Grass Water", "")
    ];
    public static IReadOnlyList<string> Types { get; } = Array.AsReadOnly(Chart.Select(row => row.Name).ToArray());
    private static readonly Dictionary<string, Dictionary<string, double>> Matchups = Chart.ToDictionary(row => row.Name, row =>
        Types.ToDictionary(type => type, type => row.Immune.Split(' ').Contains(type) ? 0d
            : row.Strong.Split(' ').Contains(type) ? 2d : row.Resisted.Split(' ').Contains(type) ? 0.5d : 1d), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FixedDamage = new(("bide counter mirrorcoat metalburst comeuppance dragonrage sonicboom nightshade seismictoss "
        + "superfang naturesmadness ruination endeavor finalgambit psywave guardianofalola fissure guillotine horndrill sheercold struggle").Split(' '));
    private static readonly HashSet<string> ConditionalMoves = new("weatherball terrainpulse naturalgift terastarstorm teracluster".Split(' '));
    private static readonly HashSet<string> SoundMoves = new(("alluringvoice boomburst bugbuzz chatter clangingscales clangoroussoulblaze disarmingvoice echoedvoice "
        + "eeriespell hypervoice overdrive psychicnoise relicsong round snarl snore sparklingaria torchsong uproar").Split(' '));
    private static readonly HashSet<string> NoConversion = new("hiddenpower judgment multiattack naturalgift revelationdance struggle technoblast terrainpulse weatherball".Split(' '));
    private static readonly Dictionary<string, string> Plates = new() {
        ["fistplate"]="Fighting", ["skyplate"]="Flying", ["toxicplate"]="Poison", ["earthplate"]="Ground",
        ["stoneplate"]="Rock", ["insectplate"]="Bug", ["spookyplate"]="Ghost", ["ironplate"]="Steel",
        ["flameplate"]="Fire", ["splashplate"]="Water", ["meadowplate"]="Grass", ["zapplate"]="Electric",
        ["mindplate"]="Psychic", ["icicleplate"]="Ice", ["dracoplate"]="Dragon", ["dreadplate"]="Dark", ["pixieplate"]="Fairy"
    };
    private static readonly Dictionary<string, string> ConditionalAbilities = new() {
        ["multiscale"]="Multiscale halves damage at full HP; not included in the base table.",
        ["shadowshield"]="Shadow Shield halves damage at full HP; not included in the base table.",
        ["terashell"]="Tera Shell changes effectiveness at full HP; not included in the base table.",
        ["fluffy"]="Fluffy doubles Fire damage here; contact damage reduction is move-dependent and excluded.",
        ["furcoat"]="Fur Coat affects physical damage; the type table does not assume an attack category.",
        ["icescales"]="Ice Scales halves special damage; the type table does not assume an attack category.",
        ["soundproof"]="Soundproof blocks sound moves, not an entire type.",
        ["bulletproof"]="Bulletproof blocks specific moves, not an entire type.",
        ["windrider"]="Wind Rider blocks wind moves, not an entire type.",
        ["protean"]="Protean can change the user's type after a move; current types are shown.",
        ["libero"]="Libero can change the user's type after a move; current types are shown.",
        ["colorchange"]="Color Change can change defensive typing after a hit; current types are shown.",
        ["mimicry"]="Mimicry changes typing with terrain; terrain changes are excluded.",
        ["forecast"]="Forecast changes form and typing with weather; weather changes are excluded.",
        ["desolateland"]="Desolate Land blocks Water attacks while its weather is active; weather is excluded.",
        ["primordialsea"]="Primordial Sea blocks Fire attacks while its weather is active; weather is excluded.",
        ["deltastream"]="Delta Stream changes Flying weaknesses while its weather is active; weather is excluded.",
        ["disguise"]="Disguise protects against the first damaging hit; not included in the base table.",
        ["iceface"]="Ice Face protects against a physical hit while intact; not included in the base table.",
        ["friendguard"]="Friend Guard protects allies in multi-Pokémon battles; ally effects are excluded.",
        ["flowergift"]="Flower Gift changes stats in sunlight; weather and stat boosts are excluded.",
        ["grasspelt"]="Grass Pelt boosts Defense on Grassy Terrain; terrain and stat boosts are excluded.",
        ["flashfire"]="Flash Fire grants Fire immunity; opposing ability suppression can bypass it.",
        ["levitate"]="Levitate grants Ground immunity; grounding effects and opposing ability suppression can bypass it."
    };

    public static string Key(string text) => string.Concat(text.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();

    public static double? TypeMultiplier(string attack, IEnumerable<string> defenders)
    {
        if (!Matchups.TryGetValue(attack, out var row)) return null;
        var types = defenders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (types.Length == 0 || types.Any(type => !row.ContainsKey(type))) return null;
        return types.Aggregate(1d, (value, type) => value * row[type]);
    }

    public static double? DefensiveMultiplier(PokemonDto pokemon, string attack)
    {
        var types = pokemon.Types.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (TypeMultiplier(attack, types) is not { } value) return null;
        var ability = Key(pokemon.Ability);
        var item = ability == "klutz" ? "" : Key(pokemon.HeldItem);
        if (item == "ringtarget" || attack == "Ground" && item == "ironball")
            value = types.Aggregate(1d, (current, type) => current * (Matchups[attack][type] == 0
                && (item == "ringtarget" || type == "Flying") ? 1 : Matchups[attack][type]));
        if (attack == "Ground" && ability is "levitate" or "eartheater" && (ability != "levitate" || item != "ironball")) return 0;
        if (attack == "Water" && ability is "waterabsorb" or "stormdrain" or "dryskin") return 0;
        if (attack == "Electric" && ability is "voltabsorb" or "lightningrod" or "motordrive") return 0;
        if (attack == "Fire" && ability is "flashfire" or "wellbakedbody") return 0;
        if (attack == "Grass" && ability == "sapsipper") return 0;
        if (ability == "wonderguard" && value <= 1) return 0;
        if (ability == "thickfat" && attack is "Fire" or "Ice" || ability is "heatproof" or "waterbubble" && attack == "Fire"
            || ability == "purifyingsalt" && attack == "Ghost") value *= 0.5;
        if (ability == "dryskin" && attack == "Fire") value *= 1.25;
        if (ability == "fluffy" && attack == "Fire") value *= 2;
        if (value > 1 && ability is "filter" or "solidrock" or "prismarmor") value *= 0.75;
        return value;
    }

    public static TeamAnalysisReport Analyze(IEnumerable<PokemonDto> source, IEnumerable<MoveDetailsDto> moveDetails, int expectedMembers = 0)
    {
        var selected = source.DistinctBy(pokemon => pokemon.Id).Take(6).ToArray();
        var available = selected.Where(pokemon => string.IsNullOrEmpty(pokemon.FusionHostId)).ToArray();
        var moves = moveDetails.DistinctBy(move => Key(move.Name)).ToDictionary(move => Key(move.Name));
        var members = available.Select(pokemon => Member(pokemon, moves)).ToArray();
        var byId = available.ToDictionary(pokemon => pokemon.Id);
        var defense = Types.Select(type => new TeamDefenseRow(type,
            members.Select(member => DefenseValue(member, byId[member.Id], type)).ToArray())).ToArray();
        var offense = Types.Select(type => new TeamCoverageRow(type, members.Select(member => {
            var best = member.Attacks.Select(move => new TeamCoverageValue(member, move.Name,
                AttackMultiplier(move, type, member.Ability))).OrderByDescending(value => value.Multiplier).FirstOrDefault();
            return best;
        }).OfType<TeamCoverageValue>().ToArray())).ToArray();
        return new(members, defense, offense, Math.Max(0, Math.Max(expectedMembers, selected.Length) - members.Length),
            members.Count(member => TypeMultiplier("Normal", member.Types) is null), members.Sum(member => member.UnknownMoves));
    }

    private static TeamDefenseValue DefenseValue(TeamAnalysisMember member, PokemonDto pokemon, string type)
    {
        var multiplier = DefensiveMultiplier(pokemon, type);
        var original = TypeMultiplier(type, member.Types);
        var item = Key(pokemon.Ability) == "klutz" ? "" : Key(pokemon.HeldItem);
        var removesImmunity = item == "ringtarget" || type == "Ground" && item == "ironball";
        var source = multiplier is null ? "Type data missing" : "Types";
        if (multiplier == 0) source = original == 0 && !removesImmunity ? "Type immunity" : member.Ability + " immunity";
        else if (original != multiplier) source = removesImmunity ? "Types + held item" : "Types + " + member.Ability;
        return new(member, multiplier, source);
    }

    private static TeamAnalysisMember Member(PokemonDto pokemon, IReadOnlyDictionary<string, MoveDetailsDto> moves)
    {
        var attacks = new List<TeamAnalysisAttack>(); var notes = new List<string>(); var unknown = 0;
        var ability = Key(pokemon.Ability); var item = ability == "klutz" ? "" : Key(pokemon.HeldItem);
        if (ConditionalAbilities.TryGetValue(ability, out var condition)) notes.Add(condition);
        if (ability == "wonderguard") notes.Add("Wonder Guard blocks most non-super-effective attacks; indirect damage and move-specific exceptions are excluded.");
        if (item == "airballoon") notes.Add("Air Balloon grants Ground immunity until popped; its temporary immunity is excluded from the base table.");
        if (item == "ironball") notes.Add("Iron Ball grounds this Pokémon, including Flying types and Levitate.");
        if (item == "ringtarget") notes.Add("Ring Target removes type immunities; ability immunities still apply.");
        foreach (var name in pokemon.Moves.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = Key(name);
            if (!moves.TryGetValue(id, out var move) && !(id.StartsWith("hiddenpower", StringComparison.Ordinal) && moves.TryGetValue("hiddenpower", out move)))
            { unknown++; continue; }
            if (move.Category == "Status") continue;
            if (move.Category is not ("Physical" or "Special")) { unknown++; continue; }
            if (FixedDamage.Contains(id)) { notes.Add($"{move.Name} does fixed, reactive or special damage and is excluded from type coverage."); continue; }
            if (ConditionalMoves.Contains(id)) { notes.Add($"{move.Name} has a conditional type and is excluded from reliable coverage."); continue; }
            var type = move.Type;
            if (id.StartsWith("hiddenpower", StringComparison.Ordinal))
            {
                string[] hpTypes = ["Fighting","Flying","Poison","Ground","Rock","Bug","Ghost","Steel","Fire","Water","Grass","Electric","Psychic","Ice","Dragon","Dark"];
                string[] order = ["hp","atk","def","spe","spa","spd"];
                var bits = order.Select((stat, i) => (pokemon.Ivs.GetValueOrDefault(stat, 31) & 1) << i).Sum();
                type = hpTypes[bits * 15 / 63]; id = "hiddenpower";
            }
            if (id == "judgment" && Plates.TryGetValue(item, out var plateType)) type = plateType;
            if (id == "multiattack" && item.EndsWith("memory", StringComparison.Ordinal))
                type = Types.FirstOrDefault(value => Key(value) + "memory" == item) ?? "Normal";
            if (id == "technoblast") type = item switch { "burndrive"=>"Fire", "dousedrive"=>"Water", "shockdrive"=>"Electric", "chilldrive"=>"Ice", _=>"Normal" };
            if (id == "revelationdance") type = pokemon.Types.FirstOrDefault() ?? "";
            if (id == "aurawheel")
            {
                type = Key(pokemon.DisplaySpecies).Contains("hangry", StringComparison.Ordinal) ? "Dark" : "Electric";
                notes.Add("Aura Wheel uses the current form here; Hunger Switch can change its type during battle.");
            }
            if (id == "ragingbull") type = Key(pokemon.DisplaySpecies) switch {
                var species when species.Contains("blaze", StringComparison.Ordinal) => "Fire",
                var species when species.Contains("aqua", StringComparison.Ordinal) => "Water", _ => "Normal" };
            if (id == "ivycudgel") type = item switch { "wellspringmask"=>"Water", "hearthflamemask"=>"Fire", "cornerstonemask"=>"Rock", _=>"Grass" };
            if (id == "terablast") notes.Add("Tera Blast uses its pre-Terastallization type here.");
            if (!NoConversion.Contains(id))
            {
                if (ability == "normalize") type = "Normal";
                else if (type == "Normal") type = ability switch { "aerilate"=>"Flying", "galvanize"=>"Electric", "pixilate"=>"Fairy", "refrigerate"=>"Ice", _=>type };
            }
            if (ability == "liquidvoice" && SoundMoves.Contains(id)) type = "Water";
            if (!Types.Contains(type)) { unknown++; continue; }
            attacks.Add(new(move.Name, type, id));
        }
        if (unknown > 0) notes.Add($"Move details missing for {unknown} selected move(s); attacking coverage is incomplete.");
        if (TypeMultiplier("Normal", pokemon.Types) is null) notes.Add("Type details are missing; defensive matchups are incomplete.");
        return new(pokemon.Id, string.IsNullOrWhiteSpace(pokemon.Nickname) ? pokemon.DisplaySpecies : pokemon.Nickname,
            pokemon.DisplaySpecies, pokemon.Ability, pokemon.Types.ToArray(), attacks, notes.Distinct().ToArray(), unknown);
    }

    private static double AttackMultiplier(TeamAnalysisAttack move, string defender, string abilityName)
    {
        var ability = Key(abilityName);
        var value = Matchups[move.Type][defender];
        if (move.Id == "freezedry" && defender == "Water") value = 2;
        if (move.Id == "flyingpress") value *= Matchups["Flying"][defender];
        if (move.Id == "thousandarrows" && move.Type == "Ground" && defender == "Flying") value = 1;
        if (defender == "Ghost" && move.Type is "Normal" or "Fighting" && ability is "scrappy" or "mindseye") value = 1;
        return value;
    }
}
