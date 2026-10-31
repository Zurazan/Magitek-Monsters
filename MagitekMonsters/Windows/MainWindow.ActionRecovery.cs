using System;
using System.Threading.Tasks;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static bool SkipActionRefresh(Exception exception) => exception is StaleSessionException or UnconfirmedActionException
        or ApiException { Code: "mutation_committed" };

    private async Task<bool> RecoverAction(bool saved)
    {
        var generation = foregroundGeneration;
        var session = sessionResponses.Generation;
        var account = trainer?.User.Id;
        if (account is null) return false;
        SetStatus(saved ? "Saved. Refreshing trainer data..." : "Action outcome unknown. Refreshing trainer data...", !saved);
        var refreshed = false;
        try
        {
            await InSession(RefreshTrainer());
            refreshed = trainer?.User.Id == account;
        }
        catch (StaleSessionException) { return false; }
        catch (Exception) when (!Current()) { return false; }
        catch (ApiException exception) when (exception.StatusCode == 426)
        {
            plugin.Api.Update.Require(exception.Message);
            SetStatus(saved ? "Saved. " + exception.Message : "Action outcome unknown. " + exception.Message, true);
            return false;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Magitek Monsters could not refresh trainer data after an action.");
        }
        if (!Current()) return false;
        SetStatus((saved, refreshed) switch
        {
            (true, true) => "Saved. Trainer data refreshed.",
            (true, false) => "Saved. Updated trainer data unavailable. Refresh before repeating.",
            (false, true) => "Action outcome unknown. Trainer data refreshed. Check before repeating.",
            _ => "Action outcome unknown. Trainer data unavailable. Refresh before repeating."
        }, !saved || !refreshed);
        return refreshed;

        bool Current() => generation == foregroundGeneration && sessionResponses.IsCurrent(session)
            && !disposeCancellation.IsCancellationRequested && trainer?.User.Id == account;
    }
}
