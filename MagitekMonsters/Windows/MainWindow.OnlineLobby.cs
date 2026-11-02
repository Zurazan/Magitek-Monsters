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
        var width = ImGui.GetContentRegionAvail().X;
        var openDetails = false;
        if (ImGui.BeginTable("OnlineSeasonSummary", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Season", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthFixed, 105 * CompanionScale);
            ImGui.TableNextColumn(); DrawClippedText($"{profile.Season.Name} / Rank {profile.Rank}", ImGui.GetContentRegionAvail().X, CompanionAccent);
            ImGui.TableNextColumn(); openDetails = DrawAccentSmallButton("Season details", CompanionAccent);
            ImGui.EndTable();
        }
        if (openDetails) ImGui.OpenPopup("Online season details");
        var end = DateTimeOffset.TryParse(profile.Season.EndsAt, out var at) ? $"Ends {at.LocalDateTime:d}" : "Open preseason";
        DrawClippedText($"{end} / Ranked {profile.RankedWins}W {profile.RankedLosses}L / Casual {profile.CasualWins}W {profile.CasualLosses}L", width, CompanionMuted);
        var next = profile.NextRankAt;
        DrawProgressBar(next is int target ? Math.Clamp((profile.RankPoints - profile.CurrentRankStart) / (float)Math.Max(1, target - profile.CurrentRankStart), 0, 1) : 1, new Vector2(-1, 4 * CompanionScale), "", ExpBarBlue);
        DrawCurrencyGuideShortcut();
        ImGui.SetNextWindowSize(new Vector2(520, 440) * CompanionScale, ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Online season details")) { DrawCompanionOnlineHeader(profile); ImGui.EndPopup(); }
    }

    private void DrawOnlineLobby(PvpSeasonDto season)
    {
        var scale = CompanionScale;
        var incoming = pvpStatus!.Challenges.Where(invitation => invitation.FromUserId != trainer!.User.Id).ToList();
        if (ImGui.BeginTable("OnlineMatchTabs", 3, ImGuiTableFlags.SizingStretchSame))
        {
            for (var index = 0; index < 3; index++)
            {
                ImGui.TableNextColumn();
                var accent = index == 1 ? PokemonYellow : index == 2 ? new Vector4(0.75f, 0.62f, 0.95f, 1) : PokemonBlue;
                var label = index == 0 ? "Casual" : index == 1 ? "Ranked" : incoming.Count > 0 ? $"Friends ({incoming.Count})" : "Friends";
                if (DrawAccentButton(label, new Vector2(-1, 36 * scale), onlineView == index ? accent : CompanionMuted))
                {
                    onlineView = index;
                    if (index < 2) pvpModeIndex = index;
                    else if (incoming.Count > 0 && OnlineInvitation is null) onlineInvitationId = incoming[0].Id;
                    pvpTeamError = string.Empty;
                }
            }
            ImGui.EndTable();
        }
        DrawClippedText(onlineView == 2 ? "Challenge a Trainer directly. No rank, record or currency changes."
            : onlineView == 1 ? $"Ranked / Win +{season.WinPoints}, draw +{season.DrawPoints}, loss +{season.LossPoints} points"
            : "Casual / Same format rules, no ranked points. Tier bans still apply.", ImGui.GetContentRegionAvail().X, onlineView == 1 ? PokemonYellow : CompanionAccent);
        if (incoming.Count > 0 && (onlineView != 2 || OnlineInvitation is null))
        {
            if (DrawAccentButton($"Review invitation from {incoming[0].FromName}", new Vector2(-1, 28 * scale), PokemonGreen))
            { onlineView = 2; onlineInvitationId = incoming[0].Id; pvpTeamError = string.Empty; }
        }
        ImGui.Spacing();
        var footer = (string.IsNullOrWhiteSpace(pvpTeamError) ? 64 : 94) * scale;
        var available = ImGui.GetContentRegionAvail();
        var layout = PvpPresentation.Lobby(available.X, Math.Max(1, available.Y - footer), scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("OnlineSetup", new Vector2(layout.SetupWidth, layout.SetupHeight), true);
        var format = onlineView == 2 ? DrawOnlineFriendlySetup() : DrawPvpFormats(season);
        ImGui.EndChild();
        if (layout.SideBySide) ImGui.SameLine(0, layout.Gap); else ImGui.Spacing();
        ImGui.BeginChild("OnlineParty", new Vector2(layout.TeamWidth, layout.TeamHeight), true);
        var ids = onlineView == 2 ? friendlyTeamIds : pvpTeamIds;
        if (format is not null)
        {
            var owned = OnlineOwnedPokemon();
            var party = owned.Where(pokemon => pokemon.PartySlot is not null).ToList();
            PvpRoster.Synchronize(ids, owned, format.MaxTeamSize);
            var initialized = onlineView == 2 ? friendlyTeamInitialized : onlineTeamInitialized;
            if (!initialized && owned.Count > 0)
            {
                foreach (var pokemon in party.Take(format.MaxTeamSize)) ids.Add(pokemon.Id);
                if (onlineView == 2) friendlyTeamInitialized = true; else onlineTeamInitialized = true;
            }
            DrawClippedText($"REGISTERED TEAM / {ids.Count} of {format.MaxTeamSize}", ImGui.GetContentRegionAvail().X, CompanionAccent);
            ImGui.BeginDisabled(CollectionLocked);
            if (DrawAccentSmallButton("Choose Pokémon", CompanionAccent)) OpenOnlineRosterPicker(format);
            ImGui.SameLine();
            if (DrawAccentSmallButton("Use party", CompanionAccent)) { ids.Clear(); foreach (var pokemon in party.Take(format.MaxTeamSize)) ids.Add(pokemon.Id); pvpTeamError = string.Empty; }
            ImGui.SameLine();
            if (DrawAccentSmallButton("Clear", CompanionMuted)) { ids.Clear(); pvpTeamError = string.Empty; }
            var columns = ImGui.GetContentRegionAvail().X >= 300 * scale ? 2 : 1;
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
        ImGui.EndChild();
        ImGui.PopStyleColor(); ImGui.PopStyleVar();
        DrawOnlineLobbyFooter(format, ids, season);
        DrawOnlineRosterPicker();
    }

    private bool DrawOnlinePartyCard(PokemonDto pokemon, PvpFormatDto format, bool selected, bool canPick)
    {
        var scale = CompanionScale;
        ImGui.PushID(pokemon.Id);
        var min = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = Math.Max(92 * scale, 4 * ImGui.GetTextLineHeight() + 22 * scale);
        var clicked = ImGui.InvisibleButton("PickOnlinePokemon", new Vector2(width, height));
        var hover = ImGui.IsItemHovered();
        if (hover) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip($"{pokemon.DisplaySpecies} / {PvpRoster.Location(pokemon)}\n{PvpPresentation.LevelRule(format)} (owned Lv. {pokemon.Level})\n{(selected ? "Click to remove from registration." : canPick ? "Click to register." : "Team is full. Remove another Pokémon first.")}"); }
        var after = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.19f, 0.28f, 1) : new Vector4(0.10f, 0.10f, 0.11f, 1)), 4 * scale);
        if (selected || hover) draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(CompanionAccent with { W = selected ? 0.7f : 0.4f }), 4 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(8, 24) * scale); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(40 * scale));
        var textWidth = Math.Max(1, width - 66 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(58, 7) * scale); DrawClippedText((selected ? "[IN] " : "") + pokemon.Nickname, textWidth, selected ? CompanionAccent : CompanionMuted);
        ImGui.SetCursorScreenPos(min + new Vector2(58, 28) * scale);
        DrawConsoleTypeRow(pokemon.Types.ToArray(), BattleLayout.Types(textWidth, scale * 0.72f, pokemon.Types.Count));
        ImGui.SetCursorScreenPos(min + new Vector2(58, 47) * scale); DrawClippedText($"Lv. {PvpPresentation.BattleLevel(format)} / {PvpRoster.Location(pokemon)}", textWidth, CompanionMuted);
        var held = itemCatalog.FirstOrDefault(item => item.Id == pokemon.HeldItem)?.Name ?? (pokemon.HeldItem.Length > 0 ? pokemon.HeldItem : "No held item");
        ImGui.SetCursorScreenPos(min + new Vector2(58, 67) * scale); DrawClippedText(held, textWidth, CompanionMuted);
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
            if (!friendlyFormats.Any(option => option.Id == friendlyFormatId)) friendlyFormatId = friendlyFormats[0].Id;
            format = DrawOnlineFormatCombo(friendlyFormats, ref friendlyFormatId, "FriendlyFormat");
        }
        if (format is not null) DrawOnlineFormatSummary(format);
        ImGui.TextWrapped("Pick your registered team on the right, then send or accept below.");
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
        return formats.FirstOrDefault(format => format.Id == selectedId);
    }

    private void DrawOnlineFormatSummary(PvpFormatDto format)
    {
        ImGui.TextColored(CompanionAccent, format.GameType == "doubles" ? "DOUBLES" : "SINGLES");
        ImGui.TextWrapped(PvpPresentation.TeamRule(format));
        ImGui.TextColored(PokemonGreen, $"Battle level: {PvpPresentation.BattleLevel(format)}");
        ImGui.TextWrapped("Owned levels, IVs, EVs and moves are not changed. No Bag items in PvP.");
        if (format.PickedTeamSize is int picked) ImGui.TextWrapped($"Register your roster now. Choose {picked} Pokémon and your leads after seeing the opponent's team.");
        else ImGui.TextWrapped("All registered Pokémon enter battle. Choose your lead in team preview.");
        if (format.OpenTeamSheets) ImGui.TextWrapped("Open team sheets: both players can inspect moves, ability, item and Tera type.");
        if (onlineView == 1) ImGui.TextWrapped(RankedForfeitRules.Notice);
        if (ImGui.CollapsingHeader("Format rules and bans")) foreach (var rule in format.Rules) ImGui.TextWrapped(rule);
    }

    private void DrawOnlineLobbyFooter(PvpFormatDto? format, HashSet<string> ids, PvpSeasonDto season)
    {
        var scale = CompanionScale;
        if (!string.IsNullOrWhiteSpace(pvpTeamError))
        {
            if (DrawAccentButton("Team rejected / View details", new Vector2(-1, 26 * scale), PokemonRed)) ImGui.OpenPopup("Online team validation");
        }
        ImGui.SetNextWindowSize(new Vector2(560, 380) * scale, ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Online team validation"))
        {
            ImGui.TextColored(PokemonRed, "TEAM VALIDATION"); ImGui.TextWrapped(pvpTeamError);
            ImGui.Spacing(); ImGui.TextWrapped("Casual and Friendly keep the chosen format's bans. National Dex is not Anything Goes. An Uber needs an eligible Ubers format.");
            if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        DrawClippedText(format is null ? "Choose a format" : $"{ids.Count}/{format.MaxTeamSize} registered / Battle Lv. {PvpPresentation.BattleLevel(format)} / {(format.PickedTeamSize is int picked ? $"Pick {picked} in preview" : "Full registered team battles")}", ImGui.GetContentRegionAvail().X, CompanionMuted);
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
                var width = Math.Max(1, (ImGui.GetContentRegionAvail().X - 8 * scale) / 2);
                ImGui.BeginDisabled(busy);
                if (DrawAccentButton(outgoing ? "Cancel invitation" : "Decline", new Vector2(outgoing ? -1 : width, 34 * scale), PokemonOrange))
                    RunOnlineAction(async () => { pvpStatus = await InSession(plugin.Api.ResolveChallenge(invitation.Id, outgoing ? "cancelled" : "declined")); }, "Updating invitation...");
                ImGui.EndDisabled();
                if (!outgoing)
                {
                    ImGui.SameLine(0, 8 * scale);
                    ImGui.BeginDisabled(CollectionLocked || !valid);
                    if (DrawAccentButton("Accept with this team", new Vector2(width, 34 * scale), PokemonGreen))
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

    private static void DrawOnlineForfeitNotice()
        => ImGui.TextWrapped(RankedForfeitRules.Notice);

    private void RunOnlineAction(Func<Task> action, string message) => Run(async () =>
    {
        pvpTeamError = string.Empty;
        try { await action(); SetStatus("Friendly invitation updated."); }
        catch (Services.ApiException exception) when (exception.StatusCode == 400) { pvpTeamError = exception.Message; SetStatus("Team rejected. Open validation details.", true); }
    }, message);
}
