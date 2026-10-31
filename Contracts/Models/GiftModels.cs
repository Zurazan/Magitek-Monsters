using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class GiftInboxDto
{
    public int Unread { get; set; }
    public int Total { get; set; }
    public List<GiftReceiptDto> Entries { get; set; } = [];
}
public sealed class GiftReceiptDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? SeenAt { get; set; }
    public PokemonDto Pokemon { get; set; } = new();
    public string? PokemonId { get; set; }
    public int? PartySlot { get; set; }
    public int? BoxNumber { get; set; }
    public int? BoxSlot { get; set; }
}
