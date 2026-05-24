using Fdp.Core;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---------------------------------------------------------------------------
// TemporalStatusBannerTests  (UBP-P3T3)
// ---------------------------------------------------------------------------

/// <summary>
/// Tests for <see cref="TemporalStatusBannerState"/> and
/// <see cref="TemporalStatusBannerPanel"/>.
/// </summary>
public sealed class TemporalStatusBannerTests
{
    /// <summary>
    /// When the manager is not paused, the banner should not render and the
    /// status text should be empty.
    /// </summary>
    [Fact]
    public void Banner_HiddenWhenNotPaused()
    {
        var (manager, _, _, _) = ManagerFactory.Create();

        var state = new TemporalStatusBannerState();
        state.Refresh(manager);

        Assert.False(state.ShouldRender);
        Assert.Equal(string.Empty, state.StatusText);
    }

    /// <summary>
    /// When the manager is paused and two mutations are staged, the banner must
    /// render with the tick number and pending mutation count in its text.
    /// </summary>
    [Fact]
    public void Banner_ShowsTickAndCount_WhenPaused()
    {
        var (manager, _, _, _) = ManagerFactory.Create();

        var bpId         = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        var registeredBp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(registeredBp, new Entity(1, 0));

        manager.StageMutation(new Entity(1, 0), typeof(object), new object());
        manager.StageMutation(new Entity(1, 0), typeof(object), new object());

        var state = new TemporalStatusBannerState();
        state.Refresh(manager);

        Assert.True(state.ShouldRender);
        Assert.Contains("Tick ", state.StatusText);
        Assert.Contains("2 Pending Mutations", state.StatusText);
    }

    /// <summary>
    /// The delegate-based Draw must invoke the text renderer exactly once when paused
    /// and not at all when not paused.
    /// </summary>
    [Fact]
    public void Panel_Draw_InvokesRenderer_WhenPaused()
    {
        var (manager, _, _, _) = ManagerFactory.Create();

        var bpId         = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        var registeredBp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(registeredBp, new Entity(1, 0));

        var state = new TemporalStatusBannerState();
        state.Refresh(manager);
        var panel = new TemporalStatusBannerPanel(state);

        string? captured = null;
        int callCount = 0;
        panel.Draw(text => { captured = text; callCount++; });

        Assert.Equal(1, callCount);
        Assert.NotNull(captured);
    }

    /// <summary>
    /// When not paused, Draw must not invoke the text renderer.
    /// </summary>
    [Fact]
    public void Panel_Draw_DoesNotInvokeRenderer_WhenNotPaused()
    {
        var (manager, _, _, _) = ManagerFactory.Create();

        var state = new TemporalStatusBannerState();
        state.Refresh(manager);
        var panel = new TemporalStatusBannerPanel(state);

        int callCount = 0;
        panel.Draw(_ => callCount++);

        Assert.Equal(0, callCount);
    }
}
