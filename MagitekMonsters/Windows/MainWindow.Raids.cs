using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private RaidBoardDto? raidBoard;
    private RaidRoomDto? raidRoom;
    private volatile bool raidRefreshRequested;
    private DateTimeOffset nextRaidPollAt;
    private DateTimeOffset raidObservedAt;
    private string raidPokemonId = "";
    private string raidJoinCode = "";
    private string raidRosterSearch = "";
    private int raidDenPage;
    private int raidRosterPage;
    private bool raidPrivate;
    private bool raidRosterOpen;
    private string raidModifier = "";
    private RaidMoveDto? raidTargetMove;
    private bool raidTargetOpen;
    private object? pendingRaidChoice;
    private string pendingRaidRound = "";
    private string? raidCaptureRequest;
    private string? raidCaptureBall;
    private bool raidSkipRequested;

    private void ApplyRaid(RaidRoomDto next)
    {
        if (raidRoom?.Id == next.Id && next.Revision < raidRoom.Revision) return;
        if (raidRoom?.RoundId != next.RoundId || raidRoom?.Id != next.Id)
        {
            pendingRaidChoice = null; raidTargetMove = null; raidModifier = "";
        }
        if (raidRoom?.Id != next.Id) { raidCaptureRequest = null; raidCaptureBall = null; }
        var terminal = raidRoom?.Status is "lobby" or "active" && next.Status is not ("lobby" or "active");
        var awarded = raidRoom?.Receipt is null && next.Receipt is not null;
        if (trainer is not null && (raidRoom?.Id == next.Id || trainer.ActiveRaidId == next.Id))
            QueueRecentActivity(trainer.User.Id, Services.RecentActivity.RaidEntries(next, DateTimeOffset.UtcNow));
        raidRoom = next; raidObservedAt = DateTimeOffset.UtcNow; nextRaidPollAt = raidObservedAt.AddSeconds(2);
        if (trainer is not null)
        {
            trainer.RaidLocked = next.Status is "lobby" or "active";
            trainer.ActiveRaidId = trainer.RaidLocked || next.CaptureState == "pending" ? next.Id : null;
        }
        if (terminal || awarded) refreshState.Trainer();
    }
    private async Task RefreshRaids()
    {
        raidBoard = await InSession(plugin.Api.GetRaids(disposeCancellation.Token));
        var id = raidRoom?.Id ?? raidBoard.ActiveRoomId;
        if (id is not null) ApplyRaid(await InSession(plugin.Api.GetRaid(id, disposeCancellation.Token)));
        nextRaidPollAt = DateTimeOffset.UtcNow.AddSeconds(5);
        SetStatus(raidRoom is null ? "Raid dens ready." : "Raid loaded.");
    }
    private void BackgroundRaids()
    {
        var id = raidRoom?.Id ?? trainer?.ActiveRaidId;
        RunBackground(async token =>
        {
            var board = id is null ? await InSession(plugin.Api.GetRaids(token)) : null;
            var roomId = id ?? board?.ActiveRoomId;
            RaidRoomDto? room;
            try { room = roomId is null ? null : await InSession(plugin.Api.GetRaid(roomId, token)); }
            catch (Services.ApiException exception) when (exception.StatusCode == 404)
            {
                return () => { raidRoom = null; raidBoard = null; if (trainer is not null) { trainer.ActiveRaidId = null; trainer.RaidLocked = false; } refreshState.Trainer(); nextRaidPollAt = DateTimeOffset.UtcNow.AddSeconds(5); };
            }
            return () =>
            {
                if (board is not null) raidBoard = board;
                if (room is not null) ApplyRaid(room);
                nextRaidPollAt = DateTimeOffset.UtcNow.AddSeconds(room?.Status is "lobby" or "active" || room?.RewardsPending == true ? 2 : 10);
                if (status == "Loading raid dens...") SetStatus(raidRoom is null ? "Raid dens ready." : "Raid loaded.");
            };
        }, () => raidRefreshRequested = true);
    }
    private async Task RaidAction(string action, object body, bool lobby = false)
    {
        var result = await InSession(plugin.Api.RaidAction(lobby ? null : raidRoom!.Id, action, body, disposeCancellation.Token));
        if (action == "leave") { raidRoom = null; raidBoard = null; }
        else ApplyRaid(result);
        trainer = await InSession(plugin.Api.GetMe(disposeCancellation.Token));
        SetStatus(action switch { "choose" => "Raid action saved.", "catch" => "Caught! Your Pokémon is saved.", "skip" => "Capture skipped.", "leave" => "You left the raid.", _ => "Raid updated." });
    }
    private void DrawRaids()
    {
        if (trainer is null) return;
        if (raidRoom is { } room)
        {
            if (room.Status == "lobby") DrawRaidLobby(room);
            else { DrawRaidWithLog(room); return; }
        }
        else
        {
            DrawSectionHeader("COOPERATIVE DENS", "Four allies, one boss. Bring one Pokémon from your party or PC. Empty slots get NPC allies.");
            if (raidBoard is null)
            {
                if (!busy) Run(RefreshRaids, "Loading raid dens...");
                ImGui.TextUnformatted("Loading the den board..."); return;
            }
            if (raidBoard.ActiveRoomId is { } saved)
            {
                if (DrawAccentButton("Resume saved raid", new Vector2(-1, 34), PokemonBlue)) Run(async () => ApplyRaid(await InSession(plugin.Api.GetRaid(saved))), "Resuming raid...");
                return;
            }
            ImGui.TextColored(CompanionMuted, $"{raidBoard.Progress.Wins:N0} victories / Best: {raidBoard.Progress.BestStars} stars / Dens rotate every 6 hours (UTC)");
            var hasPokemon = DrawRaidPokemonChoice();
            ImGui.Checkbox("Private lobby", ref raidPrivate);
            ImGui.SameLine(); ImGui.SetNextItemWidth(125 * CompanionScale);
            ImGui.InputTextWithHint("##RaidCode", "Lobby code", ref raidJoinCode, 8);
            ImGui.SameLine(); ImGui.BeginDisabled(CollectionLocked || !hasPokemon || raidJoinCode.Trim().Length != 8);
            if (DrawAccentSmallButton("Join code", PokemonBlue)) Run(() => RaidAction("join", new { code = raidJoinCode.Trim().ToUpperInvariant(), pokemonId = raidPokemonId }, true), "Joining raid...");
            ImGui.EndDisabled();
            ImGui.TextWrapped("No entry cost or loss penalty. Loot and one catch per den per rotation. Replays help friends without repeat rewards.");
            DrawPager("RaidDens", ref raidDenPage, raidBoard.Dens.Count, 4);
            if (ImGui.BeginTable("RaidDenGrid", ImGui.GetContentRegionAvail().X > 480 * CompanionScale ? 2 : 1, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var den in raidBoard.Dens.Skip(raidDenPage * 4).Take(4)) { ImGui.TableNextColumn(); DrawRaidDen(den, hasPokemon); }
                ImGui.EndTable();
            }
        }
        DrawRaidRoster(); DrawRaidPopups();
    }

    private void DrawRaidWithLog(RaidRoomDto room)
    {
        var available = ImGui.GetContentRegionAvail(); var origin = ImGui.GetCursorPos();
        var layout = BattleLayout.Console(available.X, available.Y, CompanionScale, false);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("RaidBattleMain", new Vector2(layout.MainWidth, available.Y), false);
        if (room.Status == "active") DrawRaidBattle(room); else DrawRaidResult(room);
        DrawRaidPopups();
        ImGui.EndChild();
        ImGui.SetCursorPos(origin + new Vector2(layout.MainWidth + layout.Gap, 0));
        ImGui.BeginChild("RaidHistoryRail", new Vector2(layout.LogWidth, available.Y), false, FixedBattlePanel);
        DrawPermanentBattleLog("raid:" + room.Id, room.Snapshot?.Log ?? [], available.Y);
        ImGui.EndChild(); ImGui.PopStyleVar();
    }
    private void DrawRaidDen(RaidDenDto den, bool hasPokemon)
    {
        ImGui.PushID(den.Key);
        ImGui.BeginChild("Den", new Vector2(0, 232 * CompanionScale), true, ImGuiWindowFlags.NoScrollbar);
        ImGui.TextColored(PokemonYellow, $"{new string('*', den.Stars)} / Battle Lv. {den.Level}");
        DrawPokemonIcon(den.Species, false, new Vector2(60 * CompanionScale)); ImGui.SameLine(); ImGui.BeginGroup();
        ImGui.TextUnformatted(den.Species); ImGui.TextColored(CompanionMuted, den.Theme);
        DrawStarterTypeBadges(den.Stars >= 3 ? [den.TeraType] : Services.SpeciesDisplayCatalog.Get(den.Species)?.Types ?? [], 65 * CompanionScale, CompanionScale);
        ImGui.EndGroup();
        ImGui.TextWrapped(den.RewardClaimed ? "Reward collected. You can still help other trainers." : $"¥ {den.Currency:N0} / {den.Experience:N0} EXP / Medicine + ball / Guaranteed catch");
        if (den.LockReason.Length > 0) ImGui.TextWrapped(den.LockReason);
        var rooms = raidBoard!.Rooms.Where(entry => entry.DenKey == den.Key).ToList();
        ImGui.BeginDisabled(CollectionLocked || !hasPokemon || den.LockReason.Length > 0);
        if (DrawAccentButton("Host raid", new Vector2(-1, 28 * CompanionScale), PokemonBlue)) Run(() => RaidAction("create", new { denKey = den.Key, pokemonId = raidPokemonId, @private = raidPrivate }, true), "Opening raid lobby...");
        if (rooms.Count > 0 && ImGui.BeginCombo("Join public lobby", $"{rooms.Count} waiting"))
        {
            foreach (var listing in rooms)
                if (ImGui.Selectable($"{listing.HostName} / {listing.Players}/4##{listing.Id}")) Run(() => RaidAction("join", new { roomId = listing.Id, pokemonId = raidPokemonId }, true), "Joining raid...");
            ImGui.EndCombo();
        }
        ImGui.EndDisabled(); ImGui.EndChild(); ImGui.PopID();
    }
    private bool DrawRaidPokemonChoice()
    {
        var pokemon = OnlineOwnedPokemon().FirstOrDefault(entry => entry.Id == raidPokemonId);
        if (pokemon is null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, PokemonOrange);
            ImGui.TextWrapped("Choose a Pokémon to host or join a den.");
            ImGui.PopStyleColor();
        }
        if (DrawAccentButton(pokemon is null ? "Choose Pokémon from party or PC" : $"{pokemon.Nickname} / Lv. {pokemon.Level} / Change Pokémon", new Vector2(-1, 32 * CompanionScale), pokemon is null ? PokemonYellow : PokemonBlue)) raidRosterOpen = true;
        return pokemon is not null;
    }
    private void DrawRaidRoster()
    {
        if (raidRosterOpen) { ImGui.OpenPopup("Raid Pokémon"); raidRosterOpen = false; }
        ImGui.SetNextWindowSize(new Vector2(620, 520) * CompanionScale, ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal("Raid Pokémon", ref open)) return;
        ImGui.TextWrapped("One Pokémon per trainer. Builds are frozen for the raid. Both high and low levels sync to the den's advertised level.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##RaidRosterSearch", "Search party and PC...", ref raidRosterSearch, 80)) raidRosterPage = 0;
        var candidates = OnlineOwnedPokemon().Where(pokemon => pokemon.Nickname.Contains(raidRosterSearch, StringComparison.OrdinalIgnoreCase) || pokemon.Species.Contains(raidRosterSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        DrawPager("RaidRoster", ref raidRosterPage, candidates.Count, 8);
        ImGui.BeginChild("RaidRosterList", new Vector2(0, -40 * CompanionScale), false);
        foreach (var pokemon in candidates.Skip(raidRosterPage * 8).Take(8))
        {
            ImGui.BeginDisabled(busy);
            if (ImGui.Selectable($"{pokemon.Nickname} / {pokemon.DisplaySpecies} / Lv. {pokemon.Level} / {(pokemon.PartySlot is null ? $"PC Box {pokemon.BoxNumber}" : "Party")}##{pokemon.Id}", raidPokemonId == pokemon.Id, ImGuiSelectableFlags.None, new Vector2(0, 32 * CompanionScale)))
            {
                raidPokemonId = pokemon.Id; ImGui.CloseCurrentPopup();
                SetStatus($"{pokemon.Nickname} selected for raids.");
                if (raidRoom?.Status == "lobby") Run(() => RaidAction("ready", new { ready = false, pokemonId = raidPokemonId }), "Changing raid Pokémon...");
            }
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
        if (DrawAccentButton("Close", new Vector2(-1, 28 * CompanionScale), CompanionAccent)) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
    private void DrawRaidLobby(RaidRoomDto room)
    {
        DrawSectionHeader($"{room.Den.Species.ToUpperInvariant()} DEN / {room.Den.Stars} STARS", $"Battle Lv. {room.Den.Level} / {(room.Private ? "Private" : "Public")} lobby / {room.Players.Count}/4 trainers");
        if (DrawAccentSmallButton($"Copy code: {room.Code}", PokemonBlue)) ImGui.SetClipboardText(room.Code);
        ImGui.TextWrapped("One shared boss. No bag items or switching. Three cheers each. The team shares one Mega Evolution and one Terastallization. NPCs fill empty slots at start.");
        for (var slot = 0; slot < 4; slot++)
        {
            var player = room.Players.FirstOrDefault(entry => entry.Slot == slot);
            ImGui.PushID(slot);
            if (player is null) ImGui.TextColored(CompanionMuted, $"Slot {slot + 1}: NPC support (or invite a friend)");
            else
            {
                DrawPokemonIcon(player.Species, player.Shiny, new Vector2(42 * CompanionScale)); ImGui.SameLine();
                ImGui.TextColored(player.Ready ? PokemonGreen : CompanionMuted, $"{player.Name}{(player.Host ? " (host)" : "")} / {player.Species} / {(player.Ready ? "Ready" : "Not ready")}");
            }
            ImGui.PopID();
        }
        raidPokemonId = room.SelectedPokemon.Id; DrawRaidPokemonChoice();
        ImGui.BeginDisabled(busy);
        if (DrawAccentButton(room.Ready ? "Cancel ready" : "Ready", new Vector2(-1, 34 * CompanionScale), room.Ready ? PokemonOrange : PokemonGreen)) Run(() => RaidAction("ready", new { ready = !room.Ready }), "Updating readiness...");
        if (room.Host)
        {
            ImGui.BeginDisabled(!room.Players.All(player => player.Ready));
            if (DrawAccentButton("Start raid with NPC fill", new Vector2(-1, 34 * CompanionScale), PokemonRed)) Run(() => RaidAction("start", new { }), "Entering the den...");
            ImGui.EndDisabled();
        }
        if (DrawAccentSmallButton("Leave lobby", PokemonOrange)) ImGui.OpenPopup("Leave raid?");
        ImGui.EndDisabled();
    }
    private void DrawRaidBattle(RaidRoomDto room)
    {
        if (room.Snapshot is not { } battleState) return;
        var scale = CompanionScale;
        var compact = ImGui.GetContentRegionAvail().Y < 520 * scale;
        var actorHeight = (compact ? 96 : 120) * scale;
        var seconds = Math.Max(0, room.SecondsRemaining - (DateTimeOffset.UtcNow - raidObservedAt).TotalSeconds);
        DrawClippedText($"{room.Den.Stars}-STAR DEN / Round {battleState.Turn}/{battleState.RoundLimit} / Lives: {battleState.LivesRemaining}{(battleState.ShieldRounds > 0 ? $" / Shield: {battleState.ShieldRounds}" : "")}", ImGui.GetContentRegionAvail().X, CompanionAccent);
        DrawClippedText(room.Paused ? "Battle service unavailable. Your raid is saved; waiting to reconnect." : $"{Math.Ceiling(seconds):N0}s / {(room.Submitted ? "Action saved. Waiting for teammates." : "Choose a move or cheer. Missing choices use NPC actions.")}", ImGui.GetContentRegionAvail().X, room.Paused ? PokemonOrange : CompanionMuted);
        DrawRaidActor(battleState.Boss, "DEN BOSS", actorHeight);
        if (ImGui.BeginTable("RaidAllies", 4, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var ally in battleState.Allies) { ImGui.TableNextColumn(); DrawRaidActor(ally, ally.Slot == room.OwnSlot ? "YOU" : ally.Npc ? "NPC" : ally.Submitted ? "READY" : "CHOOSING", actorHeight); }
            ImGui.EndTable();
        }
        var own = battleState.Allies.FirstOrDefault(entry => entry.Slot == room.OwnSlot);
        if (own is null) return;
        ImGui.BeginDisabled(busy || !room.CanAct);
        if (own.CanMega || own.CanTera)
        {
            if (own.CanMega && DrawAccentSmallButton(raidModifier == "mega" ? "Mega selected" : "Mega Evolve", PokemonOrange)) raidModifier = raidModifier == "mega" ? "" : "mega";
            if (own.CanTera) { if (own.CanMega) ImGui.SameLine(); if (DrawAccentSmallButton(raidModifier == "terastallize" ? "Tera selected" : "Terastallize", PokemonBlue)) raidModifier = raidModifier == "terastallize" ? "" : "terastallize"; }
        }
        if (ImGui.BeginTable("RaidMoves", 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var move in own.Moves)
            {
                ImGui.TableNextColumn(); ImGui.BeginDisabled(move.Disabled || move.Pp == 0 || move.LegalTargets.Count == 0);
                var label = $"{BattleLayout.MoveLabel(move.Move).ButtonText}\n{move.Type} / {(move.Pp.HasValue ? $"PP {move.Pp}/{move.Maxpp}" : "Continue move")}##RaidMove{move.Index}";
                if (DrawAccentButton(label, new Vector2(-1, (compact ? 38 : 44) * scale), PokemonBlue))
                {
                    if (move.LegalTargets.Count == 1) SubmitRaidMove(move, move.LegalTargets[0]);
                    else { raidTargetMove = move; raidTargetOpen = true; }
                }
                ImGui.EndDisabled();
                DrawBattleMoveTooltip(move.Move);
            }
            ImGui.EndTable();
        }
        ImGui.BeginDisabled(!own.CanCheer);
        if (ImGui.BeginTable("RaidCheers", 3, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var (kind, label, color) in new[] { ("heal", "Heal + cure", PokemonGreen), ("attack", "Attack up", PokemonRed), ("defense", "Defense up", PokemonBlue) })
            {
                ImGui.TableNextColumn();
                if (DrawAccentButton($"{label} ({own.Cheers})", new Vector2(-1, 28 * scale), color)) SubmitRaidChoice(new { cheer = kind });
            }
            ImGui.EndTable();
        }
        ImGui.EndDisabled(); ImGui.EndDisabled();
        if (DrawAccentSmallButton("Leave raid", PokemonOrange)) ImGui.OpenPopup("Leave raid?");
        if (pendingRaidChoice is not null && room.CanAct && !busy)
        {
            ImGui.SameLine(); if (DrawAccentSmallButton("Retry saved action", PokemonBlue)) Run(() => RaidAction("choose", pendingRaidChoice), "Retrying raid action...");
        }
    }
    private void DrawRaidActor(RaidActorDto actor, string heading, float height)
    {
        var scale = CompanionScale; var min = ImGui.GetCursorScreenPos(); var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        ImGui.PushID(heading + actor.Slot + actor.Species);
        ImGui.InvisibleButton("RaidActor", new Vector2(width, height)); var hovered = ImGui.IsItemHovered(); var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList(); var max = min + new Vector2(width, height);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(CompanionSurface), 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(heading == "YOU" ? PokemonBlue : CompanionMuted with { W = 0.3f }), 4 * scale);
        draw.PushClipRect(min + new Vector2(4 * scale), max - new Vector2(4 * scale), true);
        ImGui.SetCursorScreenPos(min + new Vector2(8, 5) * scale);
        DrawClippedText($"{heading} / Lv. {actor.Level}", width - 16 * scale, heading == "YOU" ? PokemonBlue : CompanionMuted);
        ImGui.SetCursorScreenPos(min + new Vector2(8, 23) * scale); DrawClippedText(actor.Species, width - 16 * scale, Vector4.One);
        ImGui.SetCursorScreenPos(min + new Vector2(8, 41) * scale); DrawPokemonIcon(actor.Species, actor.Shiny, new Vector2(Math.Min(34 * scale, height - 69 * scale)));
        ImGui.SetCursorScreenPos(min + new Vector2(46, 45) * scale);
        DrawStarterTypeBadges(actor.Types.ToArray(), Math.Max(1, Math.Min(64 * scale, (width - 60 * scale) / Math.Max(1, actor.Types.Count))), scale);
        ImGui.SetCursorScreenPos(new Vector2(min.X + 8 * scale, max.Y - 27 * scale));
        DrawClippedText($"{actor.Hp:N0} / {actor.MaxHp:N0} {actor.Status}", width - 16 * scale, CompanionMuted);
        ImGui.SetCursorScreenPos(new Vector2(min.X + 8 * scale, max.Y - 9 * scale));
        DrawProgressBar(actor.Hp / (float)Math.Max(1, actor.MaxHp), new Vector2(width - 16 * scale, 5 * scale), "", HpBarRed);
        draw.PopClipRect(); ImGui.SetCursorScreenPos(after); ImGui.PopID();
        if (hovered) ImGui.SetTooltip($"{actor.TrainerName}\n{actor.Species} / {string.Join(" / ", actor.Types)}\n{actor.Hp:N0}/{actor.MaxHp:N0} HP / {actor.Status}");
    }
    private void SubmitRaidMove(RaidMoveDto move, int target)
    {
        if (raidModifier.Length > 0) SubmitRaidChoice(new { move = move.Index, target, modifier = raidModifier });
        else SubmitRaidChoice(new { move = move.Index, target });
    }
    private void SubmitRaidChoice(object choice)
    {
        if (raidRoom is null) return;
        pendingRaidRound = raidRoom.RoundId;
        pendingRaidChoice = new { roundId = pendingRaidRound, requestId = Guid.NewGuid().ToString(), choice };
        Run(() => RaidAction("choose", pendingRaidChoice), "Sending raid action...");
    }
    private void DrawRaidResult(RaidRoomDto room)
    {
        DrawSectionHeader(room.Status == "won" ? "DEN CLEARED" : room.Status == "lost" ? "DEN DEFEAT" : "RAID ENDED", $"{room.Den.Species} / {room.Den.Stars} stars");
        DrawPokemonIcon(room.Den.Species, room.CapturePokemon?.Shiny == true, new Vector2(110 * CompanionScale));
        if (room.EndReason is not null) ImGui.TextWrapped(room.EndReason);
        if (room.RewardsPending) ImGui.TextWrapped("Saving individual rewards...");
        else if (room.Receipt is { } receipt)
        {
            ImGui.TextColored(PokemonGreen, $"¥ {receipt.Currency:N0} / {receipt.Experience:N0} EXP for your participating Pokémon");
            foreach (var item in receipt.Items) ImGui.TextUnformatted($"{itemCatalog.FirstOrDefault(entry => entry.Id == item.ItemId)?.Name ?? item.ItemId} x{item.Quantity}");
        }
        else ImGui.TextWrapped(room.RepeatReward ? "You already claimed this den's rewards this rotation. Thanks for helping!" : "No ¥ was lost. Train your team or try a different strategy.");
        if (room.CaptureState == "pending")
        {
            ImGui.TextColored(PokemonYellow, $"{(room.CapturePokemon?.Shiny == true ? "SHINY / " : "")}Guaranteed catch / Spend one owned ball");
            ImGui.TextWrapped("You can open PC Storage to free space. Catch or skip within 30 minutes. The Pokémon and shiny roll are already saved.");
            ImGui.BeginDisabled(busy);
            if (raidCaptureRequest is not null)
            {
                if (DrawAccentButton("Retry capture decision", new Vector2(-1, 32), PokemonGreen)) Run(() => RaidAction(raidSkipRequested ? "skip" : "catch", raidSkipRequested ? new { requestId = raidCaptureRequest } : (object)new { requestId = raidCaptureRequest, ballId = raidCaptureBall }), "Saving capture...");
            }
            else
            {
                foreach (var ball in itemCatalog.Where(item => item.Category == "ball" && trainer!.Inventory.Any(entry => entry.ItemId == item.Id && entry.Quantity > 0)))
                    if (DrawAccentButton($"Catch with {ball.Name} ({trainer!.Inventory.First(entry => entry.ItemId == ball.Id).Quantity})##RaidBall{ball.Id}", new Vector2(-1, 28), PokemonGreen))
                    {
                        raidCaptureRequest = Guid.NewGuid().ToString(); raidCaptureBall = ball.Id; raidSkipRequested = false;
                        Run(async () =>
                        {
                            try { await InSession(RaidAction("catch", new { requestId = raidCaptureRequest, ballId = raidCaptureBall })); }
                            catch (Services.ApiException exception) when (exception.StatusCode < 500) { raidCaptureRequest = null; throw; }
                        }, "Catching raid Pokémon...");
                    }
                if (DrawAccentSmallButton("Skip catch", PokemonOrange)) ImGui.OpenPopup("Skip raid catch?");
            }
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.TextColored(room.CaptureState == "caught" ? PokemonGreen : CompanionMuted, room.CaptureState switch { "caught" => "Caught! Saved to your collection.", "skipped" => "Catch skipped.", "expired" => "Catch opportunity expired.", _ => "" });
            if (DrawAccentButton("Back to dens", new Vector2(-1, 32), PokemonBlue)) { raidRoom = null; raidBoard = null; raidRefreshRequested = true; }
        }
    }
    private void DrawRaidPopups()
    {
        if (BeginConfirmationModal("Leave raid?"))
        {
            ImGui.TextWrapped("Leaving an active raid hands your slot to an NPC and forfeits your rewards. Disconnecting alone keeps your slot and eligibility.");
            if (DrawConfirmationActions("Leave", PokemonOrange, busy, "Stay")) { ImGui.CloseCurrentPopup(); Run(() => RaidAction("leave", new { confirmed = true }), "Leaving raid..."); }
            EndConfirmationModal();
        }
        if (BeginConfirmationModal("Skip raid catch?"))
        {
            ImGui.TextWrapped("Give up this den's catch? You keep your other rewards.");
            if (DrawConfirmationActions("Skip catch", PokemonOrange, busy))
            {
                raidCaptureRequest = Guid.NewGuid().ToString(); raidSkipRequested = true; ImGui.CloseCurrentPopup();
                Run(() => RaidAction("skip", new { requestId = raidCaptureRequest }), "Skipping catch...");
            }
            EndConfirmationModal();
        }
        if (raidTargetOpen) { ImGui.OpenPopup("Raid move target"); raidTargetOpen = false; }
        if (ImGui.BeginPopup("Raid move target"))
        {
            if (raidTargetMove is { } move && raidRoom?.Snapshot is { } current)
            foreach (var target in move.LegalTargets)
            {
                var label = target == 1 ? current.Boss.Species : target < 0 ? $"Ally {-target}: {current.Allies[-target - 1].Species}" : "Field";
                if (ImGui.Selectable(label)) { ImGui.CloseCurrentPopup(); SubmitRaidMove(move, target); }
            }
            ImGui.EndPopup();
        }
    }
}
