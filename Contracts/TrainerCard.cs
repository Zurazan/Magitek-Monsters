using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class TrainerCardPreferences
{
    public string FeaturedPokemonId { get; set; } = string.Empty;
    public string TitleId { get; set; } = "trainer";
    public int Color { get; set; }
    public List<string>? BadgeIds { get; set; }
    public bool Shared { get; set; }

    public TrainerCardPreferences Copy() => new() { FeaturedPokemonId = FeaturedPokemonId, TitleId = TitleId,
        Color = Color, BadgeIds = BadgeIds?.ToList(), Shared = Shared };
}

public sealed record TrainerCardTitle(string Id, string Name, string Requirement, bool Unlocked);
public sealed record TrainerCardBadge(string Id, string Name, string Detail, string Mark);
public sealed record TrainerCardView(PokemonDto? Partner, TrainerCardTitle Title, int Color,
    IReadOnlyList<TrainerCardBadge> Badges, IReadOnlyList<TrainerCardBadge> EarnedBadges);

public static class TrainerCard
{
    public const int MaximumBadges = 3;

    public static TrainerProfileDto Profile(TrainerSnapshotDto trainer, TrainerCardPreferences preferences)
    {
        return Profile(trainer, Resolve(trainer, preferences));
    }

    public static TrainerProfileDto Profile(TrainerSnapshotDto trainer, TrainerCardView card)
    {
        return new() { TrainerId = trainer.User.TrainerId, VanityCode = trainer.User.VanityCode, DisplayName = trainer.User.DisplayName,
            Title = card.Title.Name, Color = card.Color, Badges = card.Badges.ToList(),
            Partner = card.Partner is null ? null : new() { Species = card.Partner.Species, Form = card.Partner.Form,
                Nickname = card.Partner.Nickname, Level = card.Partner.Level, Shiny = card.Partner.Shiny },
            RegisteredSpecies = trainer.CollectionPoints.RegisteredSpecies, ShinySpecies = trainer.Pokedex.ShinyCaught,
            TrainerWins = trainer.PveProgress.TotalWins, FrontierWins = trainer.Frontier.LifetimeWins };
    }

    public static bool Same(TrainerCardPreferences? first, TrainerCardPreferences? second) => first is not null && second is not null
        && first.FeaturedPokemonId == second.FeaturedPokemonId && first.TitleId == second.TitleId && first.Color == second.Color
        && first.Shared == second.Shared && (first.BadgeIds is null ? second.BadgeIds is null
            : second.BadgeIds is not null && first.BadgeIds.SequenceEqual(second.BadgeIds, StringComparer.Ordinal));

    public static bool Valid(TrainerSnapshotDto trainer, TrainerCardPreferences preferences)
    {
        var card = Resolve(trainer, preferences);
        return preferences.Color is >= 0 and <= 3 && card.Title.Id == preferences.TitleId
            && (preferences.FeaturedPokemonId.Length == 0 || card.Partner?.Id == preferences.FeaturedPokemonId)
            && (preferences.BadgeIds is null || preferences.BadgeIds.Count <= MaximumBadges
                && preferences.BadgeIds.Distinct(StringComparer.Ordinal).Count() == preferences.BadgeIds.Count
                && preferences.BadgeIds.All(id => card.EarnedBadges.Any(badge => badge.Id == id)));
    }

    public static TrainerCardPreferences Preferences(IDictionary<string, TrainerCardPreferences> cards, string trainerId)
    {
        if (!cards.TryGetValue(trainerId, out var preferences) || preferences is null)
            cards[trainerId] = preferences = new TrainerCardPreferences();
        preferences.FeaturedPokemonId ??= string.Empty;
        preferences.TitleId ??= "trainer";
        return preferences;
    }

