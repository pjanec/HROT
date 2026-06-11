using System;
using System.Numerics;
using Hrot.UI.Common.Facades;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Main-toolbar time-control section rendering transport buttons (play/pause, step, stop)
/// at 64 px via <see cref="TransportIcons"/>, plus a formatted time readout and a
/// time-rate multiplier popup selector.
/// </summary>
/// <remarks>
/// The section reads the <see cref="ITimeTransportFacade"/> every frame (immediate mode).
/// Action logic is split from ImGui draw calls so it can be unit-tested headlessly:
/// use <see cref="PlayPauseFace"/>, <see cref="OnPlayPause"/>, <see cref="OnStep"/>,
/// <see cref="OnStop"/>, and <see cref="OnSelectRate"/> without an ImGui context.
/// </remarks>
public sealed class MainToolbarTimeControlSection
{
    private readonly ITimeTransportFacade _facade;

    public MainToolbarTimeControlSection(ITimeTransportFacade facade)
    {
        _facade = facade;
    }

    // ── Headless-testable seams ────────────────────────────────────────

    /// <summary>
    /// Returns the icon shape that reflects the current play/pause state:
    /// <see cref="TransportIcons.BtnShape.Play"/> when paused,
    /// <see cref="TransportIcons.BtnShape.Pause"/> when running.
    /// </summary>
    public static TransportIcons.BtnShape PlayPauseFace(bool isPaused)
        => isPaused ? TransportIcons.BtnShape.Play : TransportIcons.BtnShape.Pause;

    /// <summary>
    /// Handles a Play/Pause click. Only invokes <see cref="ITimeTransportFacade.TogglePlayPause"/>
    /// when <see cref="ITimeTransportFacade.IsPlayPauseEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnPlayPause()
    {
        if (_facade.IsPlayPauseEnabled)
            _facade.TogglePlayPause();
    }

    /// <summary>
    /// Handles a Step click. Only invokes <see cref="ITimeTransportFacade.Step"/>
    /// when <see cref="ITimeTransportFacade.IsStepEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnStep()
    {
        if (_facade.IsStepEnabled)
            _facade.Step();
    }

    /// <summary>
    /// Handles a Stop click. Only invokes <see cref="ITimeTransportFacade.Stop"/>
    /// when <see cref="ITimeTransportFacade.IsStopEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnStop()
    {
        if (_facade.IsStopEnabled)
            _facade.Stop();
    }

    /// <summary>
    /// Selects a time-rate multiplier via <see cref="ITimeTransportFacade.SetTimeScale"/>.
    /// </summary>
    public void OnSelectRate(float rate)
    {
        _facade.SetTimeScale(rate);
    }

    // ── Render ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the toolbar time-control group. Must be called inside an active
    /// ImGui frame (typically from a <c>MainToolbarManager</c> entry delegate).
    /// </summary>
    public void Render()
    {
        float iconSize = ImGui.GetFrameHeight();
        bool isPaused = _facade.IsPaused;
        float timeScale = _facade.TimeScale;

        // ── [Play/Pause] ───────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_pp", iconSize,
                PlayPauseFace(isPaused), enabled: _facade.IsPlayPauseEnabled))
        {
            OnPlayPause();
        }

        ImGui.SameLine();

        // ── [Step] ─────────────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_step", iconSize,
                TransportIcons.BtnShape.Step, enabled: _facade.IsStepEnabled))
        {
            OnStep();
        }

        ImGui.SameLine();

        // ── [Stop] ─────────────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_stop", iconSize,
                TransportIcons.BtnShape.Stop, enabled: _facade.IsStopEnabled))
        {
            OnStop();
        }

        ImGui.SameLine();

        // ── Time display ───────────────────────────────────────────────
        ImGui.TextUnformatted(" | ");
        ImGui.SameLine();
        ImGui.TextUnformatted(TransportIcons.FormatTime(_facade.TotalTime));

        // ── Rate selector ──────────────────────────────────────────────
        ImGui.SameLine();
        ImGui.TextUnformatted(" | ");
        ImGui.SameLine();

        if (ImGui.Button(TransportIcons.FormatRate(timeScale)))
            ImGui.OpenPopup("##mt_rate_popup");

        if (ImGui.BeginPopup("##mt_rate_popup"))
        {
            foreach (float rate in TransportIcons.TimeRates)
            {
                bool isSelected = MathF.Abs(timeScale - rate) < 0.01f;
                if (ImGui.Selectable(TransportIcons.FormatRate(rate), isSelected))
                    OnSelectRate(rate);
            }
            ImGui.EndPopup();
        }
    }
}
