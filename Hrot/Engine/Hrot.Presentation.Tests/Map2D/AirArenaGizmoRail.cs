using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐⭐ <c>CE-4647</c> (AC-15 step 6; docs/designs/air-attack/DESIGN_Air_Combat.md §3 AC-10, §4d; mockup <c>air-combat-arena.svg</c>) — the air arena's gizmo: its drawing (2-D ring + fill + label, 3-D floor and ceiling rings), its four handles
/// (centre, radius, floor, ceiling) and the ONE validation the form shares. 🔧 Drawn through the data seam <see cref="ArenaShape"/> (the air lane's <c>AirArena</c> component is not merged: re-bind = its projector; see <see cref="AirArenaGizmo"/>).
/// ⛔ The frame tests SKIP without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~AirArenaGizmoRail</c>; the PNGs go to <c>HROT_RAIL_SHOTS</c> as <c>airarena-*.png</c>, never committed.
/// 🔴 Red-proof: the floor ring's emit removed from <see cref="AirArenaGizmo.Draw"/> fails the 3-D frame (no floor).
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class AirArenaGizmoRail
{
    private static readonly ArenaShape Arena = new(new Vector2(1000f, 2000f), 6000f, 150f, 4000f);

    private static int Lines(DebugPrimitiveBuffer b, Rgba32 colour)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Shape == DebugPrimitiveShape.Line && f[i].Color.Equals(colour)) n++;
        return n;
    }

    private static int Any(DebugPrimitiveBuffer b, Rgba32 colour)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Color.Equals(colour)) n++;
        return n;
    }

    private static int Triangles(DebugPrimitiveBuffer b)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Shape == DebugPrimitiveShape.FilledTriangle) n++;
        return n;
    }

    [Fact]
    public void TheDrawing_IsTheTwoRingsTheFillTheTicksAndSpokes_AndTheHandlesOnlyWhileSelected()
    {
        var b = new DebugPrimitiveBuffer(capacity: 1 << 14);
        AirArenaGizmo.Draw(b, Arena, 0f, selected: false);
        Assert.Equal(2 * AirArenaGizmo.Segments, Lines(b, AirArenaGizmo.RingColour));          // the flat ring and the 3-D floor ring
        Assert.Equal(AirArenaGizmo.Segments + 8, Lines(b, AirArenaGizmo.CeilingColour));         // the ceiling ring, four ticks, four floor spokes
        Assert.Equal(AirArenaGizmo.Segments, Triangles(b));                                       // the faint fill
        Assert.Equal(0, Any(b, AirArenaGizmo.HandleColour));                                      // not selected: no handles
        b.Clear();
        AirArenaGizmo.Draw(b, Arena, 0f, selected: true);
        Assert.Equal(4, Any(b, AirArenaGizmo.HandleColour));                                      // a square, a diamond, a flat bar, a tall bar
    }

    [Fact]
    public void TheDrawing_AllocatesNothing_InTheSteadyState()
    {
        var b = new DebugPrimitiveBuffer(capacity: 1 << 14);
        for (int i = 0; i < 20; i++) { b.Clear(); AirArenaGizmo.Draw(b, Arena, 10f, true); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { b.Clear(); AirArenaGizmo.Draw(b, Arena, 10f, true); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
    }

    [Fact]
    public void TheHandles_AreOnTheRing_TheCentreFirst_HitByTheNearest_AndDraggingThemEditsTheFourNumbers()
    {
        Assert.Equal(new Vector3(1000f, 2000f, 5f), AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Centre, 5f));
        Assert.Equal(new Vector3(7000f, 2000f, 0f), AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Radius, 0f));
        var floor = AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Floor, 0f); var ceiling = AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Ceiling, 0f);
        Assert.True(floor.X < 1000f && floor.Y < 2000f && ceiling.X > 1000f && ceiling.Y < 2000f, "south-west and south-east");
        Assert.InRange(Vector2.Distance(new Vector2(floor.X, floor.Y), Arena.Centre), 5999f, 6001f);   // on the ring
        Assert.Equal(ArenaHandle.Centre, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(1010f, 1995f), 40f));
        Assert.Equal(ArenaHandle.Radius, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(6990f, 2020f), 40f));
        Assert.Equal(ArenaHandle.Floor, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(floor.X + 5f, floor.Y), 40f));
        Assert.Equal(ArenaHandle.Ceiling, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(ceiling.X, ceiling.Y - 5f), 40f));
        Assert.Equal(ArenaHandle.None, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(3000f, 3000f), 40f));
        // the drag is RELATIVE to the grab: a press that moves nothing changes nothing
        var c0 = new Vector2(1000f, 2000f);
        Assert.Equal(Arena, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Radius, new Vector2(7000f, 2000f), new Vector2(7000f, 2000f)));
        Assert.Equal(new Vector2(1300f, 1800f), AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Centre, c0, new Vector2(1300f, 1800f)).Centre);        // by the pointer's travel
        Assert.Equal(6500f, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Radius, new Vector2(7000f, 2000f), new Vector2(7500f, 2000f)).Radius);       // +500 m out
        Assert.Equal(250f, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Floor, new Vector2(7000f, 2000f), new Vector2(7400f, 2000f)).FloorM);        // 400 m out × 0.25 = +100
        Assert.Equal(4100f, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Ceiling, new Vector2(7000f, 2000f), new Vector2(7400f, 2000f)).CeilingM);
        Assert.Equal(Arena, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.None, c0, new Vector2(9f, 9f)));
    }

    [Fact]
    public void TheValidation_KeepsTheStageWatchable_RadiusInTheLimits_TheCeilingAboveTheFloor_NaNHealed()
    {
        var c = AirArenaGizmo.Clamp(new ArenaShape(Vector2.Zero, 10f, -5f, 20f));
        Assert.Equal((AirArenaGizmo.MinRadius, 0f), (c.Radius, c.FloorM)); Assert.True(c.CeilingM >= c.FloorM + AirArenaGizmo.MinBand);
        c = AirArenaGizmo.Clamp(new ArenaShape(Vector2.Zero, 1e9f, 5000f, 4000f));
        Assert.Equal(AirArenaGizmo.MaxRadius, c.Radius); Assert.True(c.CeilingM >= c.FloorM + AirArenaGizmo.MinBand, $"{c}");
        c = AirArenaGizmo.Clamp(new ArenaShape(Vector2.Zero, float.NaN, float.NaN, float.NaN));
        Assert.True(float.IsFinite(c.Radius + c.FloorM + c.CeilingM));
        Assert.Equal(Arena, AirArenaGizmo.Clamp(Arena));   // a valid arena is untouched
    }

    [Fact]
    public void TheLabel_IsCultureNeutral_NoDecimalComma_UnderCzech()
    {
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("cs-CZ"); Assert.Equal("AIR ARENA  r 6.0 km  floor 150 m  ceiling 4000 m", AirArenaGizmo.LabelOf(Arena)); }
        finally { CultureInfo.CurrentCulture = old; }
    }

    // ── the real component: the projector, the ops, the one undo step each ──────────────────────────────────────────────────────

    private sealed class Recorder : Fdp.Toolkit.Replication.Edits.IEditStepRecorder
    {
        public readonly System.Collections.Generic.List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static (EntityRepository W, Entity E, Recorder Rec) ArenaWorld(bool selected = false)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<Fdp.Toolkit.Motion.AirArena>(); w.RegisterComponent<Hrot.Map.Common.Components.SelectionState>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(1000f, 2000f, 0f), Rotation = Quaternion.Identity });
        var a = Hrot.UI.Common.AddEntity.AirArenaOps.Default(); a.Centre = new Vector3(1000f, 2000f, 0f); a.RadiusM = 6000f; a.FloorAglM = 150f; a.CeilingAglM = 4000f;
        w.AddComponent(e, a);
        if (selected) w.AddComponent(e, new Hrot.Map.Common.Components.SelectionState { IsSelected = true });
        var rec = new Recorder(); Fdp.Toolkit.Replication.Edits.EditSteps.Install(w, rec);
        return (w, e, rec);
    }

    [Fact]
    public void TheProjector_DrawsTheRealComponent_NoHandlesOfItsOwn_AndTheArenaIsNoUnit()
    {
        var (w, e, _) = ArenaWorld();
        var b = new DebugPrimitiveBuffer(capacity: 1 << 14);
        new AirArenaProjector().Draw(w, e, b);
        Assert.Equal(2 * AirArenaGizmo.Segments, Lines(b, AirArenaGizmo.RingColour)); Assert.Equal(0, Any(b, AirArenaGizmo.HandleColour));
        var (w2, e2, _) = ArenaWorld(selected: true);
        b.Clear(); new AirArenaProjector().Draw(w2, e2, b);
        Assert.Equal(0, Any(b, AirArenaGizmo.HandleColour));   // the handles are the armed Edit Shape tool's (AirArenaHandleGizmo), as a polygon's vertices are
        Assert.True(Hrot.Common.Diagnostics.Gizmos.NotAUnit.Is(w, e));   // drawn by its projector, never as a generic box
        var attr = (Fdp.Toolkit.Diagnostics.Gizmos.GizmoProjectorAttribute)Attribute.GetCustomAttribute(typeof(AirArenaProjector), typeof(Fdp.Toolkit.Diagnostics.Gizmos.GizmoProjectorAttribute))!;
        Assert.Contains(typeof(Fdp.Toolkit.Motion.AirArena), attr.RequiredComponents);
    }

    [Fact]
    public void EveryArenaEdit_IsOneUndoStep_AndNothingChangedIsNoStep_ClampedByTheOneValidation()
    {
        var (w, e, rec) = ArenaWorld();
        var ops = typeof(Hrot.UI.Common.AddEntity.AirArenaOps);
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetRadius(w, e, 3000f));
        Assert.Equal(new[] { "begin Set arena radius", "end" }, rec.Log);
        rec.Log.Clear();
        Assert.False(Hrot.UI.Common.AddEntity.AirArenaOps.SetRadius(w, e, 3000f)); Assert.Empty(rec.Log);                         // nothing changed: no step
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetRadius(w, e, 5f));
        Assert.Equal(AirArenaGizmo.MinRadius, w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e).RadiusM);                            // clamped
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetFloor(w, e, 9000f));
        var a = w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e);
        Assert.True(a.CeilingAglM >= a.FloorAglM + AirArenaGizmo.MinBand, $"floor {a.FloorAglM} ceiling {a.CeilingAglM}");           // the floor and the ceiling never cross
        rec.Log.Clear();
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetShape(w, e, new ArenaShape(new Vector2(5f, 6f), 8000f, 200f, 5000f)));
        Assert.Equal(2, rec.Log.Count);                                                                                              // a gizmo drag's end: ONE step for four numbers
        a = w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e);
        Assert.Equal((5f, 6f, 8000f, 200f, 5000f), (a.Centre.X, a.Centre.Y, a.RadiusM, a.FloorAglM, a.CeilingAglM));
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetMaxShotRange(w, e, 1f)); Assert.Equal(Hrot.UI.Common.AddEntity.AirArenaOps.MinShotRange, w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e).MaxShotRangeM);
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetStalemate(w, e, 30f)); Assert.Equal(30f, w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e).StalemateSeconds);
        Assert.True(Hrot.UI.Common.AddEntity.AirArenaOps.SetLapsesOff(w, e, true)); Assert.Equal((byte)1, w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e).LapsesOff);
        Assert.False(Hrot.UI.Common.AddEntity.AirArenaOps.SetCentre(w, Entity.Null, Vector2.Zero));                                 // not an arena
        Assert.False(Hrot.UI.Common.AddEntity.AirArenaOps.SetRadius(w, e, float.NaN));
    }

    // ── the handles' mouse drag: AirArenaHandleGizmo (armed by Edit Shape on a selected arena) ─────────────────────────────────────

    private static AirArenaHandleGizmo Armed(EntityRepository w, Entity e, out bool removed)
    {
        bool gone = false;
        var g = new AirArenaHandleGizmo(w, e, 7, () => gone = true); g.SetFocus(true);
        removed = gone; return g;
    }

    private static Fdp.Toolkit.Motion.AirArena Comp(EntityRepository w, Entity e) => w.GetComponentRO<Fdp.Toolkit.Motion.AirArena>(e);

    private static void Press(AirArenaHandleGizmo g, ArenaHandle h, Vector2 at) => g.OnInteractionStarted(new GizmoPickToken { AnchorId = 7, SubElementId = (uint)h }, new Vector3(at, 0f));

    [Fact]
    public void ADragOfEachHandle_ChangesExactlyItsField_ByTheDraggedAmount_PreviewedLocally_AndIsOneUndoStepOnRelease()
    {
        var cases = new (ArenaHandle H, Vector2 Grab, Vector2 To, Func<Fdp.Toolkit.Motion.AirArena, (float, float, float, float, float)> Read, (float, float, float, float, float) Want)[]
        {
            (ArenaHandle.Centre, new Vector2(1000f, 2000f), new Vector2(1300f, 1800f), a => (a.Centre.X, a.Centre.Y, a.RadiusM, a.FloorAglM, a.CeilingAglM), (1300f, 1800f, 6000f, 150f, 4000f)),
            (ArenaHandle.Radius, new Vector2(7000f, 2000f), new Vector2(7500f, 2000f), a => (a.Centre.X, a.Centre.Y, a.RadiusM, a.FloorAglM, a.CeilingAglM), (1000f, 2000f, 6500f, 150f, 4000f)),
            (ArenaHandle.Floor, new Vector2(7000f, 2000f), new Vector2(7400f, 2000f), a => (a.Centre.X, a.Centre.Y, a.RadiusM, a.FloorAglM, a.CeilingAglM), (1000f, 2000f, 6000f, 250f, 4000f)),
            (ArenaHandle.Ceiling, new Vector2(7000f, 2000f), new Vector2(7400f, 2000f), a => (a.Centre.X, a.Centre.Y, a.RadiusM, a.FloorAglM, a.CeilingAglM), (1000f, 2000f, 6000f, 150f, 4100f)),
        };
        foreach (var c in cases)
        {
            var (w, e, rec) = ArenaWorld();
            var g = Armed(w, e, out _); var before = c.Read(Comp(w, e));
            Press(g, c.H, c.Grab);
            Assert.Equal(c.H, g.Active);
            g.OnDragUpdate(new Vector3(c.Grab + (c.To - c.Grab) * 0.5f, 0f)); g.OnDragUpdate(new Vector3(c.To, 0f));
            Assert.Equal(before, c.Read(Comp(w, e)));                 // the preview is LOCAL: the component is untouched while dragging
            Assert.Empty(rec.Log);
            Assert.NotNull(g.Preview);
            g.OnCommit(new Vector3(c.To, 0f));
            Assert.Equal(c.Want, c.Read(Comp(w, e)));                 // exactly its field, by the dragged amount
            Assert.Equal(new[] { "begin Edit air arena", "end" }, rec.Log);   // ONE undo step
            Assert.Equal(ArenaHandle.None, g.Active);
        }
    }

    [Fact]
    public void APressThatMovesNothingOrIsCancelled_IsNoEdit_AndAnUnknownHandleIsIgnored()
    {
        var (w, e, rec) = ArenaWorld();
        var g = Armed(w, e, out _);
        Press(g, ArenaHandle.Radius, new Vector2(7000f, 2000f)); g.OnCommit(new Vector3(7000f, 2000f, 0f));
        Assert.Empty(rec.Log);                                        // moved nothing: no step
        Press(g, ArenaHandle.Radius, new Vector2(7000f, 2000f)); g.OnDragUpdate(new Vector3(9000f, 2000f, 0f)); g.OnCancel();
        Assert.Empty(rec.Log); Assert.Equal(6000f, Comp(w, e).RadiusM); Assert.Null(g.Preview);
        Press(g, ArenaHandle.None, Vector2.Zero); Assert.Equal(ArenaHandle.None, g.Active);
        g.OnDragUpdate(new Vector3(1f, 1f, 0f)); g.OnCommit(new Vector3(1f, 1f, 0f)); Assert.Empty(rec.Log);   // no press: nothing
        var floor = AirArenaGizmo.Handles.Position(Hrot.UI.Common.AddEntity.AirArenaOps.ShapeOf(Comp(w, e)), ArenaHandle.Floor, 0f);
        Press(g, ArenaHandle.Floor, new Vector2(floor.X, floor.Y)); g.OnDragUpdate(new Vector3(floor.X - 4000f, floor.Y - 4000f, 0f)); g.OnCommit(new Vector3(floor.X - 4000f, floor.Y - 4000f, 0f));
        var a = Comp(w, e); Assert.True(a.CeilingAglM >= a.FloorAglM + AirArenaGizmo.MinBand);       // a violent drag is clamped
    }

    [Fact]
    public void TheArmedGizmo_DrawsFourPickBoxesOnlyWhileFocused_ThePreviewRingWhileDragging_AndAllocatesNothing()
    {
        var (w, e, _) = ArenaWorld();
        var g = new AirArenaHandleGizmo(w, e, 7, () => { });
        var b = new DebugPrimitiveBuffer(capacity: 1 << 14);
        g.UpdateAndDraw(w, 0.016f, b); int unfocused = b.GetFrame().Length;
        g.SetFocus(true); b.Clear(); g.UpdateAndDraw(w, 0.016f, b); int focused = b.GetFrame().Length;
        Assert.Equal(0, unfocused); Assert.True(focused >= 8, $"four handles, each a pick box + a drawn mark: {focused}");   // an idle armed tool draws only its handles (the projector draws the ring)
        Press(g, ArenaHandle.Radius, new Vector2(7000f, 2000f)); g.OnDragUpdate(new Vector3(7500f, 2000f, 0f));
        b.Clear(); g.UpdateAndDraw(w, 0.016f, b);
        Assert.True(Lines(b, AirArenaGizmo.RingColour) >= 2 * AirArenaGizmo.Segments, "the preview ring is drawn beside the old one");
        for (int i = 0; i < 20; i++) { b.Clear(); g.UpdateAndDraw(w, 0.016f, b); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { b.Clear(); g.UpdateAndDraw(w, 0.016f, b); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
    }

    // ── the real frames ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string ShotsDir()
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-airarena");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static int Count(Image im, Func<Color, bool> p)
    {
        int n = 0;
        for (int y = 0; y < im.Height; y++) for (int x = 0; x < im.Width; x++) if (p(Raylib.GetImageColor(im, x, y))) n++;
        return n;
    }

    private static readonly Color Ground = new(60, 110, 60, 255);
    private static bool IsRing(Color c) => c.B > c.R + 45 && c.B > Ground.B + 45;
    private static bool IsHandle(Color c) => c.R > 220 && c.G > 170 && c.B < 110;

    [SkippableFact]
    public void The2DMap_ShowsTheRingAndTheFaintFill_AndTheFourHandlesOnlyWhenSelected_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const int W = 1000, H = 620; const float Zoom = 0.05f;   // 20 m a pixel: the 6 km radius is 300 px
        using var frame = UiFrameHarness.Begin(W, H);
        Fdp.Presentation.Fonts.GizmoFont.Load();
        string Render(string name, bool selected)
        {
            var canvas = new MapCanvas(input: null); var camera = new MapCamera();
            var target = new Vector2(Arena.Centre.X - W / 2f / Zoom, Arena.Centre.Y + H / 2f / Zoom);
            camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
            canvas.Camera = camera;
            var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
            var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
            canvas.AddLayer(layer);
            buffer.DrawBox2D(Arena.Centre, new Vector2(40000f, 40000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255));
            AirArenaGizmo.Draw(buffer, Arena, 0f, selected);
            layer.Update(0.016f); frame.StepN(3, canvas.Draw);
            string png = Path.Combine(ShotsDir(), name); frame.Screenshot(png); return png;
        }
        var unselected = Raylib.LoadImage(Render("airarena-2d.png", false));
        int ring = Count(unselected, IsRing), handles0 = Count(unselected, IsHandle);
        var centre = Raylib.GetImageColor(unselected, W / 2, H / 2);
        Raylib.UnloadImage(unselected);
        var selectedIm = Raylib.LoadImage(Render("airarena-2d-selected.png", true));
        int handles = Count(selectedIm, IsHandle);
        Raylib.UnloadImage(selectedIm);
        Assert.True(ring > 600, $"the dashed ring (300 px radius): {ring} px");
        Assert.True(centre.B > Ground.B + 3 && centre.B < Ground.B + 40, $"inside the ring the ground is faintly blue-tinted ({centre.R},{centre.G},{centre.B})");
        Assert.Equal(0, handles0);
        Assert.True(handles > 150, $"the four handles in amber: {handles} px");
    }

    [SkippableFact]
    public void The3DMap_ShowsTheFloorRingAndTheCeilingRing_JoinedByTicks_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        var query = WorldQuery.Of(world)!;
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        float gz = query.GroundHeightAt(mid.X, mid.Y);
        var arena = new ArenaShape(mid, 350f, 60f, 260f);
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null); var two = new MapCamera(); canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera };
        gizmos.Renderer3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(gizmos);
        sw.Set(true, animate: false);
        AirArenaGizmo.Draw(buffer, arena, gz, selected: true);
        sw.Camera3D.Pose = new CameraPose(new Vector3(mid.X, mid.Y, gz + 120f), 850f, MathF.PI * 0.5f - 0.2f, -0.28f);
        frame.Step(canvas.Draw); frame.Step(canvas.Draw);
        string png = Path.Combine(ShotsDir(), "airarena-3d.png"); frame.Screenshot(png);
        Assert.True(gizmos.Renderer3D.LinesDrawn > 200, $"3-D gizmo lines: {gizmos.Renderer3D.LinesDrawn}");
        Assert.Equal(0, gizmos.Renderer3D.Skipped.Total);
        var im = Raylib.LoadImage(png);
        int blue = Count(im, c => c.B > c.R + 50 && c.B > c.G + 15 && c.R < 150 && c.G > 90);
        Raylib.UnloadImage(im);
        Assert.True(blue > 300, $"the rings (blue, dashed): {blue} px");
    }
}
