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

    public void Draw(RenderContext ctx)
    {
        StrokesDrawn = 0;
        var terrain = TerrainOf(_world());
        if (!ReferenceEquals(terrain, _built)) Rebuild(terrain);
        if (terrain == null || !_visible()) return;

        if (_plan.FillTriangles > 0) DrawFills();

        var camera = _camera();
        int n = camera != null ? Cull(_plan, ViewOf(camera), camera.Zoom, _inView) : CopyAll(_plan, _inView);
        StrokesDrawn = n;
        if (n == 0) return;
        _renderer.Camera = camera;
        _renderer.Render(new ReadOnlySpan<DebugPrimitive>(_inView, 0, n), ctx);
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
        int tris = _plan.FillTriangles;
        if (tris == 0) return;
        var mesh = new Mesh(tris * 3, tris);
        mesh.AllocVertices();
        mesh.AllocColors();
        var vertices = mesh.VerticesAs<Vector3>();
        var colors = mesh.ColorsAs<Color>();
        for (int i = 0; i < tris * 3; i++)
        {
            var v = _plan.FillVertices[i];
            vertices[i] = new Vector3(v.X, v.Y, FillZ);
            var c = _plan.FillColors[i / 3];
            colors[i] = new Color(c.R, c.G, c.B, c.A);
        }
        Raylib.UploadMesh(ref mesh, false);
        _mesh = mesh;
        _hasMesh = true;
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
        if (_hasMaterial) { Raylib.UnloadMaterial(_material); _hasMaterial = false; }
    }

    private static TerrainWorld? TerrainOf(EntityRepository? world) =>
        world != null && world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;

    /// <summary>The world rectangle the camera shows (min, max), from the screen's corners — north-up flips Y, so both are taken.</summary>
    private static (Vector2 Min, Vector2 Max) ViewOf(MapCamera camera)
    {
        float w = Raylib.IsWindowReady() ? Raylib.GetScreenWidth() : 0f;
        float h = Raylib.IsWindowReady() ? Raylib.GetScreenHeight() : 0f;
        var a = camera.ScreenToWorld(Vector2.Zero);
        var b = camera.ScreenToWorld(new Vector2(w, h));
        return (Vector2.Min(a, b), Vector2.Max(a, b));
    }

    // ---- pure parts (rails run them without a window) -------------------------------------------------------------------------

    /// <summary>The plan of a terrain, split once: fill triangles (a vertex stream and one colour per triangle) and the outlines and
    /// labels kept as primitives for the gizmo renderer, in emission order.</summary>
    public sealed class Plan
    {
        public static readonly Plan Empty = new(Array.Empty<Vector2>(), Array.Empty<Rgba32>(), Array.Empty<DebugPrimitive>());

        public Plan(Vector2[] fillVertices, Rgba32[] fillColors, DebugPrimitive[] strokes)
        {
            FillVertices = fillVertices;
            FillColors = fillColors;
            Strokes = strokes;
        }

        public Vector2[] FillVertices { get; }
        public Rgba32[] FillColors { get; }
        public DebugPrimitive[] Strokes { get; }
        public int FillTriangles => FillColors.Length;
    }

    /// <summary>Emits <paramref name="terrain"/>'s plan (<see cref="TerrainPlan2D.Emit"/>) and splits it.</summary>
    public static Plan PlanOf(TerrainWorld terrain)
    {
        // The plan's size is not known before it is emitted: grow the private buffer until nothing is dropped.
        for (int capacity = 65_536; ; capacity *= 2)
        {
            var buffer = new DebugPrimitiveBuffer(capacity);
            TerrainPlan2D.Emit(terrain, buffer);
            if (buffer.DroppedCount > 0) continue;
            var frame = buffer.GetFrame();
            var vertices = new List<Vector2>();
            var colors = new List<Rgba32>();
            var strokes = new List<DebugPrimitive>();
            foreach (ref readonly var p in frame)
            {
                if (p.Shape == DebugPrimitiveShape.FilledTriangle)
                {
                    vertices.Add(p.TriA); vertices.Add(p.TriB); vertices.Add(p.TriC);
                    colors.Add(p.Color);
                }
                else strokes.Add(p);
            }
            return new Plan(vertices.ToArray(), colors.ToArray(), strokes.ToArray());
        }
    }

    /// <summary>Copies into <paramref name="into"/> the strokes the view shows: inside <paramref name="view"/> (padded by
    /// <see cref="CullMarginPixels"/>), an outline at least <see cref="MinStrokePixels"/> long on screen, labels only from
    /// <see cref="LabelMinZoom"/>. Emission order is kept. Returns how many.</summary>
    public static int Cull(Plan plan, (Vector2 Min, Vector2 Max) view, float zoom, DebugPrimitive[] into)
    {
        if (zoom <= 0f) zoom = 1f;
        var pad = new Vector2(CullMarginPixels / zoom);
        var min = view.Min - pad;
        var max = view.Max + pad;
        float minLength = MinStrokePixels / zoom;
        bool labels = zoom >= LabelMinZoom;
        int n = 0;
        foreach (ref readonly var p in plan.Strokes.AsSpan())
        {
            if (p.Shape == DebugPrimitiveShape.Line)
            {
                var a = new Vector2(p.LineStart.X, p.LineStart.Y);
                var b = new Vector2(p.LineEnd.X, p.LineEnd.Y);
                if (Vector2.DistanceSquared(a, b) < minLength * minLength) continue;
                if (Math.Max(a.X, b.X) < min.X || Math.Min(a.X, b.X) > max.X || Math.Max(a.Y, b.Y) < min.Y || Math.Min(a.Y, b.Y) > max.Y) continue;
            }
            else if (p.Shape == DebugPrimitiveShape.Text)
            {
                if (!labels) continue;
                if (p.TextX < min.X || p.TextX > max.X || p.TextY < min.Y || p.TextY > max.Y) continue;
            }
            into[n++] = p;
        }
        return n;
    }

    private static int CopyAll(Plan plan, DebugPrimitive[] into)
    {
        plan.Strokes.CopyTo(into, 0);
        return plan.Strokes.Length;
    }
}
