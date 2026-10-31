using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private RecurringEventScheduleDto? recurringSchedule;
    private DateTimeOffset eventLoadedAt;
    private DateTimeOffset nextEventRefresh;
    private int eventPage;
    private readonly ConcurrentDictionary<string, string> eventRequests = new();
    private async Task RefreshRecurringEvents()
    {
        var schedule = await InSession(plugin.Api.GetRecurringEvents());
        if (recurringSchedule?.Active?.OccurrenceId != schedule.Active?.OccurrenceId) eventPage = 0;
        recurringSchedule = schedule; eventLoadedAt = DateTimeOffset.UtcNow; nextEventRefresh = eventLoadedAt.AddMinutes(1);
    }
    private void DrawRecurringEvents()
    {
        if (trainer is null) return;
        if (battle is { Kind: "trainer", OpponentId: "legendary-zarude" })
        { DrawBattleConsole(battle); return; }
        DrawSectionHeader("RECURRING EVENTS", "Monthly UTC rotation / Permanent catches / No event currency");
        if (!busy && DateTimeOffset.UtcNow >= nextEventRefresh)
        {
            nextEventRefresh = DateTimeOffset.UtcNow.AddSeconds(20);
            Run(RefreshRecurringEvents, "Loading the event calendar...");
        }
        if (recurringSchedule is null) { ImGui.TextDisabled("Loading event schedule..."); return; }
        var now = recurringSchedule.ServerTime + (DateTimeOffset.UtcNow - eventLoadedAt);
        if (trainer.ActiveEncounter is { Status: "active" } encounter && (encounter.HabitatId.StartsWith("event:", StringComparison.Ordinal) || encounter.Pokemon.Source == "recurring-event"))
        {
            ImGui.TextColored(PokemonGreen, "YOUR EVENT CAPTURE BATTLE IS SAVED");
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Resume capture battle", new Vector2(230, 34), PokemonGreen))
                Run(async () => { await InSession(ResumeWildBattle(encounter.Id)); mainSection = 0; }, "Resuming event capture...");
            ImGui.EndDisabled(); ImGui.Separator();
        }
        if (recurringSchedule.Active is { } active && now < active.EndsAt)
        {
            ImGui.TextColored(PokemonYellow, active.Name.ToUpperInvariant());
            ImGui.TextWrapped(active.Description);
            var left = active.EndsAt - now;
            ImGui.TextColored(PokemonBlue, $"Ends {active.EndsAt:dd MMM yyyy HH:mm} UTC / {Math.Max(0, left.Days)}d {Math.Max(0, left.Hours)}h remaining");
            if (active.Manual) ImGui.TextDisabled("Administrator override active. Upcoming dates below show the regular rotation.");
            ImGui.TextWrapped("Caught Pokémon and form history stay yours after the event. Events do not affect Mew access. Existing encounters can finish within their normal expiry.");
            var cooling = recurringSchedule.NextEncounterAt is { } ready && ready > now;
            if (cooling && active.Mode == "encounter") ImGui.TextColored(PokemonOrange, $"Next search in {Math.Ceiling((recurringSchedule.NextEncounterAt!.Value - now).TotalSeconds)} seconds.");
            DrawPager("EventForms", ref eventPage, active.Species.Count, 6);
            foreach (var species in active.Species.Skip(eventPage * 6).Take(6))
            {
                ImGui.PushID(species);
                if (ImGui.BeginTable("EventForm", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthFixed, 70); ImGui.TableSetupColumn("Form", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableNextColumn(); DrawPokemonIcon(species, false, new Vector2(62, 62)); ImGui.TableNextColumn();
                    var caught = trainer.Pokedex.CaughtForms.Contains(species);
                    ImGui.TextColored(caught ? PokemonGreen : PokemonBlue, species); ImGui.SameLine(); ImGui.TextDisabled(caught ? "REGISTERED" : "UNDISCOVERED");
                    ImGui.TextDisabled(active.ShinyLockedSpecies.Contains(species) ? "Normal variant only / Shiny locked" : "Normal shiny odds / Shiny Charm applies");
                    var sanctuary = active.Mode == "sanctuary";
                    var eligible = !sanctuary || trainer.Legendary.Permits.Contains(active.BossId) && trainer.PveProgress.DefeatedOpponentIds.Contains("champion-eidolon");
                    if (!eligible) ImGui.TextColored(PokemonOrange, "Requires Champion clear and your permanent Zarude permit.");
                    ImGui.BeginDisabled(busy || CollectionLocked || trainer.Party.Count == 0 || !eligible || (!sanctuary && cooling));
                    if (DrawAccentButton(sanctuary ? "Challenge Zarude for Dada capture" : "Battle and catch", new Vector2(sanctuary ? 285 : 190, 32), sanctuary ? PokemonRed : PokemonBlue))
                    {
                        var occurrence = active.OccurrenceId; var boss = active.BossId; var ids = trainer.Party.Select(item => item.Id).ToArray();
                        Run(async () =>
                        {
                            if (sanctuary) { battle = await InSession(plugin.Api.StartEventSanctuary(boss, occurrence, ids)); megaEvolveSelected = false; nextBattlePollAt = DateTimeOffset.UtcNow.AddSeconds(1); }
                            else
                            {
                                var key = occurrence + ":" + species; var request = eventRequests.GetOrAdd(key, _ => Guid.NewGuid().ToString());
                                var found = await InSession(plugin.Api.SearchRecurringEvent(occurrence, species, request)); eventRequests.TryRemove(key, out _);
                                if (found.Status == "active") { await InSession(ResumeWildBattle(found.Id)); mainSection = 0; }
                            }
                            trainer = await InSession(plugin.Api.GetMe()); await InSession(RefreshRecurringEvents());
                        }, sanctuary ? "Starting the event Sanctuary raid..." : "Starting the event encounter...");
                    }
                    ImGui.EndDisabled(); ImGui.EndTable();
                }
                ImGui.Separator(); ImGui.PopID();
            }
        }
        else ImGui.TextDisabled("No event is accepting new entries. Your caught forms and saved battles remain available.");
        ImGui.Spacing(); ImGui.TextColored(PokemonBlue, "COMING BACK");
        foreach (var upcoming in recurringSchedule.Upcoming) ImGui.TextWrapped($"{upcoming.StartsAt:MMM yyyy}: {upcoming.Name} / {upcoming.Species.Count} featured forms");
    }
}
