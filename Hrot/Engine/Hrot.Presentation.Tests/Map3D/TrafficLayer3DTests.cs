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

    /// <summary>⭐ <c>CE-1197</c> — a sidewalk is drawn in its own light grey, a centimetre above the carriageway and under the paint.
    /// ✅ Red-proof: <c>Style(mesh)</c> ⇒ <c>Style(mesh.Kind)</c>.</summary>
    [Fact]
    public void CE1197_ASidewalk_IsLightGrey_AboveTheCarriageway()
    {
        var tri = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0) };
        var scene = new TrafficRoadScene(new[]
        {
            new TrafficRoadMesh(SceneKind.Asphalt, tri, new[] { 0, 1, 2 }),
            new TrafficRoadMesh(SceneKind.Asphalt, tri, new[] { 0, 1, 2 }, Sidewalk: true),
        }, Array.Empty<TrafficSignal>());
        TrafficLayer3D.BuildVertexData(scene, ambientGround: true, out var positions, out _, out var colours);

        Assert.Equal(TrafficLayer3D.AsphaltColor, colours[0]);
        Assert.Equal(TrafficLayer3D.SidewalkColor, colours[3]);
        Assert.True(TrafficLayer3D.SidewalkColor.R > TrafficLayer3D.AsphaltColor.R + 50, "a sidewalk is clearly lighter than the road");
        Assert.True(positions[3].Y > positions[0].Y, "above the carriageway");
        Assert.True(positions[3].Y < TrafficLayer3D.Style(SceneKind.LaneMarking).Lift, "under the paint");
    }

    [Fact]
    public void V8_TheAmbientGroundToggle_DropsOnlyTheGround()
    {
        TrafficLayer3D.BuildVertexData(Scene(), ambientGround: false, out var positions, out _, out var colours);

        Assert.Equal(6, positions.Length);
        Assert.DoesNotContain(TrafficLayer3D.GroundColor, colours);
    }

    /// <summary>⭐ CE-1196 (docs/DESIGN_Map_3D_Mode.md §6z CA-4) — the roads and the ambient ground are two streams (only the roads are
    /// drawn biased toward the eye), together exactly the whole scene; the bias is four depth steps at every distance — from 2 km about
    /// half a metre, from 50 m about a centimetre — and the near plane stays where R-1014 put it.</summary>
    [Fact]
    public void CE1196_TheRoadsAreBiased_TheAmbientGroundIsNot_AndTheBiasIsFourDepthStepsAtEveryDistance()
    {
        TrafficLayer3D.BuildVertexData(Scene(), k => k != SceneKind.AmbientGround, out var roads, out _, out var roadColours);
        TrafficLayer3D.BuildVertexData(Scene(), k => k == SceneKind.AmbientGround, out var ground, out _, out var groundColours);
        TrafficLayer3D.BuildVertexData(Scene(), ambientGround: true, out var all, out _, out _);
        Assert.Equal(all.Length, roads.Length + ground.Length);
        Assert.DoesNotContain(TrafficLayer3D.GroundColor, roadColours);
        Assert.All(groundColours, c => Assert.Equal(TrafficLayer3D.GroundColor, c));

        Assert.Equal(0.001f, MapCamera3D.NearClipShare);
        foreach (float d in new[] { 50f, 200f, 2000f, 5000f })
        {
            float near = d * MapCamera3D.NearClipShare;
            Assert.Equal(MapCamera3D.GroundBiasSteps * MapCamera3D.DepthStepAt(d, near), MapCamera3D.GroundBiasMetresAt(d, near), 5);
        }
        Assert.InRange(MapCamera3D.GroundBiasMetresAt(2000f, 2f), 0.4f, 0.6f);
        Assert.True(MapCamera3D.GroundBiasMetresAt(50f, 0.05f) < 0.02f);
        Assert.Equal(MapCamera3D.GroundBiasSteps * 2f / 16_777_216f, MapCamera3D.GroundBiasNdc, 12);
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
