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
}
