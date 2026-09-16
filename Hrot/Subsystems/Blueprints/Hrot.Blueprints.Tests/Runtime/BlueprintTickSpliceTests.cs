using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Editor.Runtime;
// A sibling test folder claims the namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem,
// which shadows the TYPE name inside this namespace -- alias it instead of `using` the namespace.
using BpTick = Fdp.Toolkit.Blueprints.Systems.BlueprintTickSystem;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// FC-1·G2 (Q#20 review G2) -- the composition-order contract. <c>BlueprintTickSystem</c> declares
/// <c>[UpdateBefore]</c> the three action dispatchers, but module-group execution order is ARRAY
/// POSITION (the kernel does not re-apply ordering attributes inside a module's system list), and
/// both real compositions used to APPEND the tick after the dispatchers -- silently downgrading the
/// architect-approved Q#16-B "intent is read the same tick" fact to write-visible-next-tick. These
/// tests pin <see cref="BlueprintRuntimeWiring.SpliceIntoSimulation"/>, the single shared splice
/// both hosts (<c>EditorSubsystem</c>, <c>EditorHarness</c>) now use: the tick must land BEFORE
/// every system its own attributes name.
/// </summary>
public sealed class BlueprintTickSpliceTests
{
    private sealed class DummySystem : IEcsModuleSystem
    {
        public void Execute(Fdp.ModuleHost.Abstractions.ISimulationView view, float deltaTime) { }
    }

    private static BpTick MakeTick() => new(new BlueprintRegistry());

    [Fact]
    public void Splice_InsertsTickImmediatelyBeforeFirstDispatcher()
    {
        var tick = MakeTick();
        var loco        = new LocomotionDispatcherSystem();
        var weapon      = new WeaponDispatcherSystem();
        var interaction = new InteractionDispatcherSystem();
        var systems = new IEcsModuleSystem[]
        {
            new DummySystem(),        // pre-dispatch logic (mission control, cognition, ...)
            loco, weapon, interaction,
            new DummySystem(),        // post-dispatch logic (route context, ...)
        };

        var spliced = BlueprintRuntimeWiring.SpliceIntoSimulation(systems, tick);

        Assert.Equal(systems.Length + 1, spliced.Count);
        int tickIdx = spliced.IndexOf(tick);
        Assert.Equal(1, tickIdx);                            // right before the first dispatcher
        Assert.True(tickIdx < spliced.IndexOf(loco));
        Assert.True(tickIdx < spliced.IndexOf(weapon));
        Assert.True(tickIdx < spliced.IndexOf(interaction));
        Assert.Same(systems[0], spliced[0]);                 // everything else keeps its order
        Assert.Same(systems[^1], spliced[^1]);
    }

    [Fact]
    public void Splice_NoDispatchersInList_AppendsAtEnd()
    {
        var tick = MakeTick();
        var systems = new IEcsModuleSystem[] { new DummySystem(), new DummySystem() };

        var spliced = BlueprintRuntimeWiring.SpliceIntoSimulation(systems, tick);

        Assert.Equal(3, spliced.Count);
        Assert.Same(tick, spliced[^1]);   // degenerate composition: old append behavior
    }

    /// <summary>
    /// Pins the attribute-driven contract itself: the splice targets are read off
    /// <c>BlueprintTickSystem</c>'s own <c>[UpdateBefore]</c> declarations, which today name
    /// exactly the three dispatchers. If a dispatcher is added/renamed, this test points at the
    /// splice + both composition sites as the places to re-verify.
    /// </summary>
    [Fact]
    public void TickSystem_UpdateBeforeTargets_AreExactlyTheThreeDispatchers()
    {
        var targets = typeof(BpTick)
            .GetCustomAttributes(typeof(Fdp.Core.UpdateBeforeAttribute), inherit: false)
            .Cast<Fdp.Core.UpdateBeforeAttribute>()
            .Select(a => a.Target)
            .OrderBy(t => t.Name)
            .ToArray();

        Assert.Equal(new[]
        {
            typeof(InteractionDispatcherSystem),
            typeof(LocomotionDispatcherSystem),
            typeof(WeaponDispatcherSystem),
        }, targets);
    }
}
