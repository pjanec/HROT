using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.StrideMock.Tests;

/// <summary>
/// Tests for SyncFdpToStrideScript covering all SC_SM004_x success conditions.
/// Uses an actual StrideNodeBootstrapper in headless mode with real ECS state.
/// </summary>
public sealed class SyncFdpToStrideScriptTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HrotNodeConfig HeadlessConfig() => new HrotNodeConfig
    {
        Headless             = true,
        SkipAllocatorRouting = true,
        SubsystemName        = "TestStride",
        NodeId               = 1,
        LocalTempRoot        = @"C:\FDP_Temp",
    };

    private static (StrideNodeBootstrapper bootstrapper, SyncFdpToStrideScript script)
        CreateBootstrappedScript()
    {
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new StrideNodeBootstrapper();
        bootstrapper.BootstrapNode(HeadlessConfig(), StrideNodeBootstrapper.Role, factory);

        var script = new SyncFdpToStrideScript(bootstrapper);
        script.Start();
        return (bootstrapper, script);
    }

    /// <summary>
    /// Transitions the cluster to the specified state by enqueueing a CommitState
    /// intent and advancing one tick so ClusterSlave.Tick() processes it.
    /// </summary>
    private static void SetClusterState(StrideNodeBootstrapper bootstrapper, ClusterState state)
    {
        bootstrapper.Context.ClusterSlave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(state),
        });
        bootstrapper.Tick(0f); // drives ClusterSlave.Tick() which processes the intent
    }

    /// <summary>
    /// Creates a live entity in the ECS world with a <see cref="SimTransform"/>
    /// at the given position.
    /// </summary>
    private static Entity SpawnEntityWithTransform(EntityRepository world,
        float x = 0f, float y = 0f)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(x, y, 0f),
            Rotation = Quaternion.Identity,
        });
        return entity;
    }

    // ── SC_SM004_1 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_1: A spawned ECS entity with SimTransform appears in ActiveEntities
    /// after a single Update call while the cluster is in OperatingLive.
    /// </summary>
    [Fact]
    public void SpawnedEntity_WithSimTransform_AppearsInActiveEntities_AfterUpdate()
    {
        var (boot, script) = CreateBootstrappedScript();

        SetClusterState(boot, ClusterState.OperatingLive);

        SpawnEntityWithTransform(boot.Context.World, x: 100f, y: 200f);

        script.Update(0f);

        Assert.Equal(1, script.ActiveEntities.Count());
    }

    // ── SC_SM004_2 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_2: Destroying an ECS entity causes it to be removed from
    /// ActiveEntities on the next Update call.
    /// </summary>
    [Fact]
    public void DestroyedEntity_RemovedFromActiveEntities_AfterUpdate()
    {
        var (boot, script) = CreateBootstrappedScript();

        SetClusterState(boot, ClusterState.OperatingLive);

        var entity = SpawnEntityWithTransform(boot.Context.World);
        script.Update(0f);
        Assert.Equal(1, script.ActiveEntities.Count());

        boot.Context.World.DestroyEntity(entity);
        script.Update(0f);

        Assert.Equal(0, script.ActiveEntities.Count());
    }

    // ── SC_SM004_3 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_3: After an entity is destroyed and a new entity is spawned (which
    /// may reuse the same index slot with a higher generation), the stale handle is
    /// detected via IsAlive and the dictionary holds exactly one entry for the new entity.
    /// </summary>
    [Fact]
    public void RecycledEntity_OldEntryRemoved_NewEntryCreated_GenerationalSafety()
    {
        var (boot, script) = CreateBootstrappedScript();

        SetClusterState(boot, ClusterState.OperatingLive);

        var old = SpawnEntityWithTransform(boot.Context.World);
        script.Update(0f);
        Assert.Equal(1, script.ActiveEntities.Count());

        // Destroy old and spawn a replacement — ECS may reuse the same index.
        boot.Context.World.DestroyEntity(old);
        var replacement = SpawnEntityWithTransform(boot.Context.World);

        script.Update(0f);

        // Exactly one entry must exist: the replacement (old generational handle is stale).
        Assert.Equal(1, script.ActiveEntities.Count());
        Assert.False(boot.Context.World.IsAlive(old));
        Assert.True(boot.Context.World.IsAlive(replacement));
    }

    // ── SC_SM004_4 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_4: While the cluster is in a loading state (LoadingLive),
    /// SyncStrideEntities is not called and CurrentStateMessage is non-empty.
    /// </summary>
    [Fact]
    public void LoadingState_SyncStrideEntities_NotCalled_SplashMessageNonEmpty()
    {
        var (boot, script) = CreateBootstrappedScript();

        // Default state after bootstrap is Idle (0). Transition to LoadingLive.
        SetClusterState(boot, ClusterState.LoadingLive);

        SpawnEntityWithTransform(boot.Context.World);
        script.Update(0f);

        // Sync must be suppressed — no entities in dictionary.
        Assert.Equal(0, script.ActiveEntities.Count());
        Assert.False(string.IsNullOrEmpty(script.CurrentStateMessage),
            "Expected a non-empty splash message during LoadingLive state.");
    }

    // ── SC_SM004_5 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_5: Once the cluster transitions to OperatingLive, sync resumes and
    /// the splash message is empty.
    /// </summary>
    [Fact]
    public void OperatingState_SyncResumes_SplashMessageEmpty()
    {
        var (boot, script) = CreateBootstrappedScript();

        SetClusterState(boot, ClusterState.LoadingLive);
        SpawnEntityWithTransform(boot.Context.World);
        script.Update(0f);
        Assert.Equal(0, script.ActiveEntities.Count()); // suppressed

        SetClusterState(boot, ClusterState.OperatingLive);
        script.Update(0f);

        Assert.Equal(1, script.ActiveEntities.Count());
        Assert.True(string.IsNullOrEmpty(script.CurrentStateMessage),
            "Expected empty message during OperatingLive state.");
    }

    // ── SC_SM004_6 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_6: A WeaponFireNotification published to the ECS event bus causes
    /// EventToEffectSystem (wired in SM-005) to spawn a VisualEffectState entity of
    /// type Tracer.  SyncFdpToStrideScript.Update then surfaces it in ActiveEffects.
    /// </summary>
    [Fact]
    public void WeaponFireNotification_ResultsInFakeStrideEffect_InActiveEffects()
    {
        var (boot, script) = CreateBootstrappedScript();

        // Transition to OperatingLive.
        SetClusterState(boot, ClusterState.OperatingLive);

        // Spawn shooter and target with positions so EventToEffectSystem can resolve them.
        var shooter = SpawnEntityWithTransform(boot.Context.World, x: 0f,    y: 0f);
        var target  = SpawnEntityWithTransform(boot.Context.World, x: 500f, y: 500f);

        // Publish to the ECS world bus (the same bus that ISimulationView.ReadEvents<T> reads from).
        boot.Context.World.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification
        {
            Shooter = shooter,
            Target  = target,
        });

        // One Tick to swap the world bus so EventToEffectSystem can see the event,
        // and EventToEffectSystem runs — spawning the tracer effect entity (command
        // buffer applied synchronously by the kernel during Simulation phase).
        boot.Tick(0f);

        // Script.Update queries the live world; OperatingLive means SyncStrideEffects runs.
        script.Update(0f);

        var effects = script.ActiveEffects.ToList();
        Assert.Equal(1, effects.Count);
        Assert.Equal(EffectType.Tracer, effects[0].Type);
    }

    // ── SC_SM004_7 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_7: After enough time elapses for a tracer effect to expire,
    /// VisualEffectCleanupSystem destroys it and the next script.Update removes it
    /// from ActiveEffects.
    /// </summary>
    [Fact]
    public void ExpiredEffect_RemovedFromActiveEffects_AfterCleanup()
    {
        var (boot, script) = CreateBootstrappedScript();

        SetClusterState(boot, ClusterState.OperatingLive);

        var shooter = SpawnEntityWithTransform(boot.Context.World, x: 0f,   y: 0f);
        var target  = SpawnEntityWithTransform(boot.Context.World, x: 100f, y: 100f);

        boot.Context.World.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification
        {
            Shooter = shooter,
            Target  = target,
        });

        // Tick 1: world bus SwapBuffers → EventToEffectSystem sees event → spawns tracer.
        boot.Tick(0f);
        script.Update(0f);
        Assert.Equal(1, script.ActiveEffects.Count()); // effect alive

        // Tick 2 with dt > TracerDurationSeconds (0.3 s): VisualEffectCleanupSystem
        // increments ElapsedTime past Duration → queues DestroyEntity on the live-world
        // command buffer.  Command buffers from PostSimulation global systems are flushed
        // at the start of the NEXT tick's BeforeSync phase, not immediately.
        boot.Tick(VisualEffectStateConstants.TracerDurationSeconds + 0.01f);

        // Tick 3 (any dt): BeforeSync flush applies the DestroyEntity → entity removed
        // from the live world.  The script's Pass-1 stale check then drops it from
        // ActiveEffects.
        boot.Tick(0f);
        script.Update(0f);

        Assert.Equal(0, script.ActiveEffects.Count());
    }

    // ── SC_SM004_8 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM004_8: The _staleEntities list is allocated once and reused across
    /// frames — no per-frame GC allocation. Verified by checking that the same
    /// list instance exists before and after multiple Update calls.
    /// </summary>
    [Fact]
    public void StaleEntitiesList_ReusedAcrossFrames_NoGcAlloc()
    {
        var (boot, script) = CreateBootstrappedScript();

        SetClusterState(boot, ClusterState.OperatingLive);

        var field = typeof(SyncFdpToStrideScript)
            .GetField("_staleEntities", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.NotNull(field);
        var listBefore = field.GetValue(script);

        // Several update cycles with entity churn.
        for (int i = 0; i < 5; i++)
        {
            var e = SpawnEntityWithTransform(boot.Context.World);
            script.Update(0f);
            boot.Context.World.DestroyEntity(e);
            script.Update(0f);
        }

        var listAfter = field.GetValue(script);

        // Must be the identical list instance — same reference.
        Assert.True(ReferenceEquals(listBefore, listAfter),
            "_staleEntities must be reused across frames (no replacement).");
    }
}
