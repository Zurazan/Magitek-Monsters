using System;
using System.Text.Json;
using MagitekMonsters.Models;

namespace MagitekMonsters.Contracts;

public static class LiveProtocol
{
    public const string Path = "/api/live";
    public const int Version = 1;
}
public sealed record LiveCommand(string Operation, string? Id = null, BattleChoiceRequest? BattleChoice = null, RaidChooseRequest? RaidChoice = null);
public sealed record LiveError(int Status, string Code, string Message, int? RetryAfterSeconds = null);
public sealed record LiveReply(BattleSnapshotDto? Battle = null, RaidRoomDto? Raid = null, PvpStatusDto? Pvp = null, LiveError? Error = null);
public sealed record LiveHello(int Version, string ProtocolVersion, int OnlineTrainers, DateTimeOffset ServerTime);
public sealed record LiveEvent(string Name, JsonElement Data, BattleSnapshotDto? Battle = null, RaidRoomDto? Raid = null);
public interface ILiveClient
{
    System.Threading.Tasks.Task Event(LiveEvent update);
}
