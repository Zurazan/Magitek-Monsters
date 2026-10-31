using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private PokedexDto? adventurePokedex;
    private HashSet<string> adventureCaughtForms = [];
    private HashSet<string> adventureCaughtSpecies = [];
    private bool adventureNotCaughtOnly;

    private bool IsAdventureSpeciesCaught(string species)
    {
        if (trainer is null) return false;
        if (!ReferenceEquals(adventurePokedex, trainer.Pokedex))
        {
            adventurePokedex = trainer.Pokedex;
            adventureCaughtForms = adventurePokedex.CaughtForms.Select(SpriteNames.Normalize).ToHashSet();
            adventureCaughtSpecies = adventurePokedex.CaughtSpecies.Select(SpriteNames.Normalize).ToHashSet();
        }
        return SpeciesDisplayCatalog.IsCaught(species, adventureCaughtForms, adventureCaughtSpecies);
    }

    private void DrawAdventureFieldGuide(uint territoryId, TerritoryContextInfo context)
    {
        var scale = CompanionScale;
        var surveyed = habitat is not null && loadedTerritory == territoryId;
        DrawAdventureHeading(surveyed ? habitat!.Name : context.LocalizedName, surveyed ? habitat!.Theme : null);
        DrawAdventureRaidEntry();
        DrawWishlistShortcut();
        DrawCollectionWishlist(surveyed ? habitat!.Species : null);
        if (!surveyed)
        {
            ImGui.TextColored(CompanionMuted, "This area has not been surveyed yet.");
            ImGui.TextWrapped("Survey the current territory to discover its wild Pokémon.");
            ImGui.Spacing();
            ImGui.BeginDisabled(busy);
            if (DrawAccentButton("Survey current area", new Vector2(190, 34) * scale, CompanionAccent))
                Run(() => LoadHabitat(territoryId, context), $"Surveying {context.LocalizedName}...");
            ImGui.EndDisabled();
            return;
        }

        var current = habitat!;
        var description = SpeciesDisplayCatalog.HabitatDescription(current.ThemeId);
        ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
        ImGui.TextWrapped(description.Length > 0
            ? $"{char.ToUpperInvariant(description[0])}{description[1..]} shape the Pokémon found here."
            : $"Discover the {current.Theme.ToLowerInvariant()} Pokémon found in this territory.");
        if (current.MinLevel > 0)
            ImGui.TextWrapped($"Wild levels {current.MinLevel}-{current.MaxLevel} / Fixed to this territory, not your party");
        ImGui.PopStyleColor();
        if (trainer is not null) DrawPveDefeatNotice(trainer.Party, true);
        if (!current.Catalogued)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, PokemonOrange);
            ImGui.TextWrapped("Provisional habitat / This territory is not in the current area catalog.");
            ImGui.PopStyleColor();
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        bool Caught(string species) => IsAdventureSpeciesCaught(species);
        targetIndex = Math.Clamp(targetIndex, 0, current.Species.Count);
        var anyWidth = ImGui.CalcTextSize("Any species").X + 20 * scale;
        if (ImGui.BeginTable("AdventurePoolHeading", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Pool", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthFixed, anyWidth);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            DrawClippedText($"Pokémon in this area   {current.Species.Count(Caught)} / {current.Species.Count} caught",
                ImGui.GetContentRegionAvail().X, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            ImGui.TableNextColumn();
            if (DrawCompanionSegment("Any species##AdventureAny", targetIndex == 0, anyWidth)) targetIndex = 0;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Search the whole pool without a preferred species.");
            ImGui.EndTable();
        }

        ImGui.Checkbox("Not caught only", ref adventureNotCaughtOnly);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hide Pokémon already caught. Any species still searches the full area's pool.");
        var filtersWidth = ImGui.CalcTextSize("Not caught only").X + ImGui.CalcTextSize("Wishlist only").X
            + 2 * (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) + ImGui.GetStyle().ItemSpacing.X;
        if (filtersWidth <= ImGui.GetContentRegionAvail().X) ImGui.SameLine();
        ImGui.Checkbox("Wishlist only", ref adventureWishlistOnly);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Filter by pinned species and forms. Form availability may be unconfirmed.\nGoal pins do not filter encounters. Any species still searches the full pool.");
        var preferences = LocalWishlist();
        var pool = AdventurePoolFilter.Apply(current.Species, adventureNotCaughtOnly, targetIndex, Caught,
            adventureWishlistOnly, appearance => CollectionWishlist.MatchAppearance(preferences, appearance) != WishlistAvailability.None);
        targetIndex = pool.TargetIndex;
        if (targetIndex > 0)
        {
            var appearance = current.Species[targetIndex - 1];
            var display = SpeciesDisplayCatalog.Get(appearance);
            var kind = display is { Form.Length: > 0 } ? "form" : "species";
            var id = kind == "form" ? display!.Name : display?.BaseSpecies ?? appearance;
            DrawWishlistPin(kind, id, CollectionWishlist.Contains(preferences, kind, id) ? "Unpin target" : "Pin target");
        }

        var layout = AdventureLayout.FieldGuide(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize,
            ImGui.GetContentRegionAvail().Y, scale, ImGui.GetTextLineHeight(), pool.SpeciesIndices.Length);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("AdventureSpeciesGrid", new Vector2(0, layout.GridHeight), false))
        {
            if (current.Species.Count == 0) ImGui.TextWrapped("No wild Pokémon are available in this territory.");
            else if (pool.SpeciesIndices.Length == 0) ImGui.TextWrapped(adventureWishlistOnly
                ? "No wishlist entries match this area's pool and the current filters."
                : "You've caught every Pokémon in this area. Turn off the filter to see the full field guide.");
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4) * scale);
                if (ImGui.BeginTable("AdventureFieldGuide", layout.Columns, ImGuiTableFlags.SizingStretchSame))
                {
                    foreach (var index in pool.SpeciesIndices)
                    {
                        ImGui.TableNextColumn();
                        var species = current.Species[index];
                        if (DrawAdventureSpeciesCard(species, Caught(species), targetIndex == index + 1, layout.CardHeight))
                            targetIndex = index + 1;
                    }
                    ImGui.EndTable();
                }
                ImGui.PopStyleVar();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        DrawAdventureSearchBar(territoryId, context, layout);
        DrawAdventureLead(layout.LeadHeight);
    }

    private void DrawAdventureRaidEntry()
    {
        var scale = CompanionScale;
        var resume = trainer?.ActiveRaidId is not null || raidRoom?.Status is "lobby" or "active" || raidRoom?.CaptureState == "pending";
        var stacked = ImGui.GetContentRegionAvail().X < 470 * scale;
        if (ImGui.BeginTable("AdventureRaidEntry", stacked ? 1 : 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Raid info", ImGuiTableColumnFlags.WidthStretch);
            if (!stacked) ImGui.TableSetupColumn("Open dens", ImGuiTableColumnFlags.WidthFixed, 150 * scale);
            ImGui.TableNextColumn();
            ImGui.TextColored(CompanionAccent, "Cooperative Dens");
            ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
            ImGui.TextWrapped("Four allies / One owned Pokémon / NPC support");
            ImGui.PopStyleColor();
            ImGui.TableNextColumn();
            if (DrawAccentButton(resume ? "Resume raid" : "Browse raid dens", new Vector2(-1, 32 * scale), PokemonBlue))
            {
                if (trainer?.ActiveRaidId is { } activeId && raidRoom?.Id != activeId
                    || !resume) { raidRoom = null; raidBoard = null; }
                mainSection = 12;
                raidRefreshRequested = true;
                nextRaidPollAt = DateTimeOffset.MinValue;
            }
            ImGui.EndTable();
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
    }

    private static void DrawAdventureHeading(string territoryName, string? theme)
    {
        var scale = CompanionScale;
        var badge = theme is null ? "Not surveyed" : $"{theme} habitat";
        var badgeWidth = ImGui.CalcTextSize(badge).X + 18 * scale;
        var sideBySide = ImGui.GetContentRegionAvail().X >= badgeWidth + 220 * scale;
        if (!ImGui.BeginTable("AdventureHeading", sideBySide ? 2 : 1, ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Territory", ImGuiTableColumnFlags.WidthStretch);
        if (sideBySide) ImGui.TableSetupColumn("Habitat", ImGuiTableColumnFlags.WidthFixed, badgeWidth);
        ImGui.TableNextColumn();
        ImGui.TextColored(CompanionMuted, "WILD ADVENTURE");
        var titlePosition = ImGui.GetCursorScreenPos();
        var titleSize = ImGui.CalcTextSize(territoryName) * 1.2f;
        if (titleSize.X <= ImGui.GetContentRegionAvail().X)
        {
            ImGui.Dummy(titleSize);
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.2f, titlePosition,
                ImGui.GetColorU32(ImGuiCol.Text), territoryName);
        }
        else ImGui.TextWrapped(territoryName);
        ImGui.TableNextColumn();
        var min = ImGui.GetCursorScreenPos() + new Vector2(0, 4 * scale);
        var size = new Vector2(badgeWidth, ImGui.GetTextLineHeight() + 12 * scale);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(new Vector4(0.16f, 0.18f, 0.21f, 1)), 4 * scale);
        draw.AddRect(min, min + size, ImGui.GetColorU32(new Vector4(0.26f, 0.29f, 0.34f, 1)), 4 * scale);
        draw.AddText(min + new Vector2(9, 6) * scale, ImGui.GetColorU32(CompanionMuted), badge);
        ImGui.Dummy(size + new Vector2(0, 4 * scale));
        ImGui.EndTable();
    }

    private bool DrawAdventureSpeciesCard(string species, bool caught, bool selected, float height)
    {
        var scale = CompanionScale;
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), height);
        var clicked = ImGui.InvisibleButton($"AdventureSpecies##{species}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(selected ? new Vector4(0.16f, 0.20f, 0.27f, 1)
            : hovered ? new Vector4(0.15f, 0.17f, 0.21f, 1) : CompanionSurface), 4 * scale);
        if (hovered) draw.AddRect(min, max, ImGui.GetColorU32(CompanionAccent with { W = 0.14f }), 4 * scale, ImDrawFlags.None, 4 * scale);
        draw.AddRect(min, max, ImGui.GetColorU32(selected || hovered ? CompanionAccent with { W = selected ? 0.85f : 0.65f }
            : new Vector4(0.23f, 0.24f, 0.28f, 1)), 4 * scale);
        draw.AddRectFilled(min + new Vector2(0, 4 * scale), new Vector2(min.X + 3 * scale, max.Y - 4 * scale),
            ImGui.GetColorU32(selected ? CompanionAccent : new Vector4(0.28f, 0.30f, 0.35f, 1)), 1 * scale);
        draw.PushClipRect(min + new Vector2(4 * scale, 0), max - new Vector2(4 * scale, 0), true);
        ImGui.SetCursorScreenPos(min + new Vector2(9, 10) * scale);
        DrawPokemonIcon(species, false, new Vector2(48 * scale));
        var textWidth = Math.Max(1, size.X - 72 * scale);
        ImGui.SetCursorScreenPos(min + new Vector2(64, 13) * scale);
        DrawClippedText(SpeciesDisplayCatalog.Name(species), textWidth, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SetCursorScreenPos(min + new Vector2(64 * scale, 18 * scale + ImGui.GetTextLineHeight()));
        var wish = AdventureWishlistLabel(species);
        var state = caught ? "Caught" : "Not caught";
        if (wish.Length > 0) state = (wish.Contains("unconfirmed", StringComparison.Ordinal) ? "Form unconfirmed" : "Wanted") + " / " + state;
        DrawClippedText(state, textWidth, wish.Length > 0 ? PokemonYellow : caught ? new Vector4(0.70f, 0.83f, 0.74f, 1) : CompanionMuted);
        ImGui.SetCursorScreenPos(new Vector2(min.X + 10 * scale, max.Y - 29 * scale));
        var types = SpeciesDisplayCatalog.Get(species)?.Types ?? [];
        var badgeWidth = Math.Max(1, Math.Min(65 * scale, (size.X - 20 * scale - Math.Max(0, types.Length - 1) * 5 * scale) / Math.Max(1, types.Length)));
        if (types.Length > 0) DrawStarterTypeBadges(types, badgeWidth, scale);
        else ImGui.TextColored(CompanionMuted, "Types unavailable");
        draw.PopClipRect();
        ImGui.SetCursorScreenPos(after);
        if (hovered) ImGui.SetTooltip($"{SpeciesDisplayCatalog.Name(species)}\n{string.Join(" / ", types)}\n{(caught ? "Registered in your Pokédex" : "Not caught yet")}{(wish.Length > 0 ? "\n" + wish : "")}\nClick to target this Pokémon.");
        return clicked;
    }

    private void DrawAdventureSearchBar(uint territoryId, TerritoryContextInfo context, AdventureGrid layout)
    {
        var scale = CompanionScale;
        var target = targetIndex == 0 ? null : habitat!.Species[targetIndex - 1];
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.13f, 0.15f, 0.18f, 1));
        if (ImGui.BeginChild("AdventureSearchBar", new Vector2(0, layout.ActionHeight), true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var min = ImGui.GetWindowPos();
            ImGui.GetWindowDrawList().AddRectFilled(min + new Vector2(0, 4 * scale),
                min + new Vector2(3 * scale, ImGui.GetWindowSize().Y - 4 * scale), ImGui.GetColorU32(CompanionAccent), scale);
            if (layout.StackedActions)
            {
                DrawAdventureTarget(target);
                DrawAdventureSearchButton(territoryId, context, target, ImGui.GetContentRegionAvail().X);
            }
            else if (ImGui.BeginTable("AdventureSearchLayout", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Search", ImGuiTableColumnFlags.WidthFixed, 220 * scale);
                ImGui.TableNextColumn(); DrawAdventureTarget(target);
                ImGui.TableNextColumn();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6 * scale);
                DrawAdventureSearchButton(territoryId, context, target, ImGui.GetContentRegionAvail().X);
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(); ImGui.PopStyleVar();
    }

    private static void DrawAdventureTarget(string? target)
    {
        DrawClippedText(target is null ? "Any species" : $"Targeting {SpeciesDisplayCatalog.Name(target)}",
            ImGui.GetContentRegionAvail().X, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.PushStyleColor(ImGuiCol.Text, CompanionMuted);
        ImGui.TextWrapped(target is null ? "Explore the full pool without a preferred species." : "65% encounter weight; other species may appear.");
        ImGui.PopStyleColor();
    }

    private void DrawAdventureSearchButton(uint territoryId, TerritoryContextInfo context, string? target, float width)
    {
        var scale = CompanionScale;
        var waitSeconds = searchCooldown.RemainingSeconds;
        var ready = trainer!.Party.Any(pokemon => pokemon.Vitals is null || pokemon.Vitals.Hp > 0);
        var label = waitSeconds > 0 ? $"Search again in {waitSeconds}s" : target is null ? "Search for wild Pokémon" : $"Search for {SpeciesDisplayCatalog.Name(target)}";
        if (ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 > width && waitSeconds == 0 && target is not null) label = "Search for target";
        ImGui.BeginDisabled(busy || CollectionLocked || waitSeconds > 0 || !ready || habitat!.Species.Count == 0);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.44f, 0.26f, 0.30f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.53f, 0.32f, 0.36f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.39f, 0.22f, 0.26f, 1));
        if (ImGui.Button($"{label}###WildSearch", new Vector2(Math.Max(1, width), 34 * scale)))
            Run(() => Search(territoryId, context, target), $"Searching {context.LocalizedName}...");
        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(!ready
            ? "Add a healthy Pokémon or heal at the Pokémon Center in Party before searching."
            : waitSeconds > 0 ? "The search cooldown is shared across all areas and targets."
            : target is null ? "Start a wild battle from this territory's pool." : $"Search for {SpeciesDisplayCatalog.Name(target)}. Targeting is not guaranteed.");
    }

    private void DrawAdventureLead(float height)
    {
        var scale = CompanionScale;
        var lead = trainer!.Party.OrderBy(pokemon => pokemon.PartySlot).FirstOrDefault(pokemon => pokemon.Vitals is null || pokemon.Vitals.Hp > 0);
        ImGui.Separator();
        if (lead is null)
        {
            if (trainer.Party.Count == 0) ImGui.TextColored(CompanionMuted, "Your party is empty. Add a Pokémon from PC Storage.");
            else
            {
                ImGui.AlignTextToFramePadding(); ImGui.TextColored(CenterAccent, "Your party has fainted.");
                var buttonWidth = ImGui.CalcTextSize("Pokémon Center").X + 20 * scale;
                if (ImGui.GetContentRegionAvail().X >= buttonWidth + 160 * scale) ImGui.SameLine();
                if (DrawAccentButton("Pokémon Center", new Vector2(Math.Min(buttonWidth, ImGui.GetContentRegionAvail().X), 28 * scale), CenterAccent)) mainSection = 1;
            }
            return;
        }
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.Dummy(new Vector2(width, height - ImGui.GetStyle().ItemSpacing.Y));
        var after = ImGui.GetCursorScreenPos();
        var vital = CompanionVital(lead);
        var hpLabel = vital is null ? "HP unavailable" : $"{vital.Hp} / {vital.MaxHp} HP{HealthStatus(vital)}";
        var hpWidth = ImGui.CalcTextSize(hpLabel).X;
        var stacked = width < 420 * scale;
        ImGui.SetCursorScreenPos(origin);
        DrawPokemonIcon(lead.Appearance, lead.Shiny, new Vector2(40 * scale));
        ImGui.SetCursorScreenPos(origin + new Vector2(48, 4) * scale);
        ImGui.BeginGroup();
        var nameWidth = Math.Max(1, width - (stacked ? 58 * scale : hpWidth + 66 * scale));
        DrawClippedText($"Lead: {lead.Nickname} / Lv. {lead.Level}", nameWidth, CompanionMuted);
        if (stacked) DrawClippedText(hpLabel, nameWidth, CompanionMuted);
        DrawProgressBar(vital is null ? 0 : vital.Hp / (float)Math.Max(1, vital.MaxHp), new Vector2(Math.Min(nameWidth, 120 * scale), 5 * scale), "", HpBarRed);
        ImGui.EndGroup();
        if (!stacked)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X + width - hpWidth, origin.Y + 8 * scale));
            ImGui.TextColored(CompanionMuted, hpLabel);
        }
        ImGui.SetCursorScreenPos(after);
    }
}
