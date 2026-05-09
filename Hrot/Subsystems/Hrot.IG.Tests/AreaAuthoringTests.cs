using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Fdp.Toolkit.NetworkSpawning.Events;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests for the area (tactical-shape) authoring tool (OC1-B003).
///
/// Verifies that <c>ParseCommandAndActivateAreaTool</c> activates a
/// <see cref="PointSequenceGizmo"/> when <c>tkbType != TacGraphic_Route</c> (i.e., area shapes),
/// and that finishing the gizmo emits a well-formed <see cref="SpawnEntityCommand"/>
/// with correct geometry in <see cref="SpawnEntityCommand.InitialComponents"/>.
/// </summary>
public class AreaAuthoringTests : System.IDisposable
{
    private readonly IgApplication _app;
    private readonly List<SpawnEntityCommand> _captured = new();

    // Use a TkbType that triggers the area authoring path (not route = 8802).
    private const long AreaTkbType = 8803L; // TacGraphic_Area

    public AreaAuthoringTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 231);
        _app.TestHook_SetSpawnCommandSink(cmd => _captured.Add(cmd));
    }

    public void Dispose()
    {
        _app.TestHook_SetSpawnCommandSink(null);
        _app.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<Vector2> ThreePoints = new[]
    {
        new Vector2(100f, 200f),
        new Vector2(300f, 400f),
        new Vector2(500f, 600f),
    };

    private SpawnEntityCommand CaptureAreaRequest(IReadOnlyList<Vector2> points)
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");
        _app.TestHook_DirectPointSequenceToolCommit(points);
        Assert.Single(_captured);
        return _captured[0];
    }

    // ── Tool activation ───────────────────────────────────────────────────────

    /// <summary>
    /// OC1-B003: A ParseCommand call with area TkbType must activate a PointSequenceGizmo.
    /// </summary>
    [Fact]
    public void ParseCommand_WithAreaTkbType_PushesPointSequenceTool()
    {
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");

        Assert.True(_app.TestHook_IsPointSequenceToolActive);
    }

    // ── Request shape ─────────────────────────────────────────────────────────

    /// <summary>
    /// OC1-B003: Finishing the area tool with 3 points must emit exactly one
    /// <see cref="SpawnEntityCommand"/> via the test sink.
    /// </summary>
    [Fact]
    public void AreaTool_3Points_EmitsExactlyOneRequest()
    {
        _ = CaptureAreaRequest(ThreePoints);

        Assert.Single(_captured);
    }

    /// <summary>
    /// The emitted command must carry the area TkbType, a valid InitialTransform (centroid),
    /// and exactly 2 InitialComponents (EditablePolyline + MapOverlayStyle).
    /// </summary>
    [Fact]
    public void AreaRequest_HasCorrectStructure()
    {
        var cmd = CaptureAreaRequest(ThreePoints);

        Assert.Equal(AreaTkbType, cmd.TkbType);
        Assert.True(cmd.InitialTransform.HasValue, "InitialTransform (centroid) must be set.");
        Assert.NotNull(cmd.InitialComponents);
        Assert.Equal(2, cmd.InitialComponents!.Count);
        Assert.Single(cmd.InitialComponents.OfType<EditablePolyline>());
        Assert.Single(cmd.InitialComponents.OfType<MapOverlayStyle>());
    }

    /// <summary>
    /// The emitted command must advertise the area TKB type (not route).
    /// </summary>
    [Fact]
    public void AreaRequest_EntityMaster_TkbType_IsArea()
    {
        var cmd = CaptureAreaRequest(ThreePoints);

        Assert.Equal(AreaTkbType, cmd.TkbType);
    }

    /// <summary>
    /// OC1-B003 — coordinate contract:
    /// The <see cref="EditablePolyline"/> points are entity-relative Cartesian XY.
    /// Their mean must be (0, 0) because they are offsets from the centroid
    /// (stored in <see cref="SpawnEntityCommand.InitialTransform"/>).
    /// </summary>
    [Fact]
    public void AreaRequest_Overlay_PointsAreRelativeOffsets_FromCentroid()
    {
        var cmd = CaptureAreaRequest(ThreePoints);

        var polyline = cmd.InitialComponents!.OfType<EditablePolyline>().Single();
        Assert.NotNull(polyline.Points);
        Assert.Equal(ThreePoints.Count, polyline.Points!.Count);

        // Mean of entity-relative Cartesian X/Y must be ~0 (centroid is the reference).
        double sumX = 0, sumY = 0;
        foreach (var pt in polyline.Points)
        {
            sumX += pt.X;
            sumY += pt.Y;
        }
        double meanX = sumX / polyline.Points.Count;
        double meanY = sumY / polyline.Points.Count;

        Assert.Equal(0.0, meanX, precision: 1);
        Assert.Equal(0.0, meanY, precision: 1);

        // At least one point must be non-zero (shape is not degenerate).
        bool anyNonZero = polyline.Points.Any(pt => Math.Abs(pt.X) > 0.01f || Math.Abs(pt.Y) > 0.01f);
        Assert.True(anyNonZero, "All overlay points are at centroid — polygon is degenerate.");
    }

    /// <summary>
    /// After commit, the PointSequenceGizmo must be removed.
    /// </summary>
    [Fact]
    public void AreaTool_AfterCommit_ToolIsPopped()
    {
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        Assert.False(_app.TestHook_IsPointSequenceToolActive);
    }

    /// <summary>
    /// Committing with fewer than 3 points (below the area-minimum) must NOT emit a command.
    /// </summary>
    [Fact]
    public void AreaTool_2Points_DoesNotEmitRequest()
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");
        _app.TestHook_DirectPointSequenceToolCommit(
            new[] { new Vector2(10f, 20f), new Vector2(30f, 40f) });

        Assert.Empty(_captured);
    }

    // ── Coordinate-fix regression tests ──────────────────────────────────────

    /// <summary>
    /// Canvas Y corresponds to the North (Y) axis of ENU world space.
    /// Three points with same X but different Y must produce entity-relative
    /// <see cref="EditablePolyline"/> points with non-zero Y components (North offset).
    /// </summary>
    [Fact]
    public void AreaTool_CanvasYEncodedAsNorth_NotEast()
    {
        // Three points with identical X but different Y (vertical strip).
        var sameXDifferentY = new[]
        {
            new Vector2(0f, 100f),
            new Vector2(0f, 500f),
            new Vector2(0f, 800f),
        };

        var cmd = CaptureAreaRequest(sameXDifferentY);
        var polyline = cmd.InitialComponents!.OfType<EditablePolyline>().Single();
        Assert.NotNull(polyline.Points);

        // Different canvas Y → different North (ENU Y) → non-zero relative Y.
        bool anyNonZeroY = polyline.Points!.Any(p => Math.Abs(p.Y) > 0.01f);
        Assert.True(anyNonZeroY,
            "All relative Y offsets are zero — canvas Y is NOT being encoded as North.");
    }

    /// <summary>
    /// Canvas X corresponds to East in ENU world space.
    /// Points with same Y but different X must produce entity-relative points
    /// with non-zero X components (East offset) and near-zero Y components (same North).
    /// </summary>
    [Fact]
    public void AreaTool_CanvasXEncodesEast_CanvasYEncodesNorth()
    {
        var sameYDifferentX = new[]
        {
            new Vector2(100f, 0f),
            new Vector2(400f, 0f),
            new Vector2(700f, 0f),
        };

        var cmd = CaptureAreaRequest(sameYDifferentX);
        var polyline = cmd.InitialComponents!.OfType<EditablePolyline>().Single();

        // Same canvas Y → near-zero relative Y (all on same North latitude).
        bool allNearZeroY = polyline.Points!.All(p => Math.Abs(p.Y) < 1f);
        Assert.True(allNearZeroY,
            "Same-Y canvas points produced differing North offsets — unexpected.");

        // Different X → relative X values should differ.
        bool anyNonZeroX = polyline.Points!.Any(p => Math.Abs(p.X) > 0.01f);
        Assert.True(anyNonZeroX,
            "Different-X canvas points produced identical East offsets.");
    }
}
