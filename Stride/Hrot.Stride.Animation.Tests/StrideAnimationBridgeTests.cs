using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Xunit;

namespace Hrot.Stride.Animation.Tests;

/// <summary>
/// Headless tests for the editor_stride locomotion + montage bridge (BATCH-14, STR-P4-T3/T4).
/// These exercise the bridge's <b>logic</b> against a real <see cref="EntityRepository"/> and
/// the real <see cref="StrideAnimationBackend"/> with no <c>GraphicsDevice</c>:
/// <list type="bullet">
///   <item>register/unregister-on-appear/death reconciliation;</item>
///   <item>walk-speed <see cref="SimVelocity"/> → backend Walk blend, run-speed → Run, rest → Idle,
///     all driven <i>through the bridge</i> from <c>SimVelocity</c> (STR-P4-T3);</item>
///   <item>off-mesh-link Jump traversal → <c>PlayMontageOnSlot</c> + slot active, and the
///     Start→Loop→End sequencing reflected in the backend slot state (STR-P4-T4).</item>
/// </list>
/// </summary>
public class StrideAnimationBridgeTests
{
    // Mannequin class id (matches UrbanCombat InfantrySoldier); a non-animated class id for contrast.
    private const long AnimatedClass = 2002L;
    private const long NonAnimatedClass = 2001L;

