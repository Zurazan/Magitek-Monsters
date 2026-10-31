using System;
using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class RecurringEventScheduleDto
{
    public DateTimeOffset ServerTime { get; set; }
    public RecurringEventDto? Active { get; set; }
    public DateTimeOffset? NextEncounterAt { get; set; }
    public List<RecurringEventDto> Upcoming { get; set; } = [];
}
public sealed class RecurringEventDto
{
    public string Id { get; set; } = string.Empty;
    public string OccurrenceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string BossId { get; set; } = string.Empty;
    public int CooldownSeconds { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool Manual { get; set; }
    public List<string> Species { get; set; } = [];
    public List<string> ShinyLockedSpecies { get; set; } = [];
}
