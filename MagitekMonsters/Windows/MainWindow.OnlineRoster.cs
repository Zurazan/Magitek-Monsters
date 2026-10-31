using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private bool requestOnlineRosterPicker;
    private bool onlineRosterFriendly;
    private PvpFormatDto? onlineRosterFormat;
    private int onlineRosterSource;
    private int onlineRosterBox;
    private int onlineRosterPage;
    private string onlineRosterSearch = string.Empty;

    private void OpenOnlineRosterPicker(PvpFormatDto format)
    {
        onlineRosterFriendly = onlineView == 2;
        onlineRosterFormat = format;
        onlineRosterSource = 0; onlineRosterBox = 0; onlineRosterPage = 0; onlineRosterSearch = string.Empty;
        requestOnlineRosterPicker = true;
    }

    private void DrawOnlineRosterPicker()
    {
        if (requestOnlineRosterPicker) { ImGui.OpenPopup("Choose battle Pokémon"); requestOnlineRosterPicker = false; }
        if (!ImGui.IsPopupOpen("Choose battle Pokémon") || onlineRosterFormat is not { } format || trainer is null) return;
        var scale = CompanionScale; var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(780 * scale, work.X * 0.9f), Math.Min(640 * scale, work.Y * 0.9f)), ImGuiCond.Appearing);
        var open = true;
        if (!BeginCenteredModal("Choose battle Pokémon", ref open, ImGuiWindowFlags.None)) return;
        var owned = OnlineOwnedPokemon(); var ids = onlineRosterFriendly ? friendlyTeamIds : pvpTeamIds;
        PvpRoster.Synchronize(ids, owned, format.MaxTeamSize);
        ImGui.TextColored(CompanionAccent, $"REGISTERED TEAM / {ids.Count} of {format.MaxTeamSize}");
        ImGui.TextWrapped("Click a registered name to remove it, then choose a replacement below. Your PvE party and PC positions stay unchanged.");
        ImGui.BeginDisabled(CollectionLocked);
        if (ids.Count > 0 && ImGui.BeginTable("OnlineRegisteredNames", ImGui.GetContentRegionAvail().X >= 540 * scale ? 3 : 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var pokemon in PvpRoster.Selected(owned, ids))
            {
                ImGui.TableNextColumn();
                if (DrawAccentButton($"Remove {pokemon.Nickname}##RemoveRoster{pokemon.Id}", new Vector2(-1, 26 * scale), PokemonOrange)) { ids.Remove(pokemon.Id); pvpTeamError = string.Empty; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{pokemon.DisplaySpecies} / {PvpRoster.Location(pokemon)}");
            }
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.SetNextItemWidth(Math.Min(240 * scale, Math.Max(1, ImGui.GetContentRegionAvail().X * 0.5f)));
        string[] sources = ["Party + PC Storage", "Party only", "PC Storage only"];
        onlineRosterSource = Math.Clamp(onlineRosterSource, 0, sources.Length - 1);
        if (ImGui.BeginCombo("Source", sources[onlineRosterSource]))
        {
            for (var index = 0; index < sources.Length; index++)
                if (ImGui.Selectable(sources[index], onlineRosterSource == index)) { onlineRosterSource = index; onlineRosterBox = 0; onlineRosterPage = 0; }
            ImGui.EndCombo();
        }
        if (onlineRosterSource != 1)
        {
            ImGui.SetNextItemWidth(Math.Min(240 * scale, Math.Max(1, ImGui.GetContentRegionAvail().X * 0.5f)));
            if (ImGui.BeginCombo("Box", onlineRosterBox == 0 ? "All boxes" : BoxLabel(onlineRosterBox)))
            {
                if (ImGui.Selectable("All boxes", onlineRosterBox == 0)) { onlineRosterBox = 0; onlineRosterPage = 0; }
                foreach (var box in owned.Where(pokemon => pokemon.PartySlot is null).Select(pokemon => pokemon.BoxNumber).Distinct().Order())
                    if (ImGui.Selectable(BoxLabel(box), onlineRosterBox == box)) { onlineRosterBox = box; onlineRosterPage = 0; }
                ImGui.EndCombo();
            }
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##OnlineRosterSearch", "Species, nickname, type, move, ability or held item...", ref onlineRosterSearch, 100)) onlineRosterPage = 0;
        var candidates = PvpRoster.Filter(owned, onlineRosterSource, onlineRosterBox, onlineRosterSearch);
        var footer = ImGui.GetFrameHeight() + ImGui.GetTextLineHeightWithSpacing() + 12 * scale;
        ImGui.BeginChild("OnlineRosterCandidates", new Vector2(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - footer)), false);
        DrawPager("OnlineRoster", ref onlineRosterPage, candidates.Count, 6);
        ImGui.BeginDisabled(CollectionLocked);
        var columns = ImGui.GetContentRegionAvail().X >= 660 * scale ? 3 : ImGui.GetContentRegionAvail().X >= 300 * scale ? 2 : 1;
        if (ImGui.BeginTable("OnlineRosterGrid", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var pokemon in candidates.Skip(onlineRosterPage * 6).Take(6))
            {
                ImGui.TableNextColumn();
                var selected = ids.Contains(pokemon.Id);
                if (DrawOnlinePartyCard(pokemon, format, selected, selected || ids.Count < format.MaxTeamSize))
                { if (selected) ids.Remove(pokemon.Id); else ids.Add(pokemon.Id); pvpTeamError = string.Empty; }
            }
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        if (candidates.Count == 0) ImGui.TextWrapped("No owned Pokémon match these filters. Fusion partners are unavailable until unfused.");
        ImGui.EndChild();
        DrawClippedText($"{candidates.Count:N0} available / {ids.Count}/{format.MaxTeamSize} registered / Battle Lv. {PvpPresentation.BattleLevel(format)}", ImGui.GetContentRegionAvail().X, CompanionMuted);
        if (DrawAccentButton("Done", new Vector2(-1, 30 * scale), CompanionAccent)) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
