using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class FormOptionDto
{
    public string PokemonId { get; set; } = string.Empty;
    public string TargetSpecies { get; set; } = string.Empty;
    public string TargetAbility { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public bool Current { get; set; }
    public bool Available { get; set; }
    public bool DayOnly { get; set; }
    public string SignatureMove { get; set; } = string.Empty;
    public List<string> RemovedMoves { get; set; } = [];
    public List<string> MoveChanges { get; set; } = [];
    public bool NeedsForgetMove { get; set; }
}
