using System;
using System.Collections.Generic;

namespace MagitekMonsters.Models;

public sealed class FriendCountsDto
{
    public int Online { get; set; }
    public int Offline { get; set; }
    public int NotMutual { get; set; }
    public int Incoming { get; set; }
    public int Outgoing { get; set; }
    public int Blocked { get; set; }
    public int DenInvites { get; set; }
    public int Group(string group) => group switch { "online" => Online, "offline" => Offline, "notmutual" => NotMutual,
        "incoming" => Incoming, "outgoing" => Outgoing, "blocked" => Blocked, "dens" => DenInvites, _ => 0 };
}
public sealed class FriendEntryDto
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TrainerUid { get; set; } = "";
    public string VanityCode { get; set; } = "";
    public bool Mutual { get; set; }
    public bool? Online { get; set; }
    public string? RequestId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? RoomId { get; set; }
    public string? DenSpecies { get; set; }
    public int Stars { get; set; }
    public bool Pending { get; set; }
}
public sealed class FriendPageDto
{
    public string Group { get; set; } = "online";
    public string Search { get; set; } = "";
    public int Offset { get; set; }
    public int Limit { get; set; } = 50;
    public int Total { get; set; }
    public FriendCountsDto Counts { get; set; } = new();
    public List<FriendEntryDto> Entries { get; set; } = [];
    public FriendEntryDto? Selected { get; set; }
    public DateTimeOffset ServerTime { get; set; }
}
public sealed record FriendMutationDto(string Result);
public sealed record FriendDenInviteDto(string InviteId, string RoomId, string Code, DateTimeOffset ExpiresAt);
public sealed record FriendInboxDto(int IncomingCount, IReadOnlyList<FriendEntryDto> Incoming, int DenCount, IReadOnlyList<FriendEntryDto> Dens);
