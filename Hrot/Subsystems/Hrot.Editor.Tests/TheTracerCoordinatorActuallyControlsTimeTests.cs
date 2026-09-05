using System.Collections.Generic;
using Fdp.Toolkit.Time;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.Debug;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// <b><c>AS-9</c> / <c>T4d</c> — control path D was a set of virtual NO-OPS.</b>
///
/// <para><c>AiTracerCoordinator.RequestPause</c>, <c>RequestContinue</c> and
/// <c>RequestStepOneTick</c> are <c>virtual</c> with empty bodies, and <c>EditorSubsystem</c>
/// constructed the BASE class. So a BTree or HSM tracer asking the simulation to stop did exactly
/// nothing — no exception, no log, no pause. The capability was built, documented and reachable,
/// and was simply never turned on.</para>
///
/// <para>This is the "the clock exists and nothing turns it on" shape the programme keeps hitting,
/// and the only thing that catches it is a rail asserting the WIRE, not the capability. A test that
/// called <c>RequestPause()</c> on the base class and asserted "no exception" would have passed
/// throughout.</para>
/// </summary>
public sealed class TheTracerCoordinatorActuallyControlsTimeTests
{
    private sealed class SpyTimeCommands : ITimeCommands
    {
        public readonly List<string> Calls = new();
        public void Pause()               => Calls.Add(nameof(Pause));
        public void Resume()              => Calls.Add(nameof(Resume));
        public void StepOneTick()         => Calls.Add(nameof(StepOneTick));
        public void SetTimeScale(float s) => Calls.Add($"{nameof(SetTimeScale)}({s})");
    }

    [Fact]
    public void TheEditorsCoordinator_ForwardsEveryRequest_ToTheCommandSurface()
    {
        var spy = new SpyTimeCommands();
        var coordinator = new EditorAiTracerCoordinator(spy);

        coordinator.RequestPause();
        coordinator.RequestStepOneTick();
        coordinator.RequestContinue();

        Assert.Equal(new[] { "Pause", "StepOneTick", "Resume" }, spy.Calls);
    }

    /// <summary>
    /// The base class is the thing that must NOT be constructed in production. Pinning its
    /// no-op-ness here states plainly why: nothing about calling it looks wrong.
    /// </summary>
    [Fact]
    public void TheBaseCoordinator_IsSilentlyInert_WhichIsWhyTheSubclassExists()
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
        Assert.IsNotType<EditorAiTracerCoordinator>(bare);
    }

    /// <summary>A coordinator with no command surface is a coordinator that cannot control time.</summary>
    [Fact]
    public void TheEditorsCoordinator_RefusesToBeBuiltWithoutACommandSurface()
        => Assert.Throws<System.ArgumentNullException>(() => new EditorAiTracerCoordinator(null!));
}
