using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Look.Terrain;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ CE-5034 GW-3 (📄 docs/DESIGN_Glass_Windows.md §5, §6a; mockup docs/UX/glass-windows.svg) — <b>the glass in the windows</b>, drawn on the 3-D map from the
/// REPLICATED window and building damage, the same on every map node, a late joiner included: a translucent pane in every glazed window still standing, a crack star
/// per kept hit (both faces), a few shards left in a broken frame, and a shard burst when this renderer SEES a window turn broken (<see cref="ShatterWatch"/>: none
/// on the first table, none for a breach). ⭐ The look is <see cref="GlassLook"/>'s (the shared library); this layer only caches it per table instance and draws it.
/// <para>⭐ After the opaque world (the terrain, the bodies): depth TESTED so a wall hides a pane, depth WRITE off so a pane hides nothing behind it. ⛔ Not in the
/// terrain chunks: they draw opaque, and a window change would rebuild a chunk (§6a).</para>
/// </summary>
public sealed class GlassLayer3D : ISceneLayer
{
    /// <summary>The pane: pale blue glass, mostly see-through.</summary>
    public static readonly Color Pane = new(150, 184, 206, 70);

    /// <summary>The pane's sheen: a lighter diagonal streak (the mockup's highlight).</summary>
    public static readonly Color Sheen = new(225, 238, 246, 60);

    /// <summary>A crack's lines and the impact dot: white.</summary>
    public static readonly Color Crack = new(240, 246, 250, 230);

    /// <summary>A shard left in the frame, and a flying one.</summary>
    public static readonly Color Shard = new(190, 214, 230, 170);

    private readonly Func<EntityRepository?> _world;
    private EntityRepository? _lastWorld;
    private TerrainWorld? _lastTerrain;
    private WindowTable? _lastWindows;
    private BuildingDamageTable? _lastDamage;
    private bool _built;

    private readonly List<PaneQuad> _panes = new();
    private readonly List<GlassLine> _cracks = new();
    private readonly List<Vector3> _dots = new();
    private readonly List<GlassShard> _shards = new();
    private readonly ShatterWatch _watch = new();
    private readonly List<(int Panel, int Opening)> _broke = new();
    private readonly List<(Vector3 Origin, Vector3 Outward, uint Seed, double At)> _bursts = new();

    public GlassLayer3D(Func<EntityRepository?> world) => _world = world;

