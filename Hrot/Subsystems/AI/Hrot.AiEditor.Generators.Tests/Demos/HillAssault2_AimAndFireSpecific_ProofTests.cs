using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Replication.Services;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Migration slice 3 proof (<c>docs/blueprints/AimAndFireSpecific_Slice_Design.md</c>).
///
/// <para>
/// The committed blueprint <c>Assets/Blueprints/HillAssault2_AimAndFireSpecific.bp.json</c>
/// (AiPrimitive, Intent=Action, Hostings=[BTreeAction]) is a from-scratch, blueprint-authored
/// rebuild of the C# oracle <c>HillAttackTankNodes.Action_AimAndFireSpecific</c> (~line 334-438) --
/// the first migration slice needing target-resolve (architect Q6-B) and persistent-state ammo
/// round-counting (native <c>Compare</c>/<c>BinaryOp</c>/<c>BooleanOp</c> over WorkingState).
/// </para>
///
/// <para>
/// Graph: <c>EventEntry</c> -&gt; <c>GetParameter</c>(TargetNetworkId) -&gt; <c>FunctionCall</c>
/// <see cref="NetworkEntityMapOps.ResolveTarget"/> [TrailingContext=View, IsPure] -&gt; target
/// <c>Entity</c>, fanned out to <see cref="WorldOps.IsNull"/> [TrailingContext=None] and
/// <see cref="WorldOps.IsAlive"/> [TrailingContext=View] -&gt; guard-chain <c>Branch</c>es -&gt;
/// Failure (unresolved) / Success (target destroyed) -&gt; ammo round-count sub-graph
/// (<c>GetComponent</c>(self, WeaponState).Ammo + native <c>Compare</c>/<c>BinaryOp</c>/
/// <c>BooleanOp</c> over the <c>RoundsFired</c>/<c>LastObservedAmmo</c> WorkingState counters) -&gt;
/// round-cap <c>Return(Success)</c> or <c>ChannelCommand</c>(WeaponChannel/AimAndFire) -&gt;
/// <c>WaitForChannel</c>(WeaponChannel) -&gt; <c>Return(Success)</c>. The ammo sub-graph's two nested
/// <c>Branch</c>es and the guard-chain / fire-tail Returns are AUTHORED NATURALLY (no node
/// triplication) -- several nodes are genuine merge points (exec in-degree &gt;= 2): the shared
/// <c>SetVariable(LastObservedAmmo=ammo)</c> (init path + decrement path), the round-cap
/// <c>Branch</c> (post-SetVariable path + BranchB.False path), and the shared
/// <c>Return(Success)</c> (target-destroyed / round-cap / fire-tail-success -- 3 incoming exec
/// edges). This exercises the scheduler's merge-point fix (a node reached by 2+ exec edges gets a
/// single shared block, no duplicate <c>goto</c> labels / CS0140).
/// </para>
///
/// <para>
/// DEVIATION 1 (design doc, documented): the oracle's Locomotion-channel clear (stop-to-fire) is
/// DROPPED -- no cross-channel write from this weapon action (architect Q5-B: a brain writes only
/// its own channel via <c>ChannelCommand</c>). DEVIATION 2 (design doc, documented): the oracle's
/// <c>ClearWeaponActionIfActive</c> on the MaxRounds-Success path is SIMPLIFIED to a plain
/// <c>Return(Success)</c> -- the BTree selector/arbitration reclaims the channel.
/// </para>
///
/// <para>
/// It is compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own
/// build (<c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2AimAndFireSpecific_*_Bp.g.cs</c>).
/// Drives the generated <c>TickCore</c> directly (bypassing the BTree/Blackboard1024 rail), mirroring
/// <see cref="HillAssault2_ReverseToBaseline_ProofTests"/>'s invocation style.
/// </para>
///
/// <para>
/// <b>Latent-phase note:</b> like <c>ReverseToBaseline</c>, the compiled <c>WaitForChannel</c>
/// lowering only re-runs the guard-chain/ammo sub-graph while <c>ws.__phase == 0</c> (the "issue the
/// command" entry pass); while <c>__phase == 1</c> (waiting), <c>TickCore</c> only inspects the
/// channel's <c>Status</c>. The behavioral ammo-drop test below therefore drives a SECOND
/// <c>__phase == 0</c> engagement pass by resetting <c>ws.__phase</c> directly between
/// <c>TickCore</c> calls (bypassing the BTree/Blackboard1024 rail exactly as the rest of this test
/// class does) -- a legitimate way to exercise the ammo round-count sub-graph a second time without
/// standing up the full BTree scheduler.
/// </para>
/// </summary>
public sealed class HillAssault2_AimAndFireSpecific_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2AimAndFireSpecific_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_AimAndFireSpecific.bp.json must compile via the real Roslyn source " +
            "generator into a Hrot.AI.Behaviors.Generated.HillAssault2AimAndFireSpecific_*_Bp class");
        return type!;
    }

    /// <summary>Returns the generated <c>.g.cs</c> source text for the compiled blueprint (source-inspection evidence).</summary>
    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2AimAndFireSpecific_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_AimAndFireSpecific must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<WeaponChannel>();
        world.RegisterComponent<WeaponState>();
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

    // ── Source-inspection ────────────────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_SourceContainsGuardChainAmmoArithmeticAndWeaponCommand()
    {
        var source = FindGeneratedSourceText();

        source.Should().Contain("NetworkEntityMapOps.ResolveTarget(",
            "the target-resolve guard must call the curated helper -- see generated TickCore below:\n" + source);
        source.Should().Contain("WorldOps.IsNull(",
            "the resolve-failure guard must call WorldOps.IsNull -- see generated TickCore below:\n" + source);
        source.Should().Contain("WorldOps.IsAlive(",
            "the liveness guard must call WorldOps.IsAlive -- see generated TickCore below:\n" + source);
        source.Should().Contain("GetComponentRO<global::Fdp.Toolkit.Combat.Components.WeaponState>",
            "ammo must be read via a reflection-free GetComponentRO<WeaponState> -- see generated TickCore below:\n" + source);
        source.Should().Contain(".Ammo",
            "the WeaponState.Ammo field must be read -- see generated TickCore below:\n" + source);
        source.Should().MatchRegex(@"__t\d+\s*-\s*__t\d+",
            "the ammo round-count subtraction (LastObservedAmmo - Ammo) must be emitted -- see generated TickCore below:\n" + source);
        source.Should().MatchRegex(@"__t\d+\s*\+\s*__t\d+",
            "the ammo round-count addition (RoundsFired + diff) must be emitted -- see generated TickCore below:\n" + source);
        source.Should().Contain(">=",
            "the round-cap GreaterThanOrEqual comparison must be emitted -- see generated TickCore below:\n" + source);
        source.Should().Contain("ws.RoundsFired",
            "RoundsFired must be read/written on WorkingState -- see generated TickCore below:\n" + source);
        source.Should().Contain("ws.LastObservedAmmo",
            "LastObservedAmmo must be read/written on WorkingState -- see generated TickCore below:\n" + source);
        source.Should().Contain("new global::Fdp.Toolkit.Combat.Executors.AimAndFireParams",
            "the fire tail must write an AimAndFireParams channel command -- see generated TickCore below:\n" + source);
        source.Should().Contain("Target =",
            "AimAndFireParams.Target must be wired from the resolved entity -- see generated TickCore below:\n" + source);
        source.Should().Contain("global::Fdp.Toolkit.Behavior.Components.WeaponChannel",
            "WaitForChannel must wait on WeaponChannel -- see generated TickCore below:\n" + source);
    }

    // ── Behavioral (headless) ────────────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_FirstTick_ResolvesTarget_IssuesAimAndFire_ReturnsRunning()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var self   = world.CreateEntity();
        var target = world.CreateEntity();

        var map = new NetworkEntityMap();
        map.Register(netId: 77, entity: target);
        world.SetSingletonManaged(map);

        world.AddComponent(self, new WeaponChannel());
        world.AddComponent(self, new WeaponState { Ammo = 30, MaxAmmo = 30 });

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("TargetNetworkId")!.SetValue(p, 77L);
        paramsType.GetField("MaxRounds")!.SetValue(p, 0);           // disabled cap
        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("LastObservedAmmo")!.SetValue(ws, -1);      // BTreeTick's InitDefaultWorkingState sentinel

        var status = TickOnce(tickCore!, p, ref ws, self, world);

        status.Should().Be(Fbt.NodeStatus.Running,
            "the first tick must resolve the target, pass both guards, run the ammo sub-graph's " +
            "first-tick init path, then issue AimAndFire and suspend at WaitForChannel");

        ref var chan = ref world.GetComponentRW<WeaponChannel>(self);
        chan.ActiveAction.Should().Be(CombatConstants.ActionIdAimAndFire,
            "the ChannelCommand write must set WeaponChannel.ActiveAction to the AimAndFire action id");

        unsafe
        {
            fixed (byte* paramSlot = chan.Params)
            {
                var fireParams = *(AimAndFireParams*)paramSlot;
                fireParams.Target.Should().Be(target,
                    "AimAndFireParams.Target must be the entity resolved via NetworkEntityMapOps.ResolveTarget");
                fireParams.CooldownSeconds.Should().Be(10f,
                    "CooldownSeconds must come from the ChannelCommand node's PinDefault");
            }
        }

        wsType.GetField("LastObservedAmmo")!.GetValue(ws).Should().Be(30,
            "the first-tick init path (LastObservedAmmo<0) must seed LastObservedAmmo from the current ammo");
        wsType.GetField("RoundsFired")!.GetValue(ws).Should().Be(0,
            "no shots have been observed yet -- RoundsFired must still be 0");
    }

    [Fact]
    public void GeneratedTickCore_AmmoDropOnNextEngagementPass_IncrementsRoundsFired()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var self   = world.CreateEntity();
        var target = world.CreateEntity();

        var map = new NetworkEntityMap();
        map.Register(netId: 7, entity: target);
        world.SetSingletonManaged(map);

        world.AddComponent(self, new WeaponChannel());
        world.AddComponent(self, new WeaponState { Ammo = 30, MaxAmmo = 30 });

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("TargetNetworkId")!.SetValue(p, 7L);
        paramsType.GetField("MaxRounds")!.SetValue(p, 0);           // disabled cap
        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("LastObservedAmmo")!.SetValue(ws, -1);

        // Pass 1: first-tick init (LastObservedAmmo seeded to 30) -> issues AimAndFire -> Running, __phase=1.
        TickOnce(tickCore!, p, ref ws, self, world).Should().Be(Fbt.NodeStatus.Running,
            "sanity: pass 1 must suspend at WaitForChannel after the first-tick init path");
        wsType.GetField("LastObservedAmmo")!.GetValue(ws).Should().Be(30);

        // Simulate 5 rounds fired by the muscle tier: ammo drops from 30 to 25.
        world.SetComponent(self, new WeaponState { Ammo = 25, MaxAmmo = 30 });

        // The compiled WaitForChannel lowering only re-runs the guard-chain/ammo sub-graph while
        // ws.__phase == 0 (see class doc "Latent-phase note"); manually reset __phase to simulate a
        // fresh engagement decision pass (the next time this action's entry block runs), bypassing
        // the BTree/Blackboard1024 rail exactly as TickOnce already does for Params/WorkingState.
        wsType.GetField("__phase")!.SetValue(ws, (byte)0);

        // Pass 2: ammo(25) < LastObservedAmmo(30) -> BranchB.True -> RoundsFired += (30-25) = 5.
        var status2 = TickOnce(tickCore!, p, ref ws, self, world);
        status2.Should().Be(Fbt.NodeStatus.Running,
            "pass 2 must re-issue AimAndFire (MaxRounds disabled) and suspend again");

        wsType.GetField("RoundsFired")!.GetValue(ws).Should().Be(5,
            "the ammo round-count sub-graph must compute RoundsFired += LastObservedAmmo - Ammo = 30 - 25 = 5");
        wsType.GetField("LastObservedAmmo")!.GetValue(ws).Should().Be(25,
            "LastObservedAmmo must be updated to the newly observed ammo count");
    }

    [Fact]
    public void GeneratedTickCore_RoundCapReached_ReturnsSuccess_WithoutIssuingWeaponCommand()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var self   = world.CreateEntity();
        var target = world.CreateEntity();

        var map = new NetworkEntityMap();
        map.Register(netId: 9, entity: target);
        world.SetSingletonManaged(map);

        world.AddComponent(self, new WeaponChannel());
        world.AddComponent(self, new WeaponState { Ammo = 30, MaxAmmo = 30 });

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("TargetNetworkId")!.SetValue(p, 9L);
        paramsType.GetField("MaxRounds")!.SetValue(p, 5);            // cap enabled
        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("LastObservedAmmo")!.SetValue(ws, 30);       // already initialized
        wsType.GetField("RoundsFired")!.SetValue(ws, 5);             // cap already reached

        TickOnce(tickCore!, p, ref ws, self, world).Should().Be(Fbt.NodeStatus.Success,
            "MaxRounds>0 && RoundsFired>=MaxRounds must short-circuit to Success (round cap reached), " +
            "matching the oracle's ClearWeaponActionIfActive+Success path (simplified per design doc DEVIATION 2)");

        ref var chan = ref world.GetComponentRW<WeaponChannel>(self);
        chan.ActiveAction.Should().Be((ushort)0,
            "the round-cap path must not issue AimAndFire -- WeaponChannel must remain untouched");
    }

    [Fact]
    public void GeneratedTickCore_UnresolvedTarget_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var self = world.CreateEntity();

        // NetworkEntityMap singleton present but empty -- TargetNetworkId=123 cannot resolve.
        world.SetSingletonManaged(new NetworkEntityMap());

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("TargetNetworkId")!.SetValue(p, 123L);
        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("LastObservedAmmo")!.SetValue(ws, -1);

        TickOnce(tickCore!, p, ref ws, self, world).Should().Be(Fbt.NodeStatus.Failure,
            "an unresolved TargetNetworkId (WorldOps.IsNull(target)) must return Failure, matching the " +
            "oracle's guard for a target that has not replicated yet or no NetworkEntityMap singleton");
    }

    [Fact]
    public void GeneratedTickCore_TargetDead_ReturnsSuccess_WithoutIssuingWeaponCommand()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = CreateWorld();
        var self   = world.CreateEntity();
        var target = world.CreateEntity();

        var map = new NetworkEntityMap();
        map.Register(netId: 5, entity: target);
        world.SetSingletonManaged(map);
        world.AddComponent(self, new WeaponChannel());
        world.AddComponent(self, new WeaponState { Ammo = 30, MaxAmmo = 30 });

        world.DestroyEntity(target);   // WorldOps.IsAlive(target) must now be false.

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("TargetNetworkId")!.SetValue(p, 5L);
        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("LastObservedAmmo")!.SetValue(ws, -1);

        TickOnce(tickCore!, p, ref ws, self, world).Should().Be(Fbt.NodeStatus.Success,
            "a resolved-but-dead target (WorldOps.IsAlive == false) must return Success, matching the " +
            "oracle's 'target destroyed' guard");

        ref var chan = ref world.GetComponentRW<WeaponChannel>(self);
        chan.ActiveAction.Should().Be((ushort)0,
            "the target-destroyed path must not issue AimAndFire -- WeaponChannel must remain untouched");
    }
}
