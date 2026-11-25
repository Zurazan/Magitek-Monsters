using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private enum WalletCurrency { Yen, CollectionPoints, BattlePoints, RankTokens }

    private static readonly WalletCurrency[] WalletCurrencies = Enum.GetValues<WalletCurrency>();
    private WalletCurrency walletCurrency;
    private bool openTrainerWallet;

    private static string WalletName(WalletCurrency currency) => currency switch
    {
        WalletCurrency.CollectionPoints => "Collection Points",
        WalletCurrency.BattlePoints => "Battle Points",
        WalletCurrency.RankTokens => "Rank Tokens",
        _ => "Yen",
    };

    private static Vector4 WalletAccent(WalletCurrency currency) => currency switch
    {
        WalletCurrency.CollectionPoints => CompanionAccent,
        WalletCurrency.BattlePoints => new(0.77f, 0.69f, 0.90f, 1),
        WalletCurrency.RankTokens => new(0.88f, 0.68f, 0.52f, 1),
        _ => new(0.88f, 0.77f, 0.50f, 1),
    };

    private void DrawTrainerWallet()
    {
        if (trainer is not { } current) return;
        if (openTrainerWallet)
        {
            openTrainerWallet = false;
            ImGui.SetScrollY(0);
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }
        if (!ImGui.CollapsingHeader("Wallet", ImGuiTreeNodeFlags.DefaultOpen)) return;

        var balances = new[]
        {
            $"¥ {current.Currency:N0}",
            $"{current.CollectionPoints.Balance:N0} CP",
            $"{current.Frontier.Balance:N0} BP",
            current.PvpProfile.RankTokens is { } tokens ? $"{tokens.Balance:N0} RT" : "Loading...",
        };
        var scale = CompanionScale;
        var padding = new Vector2(10, 7) * scale;
        const float balanceScale = 1.18f;
        var tileWidth = 0f;
        for (var index = 0; index < WalletCurrencies.Length; index++)
            tileWidth = Math.Max(tileWidth, Math.Max(ImGui.CalcTextSize(WalletName(WalletCurrencies[index])).X,
                ImGui.CalcTextSize(balances[index]).X * balanceScale));
        var gap = 7 * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var minimum = tileWidth + padding.X * 2 + gap;
        var columns = available >= minimum * 4 ? 4 : available >= minimum * 2 ? 2 : 1;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(gap / 2, 0));
        if (ImGui.BeginTable("WalletBalances", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            for (var index = 0; index < WalletCurrencies.Length; index++)
            {
                if (index % columns == 0) ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var currency = WalletCurrencies[index];
                if (DrawWalletCard(currency, balances[index], walletCurrency == currency, padding, balanceScale))
                    walletCurrency = currency;
                ImGui.Dummy(new Vector2(0, 2 * scale));
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        DrawWalletDetails(current.PvpProfile);
        ImGui.Spacing();
    }

    private static bool DrawWalletCard(WalletCurrency currency, string balance, bool selected, Vector2 padding, float balanceScale)
    {
        var name = WalletName(currency);
        var line = ImGui.GetTextLineHeightWithSpacing();
        var balanceHeight = ImGui.GetTextLineHeight() * balanceScale;
        var selectedBackground = Vector4.Lerp(CompanionSurface, CompanionAccent, 0.17f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? selectedBackground : CompanionSurface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selectedBackground);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Lerp(CompanionSurface, CompanionAccent, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.Border, CompanionAccent with { W = selected ? 0.85f : 0.24f });
        ImGui.PushID((int)currency);
        var clicked = ImGui.Button("##WalletCard", new Vector2(-1, padding.Y * 2 + line + balanceHeight));
        ImGui.PopID();
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();

        var position = ImGui.GetItemRectMin() + padding;
        var end = ImGui.GetItemRectMax() - padding;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(position, Vector2.Max(position, end), true);
        draw.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), name);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * balanceScale, position + new Vector2(0, line),
            ImGui.GetColorU32(WalletAccent(currency)), balance);
        draw.PopClipRect();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (Math.Max(ImGui.CalcTextSize(name).X, ImGui.CalcTextSize(balance).X * balanceScale) > end.X - position.X)
                ImGui.SetTooltip($"{name}\n{balance}");
        }
        return clicked;
    }

    private void DrawWalletDetails(PvpProfileDto profile)
    {
        var currency = walletCurrency;
        var accent = WalletAccent(currency);
        ImGui.PushID((int)currency);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(11, 9) * CompanionScale);
        if (ImGui.BeginTable("WalletDetails", 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(CompanionSurface));
            ImGui.TextColored(accent, WalletName(currency));
            switch (currency)
            {
                case WalletCurrency.Yen:
                    DrawWalletWays("Win trainer battles and earn den rewards.", "Poké Balls, medicine, held items and prize Pokémon.",
                        [("PvE League", () => mainSection = 3), ("Cooperative Dens", () => mainSection = 12)],
                        [("Poké Mart", () => OpenCurrencyShop("Items")), ("Prize Exchange", () => OpenCurrencyShop("Prize Exchange"))]);
                    break;
                case WalletCurrency.CollectionPoints:
                    DrawWalletWays("Register species to complete Pokédex goals.", "Permanently unlock starters and buy the Shiny Charm.",
                        [("Collection goals", () => mainSection = 7)],
                        [("Collection Exchange", () => OpenCurrencyShop("Collection Exchange"))]);
                    break;
                case WalletCurrency.BattlePoints:
                    DrawWalletWays("Win Battle Tower and Battle Factory fights.", "Training items, Ultra Beast rewards and Paradox Research.",
                        [("Battle Frontier", () => OpenFrontier())],
                        [("BP Exchange", () => OpenFrontier(exchange: true))]);
                    break;
                case WalletCurrency.RankTokens:
                    DrawWalletWays("Earn tokens on ranked promotions and at season end.", "Legendary Sanctuary permits and Master Balls.",
                        [("Play Ranked", () => { onlineView = pvpModeIndex = 1; pvpTeamError = string.Empty; mainSection = 4; })],
                        [("RT Exchange", () => OpenCurrencyShop("Rank Token Exchange")), ("Sanctuaries", () => mainSection = 9)]);
                    break;
            }
            ImGui.Separator();
            if (ImGui.CollapsingHeader(currency == WalletCurrency.RankTokens ? "Season payout details" : "How it works"))
                DrawWalletRules(currency, profile);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.PopID();
    }

    private static void DrawWalletWays(string earnText, string spendText,
        (string Label, Action Open)[] earnLinks, (string Label, Action Open)[] spendLinks)
    {
        var wide = ImGui.GetContentRegionAvail().X >= 420 * CompanionScale;
        if (!ImGui.BeginTable("WalletWays", wide ? 2 : 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) return;
        ImGui.TableNextColumn();
        DrawWalletWay("EARN", earnText, earnLinks);
        ImGui.TableNextColumn();
        DrawWalletWay("SPEND", spendText, spendLinks);
        ImGui.EndTable();
    }

    private static void DrawWalletWay(string heading, string description, (string Label, Action Open)[] links)
    {
        ImGui.TextColored(CompanionMuted, heading);
        ImGui.TextWrapped(description);
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var padding = ImGui.GetStyle().FramePadding;
        for (var index = 0; index < links.Length; index++)
        {
            var link = links[index];
            if (index > 0) SameLineIfFits(link.Label, right);
            var label = ImGui.CalcTextSize(link.Label).X + padding.X * 2 <= ImGui.GetContentRegionAvail().X
                ? link.Label : link.Label.Replace(' ', '\n');
            if (DrawAccentButton($"{label}##{link.Label}", Vector2.Zero, CompanionAccent)) link.Open();
        }
    }

    private static void DrawWalletRules(WalletCurrency currency, PvpProfileDto profile)
    {
        switch (currency)
        {
            case WalletCurrency.Yen:
                ImGui.TextWrapped("¥ is Magitek Monsters money. Dens pay when you are eligible for rewards. Extra copies of unlocked starters also cost ¥.");
                break;
            case WalletCurrency.CollectionPoints:
                ImGui.TextWrapped("Each goal pays once. Repeat catches and alternate forms do not add species progress. Registrations stay recorded after release.");
                break;
            case WalletCurrency.BattlePoints:
                ImGui.TextWrapped("Owned teams and rentals can earn BP in both brackets. Streaks and achievements award bonuses. You keep earned BP after a run ends.");
                break;
            case WalletCurrency.RankTokens:
                DrawRankTokenProgress(profile);
                break;
        }
    }

    private static void DrawRankTokenProgress(PvpProfileDto profile)
    {
        if (profile.RankTokens is not { } tokens)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
            ImGui.TextWrapped("Loading ranked rewards...");
            ImGui.PopStyleColor();
            return;
        }

        var season = profile.Season;
        ImGui.TextWrapped($"{season.Name} / Rank {profile.Rank}");
        if (DateTimeOffset.TryParse(season.EndsAt, out var end)) ImGui.TextWrapped($"Season ends {end.LocalDateTime:g}");
        var columns = ImGui.GetContentRegionAvail().X >= 380 * CompanionScale ? 3 : 1;
        if (ImGui.BeginTable("WalletRankPayout", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(CompanionMuted, "Next promotion");
            if (profile.NextRankAt is int next)
            {
                ImGui.TextWrapped($"+{tokens.NextPromotionTokens:N0} RT");
                ImGui.TextWrapped($"{Math.Max(0, next - profile.RankPoints):N0} rank points to go");
            }
            else ImGui.TextWrapped("Highest rank reached");
            ImGui.TableNextColumn();
            ImGui.TextColored(CompanionMuted, "Paid this season");
            ImGui.TextWrapped($"{tokens.SeasonPaid:N0} RT");
            ImGui.TableNextColumn();
            ImGui.TextColored(CompanionMuted, "At season end");
            ImGui.TextWrapped($"+{tokens.SeasonRemaining:N0} RT at your current rank");
            ImGui.EndTable();
        }

        ImGui.TextWrapped($"{season.TokenRewardPerRank:N0} RT per rank climbed, with {season.PromotionRewardPercent}% paid on promotion. Payouts are automatic and balances carry over. Each Sanctuary capture requires a fresh boss victory.");
    }

    private void OpenCurrencyWallet(WalletCurrency currency)
    {
        walletCurrency = currency;
        openTrainerProfileTab = false;
        openTrainerWallet = true;
        mainSection = 14;
    }

    private void OpenCurrencyShop(string tab)
    {
        requestedShopTab = tab;
        mainSection = 5;
    }

    private void DrawCurrencyGuideShortcut()
    {
        if (DrawAccentSmallButton("How to earn currencies", CompanionAccent)) OpenCurrencyWallet(walletCurrency);
    }
}
