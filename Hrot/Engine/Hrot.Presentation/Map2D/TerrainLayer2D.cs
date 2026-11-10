using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;

namespace Hrot.UI.Common.Map2D;

/// <summary>
/// ⭐⭐ <c>CE-1227</c> (docs/DESIGN_Terrain_World.md §10, R-1013) — <b>the 2-D map's terrain as a RETAINED layer</b>, the 2-D sibling of
/// <see cref="Fdp.Toolkit.Vis3D.TerrainLayer3D"/>. The plan (<see cref="TerrainPlan2D"/>, the look unchanged) is emitted ONCE per loaded
/// <see cref="TerrainWorld"/> into a private buffer; its fills become one mesh, drawn in one call; its outlines and labels are drawn each
/// frame through the gizmo renderer, only those in view.
///
/// <para>🔒 User, 2026-10-20: <i>"When i load scenario uc convoy hit, on that terrain in 2d the entity symbols are not shown and neither the
/// selecgion maraquee … What limits number of gizmos? The terrain has msny houses."</i> 📐 The terrain gizmo emitted 73 188 primitives a
/// frame on demo-city into the map's 16 384 buffer; everything after it was dropped. ⭐ Now the terrain takes no slot in that buffer.</para>
/// <para>⭐ 2-D only (<see cref="Has3D"/> false): the 3-D map draws its own terrain, and the old triangles were draped under it.</para>
/// </summary>
public sealed class TerrainLayer2D : ISceneLayer, IDisposable   // ⭐ CE-1380 — the 2-D ground plan: filmed (its row still gates it)
{
    /// <summary>Labels are drawn from this zoom (screen pixels per metre) up; below it a 20 m building is under 30 px and the labels
    /// only smear the map.</summary>
    public const float LabelMinZoom = 1.5f;

    /// <summary>An outline shorter than this on screen (pixels) is not drawn — a wall piece's end seen from far away.</summary>
    public const float MinStrokePixels = 0.75f;

    /// <summary>Screen pixels added around the view when culling, so a label anchored just off screen still shows its text.</summary>
    public const float CullMarginPixels = 150f;

    /// <summary>The fill mesh's depth: inside the 2-D projection's clip range (raylib's ortho keeps view-space z in [-1, 0]).</summary>
    private const float FillZ = -0.5f;

    private readonly Func<EntityRepository?> _world;
    private readonly Func<bool> _visible;
    private readonly Func<MapCamera?> _camera;
    private readonly Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D _renderer = new();

    private TerrainWorld? _built;
    private Plan _plan = Plan.Empty;
    private DebugPrimitive[] _inView = Array.Empty<DebugPrimitive>();
    private bool _meshDirty;
    private Mesh _mesh;
    private bool _hasMesh;
    private Material _material;
    private bool _hasMaterial;

    // ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-5) — the damage drawn: the damaged buildings hidden in the plan, drawn again (damaged) in a small overlay plan
    private object? _drawnDamage;
    private bool[] _hidden = Array.Empty<bool>();
    private bool _anyHidden;
    private Plan _overlay = Plan.Empty;
    private DebugPrimitive[] _overlayStrokes = Array.Empty<DebugPrimitive>();
    private bool _overlayDirty;
    private Mesh _overlayMesh;
    private bool _hasOverlayMesh;

    /// <summary>How many times the damage overlay was rebuilt (a rail: an unchanged damage table rebuilds nothing).</summary>
    public int DamageBuilds { get; private set; }

    /// <summary>The buildings the plan currently draws from the overlay (damaged).</summary>
    public int DamagedBuildingsDrawn { get; private set; }

    /// <param name="world">The world whose <see cref="TerrainWorld"/> singleton is drawn.</param>
    /// <param name="visible">The "Terrain (2-D)" checkbox — the map buffer's <see cref="DebugTraceLayers.Terrain2D"/> bit.</param>
    /// <param name="camera">The map camera, for culling to the view; <c>null</c> ⇒ nothing is culled.</param>
    public TerrainLayer2D(Func<EntityRepository?> world, Func<bool> visible, Func<MapCamera?>? camera = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _visible = visible ?? throw new ArgumentNullException(nameof(visible));
        _camera = camera ?? (() => null);
    }

    public string Name => "Terrain (2-D)";

