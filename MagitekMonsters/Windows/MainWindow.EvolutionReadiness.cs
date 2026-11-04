using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private List<EvolutionOptionDto>? readinessSource;
    private int readinessSourceCount;
    private EvolutionEnvironment? readinessEnvironment;
    private EvolutionOptionDto[] environmentalEvolutions = [];
    private TrainerSnapshotDto? navigationAlertTrainer;
    private (int Party, int Storage) navigationAlerts;

    private void UpdateEvolutionReadiness()
    {
        var source = trainer?.EvolutionOptions;
        var changed = !ReferenceEquals(readinessSource, source) || readinessSourceCount != (source?.Count ?? 0);
        if (!changed && Equals(readinessEnvironment, evolutionEnvironment)) return;
        navigationAlertTrainer = null;
        if (changed)
        {
            readinessSource = source; readinessSourceCount = source?.Count ?? 0;
            environmentalEvolutions = source?.Where(option => option.ReadyWithoutEnvironment.HasValue).ToArray() ?? [];
        }
        readinessEnvironment = evolutionEnvironment;
        foreach (var option in environmentalEvolutions)
        {
            var ready = false;
            if (option.ReadyWithoutEnvironment == true && evolutionEnvironment is { } environment)
            {
                if (option.EnvironmentAlternatives.Count == 0)
                    ready = environment.Matches(option.RequiredTime, option.RequiredWeather, option.EnvironmentChecks);
                else foreach (var route in option.EnvironmentAlternatives)
                    if (environment.Matches(route.RequiredTime, route.RequiredWeather, route.EnvironmentChecks)) { ready = true; break; }
            }
            option.Ready = ready;
        }
    }

    private (int Party, int Storage) GetNavigationAlerts()
    {
        if (trainer is null) return default;
        if (ReferenceEquals(navigationAlertTrainer, trainer)) return navigationAlerts;
        var partyIds = trainer.Party.Select(pokemon => pokemon.Id).ToHashSet();
        var party = 0; var storage = 0;
        foreach (var move in trainer.PendingMoves)
            if (partyIds.Contains(move.PokemonId)) party++; else storage++;
        foreach (var option in trainer.EvolutionOptions)
            if (option.Ready) { if (partyIds.Contains(option.PokemonId)) party++; else storage++; }
        navigationAlertTrainer = trainer;
        return navigationAlerts = (party, storage);
    }

    private void ResetEvolutionReadiness()
    {
        readinessSource = null; readinessSourceCount = 0; readinessEnvironment = null; environmentalEvolutions = [];
        navigationAlertTrainer = null; navigationAlerts = default;
    }
}
