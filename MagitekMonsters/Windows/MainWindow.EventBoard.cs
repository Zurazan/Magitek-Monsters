using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string? selectedEvent;
    private string? selectedEventForm;

    private void DrawRecurringEvents()
    {
        if (trainer is null) return;
        if (battle is { Kind: "trainer", OpponentId: "legendary-zarude" })
        {
            DrawBattleConsole(battle);
            return;
        }

        DrawSectionHeader("Events", "Monthly rotation");
        var localNow = DateTimeOffset.UtcNow;
        if (!busy && localNow >= nextEventRefresh)
        {
            nextEventRefresh = localNow.AddSeconds(20);
            Run(RefreshRecurringEvents, "Loading the event calendar...");
        }

        var schedule = recurringSchedule;
        var now = schedule is null ? localNow : schedule.ServerTime + (localNow - eventLoadedAt);
        if (SavedEventEncounter(now) is { } encounter) DrawSavedEvent(encounter);
        if (schedule is null)
        {
            EventText("Loading event schedule...", CompanionMuted);
            return;
        }

        var active = schedule.Active is { } live && EventIsLive(schedule, live, now) ? live : null;
        var upcoming = schedule.Upcoming.Where(entry => entry.StartsAt > now && entry.OccurrenceId != active?.OccurrenceId)
            .OrderBy(entry => entry.StartsAt);
        var rotation = active is null ? upcoming.ToArray() : upcoming.Prepend(active).ToArray();
        if (active is null) EventText("No event is accepting new entries.", CompanionMuted);

        if (rotation.Length > 0)
        {
            if (!rotation.Any(entry => entry.OccurrenceId == selectedEvent)) SelectEvent(rotation[0]);
            DrawEventRotation(rotation, active);
            var entry = rotation.First(entry => entry.OccurrenceId == selectedEvent);
            DrawFeaturedEvent(schedule, entry, now);
        }
        if (ImGui.CollapsingHeader("Event rules"))
        {
            EventText("Caught Pokémon and form registrations stay yours after an event. Events have no separate currency and do not affect Mew access.", CompanionMuted);
            EventText("You can finish a saved encounter until it expires, even after the rotation changes.", CompanionMuted);
        }
    }

    private void SelectEvent(RecurringEventDto entry)
    {
        selectedEvent = entry.OccurrenceId;
        selectedEventForm = null;
        eventPage = 0;
    }

    private void DrawEventRotation(RecurringEventDto[] rotation, RecurringEventDto? active)
    {
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var columns = width >= 620 * scale ? 4 : width >= 280 * scale ? 2 : 1;
        ImGui.Spacing();
        ImGui.TextDisabled("ROTATION");
        if (!ImGui.BeginTable("EventRotation", Math.Min(columns, rotation.Length), ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var entry in rotation)
        {
            ImGui.TableNextColumn();
            var species = entry.Species.FirstOrDefault();
            var isLive = entry == active;
            var subtitle = isLive ? "Live now" : entry.StartsAt.ToLocalTime().ToString("MMM yyyy");
            var pad = 8 * scale;
            var art = 30 * scale;
            var textWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - art - 3 * pad);
            var nameHeight = ImGui.CalcTextSize(entry.Name, false, textWidth).Y;
            var textHeight = nameHeight + ImGui.CalcTextSize(subtitle, false, textWidth).Y + 3 * scale;
            var height = Math.Max(art, textHeight) + 2 * pad;
            var clicked = DrawPveCardSurface("Event##" + entry.OccurrenceId, height, entry.OccurrenceId == selectedEvent,
                out var min, out _, out var after);
            ImGui.SetCursorScreenPos(min + new Vector2(pad));
            if (species is not null) DrawPokemonIcon(species, false, new Vector2(art));
            ImGui.SetCursorScreenPos(min + new Vector2(2 * pad + art, pad));
            EventText(entry.Name, ImGui.GetStyle().Colors[(int)ImGuiCol.Text], textWidth);
            ImGui.SetCursorScreenPos(min + new Vector2(2 * pad + art, pad + nameHeight + 3 * scale));
            EventText(subtitle, isLive ? PokemonGreen : CompanionMuted, textWidth);
            ImGui.SetCursorScreenPos(after);
            if (clicked && selectedEvent != entry.OccurrenceId) SelectEvent(entry);
        }
        ImGui.EndTable();
        ImGui.Spacing();
    }

    private void DrawFeaturedEvent(RecurringEventScheduleDto schedule, RecurringEventDto entry, DateTimeOffset now)
    {
        var live = EventIsLive(schedule, entry, now);
        var date = (live ? entry.EndsAt : entry.StartsAt).ToLocalTime();
        var dateLabel = $"{(live ? "Ends" : "Starts")} {date:dd MMM yyyy HH:mm} (local time)";
        if (live) dateLabel += $" / {EventTimeLeft(entry.EndsAt - now)} left";
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 8) * CompanionScale);
        if (ImGui.BeginTable("FeaturedEvent", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(.14f, .17f, .22f, 1)));
            EventText(live ? "LIVE NOW" : "UPCOMING", live ? PokemonGreen : CompanionAccent);
            EventText(entry.Name, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            EventText(entry.Description, CompanionMuted);
            EventText(dateLabel, CompanionAccent);
            if (live && entry.Manual) EventText("Special run. Upcoming dates follow the regular rotation.", CompanionMuted);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        if (entry.Species.Count == 0) return;
        var registered = entry.Species.Count(IsFormRegistered);
        EventText($"FEATURED FORMS / {registered:N0} of {entry.Species.Count:N0} registered", CompanionAccent);
        const int pageSize = 6;
        eventPage = Math.Clamp(eventPage, 0, (entry.Species.Count - 1) / pageSize);
        var listed = entry.Species.Skip(eventPage * pageSize).Take(pageSize).ToArray();
        if (!listed.Contains(selectedEventForm)) selectedEventForm = listed[0];
        var scale = CompanionScale;
        var width = ImGui.GetContentRegionAvail().X;
        var columns = width >= 520 * scale ? 3 : width >= 265 * scale ? 2 : 1;
        if (ImGui.BeginTable("EventForms", Math.Min(columns, listed.Length), ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var species in listed)
            {
                ImGui.TableNextColumn();
                if (DrawEventForm(species)) selectedEventForm = species;
            }
            ImGui.EndTable();
        }
        if (entry.Species.Count > pageSize)
        {
            var previousPage = eventPage;
            DrawPager("EventForms", ref eventPage, entry.Species.Count, pageSize);
            if (eventPage != previousPage) selectedEventForm = entry.Species[eventPage * pageSize];
        }

        ImGui.Spacing();
        DrawEventEntry(schedule, entry, selectedEventForm!, now);
        ImGui.Spacing();
    }

    private bool DrawEventForm(string species)
    {
        var scale = CompanionScale;
        var pad = 8 * scale;
        var art = 36 * scale;
        var gap = 4 * scale;
        var name = EventFormName(species);
        var registered = IsFormRegistered(species);
        var status = registered ? "Registered" : "Not registered";
        var content = Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * pad);
        var nameWidth = Math.Max(1, content - art - gap);
        var header = Math.Max(art, ImGui.CalcTextSize(name, false, nameWidth).Y);
        var badgeHeight = Math.Max(14 * scale, ImGui.GetTextLineHeight());
        var height = 2 * pad + header + badgeHeight + ImGui.CalcTextSize(status, false, content).Y + 2 * gap;
        var clicked = DrawPveCardSurface("EventForm##" + species, height, species == selectedEventForm, out var min, out _, out var after);
        ImGui.SetCursorScreenPos(min + new Vector2(pad));
        DrawPokemonIcon(species, false, new Vector2(art));
        ImGui.SetCursorScreenPos(min + new Vector2(pad + art + gap, pad));
        EventText(name, ImGui.GetStyle().Colors[(int)ImGuiCol.Text], nameWidth);
        ImGui.SetCursorScreenPos(min + new Vector2(pad, pad + header + gap));
        var types = SpeciesDisplayCatalog.Get(species)?.Types ?? [];
        var typeWidth = (content - Math.Max(0, types.Length - 1) * 5 * scale) / Math.Max(1, types.Length);
        DrawStarterTypeBadges(types, Math.Max(1, Math.Min(60 * scale, typeWidth)), scale);
        ImGui.SetCursorScreenPos(min + new Vector2(pad, pad + header + badgeHeight + 2 * gap));
        EventText(status, registered ? PokemonGreen : CompanionMuted, content);
        ImGui.SetCursorScreenPos(after);
        return clicked;
    }

    private void DrawEventEntry(RecurringEventScheduleDto schedule, RecurringEventDto entry, string species, DateTimeOffset now)
    {
        var scale = CompanionScale;
        var wide = ImGui.GetContentRegionAvail().X >= 410 * scale;
        var sanctuary = entry.Mode == "sanctuary";
        var shinyLocked = entry.ShinyLockedSpecies.Contains(species);
        var restriction = EventEntryRestriction(schedule, entry, now);
        var captureLevel = sanctuary ? legendaryBosses.FirstOrDefault(boss => boss.Id == entry.BossId)?.CaptureLevel : null;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(9, 7) * scale);
        if (ImGui.BeginTable("EventEntry", wide ? 2 : 1, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableSetupColumn("Form", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            if (wide) ImGui.TableSetupColumn("Enter", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            EventText(EventFormName(species), CompanionAccent);
            if (sanctuary)
            {
                EventText(captureLevel is > 0 ? $"Sanctuary raid / Capture at Lv. {captureLevel}" : "Sanctuary raid", CompanionMuted);
                EventText("Defeat the boss before every capture. Requires a Champion victory and the Sanctuary permit.", CompanionMuted);
            }
            else
            {
                var levels = entry.MinLevel == entry.MaxLevel ? $"Lv. {entry.MinLevel}" : $"Lv. {entry.MinLevel}–{entry.MaxLevel}";
                EventText(entry.MinLevel > 0 && entry.MaxLevel >= entry.MinLevel ? "Wild encounter / " + levels : "Wild encounter", CompanionMuted);
            }
            EventText(shinyLocked ? "Shiny locked" : "Shiny possible / Shiny Charm applies", shinyLocked ? PokemonYellow : PokemonGreen);

            if (wide) ImGui.TableNextColumn();
            var label = !EventIsLive(schedule, entry, now) ? "Upcoming" : sanctuary ? "Challenge Zarude" : "Battle and catch";
            ImGui.BeginDisabled(restriction is not null);
            var enter = DrawAccentButton(label, new Vector2(-1, Math.Max(32 * scale, ImGui.GetFrameHeight())), sanctuary ? PokemonRed : PokemonGreen);
            ImGui.EndDisabled();
            if (restriction is not null) EventText(restriction, CompanionMuted);
            if (enter && trainer is { } current)
            {
                var partyIds = current.Party.OrderBy(pokemon => pokemon.PartySlot).Select(pokemon => pokemon.Id).ToArray();
                Run(() => EnterRecurringEvent(entry, species, partyIds), sanctuary ? "Starting the event Sanctuary raid..." : "Starting the event encounter...");
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void DrawSavedEvent(EncounterDto encounter)
    {
        var scale = CompanionScale;
        var wide = ImGui.GetContentRegionAvail().X >= 450 * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(9, 7) * scale);
        if (ImGui.BeginTable("SavedEvent", wide ? 2 : 1, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableSetupColumn("Encounter", ImGuiTableColumnFlags.WidthStretch);
            if (wide) ImGui.TableSetupColumn("Resume", ImGuiTableColumnFlags.WidthFixed, 185 * scale);
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(.14f, .22f, .18f, 1)));
            EventText("Saved capture battle", PokemonGreen);
            var name = EventFormName(encounter.Pokemon.DisplaySpecies);
            var expiration = DateTimeOffset.TryParse(encounter.ExpiresAt, out var expires) ? $" / Expires {expires.ToLocalTime():HH:mm}" : "";
            EventText($"{name} / Lv. {encounter.Pokemon.Level}{expiration}", CompanionMuted);
            if (wide) ImGui.TableNextColumn();
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Resume battle", new Vector2(-1, Math.Max(32 * scale, ImGui.GetFrameHeight())), PokemonGreen))
                Run(async () => { await InSession(ResumeWildBattle(encounter.Id, true)); mainSection = 0; }, "Resuming event capture...");
            ImGui.EndDisabled();
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
    }

    private static string EventTimeLeft(TimeSpan time)
    {
        if (time.TotalDays >= 1) return $"{time.Days}d {time.Hours}h";
        if (time.TotalHours >= 1) return $"{time.Hours}h {time.Minutes}m";
        var seconds = Math.Max(0, (int)Math.Ceiling(time.TotalSeconds));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private static string EventFormName(string species)
    {
        if (species.EndsWith("-Totem", StringComparison.Ordinal)) return $"{SpeciesDisplayCatalog.Name(species[..^6])} (Totem)";
        if (species == "Zarude-Dada") return "Dada Zarude";
        if (!species.StartsWith("Pikachu-", StringComparison.Ordinal)) return SpeciesDisplayCatalog.Name(species);
        var suffix = species[8..];
        var form = suffix switch
        {
            "Original" or "Hoenn" or "Sinnoh" or "Unova" or "Kalos" or "Alola" or "Partner" or "World" => suffix + " Cap",
            _ => suffix.Replace('-', ' ')
        };
        return $"Pikachu ({form})";
    }

    private static void EventText(string text, Vector4 color, float? width = null)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, width ?? ImGui.GetContentRegionAvail().X));
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
    }
}
