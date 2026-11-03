using System;
using System.Linq;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly GameNotificationFeed notificationFeed = new();
    private Func<GameNotice, bool>? notificationVisibility;
    private string notificationOwner = "";
    private long notificationSession = -1;
    private string notificationScope = "";
    internal string NotificationScope
    {
        get
        {
            var owner = trainer?.User.Id ?? ""; var session = sessionResponses.Generation;
            if (notificationOwner != owner || notificationSession != session)
            {
                notificationOwner = owner; notificationSession = session;
                notificationScope = owner + ":" + session;
            }
            return notificationScope;
        }
    }

    private void UpdateNotifications()
    {
        var batch = notificationFeed.Observe(trainer?.User.Id ?? "", sessionResponses.Generation, trainer?.Gifts, pvpStatus, raidRoom,
            plugin.Connection.State, plugin.Api.Update.Required ? plugin.Api.Update.Message : null, trading);
        plugin.Notifications?.Update(batch, plugin.Configuration.NotificationsEnabled, notificationVisibility ??= IsNotificationVisible);
    }

    private bool IsNotificationVisible(GameNotice notice) => IsOpen && (notice.Target switch
    {
        GameNoticeTarget.Gifts => mainSection == 13,
        GameNoticeTarget.Trades => mainSection == 16 && tradingTab == (notice.Kind == GameNoticeKind.Success ? 2 : 0),
        GameNoticeTarget.Challenge => mainSection == 4 && onlineView == 2,
        GameNoticeTarget.Match => mainSection == 4 && pvpStatus?.Battle?.Id == notice.Subject,
        GameNoticeTarget.RaidReady or GameNoticeTarget.RaidBattle or GameNoticeTarget.RaidCapture => mainSection == 12 && raidRoom?.Id == notice.Subject,
        GameNoticeTarget.Connection => true,
        _ => false
    });

    internal void OpenNotification(GameNotice notice)
    {
        if (disposeCancellation.IsCancellationRequested) return;
        IsOpen = true;
        switch (notice.Target)
        {
            case GameNoticeTarget.Trades: if (trainer is not null) { mainSection = 16; requestTradingTab = notice.Kind == GameNoticeKind.Success ? 2 : 0; tradeRefreshRequested = true; } break;
            case GameNoticeTarget.Gifts: if (trainer is not null) { mainSection = 13; giftPage = 0; } break;
            case GameNoticeTarget.Challenge:
                if (pvpStatus?.Challenges.Any(invite => invite.Id == notice.Subject && invite.ToUserId == trainer?.User.Id
                    && invite.Status == "pending" && DateTimeOffset.TryParse(invite.ExpiresAt, out var at) && at > DateTimeOffset.UtcNow) == true)
                { mainSection = 4; onlineView = 2; onlineInvitationId = notice.Subject; pvpTeamError = ""; }
                break;
            case GameNoticeTarget.Match: if (pvpStatus?.Battle?.Id == notice.Subject) mainSection = 4; break;
            case GameNoticeTarget.RaidReady:
            case GameNoticeTarget.RaidBattle:
            case GameNoticeTarget.RaidCapture: if (raidRoom?.Id == notice.Subject) mainSection = 12; break;
            case GameNoticeTarget.Update: Plugin.CommandManager.ProcessCommand("/xlplugins"); break;
            case GameNoticeTarget.Login: if (trainer is not null) Disconnect(false); break;
        }
    }
}