    /// <summary>Always drawn by the canvas; the layer gates itself by the gizmo layer mask (<see cref="DebugTraceLayers.Terrain2D"/>).</summary>
    public int LayerBitIndex => -1;

    /// <summary>Triangles in the fill mesh of the terrain last built.</summary>
    public int FillTriangles => _plan.FillTriangles;

    /// <summary>Outlines and labels the plan holds (drawn when in view).</summary>
    public int Strokes => _plan.Strokes.Length;

    /// <summary>Outlines and labels handed to the renderer in the last frame.</summary>
    public int StrokesDrawn { get; private set; }

    /// <summary>How many times the plan was built — once per loaded terrain.</summary>
    public int Builds { get; private set; }

    public void Update(float dt) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <summary>⭐ CE-4389 — the strokes the last 2-D plan pass drew (the frame-stats log).</summary>
    public static int LastStrokesDrawn { get; private set; }


    public void Draw(RenderContext ctx)
    {
        StrokesDrawn = 0;
        var repo = _world();
        var terrain = TerrainOf(repo);
        if (!ReferenceEquals(terrain, _built)) Rebuild(terrain);
        if (terrain == null || !_visible()) return;
        // ⭐ CE-4392 — the replicated damage of this view (R-4196: drawn, the terrain never changed); the same instance while nothing changed ⇒ nothing done
        var damage = terrain.Buildings.Count > 0 ? BuildingDamageTable.Of(repo!, terrain) : null;
        if (!ReferenceEquals(damage, _drawnDamage)) ApplyDamage(terrain, damage);

        long tf = Fdp.Presentation.WindowManager.WindowTimings.Start();   // ⭐ CE-4389 — the 2-D plan by stage (HROT_FRAME_STATS; no-op when off)
        if (_plan.FillTriangles > 0) DrawFills();
        Fdp.Presentation.WindowManager.WindowTimings.Record("plan2D fills", tf);
        if (_overlay.FillTriangles > 0) DrawOverlayFills();

        var camera = _camera();
        long tc = Fdp.Presentation.WindowManager.WindowTimings.Start();
        int n = camera != null ? Cull(_plan, ViewOf(camera, ctx.TargetSize), camera.Zoom, _inView, _anyHidden ? _hidden : null) : CopyAll(_plan, _inView, _anyHidden ? _hidden : null);
        Fdp.Presentation.WindowManager.WindowTimings.Record("plan2D cull", tc);
        StrokesDrawn = n;
        LastStrokesDrawn = n;
        _renderer.Camera = camera;
        long tr = Fdp.Presentation.WindowManager.WindowTimings.Start();
        if (n > 0) _renderer.Render(new ReadOnlySpan<DebugPrimitive>(_inView, 0, n), ctx);
        Fdp.Presentation.WindowManager.WindowTimings.Record("plan2D strokes", tr);
        if (_overlayStrokes.Length > 0) { _renderer.Render(_overlayStrokes, ctx); StrokesDrawn += _overlayStrokes.Length; }
    }

    /// <summary>
    /// ⭐ CE-4392 X-5 — on a change of the damage: the damaged buildings' fills and strokes hidden in the plan (a flag per building; the fill colours re-uploaded once), and the
    /// buildings emitted again as <paramref name="damage"/> leaves them into the overlay plan — never the whole plan rebuilt.
    /// </summary>
    private void ApplyDamage(TerrainWorld terrain, BuildingDamageTable? damage)
    {
        _drawnDamage = damage;
        DamageBuilds++;
        if (_hidden.Length != terrain.Buildings.Count) _hidden = new bool[terrain.Buildings.Count];
        Array.Clear(_hidden);
        var set = new HashSet<int>();
        if (damage != null) DamagedBuildings.Touched(terrain, null, damage, set);
        foreach (int b in set) if ((uint)b < (uint)_hidden.Length) _hidden[b] = true;
        _anyHidden = set.Count > 0;
        DamagedBuildingsDrawn = set.Count;
        _overlay = set.Count > 0 ? PlanOf(terrain, damage, b => (uint)b < (uint)_hidden.Length && _hidden[b]) : Plan.Empty;
        _overlayStrokes = _overlay.Strokes;
        _overlayDirty = true;
        _meshDirty = true;   // the plan's fills: the hidden buildings' alpha 0
    }

