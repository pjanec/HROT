using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Replication.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

// 🔴 MEASURED 2026-09-01: 1 red in the full suite, 5/5 PASS in isolation. It boots a real
//   HrotRunnerHarness cluster and had NO [Collection], so it was its own collection and nothing
//   serialised it against the other DDS-heavy collections. Joined to HeavyE2ETests — which is
//   exactly what it is — rather than inventing a fourth collection name.
[Collection("HeavyE2ETests")]
public class MiniExConIntegrationTests
{
    private const int RequestTimeoutFrames      = 80;
    private const int SimHostSpawnTimeoutFrames = 120;
    private const int IgSpawnTimeoutFrames      = 120;
    private const int MissionTimeoutFrames      = 200;

    [Fact]
    public void MiniExConSpawn_RequestAllocatesAndPromotesEntity()
    {
        using var harness = new HrotRunnerHarness();

        var igApp = harness.Ig.App;
        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var requestReader = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        igApp.TestHook_SubmitMiniExConSpawn(tkbType, ForceId.Friend, 125f, 210f);

        CreateEntityRequest observedRequest = default;
        bool requestObserved = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(requestReader, out observedRequest),
            RequestTimeoutFrames);
        Assert.True(requestObserved, "CreateEntityRequest did not reach DDS in time.");
        Assert.NotNull(observedRequest.InitialDescriptors);
        Assert.True(
            HasMasterWithTkb(observedRequest.InitialDescriptors, tkbType),
            DescribeDescriptors("DDS", observedRequest.InitialDescriptors));

        CreateUpdateDeleteEntityAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => RunnerTestHelpers.TryTakeCreateAck(ackReader, observedRequest.RequestId, out ack),
            RequestTimeoutFrames);
        Assert.True(ackObserved, "CreateUpdateDeleteEntityAck did not arrive in time.");
        Assert.True(ack.StatusCode < (int)NedStatusCode.UnknownDescriptorType,
            $"Expected InProgress or Success, got {ack.StatusCode}.");
        Assert.True(ack.EntityId > 0, "CreateUpdateDeleteEntityAck did not include a valid network ID.");

        long networkId = ack.EntityId;

        bool simHostSpawned = harness.PumpUntil(
            () => TryGetSimHostEntity(harness.SimHost.World, networkId, out _),
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostSpawned, "SimHost did not spawn the entity in time.");

        bool igPromoted = harness.PumpUntil(
            () => IgHasEntityWithNetworkIdAndLifecycle(harness.Ig.App.World, networkId, EntityLifecycle.Active),
            IgSpawnTimeoutFrames);
        Assert.True(igPromoted, "IG entity did not promote to Active in time.");
    }

    private static bool TryTakeAnyCreateRequest(
        DdsReader<CreateEntityRequest> reader,
        out CreateEntityRequest request)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            request = sample.Data;
            return true;
        }

        request = default;
        return false;
    }

    private static bool TryGetSimHostEntity(EntityRepository world, long networkId, out Entity entity)
    {
        var view = (ISimulationView)world;
        var query = world.Query().IncludeAll().With<NetworkIdentity>().Build();
        foreach (var candidate in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(candidate);
            if (id.Value == networkId)
            {
                entity = candidate;
                return true;
            }
        }

        entity = Entity.Null;
        return false;
    }

    private static bool IgHasEntityWithNetworkIdAndLifecycle(
        EntityRepository world,
        long networkId,
        EntityLifecycle lifecycle)
    {
        var view = (ISimulationView)world;
        var query = world.Query().With<NetworkIdentity>().WithLifecycle(EntityLifecycle.All).Build();
        foreach (var entity in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(entity);
            if (id.Value != networkId)
                continue;

            if (!world.IsAlive(entity))
                return false;

            return world.GetLifecycleState(entity) == lifecycle;
        }

        return false;
    }

    private static bool HasMasterWithTkb(List<EntityDescriptorUnion> descriptors, long tkbType)
    {
        for (int i = 0; i < descriptors.Count; i++)
        {
            var d = descriptors[i];
            if (d._d != EDescriptorType.dtEntityMaster) continue;
            if (d.EntityMaster.TkbType == tkbType) return true;
        }

        return false;
    }

    private static string DescribeDescriptors(string label, List<EntityDescriptorUnion> descriptors)
    {
        var summary = new System.Text.StringBuilder();
        summary.Append(label);
        summary.Append(" CreateEntityRequest descriptors: [");
        for (int i = 0; i < descriptors.Count; i++)
        {
            var d = descriptors[i];
            if (i > 0) summary.Append(", ");
            summary.Append(d._d);
            if (d._d == EDescriptorType.dtEntityMaster)
            {
                summary.Append("(TkbType=");
                summary.Append(d.EntityMaster.TkbType);
                summary.Append(")");
            }
            else if (d._d == EDescriptorType.dtWorldPos)
            {
                summary.Append("(Lat=");
                summary.Append(d.WorldPos.Pos.Latitude);
                summary.Append(" Lon=");
                summary.Append(d.WorldPos.Pos.Longitude);
                summary.Append(" Alt=");
                summary.Append(d.WorldPos.Pos.Altitude);
                summary.Append(")");
            }
        }
        summary.Append("]");
        return summary.ToString();
    }

    // ── Entity drag-end test ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the full entity drag-end flow:
    /// <list type="number">
    ///   <item>IG spawns an entity and waits for SimHost to acknowledge.</item>
    ///   <item>IG entity SimTransform is set to a new position (simulating what the
    ///         drag tool writes during mouse movement).</item>
    ///   <item>IG fires the drag-end hook, which sends
    ///         <see cref="UpdateEntityDescriptorRequest"/> (GeoSpatial) to DDS.</item>
    ///   <item>SimHost receives the request, verifies authority, updates
    ///         <see cref="SimTransform"/>, and replies with
    ///         <see cref="UpdateEntityDescriptorAck"/> (error code 0).</item>
    ///   <item>SimHost entity <see cref="SimTransform"/> is confirmed to be near the
    ///         target position within round-trip coordinate precision.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void EntityDragEnd_MovesEntityOnSimHost_ViaUpdateDescriptorRequest()
    {
        const int DragTimeoutFrames = 150;
        const float PositionTolerance = 1.0f; // metres — accounts for geodetic round-trip rounding

        using var harness = new HrotRunnerHarness();

        var igApp = harness.Ig.App;

        using var observerParticipant  = new DdsParticipant((uint)harness.DomainId);
        using var reqReader            = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader            = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");
        using var updateReqReader      = new DdsReader<UpdateEntityDescriptorRequest>(observerParticipant, "UpdateEntityDescriptorRequest");
        using var updateAckReader      = new DdsReader<UpdateEntityDescriptorAck>(observerParticipant, "UpdateEntityDescriptorAck");

        // ── 1. Spawn an entity via IG ─────────────────────────────────────────
        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        igApp.TestHook_SubmitMiniExConSpawn(tkbType, ForceId.Friend, 100f, 200f);

        CreateEntityRequest spawnReq = default;
        Assert.True(
            harness.PumpUntil(() => TryTakeAnyCreateRequest(reqReader, out spawnReq), RequestTimeoutFrames),
            "CreateEntityRequest did not reach DDS in time.");

        CreateUpdateDeleteEntityAck spawnAck = default;
        Assert.True(
            harness.PumpUntil(() => RunnerTestHelpers.TryTakeCreateAck(ackReader, spawnReq.RequestId, out spawnAck), RequestTimeoutFrames),
            "CreateUpdateDeleteEntityAck did not arrive in time.");
        Assert.Equal(0, spawnAck.StatusCode);

        long networkId = spawnAck.EntityId;
        Assert.True(networkId > 0, "Spawn did not return a valid network ID.");

        // ── 2. Wait for both sides to be ready ───────────────────────────────
        Assert.True(
            harness.PumpUntil(() => TryGetSimHostEntity(harness.SimHost.World, networkId, out _), SimHostSpawnTimeoutFrames),
            "SimHost did not spawn the entity in time.");

        Assert.True(
            harness.PumpUntil(() => IgHasEntityWithNetworkIdAndLifecycle(harness.Ig.App.World, networkId, EntityLifecycle.Active), IgSpawnTimeoutFrames),
            "IG entity did not promote to Active in time.");

        // ── 3. Simulate drag: set new Cartesian position on IG entity ─────────
        // The drag tool writes the drop position into SimTransform every frame.
        // We replicate that by setting the component directly before firing drag-end.
        var dropPosition = new Vector3(350f, 480f, 0f);
        igApp.TestHook_SetEntitySimTransform(networkId, new SimTransform { Position = dropPosition });

        // ── 4. Fire drag-end → IG publishes UpdateEntityDescriptorRequest ─────
        igApp.TestHook_SimulateDragEnd(networkId);

        // ── 5. Verify UpdateEntityDescriptorRequest is on DDS ─────────────────
        UpdateEntityDescriptorRequest updateReq = default;
        bool updateReqObserved = harness.PumpUntil(
            () => TryTakeUpdateRequest(updateReqReader, networkId, out updateReq),
            DragTimeoutFrames);
        Assert.True(updateReqObserved, "UpdateEntityDescriptorRequest did not reach DDS in time.");
        Assert.Equal(EDescriptorType.dtWorldPos, updateReq.DescriptorType);
        Assert.Equal((int)networkId, updateReq.EntityId);

        // ── 6. Verify SimHost acknowledges with success ───────────────────────
        UpdateEntityDescriptorAck updateAck = default;
        bool updateAckObserved = harness.PumpUntil(
            () => TryTakeUpdateAck(updateAckReader, updateReq.RequestId, out updateAck),
            DragTimeoutFrames);
        Assert.True(updateAckObserved, "UpdateEntityDescriptorAck did not arrive in time.");
        Assert.Equal(0, updateAck.ErrorCode);

        // ── 7. Verify SimHost entity SimTransform is near the drop position ────
        bool simHostPositionUpdated = harness.PumpUntil(
            () => SimHostEntityIsNearPosition(harness.SimHost.World, networkId, dropPosition, PositionTolerance),
            DragTimeoutFrames);
        Assert.True(simHostPositionUpdated,
            $"SimHost entity SimTransform was not updated to the expected drop position ({dropPosition:F1}) within the timeout.");
    }

    private static bool TryTakeUpdateRequest(
        DdsReader<UpdateEntityDescriptorRequest> reader,
        long networkId,
        out UpdateEntityDescriptorRequest request)
    {
        using var loan = reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            if (sample.Data.EntityId == (int)networkId)
            {
                request = sample.Data;
                return true;
            }
        }

        request = default;
        return false;
    }

    private static bool TryTakeUpdateAck(
        DdsReader<UpdateEntityDescriptorAck> reader,
        Guid requestId,
        out UpdateEntityDescriptorAck ack)
    {
        using var loan = reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            if (sample.Data.RequestId == requestId)
            {
                ack = sample.Data;
                return true;
            }
        }

        ack = default;
        return false;
    }

    private static bool SimHostEntityIsNearPosition(
        EntityRepository world,
        long networkId,
        Vector3 expectedPosition,
        float tolerance)
    {
        if (!TryGetSimHostEntity(world, networkId, out var entity))
            return false;

        var view = (ISimulationView)world;
        if (!view.HasComponent<SimTransform>(entity))
            return false;

        var actual = view.GetComponentRO<SimTransform>(entity).Position;
        var dist   = Vector3.Distance(actual, expectedPosition);
        return dist <= tolerance;
    }

    // ── Wander mission test ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies the full DDS-distributed spawn-with-mission flow:
    /// <list type="number">
    ///   <item>IG sends <see cref="CreateEntityRequest"/> and receives <see cref="CreateEntityAck"/>.</item>
    ///   <item>IG sends <see cref="MissionControlRequest"/> with CMD_REPLACE_MISSION / WanderMilitary.</item>
    ///   <item>SimHost acknowledges the mission request (<see cref="MissionControlAck"/> ErrorCode 0).</item>
    ///   <item>SimHost publishes <see cref="EntityMission"/> containing the WanderMilitary task.</item>
    ///   <item>SimHost entity is live in the ECS world.</item>
    ///   <item>IG promotes the entity to <see cref="EntityLifecycle.Active"/>.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void MiniExConSpawnWithWanderMission_CreatesEntityExecutesMissionAndPublishes()
    {
        using var harness = new HrotRunnerHarness();

        var igApp = harness.Ig.App;
        using var observerParticipant   = new DdsParticipant((uint)harness.DomainId);
        using var missionReqReader      = new DdsReader<MissionControlRequest>(observerParticipant, "MissionControlRequest");
        using var missionAckReader      = new DdsReader<MissionControlAck>(observerParticipant, "MissionControlAck");
        using var entityMissionReader   = new DdsReader<EntityMission>(observerParticipant, "EntityMission");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // Kick off the async spawn+mission chain without blocking – the harness pumps DDS.
        _ = igApp.TestHook_SubmitMiniExConSpawnWithWanderMission(tkbType, ForceId.Friend, 100f, 200f);

        // ── 1. MissionControlRequest must arrive on DDS ───────────────────────
        MissionControlRequest observedMissionReq = default;
        bool missionReqObserved = harness.PumpUntil(
            () => TryTakeMissionControlRequest(missionReqReader, out observedMissionReq),
            MissionTimeoutFrames);

        Assert.True(missionReqObserved, "MissionControlRequest did not reach DDS within the timeout.");
        Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, observedMissionReq.Payload._d);
        Assert.NotNull(observedMissionReq.Payload.FullMissionData.Tasks);
        Assert.Single(observedMissionReq.Payload.FullMissionData.Tasks);
        Assert.Equal("WanderMilitary", observedMissionReq.Payload.FullMissionData.Tasks[0].BehaviorId);

        // ── 2. MissionControlAck must arrive with ErrorCode 0 ─────────────────
        MissionControlAck observedMissionAck = default;
        bool missionAckObserved = harness.PumpUntil(
            () => TryTakeMissionControlAck(missionAckReader, observedMissionReq.RequestId, out observedMissionAck),
            MissionTimeoutFrames);

        Assert.True(missionAckObserved, "MissionControlAck did not arrive within the timeout.");
        Assert.Equal(0, observedMissionAck.ErrorCode);

        long entityId = observedMissionReq.TargetEntityId;
        Assert.True(entityId > 0, "MissionControlRequest TargetEntityId must be a valid network ID.");

        // ── 3. EntityMission must be published with WanderMilitary ───────────
        EntityMission observedEntityMission = default;
        bool entityMissionPublished = harness.PumpUntil(
            () => TryTakeEntityMission(entityMissionReader, entityId, out observedEntityMission),
            MissionTimeoutFrames);

        Assert.True(entityMissionPublished,
            $"EntityMission for entity {entityId} was not published within the timeout.");
        Assert.NotNull(observedEntityMission.Plan.Tasks);
        Assert.Single(observedEntityMission.Plan.Tasks);
        Assert.Equal("WanderMilitary", observedEntityMission.Plan.Tasks[0].BehaviorId);

        // ── 4. SimHost entity must exist in ECS ───────────────────────────────
        bool simHostSpawned = harness.PumpUntil(
            () => TryGetSimHostEntity(harness.SimHost.World, entityId, out _),
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostSpawned, $"SimHost did not have an entity with networkId={entityId}.");

        // ── 5. IG entity must be promoted to Active ───────────────────────────
        bool igPromoted = harness.PumpUntil(
            () => IgHasEntityWithNetworkIdAndLifecycle(harness.Ig.App.World, entityId, EntityLifecycle.Active),
            IgSpawnTimeoutFrames);
        Assert.True(igPromoted, $"IG entity networkId={entityId} did not promote to Active.");
    }

    private static bool TryTakeMissionControlRequest(
        DdsReader<MissionControlRequest> reader,
        out MissionControlRequest request)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            request = sample.Data;
            return true;
        }

        request = default;
        return false;
    }

    private static bool TryTakeMissionControlAck(
        DdsReader<MissionControlAck> reader,
        Guid requestId,
        out MissionControlAck ack)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var data = sample.Data;
            if (data.RequestId != requestId) continue;
            ack = data;
            return true;
        }

        ack = default;
        return false;
    }

    private static bool TryTakeEntityMission(
        DdsReader<EntityMission> reader,
        long entityId,
        out EntityMission mission)
    {
        using var loan = reader.Take(100); // EntityMission is per-entity keyed; read generous batch
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var data = sample.Data;
            if (data.EntityId != entityId) continue;
            if (data.Plan.Tasks == null || data.Plan.Tasks.Count == 0) continue; // wait for populated mission
            mission = data;
            return true;
        }

        mission = default;
        return false;
    }

	// ── Task-3 regression: hostile affiliation propagated from IG → SimHost → IG ──

	/// <summary>
	/// Regression test for Task-3: entity spawned by the Mini ExCon panel with a hostile
	/// affiliation must arrive in the IG ECS world with
	/// <see cref="Fdp.Core.EntityInfo.ForceId"/> == <see cref="ForceId.Hostile"/>.
	///
	/// Flow:
	/// <list type="number">
	///   <item><see cref="MiniExConPanelState.SubmitViaGateway"/> sends
	///         <see cref="CreateEntityRequest"/> with a <c>dtEntityInfo</c> descriptor
	///         carrying <c>FORCE_OPPOSING</c>.</item>
	///   <item>SimHost <see cref="CreateEntityRequestSystem"/> publishes the
	///         <see cref="EntityInfo"/> DDS topic after spawning the entity.</item>
	///   <item>IG <c>EntityInfoIngressTranslator</c> reads the topic and updates
	///         <see cref="Fdp.Core.EntityInfo.ForceId"/> on the matching ghost/active entity.</item>
	///   <item><see cref="StyleResolutionSystem"/> layer 1.5 applies hostile tint.</item>
	/// </list>
	/// </summary>
	[Fact]
    public void MiniExConSpawn_HostileAffiliation_IgEntityGetsHostileForceId()
    {
        using var harness = new HrotRunnerHarness();

        var igApp = harness.Ig.App;
        using var participant = new DdsParticipant((uint)harness.DomainId);
        using var reqReader   = new DdsReader<CreateEntityRequest>(participant, "CreateEntityRequest");
        using var ackReader   = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        igApp.TestHook_SubmitMiniExConSpawn(tkbType, ForceId.Hostile, 100f, 200f);

        // ── 1. Verify CreateEntityRequest contains dtEntityInfo with FORCE_OPPOSING ──
        CreateEntityRequest req = default;
        Assert.True(
            harness.PumpUntil(() => TryTakeAnyCreateRequest(reqReader, out req), RequestTimeoutFrames),
            "CreateEntityRequest did not reach DDS in time.");
        Assert.NotNull(req.InitialDescriptors);
        Assert.True(
            HasEntityInfoWithForce(req.InitialDescriptors, eForceIdentifier.FORCE_OPPOSING),
            DescribeDescriptors("Spawn request missing dtEntityInfo/FORCE_OPPOSING:", req.InitialDescriptors));

        // ── 2. Verify SimHost acknowledges ────────────────────────────────────
        CreateUpdateDeleteEntityAck ack = default;
        Assert.True(
            harness.PumpUntil(() => RunnerTestHelpers.TryTakeCreateAck(ackReader, req.RequestId, out ack), RequestTimeoutFrames),
            "CreateUpdateDeleteEntityAck did not arrive in time.");
        Assert.Equal(0, ack.StatusCode);

        long networkId = ack.EntityId;
        Assert.True(networkId > 0, "CreateUpdateDeleteEntityAck must return a valid network ID.");

        // ── 3. Verify IG entity has ForceId.Hostile ───────────────────────────
        bool igEntityHostile = harness.PumpUntil(
            () => IgEntityHasForceId(harness.Ig.App.World, networkId, ForceId.Hostile),
            IgSpawnTimeoutFrames);
        Assert.True(igEntityHostile,
            $"IG entity (networkId={networkId}) did not get ForceId.Hostile after Hrot.NED.Descriptors.EntityInfo propagation.");
    }

    private static bool HasEntityInfoWithForce(
        System.Collections.Generic.List<EntityDescriptorUnion> descriptors,
        eForceIdentifier force)
    {
        foreach (var d in descriptors)
        {
            if (d._d != EDescriptorType.dtEntityInfo) continue;
            if (d.EntityInfo.ForceIdentifier == force) return true;
        }
        return false;
    }

    private static bool IgEntityHasForceId(EntityRepository world, long networkId, ForceId forceId)
    {
        if (!TryGetSimHostEntity(world, networkId, out var entity))
            return false;
        var view = (ISimulationView)world;
        if (!view.HasComponent<Fdp.Core.EntityInfo>(entity))
            return false;
        ref readonly var data = ref view.GetComponentRO<Fdp.Core.EntityInfo>(entity);
        return data.ForceId == forceId;
    }

    // ── Task-8 regression: first spawn must not exhaust the ID pool ───────────

    /// <summary>
    /// Regression test for Task-8: the very first <see cref="CreateEntityRequest"/>
    /// on a freshly-initialised harness must succeed without throwing
    /// "ID pool exhausted".
    ///
    /// Before the fix <see cref="DdsIdAllocator"/> called <c>RequestChunk</c> in its
    /// constructor, before DDS participant discovery completed.  The server never saw
    /// the write-before-match request, so the first <see cref="AllocateId"/> spin timed
    /// out and threw.  The entity appeared to spawn successfully only on the second
    /// attempt (which used the response now sitting in the reader buffer).
    ///
    /// After the fix the chunk request is deferred to the first lazy
    /// <see cref="AllocateId"/> call, by which point discovery is complete.
    /// </summary>
    [Fact]
    public void FirstSpawn_DoesNotExhaustIdPool()
    {
        // Use a fresh harness so that this is genuinely the first AllocateId() call.
        using var harness = new HrotRunnerHarness();

        var igApp = harness.Ig.App;
        using var participant = new DdsParticipant((uint)harness.DomainId);
        using var reqReader   = new DdsReader<CreateEntityRequest>(participant, "CreateEntityRequest");
        using var ackReader   = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");

        // First spawn — must succeed.
        igApp.TestHook_SubmitMiniExConSpawn(TkbEntityTypes.Tank_M1Abrams, ForceId.Friend, 0f, 0f);

        CreateEntityRequest req = default;
        Assert.True(
            harness.PumpUntil(() => TryTakeAnyCreateRequest(reqReader, out req), RequestTimeoutFrames),
            "First CreateEntityRequest did not reach DDS — check DdsIdAllocator initialisation.");

        CreateUpdateDeleteEntityAck ack = default;
        Assert.True(
            harness.PumpUntil(() => RunnerTestHelpers.TryTakeCreateAck(ackReader, req.RequestId, out ack), RequestTimeoutFrames),
            "First CreateUpdateDeleteEntityAck did not arrive — ID pool may have been exhausted on the first request.");

        Assert.Equal(0, ack.StatusCode);
        Assert.True(ack.EntityId > 0, "First spawn must return a valid network ID.");
    }
}
