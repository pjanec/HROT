using System.Collections.Generic;
using System.Linq;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
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

    // ──────────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ P4-② — THE ZERO-FALLBACK RAIL
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Every BTree behaviour that production registers must bind EVERY node to a real
    /// delegate — zero <c>Failure</c> fallbacks.</b>
    ///
    /// <para><b>Why this rail exists.</b> <c>P4</c>-② bound <c>TBlackboard</c> to <c>byte</c>
    /// across the generators. Three runtime sites filter registrars by an exact
    /// <c>typeof(ActionRegistry&lt;byte, BTreeContext&gt;)</c> comparison and <c>continue</c> on a
    /// mismatch — <c>BTreeActionRegistryFactory.cs:64</c>, <c>BlueprintRegistrarScanner.cs:126</c>
    /// and <c>AiHotReloadCoordinator.cs:433</c>. <c>BTreeActionRegistryFactory</c> additionally
    /// swallows a throwing registrar. If the generators and those filters ever disagree,
    /// <c>RegisterAll</c> is <b>silently skipped</b>, <c>Interpreter.BindActions</c> substitutes a
    /// delegate that returns <c>NodeStatus.Failure</c>, and every behaviour ticks forever doing
    /// nothing. Nothing else catches it: the compiler cannot see a reflection filter, and the only
    /// runtime signal is one <c>Console.WriteLine</c> per key.
    /// </para>
    ///
    /// <para><b>Why it asks the interpreter and not the console.</b> Console output is the
    /// SYMPTOM; <c>Interpreter.UnboundMethodNames</c> is the DEFINITION — it is populated by the
    /// same branch that installs the fallback, so the rail cannot drift from the behaviour it
    /// guards.
    /// </para>
    ///
    /// <para><b>Why it scans rather than enumerating <c>FbtTreeCatalog</c>.</b> The keys a tree
    /// needs come from two producers: this assembly's <c>[FbtRegistrar]</c> AND each generated
    /// <c>[BlueprintRegistrar]</c>'s own <c>actionRegistry.Register</c> calls. Only the scan runs
    /// both, and only the scan enumerates exactly the trees production actually TICKS. A catalog
    /// entry that no registrar turns into a <c>BehaviorDefinition</c> is never interpreted, so it
    /// cannot fall back — <c>HideInCover_BT</c> and <c>HideInCover_BT_v2</c> are in that state
    /// (their selector-form <c>@offset</c> thunks live in the builder's own discarded registry).
    /// A newly-registered behaviour is covered automatically, with no list to keep in step.
    /// </para>
    /// </summary>
    [Fact]
    public void Scan_BindsEveryNode_ZeroFailureFallbacks()
    {
        var blueprintStaging = new BlueprintRegistryStaging();
        var behaviors        = new BehaviorRegistry();

        BlueprintRegistrarScanner.Scan(BehaviorsAssembly, blueprintStaging, behaviors);

        var names = behaviors.GetRegisteredNames();

        // ⛔ Guard the guard: an empty scan would make every assertion below vacuously true,
        //    which is exactly the shape of the failure this rail is meant to detect.
        names.Should().NotBeEmpty(
            "the scan must register this assembly's behaviours; an empty registry would make " +
            "the zero-fallback assertion vacuous");

        var interpreted = new List<string>();
        var unbound     = new List<string>();

        foreach (var name in names)
        {
            behaviors.TryGetId(name, out var id).Should().BeTrue();
            behaviors.TryGetDefinition(id, out var def).Should().BeTrue();
            if (def!.BTreeInterpreter is not { } interp) continue;   // HSM-tier behaviours

            interpreted.Add(name);
            foreach (var key in interp.UnboundMethodNames)
                unbound.Add($"{name} -> {key}");
        }

        interpreted.Should().NotBeEmpty(
            "at least one registered behaviour must be BTree-tier, or this rail guards nothing");

        unbound.Should().BeEmpty(
            "every node of every registered BTree must bind to a real delegate. A miss means a " +
            "registrar was skipped (a typeof(ActionRegistry<byte, BTreeContext>) filter " +
            "disagreeing with the generators, or a registrar body that threw and was swallowed) " +
            "and those nodes now return Failure forever:\n  " +
            string.Join("\n  ", unbound));
    }

    /// <summary>
    /// The negative control for the rail above: an interpreter built against an EMPTY registry
    /// must REPORT its misses. Without this, a bug that left <c>UnboundMethodNames</c> always
    /// empty would make <see cref="Scan_BindsEveryNode_ZeroFailureFallbacks"/> pass forever.
    /// </summary>
    [Fact]
    public void UnboundMethodNames_ReportsMisses_WhenRegistryIsEmpty()
    {
        var blob = new Fbt.BehaviorTreeBlob
        {
            TreeName    = "Unbound",
            Nodes       = new[] { new Fbt.NodeDefinition { Type = Fbt.NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { "Nobody.Registered.This@0" },
            FloatParams = System.Array.Empty<float>(),
            IntParams   = System.Array.Empty<int>(),
        };

        var interp = new Interpreter<byte, BTreeContext>(blob, new ActionRegistry<byte, BTreeContext>());

        interp.UnboundMethodNames.Should().ContainSingle()
            .Which.Should().Be("Nobody.Registered.This@0");
    }
}
