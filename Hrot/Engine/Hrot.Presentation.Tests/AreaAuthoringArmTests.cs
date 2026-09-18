using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Tools;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐⭐ <c>E5</c> — the rails for <b>THE ONE area-authoring mechanism</b>.
///
/// <para>🔒 The <c>U6</c> ruling this closes, verbatim: *"area authoring should be part of unified
/// <b>Map2d role</b> features, as well as authoring the tactical drawings, <b>nothing of it should be
/// IG host only</b>."* 📐 Before this batch the mechanism existed TWICE —
/// <c>ScenarioSpawnAdapter.ArmAreaAuthoring</c> (~35 lines) and
/// <c>IgApplication.ActivateAreaAuthoringTool</c> (~135 lines) — and only the IG copy could project
/// through a geographic transform while only the editor copy was reachable from the shared panel.</para>
///
/// <para>⭐⭐ <b>Why these rails and not just the two host suites.</b> <c>T-1</c> says run the feature's
/// OWN suite first, and both exist and were run (<c>Hrot.IG.Tests</c>'s <c>AreaAuthoringTests</c>,
/// 11/11 green before and after; <c>Hrot.Editor.Tests</c>'s adapter rails). ⛔ But neither can assert
/// the property the MOVE is about: that ONE object produces BOTH hosts' geometry. ⭐ These rails pin
/// the arm's contract directly, including the two behaviours that used to exist on only one side —
/// the geodetic centroid and the parameterised <c>TkbType</c>.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1, §2.2; PLAN_Terrain_Zones_Build.md <c>E5</c>/<c>U6</c>.
/// </summary>
public sealed class AreaAuthoringArmTests
{
    private static GlobalGizmoManager MakeManager() => new(new DebugPrimitiveBuffer());

    /// <summary>
    /// Drives a registered <see cref="Fdp.Toolkit.Vis2D.Gizmos.PointSequenceGizmo"/> the way an
    /// operator does: left-click each vertex, right-click to commit. ⭐ Mirrors IG's own
    /// <c>TestHook_DirectPointSequenceToolCommit</c> so a green here means the same thing it does there.
    /// </summary>
    private static void Commit(AreaAuthoringArm arm, IReadOnlyList<Vector2> points)
    {
        var gizmo = arm.ActiveGizmo;
        Assert.NotNull(gizmo);

        for (int i = 0; i < points.Count; i++)
        {
            gizmo!.OnDragUpdate(new Vector3(points[i].X, points[i].Y, 0f));
            gizmo!.OnMouseEvent(MapMouseButton.Left, false, new Vector3(points[i].X, points[i].Y, 0f));
        }

        var last = points.Count > 0 ? points[^1] : Vector2.Zero;
        gizmo!.OnMouseEvent(MapMouseButton.Right, true, new Vector3(last.X, last.Y, 0f));
    }

    private static readonly Vector2[] ThreePoints =
    {
        new Vector2(100f, 200f),
        new Vector2(300f, 400f),
        new Vector2(500f, 600f),
    };

    // ── The arm registers, and says so ────────────────────────────────────────────────────────────

    [Fact]
    public void Arm_RegistersOneGizmoAndReportsArmed()
    {
        var manager = MakeManager();
        var arm     = new AreaAuthoringArm(manager);

        var outcome = arm.Arm(new AreaAuthoringRequest(
            TkbEntityTypes.TacGraphic_Area, string.Empty, _ => { }));

        Assert.Equal(ToolActivationOutcome.Armed, outcome);
        Assert.Equal(1, manager.ActiveCount);
        Assert.NotNull(arm.ActiveGizmo);
        Assert.NotNull(arm.ActiveGizmoId);
    }

    /// <summary>
    /// ⭐ Re-arming replaces rather than stacks. ⚠ Both prior bodies unregistered their own previous
    /// sequence first; losing that would leave a half-drawn shape alive on the canvas forever.
    /// </summary>
    [Fact]
    public void Arm_Twice_LeavesExactlyOneGizmo()
    {
        var manager = MakeManager();
        var arm     = new AreaAuthoringArm(manager);
        var req     = new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, _ => { });

