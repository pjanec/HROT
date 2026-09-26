using System.Collections.Generic;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiComposition;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// <b><c>AS-9</c> / <c>T4d</c> — control path D was a set of virtual NO-OPS.</b>
///
/// <para><c>AiTracerCoordinator.RequestPause</c>, <c>RequestContinue</c> and
/// <c>RequestStepOneTick</c> are <c>virtual</c>, and <c>EditorSubsystem</c> constructed the class
/// with nothing behind them. So a BTree or HSM tracer asking the simulation to stop did exactly
/// nothing — no exception, no log, no pause. The capability was built, documented and reachable,
/// and was simply never turned on.</para>
///
/// <para>This is the "the clock exists and nothing turns it on" shape the programme keeps hitting,
/// and the only thing that catches it is a rail asserting the WIRE, not the capability. A test that
/// called <c>RequestPause()</c> and asserted "no exception" would have passed throughout.</para>
///
/// <para>⭐⭐⭐ <b><c>CE-349</c> (<c>2026-09-26</c>) — RE-POINTED, NOT WEAKENED.</b> The wire it pins
/// moved: the editor-only subclass over <c>ITimeCommands</c> is deleted, and the coordinator now
/// takes <see cref="IEngineDebugTimeController"/> — the abstraction the Blueprint debugger and the
/// breakpoint manager already share, and which BOTH hosts implement. ⭐ The three claims are the
/// same three, and the last one is now stronger: it is asserted at the boundary BOTH hosts go
/// through rather than at one host's constructor. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c>
/// §32.24.</para>
/// </summary>
public sealed class TheTracerCoordinatorActuallyControlsTimeTests
{
    private sealed class SpyTimeController : IEngineDebugTimeController
    {
        public readonly List<string> Calls = new();
        public bool IsPausedByDebugger     { get; set; }
        public void RequestPause()        => Calls.Add(nameof(RequestPause));
        public void RequestResume()       => Calls.Add(nameof(RequestResume));
        public void RequestStepOneTick()  => Calls.Add(nameof(RequestStepOneTick));
    }

    [Fact]
    public void TheCoordinator_ForwardsEveryRequest_ToTheOneTimeControlAbstraction()
    {
        var spy         = new SpyTimeController();
        var coordinator = new AiTracerCoordinator(spy);

        coordinator.RequestPause();
        coordinator.RequestStepOneTick();
        coordinator.RequestContinue();

        // ⚠ `Continue` maps to `Resume` — one verb, two spellings, mapped in exactly one place.
        Assert.Equal(
            new[] { "RequestPause", "RequestStepOneTick", "RequestResume" },
            spy.Calls);
    }

    /// <summary>
    /// The controller-less coordinator is the thing that must NOT reach production. Pinning its
    /// no-op-ness here states plainly why: nothing about calling it looks wrong.
    /// </summary>
    [Fact]
    public void TheControllerLessCoordinator_IsSilentlyInert_WhichIsWhyTheComposerRefusesNull()
    {
        var bare = new AiTracerCoordinator();

        // No exception, no effect, no way for a caller to tell. That was production.
        var ex = Record.Exception(() =>
        {
            bare.RequestPause();
            bare.RequestStepOneTick();
            bare.RequestContinue();
        });

        Assert.Null(ex);
        Assert.False(bare.HasTimeControl);
    }

    /// <summary>
    /// ⭐⭐ A coordinator with no time control cannot control time — so the composition both hosts
    /// call refuses to build one. ⛔ This is the rail that makes the `T4d` defect unreachable rather
    /// than merely fixed-once: it fails at startup instead of shipping dead buttons.
    /// </summary>
    [Fact]
    public void TheComposer_RefusesToBuildSessionsWithoutTimeControl()
        => Assert.Throws<System.ArgumentNullException>(
            () => AiDebugSessionComposer.Compose(null!));

    /// <summary>
    /// ⭐⭐⭐ <b>BOTH sessions share ONE coordinator, and it is the one that controls time.</b>
    /// 📐 The drift this replaces was real and measured: the editor passed a coordinator, CGF passed
    /// none, so BTree/HSM pause was live on one host and a silent no-op on the other.
    /// </summary>
    [Fact]
    public void TheComposedSessions_ShareTheOneTimeControllingCoordinator()
    {
        var spy      = new SpyTimeController();
        var composed = AiDebugSessionComposer.Compose(spy);

        Assert.True(composed.Coordinator.HasTimeControl);
        Assert.NotNull(composed.BTree);
        Assert.NotNull(composed.Hsm);

        // Drive time through each session's own control surface: both must reach the same spy.
        composed.BTree.Pause();
        composed.Hsm.Pause();

        Assert.Equal(new[] { "RequestPause", "RequestPause" }, spy.Calls);
    }
}
