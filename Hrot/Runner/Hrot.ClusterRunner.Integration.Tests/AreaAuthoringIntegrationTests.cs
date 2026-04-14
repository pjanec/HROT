using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.IG.Components;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

public class AreaAuthoringIntegrationTests
{
    private const int ConfigSyncTimeoutFrames   = 120;
    private const int CreateRequestTimeoutFrames = 120;
    private const int AckTimeoutFrames          = 120;
    private const int OverlayTimeoutFrames      = 180;
    private const float PositionTolerance       = 0.5f;

    [Fact]
    public void EndToEnd_AreaAuthoring_PublishesOverlayAndIgReceivesPolyline()
    {
        using var harness = new HrotRunnerHarness();

        var iosLogic = harness.ExCon.Logic;
        var igApp = harness.Ig.App;

        using var observerParticipant = new DdsParticipant((uint)harness.DomainId);
        using var requestReader = new DdsReader<CreateEntityRequest>(observerParticipant, "CreateEntityRequest");
        using var ackReader = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");
        using var overlayReader = new DdsReader<MapVisualOverlay>(observerParticipant, "MapVisualOverlay");

        iosLogic.StartAreaAuthoringMode();

        bool configSynced = harness.PumpUntil(
            () => iosLogic.ActiveContextId != System.Guid.Empty
               && igApp.TestHook_ActiveContextId == iosLogic.ActiveContextId
               && igApp.TestHook_IsPointSequenceToolActive,
            ConfigSyncTimeoutFrames);
        Assert.True(configSynced, "Area authoring config did not reach IG in time.");

        var points = new List<Vector2>
        {
            new Vector2(100f, 200f),
            new Vector2(150f, 220f),
            new Vector2(120f, 260f)
        };

        igApp.TestHook_DirectPointSequenceToolCommit(points);

        CreateEntityRequest observedRequest = default;
        bool requestObserved = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(requestReader, out observedRequest),
            CreateRequestTimeoutFrames);
        Assert.True(requestObserved, "CreateEntityRequest did not reach DDS in time.");
        Assert.NotNull(observedRequest.InitialDescriptors);
        Assert.True(
            HasMasterWithTkb(observedRequest.InitialDescriptors, TkbEntityTypes.TacGraphic_Area),
            DescribeDescriptors(observedRequest.InitialDescriptors));
        // Area entities now carry a GeoSpatial (centroid reference point) in the CreateEntityRequest.
        Assert.True(
            HasGeoSpatialDescriptor(observedRequest.InitialDescriptors),
            DescribeDescriptors(observedRequest.InitialDescriptors));
        Assert.True(
            HasOverlayWithPointCount(observedRequest.InitialDescriptors, points.Count),
            DescribeDescriptors(observedRequest.InitialDescriptors));

        CreateUpdateDeleteEntityAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => RunnerTestHelpers.TryTakeCreateAck(ackReader, observedRequest.RequestId, out ack),
            AckTimeoutFrames);
        Assert.True(ackObserved, "CreateUpdateDeleteEntityAck did not arrive in time.");
        Assert.Equal(0, ack.StatusCode);
        Assert.True(ack.EntityId > 0, "CreateUpdateDeleteEntityAck returned a zero/negative entity ID.");

        long networkId = ack.EntityId;

        MapVisualOverlay overlay = default;
        bool overlayObserved = harness.PumpUntil(
            () => TryTakeOverlayForEntity(overlayReader, networkId, out overlay),
            OverlayTimeoutFrames);
        Assert.True(overlayObserved, "MapVisualOverlay did not arrive on DDS in time.");
        Assert.NotNull(overlay.Points);
        Assert.Equal(points.Count, overlay.Points.Count);

        bool simHostHasPolyline = harness.PumpUntil(
            () => SimHostHasPolyline(harness, networkId),
            OverlayTimeoutFrames);
        Assert.True(simHostHasPolyline, "SimHost did not attach EditablePolyline in time.");

        bool igHasPolyline = harness.PumpUntil(
            () => IgHasPolyline(harness, networkId),
            OverlayTimeoutFrames);
        Assert.True(igHasPolyline, "IG did not receive EditablePolyline in time.");

        var igPolyline = GetIgPolylinePoints(harness, networkId);
        Assert.Equal(points.Count, igPolyline.Count);

        // With relative-coordinate storage, polyline points are offsets from the entity's
        // SimTransform position (centroid). Verify that centroid + relative == original abs pos.
        bool igHasSimTransform = harness.PumpUntil(
            () => IgHasSimTransform(harness, networkId),
            OverlayTimeoutFrames);
        Assert.True(igHasSimTransform, "IG entity did not receive SimTransform (centroid) in time.");

        var igCentroid   = GetIgSimTransformPosition(harness, networkId);
        var igCentroidXy = new Vector2(igCentroid.X, igCentroid.Y);

        for (int i = 0; i < points.Count; i++)
        {
            // Absolute position reconstructed from centroid + relative offset.
            var absFromIg = igCentroidXy + igPolyline[i];
            float dist = Vector2.Distance(points[i], absFromIg);
            Assert.True(dist <= PositionTolerance,
                $"Abs position mismatch for point {i}: expected ({points[i].X:F2},{points[i].Y:F2}) " +
                $"centroid=({igCentroidXy.X:F2},{igCentroidXy.Y:F2}) " +
                $"rel=({igPolyline[i].X:F2},{igPolyline[i].Y:F2}) " +
                $"reconstructed=({absFromIg.X:F2},{absFromIg.Y:F2}) dist={dist:F3}.");
        }

        // Verify the MapOverlayStyle component is present on the IG entity.
        // The SimHost egress does not currently relay StyleOverrideJson back, so
        // the style resolves to MapOverlayStyle.Default() (red fill, white border).
        bool igHasStyle = harness.PumpUntil(
            () => IgHasStyle(harness, networkId),
            OverlayTimeoutFrames);
        Assert.True(igHasStyle, "IG did not receive MapOverlayStyle component in time.");

        var igStyle = GetIgStyle(harness, networkId);
        // Default fill: R=255 G=0 B=0 A=80 (per MapOverlayStyle.Default())
        Assert.Equal(255, (int)igStyle.FillR);
        Assert.Equal(0,   (int)igStyle.FillG);
        Assert.Equal(0,   (int)igStyle.FillB);
        Assert.True(igStyle.LineThickness > 0f, "LineThickness should be positive.");
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

    private static bool TryTakeOverlayForEntity(
        DdsReader<MapVisualOverlay> reader,
        long networkId,
        out MapVisualOverlay overlay)
    {
        using var loan = reader.Take(5);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            if (sample.Data.EntityId != (int)networkId) continue;
            overlay = sample.Data;
            return true;
        }

        overlay = default;
        return false;
    }

    private static bool SimHostHasPolyline(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.SimHost.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.SimHost.World;
        return world != null
            && world.IsAlive(entity)
            && world.HasManagedComponent<EditablePolyline>(entity);
    }

    private static bool IgHasPolyline(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        return world.IsAlive(entity) && world.HasManagedComponent<EditablePolyline>(entity);
    }

    private static bool IgHasStyle(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        return world.IsAlive(entity) && world.HasComponent<MapOverlayStyle>(entity);
    }

    private static MapOverlayStyle GetIgStyle(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity))
            return default;

        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasComponent<MapOverlayStyle>(entity))
            return default;

        ref readonly var style = ref world.GetComponentRO<MapOverlayStyle>(entity);
        return style; // copy-out of ref-readonly
    }

    private static IReadOnlyList<Vector2> GetIgPolylinePoints(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity))
            return new List<Vector2>();

        ISimulationView world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasManagedComponent<EditablePolyline>(entity))
            return new List<Vector2>();

        var polyline = world.GetManagedComponentRO<EditablePolyline>(entity);
        return polyline.Points;
    }

    private static bool HasMasterWithTkb(List<EntityDescriptorUnion> descriptors, long tkbType)
    {
        for (int i = 0; i < descriptors.Count; i++)
        {
            var d = descriptors[i];
            if (d._d == EDescriptorType.dtEntityMaster && d.EntityMaster.TkbType == tkbType)
                return true;
        }

        return false;
    }

    private static bool HasGeoSpatialDescriptor(List<EntityDescriptorUnion> descriptors)
    {
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (descriptors[i]._d == EDescriptorType.dtWorldPos)
                return true;
        }
        return false;
    }

    private static bool IgHasSimTransform(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        return world.IsAlive(entity) && world.HasComponent<SimTransform>(entity);
    }

    private static Vector3 GetIgSimTransformPosition(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return Vector3.Zero;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity)) return Vector3.Zero;
        ref readonly var st = ref world.GetComponentRO<SimTransform>(entity);
        return st.Position;
    }

    private static bool HasOverlayWithPointCount(List<EntityDescriptorUnion> descriptors, int count)
    {
        for (int i = 0; i < descriptors.Count; i++)
        {
            var d = descriptors[i];
            if (d._d == EDescriptorType.dtMapVisualOverlay && d.MapVisualOverlay.Points?.Count == count)
                return true;
        }

        return false;
    }

    private static string DescribeDescriptors(List<EntityDescriptorUnion> descriptors)
    {
        var summary = new System.Text.StringBuilder();
        summary.Append("CreateEntityRequest descriptors: [");
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
            else if (d._d == EDescriptorType.dtMapVisualOverlay)
            {
                summary.Append("(Points=");
                summary.Append(d.MapVisualOverlay.Points?.Count ?? 0);
                summary.Append(")");
            }
        }
        summary.Append("]");
        return summary.ToString();
    }
}
