using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class FusionOptionDto
{
    public string PokemonId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string TargetSpecies { get; set; } = string.Empty;
    public string PartnerSpecies { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public bool Available { get; set; }
    public bool NeedsForgetMove { get; set; }
    public List<string> RemovedMoves { get; set; } = [];
    public List<string> AddedMoves { get; set; } = [];
    public string FusionId { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
}
