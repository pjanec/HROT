using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.UI.Common.Facades;
using ImGuiNET;

namespace Hrot.Editor.UI
{
    /// <summary>
    /// Status-bar section that renders transport-control buttons (play/pause, step, stop)
    /// and a sim-time / time-rate display for the editor preview.
    ///
    /// <para>Rendered format: [Play/Pause] [Step] [Stop] | HH:MM:SS.SSS | 1.5x</para>
    ///
    /// <para>Registered by <see cref="EditorSubsystem"/> via
    /// <see cref="Fdp.Presentation.WindowManager.StatusBarManager.RegisterSection"/>
    /// and bound to the "Editor" perspective so it is hidden when switching away.</para>
    /// </summary>
    internal sealed class TimeControlStatusBarSection
    {
        private readonly IPreviewController   _preview;
        private readonly MasterSyncController _timeCtrl;
        private readonly EntityRepository     _world;

        // Ordered list of choosable time-rate values shown in the popup menu.
        private static readonly float[] TimeRates = { 0.1f, 0.5f, 1.0f, 1.5f, 2.0f, 5.0f, 10.0f };

        internal TimeControlStatusBarSection(
            IPreviewController   preview,
            MasterSyncController timeCtrl,
            EntityRepository     world)
        {
            _preview  = preview;
            _timeCtrl = timeCtrl;
            _world    = world;
        }

        // ── Public render entry point ─────────────────────────────────────────

        /// <summary>
        /// Called each frame by the <see cref="Fdp.Presentation.WindowManager.StatusBarManager"/>.
        /// Must be called inside an active ImGui frame and inside the status-bar window.
        /// </summary>
        public void Render()
        {
            bool  inPreview = _preview.IsInPreviewMode;
            bool  isPaused  = _timeCtrl.GetMode() == TimeMode.Deterministic;
            float timeScale = _timeCtrl.GetTimeScale();

            GlobalTime gt = _world.HasSingleton<GlobalTime>()
                ? _world.GetSingleton<GlobalTime>()
                : default;

            float iconSize = MathF.Round(ImGui.GetFrameHeight() * 0.80f);

            // ── [Play/Pause] ──────────────────────────────────────────────────
            // Not in preview or paused: show play (green triangle).
            // In preview and running: show pause (two white vertical bars).
            bool showPlay = !inPreview || isPaused;
            if (DrawTransportButton("##tc_pp", iconSize, showPlay ? BtnShape.Play : BtnShape.Pause, enabled: true))
            {
                if (!inPreview)
                    _preview.EnterPreviewMode();
                else if (isPaused)
                    _timeCtrl.SwitchToContinuous();
                else
                    _timeCtrl.SwitchToDeterministic(new HashSet<int>());
            }

            ImGui.SameLine();

            // ── [Step] — enabled only when in preview AND paused ─────────────
            bool canStep = inPreview && isPaused;
            if (DrawTransportButton("##tc_step", iconSize, BtnShape.Step, enabled: canStep) && canStep)
                _timeCtrl.Step(1f / 60f);

            ImGui.SameLine();

            // ── [Stop] — enabled only when in preview ─────────────────────────
            if (DrawTransportButton("##tc_stop", iconSize, BtnShape.Stop, enabled: inPreview) && inPreview)
                _preview.ExitPreviewMode();

            // ── Sim time display ──────────────────────────────────────────────
            ImGui.SameLine();
            ImGui.TextUnformatted(" | ");
            ImGui.SameLine();

            var ts = TimeSpan.FromSeconds(gt.TotalTime);
            ImGui.TextUnformatted($"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}");

            // ── Time-rate selector ────────────────────────────────────────────
            ImGui.SameLine();
            ImGui.TextUnformatted(" | ");
            ImGui.SameLine();

            // Button label shows current rate; clicking opens the dropdown popup.
            if (ImGui.Button(FormatRate(timeScale)))
                ImGui.OpenPopup("##tc_rate_popup");

            if (ImGui.BeginPopup("##tc_rate_popup"))
            {
                foreach (float rate in TimeRates)
                {
                    bool isSelected = MathF.Abs(timeScale - rate) < 0.01f;
                    if (ImGui.Selectable(FormatRate(rate), isSelected))
                        _timeCtrl.SetTimeScale(rate);
                }
                ImGui.EndPopup();
            }
        }

        // ── Private button-drawing helpers ────────────────────────────────────

        private enum BtnShape { Play, Pause, Step, Stop }

