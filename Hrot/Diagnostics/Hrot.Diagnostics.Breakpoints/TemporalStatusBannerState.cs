namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Pure-logic state for the temporal status banner.
/// Extracted from <see cref="TemporalStatusBannerPanel"/> so the rendering
/// logic can be tested without an ImGui context.
/// Call <see cref="Refresh"/> once per frame; read <see cref="ShouldRender"/>
/// and <see cref="StatusText"/> to drive the ImGui panel.
/// </summary>
public sealed class TemporalStatusBannerState
{
    /// <summary>True when the banner should be visible (manager is paused).</summary>
    public bool ShouldRender { get; private set; }

    /// <summary>
    /// Full status text to display when <see cref="ShouldRender"/> is true.
    /// Empty when <see cref="ShouldRender"/> is false.
    /// </summary>
    public string StatusText { get; private set; } = string.Empty;

    /// <summary>
    /// Updates the banner state from the current manager state.
    /// Call once per frame before rendering.
    /// </summary>
    public void Refresh(IDataBreakpointManager manager)
    {
        ShouldRender = manager.IsPaused;
        if (ShouldRender)
            StatusText = $"PAUSED -- Pre-Execution State (Tick {manager.PausedTick})" +
                         $"  [ {manager.PendingMutationsCount} Pending Mutations ]";
        else
            StatusText = string.Empty;
    }
}
