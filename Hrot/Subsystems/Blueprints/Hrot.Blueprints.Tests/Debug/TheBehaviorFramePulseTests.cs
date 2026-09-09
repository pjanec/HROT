using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Xunit;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94b</c>) — ONE behaviour-frame pulse for every host.</b>
///
/// <para>📄 <b>Design basis:</b> <c>Architect_Question_46…md</c> §2 rule 2b — the user's own
/// specification: <i>"the brain (cgf) does not tick ANY behavior when dt=0 so the tick source is not
/// dependent on behavior type."</i></para>
///
/// <para>⭐⭐ <b>Why these rails live HERE and not in <c>Fdp.Toolkits.Tests</c>, where the module's
/// existing order rail lives.</b> 📌 <c>DEBT-AIB-030</c>: that suite has <b>seven tests whose identity
/// ROTATES between runs</b>, so neither a red nor a green from it is evidence. ⇒ ⭐ the pulse's own
/// contract is railed in a suite that is actually gated. ⚠ The order assertion in
/// <c>CognitiveRuntimeModuleTests</c> was still updated — it would otherwise be stale — but it is
/// <b>not</b> what this batch relies on.</para>
///
/// <para>⛔⛔ <b>The <c>dt</c> gate is the entire contract</b>, and the second rail is the one that
/// matters: <c>ModuleHostKernel</c> advances the WORLD tick unconditionally, so a watch sampling on
/// that would clear its change highlight under a breakpoint — 📌 what Batch 68 refused.</para>
/// </summary>
public sealed class TheBehaviorFramePulseTests
{
    private static IEcsModuleSystem PulseSystem()
        => new CognitiveRuntimeModule(new BehaviorRegistry()).SimulationSystems.Last();

    /// <summary>
    /// ⭐⭐ The pulse is registered, and it is <b>LAST</b> — so "the counter moved" means
    /// <i>"a brain tick HAS RUN"</i>, ⛔ not <i>"one is about to"</i>.
    /// </summary>
    [Fact]
    public void ThePulseIsTheLastSystemInTheBehaviourPhase()
    {
        var systems = new CognitiveRuntimeModule(new BehaviorRegistry()).SimulationSystems;

        Assert.Equal("BehaviorFrameSystem", systems.Last().GetType().Name);
        Assert.Equal("CognitiveCleanupSystem", systems[systems.Count - 2].GetType().Name);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE contract: a frozen frame does not advance the pulse.</b>
    /// 🔴 Without this the counter would be the world tick, which ticks while paused.
    /// </summary>
    [Fact]
    public void AFrozenFrameDoesNotAdvanceThePulse()
    {
        var system = PulseSystem();
        uint before = BehaviorFrame.Current;

        system.Execute(view: null!, deltaTime: 0f);
        system.Execute(view: null!, deltaTime: -1f);

        Assert.Equal(before, BehaviorFrame.Current);
    }

    /// <summary>⭐ …and a real step does.</summary>
    [Fact]
    public void ASteppedFrameAdvancesThePulse()
    {
        var system = PulseSystem();
        uint before = BehaviorFrame.Current;

        system.Execute(view: null!, deltaTime: 0.016f);

        Assert.NotEqual(before, BehaviorFrame.Current);
    }

    /// <summary>
    /// ⚠ <b>Asserted as MOVEMENT, never as an absolute value</b> — the counter is process-global and
    /// monotonic, so a rail pinning an exact number would be order-dependent under parallel xunit
    /// collections. ⭐ Movement is all any reader asks: <c>BehaviorFrame</c> is an edge detector.
    /// </summary>
    [Fact]
    public void EachSteppedFrameMovesItAgain()
    {
        var system = PulseSystem();

        uint a = BehaviorFrame.Current;
        system.Execute(view: null!, deltaTime: 0.016f);
        uint b = BehaviorFrame.Current;
        system.Execute(view: null!, deltaTime: 0.016f);
        uint c = BehaviorFrame.Current;

        Assert.NotEqual(a, b);
        Assert.NotEqual(b, c);
    }

    /// <summary>
    /// ⛔ The system touches no entity and must not require a repository — ⭐ it is exempt from the
    /// <c>EntityRepository</c> cast every sibling performs, which is why <c>null!</c> above is a
    /// legitimate argument rather than a shortcut.
    /// </summary>
    [Fact]
    public void ThePulseSystemNeedsNoSimulationView()
    {
        var ex = Record.Exception(() => PulseSystem().Execute(view: null!, deltaTime: 0.016f));

        Assert.Null(ex);
    }
}
