using System.Linq;
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
[Collection("ComponentRegistry")]
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

    // ---- P11T5 tests ---------------------------------------------------------

    /// <summary>
    /// When GlobalTime is registered in the live repo, PausedTick must reflect
    /// GlobalTime.TotalWallTicks rather than the repo's GlobalVersion counter.
    /// </summary>
    [Fact]
    public void PausedTick_ReflectsGlobalTimeTotalWallTicks()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();

        liveRepo.RegisterComponent<GlobalTime>();
        liveRepo.SetSingletonUnmanaged(new GlobalTime { TotalWallTicks = 0xABCDEFL });

        var bpId         = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        var registeredBp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(registeredBp, new Entity(1, 0));

        Assert.Equal(0xABCDEFL, manager.PausedTick);
    }

    /// <summary>
    /// The banner StatusText must include the wall-clock tick value (from GlobalTime),
    /// not the repo's GlobalVersion counter.
    /// </summary>
    [Fact]
    public void BannerShowsWallClockTickNotVersionCounter()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();

        liveRepo.RegisterComponent<GlobalTime>();
        liveRepo.SetSingletonUnmanaged(new GlobalTime { TotalWallTicks = 12345L });

        var bpId         = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        var registeredBp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(registeredBp, new Entity(1, 0));

        var state = new TemporalStatusBannerState();
        state.Refresh(manager);

        Assert.True(state.ShouldRender);
        Assert.Contains("Tick 12345", state.StatusText);
    }

    /// <summary>
    /// When GlobalTime is NOT registered, PausedTick falls back to the repo's
    /// GlobalVersion cast to long (must not throw).
    /// </summary>
    [Fact]
    public void PausedTick_FallbackToRepoVersion_WhenGlobalTimeNotRegistered()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, snapshotProvider, _) = ManagerFactory.Create();

        // Do NOT register GlobalTime.

        var bpId         = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        var registeredBp = manager.AllBreakpoints.First(b => b.Id == bpId);

        // Seed the snapshot provider so the pre-tick snapshot has some version.
        snapshotProvider.SetEnabled(true);
        snapshotProvider.Execute(liveRepo, 0f);

        manager.OnHit(registeredBp, new Entity(1, 0));

        // Fallback: must equal the pre-tick snapshot's SimulationTick (frame clock) cast to long.
        Assert.Equal((long)manager.PreTickSnapshot.SimulationTick, manager.PausedTick);
    }
}
