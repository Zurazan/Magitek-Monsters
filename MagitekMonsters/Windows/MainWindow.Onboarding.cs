using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly Vector4 OnboardingBlue = new(0.65f, 0.76f, 1f, 1f);
    private static readonly Vector4 OnboardingMuted = new(0.70f, 0.70f, 0.74f, 1f);
    private static readonly Vector4 OnboardingHisui = new(0.82f, 0.73f, 0.91f, 1f);

    private static string StarterRegion(int generation) => generation switch
    {
        1 => "Kanto", 2 => "Johto", 3 => "Hoenn", 4 => "Sinnoh", 5 => "Unova",
        6 => "Kalos", 7 => "Alola", 8 => "Galar", 9 => "Paldea", _ => $"Gen {generation}",
    };

    private void DrawStarterOnboarding()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var available = ImGui.GetContentRegionAvail();
        var footerHeight = 34 * scale;
        var bodyHeight = Math.Max(1, available.Y - footerHeight - ImGui.GetStyle().ItemSpacing.Y);
        var railWidth = Math.Min(180 * scale, available.X * 0.24f);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 20) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.105f, 0.105f, 0.114f, 1));
        if (ImGui.BeginChild("StarterSetupRail", new Vector2(railWidth, bodyHeight), true))
        {
            ImGui.TextColored(OnboardingMuted, "FIRST ADVENTURE");
            ImGui.Spacing();
            DrawSetupStep("Account connected", false, true, scale);
            DrawSetupStep("Choose your partner", !confirmStarter, false, scale);
            DrawSetupStep("Confirm & begin", confirmStarter, false, scale);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("A Pokémon adventure alongside FFXIV");
            ImGui.Spacing();
            foreach (var feature in new[] { "Explore & catch", "Train a team of six", "Challenge the League", "Battle online", "Complete your collection" })
            {
                ImGui.PushStyleColor(ImGuiCol.Text, OnboardingMuted);
                ImGui.TextWrapped(feature);
                ImGui.PopStyleColor();
            }
            ImGui.Spacing();
            ImGui.TextWrapped("Play inside this window. Your FFXIV adventure continues.");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        ImGui.SameLine(0, gap);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20) * scale);
        if (ImGui.BeginChild("StarterSetupBody", new Vector2(Math.Max(1, available.X - railWidth - gap), bodyHeight), false))
        {
            DrawStarterSetupBody(scale);
            DrawStarterConfirmation(scale);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        DrawStarterFooter(footerHeight, scale);
    }

    private static void DrawSetupStep(string label, bool active, bool done, float scale)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetTextLineHeight() * 2 + 8 * scale;
        var min = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(width, height));
        if (active)
        {
            draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.15f, 0.18f, 0.23f, 1)), 3 * scale);
            draw.AddRectFilled(min, min + new Vector2(3 * scale, height), ImGui.GetColorU32(OnboardingBlue));
        }
        var after = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(min + new Vector2(8, 8) * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, done ? new Vector4(0.62f, 0.77f, 0.65f, 1) : active ? OnboardingBlue : OnboardingMuted);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, width - 16 * scale));
        ImGui.TextUnformatted(label);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(after);
    }

    private void DrawStarterSetupBody(float scale)
    {
        ImGui.Text("Choose your first partner");
        ImGui.TextColored(OnboardingMuted, "Every adventure starts somewhere.");
        ImGui.TextWrapped("Pick a Pokémon from any region to begin yours.");
        ImGui.Spacing();
        if (starters.Count == 0)
        {
            ImGui.TextWrapped(catalogError ?? "Loading your starter choices from the server...");
            ImGui.TextDisabled("Retries automatically every five seconds.");
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Try again", Vector2.Zero, OnboardingBlue)) Run(() => LoadCatalogs(), "Loading starter choices...");
            ImGui.EndDisabled();
            return;
        }
        starterIndex = Math.Clamp(starterIndex, 0, starters.Count - 1);
        var generations = starters.Select(entry => entry.Generation).Distinct().Order().ToArray();
        if (!generations.Contains(starterGeneration)) starterGeneration = starters[starterIndex].Generation;
        if (starters[starterIndex].Generation != starterGeneration)
            starterIndex = starters.FindIndex(entry => entry.Generation == starterGeneration);
        var width = ImGui.GetContentRegionAvail().X;
        var regionWidth = generations.Max(generation => ImGui.CalcTextSize(StarterRegion(generation)).X) + 14 * scale;
        var regionColumns = width / generations.Length >= regionWidth ? generations.Length : (generations.Length + 1) / 2;
        ImGui.TextColored(OnboardingMuted, "STARTER REGION");
        ImGui.BeginDisabled(busy);
        if (ImGui.BeginTable("StarterRegions", regionColumns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var generation in generations)
            {
                ImGui.TableNextColumn();
                var active = starterGeneration == generation;
                ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(0.16f, 0.20f, 0.27f, 1) : Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.Text, active ? OnboardingBlue : OnboardingMuted);
                if (ImGui.Button($"{StarterRegion(generation)}##StarterRegion{generation}", new Vector2(-1, 30 * scale)))
                {
                    starterGeneration = generation;
                    starterIndex = starters.FindIndex(entry => entry.Generation == generation);
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Generation {generation}");
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text($"{StarterRegion(starterGeneration)} partners");
        ImGui.TextColored(OnboardingMuted, "Click a card to compare your choices.");
        var candidates = starters.Where(entry => entry.Generation == starterGeneration).ToArray();
        if (ImGui.BeginTable("StarterPartners", Math.Min(3, candidates.Length), ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var candidate in candidates)
            {
                ImGui.TableNextColumn();
                if (DrawStarterCard(candidate, starters[starterIndex] == candidate, scale))
                    starterIndex = starters.IndexOf(candidate);
            }
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        var selected = starters[starterIndex];
        var preview = StarterPreviewCatalog.Get(selected.Species);
        if (preview is not null)
        {
            ImGui.TextColored(OnboardingMuted, "SPECIES ABILITY");
            ImGui.SameLine();
            ImGui.Text(preview.Ability);
            ImGui.Spacing();
            ImGui.TextColored(OnboardingMuted, "EVOLUTION LINE");
            DrawStarterEvolutionLine(preview, scale);
            if (preview.RegionalFinal is not null)
            {
                ImGui.TextColored(OnboardingHisui, "Two final evolution paths");
                ImGui.TextWrapped($"You are choosing {selected.Species}, not its final form. The evolution requirements you meet later determine which path is available.");
            }
        }
        else ImGui.TextWrapped("Evolution details are unavailable for this starter.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextWrapped("Your first choice is permanent. Other starters can be unlocked with Collection Points.");
        var label = $"Continue with {selected.Species}";
        var buttonWidth = ImGui.CalcTextSize(label).X + 30 * scale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - buttonWidth));
        ImGui.BeginDisabled(busy);
        if (DrawOnboardingPrimary(label, new Vector2(buttonWidth, 38 * scale)))
        {
            confirmStarter = true;
            ImGui.OpenPopup("Your first partner##ConfirmStarter");
        }
        ImGui.EndDisabled();
    }

    private bool DrawStarterCard(StarterDto candidate, bool selected, float scale)
    {
        var preview = StarterPreviewCatalog.Get(candidate.Species);
        var types = preview?.Stages[0].Types ?? [candidate.Type];
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 230 * scale);
        var clicked = ImGui.InvisibleButton($"StarterCard{candidate.Species}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(0.14f, 0.17f, 0.22f, 1) : new Vector4(0.13f, 0.13f, 0.14f, 1)), 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? OnboardingBlue : new Vector4(0.24f, 0.24f, 0.27f, 1)), 4 * scale);
        var after = ImGui.GetCursorScreenPos();
        var spriteSize = Math.Max(1, Math.Min(96 * scale, size.X - 12 * scale));
        ImGui.SetCursorScreenPos(min + new Vector2((size.X - spriteSize) / 2, 16 * scale));
        DrawPokemonImage(candidate.Species, false, new Vector2(spriteSize));
        DrawOnboardingText(min, size.X, 118 * scale, candidate.Species, new Vector4(0.93f, 0.93f, 0.95f, 1), scale);
        var badgeWidth = Math.Max(1, Math.Min(75 * scale, (size.X - 16 * scale - (types.Length - 1) * 5 * scale) / types.Length));
        var badgeRowWidth = types.Length * badgeWidth + (types.Length - 1) * 5 * scale;
        ImGui.SetCursorScreenPos(min + new Vector2((size.X - badgeRowWidth) / 2, 143 * scale));
        DrawStarterTypeBadges(types, badgeWidth, scale);
        DrawOnboardingText(min, size.X, 175 * scale, selected ? "Selected partner" : "Select partner", selected ? OnboardingBlue : OnboardingMuted, scale);
        if (preview?.RegionalFinal is not null)
        {
            draw.AddRectFilled(min + new Vector2(5, 196) * scale, new Vector2(max.X - 5 * scale, min.Y + 226 * scale),
                ImGui.GetColorU32(new Vector4(0.20f, 0.17f, 0.25f, 1)), 3 * scale);
            DrawOnboardingText(min, size.X, 199 * scale, "Hisuian evolution available", OnboardingHisui, scale, true);
        }
        ImGui.SetCursorScreenPos(after);
        return clicked;
    }

    private void DrawStarterEvolutionLine(StarterPreview preview, float scale)
    {
        var regional = preview.RegionalFinal is not null;
        if (!ImGui.BeginTable("StarterEvolution", regional ? 6 : 5, ImGuiTableFlags.SizingStretchProp)) return;
        for (var column = 0; column < (regional ? 6 : 5); column++)
            ImGui.TableSetupColumn($"Stage{column}", column is 1 or 3 ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch,
                column is 1 or 3 ? 14 * scale : 1);
        for (var stage = 0; stage < preview.Stages.Length; stage++)
        {
            ImGui.TableNextColumn();
            DrawStarterEvolutionStage(preview.Stages[stage], stage == 0 ? "Partner" : stage == 1 ? "First evolution" : regional ? "Standard final" : "Final evolution", false, scale);
            if (stage < 2)
            {
                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 67 * scale);
                ImGui.TextColored(OnboardingMuted, "> ");
            }
        }
        if (preview.RegionalFinal is { } final)
        {
            ImGui.TableNextColumn();
            DrawStarterEvolutionStage(final, "Hisuian final", true, scale);
        }
        ImGui.EndTable();
    }

    private void DrawStarterEvolutionStage(StarterStagePreview stage, string label, bool regional, float scale)
    {
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var size = new Vector2(width, 164 * scale);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(regional ? new Vector4(0.16f, 0.14f, 0.19f, 1) : new Vector4(0.125f, 0.125f, 0.137f, 1)), 4 * scale);
        if (regional) draw.AddRect(min, min + size, ImGui.GetColorU32(OnboardingHisui with { W = 0.45f }), 4 * scale);
        DrawOnboardingText(min, width, 8 * scale, label, regional ? OnboardingHisui : OnboardingMuted, scale);
        var spriteSize = Math.Max(1, Math.Min(64 * scale, width - 8 * scale));
        ImGui.SetCursorScreenPos(min + new Vector2((width - spriteSize) / 2, 28 * scale));
        DrawPokemonImage(stage.Species, false, new Vector2(spriteSize));
        DrawOnboardingText(min, width, 96 * scale, regional ? $"Hisuian\n{stage.Species.Replace("-Hisui", string.Empty)}" : stage.Species,
            new Vector4(0.93f, 0.93f, 0.95f, 1), scale);
        var badgeWidth = Math.Max(1, Math.Min(54 * scale, (width - 8 * scale - (stage.Types.Length - 1) * 5 * scale) / stage.Types.Length));
        var rowWidth = stage.Types.Length * badgeWidth + (stage.Types.Length - 1) * 5 * scale;
        ImGui.SetCursorScreenPos(min + new Vector2((width - rowWidth) / 2, 135 * scale));
        DrawStarterTypeBadges(stage.Types, badgeWidth, scale);
        ImGui.SetCursorScreenPos(after);
    }

    private static void DrawOnboardingText(Vector2 min, float width, float y, string text, Vector4 color, float scale, bool wrap = false)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, min + new Vector2(width, 250 * scale), true);
        var lines = text.Split('\n');
        if (wrap && ImGui.CalcTextSize(text).X > width - 12 * scale)
        {
            var split = text.LastIndexOf(' ');
            if (split > 0) lines = [text[..split], text[(split + 1)..]];
        }
        foreach (var line in lines)
        {
            var textWidth = ImGui.CalcTextSize(line).X;
            draw.AddText(min + new Vector2(Math.Max(4 * scale, (width - textWidth) / 2), y), ImGui.GetColorU32(color), line);
            y += ImGui.GetTextLineHeight();
        }
        draw.PopClipRect();
    }

    private void DrawStarterTypeBadges(string[] types, float width, float scale)
    {
        for (var index = 0; index < types.Length; index++)
        {
            if (index > 0) ImGui.SameLine(0, 5 * scale);
            var path = plugin.Assets.TypeBadge(types[index]);
            var texture = path.Length == 0 ? null : Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
            if (texture is not null) ImGui.Image(texture.Handle, new Vector2(width, width * texture.Height / texture.Width));
            else DrawClippedText(types[index], width, TypeColor(types[index]));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(types[index]);
        }
    }

    private static bool DrawOnboardingPrimary(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.28f, 0.39f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.27f, 0.37f, 0.50f, 1));
        ImGui.PushStyleColor(ImGuiCol.Border, OnboardingBlue with { W = 0.65f });
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private void DrawStarterConfirmation(float scale)
    {
        if (starters.Count == 0 || !ImGui.IsPopupOpen("Your first partner##ConfirmStarter")) return;
        ImGui.SetNextWindowSizeConstraints(new Vector2(360 * scale, 0), new Vector2(500 * scale, float.MaxValue));
        if (!BeginCenteredModal("Your first partner##ConfirmStarter", ref confirmStarter, ImGuiWindowFlags.AlwaysAutoResize)) return;
        var selected = starters[starterIndex];
        var preview = StarterPreviewCatalog.Get(selected.Species);
        ImGui.Text("Your first partner");
        ImGui.Separator();
        ImGui.Text(selected.Species);
        DrawPokemonImage(selected.Species, false, new Vector2(112 * scale));
        DrawStarterTypeBadges(preview?.Stages[0].Types ?? [selected.Type], 75 * scale, scale);
        ImGui.TextWrapped($"Ready to begin with {selected.Species}?");
        ImGui.TextWrapped("This is a permanent choice. The other starters remain obtainable.");
        ImGui.Spacing();
        ImGui.BeginDisabled(busy);
        if (ImGui.Button("Keep looking", new Vector2(130 * scale, 36 * scale)))
        {
            confirmStarter = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (DrawOnboardingPrimary("Begin adventure", new Vector2(150 * scale, 36 * scale)))
        {
            Run(() => ChooseStarter(selected.Species), $"Choosing {selected.Species}...");
            confirmStarter = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.EndPopup();
    }

    private void DrawStarterFooter(float height, float scale)
    {
        ImGui.BeginChild("StarterSetupFooter", new Vector2(0, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.TextColored(OnboardingMuted, "XIVAuth connected");
        var start = ImGui.GetItemRectMax() + new Vector2(12 * scale, 0);
        start.Y = ImGui.GetItemRectMin().Y;
        var buttonWidth = ImGui.CalcTextSize("Disconnect").X + 20 * scale;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - buttonWidth - 12 * scale;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(start, new Vector2(Math.Max(start.X, right), start.Y + ImGui.GetTextLineHeight()), true);
        draw.AddText(start, ImGui.GetColorU32(statusIsError ? PokemonRed : OnboardingMuted), status);
        draw.PopClipRect();
        ImGui.SameLine(Math.Max(0, ImGui.GetWindowContentRegionMax().X - buttonWidth));
        if (ImGui.SmallButton("Disconnect")) Disconnect();
        ImGui.EndChild();
    }
}
