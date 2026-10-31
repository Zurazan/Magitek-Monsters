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
    private bool openFactory;
    private string factoryViewKey = string.Empty;
    private readonly List<string> factoryPicks = [];
    private string factorySwapOut = string.Empty;
    private string factorySwapIn = string.Empty;

    private void DrawFactory()
    {
        var run = trainer!.FrontierRun;
        if (FrontierBusy && run!.Facility != "factory")
        {
            ImGui.TextWrapped("Finish or end your saved Battle Tower run before drafting Factory rentals.");
            return;
        }
        if (!FrontierBusy)
        {
            ImGui.TextColored(PokemonBlue, "BATTLE FACTORY / RENTAL DRAFT");
            ImGui.TextWrapped("Choose three Pokémon from six prepared rentals. After every victory, keep your team or swap one member for a rental from the trainer you just defeated.");
            ImGui.TextWrapped("All battles use level 50, Species Clause and Item Clause. No bag items. Full healing between fights. Rentals never enter your collection or Pokédex.");
            ImGui.TextWrapped("BP, achievements and permanent unlocks follow the Tower's existing rules. A loss ends the streak; reconnecting keeps your saved draft, battle and swap offers.");
            ImGui.Spacing();
            if (ImGui.RadioButton("Accessible##Factory", frontierBracket == 0)) frontierBracket = 0;
            ImGui.SameLine(); ImGui.BeginDisabled(!trainer.Frontier.StandardUnlocked);
            if (ImGui.RadioButton("Standard##Factory", frontierBracket == 1)) frontierBracket = 1;
            ImGui.EndDisabled();
            ImGui.TextWrapped(frontierBracket == 0 ? "Forgiving opponents. 1-3 base BP per win; every seventh victory adds 3 BP." : "Expert opponents. 3-7 base BP per win; every seventh victory adds 8 BP.");
            if (!trainer.Frontier.RentalUnlocked) ImGui.TextDisabled("Unlock rentals by defeating all five Rookie trainers.");
            if (!trainer.Frontier.StandardUnlocked) ImGui.TextDisabled("Standard unlocks after defeating the Champion.");
            ImGui.BeginDisabled(busy || CollectionLocked || !trainer.Frontier.RentalUnlocked || frontierBracket == 1 && !trainer.Frontier.StandardUnlocked);
            if (DrawAccentButton("Begin rental draft", new Vector2(220, 36) * CompanionScale, PokemonBlue))
                Run(BeginFactoryDraft, "Preparing your six Factory rentals...");
            ImGui.EndDisabled();
            return;
        }
        var key = $"{trainer.User.Id}:{run!.Id}:{run.Revision}";
        if (factoryViewKey != key) { factoryViewKey = key; factoryPicks.Clear(); factorySwapOut = string.Empty; factorySwapIn = string.Empty; }
        if (run.Status == "active")
        {
            ImGui.TextWrapped($"Saved Factory fight / {run.Bracket} / Streak {run.Streak}");
            if (DrawAccentButton("Resume Factory fight", new Vector2(220, 34), PokemonBlue))
                Run(async () => battle = await InSession(plugin.Api.GetBattle(run.BattleId)), "Restoring your Factory fight...");
            return;
        }
        var drafting = run.Phase == "draft";
        ImGui.TextColored(PokemonBlue, drafting ? "CHOOSE YOUR THREE / FIRST PICK LEADS" : $"VICTORY SAVED / STREAK {run.Streak} / OPTIONAL SWAP");
        ImGui.TextWrapped(drafting ? "Pick in battle order. Deselect a rental to change the order. Your six offers are saved." : "Choose a rental to return, then one to borrow. Only one swap is allowed before the next fight. Everyone is fully healed.");
        var scale = CompanionScale;
        ImGui.BeginDisabled(busy);
        var footer = ImGui.GetContentRegionAvail().X < 335 * scale ? 150 * scale : 114 * scale;
        ImGui.BeginChild("FactoryOffers", new Vector2(0, Math.Max(80 * scale, ImGui.GetContentRegionAvail().Y - footer)), false);
        if (!drafting)
        {
            ImGui.TextColored(PokemonBlue, "YOUR RENTALS / CHOOSE ONE TO RETURN");
            DrawFactoryGrid(run.Team, entry => factorySwapOut == entry.Id, entry => factorySwapOut = factorySwapOut == entry.Id ? string.Empty : entry.Id, true);
            ImGui.Spacing(); ImGui.TextColored(PokemonOrange, "DEFEATED TRAINER'S RENTALS / CHOOSE ONE TO BORROW");
        }
        DrawFactoryGrid(run.Offers, entry => drafting ? factoryPicks.Contains(entry.Id) : factorySwapIn == entry.Id, entry => {
            if (drafting) { if (!factoryPicks.Remove(entry.Id) && factoryPicks.Count < 3) factoryPicks.Add(entry.Id); }
            else factorySwapIn = factorySwapIn == entry.Id ? string.Empty : entry.Id;
        }, false);
        ImGui.EndChild();
        ImGui.Separator();
        if (drafting)
        {
            var order = factoryPicks.Select((id, index) => $"{index + 1}: {run.Offers.First(entry => entry.Id == id).Pokemon.DisplaySpecies}");
            DrawClippedText(factoryPicks.Count == 0 ? "No rentals selected" : string.Join(" / ", order), ImGui.GetContentRegionAvail().X, PokemonBlue);
            ImGui.BeginDisabled(factoryPicks.Count != 3);
            if (DrawAccentButton($"Confirm team ({factoryPicks.Count}/3)", new Vector2(215, 34) * scale, PokemonGreen))
            {
                var picks = factoryPicks.ToArray(); Run(() => ConfirmFactory(picks), "Saving your Factory team...");
            }
            ImGui.EndDisabled();
        }
        else
        {
            var error = FactoryPresentation.SwapError(run, factorySwapOut, factorySwapIn);
            DrawClippedText(error.Length > 0 ? error : "Swap ready. The new rental takes the returned rental's slot.", ImGui.GetContentRegionAvail().X, error.Length > 0 ? CompanionMuted : PokemonGreen);
            ImGui.BeginDisabled(error.Length > 0);
            if (DrawAccentButton("Swap and continue", new Vector2(190, 34) * scale, PokemonGreen))
            {
                var outgoing = factorySwapOut; var incoming = factorySwapIn; Run(() => ContinueFactory(outgoing, incoming), "Saving your rental swap...");
            }
            ImGui.EndDisabled(); if (ImGui.GetContentRegionAvail().X >= 335 * scale) ImGui.SameLine();
            if (DrawAccentButton("Keep team", new Vector2(125, 34) * scale, PokemonBlue)) Run(ContinueFrontier, "Starting the next Factory fight...");
        }
        if (ImGui.GetContentRegionAvail().X >= (drafting ? 335 : 445) * scale) ImGui.SameLine();
        if (DrawAccentButton("End run", new Vector2(100, 34) * scale, PokemonOrange)) ImGui.OpenPopup("End Frontier run?");
        ImGui.EndDisabled();
        DrawFrontierAbandonPopup();
    }

    private void DrawFactoryGrid(List<FrontierMemberDto> members, Func<FrontierMemberDto, bool> selected, Action<FrontierMemberDto> choose, bool current)
    {
        if (!ImGui.BeginTable(current ? "FactoryOwned" : "FactoryPool", FactoryPresentation.Columns(ImGui.GetContentRegionAvail().X, CompanionScale), ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var member in members)
        {
            ImGui.TableNextColumn(); ImGui.PushID(member.Id);
            var slot = factoryPicks.IndexOf(member.Id); var caption = current ? "RETURN" : slot >= 0 ? slot == 0 ? "PICK 1 / LEAD" : $"PICK {slot + 1}" : "BORROW";
            if (DrawFactoryCard(member.Pokemon, selected(member), caption)) choose(member);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }
    private bool DrawFactoryCard(PokemonDto pokemon, bool selected, string caption)
    {
        var scale = CompanionScale; var line = ImGui.GetTextLineHeight();
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 8 * line + 38 * scale);
        var clicked = ImGui.InvisibleButton("Rental", size); var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax(); var after = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.20f, 0.28f, 1) : CompanionSurface), 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? PokemonBlue : new Vector4(0.25f, 0.27f, 0.31f, 1)), 4 * scale, ImDrawFlags.None, selected ? 2 * scale : scale);
        draw.PushClipRect(min + new Vector2(5 * scale), max - new Vector2(5 * scale), true);
        ImGui.SetCursorScreenPos(min + new Vector2(9, 10) * scale); DrawPokemonIcon(pokemon.Appearance, pokemon.Shiny, new Vector2(46 * scale));
        var width = Math.Max(1, size.X - 18 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(62 * scale, 9 * scale)); DrawClippedText(pokemon.DisplaySpecies, width - 53 * scale, selected ? PokemonBlue : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SetCursorScreenPos(min + new Vector2(62 * scale, 11 * scale + line));
        DrawStarterTypeBadges(SpeciesDisplayCatalog.Get(pokemon.Appearance)?.Types ?? [], 55 * scale, scale);
        var y = Math.Max(60 * scale, 2 * line + 18 * scale);
        var item = itemCatalog.FirstOrDefault(entry => entry.Id == pokemon.HeldItem)?.Name ?? pokemon.HeldItem;
        foreach (var text in new[] { $"{pokemon.Nature} / {pokemon.Ability}", $"Item: {(item.Length == 0 ? "None" : item)}", string.Join(" / ", pokemon.Moves.Take(2)), string.Join(" / ", pokemon.Moves.Skip(2)), selected ? caption : "Lv. 50 / View full build on hover" })
        {
            ImGui.SetCursorScreenPos(min + new Vector2(9 * scale, y)); DrawClippedText(text, width, selected && text == caption ? PokemonGreen : CompanionMuted); y += line + 3 * scale;
        }
        draw.PopClipRect(); ImGui.SetCursorScreenPos(after);
        static string Stats(Dictionary<string, int> values) => string.Join("/", new[] { "hp", "atk", "def", "spa", "spd", "spe" }.Select(key => values.GetValueOrDefault(key)));
        if (hovered) ImGui.SetTooltip($"{pokemon.DisplaySpecies} / Lv. 50\n{pokemon.Nature} / {pokemon.Ability}\nHeld item: {item}\n{string.Join(" / ", pokemon.Moves)}\nIVs (HP/Atk/Def/SpA/SpD/Spe): {Stats(pokemon.Ivs)}\nEVs: {Stats(pokemon.Evs)}");
        return clicked;
    }
    private async Task BeginFactoryDraft()
    {
        var refresh = true;
        try { await InSession(plugin.Api.DraftFactory(frontierBracket == 0 ? "accessible" : "standard")); battle = null; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Factory draft saved. Choose three rentals in lead order.");
    }
    private async Task ConfirmFactory(string[] ids)
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.PickFactory(run.Id, run.Revision, ids)); megaEvolveSelected = false; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Factory team saved. Your first fight is ready.");
    }
    private async Task ContinueFactory(string outgoing, string incoming)
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.NextFrontier(run.Id, run.Revision, outgoing, incoming)); megaEvolveSelected = false; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Rental swap saved. Your team is fully healed.");
    }
}
