using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Edits;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Selection;
using Xunit;

namespace Hrot.ScenarioEditor.Tests.Edits;

/// <summary>
/// ⭐ B-17 P5 (📄 docs/UX/DESIGN_Map_Interaction.md §2.1 <c>GroupTransform</c>, §2.5 "multi-select", §3 P5) — move and rotate the WHOLE SELECTION as ONE gesture:
/// a drag on any selected unit moves them all by the same delta; the rotator turns them each about its own centre (headings only) or the group about its centre
/// (positions swing round the centroid); each gesture is ONE undo step — undo restores every unit.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class GroupTransformTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly EditHistory _history = new();
    private readonly EcsSelectionState _selection;
    private readonly List<Entity> _units = new();

    public GroupTransformTests()
    {
        HrotSharedComponentRegistry.RegisterAll(_world);
        _world.RegisterComponent<VehicleState>();
        _world.RegisterComponent<SelectionState>();
        _selection = new EcsSelectionState(_world);   // ⭐ the test is not a production host; the pack registers the shared one in production
        Hrot.ScenarioEditor.Map.MapUiStates.Set(_world, (Fdp.Toolkit.Vis2D.Abstractions.ISelectionState)_selection);
        // five units on a line-ish layout, each facing its own way
        var at = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100), new Vector2(50, 50) };
        var yaw = new[] { 0f, 0.5f, 1.0f, -0.7f, 2.0f };
        for (int i = 0; i < 5; i++)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(at[i], 0f), Rotation = SimMath.FromYaw(yaw[i]) });
            _world.AddComponent(e, new NetworkIdentity { Value = 900 + i });
            _world.AddComponent(e, new SelectionState());
            _units.Add(e);
        }
        _world.Tick();
        EditSteps.Install(_world, new WorldUndoRecorder(_history, new WorldSnapshotRing(), () => true, () => 0.0));
    }

    public void Dispose() => _world.Dispose();

    private Vector3 Pos(int i) => _world.GetComponent<SimTransform>(_units[i]).Position;
    private float Yaw(int i) => SimMath.ExtractYaw(_world.GetComponent<SimTransform>(_units[i]).Rotation);
    private void Pick(params int[] which)
    {
        var es = new List<Entity>(); foreach (var i in which) es.Add(_units[i]);
        _selection.SetMultiple(es);
    }
    private static float AngleDiff(float a, float b) { float d = a - b; return MathF.Atan2(MathF.Sin(d), MathF.Cos(d)); }
    private void AssertUnchanged(Vector3[] pos, float[] yaw)
    {
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(pos[i].X, Pos(i).X, 3); Assert.Equal(pos[i].Y, Pos(i).Y, 3);
            Assert.True(MathF.Abs(AngleDiff(yaw[i], Yaw(i))) < 1e-3f, $"unit {i} heading restored");
        }
    }
    private (Vector3[] pos, float[] yaw) Snapshot()
    {
        var p = new Vector3[5]; var y = new float[5];
        for (int i = 0; i < 5; i++) { p[i] = Pos(i); y[i] = Yaw(i); }
        return (p, y);
    }

    private EntityRotatorGizmo Rotator(int grabbed, IEntityComponentWriter? writer = null, Action<string?>? hint = null)
        => new(_world, _units[grabbed], () => { }, writer, hint);

    // ── the drag ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 Red-proof (compiles): the drag gizmo not taking the group (<c>_group = null</c>) ⇒ only the grabbed unit moves ⇒ red.</summary>
    [Fact]
    public void P5_ADragOnOneOfThreeSelectedUnits_MovesAllThreeByTheSameDelta_AndOnlyThem_AsOneStep()
    {
        Pick(0, 1, 2);
        var (p0, y0) = Snapshot();

        var g = new EntityDragGizmo(_world, _units[1]);
        g.OnInteractionStarted(default, new Vector3(100, 0, 0));
        g.OnDragUpdate(new Vector3(150, 40, 0));
        g.OnDragUpdate(new Vector3(200, 70, 0));
        g.OnCommit(new Vector3(200, 70, 0));

        for (int i = 0; i < 3; i++) { Assert.Equal(p0[i].X + 100f, Pos(i).X, 3); Assert.Equal(p0[i].Y + 70f, Pos(i).Y, 3); }
        for (int i = 3; i < 5; i++) { Assert.Equal(p0[i].X, Pos(i).X, 3); Assert.Equal(p0[i].Y, Pos(i).Y, 3); }
        Assert.Equal(1, _history.Count);
        Assert.True(_history.Undo());
        AssertUnchanged(p0, y0);
    }

    [Fact]
    public void P5_ACancelledGroupDrag_PutsEveryUnitBack_AndRecordsNothing()
    {
        Pick(0, 1, 2);
        var (p0, y0) = Snapshot();
        var g = new EntityDragGizmo(_world, _units[0]);
        g.OnInteractionStarted(default, new Vector3(0, 0, 0));
        g.OnDragUpdate(new Vector3(60, 60, 0));
        g.OnCancel();
        AssertUnchanged(p0, y0);
        Assert.Equal(0, _history.Count);
    }

    [Fact]
    public void P5_ADragOnAnUnselectedUnit_MovesOnlyThatUnit()
    {
        Pick(0, 1);
        var g = new EntityDragGizmo(_world, _units[4]);
        var before = Pos(0);
        g.OnInteractionStarted(default, new Vector3(50, 50, 0));
        g.OnDragUpdate(new Vector3(80, 50, 0));
        g.OnCommit(new Vector3(80, 50, 0));
        Assert.Equal(80f, Pos(4).X, 3);
        Assert.Equal(before, Pos(0));
    }

    private sealed class RecordingWriter : IEntityComponentWriter
    {
        public readonly List<(Entity E, List<EntityAttributeChange> Changes)> Calls = new();
        public EntityWriteRoute Write(Entity entity, ushort attributeId, double value) => Write(entity, new[] { EntityAttributeChange.Double(attributeId, value) });
        public EntityWriteRoute Write(Entity entity, IReadOnlyList<EntityAttributeChange> changes) { Calls.Add((entity, new List<EntityAttributeChange>(changes))); return EntityWriteRoute.Requested; }
    }

    [Fact]
    public void P5_TheGroupDragCommitsOneWritePerUnit_PositionXAndY_InsideOneStep()
    {
        Pick(0, 1, 2);
        var w = new RecordingWriter();
        var g = new EntityDragGizmo(_world, _units[0], w);
        g.OnInteractionStarted(default, new Vector3(0, 0, 0));
        g.OnDragUpdate(new Vector3(30, 20, 0));
        g.OnCommit(new Vector3(30, 20, 0));
        Assert.Equal(3, w.Calls.Count);
        foreach (var (_, ch) in w.Calls) Assert.Equal(2, ch.Count);
        Assert.Equal(1, _history.Count);
    }

    // ── the rotate ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 Red-proofs (compile): the group rotate writing N separate steps (a Begin/End per unit) ⇒ history.Count 5 ⇒ red; the Each mode rotating positions ⇒ the positions assert goes red.</summary>
    [Fact]
    public void P5_RotateFiveEach_EveryHeadingTurnsByTheAngle_PositionsStay_OneUndoStep()
    {
        Pick(0, 1, 2, 3, 4);
        var (p0, y0) = Snapshot();

        var r = Rotator(grabbed: 0);
        Assert.True(r.IsGroup);
        Assert.Equal(GroupRotateMode.Each, r.Mode);
        r.OnDragUpdate(new Vector3(0, 10, 0));            // cursor straight "up" from unit 0: heading 90°, unit 0 was 0 ⇒ +90°
        r.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0, 10, 0));

        float theta = MathF.PI / 2f;
        for (int i = 0; i < 5; i++)
        {
            Assert.True(MathF.Abs(AngleDiff(Yaw(i), y0[i] + theta)) < 1e-3f, $"unit {i} turned by +90°");
            Assert.Equal(p0[i].X, Pos(i).X, 3); Assert.Equal(p0[i].Y, Pos(i).Y, 3);   // each about its OWN centre: nobody moves
        }
        Assert.Equal(1, _history.Count);
        Assert.True(_history.Undo());
        AssertUnchanged(p0, y0);
    }

    [Fact]
    public void P5_RotateFiveAboutTheGroupCentre_PositionsSwingRoundTheCentroid_HeadingsTurnToo_OneUndoStep()
    {
        Pick(0, 1, 2, 3, 4);
        var (p0, y0) = Snapshot();
        var centre = new Vector2(50, 50);                 // the centroid of the five

        var r = Rotator(grabbed: 4);                      // unit 4 sits ON the centre; unit 4 heading 2.0 rad
        r.OnKeyEvent(MapKeyboardKey.Tab, true);
        Assert.Equal(GroupRotateMode.AboutCentre, r.Mode);
        // cursor at 90° of the centre: the grabbed unit's heading goes 2.0 ⇒ 90° (π/2); θ = π/2 − 2.0
        r.OnDragUpdate(new Vector3(50, 150, 0));
        r.OnMouseEvent(MapMouseButton.Left, false, new Vector3(50, 150, 0));

        float theta = MathF.PI / 2f - 2.0f;
        float cos = MathF.Cos(theta), sin = MathF.Sin(theta);
        for (int i = 0; i < 5; i++)
        {
            float dx = p0[i].X - centre.X, dy = p0[i].Y - centre.Y;
            Assert.Equal(centre.X + dx * cos - dy * sin, Pos(i).X, 2);
            Assert.Equal(centre.Y + dx * sin + dy * cos, Pos(i).Y, 2);
            Assert.True(MathF.Abs(AngleDiff(Yaw(i), y0[i] + theta)) < 1e-3f, $"unit {i} heading +θ");
        }
        // the distances from the centre are kept (a rigid swing)
        for (int i = 0; i < 4; i++)
            Assert.Equal(Vector2.Distance(new Vector2(p0[i].X, p0[i].Y), centre), Vector2.Distance(new Vector2(Pos(i).X, Pos(i).Y), centre), 2);
        Assert.Equal(1, _history.Count);
        Assert.True(_history.Undo());
        AssertUnchanged(p0, y0);
    }

    [Fact]
    public void P5_ARotateOnASingleUnit_IsTheOrdinaryOne_AndTheHintNamesTheTabToggleOnlyForAGroup()
    {
        Pick(2);
        var hints = new List<string?>();
        var single = Rotator(grabbed: 2, hint: hints.Add);
        Assert.False(single.IsGroup);
        Assert.Null(single.Hint);

        Pick(0, 1, 2);
        var group = Rotator(grabbed: 2, hint: hints.Add);
        Assert.True(group.IsGroup);
        Assert.Contains("Tab: each / group centre", group.Hint);
        Assert.Contains("now: each", group.Hint);
        group.OnKeyEvent(MapKeyboardKey.Tab, true);
        Assert.Contains("now: group centre", group.Hint);
        group.UpdateAndDraw(_world, 0f, new DebugPrimitiveBuffer(capacity: 32));
        Assert.Contains("now: group centre", hints[^1]);
    }

    [Fact]
    public void P5_TheGroupRotateWritesPerUnit_HeadingOnlyForEach_PositionAndHeadingAboutTheCentre()
    {
        Pick(0, 1, 2);
        var w = new RecordingWriter();
        var gt = new GroupTransform(_world, _selection, w);
        Assert.True(gt.Rotate(0.3f, GroupRotateMode.Each));
        Assert.Equal(3, w.Calls.Count);
        foreach (var (_, ch) in w.Calls) Assert.Equal(AttributeIds.Heading, Assert.Single(ch).AttributeId);

        w.Calls.Clear();
        Assert.True(gt.Rotate(0.3f, GroupRotateMode.AboutCentre));
        Assert.Equal(3, w.Calls.Count);
        foreach (var (_, ch) in w.Calls) Assert.Equal(3, ch.Count);   // PositionX, PositionY, Heading
    }

    [Fact]
    public void P5_GroupTransformMove_IsOneStep_AndNothingIsOpenAfterwards()
    {
        Pick(0, 3);
        var (p0, y0) = Snapshot();
        var gt = new GroupTransform(_world, _selection);
        Assert.True(gt.Move(new Vector2(5, -5)));
        Assert.False(gt.IsOpen);
        Assert.Equal(p0[0].X + 5f, Pos(0).X, 3);
        Assert.Equal(p0[3].Y - 5f, Pos(3).Y, 3);
        Assert.Equal(1, _history.Count);
        _history.Undo();
        AssertUnchanged(p0, y0);
    }

    /// <summary>The gizmos find the ONE selection from the world: the shared pack registers it (no host wires it, no gizmo builds its own).</summary>
    [Fact]
    public void P5_TheSharedPackRegistersTheOneSelection_ForTheGizmosToFind()
    {
        using var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        Assert.Null(Hrot.ScenarioEditor.Map.MapUiStates.SelectionOf(world));
        Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = world });
        Assert.NotNull(Hrot.ScenarioEditor.Map.MapUiStates.SelectionOf(world));
    }

    // ── ⭐ B-17 P7 ⑤ — the look of the rotate ─────────────────────────────────────────────────────────────────────────────────────────────

    private static string TextOf(DebugPrimitiveBuffer b, DebugPrimitive p) => p.StringHash != 0 ? b.InternMap.TryResolve(p.StringHash)! : p.TextContent.ToString();

    /// <summary>
    /// ⭐ B-17 P7 ⑤ (sheet <c>map-gizmo-look-2.svg</c> row ⑤) — a group rotate draws an amber DASHED ring round the pivot, a translucent amber wedge from the start angle to now (a fan of ≤ 24 triangles), the start ray dashed and the current ray solid,
    /// a screen-pixel knob where the current ray meets the ring, and the label <c>group −47°</c> on a backdrop (⛔ no "Shift: 15° steps": the rotator has no Shift snapping). Each mode says <c>each …</c>.
    /// 🔴 Red-proofs (compile): the ring left Solid ⇒ the Dashed assert; the label as Plain ⇒ the Backdrop assert; no wedge ⇒ the triangle count.
    /// </summary>
    [Fact]
    public void P7_ARotate_DrawsDashedAmberRing_Wedge_Rays_Knob_AndABackdropLabel()
    {
        Pick(0, 1, 2, 3, 4);
        var r = Rotator(grabbed: 4);
        r.OnKeyEvent(MapKeyboardKey.Tab, true);
        r.OnDragUpdate(new Vector3(90, 150, 0));
        var buffer = new DebugPrimitiveBuffer();
        r.UpdateAndDraw(_world, 0.016f, buffer);
        var frame = buffer.GetFrame().ToArray();

        var ring = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.Sphere && p.LineStyle == LineStyle.Dashed);
        Assert.Equal(0xF2, ring.Color.R); Assert.Equal(0xC0, ring.Color.G); Assert.Equal(0x33, ring.Color.B);
        Assert.True(ring.ThicknessU16 > 0, "an outline only, no disc");
        int tris = frame.Count(p => p.Shape == DebugPrimitiveShape.FilledTriangle);
        Assert.InRange(tris, 1, 24);
        Assert.All(frame.Where(p => p.Shape == DebugPrimitiveShape.FilledTriangle), p => Assert.True(p.Color.A is > 0 and < 100, "translucent"));
        var lines = frame.Where(p => p.Shape == DebugPrimitiveShape.Line).ToArray();
        Assert.Contains(lines, p => p.LineStyle == LineStyle.Dashed);   // the start ray
        Assert.Contains(lines, p => p.LineStyle == LineStyle.Solid);    // the current ray
        Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.Sphere && p.SizeMode == SizeMode.ScreenPixels && p.SphereRadius == 5f);   // the knob
        var label = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.Text);
        string text = TextOf(buffer, label);
        Assert.Matches(@"^group [+\-−]?\d+°$", text);
        Assert.DoesNotContain("Shift", text);
        Assert.Equal(GizmoTextStyle.Backdrop, label.TextStyle);

        r.OnKeyEvent(MapKeyboardKey.Tab, true);   // back to each
        buffer.Clear(); r.UpdateAndDraw(_world, 0.016f, buffer);
        Assert.StartsWith("each ", TextOf(buffer, Assert.Single(buffer.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Text)));
    }

    /// <summary>⭐ B-17 P7 ⑤ — the single-unit rotate keeps its line and says <c>46°</c> (the degree sign, not "deg") on a backdrop.</summary>
    [Fact]
    public void P7_ASingleUnitRotate_SaysTheCompassBearingWithTheDegreeSign_OnABackdrop()
    {
        Pick(2);
        var r = Rotator(grabbed: 2);
        r.OnDragUpdate(new Vector3(0, 40, 0));
        var buffer = new DebugPrimitiveBuffer();
        r.UpdateAndDraw(_world, 0.016f, buffer);
        var label = Assert.Single(buffer.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Text);
        Assert.Matches(@"^\d+°$", TextOf(buffer, label));
        Assert.Equal(GizmoTextStyle.Backdrop, label.TextStyle);
    }

    /// <summary>
    /// ⭐ B-17 P7 ⑤ — HOT PATH: a steady rotate frame allocates 0 B, in group mode (both modes) and for one unit — the label string is cached per whole degree (it was a new <c>$"…deg"</c> EVERY frame), and the hint is a constant.
    /// 🔴 Red-proof (compiles): <c>LabelFor</c> rebuilding its string every call ⇒ the delta goes red.
    /// </summary>
    [Fact]
    public void P7_ASteadyRotateFrame_Allocates0B()
    {
        Pick(0, 1, 2, 3, 4);
        var hints = 0;
        foreach (var (group, tab) in new[] { (true, false), (true, true), (false, false) })
        {
            if (!group) Pick(2);
            var r = group ? Rotator(grabbed: 4, hint: _ => hints++) : Rotator(grabbed: 2);
            if (tab) r.OnKeyEvent(MapKeyboardKey.Tab, true);
            r.OnDragUpdate(new Vector3(90, 150, 0));
            var buffer = new DebugPrimitiveBuffer();
            r.UpdateAndDraw(_world, 0.016f, buffer);   // warm: the buffer, the interned label
            buffer.Clear(); r.UpdateAndDraw(_world, 0.016f, buffer);
            buffer.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            r.UpdateAndDraw(_world, 0.016f, buffer);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(delta == 0, $"a steady rotate frame (group={group}, aboutCentre={tab}) allocates {delta} B");
        }
        Assert.True(hints > 0);
    }

    // ── the picture ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ B-17 P5 fidelity (§2.6 row "rotate": "a ring around the group for a group rotate") — a GROUP ROTATE on a real frame: the ring round the five units, the arrow from the centre to the cursor,
    /// the units' symbols. Renders <c>group-rotate.png</c> under <c>HROT_RAIL_SHOTS</c> (⛔ never committed); asserts the ring's yellow is drawn in group mode and absent in each mode.
    /// Skips without a display (run under <c>xvfb-run -a</c>).
    /// </summary>
    [SkippableFact]
    public void P5_AGroupRotate_OnARealFrame_ShowsTheRingRoundTheGroup()
    {
        Skip.IfNot(Hrot.Editor.UiFrameRail.UiFrameHarness.IsAvailable(), Hrot.Editor.UiFrameRail.UiFrameHarness.UnavailableReason);
        Pick(0, 1, 2, 3, 4);
        using var frame = Hrot.Editor.UiFrameRail.UiFrameHarness.Begin(800, 420);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        int Yellow(string name, bool group)
        {
            var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
            var camera = new Fdp.Toolkit.Vis2D.Components.MapCamera();
            const float zoom = 2f;
            var target = new Vector2(50f - 400f / zoom, 50f + 190f / zoom);   // the top-left of the view, as the P2 frame rail places it: the group centre lands mid-screen
            camera.ApplyCameraView(new Fdp.Toolkit.Vis2D.Components.MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
            canvas.Camera = camera;
            var buffer = new DebugPrimitiveBuffer();
            var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
            canvas.AddLayer(gizmos);
            var r = Rotator(grabbed: 4);
            if (group) r.OnKeyEvent(MapKeyboardKey.Tab, true);
            r.OnDragUpdate(new Vector3(90, 150, 0));
            r.UpdateAndDraw(_world, 0.016f, buffer);
            gizmos.Update(0.016f);
            frame.StepN(3, canvas.Draw);
            string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-group-rotate");
            System.IO.Directory.CreateDirectory(dir);
            string png = System.IO.Path.Combine(dir, name + ".png");
            frame.Screenshot(png);
            var image = Raylib_cs.Raylib.LoadImage(png);
            int yellow = 0;
            try
            {
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                    {
                        var c = Raylib_cs.Raylib.GetImageColor(image, x, y);
                        if (c.R > 200 && c.G is > 150 and < 235 && c.B < 110) yellow++;   // ⭐ B-17 P7 ⑤ — the amber (0xF2C033) of the dashed ring, the rays and the knob
                    }
            }
            finally { Raylib_cs.Raylib.UnloadImage(image); }
            Console.WriteLine("P5 frame: " + png);
            if (group)
            {
                // ⭐ B-17 P7 ⑤ — the ring is DASHED: sampled at 120 angles on its circle (group centre = screen (400,190) here), amber is on for about half of them, never all and never none.
                //   🔴 Red-proof (compiles): DrawStyledRing drawing every dash with no gap (dashAngle = span) ⇒ every sample is amber ⇒ red.
                float radiusPx = (new GroupTransform(_world, _selection).Radius + 3f) * zoom;
                int on = 0;
                var img = Raylib_cs.Raylib.LoadImage(png);
                try
                {
                    for (int i = 0; i < 120; i++)
                    {
                        float a = i * MathF.PI * 2f / 120f;
                        int x = (int)MathF.Round(400f + MathF.Cos(a) * radiusPx), y = (int)MathF.Round(190f - MathF.Sin(a) * radiusPx);
                        bool hit = false;
                        for (int dy = -1; dy <= 1 && !hit; dy++)
                            for (int dx = -1; dx <= 1 && !hit; dx++)
                            {
                                var c = Raylib_cs.Raylib.GetImageColor(img, x + dx, y + dy);
                                if (c.R > 200 && c.G is > 150 and < 235 && c.B < 110) hit = true;
                            }
                        if (hit) on++;
                    }
                }
                finally { Raylib_cs.Raylib.UnloadImage(img); }
                Assert.InRange(on, 30, 90);
            }
            return yellow;
        }
        int each = Yellow("group-rotate-each", group: false);
        int about = Yellow("group-rotate", group: true);
        // ⭐ B-17 P7 ⑤ — BOTH modes draw the amber dashed ring + wedge + knob now (each mode: a small ring round the grabbed unit; group mode: the ring round the whole group, which is larger)
        Assert.True(each > 120, $"each mode draws its ring, wedge and knob ({each} amber px)");
        Assert.True(about > each + 150, $"the ring round the group is larger in group mode ({about} amber px vs {each} in each mode)");
    }
}
