using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private Vector3? lastTravelPosition;
    private uint lastTravelTerritory;
    private string travelPartyKey = string.Empty;
    private double travelDistance;
    private DateTimeOffset nextTravelReport = DateTimeOffset.MinValue;
    private volatile bool travelReportBusy;
    private volatile bool evolutionHabitatBusy;
    private DateTimeOffset nextEvolutionHabitatRead = DateTimeOffset.MinValue;
    private sealed record TravelBatch(string Id, string[] PokemonIds, double Distance);
    private volatile TravelBatch? pendingTravel;
    private string? CurrentEvolutionTheme => loadedTerritory == Plugin.ClientState.TerritoryType ? habitat?.ThemeId : null;

    private void TrackEvolutionTravel(IFramework framework)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (!Plugin.ClientState.IsLoggedIn || player is null || trainer is null || plugin.Connection.State != ServerConnectionState.Connected)
        {
            lastTravelPosition = null;
            travelDistance = 0;
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var territory = (uint)Plugin.ClientState.TerritoryType;
        if (CurrentEvolutionTheme is null && !evolutionHabitatBusy && now >= nextEvolutionHabitatRead)
        {
            evolutionHabitatBusy = true;
            nextEvolutionHabitatRead = now.AddSeconds(10);
            var context = CurrentTerritoryContext(territory);
            _ = ReadEvolutionHabitat(territory, context);
        }
        var ids = trainer.Party.Select(pokemon => pokemon.Id).Order().ToArray();
        var partyKey = string.Join(",", ids);
        if (partyKey != travelPartyKey) travelDistance = 0;
        if (lastTravelPosition.HasValue && territory == lastTravelTerritory && partyKey == travelPartyKey)
        {
            var distance = Vector3.Distance(lastTravelPosition.Value, player.Position);
            if (float.IsFinite(distance) && distance < 30) travelDistance += distance;
        }
        lastTravelPosition = player.Position;
        lastTravelTerritory = territory;
        travelPartyKey = partyKey;
        if (travelReportBusy || now < nextTravelReport || ids.Length == 0) return;
        nextTravelReport = now.AddSeconds(20);
        if (pendingTravel is null && travelDistance >= 1)
        {
            pendingTravel = new TravelBatch(Guid.NewGuid().ToString(), ids, Math.Min(1000, travelDistance));
            travelDistance = 0;
        }
        if (pendingTravel is { } batch)
        {
            travelReportBusy = true;
            _ = ReportEvolutionTravel(batch);
        }
    }

    private async Task ReadEvolutionHabitat(uint territory, TerritoryContextInfo context)
    {
        var session = sessionResponses.Generation;
        try
        {
            var result = await InSession(plugin.Api.GetHabitat(territory, context.LocalizedName, context.EnglishName, context.RegionName, context.ZoneName, disposeCancellation.Token));
            backgroundResults.Enqueue(() =>
            {
                if (sessionResponses.IsCurrent(session)) ApplyHabitat(territory, result);
            });
        }
        catch (StaleSessionException) { }
        catch (Exception exception) { Plugin.Log.Debug(exception, "Evolution territory lookup failed."); }
        finally { backgroundResults.Enqueue(() => { if (sessionResponses.IsCurrent(session)) evolutionHabitatBusy = false; }); }
    }

    private async Task ReportEvolutionTravel(TravelBatch batch)
    {
        var session = sessionResponses.Generation;
        try
        {
            await InSession(plugin.Api.RecordEvolutionTravel(batch.Id, batch.PokemonIds, batch.Distance, disposeCancellation.Token));
            pendingTravel = null;
        }
        catch (StaleSessionException) { }
        catch (ApiException exception) when (exception.StatusCode is 400 or 404 or 409)
        {
            pendingTravel = null;
            Plugin.Log.Debug(exception, "Evolution travel batch discarded.");
        }
        catch (Exception exception) { Plugin.Log.Debug(exception, "Evolution travel report failed."); }
        finally { if (sessionResponses.IsCurrent(session)) travelReportBusy = false; }
    }

    private void DrawEvolutionCare(PokemonDto pokemon)
    {
        if (pokemon.EvolutionProgress is { } progress)
        {
            ImGui.TextDisabled($"Friendship: {progress.Friendship}/255   |   Party travel: {progress.Travelled:N0} yalms");
            if (pokemon.Species == "Gimmighoul")
            {
                ImGui.TextColored(PokemonYellow, $"Gimmighoul Coins: {progress.Coins}/999   |   Bag: {InventoryCount("gimmighoulcoin")}");
                var count = Math.Min(99, Math.Min(999 - progress.Coins, InventoryCount("gimmighoulcoin")));
                ImGui.BeginDisabled(count <= 0);
                if (DrawAccentButton($"Feed {count} Gimmighoul Coins##Coins{pokemon.Id}", Vector2.Zero, PokemonYellow))
                    Run(() => UseTrainingItem(pokemon.Id, "gimmighoulcoin", null, count), "Feeding Gimmighoul Coins...");
                ImGui.EndDisabled();
            }
        }
        ImGui.TextDisabled("EV-reset berries also raise friendship, even with zero EVs.");
    }
}
