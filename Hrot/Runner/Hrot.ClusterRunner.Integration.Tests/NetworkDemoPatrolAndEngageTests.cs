using System;
using System.Threading;
using Fdp.Core;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.CGF;
using Hrot.CGF.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Common;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// NDEMO-IT: NetworkDemo "Patrol and Engage" distributed CQRS integration tests.
///
/// <para>
/// Validates the end-to-end scenario data flow described in the SDK Onboarding
/// QuickStart (Hrot.Examples.NetworkDemo): a Brain node spawns a patrol entity with
/// split authority delegated to a Muscle node, and a physical hit on the Muscle side
/// is propagated back to the Brain where authoritative damage is applied.
/// </para>
///
/// <para>
/// Phase 1 (split-authority spawn) and Phase 4 (authoritative damage) are exercised
/// here because they do not require the full ExCon MissionControlRequest round-trip.
/// Phase 2 (BTree NavigationIntent flow) and Phase 3 (AutonomousPerception reaction)
/// each depend on the complete doctrine-activation chain; those are documented as
/// separate skipped tests below.
/// </para>
///
/// <para>Domain range: 50-59 (below HrotRunnerHarness range which starts at 100).</para>
/// </summary>
public sealed class NetworkDemoPatrolAndEngageTests
{
    private static int _domainCounter = 49;

    private const int SpawnPropagationTimeoutMs    = 5_000;
    private const int AuthorityTimeoutMs           = 8_000;
    private const int BTreeNavigationTimeoutMs     = 12_000;
    private const int PumpSleepMs                  = 5;

    // PatrolAndEngage doctrine ID used in Phase 3 test (outside the CgfDoctrineIds range).
    private const int PatrolAndEngage_BT = 3099;

    // ── NDEMO-IT-1 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the full distributed CQRS flow for the demo scenario:
    ///
    /// <list type="bullet">
    ///   <item>Phase 1: Brain spawns entity with split authority; Muscle gains
    ///     <c>SimTransform</c> authority and Brain retains <c>Health</c> authority.</item>
    ///   <item>Phase 4: A <c>DetonationNotification</c> injected on the Muscle bus is
    ///     translated to <c>EntityHitDamage</c> over DDS and applied to the Brain's
    ///     authoritative <c>Health</c> component; <c>ActorCapabilities.CanMove</c> is
    ///     stripped when health reaches zero.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void NetworkDemo_PatrolAndEngage_ExecutesDistributedCqrsFlow()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // -- Phase 1: Split-Authority Spawn and Verification --
        //
        // Brain (CGF) spawns the patrol entity and delegates WorldPos + NavigationStatus
        // descriptors to the first Muscle node (SimHost node ID = 1).
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait until the Muscle has SimTransform authority AND the Brain has Health.
        bool entityReady = harness.PumpUntil(
            () =>
            {
                // Muscle must have SimTransform authority (WorldPos descriptor delegated).
                var simWorld = harness.SimHost.World;
                var simMap   = harness.SimHost.TestHook_EntityMap;
                if (simWorld == null) return false;
                if (!simMap.TryGetEntity(networkId, out Entity simEntity)) return false;
                if (!simWorld.IsAlive(simEntity)) return false;
                if (!simWorld.HasComponent<SimTransform>(simEntity)) return false;
                if (!simWorld.HasAuthority<SimTransform>(simEntity)) return false;

                // Brain must have Health authority (identity descriptor retained).
                var cgfMap = harness.Cgf!.GhostEntityMap;
                if (cgfMap == null) return false;
                if (!cgfMap.TryGetEntity(networkId, out Entity cgfEntity)) return false;
                if (!harness.Cgf.World!.IsAlive(cgfEntity)) return false;
                return harness.Cgf.World.HasComponent<Health>(cgfEntity);
            },
            AuthorityTimeoutMs / PumpSleepMs);

