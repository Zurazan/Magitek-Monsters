using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class EvTrainingPlanDto
{
    public string PokemonId { get; set; } = string.Empty;
    public string PreviewHash { get; set; } = string.Empty;
    public int Level { get; set; }
    public Dictionary<string, int> CurrentEvs { get; set; } = [];
    public Dictionary<string, int> TargetEvs { get; set; } = [];
    public Dictionary<string, int> CurrentStats { get; set; } = [];
    public Dictionary<string, int> TargetStats { get; set; } = [];
    public List<EvTrainingItemDto> Items { get; set; } = [];
    public int FriendshipBefore { get; set; }
    public int FriendshipAfter { get; set; }
    public bool CanApply { get; set; }
}

public sealed class EvTrainingItemDto
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int Owned { get; set; }
    public int Missing { get; set; }
}
