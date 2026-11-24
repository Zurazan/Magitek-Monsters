using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private int onlineView = 1;
    private string? onlineInvitationId;
    private bool onlineTeamInitialized;
    private bool friendlyTeamInitialized;

    private DirectChallengeDto? OnlineInvitation => pvpStatus?.Challenges.FirstOrDefault(invitation => invitation.Id == onlineInvitationId);

    private void DrawOnlineLobbyHeader(PvpProfileDto profile)
    {
        var openDetails = false;
        if (ImGui.BeginTable("OnlineSeasonSummary", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Season", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Season details").X + 2 * ImGui.GetStyle().FramePadding.X);
            ImGui.TableNextColumn(); OnlineText(profile.Season.Name, CompanionAccent);
            ImGui.TableNextColumn(); openDetails = DrawAccentSmallButton("Season details", CompanionAccent);
            ImGui.EndTable();
        }
        if (openDetails) ImGui.OpenPopup("Online season details");
        var end = DateTimeOffset.TryParse(profile.Season.EndsAt, out var at) ? $"Ends {at.LocalDateTime:d}" : "Open preseason";
        var next = profile.NextRankAt;
        OnlineText(next is int threshold ? $"Rank {profile.Rank} / {Math.Max(0, profile.RankPoints - profile.CurrentRankStart):N0} of {Math.Max(1, threshold - profile.CurrentRankStart):N0} points to next rank / {end}"
            : $"Rank {profile.Rank} / Highest rank reached / {end}", CompanionMuted);
        DrawProgressBar(next is int target ? Math.Clamp((profile.RankPoints - profile.CurrentRankStart) / (float)Math.Max(1, target - profile.CurrentRankStart), 0, 1) : 1, new Vector2(-1, 4 * CompanionScale), "", ExpBarBlue);
        ImGui.Spacing();
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(560 * CompanionScale, work.X * .9f), Math.Min(520 * CompanionScale, work.Y * .85f)), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Online season details"))
        { ImGui.PushTextWrapPos(0); DrawCompanionOnlineHeader(profile); ImGui.PopTextWrapPos(); ImGui.EndPopup(); }
    }

    private void DrawOnlineLobby(PvpSeasonDto season)
    {
        onlineView = Math.Clamp(onlineView, 0, 2);
        if (onlineView < 2) pvpModeIndex = onlineView;
        var scale = CompanionScale;
        var incoming = pvpStatus!.Challenges.Where(invitation => invitation.FromUserId != trainer!.User.Id).ToList();
        if (ImGui.BeginTable("OnlineMatchTabs", 3, ImGuiTableFlags.SizingStretchSame))
        {
            for (var index = 0; index < 3; index++)
            {
                ImGui.TableNextColumn();
                var accent = index == 1 ? PokemonYellow : index == 2 ? new Vector4(0.75f, 0.62f, 0.95f, 1) : PokemonBlue;
                var label = index == 0 ? "Casual" : index == 1 ? "Ranked" : "Friendly";
                var note = index == 0 ? "Record only" : index == 1 ? "Season points" : incoming.Count > 0 ? $"{incoming.Count} received" : "Direct invites";
                var content = Math.Max(1, ImGui.GetContentRegionAvail().X - 18 * scale);
                var height = Math.Max(52 * scale, ImGui.GetTextLineHeight() + ImGui.CalcTextSize(note, false, content).Y + 18 * scale);
                var clicked = DrawPveCardSurface("OnlineMode##" + index, height, onlineView == index, out var min, out var max, out var after);
                ImGui.SetCursorScreenPos(min + new Vector2(9, 6) * scale);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content); OnlineText(label, onlineView == index ? accent : CompanionMuted); ImGui.PopTextWrapPos();
                ImGui.SetCursorScreenPos(min + new Vector2(9 * scale, 10 * scale + ImGui.GetTextLineHeight()));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content); OnlineText(note, CompanionMuted); ImGui.PopTextWrapPos();
                ImGui.SetCursorScreenPos(after);
                if (clicked)
                {
                    onlineView = index;
                    if (index < 2) pvpModeIndex = index;
                    else if (incoming.Count > 0 && OnlineInvitation is null) onlineInvitationId = incoming[0].Id;
                    pvpTeamError = string.Empty;
                }
            }
            ImGui.EndTable();
        }
        OnlineText(onlineView == 2 ? "Challenge a Trainer directly. No rank, record or currency changes."
            : onlineView == 1 ? $"Ranked / Win +{season.WinPoints}, draw +{season.DrawPoints}, loss +{season.LossPoints} points"
            : "Casual / Same format rules, no ranked points. Tier bans still apply.", onlineView == 1 ? PokemonYellow : CompanionAccent);
        if (incoming.Count > 0 && (onlineView != 2 || OnlineInvitation is null))
        {
            OnlineText($"Invitation from {incoming[0].FromName}", PokemonGreen);
            if (DrawAccentSmallButton("Review invitation", PokemonGreen))
            { onlineView = 2; onlineInvitationId = incoming[0].Id; pvpTeamError = string.Empty; }
        }
        ImGui.Spacing();
        var format = onlineView == 2 ? DrawOnlineFriendlySetup() : DrawPvpFormats(season);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var ids = onlineView == 2 ? friendlyTeamIds : pvpTeamIds;
        if (format is not null)
        {
            var owned = OnlineOwnedPokemon();
            var party = owned.Where(pokemon => pokemon.PartySlot is not null).ToList();
            SynchronizeOnlineRoster(ids, owned, format.MaxTeamSize);
            var initialized = onlineView == 2 ? friendlyTeamInitialized : onlineTeamInitialized;
            if (!initialized && owned.Count > 0)
            {
                foreach (var pokemon in party.Take(format.MaxTeamSize)) ids.Add(pokemon.Id);
                if (onlineView == 2) friendlyTeamInitialized = true; else onlineTeamInitialized = true;
            }
            OnlineText($"REGISTERED TEAM / {ids.Count} of {format.MaxTeamSize}", CompanionAccent);
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            ImGui.BeginDisabled(CollectionLocked);
            if (DrawAccentSmallButton("Choose Pokémon", CompanionAccent)) OpenOnlineRosterPicker(format);
            SameLineIfFits("Use party", right);
            if (DrawAccentSmallButton("Use party", CompanionAccent)) { ids.Clear(); foreach (var pokemon in party.Take(format.MaxTeamSize)) ids.Add(pokemon.Id); pvpTeamError = string.Empty; }
            SameLineIfFits("Clear", right);
            if (DrawAccentSmallButton("Clear", CompanionMuted)) { ids.Clear(); pvpTeamError = string.Empty; }
            var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (220 * scale)), 1, 3);
            if (ImGui.BeginTable("OnlinePartyGrid", columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var pokemon in PvpRoster.Selected(owned, ids))
                {
                    ImGui.TableNextColumn();
                    var selected = ids.Contains(pokemon.Id);
                    if (DrawOnlinePartyCard(pokemon, format, selected, selected || ids.Count < format.MaxTeamSize))
                    { if (selected) ids.Remove(pokemon.Id); else ids.Add(pokemon.Id); pvpTeamError = string.Empty; }
                }
                ImGui.EndTable();
            }
            ImGui.EndDisabled();
            if (ids.Count == 0) ImGui.TextWrapped("Choose Pokémon from your party or PC Storage. Your PvE party stays unchanged.");
        }
        else ImGui.TextWrapped("Choose an available format first.");
        ImGui.Spacing();
        DrawOnlineLobbyFooter(format, ids, season);
        DrawOnlineRosterPicker();
    }

    private bool DrawOnlinePartyCard(PokemonDto pokemon, PvpFormatDto format, bool selected, bool canPick)
    {
        var scale = CompanionScale;
        ImGui.PushID(pokemon.Id);
        var min = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var padding = 8 * scale; var gap = 4 * scale; var spriteSize = 40 * scale;
        var content = Math.Max(1, width - 2 * padding); var textWidth = Math.Max(1, content - spriteSize - padding);
        var name = pokemon.Nickname.Length > 0 ? pokemon.Nickname : SpeciesDisplayCatalog.Name(pokemon.Appearance);
        var level = $"Lv. {PvpPresentation.BattleLevel(format)} / {PvpRoster.Location(pokemon)}";
        var held = itemCatalog.FirstOrDefault(item => item.Id == pokemon.HeldItem)?.Name ?? (pokemon.HeldItem.Length > 0 ? pokemon.HeldItem : "None");
        var headerHeight = Math.Max(spriteSize, ImGui.CalcTextSize(name, false, textWidth).Y);
        var typeHeight = Math.Max(14 * scale, ImGui.GetTextLineHeight());
        var levelHeight = ImGui.CalcTextSize(level, false, content).Y;
        var itemHeight = ImGui.CalcTextSize("Item: " + held, false, content).Y;
        var height = 2 * padding + headerHeight + typeHeight + levelHeight + itemHeight + 3 * gap;
        var clicked = ImGui.InvisibleButton("PickOnlinePokemon", new Vector2(width, height));
        var hover = ImGui.IsItemHovered();
        if (hover) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip($"{name}\n{pokemon.DisplaySpecies} / {PvpRoster.Location(pokemon)}\nHeld item: {held}\n{PvpPresentation.LevelRule(format)} (owned Lv. {pokemon.Level})\n{(selected ? "Click to remove from registration." : canPick ? "Click to register." : "Team is full. Remove another Pokémon first.")}"); }
        var after = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.19f, 0.28f, 1) : new Vector4(0.10f, 0.10f, 0.11f, 1)), 4 * scale);
        if (selected || hover) draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(CompanionAccent with { W = selected ? 0.7f : 0.4f }), 4 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(padding)); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(spriteSize));
        ImGui.SetCursorScreenPos(min + new Vector2(padding * 2 + spriteSize, padding));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth); OnlineText(name, selected ? CompanionAccent : CompanionMuted); ImGui.PopTextWrapPos();
        var y = min.Y + padding + headerHeight + gap;
        ImGui.SetCursorScreenPos(new Vector2(min.X + padding, y));
        DrawStarterTypeBadges(pokemon.Types.ToArray(), Math.Max(1, Math.Min(64 * scale, (content - Math.Max(0, pokemon.Types.Count - 1) * 5 * scale) / Math.Max(1, pokemon.Types.Count))), scale);
        y += typeHeight + gap;
        ImGui.SetCursorScreenPos(new Vector2(min.X + padding, y));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content); OnlineText(level, CompanionMuted); ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(new Vector2(min.X + padding, y + levelHeight + gap));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content); OnlineText("Item: " + held, CompanionMuted); ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(after); ImGui.PopID();
        return clicked && canPick;
    }

    private PvpFormatDto? DrawOnlineFriendlySetup()
    {
        var invitations = pvpStatus!.Challenges;
        if (onlineInvitationId is not null && !invitations.Any(invitation => invitation.Id == onlineInvitationId)) onlineInvitationId = null;
        if (HasOutgoingInvitation && onlineInvitationId is null) onlineInvitationId = invitations.FirstOrDefault(invitation => invitation.FromUserId == trainer!.User.Id)?.Id;
        var selected = OnlineInvitation;
        ImGui.TextColored(CompanionAccent, "FRIENDLY BATTLE");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##FriendlyInvitation", selected is null ? "New invitation" : selected.FromUserId == trainer!.User.Id ? $"Sent to {selected.ToName}" : $"From {selected.FromName}"))
        {
            if (ImGui.Selectable("New invitation", selected is null)) onlineInvitationId = null;
            foreach (var invitation in invitations)
                if (ImGui.Selectable($"{(invitation.FromUserId == trainer!.User.Id ? "To " + invitation.ToName : "From " + invitation.FromName)}##{invitation.Id}", invitation.Id == onlineInvitationId)) onlineInvitationId = invitation.Id;
            ImGui.EndCombo();
        }
        selected = OnlineInvitation;
        if (friendlyFormats.Count == 0)
        {
            if (DrawAccentButton("Reload formats", Vector2.Zero, CompanionAccent)) Run(RefreshFriendlyFormats, "Loading friendly formats...");
            return null;
        }
        PvpFormatDto? format;
        if (selected is not null)
        {
            var outgoing = selected.FromUserId == trainer!.User.Id;
            ImGui.TextWrapped(outgoing ? $"Waiting for {selected.ToName}" : $"{selected.FromName} challenges you");
            DrawCopyableTrainerId(outgoing ? selected.ToTrainerId : selected.FromTrainerId);
            ImGui.BeginDisabled(busy);
            if (DrawAccentSmallButton("Open trainer card", CompanionAccent)) OpenTrainerProfile(outgoing ? selected.ToTrainerId : selected.FromTrainerId);
            ImGui.EndDisabled();
            var seconds = DateTimeOffset.TryParse(selected.ExpiresAt, out var expiry) ? Math.Max(0, (int)(expiry - DateTimeOffset.UtcNow).TotalSeconds) : 0;
            ImGui.TextColored(PokemonYellow, $"Expires in {seconds / 60}:{seconds % 60:00}");
            format = friendlyFormats.FirstOrDefault(option => option.Id == selected.FormatId);
            if (format is null && selected.FormatId is null) format = new PvpFormatDto { Id = "classic", Name = selected.FormatName, MinTeamSize = 3, MaxTeamSize = 3, AdjustLevel = 50, MaxLevel = 100 };
            ImGui.TextWrapped(selected.FormatName);
        }
        else
        {
            ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##ChallengeTrainer", "Trainer UID or vanity code", ref challengeTrainerId, 40);
            if (DrawAccentSmallButton("Choose friend##Battle", CompanionAccent)) OpenFriendPicker("battle");
            if (!friendlyFormats.Any(option => option.Id == friendlyFormatId)) friendlyFormatId = friendlyFormats[0].Id;
            format = DrawOnlineFormatCombo(friendlyFormats, ref friendlyFormatId, "FriendlyFormat");
        }
        if (format is not null) DrawOnlineFormatSummary(format);
        return format;
    }

    private PvpFormatDto? DrawOnlineFormatCombo(IReadOnlyList<PvpFormatDto> formats, ref string selected, string id)
    {
        var selectedId = selected;
        var current = formats.FirstOrDefault(format => format.Id == selectedId);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##" + id, current?.Name ?? "Choose format"))
        {
            foreach (var format in formats) if (ImGui.Selectable(format.Name, format.Id == selected)) { selected = format.Id; pvpTeamError = string.Empty; }
            ImGui.EndCombo();
        }
        selectedId = selected;
        var result = formats.FirstOrDefault(format => format.Id == selectedId);
        if (result is not null && ImGui.CalcTextSize(result.Name).X > ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X * 2)
            OnlineText(result.Name, CompanionAccent);
        return result;
    }

    private void DrawOnlineFormatSummary(PvpFormatDto format)
    {
        OnlineText($"{(format.GameType == "doubles" ? "Doubles" : "Singles")} / Battle Lv. {PvpPresentation.BattleLevel(format)} / {PvpPresentation.TeamRule(format)}", CompanionAccent);
        if (format.PickedTeamSize is int picked) OnlineText($"Pick {picked} and your leads in team preview.", CompanionMuted);
        else OnlineText("Choose your lead in team preview. All registered Pokémon battle.", CompanionMuted);
        if (ImGui.CollapsingHeader("Format rules & battle details"))
        {
            ImGui.TextWrapped("Owned levels, IVs, EVs and moves stay unchanged. Held items work; Bag items cannot be used in PvP.");
            if (format.OpenTeamSheets) ImGui.TextWrapped("Open team sheets: both players can inspect moves, ability, item and Tera type.");
            if (onlineView == 1) ImGui.TextWrapped(RankedForfeitRules.Notice);
            foreach (var rule in format.Rules) ImGui.TextWrapped(rule);
        }
    }

    private void DrawOnlineLobbyFooter(PvpFormatDto? format, HashSet<string> ids, PvpSeasonDto season)
    {
        var scale = CompanionScale;
        if (!string.IsNullOrWhiteSpace(pvpTeamError))
        {
            if (DrawAccentButton("Team rejected / View details", new Vector2(-1, 26 * scale), PokemonRed)) ImGui.OpenPopup("Online team validation");
        }
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(560 * scale, work.X * .9f), Math.Min(380 * scale, work.Y * .85f)), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Online team validation"))
        {
            ImGui.TextColored(PokemonRed, "TEAM VALIDATION"); ImGui.TextWrapped(pvpTeamError);
            ImGui.Spacing(); ImGui.TextWrapped("Casual and Friendly keep the chosen format's bans. National Dex is not Anything Goes. An Uber needs an eligible Ubers format.");
            if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        OnlineText(format is null ? "Choose a format" : ids.Count < format.MinTeamSize ? $"Choose at least {format.MinTeamSize} Pokémon to continue."
            : $"{ids.Count}/{format.MaxTeamSize} registered / Ready for {(onlineView == 2 ? "a friendly match" : onlineView == 1 ? "ranked" : "casual")}", CompanionMuted);
        var valid = format is not null && ids.Count >= format.MinTeamSize && ids.Count <= format.MaxTeamSize;
        if (onlineView < 2)
        {
            var remaining = pvpStatus?.Profile.RankedCooldownUntil is { } until ? Math.Max(0, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds)) : 0;
            var paused = onlineView == 1 && remaining > 0;
            ImGui.BeginDisabled(CollectionLocked || !valid || !season.FormatsAvailable || paused);
            if (DrawAccentButton(paused ? $"Ranked queue paused / {remaining / 60}:{remaining % 60:D2}" : onlineView == 1 ? "Find ranked battle" : "Find casual battle", new Vector2(-1, 34 * scale), onlineView == 1 ? PokemonYellow : PokemonBlue))
                Run(JoinPvpQueue, "Joining matchmaking...");
            ImGui.EndDisabled();
        }
        else
        {
            var invitation = OnlineInvitation;
            if (invitation is not null)
            {
                var outgoing = invitation.FromUserId == trainer!.User.Id;
                var sideBySide = ImGui.GetContentRegionAvail().X >= ImGui.CalcTextSize("Accept with this team").X * 2 + 4 * ImGui.GetStyle().FramePadding.X + 8 * scale;
                var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - 8 * scale) / 2);
                ImGui.BeginDisabled(busy);
                if (DrawAccentButton(outgoing ? "Cancel invitation" : "Decline", new Vector2(outgoing || !sideBySide ? -1 : width, 34 * scale), PokemonOrange))
                    RunOnlineAction(async () => { pvpStatus = await InSession(plugin.Api.ResolveChallenge(invitation.Id, outgoing ? "cancelled" : "declined")); }, "Updating invitation...");
                ImGui.EndDisabled();
                if (!outgoing)
                {
                    if (sideBySide) ImGui.SameLine(0, 8 * scale);
                    ImGui.BeginDisabled(CollectionLocked || !valid);
                    if (DrawAccentButton("Accept with this team", new Vector2(sideBySide ? width : -1, 34 * scale), PokemonGreen))
                    { var team = FriendlyTeam(); RunOnlineAction(async () => { pvpStatus = await InSession(plugin.Api.AcceptChallenge(invitation.Id, team)); }, "Starting friendly battle..."); }
                    ImGui.EndDisabled();
                }
            }
            else
            {
                ImGui.BeginDisabled(CollectionLocked || !valid || string.IsNullOrWhiteSpace(challengeTrainerId));
                if (DrawAccentButton("Send friendly invitation", new Vector2(-1, 34 * scale), CompanionAccent) && format is not null)
                {
                    var id = challengeTrainerId.Trim(); var team = FriendlyTeam(); var formatId = format.Id;
                    RunOnlineAction(async () => { pvpStatus = await InSession(plugin.Api.ChallengeTrainer(id, team, formatId)); }, "Sending invitation...");
                }
                ImGui.EndDisabled();
            }
        }
    }

    private static void OnlineText(string text, Vector4 color)
    { ImGui.PushStyleColor(ImGuiCol.Text, color); ImGui.TextWrapped(text); ImGui.PopStyleColor(); }

    private static void DrawOnlineForfeitNotice()
        => ImGui.TextWrapped(RankedForfeitRules.Notice);

    private void RunOnlineAction(Func<Task> action, string message) => Run(async () =>
    {
        pvpTeamError = string.Empty;
        try { await action(); SetStatus("Friendly invitation updated."); }
        catch (Services.ApiException exception) when (exception.StatusCode == 400) { pvpTeamError = exception.Message; SetStatus("Team rejected. Open validation details.", true); }
    }, message);
}
