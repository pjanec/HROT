using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.AddEntity;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ CE-4594 — THE TERRAIN EDITOR ACCEPTANCE, in REAL 3-D frames (docs/DESIGN_Terrain_Editor_UX.md §7 items 7, 8, 8c, 9). The terrain is <c>basic-desert</c> — measured: ground −3.0 … 10.0 m over 600 × 600 m, a 10 m height grid, slopes up to 0.31 (<c>city-battle</c>, with 2 … 40 m of relief, was tried first: its 3-D map never finished building in 7 minutes under software GL) — the site is
/// the 60 m patch with the steepest mean slope. Every piece is a PRODUCTION one: the map pack's own layers (<c>MapInteractionPack.AttachMapLayers</c>: the 3-D terrain layer, the pickers incl. <c>TerrainFeaturePicker3D</c>, the gizmo layer), the 3-D camera's
/// <c>ScreenToWorld3D</c> / <c>Pick</c> / <c>WorldToScreen</c> for clicks, <c>ScenarioSpawnAdapter</c> + the <c>PointSequenceGizmo</c> it arms for drawing, <c>LiveOverlay</c> + <c>TerrainResidency</c> (<c>BakeObstacles</c> / <c>CommitObstacles</c>) for the live bake,
/// <c>TerrainFeatureGizmo</c>, <c>VertexEditGizmo</c> and <c>TerrainLock</c>. PNGs go to <c>HROT_RAIL_SHOTS</c> (else the temp folder): <c>ce4594-*.png</c>. <c>xvfb-run -a</c>; skips without a display.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainEditorFrameRail
{
    private readonly ITestOutputHelper _out;
    public TerrainEditorFrameRail(ITestOutputHelper output) => _out = output;
    private void Say(string s) { _out.WriteLine(s); Console.WriteLine("CE4594: " + s); }

    private const string Terrain = "basic-desert", Steep = "city-battle";
    private const int W = 1280, H = 800;

    private static string Shots()
    {
        string d = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } s ? s : Path.Combine(Path.GetTempPath(), "hrot-ce4594");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>The whole stage: the resident terrain in an editor-like world, the production map layers in 3-D, the adapter that draws features.</summary>
    private sealed class Stage : IDisposable
    {
        public readonly EntityRepository World = new();
        public readonly TerrainResidency Residency;
        public readonly string Folder;
        public readonly IWorldQuery Query;
        public readonly MapCanvas Canvas = new(input: null);
        public readonly DebugPrimitiveBuffer Buffer = new(Hrot.ScenarioEditor.Map.MapInteractionContext.DefaultBufferCapacity);
        public readonly Hrot.ScenarioEditor.Map.MapLayers Layers;
        public readonly MapCamera3D Cam;
        public readonly MapCamera Cam2D = new();
        public readonly UiFrameSession Frame;
        public readonly LiveOverlay Live = new();
        public readonly GlobalGizmoManager Manager;
        public readonly ScenarioSpawnAdapter Adapter;
        public Vector2 Site; public float Slope;
        private int _net = 100;

        public Stage(string terrain = Terrain)
        {
            string root = ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!);
            var catalog = new TerrainCatalog(new[] { root });
            Folder = Path.GetDirectoryName(catalog.ResolveDefinition(terrain))!;
            HrotSharedComponentRegistry.RegisterAll(World);
            World.RegisterManagedComponent<EditablePolyline>();
            World.RegisterComponent<SelectionState>();
            Residency = new TerrainResidency(catalog, new CarKinem.Road.RoadNetworkHolder());
            Residency.Commit(World, Residency.Prepare(terrain, null));
            Query = WorldQuery.Of(World)!;
            var tw = World.GetSingletonManaged<TerrainWorld>()!;
            (Site, Slope) = SteepestPatch(Query, tw.BoundsMin + new Vector2(60f), tw.BoundsMax - new Vector2(60f));

            Frame = UiFrameHarness.Begin(W, H);
            LitShader.Reset();
            Canvas.Camera = Cam2D;
            Layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(Canvas, Buffer, new FdpEventBus(), () => World, map3DBuildPolicy: Fdp.Toolkit.Vis3D.Map3DBuildPolicy.OnFirst3DRequest);
            Layers.ViewSwitch.Set(true);
            Cam = Layers.ViewSwitch.Camera3D;
            Tick();
            // the map pack builds the 3-D map on a worker (OnFirst3DRequest): the 2-D map says "Building 3-D map…" until it is built, then the view switches
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (!(Layers.ViewSwitch.Is3D && !Layers.ViewSwitch.IsSwitching && Layers.Terrain.TriangleCount > 1000) && started.Elapsed.TotalSeconds < 180) { Tick(); Thread.Sleep(50); }
            Console.WriteLine($"CE4594: the 3-D map was built after {started.Elapsed.TotalSeconds:0.0} s");
            Assert.True(Layers.ViewSwitch.Is3D, $"the 3-D view is on (built {Layers.Map3D?.IsBuilt}, step '{Layers.Map3D?.Step}', failure '{Layers.Map3D?.Failure}', pending {Layers.ViewSwitch.Pending3D}, terrain tris {Layers.Terrain.TriangleCount})");

            var tkb = HrotEnvironment.CreateTkb();
            Manager = new GlobalGizmoManager(new DebugPrimitiveBuffer());
            Adapter = new ScenarioSpawnAdapter(Hrot.TestShared.TestEntityCreation.For(World, tkb), tkbDb: tkb, globalGizmoManager: Manager);
        }

        public void Dispose() { Frame.Dispose(); World.Dispose(); }

        /// <summary>One frame, as the host's loop runs it: the canvas updates its layers (the 3-D map builds on a worker driven from there) and draws.</summary>
        public void Tick() => Frame.Step(() => { Canvas.Update(1f / 30f); Canvas.Draw(); });

        /// <summary>The 60 m patch (interior of the map) whose mean ground gradient is greatest — the slope the rails draw on.</summary>
        private static (Vector2 At, float Slope) SteepestPatch(IWorldQuery q, Vector2 lo, Vector2 hi)
        {
            Vector2 best = default; float bestSlope = 0f;
            float step = MathF.Max(10f, (hi.X - lo.X) / 60f);
            for (float cx = lo.X; cx <= hi.X; cx += step)
                for (float cy = lo.Y; cy <= hi.Y; cy += step)
                {
                    float sum = 0f; int n = 0;
                    for (float dx = -30f; dx <= 30f; dx += 10f) for (float dy = -30f; dy <= 30f; dy += 10f)
                    {
                        float gx = (q.GroundHeightAt(cx + dx + 5f, cy + dy) - q.GroundHeightAt(cx + dx - 5f, cy + dy)) / 10f, gy = (q.GroundHeightAt(cx + dx, cy + dy + 5f) - q.GroundHeightAt(cx + dx, cy + dy - 5f)) / 10f;
                        sum += MathF.Sqrt(gx * gx + gy * gy); n++;
                    }
                    if (sum / n > bestSlope) { bestSlope = sum / n; best = new Vector2(cx, cy); }
                }
            return (best, bestSlope);
        }

        public float Ground(float x, float y) => Query.GroundHeightAt(x, y);

        /// <summary>An oblique pose over the site: from the south-west, looking north-east, down 29°.</summary>
        public void Oblique(float distance = 75f)
        {
            Cam.Pose = new CameraPose(new Vector3(Site.X, Site.Y, Ground(Site.X, Site.Y) + 2f), distance, MathF.PI * 0.30f, -0.50f);
            for (int i = 0; i < 4; i++) { Cam.Update(1f / 30f); Tick(); }
        }

        /// <summary>The picker's grid builds on a worker (R-1046): steps frames, as the loop would, until a pick at the screen centre hits the TERRAIN (not the look-at plane fallback). Returns how many frames it took.</summary>
        public int WaitForPicks()
        {
            for (int i = 0; i < 1500; i++)
            {
                Cam.InvalidatePicks();
                if (Cam.Pick(new Vector2(W / 2f, H / 2f), PickFilter.Terrain).Kind == PickKind.Terrain) return i;
                Tick(); Thread.Sleep(20);
            }
            Assert.Fail("the terrain picker never became ready");
            return -1;
        }

        /// <summary>Steps frames (the terrain layer rebuilds on a worker) until the layer's triangle count moved from <paramref name="before"/> or ~8 s passed.</summary>
        public void Settle(int minFrames = 6)
        {
            for (int i = 0; i < minFrames; i++) { Tick(); Thread.Sleep(30); }
            for (int i = 0; i < 120; i++) { Tick(); Thread.Sleep(30); }
        }

        /// <summary>Makes the entity a creation request would: position, feature, polyline, guid, network id.</summary>
        public Entity Realise(EntityCreationRequest req)
        {
            var e = World.CreateEntity();
            foreach (var c in req.InitialComponents!)
                switch (c)
                {
                    case SimTransform t: World.AddComponent(e, t); break;
                    case TerrainFeature f: World.AddComponent(e, f); break;
                    case EditablePolyline p: World.SetManagedComponent(e, p); break;
                    case EntityInfo i: World.AddComponent(e, i); break;
                }
            World.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            World.AddComponent(e, new NetworkIdentity(_net++));
            World.AddComponent(e, new SelectionState());
            return e;
        }

        /// <summary>The live bake, by the production pieces: the overlay expanded over the base the residency holds, baked and committed into the world.</summary>
        public LiveOverlayResult Bake()
        {
            var (b, ver, folder) = Residency.ExpansionBase();
            var res = Live.Expand(Live.Snapshot(World), b, ver, folder)!;
            Assert.NotNull(res);
            var bake = Residency.BakeObstacles(Array.Empty<TerrainPrism>(), null, res);
            Assert.True(Residency.CommitObstacles(World, bake), "the bake was committed");
            return res;
        }

        public PointSequenceGizmo ActiveSequence()
        {
            var field = typeof(GlobalGizmoManager).GetField("_activeGizmos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return ((System.Collections.IDictionary)field.GetValue(Manager)!).Values.Cast<object>().OfType<PointSequenceGizmo>().Single();
        }

        public EntityCreationRequest DrawFence(IReadOnlyList<Vector2> pixels, out List<Vector3> picked)
        {
            var pick = TerrainPickCatalog.FeaturePicks().Single(p => p.Name == "Fence");
            Adapter.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, TerrainBirth.PlacementJson(pick));
            var g = ActiveSequence();
            picked = new List<Vector3>();
            foreach (var px in pixels)
            {
                var p = Cam.ScreenToWorld3D(px);                       // the production pick: the point on the relief under the pixel
                picked.Add(p);
                g.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, p);
            }
            g.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Enter, true);
            World.Bus.SwapBuffers();
            return World.Bus.ReadManaged<EntityCreationRequest>().Last();
        }
    }

    /// <summary>
    /// ⭐ A — THE USER'S SHIFT REPORT, measured. From an OBLIQUE 3-D camera over the steepest slope, three screen pixels are clicked into fence points (the camera's own pick), the line finished and baked live. (1) every stored point (the entity's position + its polyline) is
    /// the relief point the camera picked; (2) the baked panels' ends lie on those points and every piece's base is the ground under it (not 0); (3) the camera's <c>WorldToScreen</c> of each baked base end lands on the pixel that was clicked.
    /// </summary>
    [SkippableFact]
    public void A_AFenceClickedOnASlope_IsStoredOnTheReliefPoint_BakedOnTheGround_AndDrawnUnderTheClickedPixels()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        RunShift(Terrain, maxPixels: 3f, png: "ce4594-a-fence-on-slope");
    }

    /// <summary>
    /// ⭐ A2 — THE SAME MEASURE ON THE STEEPEST COMMITTED RELIEF (<c>city-battle</c>, mean slope ≈ 0.38 at the site, 2 … 40 m of ground): (1) and (2) hold to the millimetre; (3) is the FINDING — a baked piece's base is ONE height (CE-5015 steps a wall into ≤ 2 m pieces), so where a piece
    /// ends at a stored point from the downhill side its base sits up to <c>slope × piece length</c> BELOW the ground there, and the camera draws that base end that many pixels BELOW the clicked pixel (vertical on screen, never sideways). Asserted only to the bound the stepping explains.
    /// </summary>
    [SkippableFact]
    public void A2_OnTheSteepestRelief_TheBakedBaseEndIsBelowTheClick_ByTheStepOfItsPiece()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        RunShift(Steep, maxPixels: 14f, png: "ce4594-a2-fence-on-steep-slope");
    }

    private void RunShift(string terrain, float maxPixels, string png)
    {
        using var s = new Stage(terrain);
        Say($"terrain {terrain}: site {s.Site} mean slope {s.Slope:0.00}, ground there {s.Ground(s.Site.X, s.Site.Y):0.0} m");
        s.Oblique();
        Say($"the picker was ready after {s.WaitForPicks()} frames");
        var cx = W / 2f; var cy = H / 2f;
        var pixels = new[] { new Vector2(cx - 170f, cy + 90f), new Vector2(cx + 10f, cy + 10f), new Vector2(cx + 190f, cy - 60f) };
        var req = s.DrawFence(pixels, out var picked);
        foreach (var p in picked) Say($"the camera picked ({p.X:0.000}, {p.Y:0.000}) at z {p.Z:0.000}; the ground function there {s.Ground(p.X, p.Y):0.000} (Δ {p.Z - s.Ground(p.X, p.Y):0.000} m)");
        Assert.All(picked, p => Assert.InRange(p.Z, s.Ground(p.X, p.Y) - 1.5f, s.Ground(p.X, p.Y) + 1.5f));
        var e = s.Realise(req);

        // (1) what is stored is what the camera picked
        var at = s.World.GetComponentRO<SimTransform>(e).Position; var poly = ((Fdp.ModuleHost.Abstractions.ISimulationView)s.World).GetManagedComponentRO<EditablePolyline>(e)!;
        Assert.Equal(3, poly.Points.Count);
        float worst1 = 0f;
        for (int i = 0; i < 3; i++)
        {
            var stored = new Vector2(at.X, at.Y) + poly.Points[i];
            float d = Vector2.Distance(stored, new Vector2(picked[i].X, picked[i].Y)); worst1 = MathF.Max(worst1, d);
            Say($"point {i}: picked ({picked[i].X:0.000}, {picked[i].Y:0.000}, z {picked[i].Z:0.00}) stored ({stored.X:0.000}, {stored.Y:0.000}) Δ {d:0.0000} m");
        }
        Assert.True(worst1 <= 0.05f, $"(1) a stored point is the picked relief point ({worst1:0.0000} m)");

        // the live bake, then the frame
        var res = s.Bake();
        s.Settle();
        s.Frame.Screenshot(Path.Combine(Shots(), png + ".png"));
        var merged = s.World.GetSingletonManaged<TerrainWorld>()!;
        var pieces = merged.Panels.Where(p => p.Material?.Name == "fence-wood" && p.Label is { } l && l.StartsWith(s.World.GetComponentRO<EntityGuid>(e).Value.ToString("D"))).ToList();
        Say($"the bake made {pieces.Count} fence pieces");
        Assert.True(pieces.Count >= 3, "the fence was baked");

        // (2) the baked ends lie on the stored points; each piece's base is the ground under it, not 0
        float worstEnd = 0f, worstBase = 0f;
        foreach (var p in pieces)
        {
            float gA = s.Ground(p.A.X, p.A.Y), gB = s.Ground(p.B.X, p.B.Y);
            float lo = MathF.Min(gA, gB), hi = MathF.Max(gA, gB);
            float off = p.BaseZ < lo ? lo - p.BaseZ : p.BaseZ > hi ? p.BaseZ - hi : 0f; worstBase = MathF.Max(worstBase, off);
            Assert.True(MathF.Abs(p.BaseZ) > 0.5f || MathF.Abs(lo) < 0.5f, $"(2) a piece's base is not 0 on a slope ({p.BaseZ:0.00})");
        }
        for (int i = 0; i < 3; i++)
        {
            var stored = new Vector2(at.X, at.Y) + poly.Points[i];
            float d = pieces.Min(p => MathF.Min(Vector2.Distance(p.A, stored), Vector2.Distance(p.B, stored))); worstEnd = MathF.Max(worstEnd, d);
        }
        Say($"(2) worst piece end from a stored point {worstEnd:0.0000} m; worst base outside its ends' ground range {worstBase:0.000} m");
        Assert.True(worstEnd <= 0.05f, $"(2) a baked end lies on its stored point ({worstEnd:0.0000} m)");
        Assert.True(worstBase <= 0.3f, $"(2) each piece's base is the ground under it ({worstBase:0.000} m)");

        // every piece that ends at a stored point: its base vs the ground there, and where that base end is drawn
        for (int i = 0; i < 3; i++)
        {
            var v = new Vector2(at.X, at.Y) + poly.Points[i]; float gv = s.Ground(v.X, v.Y);
            foreach (var p in pieces.Where(p => Vector2.Distance(p.A, v) < 0.05f || Vector2.Distance(p.B, v) < 0.05f))
            {
                var other = Vector2.Distance(p.A, v) < 0.05f ? p.B : p.A;
                var scrB = s.Cam.WorldToScreen(new Vector3(v.X, v.Y, p.BaseZ));
                Say($"    vertex {i} (ground {gv:0.00}): piece to ({other.X:0.00}, {other.Y:0.00}) base {p.BaseZ:0.00} top {p.TopZ:0.00} (base − ground at the vertex {p.BaseZ - gv:0.00} m; ground at its far end {s.Ground(other.X, other.Y):0.00}); base end on screen {scrB} = {Vector2.Distance(scrB, pixels[i]):0.0} px from the click");
            }
        }
        {   // the PNG with the clicked pixels ringed
            var img = Raylib.LoadImage(Path.Combine(Shots(), png + ".png"));
            foreach (var px in pixels) { Raylib.ImageDrawCircleLines(ref img, (int)px.X, (int)px.Y, 9, new Color(255, 0, 255, 255)); Raylib.ImageDrawCircleLines(ref img, (int)px.X, (int)px.Y, 10, new Color(255, 0, 255, 255)); }
            Raylib.ExportImage(img, Path.Combine(Shots(), png + "-marked.png")); Raylib.UnloadImage(img);
        }

        // (3) the camera projects each baked base end back onto the pixel that was clicked
        float worst3 = 0f;
        for (int i = 0; i < 3; i++)
        {
            var stored = new Vector2(at.X, at.Y) + poly.Points[i];
            var piece = pieces.OrderBy(p => MathF.Min(Vector2.Distance(p.A, stored), Vector2.Distance(p.B, stored))).First();
            var scr = s.Cam.WorldToScreen(new Vector3(stored.X, stored.Y, piece.BaseZ));
            float dpx = Vector2.Distance(scr, pixels[i]); worst3 = MathF.Max(worst3, dpx);
            var back = s.Cam.WorldToScreen(new Vector3(picked[i].X, picked[i].Y, picked[i].Z));
            Say($"(3) point {i}: clicked pixel {pixels[i]} → baked base end at screen {scr} (Δ {dpx:0.00} px; the picked point itself projects to {back})");
        }
        Assert.True(worst3 <= maxPixels, $"(3) a baked base end is drawn within {maxPixels} px of the clicked pixel (worst {worst3:0.00} px)");
    }

    // ── shared helpers of rails B–E ─────────────────────────────────────────────────────────────────────────────────

    private static readonly Vector2[] FencePixels = { new(W / 2f - 170f, H / 2f + 90f), new(W / 2f + 10f, H / 2f + 10f), new(W / 2f + 190f, H / 2f - 60f) };

    /// <summary>A fence clicked onto the slope, realised as the entity a creation makes, baked live.</summary>
    private Entity FenceOnSlope(Stage s, out List<Vector3> picked)
    {
        s.Oblique(); s.WaitForPicks();
        var e = s.Realise(s.DrawFence(FencePixels, out picked));
        s.Bake();
        return e;
    }

    private static Vector3[] AbsolutePoints(Stage s, Entity e)
    {
        var at = s.World.GetComponentRO<SimTransform>(e).Position; var poly = ((Fdp.ModuleHost.Abstractions.ISimulationView)s.World).GetManagedComponentRO<EditablePolyline>(e)!;
        return poly.Points.Select(p => new Vector3(at.X + p.X, at.Y + p.Y, 0f)).ToArray();
    }

    private sealed class Capture : IGizmoSink3D
    {
        public readonly List<Vector3> Vertices = new(); public int Lines, Triangles, Spheres;
        public void Line(Vector3 a, Vector3 b, Rgba32 ca, Rgba32 cb) { Vertices.Add(a); Vertices.Add(b); Lines++; }
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour) { Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Triangles++; }
        public void Sphere(Vector3 centre, float radius, Rgba32 colour, Rgba32 fill) { Vertices.Add(centre); Spheres++; }
    }

    /// <summary>What the 3-D gizmo drawer emits for <paramref name="buffer"/>'s primitives, by the production triage + renderer over the camera's ground.</summary>
    private static Capture Emit3D(Stage s, DebugPrimitiveBuffer buffer)
    {
        var triage = new DebugPrimitiveTriage3D(); var into = new List<TriagedPrimitive3D>();
        triage.Triage(buffer.GetFrame().ToArray(), into);
        var r = new GizmoRenderer3D { GroundHeight = s.Cam.GroundHeight };
        var sink = new Capture();
        r.Draw(into, p => s.Cam.ZoomAt(p), sink);
        return sink;
    }

    private static float WorstOffGround(Stage s, Capture c) => c.Vertices.Count == 0 ? 0f : c.Vertices.Max(v => MathF.Abs(v.Z - s.Ground(v.X, v.Y)));

    private static void Mark(string png, IEnumerable<Vector2> pixels)
    {
        var img = Raylib.LoadImage(Path.Combine(Shots(), png + ".png"));
        foreach (var px in pixels) { Raylib.ImageDrawCircleLines(ref img, (int)px.X, (int)px.Y, 9, new Color(255, 0, 255, 255)); Raylib.ImageDrawCircleLines(ref img, (int)px.X, (int)px.Y, 10, new Color(255, 0, 255, 255)); }
        Raylib.ExportImage(img, Path.Combine(Shots(), png + "-marked.png")); Raylib.UnloadImage(img);
    }

    /// <summary>
    /// ⭐ B — DRAPED MARKS (item 8): with the Terrain toolbar OPEN, the feature gizmo's outline, the drawing ghost (a thick line, a dot per tree) and the Edit Shape handles are drawn ON THE RELIEF in 3-D — every vertex the production 3-D gizmo drawer emits is within 0.5 m of the ground
    /// (the drawer drapes a z = 0 shape on the ground and lifts it a little). Closed, the feature draws nothing. 🔴 Red-proof: the renderer without the ground function ⇒ every vertex at 0 ⇒ far off the slope ⇒ red.
    /// </summary>
    [SkippableFact]
    public void B_TheFeatureOutline_TheGhostAndTheEditHandles_AreDrapedOnTheRelief()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = new Stage();
        var fence = FenceOnSlope(s, out _);
        var pts = AbsolutePoints(s, fence);
        var gizmo = new TerrainFeatureGizmo();

        TerrainLock.SetOpen(s.World, false);
        var closed = new DebugPrimitiveBuffer(); gizmo.Draw(s.World, fence, closed);
        Assert.Empty(closed.GetFrame().ToArray());                                       // the toolbar closed: no mark

        TerrainLock.SetOpen(s.World, true);
        var ghost = new TerrainDrawGhost(() => s.World);
        var wallOptions = TerrainDrawOptions.From("feature:3::Line")!; var rowOptions = TerrainDrawOptions.From("feature:14:tree:Line")!;
        var edit = new VertexEditGizmo(s.World, fence, s.World.GetComponentRO<NetworkIdentity>(fence).Value, () => { });
        edit.SetFocus(true);

        float Check(string what, Action<DebugPrimitiveBuffer> draw)
        {
            var b = new DebugPrimitiveBuffer(); draw(b);
            var c = Emit3D(s, b);
            float worst = WorstOffGround(s, c);
            Say($"B {what}: {c.Lines} lines, {c.Triangles} triangles, {c.Spheres} spheres, {c.Vertices.Count} vertices, worst {worst:0.000} m off the ground");
            Assert.True(c.Vertices.Count > 0, $"{what} drew something in 3-D");
            Assert.True(worst <= 0.5f, $"B {what}: every vertex within 0.5 m of the ground (worst {worst:0.000} m)");
            return worst;
        }
        var line = pts.Select(p => new Vector2(p.X, p.Y)).ToArray();
        Check("the feature outline", b => gizmo.Draw(s.World, fence, b));
        Check("the fence ghost", b => ghost.UnderlayOf(wallOptions)(b, line));
        Check("the tree-row ghost", b => ghost.UnderlayOf(rowOptions)(b, line));
        Check("the Edit Shape handles", b => edit.UpdateAndDraw(s.World, 0.016f, b));

        // the frame: all of it at once, in the production gizmo layer
        s.Buffer.Clear();
        gizmo.Draw(s.World, fence, s.Buffer); ghost.UnderlayOf(rowOptions)(s.Buffer, line); edit.UpdateAndDraw(s.World, 0.016f, s.Buffer);
        s.Layers.GizmoLayer.Update(0.016f);
        s.Settle(4);
        s.Frame.Screenshot(Path.Combine(Shots(), "ce4594-b-draped-marks.png"));

        // red-proof evidence: without the ground function the same outline lies at z = 0 — far off the relief
        var flat = new DebugPrimitiveBuffer(); gizmo.Draw(s.World, fence, flat);
        var triage = new DebugPrimitiveTriage3D(); var into = new List<TriagedPrimitive3D>(); triage.Triage(flat.GetFrame().ToArray(), into);
        var noGround = new Capture(); new GizmoRenderer3D().Draw(into, _ => 10f, noGround);
        float undraped = noGround.Vertices.Max(v => MathF.Abs(v.Z - s.Ground(v.X, v.Y)));
        Say($"B without the ground function the outline is {undraped:0.0} m off the relief");
        Assert.True(undraped > 3f, "the rail can tell a draped outline from an undraped one");
    }

    /// <summary>
    /// ⭐ C — A TREE ROW ON THE SLOPE (items 8 / 8c): a Row baked on the relief, picked from an oblique view by clicking a tree's CANOPY — the camera's <c>Pick(PickFilter.Entities)</c> answers the Row's entity (the baked tree prisms stand on the ground, so the pixel is the canopy's);
    /// with the toolbar CLOSED the same pixel picks no entity (the click falls to the ground behind) and the feature draws no mark. 🔴 Red-proof: the picker ignoring the lock ⇒ the closed assert fails.
    /// </summary>
    [SkippableFact]
    public void C_AClickOnATreesCanopy_PicksTheRow_OpenOnly_AndNoMarkIsDrawnClosed()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = new Stage();
        s.Oblique(); s.WaitForPicks();
        var pick = TerrainPickCatalog.FeaturePicks().Single(p => p.Name == "Tree row");
        s.Adapter.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, TerrainBirth.PlacementJson(pick));
        var g = s.ActiveSequence();
        foreach (var px in new[] { FencePixels[0] + new Vector2(30f, 10f), FencePixels[2] - new Vector2(30f, 10f) })
            g.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, s.Cam.ScreenToWorld3D(px));
        g.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Enter, true);
        s.World.Bus.SwapBuffers();
        var row = s.Realise(s.World.Bus.ReadManaged<EntityCreationRequest>().Last());
        int net = s.World.GetComponentRO<NetworkIdentity>(row).Value; string guid = s.World.GetComponentRO<EntityGuid>(row).Value.ToString("D");
        s.Bake();
        var live = s.World.GetSingletonManaged<TerrainWorld>()!;
        var trees = live.Prisms.Where(p => p.Kind == TerrainPrismKind.Tree && p.Label is { } l && l.StartsWith(guid)).ToList();
        Say($"C the row baked {trees.Count} trees; ground at the site {s.Ground(s.Site.X, s.Site.Y):0.0} m");
        Assert.True(trees.Count >= 4, "the row was baked as lone trees");
        s.Settle(4);

        // the canopy of a tree in the middle of the row, as the camera sees it
        var t = trees[trees.Count / 2]; var c = (t.Min + t.Max) * 0.5f; float zc = t.BaseZ + (t.TopZ - t.BaseZ) * 0.75f;
        Assert.True(MathF.Abs(t.BaseZ - s.Ground(c.X, c.Y)) <= 0.6f, $"C the tree stands on the ground (base {t.BaseZ:0.00}, ground {s.Ground(c.X, c.Y):0.00})");
        var canopy = s.Cam.WorldToScreen(new Vector3(c.X, c.Y, zc));
        TerrainLock.SetOpen(s.World, true); s.Cam.InvalidatePicks();
        var open = s.Cam.Pick(canopy, PickFilter.Entities);
        Say($"C open: a click at the canopy pixel {canopy} picks {open.Kind} net {open.NetworkId} (the row is {net})");
        Assert.Equal(PickKind.Entity, open.Kind); Assert.Equal((long)net, open.NetworkId);
        var gizmo = new TerrainFeatureGizmo(); var marks = new DebugPrimitiveBuffer(); gizmo.Draw(s.World, row, marks);
        Assert.NotEmpty(marks.GetFrame().ToArray());                                     // open: the outline is drawn
        s.Buffer.Clear(); gizmo.Draw(s.World, row, s.Buffer); s.Layers.GizmoLayer.Update(0.016f); s.Settle(3);
        s.Frame.Screenshot(Path.Combine(Shots(), "ce4594-c-tree-row-open.png")); Mark("ce4594-c-tree-row-open", new[] { canopy });

        TerrainLock.SetOpen(s.World, false); s.Cam.InvalidatePicks();
        var closed = s.Cam.Pick(canopy, PickFilter.Entities);
        var any = s.Cam.Pick(canopy, PickFilter.All);
        Say($"C closed: the same pixel picks {closed.Kind} (all filters: {any.Kind} at {any.Point})");
        Assert.NotEqual(PickKind.Entity, closed.Kind);
        Assert.NotEqual((long)net, any.NetworkId);
        var none = new DebugPrimitiveBuffer(); gizmo.Draw(s.World, row, none);
        Assert.Empty(none.GetFrame().ToArray());                                         // closed: no mark
        s.Buffer.Clear(); s.Layers.GizmoLayer.Update(0.016f); s.Settle(3);
        s.Frame.Screenshot(Path.Combine(Shots(), "ce4594-c-tree-row-closed.png")); Mark("ce4594-c-tree-row-closed", new[] { canopy });
    }

    private sealed class UndoRig
    {
        public readonly Hrot.Presentation.Edits.EditHistory History = new(); public double Now;
        public UndoRig(EntityRepository w)
        {
            w.Tick();
            EditSteps.Install(w, new Hrot.Presentation.Edits.WorldUndoRecorder(History, new WorldSnapshotRing(), () => true, () => Now));
        }
    }

    private static GizmoPickToken Token(int net, uint sub) => new() { AnchorId = net, SubElementId = sub };

    /// <summary>
    /// ⭐ D — EDIT SHAPE (item 9) on the fence, in 3-D then in 2-D: the CENTRE handle dragged 10 m, then one END point — each ONE undo step; after the re-bake every piece still stands on the ground at the new place and its ends are the new points; the two undos bring the old
    /// fence back. The drags are fed through the CAMERA's screen→world pick (3-D) and the 2-D camera's (2-D), as the terminal does. 🔴 Red-proof: no <c>EditSteps</c> bracket in the gizmo's write-back ⇒ the history stays empty ⇒ red.
    /// </summary>
    [SkippableFact]
    public void D_EditShape_MovesTheFenceAndAnEndPoint_OneStepEach_AndItStandsOnTheGroundAtTheNewPlace_In3DAnd2D()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = new Stage();
        TerrainLock.SetOpen(s.World, true);
        var fence = FenceOnSlope(s, out _);
        int net = s.World.GetComponentRO<NetworkIdentity>(fence).Value; string guid = s.World.GetComponentRO<EntityGuid>(fence).Value.ToString("D");
        var rig = new UndoRig(s.World);
        var original = AbsolutePoints(s, fence).Select(p => new Vector2(p.X, p.Y)).ToArray();

        void StandsOnTheGround(string when, IReadOnlyList<Vector2> expected)
        {
            s.Bake();
            var pieces = s.World.GetSingletonManaged<TerrainWorld>()!.Panels.Where(p => p.Material?.Name == "fence-wood" && p.Label is { } l && l.StartsWith(guid)).ToList();
            Assert.True(pieces.Count >= 3, $"{when}: the fence was re-baked");
            float worst = 0f, worstEnd = 0f;
            foreach (var p in pieces)
            {
                float gA = s.Ground(p.A.X, p.A.Y), gB = s.Ground(p.B.X, p.B.Y), lo = MathF.Min(gA, gB), hi = MathF.Max(gA, gB);
                worst = MathF.Max(worst, p.BaseZ < lo ? lo - p.BaseZ : p.BaseZ > hi ? p.BaseZ - hi : 0f);
            }
            foreach (var v in expected) worstEnd = MathF.Max(worstEnd, pieces.Min(p => MathF.Min(Vector2.Distance(p.A, v), Vector2.Distance(p.B, v))));
            Say($"D {when}: {pieces.Count} pieces, worst base outside its ends' ground {worst:0.000} m, worst end from an edited point {worstEnd:0.0000} m");
            Assert.True(worst <= 0.3f, $"{when}: the pieces stand on the ground ({worst:0.000} m)");
            Assert.True(worstEnd <= 0.05f, $"{when}: the pieces end at the edited points ({worstEnd:0.0000} m)");
        }

        // ── 3-D: drag the centre handle 10 m east, through the camera's screen → world ──
        using (var gizmo = new VertexEditGizmo(s.World, fence, net, () => { }))
        {
            gizmo.SetFocus(true);
            var c = gizmo.Session.Centre;
            var from = s.Cam.WorldToScreen(new Vector3(c.X, c.Y, s.Ground(c.X, c.Y)));
            var to = s.Cam.WorldToScreen(new Vector3(c.X + 10f, c.Y, s.Ground(c.X + 10f, c.Y)));
            gizmo.OnInteractionStarted(Token(net, ShapeEditSession.CentreHandleId), s.Cam.ScreenToWorld3D(from));
            rig.Now += 5; gizmo.OnDragUpdate(s.Cam.ScreenToWorld3D(to, forDrag: true));
            gizmo.OnCommit(Vector3.Zero);
        }
        Assert.Equal(1, rig.History.Count);
        var moved = AbsolutePoints(s, fence).Select(p => new Vector2(p.X, p.Y)).ToArray();
        float shift = Vector2.Distance(moved[0], original[0]);
        Say($"D 3-D centre drag: the fence moved {shift:0.00} m ({moved[0] - original[0]}), history {rig.History.Count} step");
        Assert.InRange(shift, 8f, 12f);
        StandsOnTheGround("after the centre drag (3-D)", moved);

        // ── 3-D: then one END point (the last vertex, handle id 3) ──
        rig.Now += 5;
        using (var gizmo = new VertexEditGizmo(s.World, fence, net, () => { }))
        {
            gizmo.SetFocus(true);
            var p2 = moved[2]; var target = p2 + new Vector2(6f, -4f);
            var from = s.Cam.WorldToScreen(new Vector3(p2.X, p2.Y, s.Ground(p2.X, p2.Y))); var to = s.Cam.WorldToScreen(new Vector3(target.X, target.Y, s.Ground(target.X, target.Y)));
            gizmo.OnInteractionStarted(Token(net, 3), s.Cam.ScreenToWorld3D(from));
            rig.Now += 5; gizmo.OnDragUpdate(s.Cam.ScreenToWorld3D(to, forDrag: true));
            gizmo.OnCommit(Vector3.Zero);
        }
        Assert.Equal(2, rig.History.Count);
        var edited = AbsolutePoints(s, fence).Select(p => new Vector2(p.X, p.Y)).ToArray();
        Say($"D 3-D end drag: the last point moved ({edited[2] - moved[2]}), history {rig.History.Count} steps; the first two stayed ({Vector2.Distance(edited[0], moved[0]):0.000}, {Vector2.Distance(edited[1], moved[1]):0.000})");
        Assert.True(Vector2.Distance(edited[0], moved[0]) < 0.01f && Vector2.Distance(edited[1], moved[1]) < 0.01f && Vector2.Distance(edited[2], moved[2]) > 5f);
        StandsOnTheGround("after the end drag (3-D)", edited);
        s.Buffer.Clear(); new TerrainFeatureGizmo().Draw(s.World, fence, s.Buffer); s.Layers.GizmoLayer.Update(0.016f); s.Settle(4);
        s.Frame.Screenshot(Path.Combine(Shots(), "ce4594-d-edit-shape-3d.png"));

        // two undos: the old fence comes back, each a single step
        Assert.True(rig.History.Undo()); Assert.True(rig.History.Undo());
        var back = AbsolutePoints(s, fence).Select(p => new Vector2(p.X, p.Y)).ToArray();
        Assert.True(back.Zip(original).All(z => Vector2.Distance(z.First, z.Second) < 0.01f), "two undos restore the drawn fence");
        Assert.True(rig.History.Redo()); Assert.True(rig.History.Redo());

        // ── 2-D: the same edit through the 2-D camera ──
        s.Layers.ViewSwitch.Set(false, animate: false);
        var centre = edited[1];
        const float zoom = 6f;
        var target2 = centre + new Vector2(-W / 2f / zoom, H / 2f / zoom);
        s.Cam2D.ApplyCameraView(new MapCameraView { Target = target2, Zoom = zoom, SmoothTarget = target2, SmoothZoom = zoom });
        s.Tick(); s.Tick();
        Assert.False(s.Layers.ViewSwitch.Is3D);
        rig.Now += 5;
        using (var gizmo = new VertexEditGizmo(s.World, fence, net, () => { }))
        {
            gizmo.SetFocus(true);
            var c = gizmo.Session.Centre; var px = s.Cam2D.WorldToScreen(c);
            gizmo.OnInteractionStarted(Token(net, ShapeEditSession.CentreHandleId), new Vector3(s.Cam2D.ScreenToWorld(px), 0f));
            rig.Now += 5; gizmo.OnDragUpdate(new Vector3(s.Cam2D.ScreenToWorld(px + new Vector2(30f, 0f)), 0f));   // 30 px = 5 m at this zoom
            gizmo.OnCommit(Vector3.Zero);
        }
        Assert.Equal(3, rig.History.Count);
        var after2D = AbsolutePoints(s, fence).Select(p => new Vector2(p.X, p.Y)).ToArray();
        Say($"D 2-D centre drag: the fence moved {Vector2.Distance(after2D[0], edited[0]):0.00} m, history {rig.History.Count} steps");
        Assert.InRange(Vector2.Distance(after2D[0], edited[0]), 4f, 6f);
        StandsOnTheGround("after the centre drag (2-D)", after2D);
        s.Buffer.Clear(); new TerrainFeatureGizmo().Draw(s.World, fence, s.Buffer);
        using (var gizmo = new VertexEditGizmo(s.World, fence, net, () => { })) { gizmo.SetFocus(true); gizmo.UpdateAndDraw(s.World, 0.016f, s.Buffer); }
        s.Layers.GizmoLayer.Update(0.016f); s.Settle(4);
        s.Frame.Screenshot(Path.Combine(Shots(), "ce4594-d-edit-shape-2d.png"));
    }

    /// <summary>
    /// ⭐ A3 / CE-5016 — A CLICK RIGHT AFTER A BAKE (the user: "drawn fences are almost always shifted"): a live bake replaces the terrain geometry and the picker's grid is rebuilt on a worker (R-1046). 📐 Measured before the fix: the pick MISSED for 1–3 frames
    /// and fell to the look-at plane, 6.8 m (basic-desert) / 12.9 m (city-battle) off in XY. Now the previous grid answers until the new one lands: the pick at once after the bake is ON the relief, within a few cm of the settled one.
    /// 🔴 Red-proof: drop the stale grids (Bind / Current returning null while building) ⇒ the first pick is Ground, metres off.
    /// </summary>
    [SkippableFact]
    public void A3_APickRightAfterABake_IsStillOnTheRelief_ThePreviousGridAnswersMeanwhile()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        foreach (var terrain in new[] { Terrain, Steep })
        {
            using var s = new Stage(terrain);
            FenceOnSlope(s, out _);                                    // the first fence is baked: the geometry is new
            var px = new Vector2(W / 2f - 300f, H / 2f + 150f);   // away from the fence
            s.Cam.InvalidatePicks();
            var first = s.Cam.Pick(px, PickFilter.All);
            var clock = System.Diagnostics.Stopwatch.StartNew(); int frames = 0;
            PickResult now = first;
            while (now.Kind != PickKind.Terrain && clock.Elapsed.TotalSeconds < 60) { s.Tick(); Thread.Sleep(20); frames++; s.Cam.InvalidatePicks(); now = s.Cam.Pick(px, PickFilter.All); }
            float err = Vector2.Distance(new Vector2(first.Point.X, first.Point.Y), new Vector2(now.Point.X, now.Point.Y));
            Say($"A3 {terrain}: a pick at once after the bake answers {first.Kind} at {first.Point}; the settled pick is {now.Kind} at {now.Point} — the early pick is {err:0.00} m off in XY (z {first.Point.Z:0.0} vs {now.Point.Z:0.0}); the picker was blind for {frames} frames / {clock.ElapsedMilliseconds} ms");
            Assert.Equal(PickKind.Terrain, now.Kind);
            Assert.True(MathF.Abs(now.Point.Z - s.Ground(now.Point.X, now.Point.Y)) < 0.05f, "the settled pick is on the relief");
            Assert.Equal(PickKind.Terrain, first.Kind);                                          // ⭐ CE-5016 — never blind after a bake
            Assert.True(err < 0.1f, $"the pick at once after the bake is {err:0.00} m from the settled one");
        }
    }

    /// <summary>
    /// ⭐ E — ITEM 7 FIDELITY: the DRAWING STATE in a real 3-D frame — a fence half drawn on the slope: the thick ghost line along the clicked points, the tool's own handles, and the draw panel (bottom-left, 260 px: <i>Fence — click to add a point</i>, Kind, Height, Snap, Road offset,
    /// the length and <i>Enter ends</i>). Compared by eye with <c>docs/UX/terrain-editor.svg</c> panels B / C; the deviations are listed in the report (FYI, no gate). Shot: <c>ce4594-e-drawing-state.png</c>.
    /// </summary>
    [SkippableFact]
    public void E_TheDrawingState_HasTheGhostAndThePanel()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = new Stage();
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        TerrainLock.SetOpen(s.World, true);
        s.Oblique(); s.WaitForPicks();
        var pick = TerrainPickCatalog.FeaturePicks().Single(p => p.Name == "Fence");
        s.Adapter.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, TerrainBirth.PlacementJson(pick));
        var g = s.ActiveSequence();
        foreach (var px in FencePixels.Take(2)) g.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, s.Cam.ScreenToWorld3D(px));
        g.OnDragUpdate(s.Cam.ScreenToWorld3D(FencePixels[2], forDrag: true));      // the cursor: the next segment follows it
        var panel = new TerrainDrawPanel(() => s.Adapter);
        for (int i = 0; i < 6; i++)
        {
            s.Buffer.Clear();
            g.UpdateAndDraw(s.World, 0.016f, s.Buffer);
            s.Layers.GizmoLayer.Update(0.016f);
            s.Frame.Step(() => { s.Canvas.Update(1f / 30f); s.Canvas.Draw(); panel.Draw(); });
            Thread.Sleep(20);
        }
        s.Frame.Screenshot(Path.Combine(Shots(), "ce4594-e-drawing-state.png"));
        Assert.Equal("Fence — click to add a point", panel.Lines[0]);
        Assert.True(panel.Rects.ContainsKey("snap") && panel.Rects.ContainsKey("offset") && panel.Rects.ContainsKey("height") && panel.Rects.ContainsKey("kind:Fence"), "the panel has its Kind, Height, Snap and Road offset rows");
        Assert.Matches(@"^\d+ m · Enter ends$", panel.Lines[1]);
        var capture = Emit3D(s, s.Buffer);
        Say($"E the drawing state: {capture.Lines} lines, {capture.Triangles} triangles in 3-D, worst {WorstOffGround(s, capture):0.000} m off the ground; panel lines [{string.Join(" | ", panel.Lines)}]");
        foreach (var prim in s.Buffer.GetFrame().ToArray())
        {
            var one = new DebugPrimitiveBuffer(); one.EmitRaw(in prim);
            var c1 = Emit3D(s, one); float w1 = WorstOffGround(s, c1);
            if (w1 > 0.5f) Say($"E off the relief: a {prim.Shape} primitive (layer {prim.DebugLayer}, space {prim.Space}) — {c1.Vertices.Count} vertices, worst {w1:0.0} m, first {c1.Vertices[0]}");
        }
        Assert.True(capture.Lines > 4, "the ghost and the tool's own shape are drawn");
        Assert.True(WorstOffGround(s, capture) <= 0.5f, "…on the relief");
    }
}
