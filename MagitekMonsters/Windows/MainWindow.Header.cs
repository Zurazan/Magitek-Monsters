using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly CompanionFeedback headerFeedback = new();
    private sealed record HeaderCurrency(string Label, string Value, string Description, Vector4 Color);

    private bool DrawCompanionHeader()
    {
        if (trainer is null) return false;
        var scale = CompanionScale;
        var padding = new Vector2(14, 10) * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var buttonSize = Math.Max(ImGui.GetFrameHeight(), 26 * scale);
        var connection = HeaderConnectionText();
        var number = plugin.Connection.State == ServerConnectionState.Connected && onlineTrainers >= 0 ? onlineTrainers.ToString("N0") : "?";
        var currencies = new[]
        {
            new HeaderCurrency("¥", trainer.Currency.ToString("N0"), "Poké Mart currency", new Vector4(0.88f, 0.77f, 0.50f, 1)),
            new HeaderCurrency("CP", trainer.CollectionPoints.Balance.ToString("N0"), "Collection Points", CompanionAccent),
            new HeaderCurrency("BP", trainer.Frontier.Balance.ToString("N0"), "Battle Points", new Vector4(0.77f, 0.69f, 0.90f, 1)),
            new HeaderCurrency("RT", (trainer.PvpProfile.RankTokens?.Balance ?? 0).ToString("N0"), "Rank Tokens", new Vector4(0.88f, 0.68f, 0.52f, 1))
        };
        var outerWidth = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var identityCode = trainer.User.VanityCode ?? trainer.User.TrainerId;
        var plan = CompanionHeaderLayout.Plan(Math.Max(1, outerWidth - padding.X * 2), lineHeight, buttonSize, scale,
            ImGui.CalcTextSize(string.IsNullOrWhiteSpace(identityCode) ? "TRAINER ID PENDING" : identityCode).X + 10 * scale,
            ImGui.CalcTextSize(number + " Users Online").X + 4 * scale,
            ImGui.CalcTextSize(connection).X + buttonSize + 30 * scale,
            currencies.Select(currency => ImGui.CalcTextSize(currency.Label).X + 6 * scale + ImGui.CalcTextSize(currency.Value).X).ToArray());
        var height = plan.Height + padding.Y * 2;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CompanionSurface);
        ImGui.BeginChild("CompanionIdentity", new Vector2(0, height), false, ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        var windowMin = ImGui.GetWindowPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(windowMin, windowMin + ImGui.GetWindowSize(),
            ImGui.GetColorU32(new Vector4(0.13f, 0.15f, 0.19f, 1)), ImGui.GetColorU32(CompanionSurface),
            ImGui.GetColorU32(CompanionSurface), ImGui.GetColorU32(new Vector4(0.13f, 0.15f, 0.19f, 1)));
        var origin = ImGui.GetCursorScreenPos();
        var name = trainer.User.DisplayName;
        HeaderText(origin, plan.Name, name, CompanionAccent, 1.25f);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);

        ImGui.SetCursorScreenPos(origin + new Vector2(plan.Uid.X, plan.Uid.Y));
        DrawCopyableTrainerId(identityCode, plan.Uid.Width);
        if (trainer.User.VanityCode is not null && ImGui.IsItemHovered()) ImGui.SetTooltip($"Permanent UID: {trainer.User.TrainerId}\nClick to copy your vanity code.");
        if (plan.DividerX is { } divider)
            draw.AddText(origin + new Vector2(divider - ImGui.CalcTextSize("|").X / 2, plan.Uid.Y), ImGui.GetColorU32(CompanionMuted with { W = 0.55f }), "|");
        var onlinePosition = origin + new Vector2(plan.Online.X, plan.Online.Y);
        ImGui.SetCursorScreenPos(onlinePosition);
        ImGui.InvisibleButton("##HeaderOnline", new Vector2(plan.Online.Width, plan.Online.Height));
        draw.PushClipRect(onlinePosition, onlinePosition + new Vector2(plan.Online.Width, plan.Online.Height), true);
        draw.AddText(onlinePosition, ImGui.GetColorU32(number == "?" ? CompanionMuted : PokemonGreen), number);
        draw.AddText(onlinePosition + new Vector2(ImGui.CalcTextSize(number).X + 4 * scale, 0), ImGui.GetColorU32(CompanionMuted), "Users Online");
        draw.PopClipRect();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(number == "?" ? "Online count is unavailable while live updates reconnect or synchronize." : $"{number} trainers connected to Magitek Monsters.");

        var controls = origin + new Vector2(plan.Connection.X, plan.Connection.Y);
        var tagWidth = Math.Max(1, plan.Connection.Width - buttonSize - 10 * scale);
        ImGui.SetCursorScreenPos(controls);
        ImGui.InvisibleButton("##HeaderConnection", new Vector2(tagWidth, buttonSize));
        var color = busy && plugin.Connection.State == ServerConnectionState.Connected ? PokemonYellow : ConnectionColor(plugin.Connection.State);
        draw.PushClipRect(controls, controls + new Vector2(tagWidth, buttonSize), true);
        draw.AddCircleFilled(controls + new Vector2(3 * scale, buttonSize / 2), 2.5f * scale, ImGui.GetColorU32(color));
        draw.AddText(controls + new Vector2(12 * scale, Math.Max(0, (buttonSize - lineHeight) / 2)), ImGui.GetColorU32(color), connection);
        draw.PopClipRect();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{plugin.Connection.State}\n{status}");
        ImGui.SetCursorScreenPos(controls + new Vector2(Math.Max(0, plan.Connection.Width - buttonSize), 0));
        var disconnect = HeaderIconButton(FontAwesomeIcon.Unlink, "Disconnect from Magitek Monsters", PokemonRed, buttonSize);
        for (var index = 0; index < currencies.Length; index++) DrawHeaderCurrency(origin, plan.Currencies[index], currencies[index]);
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar();
        return disconnect;
    }
    private string HeaderConnectionText() => plugin.Connection.State switch
    {
        ServerConnectionState.Connected when busy => "Working...",
        ServerConnectionState.Connecting => "Connecting...",
        ServerConnectionState.Reconnecting => "Reconnecting...",
        ServerConnectionState.Syncing => "Syncing...",
        _ => plugin.Connection.State.ToString()
    };
    private static void HeaderText(Vector2 origin, HeaderRegion region, string text, Vector4 color, float fontScale = 1)
    {
        var min = origin + new Vector2(region.X, region.Y);
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##HeaderName", new Vector2(region.Width, region.Height));
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, min + new Vector2(region.Width, region.Height), true);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * fontScale, min, ImGui.GetColorU32(color), text);
        draw.PopClipRect();
    }
    private static bool HeaderIconButton(FontAwesomeIcon icon, string tooltip, Vector4 color, float size)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color with { W = 0.12f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color with { W = 0.22f });
        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            clicked = ImGui.Button(icon.ToIconString() + "##" + tooltip, new Vector2(size));
        ImGui.PopStyleColor(4);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }
    private void DrawHeaderCurrency(Vector2 origin, HeaderRegion region, HeaderCurrency currency)
    {
        var min = origin + new Vector2(region.X, region.Y);
        ImGui.SetCursorScreenPos(min); ImGui.PushID(currency.Label);
        if (ImGui.InvisibleButton("##HeaderCurrency", new Vector2(region.Width, region.Height))) mainSection = 15;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, min + new Vector2(region.Width, region.Height), true);
        draw.AddText(min, ImGui.GetColorU32(currency.Color), currency.Label);
        draw.AddText(min + new Vector2(ImGui.CalcTextSize(currency.Label).X + 6 * CompanionScale, 0), ImGui.GetColorU32(ImGuiCol.Text), currency.Value);
        draw.PopClipRect();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip($"{currency.Description}: {currency.Value}\nClick to view currencies.");
        }
        ImGui.PopID();
    }
    private void DrawCompanionError()
    {
        if (headerFeedback.Current is not { IsError: true } notice) return;
        var scale = CompanionScale;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.22f, 0.13f, 0.15f, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 6) * scale);
        ImGui.BeginChild("CompanionError", new Vector2(0, ImGui.GetFrameHeight() + 12 * scale), false, ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (ImGui.BeginTable("CompanionErrorLayout", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Dismiss", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableNextColumn();
            DrawClippedText(notice.Message, ImGui.GetContentRegionAvail().X, new Vector4(0.96f, 0.72f, 0.74f, 1));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(notice.Message);
            ImGui.TableNextColumn();
            if (HeaderIconButton(FontAwesomeIcon.Times, "Dismiss error", PokemonRed, ImGui.GetFrameHeight())) headerFeedback.Clear();
            ImGui.EndTable();
        }
        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.PopStyleColor();
    }
}
