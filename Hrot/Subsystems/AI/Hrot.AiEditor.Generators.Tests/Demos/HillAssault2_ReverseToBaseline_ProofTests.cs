using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Migration slice 2 proof (<c>docs/blueprints/ReverseToBaseline_Slice_Design.md</c>).
///
/// <para>
/// The committed blueprint <c>Assets/Blueprints/HillAssault2_ReverseToBaseline.bp.json</c> (AiPrimitive,
/// Intent=Action, Hostings=[BTreeAction]) is a from-scratch, blueprint-authored rebuild of the C# oracle
/// <c>HillAttackTankNodes.Action_ReverseToBaseline</c> (~line 456), using only shipped nodes
/// (<c>ChannelCommand</c>, <c>WaitForChannel</c>, <c>GetParameter</c>, <c>PublishEvent</c>,
/// <c>Return</c>) plus one small curated helper (<see cref="Hrot.AI.Behaviors.Brains.VectorOps.Vec3"/>)
/// for <c>Vector3</c> construction -- there is no vector-literal node. Graph: <c>EventEntry</c> -&gt;
/// <c>ChannelCommand</c>(LocomotionChannel/MoveTo) [explicit data-in pins Destination (wired from
/// <c>VectorOps.Vec3(GetParameter(BaselineX), GetParameter(BaselineY), Literal 0f)</c>),
/// Speed/ArrivalRadius/ReverseAllowed baked via PinDefaults ("12"/"5"/"1")] -&gt;
/// <c>WaitForChannel</c>(LocomotionChannel) [AiPrimitive latent lowering: issues the command once on the
/// first tick and always returns Running; on later ticks, Running while the channel is Running; on channel
/// <b>Success</b> continues on <c>Out</c> -&gt; <c>PublishEvent</c>(ClearBehaviorEvent, Target unwired =&gt;
/// self-default) -&gt; <c>Return(Success)</c>; on channel <b>Failure</b> takes the wired <c>OnFailure</c>
/// exec-out -&gt; <c>PublishEvent</c>(ClearBehaviorEvent) -&gt; <c>Return(Failure)</c>].
/// </para>
///
/// <para>
/// Q#13 UPDATE — deviation REMOVED: <c>WaitForChannel</c>'s <c>OnFailure</c> exec-out is now wired (the
/// architect-approved Q#13 failure split), so the blueprint publishes <c>ClearBehaviorEvent{Entity=self}</c>
/// on BOTH the Success and Failure paths, exactly matching the C# oracle. (Previously the failure path
/// auto-returned Failure without publishing — the accepted simplification documented in design doc §2.)
/// </para>
///
/// <para>
/// It is compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build
/// (<c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2ReverseToBaseline_*_Bp.g.cs</c>).
/// Mirrors <c>HillAssault2_ForEachSubordinate_ProofTests</c>'s reflection-based invocation style, driving
/// the generated <c>TickCore</c> directly (bypassing the BTree/Blackboard1024 rail, which contributes
/// nothing extra for this proof) across two ticks to exercise the latent suspend/resume: Tick 1 issues
/// the MoveTo command and returns Running (the wait's dispatch phase is 0, so it always suspends
/// unconditionally on the first pass -- see <c>WaitLowering_AiPrimitive</c>); Tick 2, after the test sets
/// <c>LocomotionChannel.Status = Fbt.NodeStatus.Success</c> to simulate the muscle tier reporting
/// arrival, resumes past the wait and returns Success having published exactly one
/// <c>ClearBehaviorEvent{Entity=self}</c>.
/// </para>
/// </summary>
public sealed class HillAssault2_ReverseToBaseline_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class
    /// (<c>Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_*_Bp</c>) by name pattern rather
    /// than hardcoding the BlueprintId hash baked into the class name.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2ReverseToBaseline_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_ReverseToBaseline.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_*_Bp class");
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
            ? System.IO.Directory.GetFiles(generatedDir, "HillAssault2ReverseToBaseline_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_ReverseToBaseline must exist under {generatedDir}");
        return System.IO.File.ReadAllText(file!);
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<LocomotionChannel>();
        return world;
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection, threading Params/WorkingState across ticks.</summary>
    private static Fbt.NodeStatus TickOnce(
        MethodInfo tickCore, object paramsInstance, ref object workingStateInstance, Entity entity, EntityRepository world)
    {
        object?[] args = { paramsInstance, workingStateInstance, entity, world, 0f };
        var result = tickCore.Invoke(null, args);
        workingStateInstance = args[1]!;   // WorkingState is a ref parameter -- Invoke writes the mutated struct back.
        return (Fbt.NodeStatus)result!;
    }

    [Fact]
    public void GeneratedTickCore_SourceContainsMoveToWriteVectorOpsAndClearBehaviorPublish()
    {
        var source = FindGeneratedSourceText();

        source.Should().Contain("new global::Fdp.Toolkit.Navigation.MoveToParams",
            "ChannelCommand must lower to a MoveToParams channel write -- see generated TickCore below:\n" + source);
        source.Should().Contain("ReverseAllowed",
            "the ReverseAllowed pin default (\"1\") must be baked into the MoveToParams initializer -- see generated TickCore below:\n" + source);
        source.Should().Contain("VectorOps.Vec3(",
            "the Destination pin must be built via the curated VectorOps.Vec3 helper (no vector-literal node exists) -- see generated TickCore below:\n" + source);
        source.Should().Contain("p.BaselineX",
            "BaselineX must be read via GetParameter (p.BaselineX) -- see generated TickCore below:\n" + source);
        source.Should().Contain("p.BaselineY",
            "BaselineY must be read via GetParameter (p.BaselineY) -- see generated TickCore below:\n" + source);
        source.Should().Contain("world.Bus.Publish(new global::Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent",
            "the success path must publish ClearBehaviorEvent via world.Bus.Publish -- see generated TickCore below:\n" + source);
    }

    [Fact]
    public void GeneratedTickCore_FirstTick_IssuesMoveToCommand_ReturnsRunning_BeforeChannelCompletes()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, default(LocomotionChannel));

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("BaselineX")!.SetValue(p, 10f);
        paramsType.GetField("BaselineY")!.SetValue(p, 20f);
        object ws = Activator.CreateInstance(wsType)!;

        var status = TickOnce(tickCore!, p, ref ws, entity, world);

        status.Should().Be(Fbt.NodeStatus.Running,
            "the first tick must issue the MoveTo command once (ChannelCommand) and then suspend " +
            "unconditionally at WaitForChannel (dispatch phase 0 always takes the entry path)");

        ref var chan = ref world.GetComponentRW<LocomotionChannel>(entity);
        chan.ActiveAction.Should().Be((ushort)1, "the ChannelCommand write must set LocomotionChannel.ActiveAction to the MoveTo action id");

        unsafe
        {
            fixed (byte* paramSlot = chan.Params)
            {
                var moveTo = *(Fdp.Toolkit.Navigation.MoveToParams*)paramSlot;
                moveTo.Destination.Should().Be(new System.Numerics.Vector3(10f, 20f, 0f),
                    "Destination must be built from VectorOps.Vec3(BaselineX, BaselineY, 0f)");
                moveTo.Speed.Should().Be(12f, "Speed must come from the ChannelCommand node's PinDefault");
                moveTo.ArrivalRadius.Should().Be(5f, "ArrivalRadius must come from the ChannelCommand node's PinDefault");
                moveTo.ReverseAllowed.Should().Be((byte)1, "ReverseAllowed must come from the ChannelCommand node's PinDefault");
            }
        }
    }

    [Fact]
    public void GeneratedTickCore_AfterChannelSucceeds_ReturnsSuccess_AndPublishesOneClearBehaviorEventTargetingSelf()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, default(LocomotionChannel));

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("BaselineX")!.SetValue(p, 10f);
        paramsType.GetField("BaselineY")!.SetValue(p, 20f);
        object ws = Activator.CreateInstance(wsType)!;

        // Tick 1: idle channel -> command issued -> Running (unconditional first-pass suspend).
        var tick1 = TickOnce(tickCore!, p, ref ws, entity, world);
        tick1.Should().Be(Fbt.NodeStatus.Running, "sanity: tick 1 must suspend at WaitForChannel");

        // Simulate the muscle tier reporting arrival.
        ref var chan = ref world.GetComponentRW<LocomotionChannel>(entity);
        chan.Status = Fbt.NodeStatus.Success;

        // Tick 2: WaitForChannel resumes past the wait -> PublishEvent -> Return(Success).
        var tick2 = TickOnce(tickCore!, p, ref ws, entity, world);
        tick2.Should().Be(Fbt.NodeStatus.Success,
            "tick 2 must resume past WaitForChannel once the channel reports Success and return Success");

        world.Bus.SwapBuffers();
        var events = world.Bus.Read<ClearBehaviorEvent>().ToArray();

        events.Length.Should().Be(1, "exactly one ClearBehaviorEvent must be published on the success path");
        events[0].Entity.Should().Be(entity,
            "PublishEvent's Target pin is left unwired -- it must self-default to the entity itself, " +
            "matching the oracle's ClearBehaviorEvent{Entity=self}");
    }

    [Fact]
    public void GeneratedTickCore_AfterChannelFails_ReturnsFailure_AndPublishesOneClearBehaviorEventTargetingSelf()
    {
        // Q#13-D: the WaitForChannel OnFailure exec-out is now wired -> PublishEvent(ClearBehaviorEvent)
        // -> Return(Failure). On channel Failure the blueprint must publish ClearBehaviorEvent (as the
        // oracle does on its failure path) and return Failure -- the removed deviation.
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, default(LocomotionChannel));

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("BaselineX")!.SetValue(p, 10f);
        paramsType.GetField("BaselineY")!.SetValue(p, 20f);
        object ws = Activator.CreateInstance(wsType)!;

        // Tick 1: idle channel -> command issued -> Running (unconditional first-pass suspend).
        var tick1 = TickOnce(tickCore!, p, ref ws, entity, world);
        tick1.Should().Be(Fbt.NodeStatus.Running, "sanity: tick 1 must suspend at WaitForChannel");

        // Simulate the muscle tier reporting FAILURE.
        ref var chan = ref world.GetComponentRW<LocomotionChannel>(entity);
        chan.Status = Fbt.NodeStatus.Failure;

        // Tick 2: WaitForChannel resumes onto the wired OnFailure chain -> PublishEvent -> Return(Failure).
        var tick2 = TickOnce(tickCore!, p, ref ws, entity, world);
        tick2.Should().Be(Fbt.NodeStatus.Failure,
            "tick 2 must resume onto the OnFailure chain when the channel reports Failure and return Failure");

        world.Bus.SwapBuffers();
        var events = world.Bus.Read<ClearBehaviorEvent>().ToArray();

        events.Length.Should().Be(1,
            "the OnFailure path must also publish exactly one ClearBehaviorEvent -- matching the oracle's " +
            "publish-on-both-paths (Q#13 removed the success-only deviation)");
        events[0].Entity.Should().Be(entity,
            "the OnFailure PublishEvent's Target pin is left unwired -- it must self-default to the entity itself");
    }
}
