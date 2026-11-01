using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;

namespace MagitekMonsters.Services;

public sealed class DalamudNotifications(INotificationManager manager, Action<GameNotice> open, Func<string> readScope) : IDisposable
{
    private readonly Dictionary<string, (GameNotice Notice, IActiveNotification Active)> active = new(StringComparer.Ordinal);
    private long generation;
    private bool disposed;
    private bool enabled = true;
    private string lastFeedback = "";
    private DateTimeOffset feedbackAt;

    public void Update(GameNoticeBatch batch, bool allow)
    {
        if (disposed) return;
        if (batch.Reset || enabled != allow) Clear();
        enabled = allow;
        foreach (var entry in active.ToArray())
            if (entry.Value.Active.DismissReason is not null || entry.Value.Notice.Target != GameNoticeTarget.None && !batch.ActiveKeys.Contains(entry.Key))
                Remove(entry.Key);
        if (enabled) foreach (var notice in batch.New) Show(notice);
    }

    public void Feedback(string message, bool error)
    {
        if (disposed || !enabled || string.IsNullOrWhiteSpace(message)) return;
        var fingerprint = error + ":" + message;
        var now = DateTimeOffset.UtcNow;
        if (lastFeedback == fingerprint && now - feedbackAt < TimeSpan.FromSeconds(15)) return;
        lastFeedback = fingerprint; feedbackAt = now;
        Show(new("feedback", error ? "Action failed" : "Notice", message, error ? GameNoticeKind.Error : GameNoticeKind.Info));
    }

    private void Show(GameNotice notice)
    {
        if (notice.ExpiresAt <= DateTimeOffset.UtcNow) return;
        Remove(notice.Key);
        if (active.Count >= 6) Remove(active.Keys.First());
        var notification = manager.AddNotification(new Notification
        {
            Title = "Magitek Monsters / " + notice.Title,
            Content = notice.Content,
            MinimizedText = "Magitek Monsters",
            Type = notice.Kind switch { GameNoticeKind.Error => NotificationType.Error, GameNoticeKind.Warning => NotificationType.Warning,
                GameNoticeKind.Success => NotificationType.Success, _ => NotificationType.Info },
            InitialDuration = TimeSpan.FromSeconds(notice.Kind == GameNoticeKind.Error ? 10 : 8),
            HardExpiry = notice.ExpiresAt?.UtcDateTime ?? DateTime.MaxValue,
            RespectUiHidden = true,
        });
        active[notice.Key] = (notice, notification);
        var owner = generation;
        var scope = readScope();
        if (notice.ActionLabel is not null)
            notification.DrawActions += args =>
            {
                if (disposed || !enabled || generation != owner || readScope() != scope || args.Notification.DismissReason is not null
                    || !active.TryGetValue(notice.Key, out var current) || !ReferenceEquals(current.Active, args.Notification)) return;
                if (ImGui.Button(notice.ActionLabel))
                {
                    if (notice.ExpiresAt is null || notice.ExpiresAt > DateTimeOffset.UtcNow) open(notice);
                    args.Notification.DismissNow();
                }
            };
    }
    private void Remove(string key)
    {
        if (active.Remove(key, out var entry)) entry.Active.DismissNow();
    }
    public void Clear()
    {
        generation++;
        foreach (var entry in active.Values) entry.Active.DismissNow();
        active.Clear(); lastFeedback = "";
    }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); }
}
