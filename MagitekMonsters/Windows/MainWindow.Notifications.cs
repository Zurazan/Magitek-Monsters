using System;
using System.Linq;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly GameNotificationFeed notificationFeed = new();
    private CompanionNotice? deliveredFeedback;
    internal string NotificationScope => (trainer?.User.Id ?? "") + ":" + sessionResponses.Generation;

    private void UpdateNotifications()
    {
        var batch = notificationFeed.Observe(trainer?.User.Id ?? "", sessionResponses.Generation, trainer?.Gifts, pvpStatus, raidRoom,
            plugin.Connection.State, plugin.Api.Update.Required ? plugin.Api.Update.Message : null);
        plugin.Notifications?.Update(batch, plugin.Configuration.NotificationsEnabled);
        if (batch.Reset) deliveredFeedback = headerFeedback.Current;
        var feedback = headerFeedback.Current;
        if (ReferenceEquals(deliveredFeedback, feedback) || feedback is null || busy) return;
        deliveredFeedback = feedback;
        if (!feedback.IsError && (battle?.Status == "active" || pvpStatus?.Battle?.Status == "active" || raidRoom?.Status == "active"
            || feedback.Message is "Raid action saved." or "Searching for another Trainer..." or "PvP status synchronized.")) return;
        plugin.Notifications?.Feedback(feedback.Message, feedback.IsError);
    }

    internal void OpenNotification(GameNotice notice)
    {
        if (disposeCancellation.IsCancellationRequested) return;
        IsOpen = true;
        switch (notice.Target)
        {
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
