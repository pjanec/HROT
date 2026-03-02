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
    private const int RequestTimeoutFrames = 80;
    private const int SimHostSpawnTimeoutFrames = 120;
    private const int IgSpawnTimeoutFrames = 120;

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
}
