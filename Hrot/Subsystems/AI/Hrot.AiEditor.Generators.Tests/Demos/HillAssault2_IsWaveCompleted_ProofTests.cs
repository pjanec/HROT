using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P1b (Hill-attack -&gt; Blueprints migration, wave-core slice) proof for
/// <c>docs/blueprints/WaveCore_Slice_Design.md</c>, architect <c>Architect_Question_8_Wave_Core.md</c>.
/// Rebuilds the C# oracle <c>HillAttackCommanderNodes.Condition_IsWaveCompleted</c> (line 447-503) as a
/// visually-authored AiPrimitive BTreeAction (<c>Assets/Blueprints/HillAssault2_IsWaveCompleted.bp.json</c>,
/// Intent=Action so the graph can return the full Running/Success tri-state -- a Condition's bool wrapper
/// would collapse Running).
///
/// <para>
/// Graph: <c>EventEntry -&gt; SetVariable(Wave) [Value &lt;- FunctionCall WaveMonitorOps.Update(s &lt;-
/// GetVariable(Wave)) [IsPure, TrailingContext=View]] -&gt; Branch(Condition &lt;- Compare(FunctionCall
/// WaveMonitorOps.ActiveCount(s &lt;- the SAME Update.Return), Literal Int32 0, Equal)) -&gt; True:
/// Return(Success); False: Return(Running)</c>. The single <c>Update.Return</c> pin fans out to both the
/// SetVariable writeback and the ActiveCount read (pure data nodes fan out freely, proven by
/// <c>HillAssault2_RequestAreaQuery</c>), avoiding any re-read-before-write ordering hazard.
/// </para>
///
/// <para>
/// Compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build. Does not
/// modify the C# oracle or the compiler.
/// </para>
/// </summary>
public sealed class HillAssault2_IsWaveCompleted_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2IsWaveCompleted_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_IsWaveCompleted.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2IsWaveCompleted_*_Bp class");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2IsWaveCompleted_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_IsWaveCompleted must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection, threading WorkingState across ticks.</summary>
    private static Fbt.NodeStatus TickOnce(
        MethodInfo tickCore, object paramsInstance, ref object workingStateInstance,
        Entity self, EntityRepository world)
    {
        object?[] args = { paramsInstance, workingStateInstance, self, world, 0f };
        var result = tickCore.Invoke(null, args);
        workingStateInstance = args[1]!;   // WorkingState is a ref parameter -- Invoke writes the mutated struct back.
        return (Fbt.NodeStatus)result!;
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        return world;
    }

    /// <summary>
    /// Sets the boxed <c>WorkingState.Wave</c> field (a <see cref="WaveState"/>) via reflection --
    /// FieldInfo.SetValue on a boxed struct mutates the box directly, so nested-field writes made this
    /// way are visible to the next TickCore invocation.
    /// </summary>
    private static void SetWave(Type wsType, object ws, MemberSlotList runners, ushort burnedMask, ushort baselineReservedMask)
    {
        var waveField = wsType.GetField("Wave")!;
        var waveType = waveField.FieldType;
        object waveVal = Activator.CreateInstance(waveType)!;
        waveType.GetField("Runners")!.SetValue(waveVal, runners);
        waveType.GetField("BurnedSlotsMask")!.SetValue(waveVal, burnedMask);
        waveType.GetField("BaselineReservedMask")!.SetValue(waveVal, baselineReservedMask);
        waveField.SetValue(ws, waveVal);
    }

    private static (int Count, ushort Burned) ReadWave(Type wsType, object ws)
    {
        var waveVal = wsType.GetField("Wave")!.GetValue(ws)!;
        var waveType = waveVal.GetType();
        var runners = waveType.GetField("Runners")!.GetValue(waveVal)!;
        var count = (int)runners.GetType().GetField("Count")!.GetValue(runners)!;
        var burned = (ushort)waveType.GetField("BurnedSlotsMask")!.GetValue(waveVal)!;
        return (count, burned);
    }

    // ── Source-inspection: Update + ActiveCount curated calls ────────────────────────────────

    [Fact]
    public void GeneratedTickCore_SourceContainsWaveMonitorUpdateAndActiveCount()
    {
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("WaveMonitorOps.Update(",
            "the graph must route the whole WaveState bundle through the curated swap-remove kernel -- " +
            "see below:\n" + source);
        source.Should().Contain("WaveMonitorOps.ActiveCount(",
            "the Running/Success routing must read the post-Update active count -- see below:\n" + source);
    }

    // ── Behavioral: empty wave, dead runner, live-not-started runner ─────────────────────────

    [Fact]
    public void GeneratedTickCore_EmptyWave_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var p = Activator.CreateInstance(paramsType)!;
            object ws = Activator.CreateInstance(wsType)!;   // Wave defaults to zeroed (Runners.Count == 0).

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Success,
                "an empty Wave (no active runners) must complete immediately, matching the oracle's " +
                "s.ActiveAttackerCount == 0 fast path");
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public void GeneratedTickCore_DeadRunner_SwapRemoved_ReturnsSuccess_AndBurnsSlot()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var attacker = world.CreateEntity();
            world.DestroyEntity(attacker);   // dead before the tick

            var runners = MemberSlotListOps.AddRunner(default, attacker, firingSlot: 2, baselineSlot: 3);

            var p = Activator.CreateInstance(paramsType)!;
            object ws = Activator.CreateInstance(wsType)!;
            SetWave(wsType, ws, runners, burnedMask: 0, baselineReservedMask: 0);

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Success,
                "a dead runner must be swap-removed by WaveMonitorOps.Update, leaving Runners.Count == 0 " +
                "-> Success, matching the oracle's dead-tank cleanup path");

            var (count, burned) = ReadWave(wsType, ws);
            count.Should().Be(0, "the dead runner must be swap-removed from Wave.Runners");
            burned.Should().Be((ushort)0b100,
                "the dead runner's firing slot (2) must be permanently burned into BurnedSlotsMask");
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public void GeneratedTickCore_LiveRunner_NotYetStarted_ReturnsRunning_AndKeepsRunner()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var attacker = world.CreateEntity();   // alive, no BehaviorState added -- can't be observed as started

            var runners = MemberSlotListOps.AddRunner(default, attacker, firingSlot: 1, baselineSlot: 1);

            var p = Activator.CreateInstance(paramsType)!;
            object ws = Activator.CreateInstance(wsType)!;
            SetWave(wsType, ws, runners, burnedMask: 0, baselineReservedMask: 0);

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Running,
                "a live runner whose HullDownAttackRun behavior has not yet been observed (no " +
                "BehaviorState component) must keep the wave Running, matching the oracle's " +
                "'intent still propagating' path");

            var (count, _) = ReadWave(wsType, ws);
            count.Should().Be(1, "a live, not-yet-started runner must remain in Wave.Runners (no swap-remove)");
        }
        finally
        {
            world.Dispose();
        }
    }
}