        /// <summary>
        /// Draws a custom transport-control icon button using the
        /// <c>InvisibleButton + ImDrawList</c> pattern. Returns <c>true</c> on the frame it
        /// is clicked. When <paramref name="enabled"/> is <c>false</c> the icon is rendered
        /// dimmed and the hit area is replaced by a passive <c>Dummy</c> widget.
        /// </summary>
        private static bool DrawTransportButton(string id, float size, BtnShape shape, bool enabled)
        {
            // Capture the top-left screen position BEFORE the hit-area widget so we can
            // draw at that position via the ImDrawList regardless of cursor movement.
            var pos = ImGui.GetCursorScreenPos();

            bool clicked = false;
            bool hovered = false;
            bool pressed = false;

            if (enabled)
            {
                clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
                hovered = ImGui.IsItemHovered();
                pressed = ImGui.IsItemActive();
            }
            else
            {
                // Advance layout without registering a hit area.
                ImGui.Dummy(new Vector2(size, size));
            }

            var dl = ImGui.GetWindowDrawList();

            // Hover highlight background.
            if (hovered)
                dl.AddRectFilled(
                    pos, pos + new Vector2(size, size),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), 2f);

            // Icon shape — shift 1 px down-right when pressed for tactile feedback.
            var drawPos = pressed ? pos + new Vector2(1f, 1f) : pos;
            DrawShape(dl, shape, drawPos, size, dim: !enabled, hovered: hovered);

            // Hover border.
            if (hovered)
                dl.AddRect(
                    pos, pos + new Vector2(size, size),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)), 2f);

            return clicked;
        }

        /// <summary>
        /// Draws the icon geometry onto <paramref name="dl"/> at the given
        /// <paramref name="pos"/> using filled primitives.
        /// </summary>
        private static void DrawShape(
            ImDrawListPtr dl, BtnShape shape, Vector2 pos, float size,
            bool dim, bool hovered)
        {
            float alpha = dim ? 0.28f : (hovered ? 1.0f : 0.85f);
            float pad   = MathF.Round(size * 0.22f);

            switch (shape)
            {
                case BtnShape.Play:
                {
                    // Green filled triangle pointing right.
                    uint col = ImGui.GetColorU32(new Vector4(0.20f, 0.78f, 0.20f, alpha));
                    var p0 = pos + new Vector2(pad, pad);
                    var p1 = pos + new Vector2(size - pad, size * 0.5f);
                    var p2 = pos + new Vector2(pad, size - pad);
                    dl.AddTriangleFilled(p0, p1, p2, col);
                    break;
                }

                case BtnShape.Pause:
                {
                    // Two white vertical bars.
                    uint col  = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
                    float bw  = MathF.Round(size * 0.18f);
                    float bx  = pos.X + pad;
                    float by  = pos.Y + pad;
                    float bh  = size - pad * 2f;
                    dl.AddRectFilled(
                        new Vector2(bx,           by),
                        new Vector2(bx + bw,       by + bh), col);
                    dl.AddRectFilled(
                        new Vector2(bx + bw * 2.2f, by),
                        new Vector2(bx + bw * 3.2f, by + bh), col);
                    break;
                }

                case BtnShape.Step:
                {
                    // Small right-pointing triangle immediately left of a vertical bar.
                    uint  col    = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
                    float lineW  = MathF.Round(size * 0.16f);
                    float lineX  = pos.X + size - pad - lineW;
                    // Triangle tip stops at the left edge of the vertical bar.
                    var   p0     = pos + new Vector2(pad, pad);
                    var   p1     = new Vector2(lineX, pos.Y + size * 0.5f);
                    var   p2     = pos + new Vector2(pad, size - pad);
                    dl.AddTriangleFilled(p0, p1, p2, col);
                    // Vertical bar.
                    dl.AddRectFilled(
                        new Vector2(lineX,         pos.Y + pad),
                        new Vector2(lineX + lineW, pos.Y + size - pad), col);
                    break;
                }

                case BtnShape.Stop:
                {
                    // Red filled square.
                    uint col = ImGui.GetColorU32(new Vector4(0.90f, 0.20f, 0.20f, alpha));
                    dl.AddRectFilled(
                        pos + new Vector2(pad, pad),
                        pos + new Vector2(size - pad, size - pad), col);
                    break;
                }
            }
        }

        /// <summary>
        /// Formats a time-rate multiplier compactly: integers without decimal point,
        /// fractional values with one decimal place. Examples: 1x, 2x, 0.5x, 1.5x.
        /// </summary>
        private static string FormatRate(float rate)
        {
            int rounded = (int)MathF.Round(rate * 10f);
            return (rounded % 10 == 0)
                ? $"{rounded / 10}x"
                : $"{rate:F1}x";
        }
    }
}
