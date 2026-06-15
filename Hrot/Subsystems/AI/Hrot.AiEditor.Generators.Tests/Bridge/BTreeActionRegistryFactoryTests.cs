using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using FluentAssertions;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// Proves PREREQ-A: the action registry that the BlueprintRegistrar scanner / hot-reload
/// coordinator inject into JSON BTree bridges is actually populated with the assembly's
/// real bound action/condition delegates (via the source-generated <c>[FbtRegistrar]</c>).
///
/// <para>
/// Before this fix the JSON bridges built an EMPTY <c>ActionRegistry</c>, so the interpreter
/// bound every method name to its <c>=&gt; NodeStatus.Failure</c> fallback — JSON-defined trees
/// could not execute any bound logic. This test asserts the registry now resolves a real
/// delegate for a known bound method (<c>CgfNodes.Action_Wander</c>, bound by CombatShowcase).
/// </para>
/// </summary>
public sealed class BTreeActionRegistryFactoryTests
{
    private static readonly System.Reflection.Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    [Fact]
    public void BuildFromAssembly_PopulatesRegistry_WithRealBoundActions()
    {
        var registry = BTreeActionRegistryFactory.BuildFromAssembly(BehaviorsAssembly);

        // The method name baked into CombatShowcase's blob; if the registry is empty the
        // interpreter would silently substitute the Failure fallback at Tick time.
        registry.TryGetAction("Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander", out var action)
            .Should().BeTrue(
                "the injected registry must carry real action delegates from [FbtRegistrar] " +
                "so JSON-defined BTrees execute their bound logic at runtime");
        action.Should().NotBeNull();
    }

    [Fact]
    public void BuildFromAssembly_ResolvesTypedConditionBridge_AtOffsetZero()
    {
        var registry = BTreeActionRegistryFactory.BuildFromAssembly(BehaviorsAssembly);

        // A DTO-param condition is registered by FbtActionRegistrar as an @0 bridge closure
        // (Unsafe.As projection of the blackboard's first bytes) — the VE-DEBT-002 mechanism.
        registry.TryGetAction("Hrot.AI.Behaviors.Brains.CgfNodes.Condition_TargetAliveAndVisible@0", out var cond)
            .Should().BeTrue(
                "typed DTO-param conditions resolve via their @offset bridge-closure key");
        cond.Should().NotBeNull();
    }
}
