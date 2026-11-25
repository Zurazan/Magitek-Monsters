using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string factoryViewKey = string.Empty;
    private readonly List<string> factoryPicks = [];
    private string factorySwapOut = string.Empty;
    private string factorySwapIn = string.Empty;

    private void DrawFactory(FrontierRunDto run, bool drafting)
    {
        var key = $"{trainer!.User.Id}:{run.Id}:{run.Revision}";
        if (factoryViewKey != key)
        {
            factoryViewKey = key;
            factoryPicks.Clear();
            factorySwapOut = factorySwapIn = string.Empty;
        }

        ImGui.BeginDisabled(busy);
        DrawGoalText("Hover a rental to inspect its build.", CompanionMuted);
        if (drafting)
        {
            DrawFactoryGrid("FactoryDraft", run.Offers, entry =>
            {
                var order = factoryPicks.IndexOf(entry.Id);
                var caption = order < 0 ? "Rental" : order == 0 ? "Pick 1 / Lead" : $"Pick {order + 1}";
                if (DrawFrontierMember(entry.Pokemon, caption, true, order >= 0, order < 0 && factoryPicks.Count >= 3))
                {
                    if (order >= 0) factoryPicks.RemoveAt(order);
                    else factoryPicks.Add(entry.Id);
                }
            });
        }
        else
        {
            DrawGoalText("Your team / Choose one to return", CompanionAccent);
            DrawFactoryGrid("FactoryCurrent", run.Team, entry =>
            {
                var selected = factorySwapOut == entry.Id;
                if (!DrawFrontierMember(entry.Pokemon, selected ? "Selected to return" : "Your rental", true, selected)) return;
                factorySwapOut = selected ? string.Empty : entry.Id;
                factorySwapIn = string.Empty;
            });
            if (factorySwapOut.Length > 0)
            {
                ImGui.Spacing();
                DrawGoalText("Opponent rentals / Choose one to borrow", CompanionAccent);
                DrawFactoryGrid("FactorySwap", run.Offers, entry =>
                {
                    var selected = factorySwapIn == entry.Id;
                    if (DrawFrontierMember(entry.Pokemon, selected ? "Selected to borrow" : "Rental offer", true, selected))
                        factorySwapIn = selected ? string.Empty : entry.Id;
                });
            }
        }
        ImGui.EndDisabled();
        ImGui.Spacing();

        if (drafting)
        {
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            ImGui.BeginDisabled(busy || factoryPicks.Count != 3);
            if (DrawAccentButton($"Confirm team ({factoryPicks.Count}/3)", new Vector2(0, 34 * CompanionScale), CompanionAccent))
            {
                var picks = factoryPicks.ToArray();
                Run(() => ConfirmFactory(picks), "Saving your Factory team...");
            }
            ImGui.EndDisabled();
            SameLineIfFits("End run", right);
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("End run", new Vector2(0, 34 * CompanionScale), PokemonOrange)) ImGui.OpenPopup("End Frontier run?");
            ImGui.EndDisabled();
            return;
        }

        if (factorySwapIn.Length > 0)
        {
            var error = FactoryPresentation.SwapError(run, factorySwapOut, factorySwapIn);
            if (error.Length > 0) DrawGoalText(error, PokemonOrange);
            ImGui.BeginDisabled(busy || error.Length > 0);
            if (DrawAccentButton("Swap and continue", new Vector2(0, 34 * CompanionScale), PokemonGreen))
            {
                var outgoing = factorySwapOut;
                var incoming = factorySwapIn;
                Run(() => ContinueFactory(outgoing, incoming), "Saving your rental swap...");
            }
            ImGui.EndDisabled();
        }
        DrawFrontierRunActions("Keep team and continue", () => Run(ContinueFrontier, "Starting the next Factory fight..."));
    }

    private static void DrawFactoryGrid(string id, List<FrontierMemberDto> members, Action<FrontierMemberDto> draw)
    {
        if (members.Count == 0)
        {
            DrawGoalText("No rentals are available.", CompanionMuted);
            return;
        }
        if (!ImGui.BeginTable(id, FactoryPresentation.Columns(ImGui.GetContentRegionAvail().X, CompanionScale), ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var member in members)
        {
            ImGui.TableNextColumn();
            ImGui.PushID(member.Id);
            draw(member);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private async Task BeginFactoryDraft(string bracket)
    {
        var refresh = true;
        try { await InSession(plugin.Api.DraftFactory(bracket)); battle = null; }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Factory draft saved. Choose three rentals in lead order.");
    }
    private async Task ConfirmFactory(string[] ids)
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.PickFactory(run.Id, run.Revision, ids)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Factory team saved. Your first fight is ready.");
    }
    private async Task ContinueFactory(string outgoing, string incoming)
    {
        var run = trainer!.FrontierRun!;
        var refresh = true;
        try { battle = await InSession(plugin.Api.NextFrontier(run.Id, run.Revision, outgoing, incoming)); }
        catch (Exception exception) when (SkipActionRefresh(exception)) { refresh = false; throw; }
        finally { if (refresh) trainer = await InSession(plugin.Api.GetMe()); }
        SetStatus("Rental swap saved. Your team is fully healed.");
    }
}
