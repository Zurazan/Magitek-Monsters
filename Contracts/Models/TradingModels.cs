using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class TradingSnapshotDto
{
    public TradeSessionDto? Active { get; set; }
    public WonderDepositDto? Wonder { get; set; }
    public List<TradeReceiptDto> Receipts { get; set; } = [];
    public int Unread { get; set; }
}
public sealed class TradeSessionDto
{
    public string Id { get; set; } = "";
    public int Revision { get; set; }
    public string Status { get; set; } = "";
    public bool YouAreSender { get; set; }
    public string PartnerUid { get; set; } = "";
    public string PartnerName { get; set; } = "";
    public PokemonDto? YourPokemon { get; set; }
    public PokemonDto? TheirPokemon { get; set; }
    public bool YouConfirmed { get; set; }
    public bool TheyConfirmed { get; set; }
    public string ExpiresAt { get; set; } = "";
}
public sealed class WonderDepositDto
{
    public string Id { get; set; } = "";
    public PokemonDto Pokemon { get; set; } = new();
    public string CreatedAt { get; set; } = "";
}
public sealed class TradeReceiptDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string PartnerUid { get; set; } = "";
    public string PartnerName { get; set; } = "";
    public PokemonDto Given { get; set; } = new();
    public PokemonDto Received { get; set; } = new();
    public string CreatedAt { get; set; } = "";
    public string? SeenAt { get; set; }
}
