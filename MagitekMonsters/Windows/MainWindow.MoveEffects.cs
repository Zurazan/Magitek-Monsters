using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly record struct MoveEffectAnchor(Vector2 Center, float Size);
    private BattleMoveEffects? battleMoveEffects;
    private BattleMoveFrame? moveEffectFrame;
    private MoveEffectAnchor[]? moveEffectAnchors;
    private ImDrawListPtr moveEffectLayer;
    private int lastMoveEffectFrame;

    private bool UpdateMoveEffects(string id, int revision, IReadOnlyList<string> log, bool finished)
    {
        moveEffectFrame = null;
        if (!plugin.Configuration.BattleMoveAnimationsEnabled || Plugin.PluginInterface?.UiBuilder?.ShouldUseReducedMotion == true)
        {
            battleMoveEffects?.Reset();
            return false;
        }

        var frame = ImGui.GetFrameCount();
        var playback = battleMoveEffects ??= new();
        if (frame > lastMoveEffectFrame + 1) playback.Reset();
        lastMoveEffectFrame = frame;
        var now = ImGui.GetTime();
        playback.Observe(id, revision, log, now, finished);
        moveEffectFrame = playback.Frame(now);
        return moveEffectFrame is not null;
    }

    private void BeginMoveEffectField()
    {
        if (moveEffectFrame is null) return;
        moveEffectAnchors ??= new MoveEffectAnchor[8];
        Array.Clear(moveEffectAnchors);
        moveEffectLayer = ImGui.GetWindowDrawList();
    }

    private void MarkMoveEffectActor(int slot, Vector2 center, float size)
    {
        if (moveEffectFrame is not null && moveEffectAnchors is not null && (uint)slot < (uint)moveEffectAnchors.Length)
            moveEffectAnchors[slot] = new(center, size);
    }

    private void DrawBattleMoveSprite(int slot, string species, string name, bool shiny, Vector2 size)
    {
        if (moveEffectFrame is not { } frame)
        {
            DrawPokemonBattleImage(species, shiny, size);
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        var center = origin + size / 2;
        MarkMoveEffectActor(slot, center, size.X);
        var source = slot == frame.Cue.Source && string.Equals(frame.Cue.SourceName, name, StringComparison.Ordinal);
        var target = Array.IndexOf(frame.Cue.Targets, slot) >= 0;
        var actor = source ? "@source" : target ? "@target" : null;
        if (actor is null)
        {
            DrawPokemonBattleImage(species, shiny, size);
            return;
        }
        ImGui.Dummy(size);
        if (GetBattleSprite(species, shiny) is not { } sprite) return;

        var direction = frame.Cue.Source < 4 ? 1f : -1f;
        var alpha = 1f;
        var position = center;
        var fittedSize = size;
        foreach (var track in frame.Tracks)
        {
            if (track.Name != actor || !TrackProgress(track, frame.Milliseconds, out var progress)) continue;
            var x = Mix(track.From[1], track.To[1], progress) - (source ? 0 : 140);
            var y = Mix(track.From[2], track.To[2], progress);
            position += new Vector2(Math.Clamp(x / 160, -.36f, .36f) * size.X * direction, -Math.Clamp(y / 150, -.28f, .28f) * size.Y);
            fittedSize *= new Vector2(Math.Clamp(Mix(track.From[3], track.To[3], progress), .45f, 1.25f),
                Math.Clamp(Mix(track.From[4], track.To[4], progress), .45f, 1.25f));
            alpha = Math.Clamp(Mix(track.From[5], track.To[5], progress), .28f, 1f);
            break;
        }
        DrawFittedSpriteAt(sprite.Texture, sprite.Region, position - fittedSize / 2, fittedSize, ImGui.GetColorU32(new Vector4(1, 1, 1, alpha)));
    }

    private void DrawMoveEffectField(Vector2 min, Vector2 max, Vector2 clipMin, Vector2 clipMax)
    {
        if (moveEffectFrame is not { } frame || moveEffectAnchors is not { } anchors) return;
        min = Vector2.Max(min, clipMin);
        max = Vector2.Min(max, clipMax);
        if (min.X >= max.X || min.Y >= max.Y || anchors[frame.Cue.Source].Size <= 0) return;
        var source = anchors[frame.Cue.Source];
        moveEffectLayer.PushClipRect(min, max, false);
        foreach (var slot in frame.Cue.Targets)
        {
            var target = anchors[slot];
            if (target.Size <= 0) continue;
            var unit = Math.Clamp(Math.Min(source.Size, target.Size) / 88f, .47f * CompanionScale, 1.1f * CompanionScale);
            var direction = target.Center.X >= source.Center.X ? 1f : -1f;
            if (frame.Tracks.Length == 0)
            {
                DrawMoveEffectFallback(frame.Cue.Blocked ? "miss" : "impact", target.Center, new Vector2(36 * unit), 1 - frame.Progress);
                continue;
            }
            foreach (var track in frame.Tracks)
            {
                if (track.Name[0] == '@' || !TrackProgress(track, frame.Milliseconds, out var progress)) continue;
                var image = MoveEffectCatalog.Data.Value.Effects.GetValueOrDefault(track.Name);
                if (image is null) continue;
                var alpha = Math.Clamp(Mix(track.From[5], track.To[5], progress), 0f, .9f);
                if (alpha <= .01f) continue;
                var from = MoveEffectPoint(track.From, source.Center, target.Center, unit, direction);
                var to = MoveEffectPoint(track.To, source.Center, target.Center, unit, direction);
                var center = Vector2.Lerp(from, to, progress);
                if (track.Ease.StartsWith("ballistic", StringComparison.Ordinal))
                    center.Y += MathF.Sin(progress * MathF.PI) * 24 * unit * (track.Ease.Contains("Under", StringComparison.Ordinal) ? 1 : -1);
                var size = new Vector2(image.W * MathF.Abs(Mix(track.From[3], track.To[3], progress)),
                    image.H * MathF.Abs(Mix(track.From[4], track.To[4], progress))) * unit;
                if (size.X < 1 || size.Y < 1) continue;
                size = Vector2.Min(size, new Vector2(200 * unit));
                if (image.File is { } file && plugin.MoveTextures?.Get(file) is { } texture)
                {
                    moveEffectLayer.AddImage(texture.Handle, center - size / 2, center + size / 2, Vector2.Zero, Vector2.One,
                        ImGui.GetColorU32(new Vector4(1, 1, 1, alpha)));
                }
                else DrawMoveEffectFallback(track.Name, center, size, alpha);
            }
        }
        moveEffectLayer.PopClipRect();
    }

    private void DrawMoveEffectFallback(string effect, Vector2 center, Vector2 size, float alpha)
    {
        var color = ImGui.GetColorU32((effect == "lightning" ? PokemonYellow : CompanionAccent) with { W = alpha * .7f });
        if (effect is "leftslash" or "rightslash" or "sword")
        {
            var scale = CompanionScale;
            var half = Vector2.Max(size / 2, new Vector2(22, 17) * scale);
            var direction = effect == "rightslash" ? -1f : 1f;
            var start = center + new Vector2(-half.X * direction, half.Y);
            var end = center + new Vector2(half.X * direction, -half.Y);
            var control = center + new Vector2(half.X * .1f * direction, half.Y * .55f);
            moveEffectLayer.AddBezierCubic(start, control, control, end, color, 8 * scale, 16);
            moveEffectLayer.AddBezierCubic(start, control, control, end, ImGui.GetColorU32(new Vector4(.88f, .95f, 1f, alpha)), 3 * scale, 16);
        }
        else if (effect == "lightning")
        {
            var last = center - new Vector2(0, size.Y / 2);
            for (var i = 1; i <= 5; i++)
            {
                var x = i == 5 ? 0 : (i % 2 == 0 ? -1 : 1) * Math.Max(3, size.X * .3f);
                var next = center + new Vector2(x, size.Y * (i / 5f - .5f));
                moveEffectLayer.AddLine(last, next, color, 2.5f * CompanionScale);
                last = next;
            }
        }
        else
        {
            var radius = Math.Clamp(Math.Min(size.X, size.Y) / 2, 3, 52 * CompanionScale);
            moveEffectLayer.AddCircle(center, radius, color, 22, 2 * CompanionScale);
        }
    }

    private static Vector2 MoveEffectPoint(float[] point, Vector2 source, Vector2 target, float unit, float direction)
    {
        var far = point[1] >= 70;
        return (far ? target : source) + new Vector2((point[1] - (far ? 140 : 0)) * direction, -point[2]) * unit;
    }

    private static bool TrackProgress(MoveEffectTrack track, float time, out float progress)
    {
        progress = 0;
        if (time < track.From[0] || time >= track.To[0]) return false;
        var t = (time - track.From[0]) / (track.To[0] - track.From[0]);
        progress = track.Ease switch
        {
            "accel" => t * t,
            "decel" => 1 - (1 - t) * (1 - t),
            "swing" => (1 - MathF.Cos(t * MathF.PI)) / 2,
            _ => t,
        };
        return true;
    }

    private static float Mix(float from, float to, float progress) => from + (to - from) * progress;
}
