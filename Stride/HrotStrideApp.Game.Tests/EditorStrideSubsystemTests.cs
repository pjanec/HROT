using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using HrotStrideApp;
using Hrot.Core.Network;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Integration tests for <see cref="EditorStrideSubsystem"/> (STR-P0-T6).
///
/// <para>
/// These tests run headlessly — no Stride GPU, no Raylib, no DDS.
/// They model the success conditions from the batch spec / TASK-DETAIL.md:
/// <list type="bullet">
///   <item>Boots headless without throwing; world/kernel/time-controller are non-null.</item>
///   <item>OrchestrationBus ≠ WorldBus (separate instances — design §8.1 invariant).</item>
///   <item>ClusterMaster releases bootstrap latch immediately (empty Mandatory); initial
///     cluster state is <c>Standby</c> (= <see cref="ClusterState.Idle"/>).</item>
///   <item>Spawning via the Brain path stamps <c>OwnerNodeId = 0</c> and the entity
///     carries <c>SimTransform</c> authority (`.WithOwned&lt;SimTransform&gt;()`) from
///     birth.</item>
///   <item>Pumping N frames after a spawn does not throw.</item>
/// </list>
/// </para>
/// </summary>
public sealed class EditorStrideSubsystemTests : IDisposable
{
    private readonly EditorStrideSubsystem _sut;

    public EditorStrideSubsystemTests()
    {
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();
    }

    public void Dispose() => _sut.Dispose();

    // ── Boot: headless without throwing ──────────────────────────────────

    /// <summary>
    /// After <see cref="EditorStrideSubsystem.Initialize"/>, the core objects
    /// are created and non-null.  This is the headless-boot success condition.
    /// </summary>
    [Fact]
    public void Initialize_CoreObjects_AreNonNull()
    {
        Assert.NotNull(_sut.World);
        Assert.NotNull(_sut.Kernel);
        Assert.NotNull(_sut.TimeController);
        Assert.NotNull(_sut.OrchestrationBus);
        Assert.NotNull(_sut.ClusterMaster);
        Assert.NotNull(_sut.ScenarioSource);
        Assert.NotNull(_sut.EntityMap);
    }

    // ── Separate bus invariant (design §8.1) ─────────────────────────────

    /// <summary>
    /// The orchestration bus must be a DIFFERENT <see cref="FdpEventBus"/> instance
    /// from the simulation world bus.  This is the §8.1 invariant that keeps
    /// orchestration events from polluting simulation event streams.
    /// </summary>
    [Fact]
    public void OrchestrationBus_IsDifferentInstance_FromWorldBus()
    {
        // Reference inequality — the two are distinct object instances.
        Assert.False(
            ReferenceEquals(_sut.OrchestrationBus, _sut.WorldBus),
            "OrchestrationBus must NOT be the same FdpEventBus instance as World.Bus");
    }

    // ── ClusterMaster latch + Standby (empty Mandatory) ──────────────────

    /// <summary>
    /// With <c>Mandatory = Array.Empty&lt;string&gt;()</c>, <see cref="Hrot.Orchestrator.ClusterMaster"/>
    /// releases its bootstrap latch immediately in its constructor and publishes
    /// <see cref="ClusterStateUpdateEvent"/> with <see cref="ClusterState.Idle"/>
    /// (the "Standby" state in the design).
    ///
    /// Observed by reading the native event after the first Tick's SwapBuffers:
    /// the constructor publishes to the pending buffer; Tick calls SwapBuffers
    /// which moves it to the active buffer, making it readable via <c>Read&lt;T&gt;()</c>.
    /// </summary>
    [Fact]
    public void ClusterMaster_EmptyMandatory_PublishesStandbyIdle_AfterFirstTick()
    {
        // The ClusterMaster constructor (with empty Mandatory) calls PublishStandby()
        // → PublishClusterState(ClusterState.Idle) synchronously. This writes to the
        // pending buffer of OrchestrationBus. After Tick swaps the buffer, we can read it.
        _sut.Tick(1f / 60f);

        // ClusterMaster.PublishClusterState uses PublishManaged (not Publish/native),
        // even though ClusterStateUpdateEvent is a struct.
        // ReadManaged<T> returns IEnumerable<T> from the managed event stream.
        var events = _sut.OrchestrationBus.ReadManaged<ClusterStateUpdateEvent>().ToList();

        Assert.True(events.Count > 0,
            "ClusterMaster (empty Mandatory) must publish at least one ClusterStateUpdateEvent.");

        // The first state published must be Idle (= design's "Standby").
        Assert.Equal(ClusterState.Idle, events[0].CurrentState);
    }

