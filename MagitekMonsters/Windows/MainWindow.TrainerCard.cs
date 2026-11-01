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
            var card = TrainerCard.Resolve(trainer, trainer.Collection.TrainerCard ?? preferences);
            DrawTrainerCardPreview(card);
            ImGui.Spacing();
            if (DrawAccentSmallButton("Copy card summary", CompanionAccent))
            {
                Interlocked.Exchange(ref pendingClipboard, TrainerCard.Summary(trainer, card));
                SetStatus("Trainer card summary copied.");
            }
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Progress & records"))
        {
            DrawTrainerRecords();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Recent activity"))
        {
            DrawRecentActivity();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Customise"))
        {
            var card = TrainerCard.Resolve(trainer, preferences);
            DrawTrainerCardPreview(card);
            ImGui.Spacing();
            ImGui.BeginDisabled(busy);
            DrawTrainerCardEditor(preferences, card);
            ImGui.Checkbox("Share my card with other trainers", ref trainerCardShared);
            if (preferences.Shared != trainerCardShared) { preferences.Shared = trainerCardShared; SaveTrainerCard(); }
            ImGui.TextWrapped("Shared cards show your partner, title, showcase and the four stats above to logged-in trainers.");
            ImGui.BeginDisabled(!trainerCardDirty && trainer.Collection.TrainerCard is not null);
            if (DrawAccentSmallButton("Save card", CompanionAccent)) Run(PublishTrainerCard, "Saving trainer card...");
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (DrawAccentSmallButton("Reset changes", CompanionMuted)) { trainerCardDirty = false; trainerCardDraft = null; }
            ImGui.EndDisabled();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Identity"))
        {
            DrawTrainerIdentity();
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

    private static Vector4 TrainerCardColor(int color) => color switch
    {
        1 => PokemonRed,
        2 => PokemonGreen,
        3 => PokemonYellow,
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
            + 2 * line + 6 * gap + badgeRows * (Math.Max(28 * scale, line) + gap);
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
                DrawClippedText(card.Title, ImGui.GetContentRegionAvail().X, accent);
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
                    DrawTrainerCardBadge(badge, accent);
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

    private static void DrawTrainerCardBadge(TrainerCardBadge badge, Vector4 accent)
    {
        var scale = CompanionScale;
        var size = Math.Max(28 * scale, ImGui.GetTextLineHeight());
        var width = ImGui.GetContentRegionAvail().X;
        var min = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(size));
        draw.AddCircleFilled(min + new Vector2(size / 2), size / 2, ImGui.GetColorU32(accent with { W = 0.2f }));
        draw.AddCircle(min + new Vector2(size / 2), size / 2 - 1, ImGui.GetColorU32(accent));
        draw.AddText(min + (new Vector2(size) - ImGui.CalcTextSize(badge.Mark)) / 2, ImGui.GetColorU32(accent), badge.Mark);
        ImGui.SameLine(0, 6 * scale);
        DrawClippedText(badge.Name, Math.Max(1, width - size - 6 * scale), ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{badge.Name}\n{badge.Detail}");
    }

    private void DrawTrainerCardEditor(TrainerCardPreferences preferences, TrainerCardView card)
    {
        ImGui.TextWrapped("Appearance saves for this trainer on this device. Titles and badges unlock through your progress.");
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
            foreach (var pokemon in trainer!.Party.Concat(trainer.Storage).OrderBy(entry => entry.PartySlot is null).ThenBy(entry => entry.PartySlot).ThenBy(entry => entry.BoxNumber).ThenBy(entry => entry.BoxSlot))
            {
                var label = TrainerCard.PartnerLabel(pokemon);
                if (!string.IsNullOrWhiteSpace(trainerCardSearch) && !label.Contains(trainerCardSearch.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                matches++;
                var location = pokemon.PartySlot is int slot ? $"Party {slot}" : $"Box {pokemon.BoxNumber}, slot {pokemon.BoxSlot}";
                if (ImGui.Selectable($"{label} / {location}##CardPartner{pokemon.Id}", preferences.FeaturedPokemonId == pokemon.Id))
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
            foreach (var title in TrainerCard.Titles(trainer!))
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
        string[] colors = ["Blue", "Red", "Green", "Gold"];
        var color = card.Color;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##TrainerCardColor", ref color, colors, colors.Length))
        {
            preferences.Color = color;
            SaveTrainerCard();
        }
        ImGui.Spacing();
        ImGui.Text($"Showcase badges / {card.Badges.Count}/{TrainerCard.MaximumBadges} selected");
        if (DrawAccentSmallButton("Use automatic showcase", CompanionMuted))
        {
            preferences.BadgeIds = null;
            SaveTrainerCard();
        }
        if (card.EarnedBadges.Count == 0) { ImGui.TextWrapped("Earn Gym badges, Frontier achievements or collection objectives to unlock showcase badges."); return; }
        var selected = card.Badges.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        ImGui.BeginChild("TrainerCardBadgePicker", new Vector2(0, 150 * CompanionScale), true);
        foreach (var badge in card.EarnedBadges)
        {
            var active = selected.Contains(badge.Id);
            ImGui.BeginDisabled(!active && selected.Count >= TrainerCard.MaximumBadges);
            if (ImGui.Checkbox($"{badge.Name}##CardBadge{badge.Id}", ref active))
            {
                if (active) selected.Add(badge.Id); else selected.Remove(badge.Id);
                preferences.BadgeIds = card.EarnedBadges.Where(entry => selected.Contains(entry.Id)).Select(entry => entry.Id).ToList();
                SaveTrainerCard();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(!active && selected.Count >= TrainerCard.MaximumBadges ? "Remove a showcase badge to make room." : badge.Detail);
        }
        ImGui.EndChild();
    }

    private void SaveTrainerCard() => trainerCardDirty = true;

    private void DrawTrainerRecords()
    {
        if (trainer is null) return;
        DrawFrontierProfile();
        if (ImGui.CollapsingHeader("Gym badge case", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (var index = 0; index < 8; index++)
                ImGui.TextColored(index < trainer.Pokedex.Badges.Count ? PokemonYellow : CompanionMuted,
                    index < trainer.Pokedex.Badges.Count ? trainer.Pokedex.Badges[index] : $"Badge {index + 1}: locked");
        }
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
        if (ImGui.CollapsingHeader("Battle records", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var profile = trainer.PvpProfile;
            ImGui.TextWrapped($"Trainer battles: {trainer.PveProgress.TotalWins:N0} wins / {trainer.PveProgress.TotalLosses:N0} losses / {trainer.PveProgress.TotalAttempts:N0} attempts");
            ImGui.TextWrapped($"Ranked: {profile.RankedWins:N0} wins / {profile.RankedLosses:N0} losses / {profile.RankedDraws:N0} draws / Rank {profile.Rank}");
            ImGui.TextWrapped($"Casual: {profile.CasualWins:N0} wins / {profile.CasualLosses:N0} losses / {profile.CasualDraws:N0} draws");
        }
        ImGui.Spacing();
        ImGui.TextWrapped($"Collection: {trainer.CollectionPoints.CompletedObjectives:N0} objectives completed / {trainer.CollectionPoints.TotalEarned:N0} lifetime CP");
        if (DrawAccentSmallButton("Open collection goals", PokemonBlue)) mainSection = 7;
    }
}
