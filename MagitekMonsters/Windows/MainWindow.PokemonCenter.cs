using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private static readonly Vector4 CenterAccent = new(0.90f, 0.48f, 0.56f, 1);
    private bool healingParty;

    private void DrawPokemonCenter()
    {
        if (trainer is null) return;
        var scale = CompanionScale;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var wide = width >= 560 * scale;
        var padding = 12 * scale;
        var iconWidth = width >= 360 * scale ? 52 * scale : 0;
        var buttonWidth = Math.Min(156 * scale, Math.Max(1, width - 2 * padding));
        var textWidth = Math.Max(1, width - 2 * padding - iconWidth - (wide ? buttonWidth + 18 * scale : 0));
        var count = trainer.Party.Count;
        var needsCare = trainer.Party.Count(NeedsRest);
        var fainted = trainer.Party.Count(pokemon => pokemon.Vitals?.Hp == 0);
        var lowPp = trainer.Party.Count(pokemon => pokemon.Vitals?.Moves.Any(move => move.Pp < move.MaxPp) == true);
        var unknown = trainer.Party.Count(pokemon => pokemon.Vitals is null);
        var condition = count == 0 ? "Add Pokémon from PC Storage to use the Center."
            : needsCare > 0 ? $"{needsCare} of {count} Pokémon need care{(fainted > 0 ? $" / {fainted} fainted" : "")}{(lowPp > 0 ? $" / {lowPp} low PP" : "")}."
            : unknown > 0 ? "Party condition unavailable." : "Your party is fully healed.";
        var restriction = CenterRestriction();
        var statusText = restriction.Length > 0 ? restriction : condition;
        var statusColor = healingParty ? PokemonYellow : restriction.Length > 0 ? PokemonOrange
            : count > 0 && needsCare == 0 && unknown == 0 ? PokemonGreen : fainted > 0 ? CenterAccent : CompanionMuted;
        const string title = "POKÉMON CENTER";
        const string description = "Free party healing. Restores HP and PP, cures status and revives fainted Pokémon.";
        var titleHeight = ImGui.CalcTextSize(title, false, textWidth).Y;
        var descriptionHeight = ImGui.CalcTextSize(description, false, textWidth).Y;
        var statusHeight = ImGui.CalcTextSize(statusText, false, textWidth).Y;
        var textHeight = titleHeight + descriptionHeight + statusHeight + 10 * scale;
        var buttonHeight = Math.Max(36 * scale, ImGui.GetFrameHeight());
        var contentHeight = Math.Max(textHeight, iconWidth > 0 ? 40 * scale : 0);
        var height = 2 * padding + contentHeight + (wide ? 0 : buttonHeight + 10 * scale);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));
        var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.17f, 0.13f, 0.15f, 1)), 5 * scale);
        draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(CenterAccent with { W = 0.40f }), 5 * scale);
        draw.AddRectFilled(min + new Vector2(0, 5 * scale), min + new Vector2(3 * scale, height - 5 * scale), ImGui.GetColorU32(CenterAccent), 2 * scale);
        if (iconWidth > 0)
        {
            var center = min + new Vector2(padding + 19 * scale, padding + 22 * scale);
            draw.AddCircleFilled(center, 19 * scale, ImGui.GetColorU32(CenterAccent with { W = 0.18f }));
            draw.AddRectFilled(center - new Vector2(3, 10) * scale, center + new Vector2(3, 10) * scale, ImGui.GetColorU32(CenterAccent), scale);
            draw.AddRectFilled(center - new Vector2(10, 3) * scale, center + new Vector2(10, 3) * scale, ImGui.GetColorU32(CenterAccent), scale);
        }
        var textX = min.X + padding + iconWidth;
        var y = min.Y + padding;
        DrawCenterText(title, CenterAccent, titleHeight);
        DrawCenterText(description, CompanionMuted, descriptionHeight);
        DrawCenterText(statusText, statusColor, statusHeight);
        var button = wide ? min + new Vector2(width - padding - buttonWidth, (height - buttonHeight) / 2)
            : min + new Vector2(padding + iconWidth, padding + contentHeight + 10 * scale);
        ImGui.SetCursorScreenPos(button);
        ImGui.BeginDisabled(CollectionLocked || count == 0 || needsCare == 0 && unknown == 0);
        if (DrawAccentButton(healingParty ? "Healing..." : "Heal party", new Vector2(Math.Max(1, Math.Min(buttonWidth, width - (button.X - min.X) - padding)), buttonHeight), CenterAccent))
            Run(HealPartyAtCenter, "Healing your party at the Pokémon Center...");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(restriction.Length > 0 ? restriction
            : count == 0 ? "Add a Pokémon from PC Storage first." : "Restore your entire party for free. No medicine or currency is consumed.");
        ImGui.SetCursorScreenPos(after);

        void DrawCenterText(string text, Vector4 color, float measuredHeight)
        {
            ImGui.SetCursorScreenPos(new Vector2(textX, y));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
            ImGui.TextColored(color, text);
            ImGui.PopTextWrapPos();
            y += measuredHeight + 5 * scale;
        }
    }
    private string CenterRestriction()
    {
        if (healingParty) return "Healing your party...";
        if (LeagueBusy) return "Finish or end your League run before visiting the Center.";
        if (FrontierBusy) return "Finish or end your Frontier run before visiting the Center.";
        if (trainer?.ActiveEncounter is not null) return "Finish or flee your wild encounter before healing.";
        if (battle?.Status == "active" || trainer?.ActiveTrainerBattle?.Status == "active" || pvpStatus?.Battle?.Status == "active") return "Finish your battle before healing.";
        if (pvpStatus?.State == "queued") return "Leave matchmaking before healing your party.";
        if (HasOutgoingInvitation) return "Cancel your outgoing battle invitation before healing.";
        return busy ? "Wait for the current action to finish." : string.Empty;
    }
    private async Task HealPartyAtCenter()
    {
        var owner = trainer?.User.Id;
        if (owner is null) return;
        healingParty = true;
        try
        {
            var healed = await InSession(plugin.Api.RestParty(disposeCancellation.Token));
            if (trainer?.User.Id != owner) return;
            trainer = healed;
            AcknowledgeBattleResultTrainer();
            SetStatus("Pokémon Center: your party is fully healed. HP, PP and status restored.");
        }
        finally { healingParty = false; }
    }
}
