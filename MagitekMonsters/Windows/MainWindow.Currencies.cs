using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private void DrawCurrencies()
    {
        if (trainer is null) return;
        ImGui.PushTextWrapPos(0);
        DrawSectionHeader("CURRENCIES", "How to earn them and where to spend them");
        ImGui.TextWrapped("Magitek Monsters has four separate currencies. Each activity awards its own rewards; balances cannot be converted into another currency.");
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(14, 12) * CompanionScale);
        if (ImGui.BeginTable("CurrencyGuide", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg))
        {
            DrawCurrencyHeading("RT / Rank Tokens", trainer.PvpProfile.RankTokens is { } tokens ? $"{tokens.Balance:N0} RT" : "Loading RT balance...", PokemonYellow);
            DrawCurrencyExplanation("How to earn", "Play Online Battles > Ranked to earn rank points and climb from Rank Z towards A. RT is paid automatically when you reach a new rank, with the remaining reward paid at season end.");
            DrawCurrencyExplanation("What it buys", "Permanent Legendary Sanctuary permits, plus Master Balls in Poké Mart > Rank Token Exchange. A fresh Sanctuary boss victory is still required for each capture.");
            DrawCurrencyExplanation("Good to know", "Rank points track your seasonal rank. RT is the currency you spend. Casual and direct friend battles award no RT. Your RT balance carries across seasons.");
            DrawRankTokenProgress();
            DrawCurrencyLinks(PokemonYellow,
                ("Play Ranked", () => { onlineView = pvpModeIndex = 1; pvpTeamError = string.Empty; mainSection = 4; }),
                ("RT Exchange", () => OpenCurrencyShop("Rank Token Exchange")),
                ("Sanctuaries", () => mainSection = 9));

            DrawCurrencyHeading("¥ / Yen", $"¥ {trainer.Currency:N0}", PokemonGreen);
            DrawCurrencyExplanation("How to earn", "Win reward-paying NPC battles in PvE League, including the Trainer circuit and League run. Cooperative Den victories also award ¥ when you are eligible for that den's rewards.");
            DrawCurrencyExplanation("What it buys", "Poké Balls, medicine, evolution and training items, held items, move licenses and Prize Exchange Pokémon in the Poké Mart. Extra copies of unlocked starters also cost ¥.");
            DrawCurrencyExplanation("Good to know", "This is Magitek Monsters money, separate from FFXIV gil. Check each battle's reward preview before starting.");
            DrawCurrencyLinks(PokemonGreen,
                ("PvE League", () => mainSection = 3),
                ("Cooperative Dens", () => mainSection = 12),
                ("Poké Mart", () => OpenCurrencyShop("Items")));

            DrawCurrencyHeading("CP / Collection Points", $"{trainer.CollectionPoints.Balance:N0} CP", PokemonBlue);
            DrawCurrencyExplanation("How to earn", "Register new species in your Pokédex to complete Collection goals: species milestones, type collections, evolution families and generation surveys. Each objective awards CP automatically once.");
            DrawCurrencyExplanation("What it buys", "Permanent starter unlocks and the Shiny Charm in Poké Mart > Collection Exchange. Rewards can have extra collection requirements, shown in the exchange.");
            DrawCurrencyExplanation("Good to know", "Repeat catches and alternate forms do not add species progress. Registrations stay recorded after release. CP comes from collection objectives rather than battle payouts.");
            DrawCurrencyLinks(PokemonBlue,
                ("Collection goals", () => mainSection = 7),
                ("Collection Exchange", () => { openCollectionExchange = true; mainSection = 5; }));

            DrawCurrencyHeading("BP / Battle Points", $"{trainer.Frontier.Balance:N0} BP", CompanionAccent);
            DrawCurrencyExplanation("How to earn", "Win Battle Tower or Battle Factory fights in Battle Frontier. Both Accessible and Standard brackets award BP. Longer streaks, every seventh fight and one-time achievements add rewards.");
            DrawCurrencyExplanation("What it buys", "Competitive supplies and move licenses in BP Exchange, unlocked Ultra Beast rewards and permanent Paradox Research permits in Battle Frontier.");
            DrawCurrencyExplanation("Good to know", "Owned teams and rentals can earn BP. Losing or ending a run keeps BP already earned, achievements and permanent unlocks.");
            DrawCurrencyLinks(CompanionAccent,
                ("Battle Frontier", () => mainSection = 8),
                ("BP Exchange", () => { openBpExchange = true; mainSection = 8; }));
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.PopTextWrapPos();
    }

    private static void DrawCurrencyHeading(string title, string balance, Vector4 accent)
    {
        ImGui.TableNextRow(); ImGui.TableNextColumn();
        ImGui.TextColored(accent, title);
        ImGui.TextWrapped($"Your balance: {balance}");
        ImGui.Spacing();
    }

    private static void DrawCurrencyExplanation(string label, string text)
    {
        ImGui.TextColored(CompanionMuted, label);
        ImGui.TextWrapped(text);
        ImGui.Spacing();
    }

    private void DrawRankTokenProgress()
    {
        var profile = trainer!.PvpProfile;
        if (profile.RankTokens is not { } tokens) return;
        var season = profile.Season;
        ImGui.TextColored(PokemonYellow, "Your ranked rewards");
        ImGui.TextWrapped($"{season.Name} / Rank {profile.Rank} / {profile.RankPoints:N0} rank points");
        ImGui.TextWrapped($"This season: {season.TokenRewardPerRank:N0} RT per rank climbed, with {season.PromotionRewardPercent}% paid through promotions.");
        if (profile.NextRankAt is int next)
            ImGui.TextWrapped($"Next promotion: {Math.Max(0, next - profile.RankPoints):N0} more rank points / +{tokens.NextPromotionTokens:N0} RT.");
        else ImGui.TextWrapped("Rank A reached. You have earned this season's highest rank reward.");
        ImGui.TextWrapped($"Already paid this season: {tokens.SeasonPaid:N0} RT. Remaining at your current rank: {tokens.SeasonRemaining:N0} RT at season end.");
        if (DateTimeOffset.TryParse(season.EndsAt, out var end)) ImGui.TextWrapped($"Season ends {end.LocalDateTime:g}.");
        ImGui.TextWrapped("Final payments arrive automatically after the season's results finish processing, including while you are offline.");
        ImGui.Spacing();
    }

    private static void DrawCurrencyLinks(Vector4 accent, params (string Label, Action Open)[] links)
    {
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var buttonWidth = links.Max(link => ImGui.CalcTextSize(link.Label).X) + ImGui.GetStyle().FramePadding.X * 2;
        var columns = Math.Clamp((int)((ImGui.GetContentRegionAvail().X + gap) / (buttonWidth + gap)), 1, links.Length);
        ImGui.PushID(links[0].Label);
        if (ImGui.BeginTable("CurrencyLinks", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var link in links)
            {
                ImGui.TableNextColumn();
                var label = link.Label;
                var padding = ImGui.GetStyle().FramePadding;
                if (ImGui.CalcTextSize(label).X > ImGui.GetContentRegionAvail().X - padding.X * 2) label = label.Replace(' ', '\n');
                var height = Math.Max(32 * CompanionScale, ImGui.CalcTextSize(label).Y + padding.Y * 2);
                if (DrawAccentButton($"{label}##{link.Label}", new Vector2(-1, height), accent)) link.Open();
            }
            ImGui.EndTable();
        }
        ImGui.PopID(); ImGui.Spacing();
    }

    private void OpenCurrencyShop(string tab)
    {
        requestedShopTab = tab;
        mainSection = 5;
    }

    private void DrawCurrencyGuideShortcut()
    {
        if (DrawAccentSmallButton("How to earn currencies", CompanionAccent)) mainSection = 15;
    }
}
