using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public enum GameNoticeKind { Info, Success, Warning, Error }
public enum GameNoticeTarget { None, Gifts, Challenge, Match, RaidReady, RaidBattle, RaidCapture, Connection, Login, Update }
public sealed record GameNotice(string Key, string Title, string Content, GameNoticeKind Kind = GameNoticeKind.Info,
    GameNoticeTarget Target = GameNoticeTarget.None, string? Subject = null, string? ActionLabel = null, DateTimeOffset? ExpiresAt = null);
public sealed record GameNoticeBatch(bool Reset, IReadOnlyList<GameNotice> New, IReadOnlySet<string> ActiveKeys);

public sealed class GameNotificationFeed(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> history = new();
    private string ownerScope = "";
    private long sessionScope = -1;
    private int previousUnread;
    private bool wasConnected;
    private DateTimeOffset? interruptedAt;
    private int interruption;
    private NoticeInputs? previousInputs;
    private (string Id, string? SeenAt)[] giftInputs = [];
    private ChallengeInputs[] challengeInputs = [];
    private GameNoticeBatch? idleBatch;
    private DateTimeOffset observedAt;
    private DateTimeOffset refreshAt = DateTimeOffset.MinValue;

    private readonly record struct ChallengeInputs(string Id, string From, string To, string Name, string Format, string Status, string ExpiresAt);
    private readonly record struct NoticeInputs(string Owner, long Session, int Unread, int Gifts, int Challenges,
        string? Match, string? MatchStatus, string? Opponent, string? Raid, string? RaidStatus, bool Host, int Players, bool Ready,
        string? Species, string? Capture, DateTimeOffset? CaptureExpiry, ServerConnectionState Connection, string? Update);

    private bool InputsChanged(string owner, long session, GiftInboxDto? gifts, PvpStatusDto? pvp, RaidRoomDto? raid,
        ServerConnectionState connection, string? update)
    {
        var ready = true;
        if (raid is not null) foreach (var player in raid.Players) ready &= player.Ready;
        var current = new NoticeInputs(owner, session, gifts?.Unread ?? 0, gifts?.Entries.Count ?? 0, pvp?.Challenges.Count ?? 0,
            pvp?.Battle?.Id, pvp?.Battle?.Status, pvp?.Battle?.OpponentName, raid?.Id, raid?.Status, raid?.Host ?? false,
            raid?.Players.Count ?? 0, ready, raid?.Den.Species, raid?.CaptureState, raid?.CaptureExpiresAt, connection, update);
        var changed = previousInputs != current;
        if (giftInputs.Length < current.Gifts) giftInputs = new (string, string?)[current.Gifts];
        for (var index = 0; index < current.Gifts; index++)
        {
            var gift = gifts!.Entries[index]; var value = (gift.Id, gift.SeenAt);
            changed |= giftInputs[index] != value; giftInputs[index] = value;
        }
        if (challengeInputs.Length < current.Challenges) challengeInputs = new ChallengeInputs[current.Challenges];
        for (var index = 0; index < current.Challenges; index++)
        {
            var invite = pvp!.Challenges[index];
            var value = new ChallengeInputs(invite.Id, invite.FromUserId, invite.ToUserId, invite.FromName, invite.FormatName, invite.Status, invite.ExpiresAt);
            changed |= challengeInputs[index] != value; challengeInputs[index] = value;
        }
        if (previousInputs is { } previous && (previous.Owner != owner || previous.Session != session))
        {
            if (giftInputs.Length > current.Gifts) Array.Clear(giftInputs, current.Gifts, giftInputs.Length - current.Gifts);
            if (challengeInputs.Length > current.Challenges) Array.Clear(challengeInputs, current.Challenges, challengeInputs.Length - current.Challenges);
        }
        previousInputs = current;
        return changed;
    }

    public GameNoticeBatch Observe(string owner, long session, GiftInboxDto? gifts, PvpStatusDto? pvp, RaidRoomDto? raid,
        ServerConnectionState connection, string? requiredUpdate = null)
    {
        var now = clock.GetUtcNow();
        var changed = InputsChanged(owner, session, gifts, pvp, raid, connection, requiredUpdate);
        if (!changed && idleBatch is not null && now >= observedAt && now < refreshAt) return idleBatch;
        observedAt = now; refreshAt = DateTimeOffset.MaxValue;
        var reset = ownerScope != owner || sessionScope != session;
        if (reset)
        {
            ownerScope = owner; sessionScope = session; seen.Clear(); history.Clear(); previousUnread = 0;
            wasConnected = false; interruptedAt = null; interruption = 0;
        }
        var notices = new List<GameNotice>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        bool Remember(string key)
        {
            if (!seen.Add(key)) return false;
            history.Enqueue(key);
            if (history.Count > 512) seen.Remove(history.Dequeue());
            return true;
        }
        void Add(GameNotice notice, string? receipt = null)
        {
            active.Add(notice.Key);
            if (Remember(receipt ?? notice.Key)) notices.Add(notice);
        }
        if (owner.Length > 0)
        {
            if (gifts is { Unread: > 0 })
            {
                active.Add("gifts");
                var unread = gifts.Entries.Where(gift => gift.SeenAt is null && gift.Id.Length > 0).ToArray();
                var newGifts = false;
                foreach (var gift in unread) newGifts |= Remember("gift:" + gift.Id);
                if (newGifts || unread.Length == 0 && gifts.Unread > previousUnread)
                    notices.Add(new("gifts", "Mystery Gifts", $"{gifts.Unread:N0} unread gift{(gifts.Unread == 1 ? " is" : "s are")} already in your collection.",
                        GameNoticeKind.Success, GameNoticeTarget.Gifts, ActionLabel: "View gifts"));
            }
            previousUnread = gifts?.Unread ?? 0;
            foreach (var invite in pvp?.Challenges ?? [])
                if (invite.ToUserId == owner && invite.FromUserId != owner && invite.Status == "pending"
                    && DateTimeOffset.TryParse(invite.ExpiresAt, out var expiry) && expiry > now)
                {
                    if (expiry < refreshAt) refreshAt = expiry;
                    Add(new("challenge:" + invite.Id, "Friendly challenge", $"{invite.FromName} challenges you to {invite.FormatName}.",
                        Target: GameNoticeTarget.Challenge, Subject: invite.Id, ActionLabel: "Review challenge", ExpiresAt: expiry));
                }
            if (pvp?.Battle is { Status: "active" } match)
                Add(new("match:" + match.Id, "Online battle ready", $"Your battle against {match.OpponentName} is ready.",
                    Target: GameNoticeTarget.Match, Subject: match.Id, ActionLabel: "Open battle"));
            if (raid is not null)
            {
                if (raid.Status == "lobby" && raid.Host && raid.Players.Count > 1 && raid.Players.All(player => player.Ready))
                    Add(new("raid-ready:" + raid.Id, "Your raid party is ready", $"Everyone is ready for {raid.Den.Species}. Open the den to start.",
                        Target: GameNoticeTarget.RaidReady, Subject: raid.Id, ActionLabel: "Open den"));
                if (raid.Status == "active")
                    Add(new("raid-battle:" + raid.Id, "Raid started", $"The raid against {raid.Den.Species} has started.",
                        Target: GameNoticeTarget.RaidBattle, Subject: raid.Id, ActionLabel: "Open raid"));
                if (raid.CaptureState == "pending" && raid.CaptureExpiresAt is { } captureExpiry && captureExpiry > now)
                {
                    if (captureExpiry < refreshAt) refreshAt = captureExpiry;
                    Add(new("raid-capture:" + raid.Id, "Raid capture available", $"Use one owned Poké Ball to catch {raid.Den.Species}. The catch is guaranteed.",
                        GameNoticeKind.Success, GameNoticeTarget.RaidCapture, raid.Id, "Open capture", raid.CaptureExpiresAt));
                }
            }
        }
        if (connection == ServerConnectionState.Connected) { wasConnected = true; interruptedAt = null; }
        else if (connection is ServerConnectionState.Reconnecting or ServerConnectionState.Syncing && wasConnected)
        {
            if (interruptedAt is null) { interruptedAt = now; interruption++; }
            if (now - interruptedAt >= TimeSpan.FromSeconds(5))
                Add(new("connection", "Connection interrupted", "Reconnecting to Magitek Monsters. Your progress stays on the server.",
                    GameNoticeKind.Warning, GameNoticeTarget.Connection, ActionLabel: "Open plugin"), "connection:" + interruption);
            else if (interruptedAt.Value.AddSeconds(5) < refreshAt) refreshAt = interruptedAt.Value.AddSeconds(5);
        }
        else interruptedAt = null;
        if (connection == ServerConnectionState.Unauthorized)
            Add(new("login", "Sign in again", "Your Magitek Monsters session has ended. Sign in with XIVAuth to reconnect.",
                GameNoticeKind.Warning, GameNoticeTarget.Login, ActionLabel: "Open sign-in"));
        if (!string.IsNullOrWhiteSpace(requiredUpdate))
            Add(new("update", "Client update required", requiredUpdate, GameNoticeKind.Warning,
                GameNoticeTarget.Update, ActionLabel: "Open Plugin Installer"));
        idleBatch = new(false, Array.Empty<GameNotice>(), active);
        return new(reset, notices, active);
    }
}
