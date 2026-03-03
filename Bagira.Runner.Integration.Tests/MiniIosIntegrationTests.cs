using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

public class MiniIosIntegrationTests
{
    private const int RequestTimeoutFrames      = 80;
    private const int SimHostSpawnTimeoutFrames = 120;
    private const int IgSpawnTimeoutFrames      = 120;
    private const int MissionTimeoutFrames      = 200;

    [Fact]
    public void MiniIosSpawn_RequestAllocatesAndPromotesEntity()
    {
        using var harness = new BagiraRunnerHarness();

        var igApp = harness.Ig.App;
        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var requestReader = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader = new DdsReader<CreateEntityAck>(observerParticipant, "CreateEntityAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        igApp.TestHook_SubmitMiniIosSpawn(tkbType, ForceId.Friend, 125f, 210f);

        CreateEntityRequest observedRequest = default;
        bool requestObserved = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(requestReader, out observedRequest),
            RequestTimeoutFrames);
        Assert.True(requestObserved, "CreateEntityRequest did not reach DDS in time.");
        Assert.NotNull(observedRequest.InitialDescriptors);
        Assert.True(
            HasMasterWithTkb(observedRequest.InitialDescriptors, tkbType),
            DescribeDescriptors("DDS", observedRequest.InitialDescriptors));

        CreateEntityAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => TryTakeCreateAck(ackReader, observedRequest.RequestId, out ack),
            RequestTimeoutFrames);
        Assert.True(ackObserved, "CreateEntityAck did not arrive in time.");
        Assert.Equal(0, ack.ErrorCode);
        Assert.True(ack.NewEntityId > 0, "CreateEntityAck did not include a valid network ID.");

        long networkId = ack.NewEntityId;

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

    private static bool TryTakeCreateAck(
        DdsReader<CreateEntityAck> reader,
        Guid requestId,
        out CreateEntityAck ack)
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
            else if (d._d == EDescriptorType.dtGeoSpatial)
            {
                summary.Append("(Lat=");
                summary.Append(d.GeoSpatial.Pos.Latitude);
                summary.Append(" Lon=");
                summary.Append(d.GeoSpatial.Pos.Longitude);
                summary.Append(" Alt=");
                summary.Append(d.GeoSpatial.Pos.Altitude);
                summary.Append(")");
            }
        }
        summary.Append("]");
        return summary.ToString();
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
    public void MiniIosSpawnWithWanderMission_CreatesEntityExecutesMissionAndPublishes()
    {
        using var harness = new BagiraRunnerHarness();

        var igApp = harness.Ig.App;
        using var observerParticipant   = new DdsParticipant((uint)harness.DomainId);
        using var missionReqReader      = new DdsReader<MissionControlRequest>(observerParticipant, "MissionControlRequest");
        using var missionAckReader      = new DdsReader<MissionControlAck>(observerParticipant, "MissionControlAck");
        using var entityMissionReader   = new DdsReader<EntityMission>(observerParticipant, "EntityMission");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // Kick off the async spawn+mission chain without blocking – the harness pumps DDS.
        _ = igApp.TestHook_SubmitMiniIosSpawnWithWanderMission(tkbType, ForceId.Friend, 100f, 200f);

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
            mission = data;
            return true;
        }

        mission = default;
        return false;
    }
}