    private void DrawOverlayFills()
    {
        if (_overlayDirty)
        {
            _overlayDirty = false;
            if (_hasOverlayMesh) { Raylib.UnloadMesh(_overlayMesh); _hasOverlayMesh = false; }
            if (_overlay.FillTriangles > 0) { _overlayMesh = MeshOf(_overlay, null); _hasOverlayMesh = true; }
        }
        if (!_hasOverlayMesh) return;
        if (!_hasMaterial) { _material = Raylib.LoadMaterialDefault(); _hasMaterial = true; }
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Raylib.DrawMesh(_overlayMesh, _material, Matrix4x4.Identity);
    }

    /// <summary>Builds the plan of <paramref name="terrain"/> (none ⇒ empty); the mesh is uploaded on the next draw.</summary>
    private void Rebuild(TerrainWorld? terrain)
    {
        _built = terrain;
        _plan = terrain != null ? PlanOf(terrain) : Plan.Empty;
        _inView = new DebugPrimitive[_plan.Strokes.Length];
        _meshDirty = true;
        Builds++;
    }

    private void DrawFills()
    {
        if (_meshDirty) UploadMesh();
        if (!_hasMesh) return;
        if (!_hasMaterial) { _material = Raylib.LoadMaterialDefault(); _hasMaterial = true; }
        // ⭐ The batch holds what the layers below drew (the grid); flush it so the mesh lands above it, as the painter's order says.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();   // the north-up mirror reverses every triangle's winding
        Raylib.DrawMesh(_mesh, _material, Matrix4x4.Identity);
    }

    private void UploadMesh()
    {
        _meshDirty = false;
        ReleaseMesh();
        if (_plan.FillTriangles == 0) return;
        _mesh = MeshOf(_plan, _anyHidden ? _hidden : null);
        _hasMesh = true;
    }