    public static IReadOnlyList<TrainerCardTitle> Titles(TrainerSnapshotDto trainer) =>
    [
        new("trainer", "Pokémon Trainer", "Choose your first partner.", true),
        new("collector", "Pokémon Collector", "Register 50 different species.", trainer.CollectionPoints.RegisteredSpecies >= 50),
        new("researcher", "Pokédex Researcher", "Register 250 different species.", trainer.CollectionPoints.RegisteredSpecies >= 250),
        new("shiny", "Shiny Enthusiast", "Register a shiny species.", trainer.Pokedex.ShinyCaught > 0),
        new("ace", "Ace Trainer", "Win 25 trainer battles.", trainer.PveProgress.TotalWins >= 25),
        new("challenger", "Frontier Challenger", "Win 25 Frontier battles.", trainer.Frontier.LifetimeWins >= 25),
        new("veteran", "Frontier Veteran", "Reach a Standard Frontier streak of 21.", trainer.Frontier.BestStandard >= 21),
        new("badges", "Badge Collector", "Earn all eight Gym badges.", trainer.Pokedex.Badges.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).Count() >= 8)
    ];

    public static TrainerCardView Resolve(TrainerSnapshotDto trainer, TrainerCardPreferences preferences)
    {
        var owned = trainer.Party.Concat(trainer.Storage).ToList();
        var partner = owned.FirstOrDefault(entry => entry.Id == preferences.FeaturedPokemonId)
            ?? trainer.Party.OrderBy(entry => entry.PartySlot).FirstOrDefault() ?? owned.FirstOrDefault();
        var titles = Titles(trainer);
        var title = titles.FirstOrDefault(entry => entry.Id == preferences.TitleId && entry.Unlocked) ?? titles[0];
        var earned = new List<TrainerCardBadge>();
        foreach (var name in trainer.Pokedex.Badges.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal))
            earned.Add(new("gym:" + name, name, "Gym badge earned through trainer battles.", (earned.Count + 1).ToString()));
        foreach (var achievement in trainer.Frontier.Achievements.Where(entry => entry.Completed))
            earned.Add(new("frontier:" + achievement.Id, achievement.Name, "Frontier achievement", "F"));
        foreach (var objective in trainer.CollectionPoints.Objectives.Where(entry => entry.Completed))
            earned.Add(new("collection:" + objective.Id, objective.Name, "Collection objective completed", "C"));
        if (trainer.StarterChosen) earned.Add(new("partner", "First partner", "Your journey began with your first Pokémon.", "P"));
        earned = earned.DistinctBy(entry => entry.Id).ToList();
        var selected = preferences.BadgeIds is null ? earned.Take(MaximumBadges)
            : preferences.BadgeIds.Distinct(StringComparer.Ordinal).Select(id => earned.FirstOrDefault(entry => entry.Id == id))
                .OfType<TrainerCardBadge>().Take(MaximumBadges);
        return new(partner, title, Math.Clamp(preferences.Color, 0, 3), selected.ToList(), earned);
    }

    public static string PartnerLabel(PokemonDto pokemon)
    {
        var name = string.IsNullOrWhiteSpace(pokemon.Nickname) ? pokemon.Species : pokemon.Nickname;
        return $"{(pokemon.Shiny ? "★ " : "")}{name} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level}";
    }

    public static string Summary(TrainerSnapshotDto trainer, TrainerCardView card)
    {
        static string Line(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.Join(Environment.NewLine,
            $"Magitek Monsters | {Line(trainer.User.DisplayName)}",
            $"{card.Title.Name} | Trainer ID: {Line(trainer.User.TrainerId)}",
            $"Partner: {(card.Partner is null ? "None" : Line(PartnerLabel(card.Partner)))}",
            $"Registered: {trainer.CollectionPoints.RegisteredSpecies:N0} species | Shiny species: {trainer.Pokedex.ShinyCaught:N0}",
            $"Trainer wins: {trainer.PveProgress.TotalWins:N0} | Frontier wins: {trainer.Frontier.LifetimeWins:N0}",
            $"Showcase: {(card.Badges.Count == 0 ? "None" : string.Join(" / ", card.Badges.Select(entry => Line(entry.Name))))}");
    }
}
