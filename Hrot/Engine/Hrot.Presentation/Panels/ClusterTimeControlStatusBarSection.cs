using System;
using System.Numerics;
using Hrot.UI.Common.Facades;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Status-bar section that renders transport-control buttons (play/pause, step, stop)
/// and a sim-time / time-rate display.  Shared across Editor, SimHost, and CGF perspectives.
///
/// <para>Rendered format: [Play/Pause] [Step] [Stop] | HH:MM:SS.SSS | 1.5x</para>
///
/// <para>Transport actions and state are provided by an <see cref="ITimeTransportFacade"/>
/// so that editor-local and cluster-bus implementations are fully interchangeable.</para>
/// </summary>
public sealed class ClusterTimeControlStatusBarSection
{
    private readonly ITimeTransportFacade _facade;

    public ClusterTimeControlStatusBarSection(ITimeTransportFacade facade)
    {
        _facade = facade;
    }

    // ── Public render entry point ─────────────────────────────────────────

    /// <summary>
    /// Called each frame by the <see cref="Fdp.Presentation.WindowManager.StatusBarManager"/>.
    /// Must be called inside an active ImGui frame and inside the status-bar window.
    /// </summary>
    public void Render()
    {
        bool  isPaused  = _facade.IsPaused;
        float timeScale = _facade.TimeScale;

        float iconSize = MathF.Round(ImGui.GetFrameHeight() * 0.80f);

        // ── [Play/Pause] ──────────────────────────────────────────────────
        // Paused or stopped: show play (green triangle).
        // Running: show pause (two white vertical bars).
        bool showPlay = isPaused;
        if (TransportIcons.DrawTransportButton("##tc_pp", iconSize,
                showPlay ? TransportIcons.BtnShape.Play : TransportIcons.BtnShape.Pause,
                enabled: _facade.IsPlayPauseEnabled))
        {
            _facade.TogglePlayPause();
        }

        ImGui.SameLine();

        // ── [Step] — enabled only when paused ────────────────────────────
        if (TransportIcons.DrawTransportButton("##tc_step", iconSize,
                TransportIcons.BtnShape.Step, enabled: _facade.IsStepEnabled)
            && _facade.IsStepEnabled)
        {
            _facade.Step();
        }

        ImGui.SameLine();

        // ── [Stop] — enabled only when operating ─────────────────────────
        if (TransportIcons.DrawTransportButton("##tc_stop", iconSize,
                TransportIcons.BtnShape.Stop, enabled: _facade.IsStopEnabled)
            && _facade.IsStopEnabled)
        {
            _facade.Stop();
        }

        // ── Sim time display ──────────────────────────────────────────────
        ImGui.SameLine();
        ImGui.TextUnformatted(" | ");
        ImGui.SameLine();

        ImGui.TextUnformatted(TransportIcons.FormatTime(_facade.TotalTime));

        // ── Time-rate selector ────────────────────────────────────────────
        ImGui.SameLine();
        ImGui.TextUnformatted(" | ");
        ImGui.SameLine();

        // Button label shows current rate; clicking opens the dropdown popup.
        if (ImGui.Button(TransportIcons.FormatRate(timeScale)))
            ImGui.OpenPopup("##tc_rate_popup");

        if (ImGui.BeginPopup("##tc_rate_popup"))
        {
            foreach (float rate in TransportIcons.TimeRates)
            {
                bool isSelected = MathF.Abs(timeScale - rate) < 0.01f;
                if (ImGui.Selectable(TransportIcons.FormatRate(rate), isSelected))
                    _facade.SetTimeScale(rate);
            }
            ImGui.EndPopup();
        }
    }
}