    // ── Owned-from-birth spawn via Brain path ─────────────────────────────

    /// <summary>
    /// Spawning an entity through the Brain path (enqueue an
    /// <see cref="EntityCreationRequest"/> with <c>OwnerAppInstanceId = 0</c>
    /// into <see cref="EditorStrideSubsystem.ScenarioSource"/>) and pumping two
    /// frames:
    /// <list type="bullet">
    ///   <item>Exactly one entity is alive in the world.</item>
    ///   <item>The entity carries <see cref="SimTransform"/> and is locally
    ///     authoritative — <c>World.HasAuthority&lt;SimTransform&gt;(entity)</c>
    ///     returns <c>true</c> — which is equivalent to
    ///     <c>.WithOwned&lt;SimTransform&gt;()</c> matching it.</item>
    /// </list>
    ///
    /// <para>
    /// In Mode 1 (offline, localNodeId = 0), <see cref="Fdp.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>
    /// with <c>localNodeId = 0</c> grants full authority instantly at spawn —
    /// no deferred handshake.  This is the core P0 invariant.
    /// </para>
    /// </summary>
    [Fact]
    public void BrainPathSpawn_EntityIsWithOwned_FromBirth()
    {
        // Enqueue a spawn request via the Brain source (tkbType 1 = "TestUnit").
        // Supply SimTransform as an InitialComponent so the entity carries it at birth;
        // with OwnerAppInstanceId=0 (=localNodeId=0) the NetworkSpawningSystem sets
        // authority on all initial components immediately.
        // BATCH-03 STR-D8: Use a real UrbanCombat TKB type (1001 = CivilianPedestrian)
        // now that EditorStrideSubsystem.Initialize registers the real UrbanCombat templates.
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,           // localNodeId = EditorNodeId = 0
            TkbType            = 1001L,        // CivilianPedestrian (UrbanCombat templates)
            InitialComponents  = new System.Collections.Generic.List<object>
            {
                new SimTransform { Position = new System.Numerics.Vector3(100f, 200f, 0f) }
            },
        });

        // Pump three frames:
        //   Frame 1 Input tick: CreateEntityRequestSystem drains ScenarioSource,
        //     queues PendingRequest; Simulation tick: ProcessPendingRequest publishes SpawnEntityCommand.
        //   Bus SwapBuffers makes SpawnEntityCommand active.
        //   Frame 2 Input tick: NetworkSpawningSystem (BeforeSync) consumes SpawnEntityCommand → entity created.
        //   Frame 3: entity is now fully live; authority is set by NetworkSpawningSystem.
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        // Verify exactly one entity was spawned.
        Assert.Equal(1, _sut.World.EntityCount);

        // Find the spawned entity via the QueryBuilder (public API).
        // WithOwned<SimTransform> selects entities that have SimTransform and local authority.
        Entity? maybeEntity = _sut.World.Query()
            .With<SimTransform>()
            .Build()
            .FirstOrNull();

        // The query must find the entity (has SimTransform).
        Assert.True(maybeEntity.HasValue,
            "Spawned entity must carry SimTransform and appear in the query.");

        var spawnedEntity = maybeEntity!.Value;

        // The entity must be locally authoritative for SimTransform —
        // equivalent to .WithOwned<SimTransform>() matching it.
        // This is the primary T6 invariant: localNodeId=0 grants authority instantly.
        Assert.True(_sut.World.HasAuthority<SimTransform>(spawnedEntity),
            "Spawned entity must be WithOwned<SimTransform> from birth (localNodeId=0).");

        // Also verify via the WithOwned query: only owned entities appear in WithOwned query.
        int ownedCount = _sut.World.Query()
            .WithOwned<SimTransform>()
            .Build()
            .Count();
        Assert.Equal(1, ownedCount);

        // BATCH-03 STR-D8: The spawned entity was created from a real UrbanCombat TKB template.
        // Verify the TkbDb contains the CivilianPedestrian template with a StrideRenderModelDefDto.
        Assert.True(_sut.TkbDb.TryGetByType(1001L, out var pedTemplate),
            "CivilianPedestrian TKB template (1001) must be registered (STR-D8).");
        var renderDef = pedTemplate.GetDescriptor<Fdp.Toolkit.Tkb.Domain.StrideRenderModelDefDto>();
        Assert.NotNull(renderDef);
        Assert.Equal("Models/mannequinModel", renderDef!.ModelAssetRef);
    }

    // ── Frame stability after spawn ───────────────────────────────────────

    /// <summary>
    /// Pumping 60 frames after a spawn does not throw.
    /// This exercises the full ECS pipeline (CGF + SimHost + orchestration)
    /// with a live entity present.
    /// </summary>
    [Fact]
    public void PumpSixtyFrames_AfterSpawn_DoesNotThrow()
    {
        // BATCH-03 STR-D8: Use a real UrbanCombat TKB type (2002 = InfantrySoldier).
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = 2002L,        // InfantrySoldier (UrbanCombat templates)
            InitialComponents  = new System.Collections.Generic.List<object>
            {
                new SimTransform()
            },
        });
        _sut.Tick(1f / 60f); // Frame 1: spawn command published
        _sut.Tick(1f / 60f); // Frame 2: entity materialised

        // Pump 60 more frames — no exceptions expected.
        for (int i = 0; i < 60; i++)
            _sut.Tick(1f / 60f);
    }
}

