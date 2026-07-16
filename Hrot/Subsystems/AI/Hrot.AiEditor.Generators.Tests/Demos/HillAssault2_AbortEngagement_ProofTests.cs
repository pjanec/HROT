using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Slice 0 (warm-up) proof for the Hill-attack → Blueprints migration
/// (<c>docs/blueprints/HillAssault_Blueprint_Migration.md</c>).
///
/// <para>
/// The committed blueprint <c>Assets/Blueprints/HillAssault2_AbortEngagement.bp.json</c> (AiPrimitive,
/// Intent=Action, Hostings=[BTreeAction]) is a from-scratch, blueprint-authored rebuild of the C#
/// oracle <c>HillAttackTankNodes.Action_AbortEngagement</c> (unconditionally returns
/// <see cref="Fbt.NodeStatus.Success"/>, the outer selector's trivial fallback). The C# oracle is left
/// untouched; this is a NEW asset under the working name <c>HillAssault2</c>.
/// </para>
///
/// <para>
/// Its graph is the smallest possible real node: <c>EventEntry</c> (empty EventTypeId, the per-tick
/// entry) linked straight to <c>Return(Success)</c> — no Parameters, WorkingState, Variables, or
/// GetShared, so it only exercises lowered node kinds (see the migration doc's "Safety-net findings"
/// table for the node kinds that are currently unimplemented no-ops).
/// </para>
///
/// <para>
/// It is compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build
/// (<c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2AbortEngagement_*_Bp.g.cs</c>),
/// which emits <c>Params</c>/<c>WorkingState</c> as empty structs and a <c>TickCore</c> whose body is
/// exactly <c>return NodeStatus.Success;</c>. Because there is no Parameters/WorkingState/shared-state
/// dependency, this test invokes the generated <c>TickCore</c> directly by reflection (mirroring
/// <c>T36_SharedStateGetSet_ProofTests.TickOnce</c>) rather than composing a host BTree — a host tree
/// buys nothing extra for an action with no cross-tick state to prove.
/// </para>
/// </summary>
public sealed class HillAssault2_AbortEngagement_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class (<c>Hrot.AI.Behaviors.Generated.HillAssault2AbortEngagement_*_Bp</c>)
    /// by name pattern rather than hardcoding the BlueprintId hash baked into the class name.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2AbortEngagement_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_AbortEngagement.bp.json must compile via the real Roslyn source generator into a " +
            "Hrot.AI.Behaviors.Generated.HillAssault2AbortEngagement_*_Bp class");
        return type!;
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection (ref Params/WorkingState are both empty structs for this node).</summary>
    private static Fbt.NodeStatus TickOnce(Type bpType, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        object?[] args =
        {
            Activator.CreateInstance(paramsType),
            Activator.CreateInstance(wsType),
            entity,
            world,
            0f,
        };
        var result = tickCore!.Invoke(null, args);
        return (Fbt.NodeStatus)result!;
    }

    [Fact]
    public void GeneratedTickCore_AlwaysReturnsSuccess_MatchingTheCSharpOracle()
    {
        var bpType = FindGeneratedBlueprintType();

        var world  = new EntityRepository();
        var entity = world.CreateEntity();

        TickOnce(bpType, entity, world).Should().Be(Fbt.NodeStatus.Success,
            "the blueprint-authored HillAssault2_AbortEngagement must unconditionally return Success, " +
            "matching the C# oracle Action_AbortEngagement (HillAttackTankNodes.cs)");

        world.Dispose();
    }
}
