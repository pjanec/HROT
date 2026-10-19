using System.Numerics;
using Fdp.Toolkit.Vis3D;
using Hrot.Traffic;
using Hrot.UI.Common.Map3D;
using Sim.Scene;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>AQ93 V7/V8 — the SUMO road layer's vertex build (pure: no window).</summary>
public sealed class TrafficLayer3DTests
{
    private static TrafficRoadScene Scene() => new(new[]
    {
        new TrafficRoadMesh(SceneKind.Asphalt, new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0) }, new[] { 0, 1, 2 }),
        new TrafficRoadMesh(SceneKind.LaneMarking, new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) }, new[] { 0, 1, 2 }),
        new TrafficRoadMesh(SceneKind.AmbientGround, new[] { new Vector3(0, 0, 0), new Vector3(40, 0, 0), new Vector3(0, 40, 0) }, new[] { 0, 1, 2 }),
    }, Array.Empty<TrafficSignal>());

    [Fact]
    public void V7_EachKind_IsColouredAndLifted_PaintOverAsphaltOverGround()
    {
        TrafficLayer3D.BuildVertexData(Scene(), ambientGround: true, out var positions, out var normals, out var colours);

        Assert.Equal(9, positions.Length);
        // Raylib is Y-up: HROT z (the lift) is Raylib y.
        Assert.Equal(TrafficLayer3D.Style(SceneKind.Asphalt).Lift, positions[0].Y, 4);
        Assert.Equal(TrafficLayer3D.Style(SceneKind.LaneMarking).Lift, positions[3].Y, 4);
        Assert.Equal(TrafficLayer3D.Style(SceneKind.AmbientGround).Lift, positions[6].Y, 4);
        Assert.True(positions[3].Y > positions[0].Y && positions[0].Y > positions[6].Y);
        Assert.Equal(TrafficLayer3D.AsphaltColor, colours[0]);
        Assert.Equal(TrafficLayer3D.GroundColor, colours[6]);
        Assert.All(normals, n => Assert.Equal(HrotToRaylib.Position(Vector3.UnitZ), n));
    }

    [Fact]
    public void V8_TheAmbientGroundToggle_DropsOnlyTheGround()
    {
        TrafficLayer3D.BuildVertexData(Scene(), ambientGround: false, out var positions, out _, out var colours);

        Assert.Equal(6, positions.Length);
        Assert.DoesNotContain(TrafficLayer3D.GroundColor, colours);
    }

    /// <summary>⭐ CE-4031 (AQ93 §15.8) — SUMO's agents that are not entities become bodies the entity layer draws as ours: a pedestrian
    /// typed by his id (the type his replica and his civilian also have), a car by its TKB type (else the generic traffic car) in its
    /// own paint, each keyed by its network id; a replica is left to the entity pass. ⛔ Before: grey and blue boxes (CE-4026).</summary>
    [Fact]
    public void CE4031_TheFarAgents_AreBodiesTypedAsTheirReplicasAre()
    {
        var walker = new TrafficPedPose(1, 7, "7", 10f, 0f, 0f, 0.5f);
        var replica = new TrafficPedPose(1, 8, "8", 12f, 0f, 0f, 0f);
        var car = new TrafficCarPose(1, default, "c1", 41, 0, 0x336699, 20f, 5f, 0f, 1f, 4.5f, 1.8f);
        var near = new TrafficCarPose(1, default, "c2", 42, 1050, 0, 30f, 5f, 0f, 0f, 4.5f, 1.8f);
        using var world = new Fdp.Core.EntityRepository();
        world.SetSingletonManaged(new TrafficFrame(new[] { car, near }, c => c.NetworkId == 42, new System.Collections.Generic.Dictionary<int, byte>(),
                                                   new[] { walker, replica }, p => p.NetworkId == 8));

        var bodies = TrafficAmbientBodies.Of(world);

        Assert.Equal(2, bodies.Count);
        var man = Assert.Single(bodies, b => b.Key == 7);
        Assert.Equal(TrafficPanicSystem.TypeOf(7), man.TkbType);
        Assert.Equal(TrafficReplicas.PedestrianTypeOf(7), man.TkbType);
        Assert.Equal(new Vector3(10f, 0f, 0f), man.Position);
        Assert.Equal(0.5f, man.YawRad);
        var auto = Assert.Single(bodies, b => b.Key == 41);
        Assert.Equal(Hrot.Map.Common.TkbEntityTypes.TrafficCar, auto.TkbType);
        Assert.Equal(0x336699u, auto.Colour);
        Assert.NotEqual(0, TrafficAmbientBodies.KeyOf(0, "veh7"));
        Assert.Equal(TrafficAmbientBodies.KeyOf(0, "veh7"), TrafficAmbientBodies.KeyOf(0, "veh7"));
        Assert.True(TrafficAmbientBodies.KeyOf(0, "veh7") < 0, "a hashed key never meets a real network id");
    }

    /// <summary>⭐ CE-1196 — SUMO's roads no longer flicker against our ground from afar: with the camera 2 km from what it looks at, the depth
    /// buffer tells apart less than the asphalt's lift over the ground there (it told apart ~12 cm with the old near clip, three times the
    /// lift). 🔒 User, live, 2026-10-19: <i>"sumo roads (black) z-fight heavily with artificial terrain if looked at from distance"</i>.</summary>
    [Fact]
    public void CE1196_FromTwoKilometresTheRoadsLiftIsWithinTheDepthBuffersReach()
    {
        var camera = new Fdp.Toolkit.Vis3D.MapCamera3D { Distance = 2000f };
        float lift = TrafficLayer3D.Style(SceneKind.Asphalt).Lift - TrafficLayer3D.Style(SceneKind.AmbientGround).Lift;   // over the ground
        float step = Fdp.Toolkit.Vis3D.MapCamera3D.DepthStepAt(2000f, camera.NearClip);
        Assert.True(step < lift, $"the depth step at 2 km is {step * 100f:F1} cm, the road lies {lift * 100f:F1} cm above the ground");
        Assert.True(camera.NearClip < 2000f * 0.05f, "the near clip stays far inside the eye-to-look-at distance");
        Assert.Equal(0.05f, new Fdp.Toolkit.Vis3D.MapCamera3D { Distance = 2f }.NearClip);   // close up (first person): unchanged
    }
}