/// <summary>
/// Headless boot test for <see cref="EditorStrideSubsystem"/> in <b>hosted-editor mode</b>
/// (<c>hostRealEditor=true</c>, the <c>STRIDE_HOST_REAL_EDITOR=1</c> path).
///
/// <para>
/// Verifies that:
/// <list type="bullet">
///   <item>Construction and a few Ticks complete without exception (no GPU required —
///     physics body service defaults to <see cref="Hrot.Stride.Core.NoOpPhysicsBodyService"/>).</item>
///   <item><see cref="EditorStrideSubsystem.HostRealEditor"/> returns <c>true</c>.</item>
///   <item><see cref="EditorStrideSubsystem.World"/>, <c>Kernel</c>, <c>TimeController</c>,
///     and <c>ScenarioSource</c> are non-null (repointed to the real editor's objects).</item>
///   <item>Spawning via <see cref="EditorStrideSubsystem.ScenarioSource"/> materialises an
///     entity in the editor's World after a few Ticks.</item>
/// </list>
/// </para>
///
/// <para>
/// The OFF path (<c>hostRealEditor=false</c>) is covered by <see cref="EditorStrideSubsystemTests"/>;
/// this fixture covers only the ON path delta.
/// </para>
/// </summary>
public sealed class EditorStrideSubsystemHostedModeTests : IDisposable
{
    private readonly EditorStrideSubsystem _sut;

    public EditorStrideSubsystemHostedModeTests()
    {
        _sut = new EditorStrideSubsystem();
        // No visualFactory / physicsBodyService — headless, no GPU.
        _sut.Initialize(hostRealEditor: true);
    }

    public void Dispose() => _sut.Dispose();

    // ── SI-HM-1: Hosted mode boots headlessly ────────────────────────────

    /// <summary>
    /// The hosted-mode path (<c>hostRealEditor=true</c>) initialises and ticks 3 frames
    /// without throwing, in headless/CI mode (no GPU, no Bullet, no Raylib).
    /// </summary>
    [Fact]
    public void HostedMode_Initialize_AndTickThreeFrames_DoesNotThrow()
    {
        Assert.True(_sut.HostRealEditor, "HostRealEditor must be true when hostRealEditor=true was passed.");
        Assert.NotNull(_sut.World);
        Assert.NotNull(_sut.Kernel);
        Assert.NotNull(_sut.TimeController);
        Assert.NotNull(_sut.ScenarioSource);

        // Three ticks must complete without exception.
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
    }

    // ── SI-HM-2: Spawn via ScenarioSource in hosted mode ─────────────────

    /// <summary>
    /// Spawning an entity via <see cref="EditorStrideSubsystem.ScenarioSource"/> in hosted mode
    /// routes through the real <c>EditorSubsystem</c>'s spawn pipeline and materialises the
    /// entity in the shared <see cref="EditorStrideSubsystem.World"/> after a few ticks.
    /// </summary>
    [Fact]
    public void HostedMode_BrainPathSpawn_MaterialisesEntity_InSharedWorld()
    {
        // Enqueue a spawn via the (repointed) ScenarioSource.
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = 2002L,  // InfantrySoldier (UrbanCombat templates)
            InitialComponents  = new System.Collections.Generic.List<object>
            {
                new SimTransform { Position = System.Numerics.Vector3.Zero },
            },
        });

        // Drive 5 frames to let the spawn pipeline materialise the entity.
        for (int i = 0; i < 5; i++)
            _sut.Tick(1f / 60f);

        // The entity must appear in World (= the real editor's World).
        Assert.True(_sut.World.EntityCount > 0,
            "Hosted mode: entity spawned via ScenarioSource must appear in World after 5 ticks.");
    }
}
