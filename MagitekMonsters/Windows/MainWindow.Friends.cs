using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly FriendListCache friendPages = new();
    private FriendInboxDto? friendInbox;
    private FriendEntryDto? selectedFriend;
    private volatile bool friendsRefreshRequested = true;
    private DateTimeOffset nextFriendsPollAt;
    private string friendSearch = "", friendAddCode = "", friendPickerSearch = "";
    private string? friendPickerPurpose;
    private bool openFriendPicker;
    private int friendsTab;
    private (string InviteId, string Message)? denInviteError;
    private StoragePicker? denInvitePicker;
    private readonly Dictionary<string, bool> searchFriendGroups = [];
    private FriendEntryDto? friendConfirmation;
    private bool openFriendConfirmation;
    private string friendConfirmationAction = "";

    private void ResetFriends()
    {
        friendPages.Clear(); friendInbox = null; selectedFriend = null;
        friendsRefreshRequested = true; nextFriendsPollAt = DateTimeOffset.MinValue;
        friendSearch = friendAddCode = friendPickerSearch = ""; friendsTab = 0;
        denInviteError = null; denInvitePicker = null;
        friendPickerPurpose = null; openFriendPicker = false; friendConfirmation = null; openFriendConfirmation = false;
        searchFriendGroups.Clear();
    }
    private void BackgroundFriends()
    {
        var refresh = friendsRefreshRequested || DateTimeOffset.UtcNow >= nextFriendsPollAt;
        if (refresh) { friendsRefreshRequested = false; friendPages.Clear(); }
        var selected = selectedFriend?.UserId;
        var selectedGroup = selectedFriendGroup;
        var generation = friendPages.Generation;
        var read = refresh ? null : friendPages.Take();
        if (!refresh && read is null) return;
        nextFriendsPollAt = DateTimeOffset.UtcNow.AddSeconds(60);
        RunBackground(async token =>
        {
            if (refresh)
            {
                var incoming = await InSession(plugin.Api.GetFriends("incoming", "", 0, selected, token));
                var dens = incoming.Counts.DenInvites > 0 ? await InSession(plugin.Api.GetFriends("dens", "", 0, selected, token)) : null;
                return () =>
                {
                    if (friendPages.Generation != generation) return;
                    friendInbox = new(incoming.Counts.Incoming, incoming.Entries, dens?.Total ?? 0, dens?.Entries ?? []);
                    RefreshSelectedFriend(incoming, selected, selectedGroup);
                    friendPages.Apply(new(generation, new("incoming", "", 0)), incoming);
                    if (dens is not null) { RefreshSelectedFriend(dens, selected, selectedGroup); friendPages.Apply(new(generation, new("dens", "", 0)), dens); }
                };
            }
            var request = read!.Value;
            var page = await InSession(plugin.Api.GetFriends(request.Key.Group, request.Key.Search, request.Key.Offset, selected, token));
            return () => { if (friendPages.Apply(request, page)) RefreshSelectedFriend(page, selected, selectedGroup); };
        }, () => { if (read is { } request) friendPages.Retry(request); else friendsRefreshRequested = true; }, deferLiveUpdates: false);
    }
    private void RefreshSelectedFriend(FriendPageDto page, string? requested, string group)
    {
        if (selectedFriend is null || selectedFriend.UserId != requested || selectedFriendGroup != group) return;
        if (selectedFriendGroup is "online" or "offline" or "notmutual") selectedFriend = page.Selected;
        else if (page.Group == selectedFriendGroup)
            selectedFriend = page.Entries.FirstOrDefault(entry => entry.UserId == selectedFriend.UserId && entry.RequestId == selectedFriend.RequestId);
    }
    private void ChangeFriend(Func<Task<FriendMutationDto>> action, string progress)
    {
        Run(async () =>
        {
            var result = await InSession(action()); friendsRefreshRequested = true; selectedFriend = null;
            SetStatus(result.Result switch { "requested" => "Friend request sent.", "mutual" or "accepted" => "You are now friends.",
                "remove" => "Removed from your friends.", "block" => "Trainer blocked.", "unblock" => "Trainer unblocked.",
                "invited" => "Den invitation sent.", _ => "Friends updated." });
        }, progress);
    }
    private void DrawFriends()
    {
        DrawSectionHeader("FRIENDS", "Add a trainer by UID or vanity code. Mutual friends can see each other online.");
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(Math.Max(80, width - 110 * CompanionScale));
        ImGui.InputTextWithHint("##AddFriend", "Trainer UID or vanity code", ref friendAddCode, 64);
        ImGui.SameLine(); ImGui.BeginDisabled(busy || string.IsNullOrWhiteSpace(friendAddCode));
        if (DrawAccentButton("Add trainer", new Vector2(-1, 0), CompanionAccent))
        { var code = friendAddCode.Trim(); ChangeFriend(() => plugin.Api.AddFriend(code), "Adding trainer..."); }
        ImGui.EndDisabled();
        var counts = friendPages.Counts("");
        for (var tab = 0; tab < 3; tab++)
        {
            if (tab > 0) ImGui.SameLine();
            var label = tab switch { 0 => "Friends", 1 => $"Requests [{(counts?.Incoming ?? 0) + (counts?.DenInvites ?? 0)}]", _ => $"Blocked [{counts?.Blocked ?? 0}]" };
            if (DrawAccentButton(label + "##FriendsTab" + tab, new Vector2(0, 28 * CompanionScale), friendsTab == tab ? CompanionAccent : CompanionMuted)) friendsTab = tab;
        }
        if (friendsTab == 0)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##FriendSearch", "Search name, UID or vanity code", ref friendSearch, 80)) searchFriendGroups.Clear();
        }
        var available = ImGui.GetContentRegionAvail();
        var listWidth = Math.Max(160 * CompanionScale, available.X * 0.51f);
        ImGui.BeginChild("FriendList", new Vector2(listWidth, available.Y), true);
        if (friendsTab == 0)
        {
            DrawFriendGroup("online", "Online", friendSearch, true);
            DrawFriendGroup("offline", "Offline", friendSearch, true);
            DrawFriendGroup("notmutual", "Not mutual", friendSearch, true);
        }
        else if (friendsTab == 1)
        {
            DrawFriendGroup("incoming", "Incoming requests", "", false);
            DrawFriendGroup("outgoing", "Sent requests", "", false);
            DrawFriendGroup("dens", "Den invitations", "", false);
        }
        else DrawFriendGroup("blocked", "Blocked trainers", "", false);
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("FriendDetails", new Vector2(0, available.Y), true);
        DrawFriendDetails(); ImGui.EndChild();
        DrawFriendConfirmation();
        DrawStoragePicker(ref denInvitePicker, "Join invited den");
    }
    private bool FriendGroupOpen(string group, string search)
    {
        if (search.Trim().Length > 0) return !searchFriendGroups.TryGetValue(group, out var open) || open;
        var owner = trainer!.User.Id;
        return plugin.Configuration.FriendGroups.TryGetValue(owner, out var groups) && groups.TryGetValue(group, out var stored) ? stored : group == "online";
    }
    private void DrawFriendGroup(string group, string label, string search, bool remember)
    {
        var counts = friendPages.Counts(search);
        var count = counts?.Group(group) ?? 0;
        var before = remember ? FriendGroupOpen(group, search) : !searchFriendGroups.TryGetValue("requests:" + group, out var stored) || stored;
        ImGui.SetNextItemOpen(before, ImGuiCond.Always);
        var open = ImGui.CollapsingHeader($"{label} / {(counts is null ? "..." : count.ToString("N0"))}##{group}");
        if (!remember && before != open) searchFriendGroups["requests:" + group] = open;
        if (remember && open != before)
        {
            if (search.Trim().Length > 0) searchFriendGroups[group] = open;
            else
            {
                var config = plugin.Configuration;
                if (!config.FriendGroups.TryGetValue(trainer!.User.Id, out var groups)) config.FriendGroups[trainer.User.Id] = groups = [];
                groups[group] = open; config.Save();
            }
        }
        if (!open) return;
        if (counts is null) { ImGui.TextDisabled("Loading trainers..."); return; }
        if (count == 0) { ImGui.TextDisabled("No trainers here."); return; }
        DrawFriendRows(group, search, count, false);
    }
    private void DrawFriendRows(string group, string search, int count, bool picker)
    {
        var height = Math.Max(52 * CompanionScale, ImGui.GetTextLineHeightWithSpacing() * 2 + 12 * CompanionScale);
        var start = ImGui.GetCursorPosY();
        var top = ImGui.GetScrollY();
        var first = Math.Clamp((int)((top - start) / height) - 1, 0, count);
        var last = Math.Clamp((int)((top + ImGui.GetWindowHeight() - start) / height) + 2, first, count);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
        if (first > 0) ImGui.Dummy(new Vector2(1, first * height));
        for (var index = first; index < last; index++)
        {
            var entry = friendPages.Entry(group, search, index);
            ImGui.PushID(group + ":" + index);
            var min = ImGui.GetCursorScreenPos(); var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var allowed = entry is not null && (!picker || CanPickFriend(entry));
            ImGui.BeginDisabled(!allowed);
            var clicked = ImGui.Selectable("##FriendRow", entry is not null && selectedFriend?.UserId == entry.UserId && !picker, ImGuiSelectableFlags.None, new Vector2(width, height));
            ImGui.EndDisabled();
            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(min + new Vector2(9, 5) * CompanionScale);
            DrawClippedText(entry?.Name ?? "Loading...", Math.Max(1, width - 18 * CompanionScale), entry?.Online == true ? PokemonGreen : CompanionAccent);
            ImGui.SetCursorScreenPos(min + new Vector2(9 * CompanionScale, height / 2));
            var text = entry is null ? "" : group switch
            {
                "incoming" => "Wants to be friends / " + entry.TrainerUid,
                "outgoing" => "Awaiting acceptance / " + entry.TrainerUid,
                "dens" => $"{entry.DenSpecies} / {entry.Stars} stars",
                "blocked" => "Blocked / " + entry.TrainerUid,
                _ => (entry.Online == true ? "Online" : entry.Mutual ? "Offline" : entry.Pending ? "Request sent" : "Not mutual") + " / " + entry.TrainerUid
            };
            DrawClippedText(text, Math.Max(1, width - 18 * CompanionScale), CompanionMuted);
            ImGui.SetCursorScreenPos(after);
            if (clicked && entry is not null) { if (picker) PickFriend(entry); else { selectedFriend = entry; selectedFriendGroup = group; } }
            ImGui.PopID();
        }
        if (last < count) ImGui.Dummy(new Vector2(1, (count - last) * height));
        ImGui.PopStyleVar();
    }
    private string selectedFriendGroup = "online";
    private void DrawFriendDetails()
    {
        if (selectedFriend is not { } friend) { ImGui.TextWrapped("Select a trainer to view their details and invitations."); return; }
        DrawClippedText(friend.Name, ImGui.GetContentRegionAvail().X, CompanionAccent); DrawCopyableTrainerId(friend.TrainerUid);
        if (friend.VanityCode.Length > 0) ImGui.TextWrapped(friend.VanityCode);
        ImGui.TextColored(friend.Online == true ? PokemonGreen : CompanionMuted, friend.Mutual ? friend.Online == true ? "Online / Mutual friends" : "Offline / Mutual friends" : selectedFriendGroup == "blocked" ? "Blocked" : "Not mutual / Presence private");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.BeginDisabled(busy);
        if (selectedFriendGroup == "incoming")
        {
            if (DrawAccentButton("Accept request", new Vector2(-1, 0), PokemonGreen) && friend.RequestId is { } id) ChangeFriend(() => plugin.Api.AcceptFriendRequest(id), "Accepting request...");
            if (DrawAccentButton("Decline request", new Vector2(-1, 0), CompanionMuted) && friend.RequestId is { } decline) ChangeFriend(() => plugin.Api.DeclineFriendRequest(decline), "Declining request...");
        }
        else if (selectedFriendGroup == "outgoing")
        {
            if (DrawAccentButton("Cancel request", new Vector2(-1, 0), PokemonOrange) && friend.RequestId is { } id) ChangeFriend(() => plugin.Api.CancelFriendRequest(id), "Cancelling request...");
        }
        else if (selectedFriendGroup == "dens")
        {
            ImGui.TextWrapped($"{friend.DenSpecies} / {friend.Stars} stars");
            if (denInviteError is { } error && error.InviteId == friend.RequestId)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, PokemonRed);
                ImGui.TextWrapped(error.Message);
                ImGui.PopStyleColor();
            }
            if (DrawAccentButton("Review den invitation", new Vector2(-1, 0), PokemonGreen)) ReviewFriendDen(friend);
            if (DrawAccentButton("Decline invitation", new Vector2(-1, 0), CompanionMuted) && friend.RequestId is { } decline)
                Run(async () => { await InSession(plugin.Api.DeclineDenInvitation(decline)); friendsRefreshRequested = true; SetStatus("Invitation declined."); }, "Declining invitation...");
        }
        else if (selectedFriendGroup == "blocked")
        {
            ImGui.TextWrapped("Unblocking allows invitations again. It does not restore friendship.");
            if (DrawAccentButton("Unblock trainer", new Vector2(-1, 0), PokemonOrange)) ChangeFriend(() => plugin.Api.UnblockFriend(friend.UserId), "Unblocking trainer...");
        }
        else
        {
            ImGui.BeginDisabled(friend.Online != true || !friend.Mutual);
            if (DrawAccentButton("Battle", new Vector2(-1, 32 * CompanionScale), CompanionAccent)) OpenFriendActivity(friend, "battle");
            if (DrawAccentButton("Trade", new Vector2(-1, 32 * CompanionScale), PokemonGreen)) OpenFriendActivity(friend, "trade");
            ImGui.BeginDisabled(raidRoom?.Status != "lobby" || raidRoom.Players.Count >= 4);
            if (DrawAccentButton("Invite to den", new Vector2(-1, 32 * CompanionScale), PokemonYellow)) SendFriendDen(friend);
            ImGui.EndDisabled(); ImGui.EndDisabled();
            if (raidRoom?.Status != "lobby") ImGui.TextWrapped("Open a den lobby to invite friends.");
            if (DrawAccentButton("Trainer card", new Vector2(-1, 0), CompanionAccent)) OpenTrainerProfile(friend.TrainerUid);
            if (DrawAccentButton("Remove friend", new Vector2(-1, 0), PokemonOrange)) ConfirmFriend(friend, "remove");
        }
        if (selectedFriendGroup != "blocked" && DrawAccentButton("Block trainer", new Vector2(-1, 0), PokemonRed)) ConfirmFriend(friend, "block");
        ImGui.EndDisabled();
    }
    private void ConfirmFriend(FriendEntryDto friend, string action)
    {
        friendConfirmation = friend; friendConfirmationAction = action; openFriendConfirmation = true;
    }
    private void DrawFriendConfirmation()
    {
        if (openFriendConfirmation) { openFriendConfirmation = false; ImGui.OpenPopup("Change friendship?"); }
        if (!BeginConfirmationModal("Change friendship?")) return;
        if (friendConfirmation is null) ImGui.CloseCurrentPopup();
        if (friendConfirmation is { } friend)
        {
            ImGui.TextWrapped(friendConfirmationAction == "block" ? $"Block {friend.Name}? Invitations and pending trades will be cancelled. Their online status stays private."
                : $"Remove {friend.Name}? They keep a Not mutual entry until they remove you too.");
            if (DrawConfirmationActions(friendConfirmationAction == "block" ? "Block" : "Remove", PokemonRed, busy))
            {
                var action = friendConfirmationAction; ImGui.CloseCurrentPopup(); selectedFriend = null;
                ChangeFriend(() => action == "block" ? plugin.Api.BlockFriend(friend.UserId) : plugin.Api.RemoveFriend(friend.UserId), "Updating friendship...");
            }
        }
        EndConfirmationModal();
    }
    private void OpenFriendActivity(FriendEntryDto friend, string purpose)
    {
        if (purpose == "battle") { mainSection = 4; onlineView = 2; onlineInvitationId = null; challengeTrainerId = friend.TrainerUid; serverPvpRefreshRequested = true; }
        else { mainSection = 16; tradeTrainerCode = friend.TrainerUid; tradeRefreshRequested = true; }
    }
    private void SendFriendDen(FriendEntryDto friend)
    {
        if (raidRoom is not { Status: "lobby" } room) return;
        ChangeFriend(() => plugin.Api.InviteFriendToDen(friend.UserId, room.Id), "Inviting friend to den...");
    }
    private void ReviewFriendDen(FriendEntryDto friend)
    {
        if (busy || friend.RequestId is not { } id) return;
        mainSection = 17; friendsTab = 1; selectedFriendGroup = "dens"; selectedFriend = friend;
        denInviteError = null; denInvitePicker = null;
        Run(async () =>
        {
            if (friend.ExpiresAt <= DateTimeOffset.UtcNow)
            { ShowDenInviteError(id, "This den invitation expired."); return; }
            var minimumLevel = 0;
            if (friend.Stars > 0)
            {
                var board = await InSession(plugin.Api.GetRaids(disposeCancellation.Token));
                var den = board.Dens.FirstOrDefault(den => den.Stars == friend.Stars);
                if (den?.LockReason is { Length: > 0 } reason)
                { ShowDenInviteError(id, reason); return; }
                minimumLevel = den?.MinimumLevel ?? 0;
            }
            FriendDenInviteDto invite;
            try { invite = await InSession(plugin.Api.ReviewDenInvitation(id)); }
            catch (ApiException exception) when (exception.Code is "raid_locked" or "friend_request_not_found" or "raid_lobby_closed")
            { ShowDenInviteError(id, exception.Message); return; }
            if (invite.ExpiresAt <= DateTimeOffset.UtcNow)
            { ShowDenInviteError(id, "This den invitation expired."); return; }
            if (raidRoom is { Status: "lobby" or "active" } current)
            {
                if (current.Id == invite.RoomId) { mainSection = 12; return; }
                ShowDenInviteError(id, "Leave your current den before joining another lobby.");
                return;
            }
            denInvitePicker = CreateStoragePicker(StoragePickerMode.Raid, raidRoom?.Id, SelectedRaidPokemon(), invite, minimumLevel);
            SetStatus("Choose a Pokémon to join the den.", quiet: true);
        }, "Reviewing den invitation...");
    }
    private void JoinInvitedDen(FriendDenInviteDto invite, PokemonDto pokemon)
    {
        denInvitePicker = null;
        mainSection = 12;
        Run(async () =>
        {
            try { await RaidAction("join", new RaidJoinRequest(pokemon.Id, Code: invite.Code), true); }
            catch (ApiException exception) when (exception.StatusCode is 400 or 404 or 409 or 429
                && exception.Code != "mutation_committed" && raidRoom?.Id != invite.RoomId)
            {
                if (mainSection == 12) { mainSection = 17; friendsTab = 1; }
                ShowDenInviteError(invite.InviteId, exception.Message);
            }
        }, "Joining the invited den...");
    }
    private void ShowDenInviteError(string id, string message)
    {
        denInviteError = (id, message);
        SetStatus(message, isError: true, quiet: mainSection == 17 && friendsTab == 1 && selectedFriend?.RequestId == id);
    }
    private void OpenFriendPicker(string purpose) { friendPickerPurpose = purpose; friendPickerSearch = ""; openFriendPicker = true; }
    private bool CanPickFriend(FriendEntryDto friend) => friend.Mutual && friend.Online == true
        && (friendPickerPurpose != "den" || raidRoom is { Status: "lobby" } room && room.Players.Count < 4 && !room.Players.Any(player => player.TrainerId == friend.TrainerUid));
    private void PickFriend(FriendEntryDto friend)
    {
        if (!CanPickFriend(friend)) return;
        if (friendPickerPurpose == "battle") challengeTrainerId = friend.TrainerUid;
        else if (friendPickerPurpose == "trade") tradeTrainerCode = friend.TrainerUid;
        else SendFriendDen(friend);
        ImGui.CloseCurrentPopup();
    }
    private void DrawFriendPicker()
    {
        if (openFriendPicker) { openFriendPicker = false; ImGui.OpenPopup("Choose a friend"); }
        ImGui.SetNextWindowSize(new Vector2(440, 440) * CompanionScale, ImGuiCond.Appearing);
        if (!BeginCenteredModal("Choose a friend", ImGuiWindowFlags.NoResize)) return;
        if (friendPickerPurpose is null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.TextWrapped("Choose an online mutual friend. Your selected team or trade offer stays unchanged.");
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##PickerSearch", "Search friends", ref friendPickerSearch, 80);
        var counts = friendPages.Counts(friendPickerSearch);
        ImGui.BeginChild("FriendPickerList", new Vector2(0, -40 * CompanionScale), true);
        if (counts is null) ImGui.TextDisabled("Loading friends...");
        else if (counts.Online == 0) ImGui.TextWrapped("No online friends match your search.");
        else DrawFriendRows("online", friendPickerSearch, counts.Online, true);
        ImGui.EndChild();
        if (DrawAccentButton("Cancel", new Vector2(-1, 0), CompanionMuted)) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
