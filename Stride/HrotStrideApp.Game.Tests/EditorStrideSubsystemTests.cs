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
/// These tests run headlessly — no Stride GPU, no Raylib, no DDS. They model the success
/// conditions from the batch spec / TASK-DETAIL.md:
/// <list type="bullet">
///   <item>Boots headless without throwing; world/kernel/time-controller are non-null.</item>
///   <item>Spawning via the Brain path stamps <c>OwnerNodeId = 0</c> and the entity carries
///     <c>SimTransform</c> authority (<c>.WithOwned&lt;SimTransform&gt;()</c>) from birth.</item>
///   <item>Pumping N frames after a spawn does not throw.</item>
/// </list>
/// </para>
///
/// <para>
/// ⭐⭐ <b>CE-209 / R-S9 — this fixture used to cover the SELF-CONTAINED (OFF) path, with a second
/// class below it for the hosted one.</b> The self-contained composition is retired, so there is
/// one mode and one fixture: this one. The hosted class was a straight duplicate of these claims
/// once both fixtures booted the same composition, so it is gone rather than kept beside this.
/// </para>
///
/// <para>
/// ⚠ Two claims went with the retired arm rather than being re-homed, and both already had a
/// better owner:
/// <list type="bullet">
///   <item><c>OrchestrationBus ≠ WorldBus</c> (design §8.1) — the bus belongs to the hosted
///     <c>EditorSubsystem</c> now, asserted by <c>EditorSubsystemBootTests</c> in
///     <c>Hrot.ClusterRunner.Integration.Tests</c>, and for ECS nodes by
///     <c>TheDebugProvidersDoNotUnderReportTests.AnEcsNodeDoesNotBuildASecondOrchestrationBus</c>.</item>
///   <item><c>ClusterMaster</c> publishes Standby/Idle on the first tick — same owner. This
///     subsystem has no ClusterMaster of its own left to assert about.</item>
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
        Assert.NotNull(_sut.ScenarioSource);
        // ⭐ CE-209: OrchestrationBus / ClusterMaster / EntityMap were the self-contained arm's own
        //   objects and are gone with it. HostedEditorLogic is what this composition now repoints to.
        Assert.NotNull(_sut.HostedEditor);
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