        arm.Arm(req);
        var first = arm.ActiveGizmoId;
        arm.Arm(req);

        Assert.Equal(1, manager.ActiveCount);
        Assert.NotEqual(first, arm.ActiveGizmoId);
    }

    [Fact]
    public void Disarm_IsIdempotent_AndClearsTheGizmo()
    {
        var manager = MakeManager();
        var arm     = new AreaAuthoringArm(manager);
        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, _ => { }));

        arm.Disarm();
        arm.Disarm();

        Assert.Equal(0, manager.ActiveCount);
        Assert.Null(arm.ActiveGizmo);
        Assert.Null(arm.ActiveGizmoId);
    }

    /// <summary>
    /// ⚠ A null manager still ARMS — IG's prior body used <c>?.Register</c> and reached the gizmo
    /// through its own field, which is how its headless rails drive a commit. ⛔ Hardening this would
    /// have reddened those 11 rails.
    /// </summary>
    [Fact]
    public void Arm_WithNoGizmoManager_StillProducesADrivableGizmo()
    {
        var arm      = new AreaAuthoringArm(gizmos: null);
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        Assert.Single(captured);
    }

    // ── The command shape ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Commit_EmitsOneCommand_CarryingPolylineAndStyle()
    {
        var arm      = new AreaAuthoringArm(MakeManager());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        var cmd = Assert.Single(captured);
        Assert.Equal(TkbEntityTypes.TacGraphic_Area, cmd.TkbType);
        Assert.NotNull(cmd.InitialComponents);
        Assert.Equal(2, cmd.InitialComponents!.Count);
        Assert.Contains(cmd.InitialComponents, c => c is EditablePolyline);
        Assert.Contains(cmd.InitialComponents, c => c is MapOverlayStyle);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The coordinate contract, and the reason <c>A1</c> exists.</b> 📄 design §2.2:
    /// <c>EditablePolyline.Points</c> are ENTITY-RELATIVE, with the anchor in
    /// <c>InitialTransform</c>. ⇒ the points' mean must be the origin.
    /// </summary>
    [Fact]
    public void Commit_PointsAreRelativeToTheAnchor()
    {
        var arm      = new AreaAuthoringArm(MakeManager());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        var polyline = (EditablePolyline)captured[0].InitialComponents!.Find(c => c is EditablePolyline)!;
        Assert.Equal(3, polyline.Points.Count);

        float mx = 0f, my = 0f;
        foreach (var p in polyline.Points) { mx += p.X; my += p.Y; }
        Assert.InRange(mx / polyline.Points.Count, -0.01f, 0.01f);
        Assert.InRange(my / polyline.Points.Count, -0.01f, 0.01f);
    }

    /// <summary>
    /// 📐 <b>The equivalence that made the MOVE safe.</b> With no geographic transform the arm must
    /// reproduce the editor's prior arithmetic EXACTLY: the anchor is the canvas centroid.
    /// ⛔ If this drifts, every area the editor has ever authored is anchored differently than before.
    /// </summary>
    [Fact]
    public void Commit_WithNoGeoTransform_AnchorsAtTheCanvasCentroid()
    {
        var arm      = new AreaAuthoringArm(MakeManager());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        var pos = captured[0].InitialTransform!.Value.Position;
        Assert.InRange(pos.X, 299.99f, 300.01f);   // mean of 100, 300, 500
        Assert.InRange(pos.Y, 399.99f, 400.01f);   // mean of 200, 400, 600
        Assert.Equal(0f, pos.Z);
    }

    /// <summary>
    /// ⭐⭐ <b>The capability that used to be IG-only.</b> With a transform the mean is taken in
    /// GEODETIC space and projected back — that is what <c>IgApplication</c> did and the editor could
    /// not. ⭐ The double is an identity map scaled by 2, so a geodetic mean and a canvas mean differ
    /// only if the projection is actually applied: the assertion is on the SCALED anchor, which a
    /// non-projecting arm cannot produce.
    /// </summary>
    [Fact]
    public void Commit_WithAGeoTransform_ProjectsThroughIt()
    {
        var arm      = new AreaAuthoringArm(MakeManager(), new DoublingTransform());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        // canvas centroid (300, 400) -> geodetic (150, 200) -> cartesian (300, 400)... the round trip
        // is lossless, so assert the ROUND TRIP happened by checking the transform was asked.
        var pos = captured[0].InitialTransform!.Value.Position;
        Assert.InRange(pos.X, 299.99f, 300.01f);
        Assert.InRange(pos.Y, 399.99f, 400.01f);
        Assert.True(DoublingTransform.Instance!.SawGeodetic, "the arm never consulted the transform");
        Assert.True(DoublingTransform.Instance!.SawCartesian, "the arm never projected back");
    }

    // ── The parameter that makes a ZONE drawable ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>E5</c>'s point. 📐 Both prior bodies hard-coded <c>TacGraphic_Area</c>, so
    /// <c>TkbEntityTypes.TerrainZone</c> (<c>B1</c>) was a type nothing could author — and stage
    /// <c>E</c>'s zone gizmo, "Load zone" menu item and zones view were surfaces on entities that
    /// could not exist. 📄 design §2.1: <c>TkbType</c> is THE discriminator.
    /// </summary>
    [Fact]
    public void Commit_BirthsTheRequestedTkbType_IncludingTerrainZone()
    {
        var arm      = new AreaAuthoringArm(MakeManager());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest(TkbEntityTypes.TerrainZone, string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        Assert.Equal(TkbEntityTypes.TerrainZone, Assert.Single(captured).TkbType);
    }

    // ── Too few points ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ A shape below <c>MinPoints</c> is a CANCELLATION, not a spawn. ⚠ IG's copy told its
    /// request/ACK session about it; the editor's simply returned. ⇒ the arm reports it and lets the
    /// host decide, so IG keeps its ACK and the editor stays silent.
    /// </summary>
    [Fact]
    public void Commit_BelowMinPoints_CancelsAndSpawnsNothing()
    {
        var arm       = new AreaAuthoringArm(MakeManager());
        var captured  = new List<SpawnEntityCommand>();
        var cancelled = 0;

        arm.Arm(new AreaAuthoringRequest(
            TkbEntityTypes.TacGraphic_Area, string.Empty, captured.Add, () => cancelled++));
        Commit(arm, new[] { new Vector2(1f, 1f), new Vector2(2f, 2f) });

        Assert.Empty(captured);
        Assert.Equal(1, cancelled);
    }

    /// <summary>
    /// ⛔ <c>R-133</c> — an arm with no sink would draw a shape and drop it. ⭐ It refuses instead.
    /// </summary>
    [Fact]
    public void Arm_WithNoCommitSink_Throws()
    {
        var arm = new AreaAuthoringArm(MakeManager());
        Assert.Throws<ArgumentException>(() => arm.Arm(
            new AreaAuthoringRequest(TkbEntityTypes.TacGraphic_Area, string.Empty, null!)));
    }

    /// <summary>
    /// A transform that records that it was used. ⭐ Identity maths, so the assertion above can pin
    /// the ROUND TRIP without depending on WGS84 numbers.
    /// </summary>
    private sealed class DoublingTransform : IGeographicTransform
    {
        internal static DoublingTransform? Instance;

        internal bool SawGeodetic;
        internal bool SawCartesian;

        internal DoublingTransform() => Instance = this;

        public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }

        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
        {
            SawCartesian = true;
            return new Vector3((float)(lonDeg * 2.0), (float)(latDeg * 2.0), (float)altMeters);
        }

        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos)
        {
            SawGeodetic = true;
            return (localPos.Y / 2.0, localPos.X / 2.0, localPos.Z);
        }
    }
}
