using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class MoveTypeHintsDto
{
    public MoveTypeHintDto Normal { get; set; } = new();
    public MoveTypeHintDto? Mega { get; set; }
    public MoveTypeHintDto? Terastallize { get; set; }
    public MoveTypeHintDto? Ultra { get; set; }
}

public sealed class MoveTypeHintDto
{
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public string Note { get; set; } = "";
    public List<MoveTargetHintDto> Targets { get; set; } = [];
}

public sealed class MoveTargetHintDto
{
    public int Target { get; set; }
    public string Name { get; set; } = "";
    public double? Multiplier { get; set; }
    public string? Note { get; set; }
}