    /// <summary>A plan's fills as one mesh; a fill of a <paramref name="hidden"/> building drawn transparent (CE-4392).</summary>
    private static Mesh MeshOf(Plan plan, bool[]? hidden)
    {
        int tris = plan.FillTriangles;
        var mesh = new Mesh(tris * 3, tris);
        mesh.AllocVertices();
        mesh.AllocColors();
        var vertices = mesh.VerticesAs<Vector3>();
        var colors = mesh.ColorsAs<Color>();
        for (int i = 0; i < tris * 3; i++)
        {
            var v = plan.FillVertices[i];
            vertices[i] = new Vector3(v.X, v.Y, FillZ);
            var c = plan.FillColors[i / 3];
            int b = plan.FillBuilding[i / 3];
            byte a = hidden != null && (uint)b < (uint)hidden.Length && hidden[b] ? (byte)0 : c.A;
            colors[i] = new Color(c.R, c.G, c.B, a);
        }
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    private void ReleaseMesh()
    {
        if (!_hasMesh) return;
        Raylib.UnloadMesh(_mesh);
        _hasMesh = false;
    }

    public void Dispose()
    {
        ReleaseMesh();
        if (_hasOverlayMesh) { Raylib.UnloadMesh(_overlayMesh); _hasOverlayMesh = false; }
        if (_hasMaterial) { Raylib.UnloadMaterial(_material); _hasMaterial = false; }
    }

    private static TerrainWorld? TerrainOf(EntityRepository? world) =>
        world != null && world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;

    /// <summary>The world rectangle the camera shows (min, max), from the screen's corners — north-up flips Y, so both are taken.</summary>
    private static (Vector2 Min, Vector2 Max) ViewOf(MapCamera camera, Vector2 size)
    {
        // ⭐ CE-1383 — the TARGET being drawn (RenderContext.TargetSize: the window's, or a film's render texture), never the window by default
        var a = camera.ScreenToWorld(Vector2.Zero);
        var b = camera.ScreenToWorld(size);
        return (Vector2.Min(a, b), Vector2.Max(a, b));
    }

    // ---- pure parts (rails run them without a window) -------------------------------------------------------------------------

    /// <summary>The plan of a terrain, split once: fill triangles (a vertex stream and one colour per triangle) and the outlines and
    /// labels kept as primitives for the gizmo renderer, in emission order.</summary>
    public sealed class Plan
    {
        public static readonly Plan Empty = new(Array.Empty<Vector2>(), Array.Empty<Rgba32>(), Array.Empty<DebugPrimitive>());

        public Plan(Vector2[] fillVertices, Rgba32[] fillColors, DebugPrimitive[] strokes) : this(fillVertices, fillColors, strokes, null, null) { }

        /// <param name="fillBuilding">⭐ CE-4392 — the building of each fill triangle (−1 none); null = none</param>
        /// <param name="strokeBuilding">⭐ CE-4392 — the building of each stroke (−1 none); null = none</param>
        public Plan(Vector2[] fillVertices, Rgba32[] fillColors, DebugPrimitive[] strokes, int[]? fillBuilding, int[]? strokeBuilding)
        {
            FillVertices = fillVertices;
            FillColors = fillColors;
            Strokes = strokes;
            FillBuilding = fillBuilding ?? Filled(fillColors.Length);
            StrokeBuilding = strokeBuilding ?? Filled(strokes.Length);
            Index = new StrokeIndex(strokes);
        }

        private static int[] Filled(int n) { var a = new int[n]; Array.Fill(a, -1); return a; }

        /// <summary>⭐ CE-4392 — which building each fill triangle belongs to (−1 = none).</summary>
        public int[] FillBuilding { get; }

        /// <summary>⭐ CE-4392 — which building each stroke belongs to (−1 = none).</summary>
        public int[] StrokeBuilding { get; }

        /// <summary>⭐ CE-4376 (D-2) — the strokes binned by <see cref="StrokeIndex.Bin"/> m cells, so a frame visits only the cells in view and stops at the length cut.</summary>
        public StrokeIndex Index { get; }

        public Vector2[] FillVertices { get; }
        public Rgba32[] FillColors { get; }
        public DebugPrimitive[] Strokes { get; }
        public int FillTriangles => FillColors.Length;
    }

    /// <summary>Emits <paramref name="terrain"/>'s plan (<see cref="TerrainPlan2D.Emit"/>) and splits it.</summary>
    public static Plan PlanOf(TerrainWorld terrain) => PlanOf(terrain, null, null);

    /// <summary>⭐ CE-4392 — the plan under <paramref name="damage"/>, of the buildings <paramref name="only"/> accepts (null = all, the whole plan), with the building of every fill and stroke.</summary>
    public static Plan PlanOf(TerrainWorld terrain, BuildingDamageTable? damage, Func<int, bool>? only)
    {
        // The plan's size is not known before it is emitted: grow the private buffer until nothing is dropped.
        for (int capacity = only != null ? 4_096 : 65_536; ; capacity *= 2)
        {
            var buffer = new DebugPrimitiveBuffer(capacity);
            var marks = new List<(int Start, int Building)>();
            TerrainPlan2D.Emit(terrain, buffer, damage, only, b => marks.Add((buffer.Count, b)));
            if (buffer.DroppedCount > 0) continue;
            var frame = buffer.GetFrame();
            var vertices = new List<Vector2>();
            var colors = new List<Rgba32>();
            var strokes = new List<DebugPrimitive>();
            var fillB = new List<int>(); var strokeB = new List<int>();
            int m = 0, current = -1, i = 0;
            foreach (ref readonly var p in frame)
            {
                while (m < marks.Count && marks[m].Start <= i) current = marks[m++].Building;
                i++;
                if (p.Shape == DebugPrimitiveShape.FilledTriangle)
                {
                    vertices.Add(p.TriA); vertices.Add(p.TriB); vertices.Add(p.TriC);
                    colors.Add(p.Color); fillB.Add(current);
                }
                else { strokes.Add(p); strokeB.Add(current); }
            }
            return new Plan(vertices.ToArray(), colors.ToArray(), strokes.ToArray(), fillB.ToArray(), strokeB.ToArray());
        }
    }

    /// <summary>
    /// ⭐ CE-4376 (D-2, docs/DESIGN_Terrain_World.md §10) — the plan's strokes binned by their midpoint into <see cref="Bin"/> m cells: in each cell the lines LONGEST FIRST
    /// (a cull stops at the first line under the zoom's length cut) and the labels; lines longer than a cell, and every other shape, in lists scanned whole. 📐 scale-1:
    /// 482 k strokes — the linear cull scanned all of them every frame (14.5 ms at the whole-terrain zoom, 4.2 ms close in).
    /// </summary>
    public sealed class StrokeIndex
    {
        public const float Bin = 256f;
        public readonly Vector2 Origin;
        public readonly int Nx, Ny;
        /// <summary>Per cell, [start, end) into <see cref="Lines"/> / <see cref="Labels"/>.</summary>
        public readonly int[] LineStart, LabelStart;
        public readonly int[] Lines, Labels, Long, Other;
        private readonly float[] _length2;   // per stroke

        public StrokeIndex(DebugPrimitive[] strokes)
        {
            _length2 = new float[strokes.Length];
            var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
            for (int i = 0; i < strokes.Length; i++)
            {
                var c = Centre(strokes[i]);
                if (c is { } p) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            }
            if (lo.X > hi.X) { lo = hi = Vector2.Zero; }
            Origin = lo;
            Nx = Math.Max(1, (int)MathF.Floor((hi.X - lo.X) / Bin) + 1);
            Ny = Math.Max(1, (int)MathF.Floor((hi.Y - lo.Y) / Bin) + 1);
            var lines = new List<int>[Nx * Ny]; var labels = new List<int>[Nx * Ny];
            var longOnes = new List<int>(); var other = new List<int>();
            for (int i = 0; i < strokes.Length; i++)
            {
                ref readonly var p = ref strokes[i];
                if (p.Shape == DebugPrimitiveShape.Line)
                {
                    var a = new Vector2(p.LineStart.X, p.LineStart.Y); var b = new Vector2(p.LineEnd.X, p.LineEnd.Y);
                    _length2[i] = Vector2.DistanceSquared(a, b);
                    if (_length2[i] > Bin * Bin) { longOnes.Add(i); continue; }
                    (lines[Cell((a + b) * 0.5f)] ??= new List<int>()).Add(i);
                }
                else if (p.Shape == DebugPrimitiveShape.Text) (labels[Cell(new Vector2(p.TextX, p.TextY))] ??= new List<int>()).Add(i);
                else other.Add(i);
            }
            LineStart = new int[(Nx * Ny) + 1]; LabelStart = new int[(Nx * Ny) + 1];
            var ls = new List<int>(strokes.Length); var lb = new List<int>();
            for (int k = 0; k < Nx * Ny; k++)
            {
                LineStart[k] = ls.Count; LabelStart[k] = lb.Count;
                if (lines[k] is { } l) { l.Sort((x, y) => _length2[y].CompareTo(_length2[x])); ls.AddRange(l); }
                if (labels[k] is { } t) lb.AddRange(t);
            }
            LineStart[Nx * Ny] = ls.Count; LabelStart[Nx * Ny] = lb.Count;
            Lines = ls.ToArray(); Labels = lb.ToArray(); Long = longOnes.ToArray(); Other = other.ToArray();
        }

        public float Length2(int stroke) => _length2[stroke];

        public int Cell(Vector2 p)
            => (Math.Clamp((int)MathF.Floor((p.Y - Origin.Y) / Bin), 0, Ny - 1) * Nx) + Math.Clamp((int)MathF.Floor((p.X - Origin.X) / Bin), 0, Nx - 1);

        private static Vector2? Centre(in DebugPrimitive p) => p.Shape switch
        {
            DebugPrimitiveShape.Line => new Vector2((p.LineStart.X + p.LineEnd.X) * 0.5f, (p.LineStart.Y + p.LineEnd.Y) * 0.5f),
            DebugPrimitiveShape.Text => new Vector2(p.TextX, p.TextY),
            _ => null,
        };
    }

    /// <summary>Copies into <paramref name="into"/> the strokes the view shows: inside <paramref name="view"/> (padded by
    /// <see cref="CullMarginPixels"/>), an outline at least <see cref="MinStrokePixels"/> long on screen, labels only from
    /// <see cref="LabelMinZoom"/>. Emission order is kept. Returns how many.</summary>
    public static int Cull(Plan plan, (Vector2 Min, Vector2 Max) view, float zoom, DebugPrimitive[] into) => Cull(plan, view, zoom, into, null);

    /// <summary>
    /// ⭐ CE-4389 (render under load R-3) — coarser than this map scale (m per pixel) a BUILDING is drawn by its fill alone: its wall pieces' and floor slabs'
    /// outlines are skipped. 📐 Measured on Windows (scale-1, the whole terrain at ≈ 2.5 m/px): 61 492 strokes a frame, every one a building's, 14.7 ms to draw —
    /// where a house is 4–6 px wide. 🔒 User, 2026-11-10: <i>"Whatever is better for the performance"</i> (fills only, over one outline per building). Closer in,
    /// every stroke as before; strokes of no building (a garden wall, a fence) keep their own rules.
    /// </summary>
    public const float BuildingStrokesMaxMetresPerPixel = 1f;

    /// <summary>⭐ CE-4392 — <see cref="Cull(Plan, ValueTuple{Vector2, Vector2}, float, DebugPrimitive[])"/>, skipping the strokes of the <paramref name="hidden"/> buildings (drawn by the damage overlay).</summary>
    public static int Cull(Plan plan, (Vector2 Min, Vector2 Max) view, float zoom, DebugPrimitive[] into, bool[]? hidden)
    {
        if (zoom <= 0f) zoom = 1f;
        var pad = new Vector2(CullMarginPixels / zoom);
        var min = view.Min - pad;
        var max = view.Max + pad;
        float minLength = MinStrokePixels / zoom, min2 = minLength * minLength;
        bool labels = zoom >= LabelMinZoom;
        bool buildings = 1f / zoom <= BuildingStrokesMaxMetresPerPixel;   // ⭐ CE-4389 — coarser: a building is its fill only
        var strokes = plan.Strokes;
        var ix = plan.Index;
        int n = 0;
        // ⭐ CE-4376 — only the cells in view (a binned line's midpoint lies within half a cell of its ends: one cell of slack)
        int c0 = Math.Max(0, (int)MathF.Floor((min.X - ix.Origin.X) / StrokeIndex.Bin) - 1), c1 = Math.Min(ix.Nx - 1, (int)MathF.Floor((max.X - ix.Origin.X) / StrokeIndex.Bin) + 1);
        int r0 = Math.Max(0, (int)MathF.Floor((min.Y - ix.Origin.Y) / StrokeIndex.Bin) - 1), r1 = Math.Min(ix.Ny - 1, (int)MathF.Floor((max.Y - ix.Origin.Y) / StrokeIndex.Bin) + 1);
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                int k = (r * ix.Nx) + c;
                for (int q = ix.LineStart[k]; q < ix.LineStart[k + 1]; q++)
                {
                    int s = ix.Lines[q];
                    if (ix.Length2(s) < min2) break;   // longest first: the rest are shorter still
                    if (!buildings && plan.StrokeBuilding[s] >= 0) continue;
                    if (hidden != null && Hidden(plan, s, hidden)) continue;
                    if (LineInView(strokes[s], min, max)) into[n++] = strokes[s];
                }
                if (!labels) continue;
                for (int q = ix.LabelStart[k]; q < ix.LabelStart[k + 1]; q++)
                {
                    if (hidden != null && Hidden(plan, ix.Labels[q], hidden)) continue;
                    ref readonly var p = ref strokes[ix.Labels[q]];
                    if (p.TextX < min.X || p.TextX > max.X || p.TextY < min.Y || p.TextY > max.Y) continue;
                    into[n++] = p;
                }
            }
        for (int q = 0; q < ix.Long.Length; q++)
        {
            int s = ix.Long[q];
            if (ix.Length2(s) >= min2 && (buildings || plan.StrokeBuilding[s] < 0) && LineInView(strokes[s], min, max) && !(hidden != null && Hidden(plan, s, hidden))) into[n++] = strokes[s];
        }
        for (int q = 0; q < ix.Other.Length; q++) if (!(hidden != null && Hidden(plan, ix.Other[q], hidden))) into[n++] = strokes[ix.Other[q]];
        return n;
    }

    private static bool Hidden(Plan plan, int stroke, bool[] hidden) { int b = plan.StrokeBuilding[stroke]; return (uint)b < (uint)hidden.Length && hidden[b]; }

    private static bool LineInView(in DebugPrimitive p, Vector2 min, Vector2 max)
    {
        float ax = p.LineStart.X, ay = p.LineStart.Y, bx = p.LineEnd.X, by = p.LineEnd.Y;
        return !(Math.Max(ax, bx) < min.X || Math.Min(ax, bx) > max.X || Math.Max(ay, by) < min.Y || Math.Min(ay, by) > max.Y);
    }

    private static int CopyAll(Plan plan, DebugPrimitive[] into, bool[]? hidden)
    {
        if (hidden == null) { plan.Strokes.CopyTo(into, 0); return plan.Strokes.Length; }
        int n = 0;
        for (int i = 0; i < plan.Strokes.Length; i++) if (!Hidden(plan, i, hidden)) into[n++] = plan.Strokes[i];
        return n;
    }
}
