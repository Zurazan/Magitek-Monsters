using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private int pokedexFormPage;

    private void DrawPokedexForms(PokedexEntryDto entry)
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        var discovered = trainer.Pokedex.CaughtForms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heading = $"DISCOVERED  {entry.Forms.Count(discovered.Contains)}/{entry.Forms.Count}";
        const string note = "Permanent forms only. No extra Dex entries or CP.";
        var sameLine = ImGui.GetContentRegionAvail().X > ImGui.CalcTextSize(heading).X + ImGui.CalcTextSize(note).X + ImGui.GetStyle().ItemSpacing.X;
        ImGui.TextColored(PokemonBlue, heading);
        if (sameLine) ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted); ImGui.TextWrapped(note); ImGui.PopStyleColor();
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (175 * CompanionScale)), 1, 5);
        var pageSize = columns * 2;
        DrawPager("PokedexForms", ref pokedexFormPage, entry.Forms.Count, pageSize);
        if (!ImGui.BeginTable("PokedexFormGrid", columns, ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var form in entry.Forms.Skip(pokedexFormPage * pageSize).Take(pageSize))
        {
            ImGui.TableNextColumn();
            var caught = discovered.Contains(form);
            var original = SpriteNames.Normalize(entry.Species) switch
            {
                "UNOWN" => "A", "DEERLING" or "SAWSBUCK" => "Spring", "SHELLOS" or "GASTRODON" => "West Sea",
                "FLABEBE" or "FLOETTE" or "FLORGES" or "MINIOR" => "Red", "PUMPKABOO" or "GOURGEIST" => "Average",
                "SQUAWKABILLY" => "Green plumage", "TATSUGIRI" => "Curly", _ => "Original"
            };
            var label = form == entry.Species ? original : form.StartsWith(entry.Species + "-", StringComparison.OrdinalIgnoreCase)
                ? form[(entry.Species.Length + 1)..] : form;
            if (entry.Species == "Maushold") label = form == "Maushold" ? "Family of Three" : "Family of Four";
            if (entry.Species == "Dudunsparce") label = form == "Dudunsparce" ? "Two-Segment" : "Three-Segment";
            var decoration = SweetDecoration(form);
            var title = decoration.Length > 0 ? label[..label.LastIndexOf('-')].Replace('-', ' ') : label;
            var padding = ImGui.GetStyle().WindowPadding;
            var gap = ImGui.GetStyle().ItemSpacing.Y;
            var labelWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - 2 * padding.X - 4 * scale);
            var height = 2 * padding.Y + Math.Max(36 * scale, ImGui.GetFrameHeight()) + gap
                + ImGui.CalcTextSize(title, false, labelWidth).Y + (decoration.Length > 0 ? gap + ImGui.GetTextLineHeight() : 0);
            ImGui.PushStyleColor(ImGuiCol.Border, caught ? PokemonBlue with { W = 0.6f } : new Vector4(0.25f, 0.25f, 0.28f, 1f));
            ImGui.BeginChild($"PokedexForm-{form}", new Vector2(0, height), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.PopStyleColor();
            if (caught) DrawPokemonIcon(form, false, new Vector2(36 * scale));
            else
            {
                ImGui.Dummy(new Vector2(36 * scale));
                var min = ImGui.GetItemRectMin();
                ImGui.GetWindowDrawList().AddText(min + new Vector2(12, 7) * scale, ImGui.GetColorU32(ImGuiCol.TextDisabled), "?");
            }
            ImGui.SameLine();
            ImGui.TextColored(caught ? PokemonBlue : new Vector4(0.5f, 0.5f, 0.55f, 1f), caught ? "FOUND" : "UNKNOWN");
            ImGui.SameLine(); DrawWishlistPin("form", form);
            ImGui.TextWrapped(title);
            if (decoration.Length > 0) ImGui.TextColored(PokemonYellow, decoration + " Sweet");
            ImGui.EndChild();
        }
        ImGui.EndTable();
    }
}
