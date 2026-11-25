using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string trainerCardSearch = string.Empty;
    private string trainerCardOwner = string.Empty;

    private void DrawTrainerCard()
    {
        if (trainer is null) return;
        PrepareTrainerCard();
        var preferences = trainerCardDraft!;
        DrawSectionHeader("Trainer Card", "Your partner, earned titles and achievement showcase");
        if (!ImGui.BeginTabBar("TrainerCardTabs")) return;
        if (ImGui.BeginTabItem("Your card"))
        {
            DrawYourTrainerCard(preferences);
            ImGui.EndTabItem();
        }
        var progressFlags = openTrainerWallet ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (ImGui.BeginTabItem("Progress & currency", progressFlags))
        {
            DrawTrainerRecords();
            ImGui.EndTabItem();
        }
        var flags = openTrainerProfileTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (ImGui.BeginTabItem("Other trainers", flags))
        {
            openTrainerProfileTab = false;
            DrawTrainerProfileLookup();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawYourTrainerCard(TrainerCardPreferences preferences)
    {
        if (trainer is not { } current) return;
        var card = TrainerCard.Resolve(current, preferences, regionalLeagues);
        if (ImGui.CollapsingHeader("Card preview", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (trainerCardDirty) ImGui.TextDisabled("Previewing unsaved changes.");
            DrawTrainerCardPreview(card);
            if (DrawAccentSmallButton("Copy card summary", CompanionAccent))
            {
                Interlocked.Exchange(ref pendingClipboard, TrainerCard.Summary(current, card));
                SetStatus("Trainer card summary copied.");
            }
            ImGui.Spacing();
        }
        if (ImGui.CollapsingHeader("Card appearance & identity"))
        {
            ImGui.BeginDisabled(busy);
            DrawTrainerCardEditor(preferences, card);
            ImGui.Checkbox("Share my card with other trainers", ref trainerCardShared);
            if (preferences.Shared != trainerCardShared) { preferences.Shared = trainerCardShared; SaveTrainerCard(); }
            ImGui.TextWrapped("Shared cards show your partner, title, showcase, Pokédex totals and battle wins.");
            ImGui.EndDisabled();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            DrawTrainerIdentity();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        if (ImGui.CollapsingHeader("Showcase badges")) DrawShowcaseCatalog(preferences, card);
        if (!trainerCardDirty && current.Collection.TrainerCard is not null) return;
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        ImGui.BeginDisabled(busy);
        if (DrawAccentSmallButton("Save card", CompanionAccent)) Run(PublishTrainerCard, "Saving trainer card...");
        if (trainerCardDirty)
        {
            SameLineIfFits("Reset changes", right);
            if (DrawAccentSmallButton("Reset changes", CompanionMuted)) { trainerCardDirty = false; trainerCardDraft = null; }
        }
        ImGui.EndDisabled();
    }

    private static Vector4 TrainerCardColor(int color) => color switch
    {
        1 => PokemonRed,
        2 => PokemonGreen,
        3 => PokemonYellow,
        4 => new Vector4(0.64f, 0.42f, 0.92f, 1f),
        5 => new Vector4(1f, 0.46f, 0.70f, 1f),
        6 => new Vector4(0.16f, 0.74f, 0.64f, 1f),
        7 => PokemonOrange,
        8 => new Vector4(0.76f, 0.80f, 0.86f, 1f),
        9 => new Vector4(0.24f, 0.78f, 0.96f, 1f),
        10 => new Vector4(0.72f, 0.65f, 0.96f, 1f),
        11 => new Vector4(0.85f, 0.32f, 0.45f, 1f),
        _ => PokemonBlue
    };

    private void DrawTrainerCardPreview(TrainerCardView card)
        => DrawTrainerProfilePreview(TrainerCard.Profile(trainer!, card));

    private void DrawTrainerProfilePreview(MagitekMonsters.Models.TrainerProfileDto card)
    {
        var partner = card.Partner is null ? null : new MagitekMonsters.Models.PokemonDto {
            Species = card.Partner.Species, Form = card.Partner.Form, Nickname = card.Partner.Nickname,
            Level = card.Partner.Level, Shiny = card.Partner.Shiny };
        var scale = CompanionScale;
        var line = ImGui.GetTextLineHeight();
        var gap = ImGui.GetStyle().ItemSpacing.Y;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - 36 * scale);
        var columns = width >= 620 * scale ? 4 : 2;
        var badgeColumns = Math.Clamp((int)(width / (180 * scale)), 1, TrainerCard.MaximumBadges);
        var badgeRows = Math.Max(1, (card.Badges.Count + badgeColumns - 1) / badgeColumns);
        var headerHeight = Math.Max(112 * scale, 5 * line + 6 * gap);
        var height = 36 * scale + headerHeight + (4 / columns) * (2 * line + 3 * gap)
            + 2 * line + 6 * gap + badgeRows * (Math.Max(36 * scale, line) + gap);
        var accent = TrainerCardColor(card.Color);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 16) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Lerp(CompanionSurface, accent, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.Border, accent with { W = 0.65f });
        if (ImGui.BeginChild("TrainerCardPreview", new Vector2(0, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var headerStart = ImGui.GetCursorPosY();
            if (ImGui.BeginTable("TrainerCardIdentity", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Identity", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Partner", ImGuiTableColumnFlags.WidthFixed, 100 * scale);
                ImGui.TableNextColumn();
                ImGui.TextColored(accent, "TRAINER CARD");
                var nameStart = ImGui.GetCursorScreenPos();
                var nameSize = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), line * 1.35f);
                var draw = ImGui.GetWindowDrawList();
                draw.PushClipRect(nameStart, nameStart + nameSize, true);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.35f, nameStart, ImGui.GetColorU32(ImGuiCol.Text), card.DisplayName);
                draw.PopClipRect();
                ImGui.Dummy(nameSize);
                if (ImGui.IsItemHovered() && ImGui.CalcTextSize(card.DisplayName).X * 1.35f > nameSize.X) ImGui.SetTooltip(card.DisplayName);
                DrawTrainerTitle(card.Title, card.TitleId, ImGui.GetContentRegionAvail().X, accent);
                DrawClippedText(card.VanityCode is null ? card.TrainerId : $"{card.VanityCode} / {card.TrainerId}", ImGui.GetContentRegionAvail().X, CompanionMuted);
                DrawClippedText(partner is null ? "No partner selected" : TrainerCard.PartnerLabel(partner), ImGui.GetContentRegionAvail().X, CompanionMuted);
                ImGui.TableNextColumn();
                if (partner is not null) DrawPokemonIcon(partner.Appearance, partner.Shiny, new Vector2(96 * scale));
                else ImGui.Dummy(new Vector2(96 * scale));
                ImGui.EndTable();
            }
            ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), headerStart + headerHeight));
            ImGui.Separator(); ImGui.Spacing();
            if (ImGui.BeginTable("TrainerCardStats", columns, ImGuiTableFlags.SizingStretchSame))
            {
                CardMetric("Registered", card.RegisteredSpecies.ToString("N0"));
                CardMetric("Shiny species", card.ShinySpecies.ToString("N0"));
                CardMetric("Trainer wins", card.TrainerWins.ToString("N0"));
                CardMetric("Frontier wins", card.FrontierWins.ToString("N0"));
                ImGui.EndTable();
            }
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(accent, "ACHIEVEMENT SHOWCASE");
            if (card.Badges.Count == 0) DrawClippedText("No showcase badges selected.", ImGui.GetContentRegionAvail().X, CompanionMuted);
            else if (ImGui.BeginTable("TrainerCardShowcase", badgeColumns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var badge in card.Badges)
                {
                    ImGui.TableNextColumn();
                    DrawTrainerCardBadge(badge);
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild(); ImGui.PopStyleColor(2); ImGui.PopStyleVar();

        static void CardMetric(string label, string value)
        {
            ImGui.TableNextColumn();
            DrawClippedText(label, ImGui.GetContentRegionAvail().X, CompanionMuted);
            DrawClippedText(value, ImGui.GetContentRegionAvail().X, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        }
    }

    private void DrawTrainerCardBadge(TrainerCardBadge badge)
    {
        var scale = CompanionScale;
        var size = Math.Max(36 * scale, ImGui.GetTextLineHeight());
        var width = ImGui.GetContentRegionAvail().X;
        var min = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(size));
        DrawAchievementIconAt(badge.Id, min, new Vector2(size));
        ImGui.SameLine(0, 6 * scale);
        DrawClippedText(badge.Name, Math.Max(1, width - size - 6 * scale), ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{badge.Name}\n{badge.Detail}");
    }

    private void DrawTrainerCardEditor(TrainerCardPreferences preferences, TrainerCardView card)
    {
        ImGui.TextWrapped("Appearance saves to your trainer account. Titles and badges unlock through progress or special awards.");
        ImGui.TextUnformatted("Featured Pokémon");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##TrainerCardPartner", card.Partner is null ? "Choose a partner" : TrainerCard.PartnerLabel(card.Partner)))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##TrainerCardSearch", "Search your party and PC...", ref trainerCardSearch, 100);
            if (ImGui.Selectable("Use party leader", preferences.FeaturedPokemonId.Length == 0))
            {
                preferences.FeaturedPokemonId = string.Empty;
                SaveTrainerCard();
            }
            ImGui.BeginChild("TrainerCardPartners", new Vector2(0, 220 * CompanionScale), false);
            var matches = 0;
            foreach (var choice in GetCardPartners())
            {
                var pokemon = choice.Pokemon;
                matches++;
                if (ImGui.Selectable(choice.Button, preferences.FeaturedPokemonId == pokemon.Id))
                {
                    preferences.FeaturedPokemonId = pokemon.Id;
                    SaveTrainerCard();
                    ImGui.CloseCurrentPopup();
                }
            }
            if (matches == 0) ImGui.TextDisabled("No Pokémon match your search.");
            ImGui.EndChild();
            ImGui.EndCombo();
        }
        ImGui.TextUnformatted("Trainer title");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##TrainerCardTitle", card.Title.Name))
        {
            foreach (var title in TrainerCard.Titles(trainer!, regionalLeagues))
            {
                ImGui.BeginDisabled(!title.Unlocked);
                if (ImGui.Selectable($"{title.Name}{(title.Unlocked ? "" : " (locked)")}", card.Title.Id == title.Id))
                {
                    preferences.TitleId = title.Id;
                    SaveTrainerCard();
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(title.Requirement);
            }
            ImGui.EndCombo();
        }
        ImGui.TextUnformatted("Card colour");
        var color = card.Color;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##TrainerCardColor", TrainerCard.ColorNames[color]))
        {
            for (var index = 0; index < TrainerCard.ColorNames.Count; index++)
            {
                if (!ImGui.Selectable(TrainerCard.ColorNames[index], color == index)) continue;
                preferences.Color = index;
                SaveTrainerCard();
            }
            ImGui.EndCombo();
        }
    }

    private void SaveTrainerCard() => trainerCardDirty = true;

    private void DrawTrainerRecords()
    {
        if (trainer is null) return;
        DrawTrainerWallet();
        DrawFrontierProfile();
        if (ImGui.CollapsingHeader("Regional badge case", ImGuiTreeNodeFlags.DefaultOpen)) DrawRegionalBadgeCase();
        if (ImGui.CollapsingHeader($"Challenge medals ({trainer.PveProgress.Medals.Count})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var group in trainer.PveProgress.Medals.GroupBy(entry => entry.OpponentId))
            {
                if (!ImGui.TreeNode(opponents.FirstOrDefault(entry => entry.Id == group.Key)?.Name ?? group.Key)) continue;
                ImGui.TextWrapped(string.Join(" / ", group.Select(entry => MedalLabel(entry.MedalId))));
                ImGui.TreePop();
            }
            if (trainer.PveProgress.Medals.Count == 0) ImGui.TextWrapped("Clear a trainer normally, then return for optional challenges.");
        }
        if (ImGui.CollapsingHeader("Battle records", ImGuiTreeNodeFlags.DefaultOpen)) DrawTrainerBattleRecords();
        ImGui.Spacing();
        ImGui.TextWrapped($"Collection: {trainer.CollectionPoints.CompletedObjectives:N0} objectives completed / {trainer.CollectionPoints.TotalEarned:N0} lifetime CP");
        if (DrawAccentSmallButton("Open collection goals", PokemonBlue)) mainSection = 7;
    }
}
