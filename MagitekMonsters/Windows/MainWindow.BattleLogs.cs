using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly BattleLogArchive battleLogArchive;
    private string? battleLogUser;
    private BattleSnapshotDto? loggedPve;
    private BattleSnapshotDto? loggedPvp;
    private RaidRoomDto? loggedRaid;
    private DateTimeOffset battleLogCopiedUntil;
    private string? copiedBattleLogId;

    private void ObserveBattleLogs()
    {
        battleLogArchive.Maintain();
        var user = trainer?.User.Id;
        if (battleLogUser != user)
        {
            battleLogArchive.Reset();
            battleLogUser = user;
            loggedPve = loggedPvp = null;
            loggedRaid = null;
        }
        if (user is null) return;

        if (battle is { } pve && !ReferenceEquals(loggedPve, pve))
        {
            ObserveBattleLog("pve", pve);
            loggedPve = pve;
        }
        if (pvpStatus?.Battle is { } pvp && !ReferenceEquals(loggedPvp, pvp))
        {
            ObserveBattleLog("pvp", pvp);
            loggedPvp = pvp;
        }
        if (raidRoom is { Snapshot: { } snapshot } raid && !ReferenceEquals(loggedRaid, raid))
        {
            var allies = string.Join(", ", snapshot.Allies.Select(RaidCombatant));
            var participants = $"Allies: {allies}{Environment.NewLine}Boss: {RaidCombatant(snapshot.Boss)}";
            battleLogArchive.Observe("raid", "raid:" + raid.Id, $"Cooperative den / {raid.Den.Species}", raid.Status,
                snapshot.Turn, raid.Revision, snapshot.Log, participants);
            loggedRaid = raid;
        }
    }

    private void ObserveBattleLog(string source, BattleSnapshotDto snapshot)
    {
        var player = BattleLogSide(snapshot.PlayerActives, snapshot.PlayerActive);
        var opponent = BattleLogSide(snapshot.OpponentActives, snapshot.OpponentActive);
        var participants = $"Your side: {player}{Environment.NewLine}Opponent: {opponent}";
        var category = snapshot.Kind == "wild" ? "Wild encounter" : source == "pvp" ? snapshot.FormatName ?? "Online battle" : "Trainer battle";
        battleLogArchive.Observe(source, snapshot.Id, $"{category} / {snapshot.OpponentName}", snapshot.Status,
            snapshot.Turn, snapshot.Revision, snapshot.Log, participants);
    }

    private static string BattleLogSide(IReadOnlyList<BattleCombatantDto?> actives, BattleCombatantDto? fallback)
    {
        var names = actives.OfType<BattleCombatantDto>().Select(BattleLogCombatant).ToArray();
        return names.Length > 0 ? string.Join(", ", names) : fallback is not null ? BattleLogCombatant(fallback) : "Waiting for preview";
    }

    private static string BattleLogCombatant(BattleCombatantDto pokemon) => $"{pokemon.Species} Lv. {pokemon.Level} ({pokemon.Condition})";
    private static string RaidCombatant(RaidActorDto pokemon) => $"{pokemon.Species} Lv. {pokemon.Level} ({pokemon.Hp}/{pokemon.MaxHp} HP)";

    private void DrawCopyBattleLog(string id)
    {
        var label = id == copiedBattleLogId && DateTimeOffset.UtcNow < battleLogCopiedUntil ? "Copied" : "Copy log";
        if (DrawAccentSmallButton(label + "##CopyBattleLog", PokemonBlue))
        {
            ObserveBattleLogs();
            if (battleLogArchive.GetText(id) is { } transcript)
            {
                ImGui.SetClipboardText(transcript);
                copiedBattleLogId = id;
                battleLogCopiedUntil = DateTimeOffset.UtcNow.AddSeconds(2);
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy the battle log as plain text, including earlier turns captured by this client.");
    }
}