    public string Name => "Glass";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>Panes / crack segments / frame shards / flying shards drawn in the last 3-D frame — the rails' counters.</summary>
    public int PanesDrawn { get; private set; }
    /// <inheritdoc cref="PanesDrawn"/>
    public int CrackLines => _cracks.Count;
    /// <inheritdoc cref="PanesDrawn"/>
    public int FrameShards => _shards.Count;
    /// <inheritdoc cref="PanesDrawn"/>
    public int FlyingShards { get; private set; }
    /// <summary>Shard bursts started since this layer was made — a rail's counter.</summary>
    public int Bursts { get; private set; }

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <summary>
    /// Bring the cached look up to the world's current tables: rebuilt only when a table INSTANCE changes (the tables stay the same object while nothing changed,
    /// GW-2), so a steady frame does no work and allocates nothing. Returns false when there is nothing to draw. Railable without a GL context.
    /// </summary>
    public bool Refresh()
    {
        var world = _world();
        var terrain = world != null && world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
        if (world == null || terrain == null || terrain.PaneLeaves.Count == 0)
        {
            _panes.Clear(); _cracks.Clear(); _dots.Clear(); _shards.Clear(); _bursts.Clear();
            _lastTerrain = null; _built = false;
            return false;
        }
        if (!ReferenceEquals(world, _lastWorld) || !ReferenceEquals(terrain, _lastTerrain))
        {
            _watch.Reset(); _bursts.Clear();   // a new world: the next table is a first sight
            _lastWorld = world; _lastTerrain = terrain; _built = false;
        }
        var doors = DoorStates.Of(world, terrain);
        var windows = doors.Windows;
        var damage = doors.Damage;
        if (_built && ReferenceEquals(windows, _lastWindows) && ReferenceEquals(damage, _lastDamage)) return true;

        _lastWindows = windows; _lastDamage = damage; _built = true;
        _panes.Clear(); _cracks.Clear(); _dots.Clear(); _shards.Clear();
        GlassLook.Panes(terrain, doors, _panes);
        if (windows != null)
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if ((uint)w.Panel >= (uint)terrain.Panels.Count) continue;
                int building = terrain.Panels[w.Panel].Building;
                if (damage != null && building >= 0 && damage.Collapsed(building)) continue;
                if (w.Broken) { GlassLook.Shards(terrain, w.Panel, w.Opening, _shards); continue; }
                for (int hit = 0; hit < 2; hit++)
                    if (GlassLook.CrackStar(terrain, w, hit, _cracks) > 0 && GlassLook.ImpactOf(terrain, w, hit) is { } dot) _dots.Add(dot);
            }
        _broke.Clear();
        if (_watch.Seen(windows, terrain, damage, _broke) > 0)
        {
            double now = Now(world);
            foreach (var (panel, opening) in _broke)
            {
                var q = GlassLook.Quad(terrain, panel, opening);
                var centre = (q.A + q.C) * 0.5f;
                // ⭐ the shards fly to the side the round LEFT by: unknown here, so OUT of the building (the street side reads best from outside)
                _bursts.Add((centre, GlassLook.OutwardOf(terrain, panel), GlassLook.Seed(panel, opening, 3), now));
                Bursts++;
            }
        }
        return true;
    }

    public void Draw3D(RenderContext ctx)
    {
        PanesDrawn = 0; FlyingShards = 0;
        if (!Refresh()) return;
        var world = _lastWorld!;
        double now = Now(world);

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        foreach (var q in _panes)
        {
            Quad(q.A, q.B, q.C, q.D, Pane);
            // the sheen: a thin diagonal band across the pane
            var s0 = Lerp(q.A, q.D, 0.55f); var s1 = Lerp(q.A, q.D, 0.75f);
            var s2 = Lerp(q.B, q.C, 0.35f); var s3 = Lerp(q.B, q.C, 0.15f);
            Quad(s3, s2, s1, s0, Sheen);
            PanesDrawn++;
        }
        foreach (var s in _shards)
            Raylib.DrawTriangle3D(HrotToRaylib.Position(s.A), HrotToRaylib.Position(s.B), HrotToRaylib.Position(s.C), Shard);
        foreach (var l in _cracks)
            Raylib.DrawLine3D(HrotToRaylib.Position(l.From), HrotToRaylib.Position(l.To), Crack);
        foreach (var d in _dots)
            Raylib.DrawSphereEx(HrotToRaylib.Position(d), 0.012f, 4, 6, Crack);
        for (int b = _bursts.Count - 1; b >= 0; b--)
        {
            var (origin, outward, seed, at) = _bursts[b];
            float age = (float)(now - at);
            if (age > GlassLook.BurstSeconds || age < -1f) { _bursts.RemoveAt(b); continue; }   // over, or the clock went back (a replay jump)
            for (int i = 0; i < GlassLook.BurstShards; i++)
            {
                if (GlassLook.BurstShard(origin, outward, seed, i, age) is not { } p) continue;
                var c = HrotToRaylib.Position(p);
                float r = 0.025f + 0.01f * (i % 3);
                Raylib.DrawTriangle3D(c + new Vector3(-r, 0, 0), c + new Vector3(r, r * 0.6f, 0), c + new Vector3(0, -r, r * 0.5f), Shard);
                FlyingShards++;
            }
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
    {
        var ra = HrotToRaylib.Position(a); var rb = HrotToRaylib.Position(b);
        var rc = HrotToRaylib.Position(c); var rd = HrotToRaylib.Position(d);
        Raylib.DrawTriangle3D(ra, rb, rc, colour);
        Raylib.DrawTriangle3D(ra, rc, rd, colour);
    }

    // the sim clock read directly: ⛔ SimClock.Of boxes its struct (an allocation every frame)
    private static double Now(EntityRepository world)
        => world.HasSingletonUnmanaged<GlobalTime>() ? world.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0;

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
}
