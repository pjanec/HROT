using System;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>⭐⭐⭐ U-obs-5 — the whole of what <see cref="TemporalStatusBannerPanel"/> shows, this frame.
/// ⚠⚠ <b>This panel has NO standalone window anywhere</b> (measured — it is only ever constructed and
/// drawn from inside <c>Hrot.Presentation.Panels.Breakpoints.DataBreakpointManagerPanel</c>), so per
/// the queue's caller-registers rule there is no independent <c>PanelSnapshot</c> registration for it;
/// its VM is embedded into <c>DataBreakpointManagerPanelViewModel.Banner</c> by its one caller instead.
/// This record exists so that embedding is a projection, not hand-duplicated fields.</summary>
public sealed record TemporalStatusBannerViewModel(bool ShouldRender, string StatusText);

/// <summary>
/// Small overlay panel rendered when the simulation is paused by a data breakpoint.
/// Displays the paused tick and the number of pending mutations.
/// Call <see cref="Draw"/> each frame.
/// ImGuiNET is not referenced by this project, so callers supply a delegate for
/// the actual text rendering; UI subsystems with ImGui available can wrap the call
/// with their own ImGui window.
/// </summary>
public sealed class TemporalStatusBannerPanel
{
    private readonly TemporalStatusBannerState _state;

    public TemporalStatusBannerPanel(TemporalStatusBannerState state)
        => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the banner state. No ImGui, no delegate call.</summary>
    public TemporalStatusBannerViewModel BuildViewModel() => new(_state.ShouldRender, _state.StatusText);

    /// <summary>
    /// Renders the banner if the manager is paused.
    /// The <paramref name="textRenderer"/> delegate is invoked with the full status
    /// string; callers are responsible for any ImGui window setup around the call.
    /// </summary>
    public void Draw(Action<string> textRenderer)
    {
        if (!_state.ShouldRender) return;
        textRenderer(_state.StatusText);
    }
}
