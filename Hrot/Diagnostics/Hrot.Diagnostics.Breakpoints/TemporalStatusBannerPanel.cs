using System;

namespace Hrot.Diagnostics.Breakpoints;

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
