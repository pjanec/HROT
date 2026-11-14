using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Tools;
using System.Linq;
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
/// OWN suite first, and both exist and were run (<c>Hrot.Player.Tests</c>'s <c>AreaAuthoringTests</c>,
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
    /// operator does: left-click each vertex, press Enter to finish (D11). ⭐ Mirrors IG's own
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

        // ⭐ B-17 P2 (D11) — Enter finishes; right-click is the menu and ends nothing
        gizmo!.OnKeyEvent(MapKeyboardKey.Enter, true);
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
            (Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, _ => { }));

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
        var req     = new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, _ => { });

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
        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, _ => { }));

        arm.Disarm();
        arm.Disarm();

        Assert.Equal(0, manager.ActiveCount);
        Assert.Null(arm.ActiveGizmo);
        Assert.Null(arm.ActiveGizmoId);
    }

    /// <summary>
    /// ⭐ CE-1033 S3 (docs/DESIGN_Map_3D_Mode.md M22 step 1) — an area drawn on a ROOF is on the roof: the committed anchor keeps
    /// the level's height. One drawn on the ground keeps 0, so the 3-D map drapes it over relief (M18, R-248) — and a 2-D click
    /// (no height) is byte-identical to before.
    /// </summary>
    [Fact]
    public void M22_AnAreaDrawnOnARoof_KeepsTheRoofsHeight_OnTheGround_Zero()
    {
        var terrain = Hrot.Presentation.Tests.Map3D.Map3DTests.TestTown();
        var world = Hrot.Presentation.Tests.Map3D.Map3DTests.WorldWith(terrain);
        var q = Fdp.Toolkit.World.WorldQuery.Of(world)!;
        Vector3? roof = null, ground = null;
        for (float x = terrain.BoundsMin.X + 5; x < terrain.BoundsMax.X && (roof == null || ground == null); x += 2f)
            for (float y = terrain.BoundsMin.Y + 5; y < terrain.BoundsMax.Y && (roof == null || ground == null); y += 2f)
            {
                var s = q.SurfacesAt(x, y, out int g);
                if (roof == null && s.Count > g + 1 && s[^1] - s[g] > 4f) roof = new Vector3(x, y, s[^1]);
                if (ground == null && s.Count == g + 1) ground = new Vector3(x, y, s[g]);
            }
        Assert.True(roof != null && ground != null, "test-town has a roof and open ground");

        SpawnEntityCommand? committed = null;
        var arm = new AreaAuthoringArm(MakeManager());
        void Draw(Vector3 at)
        {
            committed = null;
            arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, c => committed = c));
            var gizmo = arm.ActiveGizmo!;
            gizmo.UpdateAndDraw(world, 0f, new DebugPrimitiveBuffer());   // the gizmo learns the world it picks in
            foreach (var d in new[] { Vector3.Zero, new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) })
                gizmo.OnMouseEvent(MapMouseButton.Left, false, at + d);
            gizmo.OnKeyEvent(MapKeyboardKey.Enter, true);   // ⭐ B-17 P2 — Enter finishes (right-click is the menu)
        }

        Draw(roof!.Value);
        Assert.Equal(roof.Value.Z, committed!.Value.InitialTransform!.Value.Position.Z, 3);
        Draw(ground!.Value with { Z = ground.Value.Z == 0f ? 0.01f : ground.Value.Z });
        Assert.Equal(0f, committed!.Value.InitialTransform!.Value.Position.Z);
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

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        Assert.Single(captured);
    }

    // ── The command shape ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Commit_EmitsOneCommand_CarryingPolylineAndStyle()
    {
        var arm      = new AreaAuthoringArm(MakeManager());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
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

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
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

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
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

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
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

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TerrainZone), string.Empty, captured.Add));
        Commit(arm, ThreePoints);

        Assert.Equal(TkbEntityTypes.TerrainZone, Assert.Single(captured).TkbType);
    }

    /// <summary>⭐ CE-1161 (Weather and Time §7 E-5; backend handoff U3) — a drawn Weather Region is born WITH its weather: a region
    /// without <c>WeatherConditions</c> is no region (<c>WeatherField</c> ignores it). Any other area is born without one.</summary>
    [Fact]
    public void CE1161_AWeatherRegion_IsBornWithStandardWeather_AnAreaIsNot()
    {
        var arm      = new AreaAuthoringArm(MakeManager());
        var captured = new List<SpawnEntityCommand>();

        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.WeatherRegion), string.Empty, captured.Add));
        Commit(arm, ThreePoints);
        var region = Assert.Single(captured);
        Assert.Equal(TkbEntityTypes.WeatherRegion, region.TkbType);
        Assert.Contains(region.InitialComponents!, c => c is Fdp.Toolkit.Weather.WeatherConditions);

        captured.Clear();
        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
        Commit(arm, ThreePoints);
        Assert.DoesNotContain(Assert.Single(captured).InitialComponents!, c => c is Fdp.Toolkit.Weather.WeatherConditions);
    }

    // ── Too few points ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ B-17 P2 (D11) — a finish below <c>MinPoints</c> spawns NOTHING and cancels NOTHING: the tool says why and STAYS armed (it used to be a silent
    /// cancel that lost the user's points). Esc then cancels, and tells the host (<c>OnCancelled</c>).
    /// </summary>
    [Fact]
    public void Commit_BelowMinPoints_SpawnsNothing_SaysWhy_AndStaysArmed()
    {
        var manager   = MakeManager();
        var arm       = new AreaAuthoringArm(manager);
        var captured  = new List<SpawnEntityCommand>();
        var cancelled = 0;

        arm.Arm(new AreaAuthoringRequest(
            (Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add, () => cancelled++));
        Commit(arm, new[] { new Vector2(1f, 1f), new Vector2(2f, 2f) });

        Assert.Empty(captured);
        Assert.Equal(0, cancelled);                                   // not a cancel …
        Assert.NotNull(arm.ActiveGizmo);                              // … the tool is still there
        Assert.Equal(1, manager.ActiveCount);
        Assert.Contains("an area needs 3 points", arm.ActiveGizmo!.Session.Reason);

        arm.ActiveGizmo.OnKeyEvent(MapKeyboardKey.Escape, true);      // Esc cancels, through the one cancel
        Assert.Equal(1, cancelled);
        Assert.Null(arm.ActiveGizmo);
        Assert.Equal(0, manager.ActiveCount);
    }

    // ── ⭐ B-17 P2 — the drawing convention (D11), on the area gizmo (PointSequenceGizmo, closed, 3+ points) ───────────────

    private static (AreaAuthoringArm Arm, List<SpawnEntityCommand> Out, int[] Cancelled, GlobalGizmoManager Manager) ArmedArea(ToolController? tools = null)
    {
        var manager = MakeManager();
        var arm = new AreaAuthoringArm(manager, tools: tools);
        var captured = new List<SpawnEntityCommand>();
        var cancelled = new int[1];
        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add, () => cancelled[0]++));
        return (arm, captured, cancelled, manager);
    }

    private static void Click(Fdp.Toolkit.Vis2D.Gizmos.PointSequenceGizmo g, Vector2 at)
    {
        g.OnDragUpdate(new Vector3(at, 0f));
        g.OnMouseEvent(MapMouseButton.Left, false, new Vector3(at, 0f));
    }

    /// <summary>⭐ The three finish doors — Enter, the on-map ✓, the menu's Finish — all reach the ONE <c>Finish</c> and each commits exactly one command.
    /// Red-proof (compiles): the gizmo's <c>Resolve</c> ignoring <c>Finished</c> ⇒ all three fail.</summary>
    [Fact]
    public void P2_Enter_the_tick_and_the_menu_Finish_all_commit_ONE_area()
    {
        foreach (var door in new[] { "enter", "tick", "menu" })
        {
            var (arm, captured, _, manager) = ArmedArea();
            var g = arm.ActiveGizmo!;
            foreach (var p in ThreePoints) Click(g, p);
            switch (door)
            {
                case "enter": g.OnKeyEvent(MapKeyboardKey.Enter, true); break;
                case "tick": Click(g, g.Session.DonePosition); break;   // a left click on the ✓
                default: g.OnMenuAction(Hrot.Common.Constants.GlobalActionIds.FinishDrawing); break;
            }
            Assert.True(captured.Count == 1, $"{door}: one commit");
            Assert.Equal(3, Assert.Single(captured).InitialComponents!.OfType<EditablePolyline>().Single().Points.Count);   // the ✓ click is NOT a fourth point
            Assert.Equal(0, manager.ActiveCount);                    // and the tool is gone
        }
    }

    /// <summary>⭐ Esc and the menu's Cancel cancel (and tell the host); Backspace removes the last point and nothing else.</summary>
    [Fact]
    public void P2_Esc_and_the_menu_Cancel_cancel_and_Backspace_removes_the_last_point()
    {
        var (arm, captured, cancelled, manager) = ArmedArea();
        var g = arm.ActiveGizmo!;
        foreach (var p in ThreePoints) Click(g, p);
        g.OnKeyEvent(MapKeyboardKey.Backspace, true);
        Assert.Equal(2, g.Session.Points.Count);
        Assert.Equal(1, manager.ActiveCount);                         // Backspace ends nothing
        g.OnKeyEvent(MapKeyboardKey.Escape, true);
        Assert.Equal(1, cancelled[0]);
        Assert.Empty(captured);
        Assert.Equal(0, manager.ActiveCount);

        var (arm2, captured2, cancelled2, _) = ArmedArea();
        var g2 = arm2.ActiveGizmo!;
        foreach (var p in ThreePoints) Click(g2, p);
        g2.OnMenuAction(Hrot.Common.Constants.GlobalActionIds.RemoveLastPoint);
        Assert.Equal(2, g2.Session.Points.Count);
        g2.OnMenuAction(Hrot.Common.Constants.GlobalActionIds.CancelDrawing);
        Assert.Equal(1, cancelled2[0]);
        Assert.Empty(captured2);
    }

    /// <summary>
    /// ⭐⭐ Right-click is the MENU. The press and the release finish NOTHING and cancel nothing (the area gizmo used to finish on the press), and the armed
    /// gizmo's menu — the three catalogue rows Finish · Remove last point · Cancel — is bound to its tool id so a right-click opens it.
    /// Red-proof (compiles): make <c>OnMouseEvent</c> finish on <c>Right</c> again ⇒ the commit count below reads 1.
    /// </summary>
    [Fact]
    public void P2_RightClick_finishes_NOTHING_and_opens_the_drawing_menu()
    {
        var buffer = new DebugPrimitiveBuffer();
        var manager = new GlobalGizmoManager(buffer);
        var arm = new AreaAuthoringArm(manager);
        var captured = new List<SpawnEntityCommand>();
        arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, captured.Add));
        var g = arm.ActiveGizmo!;
        foreach (var p in ThreePoints) Click(g, p);

        g.OnMouseEvent(MapMouseButton.Right, true, new Vector3(500f, 600f, 0f));
        g.OnMouseEvent(MapMouseButton.Right, false, new Vector3(500f, 600f, 0f));
        Assert.Empty(captured);
        Assert.Equal(1, manager.ActiveCount);
        Assert.Equal(3, g.Session.Points.Count);

        using var repo = new EntityRepository();
        repo.RegisterEvent<GizmoDragUpdateEvent>(); repo.RegisterEvent<GizmoMouseEvent>(); repo.RegisterEvent<GizmoKeyEvent>();
        repo.Bus.SwapBuffers();
        manager.Execute(repo, 0.016f);
        string? json = null; long anchor = 0;
        foreach (ref readonly var prim in buffer.GetFrame())
            if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding) { anchor = prim.StructNetworkId; json = buffer.InternMap.TryResolve(prim.StringHash); }
        Assert.Equal(arm.ActiveGizmoId, anchor);
        var rows = System.Text.Json.JsonSerializer.Deserialize<ContextMenuItemDto[]>(json!, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
        Assert.Equal(new[] { "Finish", "Remove last point", "Cancel" }, rows.Select(r => r.Label).ToArray());
        Assert.Equal(new[] { Hrot.Common.Constants.GlobalActionIds.FinishDrawing, Hrot.Common.Constants.GlobalActionIds.RemoveLastPoint, Hrot.Common.Constants.GlobalActionIds.CancelDrawing },
                     rows.Select(r => r.Id).ToArray());
    }

    /// <summary>⭐ Clicking the FIRST point closes an area (once it has its minimum); with fewer it is just another point.</summary>
    [Fact]
    public void P2_ClickingTheFirstPoint_ClosesTheArea_OnceItHasThree()
    {
        var (arm, captured, _, manager) = ArmedArea();
        var g = arm.ActiveGizmo!;
        Click(g, ThreePoints[0]); Click(g, ThreePoints[1]);
        Click(g, ThreePoints[0] + new Vector2(0.5f, 0.5f));          // two points so far: a third POINT, near the first
        Assert.Equal(3, g.Session.Points.Count);
        Assert.Empty(captured);
        Click(g, ThreePoints[0] + new Vector2(0.5f, 0.5f));          // now it closes
        Assert.Single(captured);
        Assert.Equal(3, Assert.Single(captured).InitialComponents!.OfType<EditablePolyline>().Single().Points.Count);   // the closing click adds nothing
        Assert.Equal(0, manager.ActiveCount);
    }

    /// <summary>
    /// ⭐ Too few points: the tool says why on the armed tool's HINT LINE (replacing the hint) and stays armed; the next click gives the hint back.
    /// Red-proof (compiles): <c>Finish()</c> returning <c>Cancelled</c> for too few ⇒ the tool is gone (<c>ActiveCount</c> 0) and no reason is set.
    /// </summary>
    [Fact]
    public void P2_TooFew_StaysArmed_AndTheReasonReplacesTheHintUntilTheNextClick()
    {
        var manager = MakeManager();
        var tools = new ToolController(() => manager, () => null);
        var arm = new AreaAuthoringArm(manager, tools: tools);
        tools.Register(new ToolDescriptor("draw-area", "Draw Area", ToolModality.Modal, ToolArbiter.Global, Hint: DrawingMenu.AreaHint),
            _ => arm.Arm(new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, _ => { })));
        Assert.True(tools.Activate("draw-area"));
        Assert.Contains("Enter / Done: finish (3+)", tools.ActiveHint);   // the hint names the minimum and the keys

        var g = arm.ActiveGizmo!;
        Click(g, ThreePoints[0]); Click(g, ThreePoints[1]);
        g.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Equal(1, manager.ActiveCount);                          // stays armed
        Assert.Contains("an area needs 3 points", tools.ActiveHint);   // the reason replaces the hint …

        Click(g, ThreePoints[2]);
        Assert.Equal(DrawingMenu.AreaHint, tools.ActiveHint);          // … until the next click
    }

    /// <summary>⭐ The route tool is the same gizmo with 2 points and no closing click: Enter finishes a 2-point route, one point says "a route needs 2 points".</summary>
    [Fact]
    public void P2_ARouteNeedsTwoPoints_AndAFirstPointClickDoesNotCloseIt()
    {
        var finished = new List<Vector2[]>();
        var g = new Fdp.Toolkit.Vis2D.Gizmos.PointSequenceGizmo(finished.Add, () => { }, minPoints: 2, closed: false, noun: "a route");
        Click(g, new Vector2(0, 0));
        g.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Empty(finished);
        Assert.Contains("a route needs 2 points", g.Session.Reason);
        Click(g, new Vector2(0.5f, 0.5f));                             // near the first point: an open route just takes another point
        Assert.Equal(2, g.Session.Points.Count);
        g.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Equal(2, Assert.Single(finished).Length);
    }

    /// <summary>
    /// ⛔ <c>R-133</c> — an arm with no sink would draw a shape and drop it. ⭐ It refuses instead.
    /// </summary>
    [Fact]
    public void Arm_WithNoCommitSink_Throws()
    {
        var arm = new AreaAuthoringArm(MakeManager());
        Assert.Throws<ArgumentException>(() => arm.Arm(
            new AreaAuthoringRequest((Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area), string.Empty, null!)));
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
