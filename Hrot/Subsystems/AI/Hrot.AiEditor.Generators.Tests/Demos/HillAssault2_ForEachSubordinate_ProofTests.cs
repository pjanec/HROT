using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Events;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P1a (GAP-1) proof for the Hill-attack -&gt; Blueprints migration
/// (<c>docs/blueprints/P1_FlowForEach_Design.md</c>).
///
/// <para>
/// The committed blueprint
/// <c>Assets/Blueprints/HillAssault2_ForEachSubordinate_ClearBehavior.bp.json</c> (AiPrimitive,
/// Intent=Action, Hostings=[BTreeAction]) is a from-scratch proof asset for the new visually-native
/// <c>FlowForEachNode</c>: <c>EventEntry</c> -&gt; <c>FlowForEach</c> (SourceComponentFqn=
/// "Fdp.Core.CommandHierarchy.UnitRoster", curated <c>UnitRosterOps.Count</c>/<c>Subordinate</c>
/// accessors) whose <c>Body</c> exec-out -&gt; <c>PublishEvent</c>(EventId="ClearBehaviorEvent",
/// "Target" data-in pin wired FROM FlowForEach's <c>CurrentItem</c> out-pin -- so each subordinate,
/// NOT self, receives the event) -&gt; FlowForEach's <c>Completed</c> exec-out -&gt;
/// <c>Return(Success)</c>.
/// </para>
///
/// <para>
/// Architect ruling (Q#5-C, closing GAP-1 P1a): the loop lowers to an inline C# <c>for</c> over
/// <c>[0, Count)</c> -- NOT a BFS-scheduled block per iteration -- with a branch-free, latent-free
/// synchronous body scheduled as a nested statement list (see <c>IrOp_ForEach</c> and
/// <c>GraphScheduler.ScheduleFlowForEachNode</c>/<c>ScheduleBodyInline</c> in
/// <c>Stage5_Schedule.cs</c>). The roster's <c>fixed long[16]</c> buffer requires <c>unsafe</c>
/// access, so it never appears in the graph: <c>UnitRosterOps.Count(in UnitRoster)</c> /
/// <c>UnitRosterOps.Subordinate(in UnitRoster, int)</c> are the curated, reflection-free accessor
/// surface the node's baked <c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c> strings resolve to.
/// </para>
///
/// <para>
/// It is compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own
/// build (<c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2ForEachSubordinateClearBehavior_*_Bp.g.cs</c>),
/// which emits a <c>TickCore</c> whose body reads <c>UnitRoster</c> off self via
/// <c>GetComponentRO</c>, loops <c>for (int __iN = 0; __iN &lt; UnitRosterOps.Count(in __tM); __iN++)</c>,
/// and publishes <c>ClearBehaviorEvent{ Entity = UnitRosterOps.Subordinate(in __tM, __iN) }</c> on
/// <c>world.Bus</c> once per subordinate. Mirrors <c>HillAssault2_ClearBehavior_ProofTests</c>'s
/// reflection-based invocation style.
/// </para>
/// </summary>
public sealed class HillAssault2_ForEachSubordinate_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class
    /// (<c>Hrot.AI.Behaviors.Generated.HillAssault2ForEachSubordinateClearBehavior_*_Bp</c>) by name
    /// pattern rather than hardcoding the BlueprintId hash baked into the class name.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2ForEachSubordinateClearBehavior_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_ForEachSubordinate_ClearBehavior.bp.json must compile via the real Roslyn " +
            "source generator into a Hrot.AI.Behaviors.Generated.HillAssault2ForEachSubordinateClearBehavior_*_Bp class");
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
            ? System.IO.Directory.GetFiles(generatedDir, "HillAssault2ForEachSubordinateClearBehavior_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_ForEachSubordinate_ClearBehavior must exist under {generatedDir}");
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

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<UnitRoster>();
        return world;
    }

    [Fact]
    public void GeneratedTickCore_SourceContainsInlineForLoopOverCuratedUnitRosterOps()
    {
        var source = FindGeneratedSourceText();

        source.Should().Contain("for (",
            "FlowForEachNode must lower to an inline C# for loop (architect ruling Q#5-C), not a " +
            "BFS-scheduled block per iteration -- see generated TickCore below:\n" + source);
        source.Should().Contain("UnitRosterOps.Count(",
            "the loop bound must call the curated, reflection-free UnitRosterOps.Count(in UnitRoster) " +
            "accessor -- see generated TickCore below:\n" + source);
        source.Should().Contain("UnitRosterOps.Subordinate(",
            "the per-iteration item must be read via the curated UnitRosterOps.Subordinate(in " +
            "UnitRoster, int) accessor -- see generated TickCore below:\n" + source);
        source.Should().Contain("world.Bus.Publish",
            "the Body must lower to a world.Bus.Publish call (P4 PublishEvent reuse) -- see " +
            "generated TickCore below:\n" + source);
        source.Should().Contain(
            "GetComponentRO<global::Fdp.Core.CommandHierarchy.UnitRoster>",
            "the roster must be read off self via a reflection-free GetComponentRO<global::FQN> call " +
            "-- see generated TickCore below:\n" + source);
    }

    [Fact]
    public void GeneratedTickCore_PublishesOneClearBehaviorEventPerSubordinate_TargetingEachSubordinate_NotSelf()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        var sub1 = world.CreateEntity();
        var sub2 = world.CreateEntity();
        var sub3 = world.CreateEntity();

        var roster = new UnitRoster();
        UnitRoster.Add(ref roster, (long)sub1.PackedValue);
        UnitRoster.Add(ref roster, (long)sub2.PackedValue);
        UnitRoster.Add(ref roster, (long)sub3.PackedValue);
        world.AddComponent(commander, roster);

        var status = TickOnce(bpType, commander, world);

        status.Should().Be(Fbt.NodeStatus.Success,
            "FlowForEach is a plain exec node -- the graph unconditionally returns Success after the loop");

        // Publish() writes into the pending/write buffer; SwapBuffers() makes it visible via Read<T>().
        world.Bus.SwapBuffers();
        var events = world.Bus.Read<ClearBehaviorEvent>().ToArray();

        events.Length.Should().Be(3, "TickCore must publish exactly one ClearBehaviorEvent per subordinate");
        var targetedEntities = events.Select(e => e.Entity).ToArray();
        targetedEntities.Should().BeEquivalentTo(new[] { sub1, sub2, sub3 },
            "each event's Entity must be the subordinate wired via FlowForEach's CurrentItem out-pin, " +
            "NOT the commander (self)");
        targetedEntities.Should().NotContain(commander,
            "PublishEvent's Target pin is wired to CurrentItem, not left unwired -- it must never self-default");
    }

    [Fact]
    public void GeneratedTickCore_EmptyRoster_PublishesNoEvents_StillReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        world.AddComponent(commander, new UnitRoster()); // Count == 0

        var status = TickOnce(bpType, commander, world);

        status.Should().Be(Fbt.NodeStatus.Success,
            "an empty roster must still exec through Completed -> Return(Success)");

        world.Bus.SwapBuffers();
        var events = world.Bus.Read<ClearBehaviorEvent>();

        events.Length.Should().Be(0, "Count == 0 must skip the loop body entirely -- zero iterations, zero publishes");
    }
}