        Assert.True(entityReady,
            $"Phase 1: Muscle must have SimTransform authority and Brain must have Health " +
            $"for entity {networkId} within {AuthorityTimeoutMs} ms after split-authority spawn.");

        // Resolve ECS handles for assertions.
        harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simHostEntity);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntityHandle);

        float healthBefore = harness.Cgf.World!.GetComponent<Health>(cgfEntityHandle).Max;

        // Verify Brain does NOT have SimTransform authority (Muscle owns it).
        // The OwnershipUpdate from Muscle propagates back to CGF asynchronously, so pump
        // until it arrives (same pattern as SplitAuthoritySpawnTests IT-SA-3).
        bool cgfReleasedSimTransform = harness.PumpUntil(
            () =>
            {
                var cgfWorld = harness.Cgf!.World;
                var cgfMap   = harness.Cgf.GhostEntityMap;
                if (cgfWorld == null || cgfMap == null) return true;
                if (!cgfMap.TryGetEntity(networkId, out Entity e)) return true;
                if (!cgfWorld.IsAlive(e)) return true;
                if (!cgfWorld.HasComponent<SimTransform>(e)) return true;
                return !cgfWorld.HasAuthority<SimTransform>(e);
            },
            AuthorityTimeoutMs / PumpSleepMs);

        Assert.True(cgfReleasedSimTransform,
            "Phase 1: Brain must NOT hold SimTransform authority when WorldPos is delegated to Muscle.");

        // -- Phase 4: Physical Hit to Authoritative Damage --
        //
        // Inject a DetonationNotification on the Muscle bus, simulating the ballistic
        // CCD HitResolutionSystem detecting a physical impact.
        // DamageCalculationSystem (Muscle) emits DamageAssessedEvent;
        // DamageAssessedEgressTranslator broadcasts EntityHitDamage over DDS;
        // HealthApplicationSystem (Brain) applies the damage authoritatively.
        harness.SimHost.App.World.Bus.Publish(new DetonationNotification
        {
            Shooter = Entity.Null,
            Target  = simHostEntity,
            HitX    = 0f,
            HitY    = 0f,
            HitZ    = 0f,
        });

        bool brainTookDamage = harness.PumpUntil(
            () =>
            {
                if (!harness.Cgf!.World!.IsAlive(cgfEntityHandle)) return false;
                var health = harness.Cgf.World.GetComponent<Health>(cgfEntityHandle);
                return health.Current < healthBefore;
            },
            SpawnPropagationTimeoutMs / PumpSleepMs);

        Assert.True(brainTookDamage,
            "Phase 4: Brain Health must decrease after DetonationNotification injected on Muscle. " +
            "The DamageAssessedEgressTranslator -> EntityHitDamage -> HealthApplicationSystem pipeline " +
            "must route the hit from the Muscle to the Brain's authoritative Health component.");

        // -- Phase 5: Capability Loss Verification --
        //
        // HealthApplicationSystem strips ActorCapabilities.CanMove when health reaches zero,
        // which HsmDamageBridgeSystem uses to inject MobilityLost into the entity's HSM queue.
        if (harness.Cgf.World!.HasComponent<ActorCapabilityState>(cgfEntityHandle))
        {
            var caps = harness.Cgf.World.GetComponent<ActorCapabilityState>(cgfEntityHandle);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove),
                "Phase 5: CanMove must be stripped by HealthApplicationSystem after lethal damage " +
                "so that HsmDamageBridgeSystem can inject MobilityLost into the HSM queue.");
        }
    }

    // ── NDEMO-IT-2 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 2: Validates the Brain BTree → NavigationIntent → Muscle kinematics pipeline.
    ///
    /// <list type="bullet">
    ///   <item>The CGF BTreeTickSystem evaluates the WanderMilitary_BT doctrine on the
    ///     patrol entity and calls <c>Action_Wander</c>, which writes a MoveTo command to
    ///     <c>LocomotionChannel</c>.</item>
    ///   <item><c>MoveToExecutor.OnEnter</c> (ActionDispatchModule) writes
    ///     <c>NavigationIntent</c> on the CGF entity.</item>
    ///   <item><c>NavigationIntentEgressTranslator</c> detects the changed intent and
    ///     publishes it over the DDS loopback.</item>
    ///   <item><c>NavigationIntentBridgeSystem</c> on SimHost converts the received intent to
    ///     <c>NavState</c>, and <c>CarKinematicsSystem</c> begins updating
    ///     <c>SimTransform.Position</c>.</item>
    /// </list>
    ///
    /// <para>
    /// Doctrine activation is performed directly on the CGF entity's <c>DoctrineState</c>
    /// component, bypassing the ExCon <c>MissionControlRequest</c> chain.  This is the
    /// minimal test hook that still exercises the full BTree-to-kinematics pipeline.
    /// </para>
    /// </summary>
    [Fact]
    public void NetworkDemo_Phase2_BTreeNavigationIntent_FlowsToMuscle()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Spawn patrol entity: Brain (CGF) owns Health/Behavior/NavigationIntent,
        // Muscle (SimHost) owns SimTransform and NavigationStatus.
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait until SimHost has SimTransform authority and CGF has the entity ready.
        bool entityReady = harness.PumpUntil(
            () =>
            {
                var simMap   = harness.SimHost.TestHook_EntityMap;
                var simWorld = harness.SimHost.World;
                if (simWorld == null) return false;
                if (!simMap.TryGetEntity(networkId, out Entity simEntity)) return false;
                if (!simWorld.IsAlive(simEntity)) return false;
                if (!simWorld.HasComponent<SimTransform>(simEntity)) return false;
                if (!simWorld.HasAuthority<SimTransform>(simEntity)) return false;
                var cgfMap = harness.Cgf!.GhostEntityMap;
                if (cgfMap == null) return false;
                return cgfMap.TryGetEntity(networkId, out _);
            },
            AuthorityTimeoutMs / PumpSleepMs);

        Assert.True(entityReady,
            "Phase 2: Both sides must have the entity with correct authority before testing " +
            "the NavigationIntent flow.");

        harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simHostEntity);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);

        // Capture the initial SimTransform position on SimHost.
        var initialPos = harness.SimHost.World!.GetComponent<SimTransform>(simHostEntity).Position;

        // Directly activate WanderMilitary_BT on the CGF entity.
        // This bypasses the ExCon MissionControlRequest chain but exercises the full
        // BTreeTickSystem -> Action_Wander -> LocomotionChannel -> MoveToExecutor ->
        // NavigationIntent -> NavigationIntentEgressTranslator (DDS) ->
        // NavigationIntentIngressTranslator -> NavigationIntentBridgeSystem ->
        // NavState -> CarKinematicsSystem pipeline.
        var doctrine = harness.Cgf.World!.GetComponent<DoctrineState>(cgfEntity);
        doctrine.ActiveDoctrineHash = CgfDoctrineIds.WanderMilitary_BT;
        harness.Cgf.World.SetComponent(cgfEntity, doctrine);

        // Wait for SimTransform.Position to move; threshold > 0.1 m confirms
        // CarKinematicsSystem acted on the NavigationIntent delivered via DDS.
        bool entityMoved = harness.PumpUntil(
            () =>
            {
                var simWorld = harness.SimHost.World;
                if (simWorld == null || !simWorld.IsAlive(simHostEntity)) return false;
                var pos = simWorld.GetComponent<SimTransform>(simHostEntity).Position;
                return System.Numerics.Vector3.Distance(pos, initialPos) > 0.1f;
            },
            BTreeNavigationTimeoutMs / PumpSleepMs);

        Assert.True(entityMoved,
            "Phase 2: SimTransform.Position must change after CGF BTree (WanderMilitary_BT) " +
            "writes NavigationIntent, which crosses the DDS loopback to SimHost, where " +
            "NavigationIntentBridgeSystem converts it to NavState and CarKinematicsSystem " +
            "begins kinematic execution.");
    }

    // ── NDEMO-IT-3 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 3: Validates the asynchronous perception-to-engagement pipeline on the Brain.
    ///
    /// <list type="bullet">
    ///   <item>A custom <c>PatrolAndEngage_BT</c> doctrine is registered in the CGF
    ///     DoctrineRegistry.  The BTree has a Selector: when <c>TargetMemory.Count&gt;0</c>
    ///     (<c>Condition_HasTarget</c> succeeds) it engages via <c>Action_AimAndFire</c>;
    ///     otherwise it falls back to <c>Action_Wander</c>.</item>
    ///   <item><c>TargetMemory</c> is injected on the CGF entity (simulating the delivery
    ///     that <c>SensorTargetsIngressTranslator</c> would perform when the full
    ///     Brain-Muscle perception DDS pipeline is implemented).</item>
    ///   <item>After the injection, the BTree evaluates <c>Condition_HasTarget</c>
    ///     (returns Success), advances to <c>Action_AimAndFire</c>, and writes
    ///     <c>CombatConstants.ActionIdAimAndFire</c> to <c>WeaponChannel.ActiveAction</c>.</item>
    /// </list>
    ///
    /// <para>
    /// The full end-to-end path (AutonomousPerceptionModule on SimHost -> SensorTargets DDS ->
    /// Brain TargetMemory) is not yet implemented (the SensorTargets translator is a stub).
    /// This test demonstrates and validates the Brain-side reaction half of the pipeline.
    /// </para>
    /// </summary>
    [Fact]
    public void NetworkDemo_Phase3_PerceptionReaction_TargetMemoryPopulates()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Spawn patrol entity with split authority.
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for both sides to have the entity ready.
        bool entityReady = harness.PumpUntil(
            () =>
            {
                var simMap   = harness.SimHost.TestHook_EntityMap;
                var simWorld = harness.SimHost.World;
                if (simWorld == null) return false;
                if (!simMap.TryGetEntity(networkId, out Entity simEntity)) return false;
                if (!simWorld.IsAlive(simEntity)) return false;
                if (!simWorld.HasAuthority<SimTransform>(simEntity)) return false;
                var cgfMap = harness.Cgf!.GhostEntityMap;
                if (cgfMap == null) return false;
                return cgfMap.TryGetEntity(networkId, out _);
            },
            AuthorityTimeoutMs / PumpSleepMs);

        Assert.True(entityReady, "Phase 3: Patrol entity must be ready on both sides.");

        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);

        // -- Phase 3a: Register PatrolAndEngage_BT doctrine --
        // The BTree selector: if TargetMemory.Count > 0 engage via AimAndFire,
        // otherwise wander (fallback).
        harness.Cgf.TestHook_DoctrineRegistry!.Register(PatrolAndEngage_BT, "PatrolAndEngage",
            new DoctrineDefinition
            {
                Name             = "PatrolAndEngage",
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = BuildPatrolAndEngageInterpreter(),
            });

        // Activate the PatrolAndEngage doctrine on the CGF entity.
        var doctrine = harness.Cgf.World!.GetComponent<DoctrineState>(cgfEntity);
        doctrine.ActiveDoctrineHash = PatrolAndEngage_BT;
        unchecked { doctrine.InstanceId++; }
        harness.Cgf.World.SetComponent(cgfEntity, doctrine);

        // Pump a few frames: BTree should be Running in Action_Wander (no target yet).
        harness.PumpFrames(10);

        // -- Phase 3b: Inject TargetMemory on CGF entity --
        // Simulates the delivery that SensorTargetsIngressTranslator would perform once
        // the Brain-Muscle perception DDS pipeline is fully implemented.
        var targetMemory = new TargetMemory();
        TargetMemory.AddOrUpdateTarget(
            ref targetMemory,
            entityId:   1L,
            posX:       50f,
            posY:       50f,
            scoreBoost: 100f,
            tick:       1);
        harness.Cgf.World.SetComponent(cgfEntity, targetMemory);

        // -- Phase 3c: Wait for BTree to transition to AimAndFire --
        // Condition_HasTarget returns Success (Count > 0) -> Action_AimAndFire writes
        // ActionIdAimAndFire to WeaponChannel.ActiveAction.
        bool brainEngaging = harness.PumpUntil(
            () =>
            {
                var cgfWorld = harness.Cgf!.World;
                if (cgfWorld == null || !cgfWorld.IsAlive(cgfEntity)) return false;
                var channel = cgfWorld.GetComponent<WeaponChannel>(cgfEntity);
                return channel.ActiveAction == CombatConstants.ActionIdAimAndFire;
            },
            BTreeNavigationTimeoutMs / PumpSleepMs);

        Assert.True(brainEngaging,
            "Phase 3: After TargetMemory injection on the Brain entity, the PatrolAndEngage " +
            "BTree must evaluate Condition_HasTarget (Count > 0) -> Action_AimAndFire and " +
            "set WeaponChannel.ActiveAction = ActionIdAimAndFire on the CGF entity.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PumpBoth(HrotRunnerHarness harness, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            harness.PumpFrames(1);
            Thread.Sleep(PumpSleepMs);
        }
    }

    // PatrolAndEngage BTree JSON: Selector -> [Sequence[Condition_HasTarget, Action_AimAndFire], Action_Wander]
    private const string PatrolAndEngageJson = """
        {
          "TreeName": "PatrolAndEngage",
          "Root": {
            "Type": "Selector",
            "Children": [
              {
                "Type": "Sequence",
                "Children": [
                  { "Type": "Condition", "Action": "Condition_HasTarget" },
                  { "Type": "Action",    "Action": "Action_AimAndFire"   }
                ]
              },
              { "Type": "Action", "Action": "Action_Wander" }
            ]
          }
        }
        """;

    /// <summary>
    /// Builds the PatrolAndEngage BTree interpreter used in NDEMO-IT-3.
    /// The tree transitions from wander to aim-and-fire when TargetMemory is populated.
    /// </summary>
    private static unsafe Interpreter<BrainBlackboard, BTreeContext> BuildPatrolAndEngageInterpreter()
    {
        var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();

        // Condition: return Success when the entity's TargetMemory has at least one entry.
        registry.Register("Condition_HasTarget",
            (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int p) =>
            {
                if (!ctx.World.HasComponent<TargetMemory>(ctx.Self)) return NodeStatus.Failure;
                var tm = ctx.World.GetComponent<TargetMemory>(ctx.Self);
                return tm.Count > 0 ? NodeStatus.Success : NodeStatus.Failure;
            });

        // Action: write AimAndFire to WeaponChannel when a target is present.
        registry.Register("Action_AimAndFire",
            (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int p) =>
            {
                if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))  return NodeStatus.Failure;
                if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))   return NodeStatus.Failure;

                var tm = ctx.World.GetComponent<TargetMemory>(ctx.Self);
                if (tm.Count == 0) return NodeStatus.Failure;

                ref var ch = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);

                // Pack AimAndFireParams into the channel's inline buffer.
                fixed (byte* ptr = ch.Params)
                    *(AimAndFireParams*)ptr = new AimAndFireParams { Target = Entity.Null, CooldownSeconds = 0f };

                bool needsReactivation =
                    ch.ActiveAction != CombatConstants.ActionIdAimAndFire
                    || ch.Status == NodeStatus.Failure;
                if (needsReactivation)
                    unchecked { ch.ActionInstanceId++; }

                ch.ActiveAction = CombatConstants.ActionIdAimAndFire;
                return NodeStatus.Running;
            });

        // Fallback action: wander when no target is present.
        registry.Register("Action_Wander", CgfNodes.Action_Wander);

        var blob = TreeCompiler.CompileFromJson(PatrolAndEngageJson);
        return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
    }
}