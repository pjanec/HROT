using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P4 (GAP-3) proof for the Hill-attack → Blueprints migration
/// (<c>docs/blueprints/HillAssault_Blueprint_Migration.md</c>).
///
/// <para>
/// The committed blueprint <c>Assets/Blueprints/HillAssault2_ClearBehavior.bp.json</c> (AiPrimitive,
/// Intent=Action, Hostings=[BTreeAction]) is a from-scratch proof asset for the new visually-native
/// <c>PublishEventNode</c>: <c>EventEntry</c> (per-tick entry) → <c>PublishEvent</c>
/// (EventId="ClearBehaviorEvent", no "Target" pin wired → self-default) → <c>Return(Success)</c>.
/// </para>
///
/// <para>
/// Architect ruling (Q#5-A, closing GAP-3): the event is published via
/// <c>world.Bus.Publish(...)</c>, NOT the ECB — <c>ecb</c> is deliberately absent from the
/// AiPrimitive <c>TickCore</c> ABI, and a bus publish is not a structural (entity/component)
/// mutation, so it is the sanctioned path. See <c>IrOp_PublishBusEvent</c>
/// (Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs) and its <c>StatementEmitter</c> case,
/// which are distinct from the pre-existing ECB-based <c>IrOp_PublishEvent</c>.
/// </para>
///
/// <para>
/// It is compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own
/// build (<c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2ClearBehavior_*_Bp.g.cs</c>),
/// which emits <c>Params</c>/<c>WorkingState</c> as empty structs and a <c>TickCore</c> whose body
/// publishes <c>Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent{ Entity = self }</c> on
/// <c>world.Bus</c> and returns <c>NodeStatus.Success</c>. Mirrors
/// <c>HillAssault2_AbortEngagement_ProofTests</c>'s reflection-based invocation style (no
/// Parameters/WorkingState/shared-state dependency, so a host BTree buys nothing extra).
/// </para>
/// </summary>
public sealed class HillAssault2_ClearBehavior_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class (<c>Hrot.AI.Behaviors.Generated.HillAssault2ClearBehavior_*_Bp</c>)
    /// by name pattern rather than hardcoding the BlueprintId hash baked into the class name.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2ClearBehavior_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_ClearBehavior.bp.json must compile via the real Roslyn source generator into a " +
            "Hrot.AI.Behaviors.Generated.HillAssault2ClearBehavior_*_Bp class");
        return type!;
    }

    /// <summary>Returns the generated <c>.g.cs</c> source text for the compiled blueprint (source-inspection evidence).</summary>
    private static string FindGeneratedSourceText()
    {
        var generatedDir = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = System.IO.Directory.Exists(generatedDir)
            ? System.IO.Directory.GetFiles(generatedDir, "HillAssault2ClearBehavior_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_ClearBehavior must exist under {generatedDir}");
        return System.IO.File.ReadAllText(file!);
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
    public void GeneratedTickCore_SourceContainsWorldBusPublishOfClearBehaviorEvent()
    {
        var source = FindGeneratedSourceText();

        source.Should().Contain("world.Bus.Publish",
            "PublishEventNode must lower to world.Bus.Publish (architect Q#5-A), not ecb.PublishEvent");
        source.Should().Contain("global::Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent",
            "the catalog entry must resolve ClearBehaviorEvent's real FQN");
        source.Should().Contain("Entity = ",
            "the unwired \"Target\" pin must self-default, assigning self to the event's Entity field");
    }

    [Fact]
    public void GeneratedTickCore_PublishesClearBehaviorEventForSelf_AndReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = new EntityRepository();
        var self = world.CreateEntity();

        var status = TickOnce(bpType, self, world);

        status.Should().Be(Fbt.NodeStatus.Success,
            "PublishEvent is a plain exec node -- the graph unconditionally returns Success after publishing");

        // Publish() writes into the pending/write buffer; SwapBuffers() makes it visible via Read<T>().
        world.Bus.SwapBuffers();
        var events = world.Bus.Read<ClearBehaviorEvent>();

        events.Length.Should().Be(1, "TickCore must publish exactly one ClearBehaviorEvent per invocation");
        events[0].Entity.Should().Be(self, "the unwired \"Target\" pin must self-default to the ticking entity");
    }
}
