using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private RecurringEventScheduleDto? recurringSchedule;
    private DateTimeOffset eventLoadedAt;
    private DateTimeOffset nextEventRefresh;
    private int eventPage;
    private readonly ConcurrentDictionary<string, string> eventRequests = new();

    private void BackgroundRecurringEvents(DateTimeOffset now)
    {
        if (busy || backgroundRefreshing || now < nextEventRefresh) return;
        var retryAt = now.AddSeconds(20);
        nextEventRefresh = retryAt;
        RunBackground(async token =>
        {
            var schedule = await plugin.Api.GetRecurringEvents(token);
            return () =>
            {
                if (nextEventRefresh == retryAt) ApplyRecurringEvents(schedule);
            };
        }, () => { }, sharedBackoff: false, deferLiveUpdates: false);
    }

    private async Task RefreshRecurringEvents() =>
        ApplyRecurringEvents(await InSession(plugin.Api.GetRecurringEvents()));

    private void ApplyRecurringEvents(RecurringEventScheduleDto schedule)
    {
        if (!plugin.Api.Catalogs.TryApply([schedule], () =>
        {
            recurringSchedule = schedule; eventLoadedAt = DateTimeOffset.UtcNow; nextEventRefresh = eventLoadedAt.AddMinutes(1);
        })) { RetryCatalogs(); nextEventRefresh = DateTimeOffset.UtcNow.AddSeconds(5); }
    }

    private static bool EventIsLive(RecurringEventScheduleDto schedule, RecurringEventDto entry, DateTimeOffset now) =>
        schedule.Active?.OccurrenceId == entry.OccurrenceId && now >= entry.StartsAt && now < entry.EndsAt;

    private EncounterDto? SavedEventEncounter(DateTimeOffset now) =>
        trainer?.ActiveEncounter is { Status: "active" } encounter && IsEventEncounter(encounter)
            && (!DateTimeOffset.TryParse(encounter.ExpiresAt, out var expires) || expires > now) ? encounter : null;

    private string? EventEntryRestriction(RecurringEventScheduleDto schedule, RecurringEventDto entry, DateTimeOffset now)
    {
        if (!EventIsLive(schedule, entry, now)) return "This event is not accepting entries yet.";
        if (SavedEventEncounter(now) is not null) return "Resume your saved battle first.";
        if (busy) return "Wait for the current action to finish.";
        if (CollectionLocked) return "Finish your current battle, run or matchmaking first.";
        if (trainer is null || trainer.Party.Count == 0) return "Add Pokémon to your party first.";
        if (trainer.Party.All(pokemon => pokemon.Vitals?.Hp == 0)) return "Heal your party at the Pokémon Center first.";
        if (entry.Mode == "sanctuary")
        {
            var champion = RegionalLeagueProgress.HasChampion(trainer.PveProgress.DefeatedOpponentIds);
            var permit = trainer.Legendary.Permits.Contains(entry.BossId);
            if (!champion && !permit) return "Requires a Champion victory and the Sanctuary permit.";
            if (!champion) return "Defeat a regional League Champion first.";
            return permit ? null : "Unlock the permanent Zarude permit in Sanctuaries.";
        }
        if (schedule.NextEncounterAt is { } ready && ready > now)
            return $"Next search in {EventTimeLeft(ready - now)}.";
        return null;
    }

    private async Task EnterRecurringEvent(RecurringEventDto entry, string species, string[] partyIds)
    {
        if (entry.Mode == "sanctuary")
        {
            battle = await InSession(plugin.Api.StartEventSanctuary(entry.BossId, entry.OccurrenceId, partyIds));
            eventBattleId = battle.Id;
            nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        else
        {
            var key = entry.OccurrenceId + ":" + species;
            var request = eventRequests.GetOrAdd(key, _ => Guid.NewGuid().ToString());
            var found = await InSession(plugin.Api.SearchRecurringEvent(entry.OccurrenceId, species, request));
            eventRequests.TryRemove(key, out _);
            if (found.Status == "active")
            {
                trainer!.ActiveEncounter = found;
                await InSession(ResumeWildBattle(found.Id, true));
                mainSection = 0;
            }
        }
        trainer = await InSession(plugin.Api.GetMe());
        await InSession(RefreshRecurringEvents());
    }
}