    private static EntityRepository NewWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        return world;
    }

    private static StrideAnimationBridge NewBridge(StrideAnimationBackend backend)
        => new StrideAnimationBridge(
            backend,
            isAnimatedClass: t => t == AnimatedClass,
            jumpStartMontageId: 1001,
            jumpLoopMontageId: 1002,
            jumpEndMontageId: 1003);

    private static Entity SpawnMannequin(EntityRepository world, long tkbClass, Vector3 pos)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
        world.AddComponent(e, new SimVelocity());
        world.AddComponent(e, new TkbIdentity { TkbType = tkbClass });
        return e;
    }

    private static void SetPlanarVelocity(EntityRepository world, Entity e, float speed)
    {
        ref var v = ref world.GetComponentRW<SimVelocity>(e);
        // Drive along FDP +Y (north); planar speed magnitude = speed.
        v.Linear = new Vector3(0f, speed, 0f);
    }

    // ── STR-P4-T3: registration reconciliation ──────────────────────────────

    [Fact]
    public void Execute_RegistersAnimatedEntity_OnAppear()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        Assert.Equal(0, bridge.RegisteredCount);

        bridge.Execute(world, 1f / 60f);

        Assert.Equal(1, bridge.RegisteredCount);
        Assert.Equal(1, backend.SnapshotMetrics().ActiveEntityCount);
    }

    [Fact]
    public void Execute_DoesNotRegister_NonAnimatedClass()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        SpawnMannequin(world, NonAnimatedClass, Vector3.Zero);

        bridge.Execute(world, 1f / 60f);

        Assert.Equal(0, bridge.RegisteredCount);
        Assert.Equal(0, backend.SnapshotMetrics().ActiveEntityCount);
    }

    [Fact]
    public void Execute_UnregistersEntity_OnDeath()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);
        Assert.Equal(1, bridge.RegisteredCount);

        // Grab the handle, kill the entity, tick again → unregistered + handle stale.
        Assert.True(bridge.TryGetHandle(e, out var handle));
        world.DestroyEntity(e);

        bridge.Execute(world, 1f / 60f);

        Assert.Equal(0, bridge.RegisteredCount);
        Assert.Equal(0, backend.SnapshotMetrics().ActiveEntityCount);
        Assert.False(backend.TryResolve(handle, out _)); // handle invalidated on unregister
    }

    [Fact]
    public void Execute_RegistersOnce_AcrossMultipleTicks()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);
        bridge.Execute(world, 1f / 60f);
        bridge.Execute(world, 1f / 60f);

        Assert.Equal(1, bridge.RegisteredCount);
        Assert.Equal(1, backend.SnapshotMetrics().ActiveEntityCount);
    }

    // ── STR-P4-T3: SimVelocity → idle/walk/run blend, through the bridge ─────

    [Fact]
    public void Bridge_AtRest_ProducesIdleBlend()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero); // zero velocity
        bridge.Execute(world, 1f / 60f);

        Assert.True(bridge.TryGetHandle(e, out var handle));
        var loco = backend.QueryLocomotion(handle);

        Assert.Equal(1f, loco.Idle, 3);
        Assert.Equal(0f, loco.Walk, 3);
        Assert.Equal(0f, loco.Run, 3);
    }

    [Fact]
    public void Bridge_WalkSpeedVelocity_ProducesWalkDominantBlend()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f); // register first

        // WalkSpeed (1.5 m/s) → pure Walk per LocomotionBlend.
        SetPlanarVelocity(world, e, LocomotionBlend.WalkSpeed);
        bridge.Execute(world, 1f / 60f);

        Assert.True(bridge.TryGetHandle(e, out var handle));
        var loco = backend.QueryLocomotion(handle);

        Assert.Equal(1f, loco.Walk, 3);
        Assert.Equal(0f, loco.Idle, 3);
        Assert.Equal(0f, loco.Run, 3);
        Assert.True(loco.Walk > loco.Run && loco.Walk > loco.Idle);
    }

    [Fact]
    public void Bridge_IntermediateWalkSpeed_BlendsToward_Walk_NotRun()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);

        // Halfway between idle and walk → some Walk, some Idle, no Run.
        SetPlanarVelocity(world, e, 0.8f);
        bridge.Execute(world, 1f / 60f);

        Assert.True(bridge.TryGetHandle(e, out var handle));
        var loco = backend.QueryLocomotion(handle);

        Assert.Equal(0f, loco.Run, 3);
        Assert.True(loco.Walk > 0f && loco.Walk < 1f);
        Assert.True(loco.Idle > 0f && loco.Idle < 1f);
        Assert.Equal(1f, loco.Idle + loco.Walk + loco.Run, 3); // weights sum to 1
    }

    [Fact]
    public void Bridge_RunSpeedVelocity_ProducesRunDominantBlend()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);

        // RunSpeed (4.0 m/s) → pure Run.
        SetPlanarVelocity(world, e, LocomotionBlend.RunSpeed);
        bridge.Execute(world, 1f / 60f);

        Assert.True(bridge.TryGetHandle(e, out var handle));
        var loco = backend.QueryLocomotion(handle);

        Assert.Equal(1f, loco.Run, 3);
        Assert.Equal(0f, loco.Idle, 3);
        Assert.Equal(0f, loco.Walk, 3);
        Assert.True(loco.Run > loco.Walk && loco.Run > loco.Idle);
    }

    [Fact]
    public void Bridge_WalkThenRunThenRest_TransitionsThroughBlends()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);
        Assert.True(bridge.TryGetHandle(e, out var handle));

        SetPlanarVelocity(world, e, LocomotionBlend.WalkSpeed);
        bridge.Execute(world, 1f / 60f);
        Assert.True(backend.QueryLocomotion(handle).Walk > 0.99f);

        SetPlanarVelocity(world, e, LocomotionBlend.RunSpeed);
        bridge.Execute(world, 1f / 60f);
        Assert.True(backend.QueryLocomotion(handle).Run > 0.99f);

        SetPlanarVelocity(world, e, 0f);
        bridge.Execute(world, 1f / 60f);
        Assert.True(backend.QueryLocomotion(handle).Idle > 0.99f);
    }

    // ── STR-P4-T4: off-mesh-link traversal → jump montage ───────────────────

    [Fact]
    public void DispatchTraversal_Jump_StartsMontage_OnSlot()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f); // register
        Assert.True(bridge.TryGetHandle(e, out var handle));
        Assert.False(backend.IsAnySlotActive(handle)); // nothing playing yet

        bridge.DispatchTraversal(new OffMeshTraversalStartedEvent
        {
            Target = e,
            TraversalKind = TraversalKind.Jump,
        });

        // Jump_Start now playing on the slot.
        Assert.True(backend.IsAnySlotActive(handle));
        Assert.Equal(1001, backend.QuerySlotState(handle, 0).MontageHash); // jumpStart id
        Assert.Equal(1, bridge.ActiveJumpCount);
    }

    [Fact]
    public void DispatchTraversal_NonJumpKind_DoesNotStartMontage()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);
        Assert.True(bridge.TryGetHandle(e, out var handle));

        bridge.DispatchTraversal(new OffMeshTraversalStartedEvent
        {
            Target = e,
            TraversalKind = TraversalKind.Climb,
        });

        Assert.False(backend.IsAnySlotActive(handle));
        Assert.Equal(0, bridge.ActiveJumpCount);
    }

    [Fact]
    public void JumpSequence_AdvancesStart_Loop_End_ThenCompletes()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);
        bridge.Execute(world, 1f / 60f);
        Assert.True(bridge.TryGetHandle(e, out var handle));

        // Start the jump (Jump_Start on slot 0).
        bridge.TriggerJump(e);
        Assert.Equal(1001, backend.QuerySlotState(handle, 0).MontageHash);

        // The default montage duration is 1.0s; advancing past it lets the slot complete and
        // the bridge chain the next phase. Tick the bridge in 0.25s steps until the sequence
        // finishes, recording which montage ids were observed active.
        var observed = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 100 && bridge.ActiveJumpCount > 0; i++)
        {
            var slot = backend.QuerySlotState(handle, 0);
            if (slot.IsActive && (observed.Count == 0 || observed[^1] != slot.MontageHash))
                observed.Add(slot.MontageHash);
            bridge.Execute(world, 0.25f);
        }

        // All three jump phases were played in order, and the sequence finished.
        Assert.Contains(1001, observed); // Jump_Start
        Assert.Contains(1002, observed); // Jump_Loop
        Assert.Contains(1003, observed); // Jump_End
        Assert.Equal(new[] { 1001, 1002, 1003 }, observed);
        Assert.Equal(0, bridge.ActiveJumpCount);
    }

    [Fact]
    public void TriggerJump_UnregisteredEntity_IsNoOp()
    {
        var backend = new StrideAnimationBackend();
        var bridge = NewBridge(backend);
        var world = NewWorld();

        // Entity never registered (bridge.Execute not called).
        var e = SpawnMannequin(world, AnimatedClass, Vector3.Zero);

        bridge.TriggerJump(e);

        Assert.Equal(0, bridge.ActiveJumpCount);
    }
}
