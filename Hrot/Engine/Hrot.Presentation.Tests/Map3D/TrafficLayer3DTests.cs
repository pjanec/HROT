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

    /// <summary>⛔ R-1014 — the near plane is where it was before CE-1196 moved it: 0.1 % of the look-at distance, never under 5 cm.
    /// 🔒 User, 2026-10-20: <i>"the near plane seems to be too far probably maybe as a result of trying to fix teh z-fight of the roads; i
    /// need near plane as it was before"</i>. CE-1196 (the roads flickering from afar) is reopened, to be fixed without moving it.</summary>
    [Fact]
    public void R1014_TheNearPlane_IsAsBefore_ATenthOfAPercentOfTheLookAtDistance()
    {
        Assert.Equal(2f, new Fdp.Toolkit.Vis3D.MapCamera3D { Distance = 2000f }.NearClip, 3);
        Assert.Equal(0.2f, new Fdp.Toolkit.Vis3D.MapCamera3D { Distance = 200f }.NearClip, 3);
        Assert.Equal(0.05f, new Fdp.Toolkit.Vis3D.MapCamera3D { Distance = 2f }.NearClip);   // close up (first person): the 5 cm floor
    }
}
