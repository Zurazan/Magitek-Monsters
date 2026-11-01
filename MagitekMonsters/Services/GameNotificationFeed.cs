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
    private string scope = "";
    private int previousUnread;
    private bool wasConnected;
    private DateTimeOffset? interruptedAt;
    private int interruption;

    public GameNoticeBatch Observe(string owner, long session, GiftInboxDto? gifts, PvpStatusDto? pvp, RaidRoomDto? raid,
        ServerConnectionState connection, string? requiredUpdate = null)
    {
        var nextScope = owner + ":" + session;
        var reset = scope != nextScope;
        if (reset)
        {
            scope = nextScope; seen.Clear(); history.Clear(); previousUnread = 0;
            wasConnected = false; interruptedAt = null; interruption = 0;
        }
        var now = clock.GetUtcNow();
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
                    Add(new("challenge:" + invite.Id, "Friendly challenge", $"{invite.FromName} challenges you to {invite.FormatName}.",
                        Target: GameNoticeTarget.Challenge, Subject: invite.Id, ActionLabel: "Review challenge", ExpiresAt: expiry));
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
                if (raid.CaptureState == "pending" && raid.CaptureExpiresAt > now)
                    Add(new("raid-capture:" + raid.Id, "Raid capture available", $"Use one owned Poké Ball to catch {raid.Den.Species}. The catch is guaranteed.",
                        GameNoticeKind.Success, GameNoticeTarget.RaidCapture, raid.Id, "Open capture", raid.CaptureExpiresAt));
            }
        }
        if (connection == ServerConnectionState.Connected) { wasConnected = true; interruptedAt = null; }
        else if (connection is ServerConnectionState.Reconnecting or ServerConnectionState.Syncing && wasConnected)
        {
            if (interruptedAt is null) { interruptedAt = now; interruption++; }
            if (now - interruptedAt >= TimeSpan.FromSeconds(5))
                Add(new("connection", "Connection interrupted", "Reconnecting to Magitek Monsters. Your progress stays on the server.",
                    GameNoticeKind.Warning, GameNoticeTarget.Connection, ActionLabel: "Open plugin"), "connection:" + interruption);
        }
        else interruptedAt = null;
        if (connection == ServerConnectionState.Unauthorized)
            Add(new("login", "Sign in again", "Your Magitek Monsters session has ended. Sign in with XIVAuth to reconnect.",
                GameNoticeKind.Warning, GameNoticeTarget.Login, ActionLabel: "Open sign-in"));
        if (!string.IsNullOrWhiteSpace(requiredUpdate))
            Add(new("update", "Client update required", requiredUpdate, GameNoticeKind.Warning,
                GameNoticeTarget.Update, ActionLabel: "Open Plugin Installer"));
        return new(reset, notices, active);
    }
}
