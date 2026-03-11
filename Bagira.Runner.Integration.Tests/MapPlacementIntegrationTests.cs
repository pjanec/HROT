using System.Numerics;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using Bagira.IOS.Services;
using Bagira.Map.Common;
using CycloneDDS.Runtime;
using FDP.Toolkit.DER;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

public class MapPlacementIntegrationTests
{
    private const int ConfigSyncTimeoutFrames = 100;
    private const int CreateRequestTimeoutFrames = 60;
    private const int SimHostSpawnTimeoutFrames = 100;
    private const int IgSpawnTimeoutFrames = 100;
    private const int IosSpawnTimeoutFrames = 60;

    [Fact]
    public void EndToEnd_PlacementFlow_SpawnsAndDistributesEntity()
    {
        using var harness = new BagiraRunnerHarness();

        var iosLogic = harness.Ios.Logic;
        var igApp = harness.Ig.App;
        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var requestReader = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader = new DdsReader<CreateEntityAck>(observerParticipant, "CreateEntityAck");

        int initialDerCount = CountDerEntities(iosLogic.Repo);
        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        iosLogic.StartPlacementMode(tkbType);
        Assert.Equal(tkbType, iosLogic.PlacementType);

        bool configSynced = harness.PumpUntil(
            () => iosLogic.ActiveContextId != Guid.Empty
               && igApp.TestHook_ActiveContextId == iosLogic.ActiveContextId,
            ConfigSyncTimeoutFrames);
        Assert.True(configSynced, "MapInteractionConfig did not reach IG in time.");

        igApp.TestHook_SimulateMapClick(new Vector2(100f, 200f));

        bool requestSent = harness.PumpUntil(
            () => HasPendingRequests(iosLogic.TransactionManager),
            CreateRequestTimeoutFrames);
        Assert.True(requestSent, "IOS did not track a CreateEntityRequest in time.");
        Assert.Equal(tkbType, iosLogic.PlacementType);

        CreateEntityRequest observedRequest = default;
        bool requestObserved = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(requestReader, out observedRequest),
            CreateRequestTimeoutFrames);
        Assert.True(requestObserved, "CreateEntityRequest did not reach DDS in time.");
        Assert.NotNull(observedRequest.InitialDescriptors);
        Assert.True(
            HasMasterWithTkb(observedRequest.InitialDescriptors, tkbType),
            DescribeDescriptors("DDS", observedRequest.InitialDescriptors));

        CreateEntityAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => TryTakeCreateAck(ackReader, observedRequest.RequestId, out ack),
            CreateRequestTimeoutFrames);
        Assert.True(ackObserved, "CreateEntityAck did not arrive in time.");
        Assert.Equal(0, ack.ErrorCode);

        bool simHostSpawned = harness.PumpUntil(
            () => harness.SimHost.World.EntityCount > 0,
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostSpawned, "SimHost did not spawn an entity in time.");

        bool simHostTkbMatched = harness.PumpUntil(
            () => SimHostHasEntityWithTkbType(harness.SimHost.World, tkbType),
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostTkbMatched, "SimHost entity did not have expected TkbType.");

        bool igSpawned = harness.PumpUntil(
            () => IgHasEntity(igApp.World),
            IgSpawnTimeoutFrames);
        Assert.True(igSpawned, "IG did not receive a spawned entity in time.");

        bool iosSpawned = harness.PumpUntil(
            () => IosHasEntityWithTkbType(iosLogic.Repo, tkbType),
            IosSpawnTimeoutFrames);
        Assert.True(iosSpawned, "IOS DER repo did not receive the spawned entity in time.");

        int finalDerCount = CountDerEntities(iosLogic.Repo);
        Assert.Equal(initialDerCount + 1, finalDerCount);
    }

    private static bool IgHasEntity(EntityRepository world)
    {
        var query = world.Query()
            .With<NetworkIdentity>()
            .With<ResolvedStyle>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();
        foreach (var _ in query)
            return true;

        return false;
    }

    private static bool IosHasEntityWithTkbType(IDerRepo repo, long tkbType)
    {
        foreach (var entity in repo.GetAllEntities())
        {
            if (entity.TkbType == tkbType)
                return true;
        }

        return false;
    }

    private static bool HasPendingRequests(IRequestTransactionManager txMgr)
    {
        foreach (var _ in txMgr.GetPendingRequests())
            return true;

        return false;
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

    private static bool SimHostHasEntityWithTkbType(EntityRepository world, long tkbType)
    {
        var query = world.Query().With<TkbIdentity>().Build();
        foreach (var entity in query)
        {
            var tkbId = world.GetComponent<TkbIdentity>(entity);
            if (tkbId.TkbType == tkbType)
                return true;
        }

        return false;
    }

    private static int CountDerEntities(IDerRepo repo)
    {
        int count = 0;
        foreach (var _ in repo.GetAllEntities())
            count++;

        return count;
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

    /// <summary>
    /// Verifies the direct <see cref="Bagira.IG.Tools.CreationTool"/> path:
    /// IOS starts placement mode  IG activates CreationTool  a direct
    /// left-click through the test hook  SimHost receives a valid
    /// <see cref="CreateEntityRequest"/> with geo-converted coordinates and
    /// acknowledges it by spawning an entity.
    ///
    /// This test exercises the code path that was broken before TASK-IF006:
    /// raw world-space metres were sent as geodetic degrees, producing
    /// out-of-range values that caused SimHost to reject the request silently.
    /// </summary>
    [Fact]
    public void EndToEnd_DirectCreationTool_SpawnsEntityInSimHost()
    {
        using var harness = new BagiraRunnerHarness();

        var iosLogic = harness.Ios.Logic;
        var igApp    = harness.Ig.App;

        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var requestReader = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader     = new DdsReader<CreateEntityAck>(observerParticipant, "CreateEntityAck");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // Ask IOS to enter placement mode  this publishes a MapInteractionConfig
        // that IG receives and activates CreationTool.
        iosLogic.StartPlacementMode(tkbType);

        bool toolActive = harness.PumpUntil(
            () => iosLogic.ActiveContextId != Guid.Empty
               && igApp.TestHook_ActiveContextId == iosLogic.ActiveContextId
               && igApp.TestHook_IsCreationToolActive,
            ConfigSyncTimeoutFrames);
        Assert.True(toolActive, "CreationTool did not become active in time.");

        // Simulate a left-click directly on the CreationTool (not via IOS).
        // Click at the local origin (0, 0) which the WGS84Transform maps to
        //  Berlin (52.52N, 13.405E)  coordinates within valid geodetic range.
        igApp.TestHook_DirectCreationToolClick(new Vector2(0f, 0f));

        // Verify the DDS request was written with valid geodetic coordinates.
        CreateEntityRequest observedRequest = default;
        bool requestObserved = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(requestReader, out observedRequest),
            CreateRequestTimeoutFrames);
        Assert.True(requestObserved, "CreateEntityRequest did not reach DDS in time.");
        Assert.NotNull(observedRequest.InitialDescriptors);
        Assert.True(
            HasMasterWithTkb(observedRequest.InitialDescriptors, tkbType),
            DescribeDescriptors("DDS", observedRequest.InitialDescriptors));

        // Before TASK-IF006 the lat/lon were raw world-space metres (e.g. 0 m
        // treated as 0, which is technically valid but would break at any non-
        // origin position). Verify the values are within geodetic bounds.
        var geoDescriptor = observedRequest.InitialDescriptors
            .First(d => d._d == EDescriptorType.dtGeoSpatial);
        double lat = geoDescriptor.GeoSpatial.Pos.Latitude;
        double lon = geoDescriptor.GeoSpatial.Pos.Longitude;
        Assert.InRange(lat, -90.0,  90.0);
        Assert.InRange(lon, -180.0, 180.0);

        // Verify SimHost acknowledged and spawned the entity.
        CreateEntityAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => TryTakeCreateAck(ackReader, observedRequest.RequestId, out ack),
            CreateRequestTimeoutFrames);
        Assert.True(ackObserved, "CreateEntityAck did not arrive in time.");
        Assert.Equal(0, ack.ErrorCode);

        bool simHostSpawned = harness.PumpUntil(
            () => SimHostHasEntityWithTkbType(harness.SimHost.World, tkbType),
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostSpawned, "SimHost did not spawn a entity via direct CreationTool click.");

        bool igSpawned = harness.PumpUntil(
            () => IgHasEntity(igApp.World),
            IgSpawnTimeoutFrames);
        Assert.True(igSpawned, "IG did not receive the spawned entity in time.");
    }
}