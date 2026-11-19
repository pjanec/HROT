using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-5034</c> GW-3 (📄 docs/DESIGN_Glass_Windows.md §5, mockup <c>docs/UX/glass-windows.svg</c>) — the GLASS in the windows on the 3-D map, drawn from what
/// this host's view holds and nothing else: every glazed window's pane (<see cref="TerrainWorld.PaneLeaves"/>) near the eye as a pale-blue TRANSLUCENT sheet
/// with a sheen (both faces), a crack star at each kept hit (<see cref="WindowTable"/>, the replicated <c>WindowDamage</c> — so a late joiner and a replay see
/// every crack), and — once broken — the open hole with a few shards left in the frame; a breached wall's or a collapsed building's pane is gone with it (D-10).
/// At the moment this host SEES a pane turn broken it plays a short shard burst (D-8: local, a state change — a pane already broken when the layer first looks
/// bursts nothing, so a late joiner plays none).
/// <para>⭐ Its own pass after the opaque terrain (the cloud deck's shape): depth TEST on (a wall in front hides the glass), depth WRITE off (the room behind
/// shows through). ⛔ Not in the chunk mesh: the terrain draws opaque, its vertex alpha carries the shader's pattern code.</para>
/// <para>⚠ Hot path: no allocation in the steady state — the pane grid is built once per world, the burst list is a reused buffer.</para>
/// </summary>
public sealed class GlassLayer3D : ISceneLayer
{
    /// <summary>The pane's tint (the mockup's #9FD7FF at 0.45).</summary>
    public static readonly Color PaneColour = new(159, 215, 255, 115);
    /// <summary>The sheen strip across a pane.</summary>
    public static readonly Color SheenColour = new(255, 255, 255, 46);
    /// <summary>A crack's lines.</summary>
    public static readonly Color CrackColour = new(255, 255, 255, 235);
    /// <summary>A shard (left in the frame, or flying).</summary>
    public static readonly Color ShardColour = new(159, 215, 255, 150);

    /// <summary>Panes farther than this from the eye (m) are not drawn.</summary>
    public const float DrawRadiusM = 220f;
    /// <summary>A crack star's radius as a share of the pane's smaller side (the mockup's 0.32).</summary>
    public const float CrackShare = 0.32f;
    /// <summary>How long a shard burst lasts (s).</summary>
    public const float BurstSeconds = 1.4f;
    /// <summary>How many shards one shattered pane throws.</summary>
    public const int ShardsPerBurst = 26;

    private readonly Func<EntityRepository?> _world;
    private readonly Func<Vector3> _eye;

    public GlassLayer3D(Func<EntityRepository?> world, Func<Vector3> eye)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _eye = eye ?? throw new ArgumentNullException(nameof(eye));
    }

    public string Name => "Window glass (3-D)";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>Panes drawn last frame / cracks drawn / panes drawn as broken / shards in flight (rails).</summary>
    public int PanesDrawn { get; private set; }
    public int CracksDrawn { get; private set; }
    public int BrokenDrawn { get; private set; }
    public int ShardsFlying => _shardCount;
    /// <summary>Bursts started since the layer was made (rails).</summary>
    public int Bursts { get; private set; }

    public void Update(float dt) => _dt = dt;
    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    private float _dt;
    private TerrainWorld? _seenWorld;
    private readonly HashSet<long> _broken = new();     // the panes this host has seen broken (a turn to broken bursts once)
    private bool _primed;

    public void Draw3D(RenderContext ctx)
    {
        PanesDrawn = CracksDrawn = BrokenDrawn = 0;
        if (_world() is not { } world || !world.HasSingletonManaged<TerrainWorld>() || world.GetSingletonManaged<TerrainWorld>() is not { PaneLeaves.Count: > 0 } terrain)
        {
            StepShards(_dt); DrawShards(); return;
        }
        if (!ReferenceEquals(terrain, _seenWorld)) { _seenWorld = terrain; _broken.Clear(); _primed = false; }
        var view = DoorStates.Of(world, terrain);
        var windows = view.Windows;
        Notice(terrain, windows);

        var eye = _eye();
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        var grid = PaneGrid.Of(terrain);
        int c0x = (int)MathF.Floor((eye.X - DrawRadiusM) / PaneGrid.Cell), c1x = (int)MathF.Floor((eye.X + DrawRadiusM) / PaneGrid.Cell);
        int c0y = (int)MathF.Floor((eye.Y - DrawRadiusM) / PaneGrid.Cell), c1y = (int)MathF.Floor((eye.Y + DrawRadiusM) / PaneGrid.Cell);
        for (int cy = c0y; cy <= c1y; cy++)
            for (int cx = c0x; cx <= c1x; cx++)
            {
                if (!grid.Cells.TryGetValue(PaneGrid.Key(cx, cy), out var list)) continue;
                for (int k = 0; k < list.Length; k++) DrawPane(terrain, view, windows, list[k], eye);
            }
        Rlgl.DrawRenderBatchActive();
        StepShards(_dt);
        DrawShards();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    // ── a pane ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A pane's four corners on its wall's centre plane (Hrot metres): the start's sill and head, the end's head and sill — and its unit normal.</summary>
    public static (Vector3 A0, Vector3 A1, Vector3 B1, Vector3 B0, Vector3 Normal) Corners(TerrainPrism pane)
    {
        var fp = pane.Footprint;
        var a = (fp[0] + fp[3]) * 0.5f;
        var b = (fp[1] + fp[2]) * 0.5f;
        var n2 = fp[3] - fp[0];
        float len = n2.Length();
        var n = len > 1e-6f ? n2 / len : Vector2.UnitY;
        return (new Vector3(a, pane.BaseZ), new Vector3(a, pane.TopZ), new Vector3(b, pane.TopZ), new Vector3(b, pane.BaseZ), new Vector3(n, 0f));
    }

    /// <summary>What a pane draws as in a view: nothing (its wall is gone), the open hole, or the glass (with the window's entry, when it was hit).</summary>
    public enum PaneLook : byte { Gone = 0, Broken = 1, Glass = 2 }

    /// <summary>⭐ The one rule the layer and its rails read: pane <paramref name="index"/> of <paramref name="terrain"/> as <paramref name="view"/> holds it.</summary>
    public static PaneLook LookOf(TerrainWorld terrain, DoorStates? view, int index, out WindowEntry entry)
    {
        entry = default;
        var pane = terrain.PaneLeaves[index];
        var (panel, opening) = terrain.Panes[index];
        var damage = view?.Damage;
        if (damage != null && pane.Building >= 0 && damage.Collapsed(pane.Building)) return PaneLook.Gone;
        if (damage != null && damage.HasBreaches(panel))
        {
            var o = terrain.Panels[panel].Openings[opening];
            if (damage.Opens(panel, o.At + o.Width * 0.5f, (o.SillZ + o.HeadZ) * 0.5f)) return PaneLook.Gone;   // D-10 — the glass went with the wall
        }
        var windows = view?.Windows;
        if (windows != null && windows.TryGet(panel, opening, out entry)) return entry.Broken ? PaneLook.Broken : PaneLook.Glass;
        return PaneLook.Glass;
    }

    private void DrawPane(TerrainWorld terrain, DoorStates view, WindowTable? windows, int index, Vector3 eye)
    {
        var pane = terrain.PaneLeaves[index];
        var centre = new Vector3((pane.Min + pane.Max) * 0.5f, (pane.BaseZ + pane.TopZ) * 0.5f);
        if (Vector3.DistanceSquared(centre, eye) > DrawRadiusM * DrawRadiusM) return;
        var look = LookOf(terrain, view, index, out var entry);
        if (look == PaneLook.Gone) return;
        var (a0, a1, b1, b0, n) = Corners(pane);
        if (look == PaneLook.Broken)
        {
            BrokenDrawn++;
            DrawFrameShards(a0, a1, b1, b0, index);
            return;
        }
        PanesDrawn++;
        Quad(a0, a1, b1, b0, PaneColour);
        // the sheen: a slanted strip across the pane, as the mockup's
        var along = b0 - a0; var up = a1 - a0;
        Quad(a0 + along * 0.07f + up * 0.06f, a0 + along * 0.26f + up * 0.94f, a0 + along * 0.35f + up * 0.94f, a0 + along * 0.16f + up * 0.06f, SheenColour);
        if (entry.Hits > 0 && !entry.Broken)
        {
            float r = MathF.Min(along.Length(), up.Length()) * CrackShare;
            Crack(a0 + along * (entry.U0 / 255f) + up * (entry.V0 / 255f), along, up, n, r, Seed(index, 0));
            CracksDrawn++;
            if (entry.Hits > 1)
            {
                Crack(a0 + along * (entry.U1 / 255f) + up * (entry.V1 / 255f), along, up, n, r, Seed(index, 1));
                CracksDrawn++;
            }
        }
    }

    private static int Seed(int index, int k) => unchecked(index * 7919 + k * 104729 + 17);

    /// <summary>A crack star on both faces: a white impact dot, seven radial lines, two broken rings (the mockup's).</summary>
    private static void Crack(Vector3 at, Vector3 along, Vector3 up, Vector3 normal, float r, int seed)
    {
        var u = Vector3.Normalize(along); var v = Vector3.Normalize(up);
        for (int face = -1; face <= 1; face += 2)
        {
            var c = at + normal * (0.006f * face);
            Rlgl.Begin(DrawMode.Lines);
            Rlgl.Color4ub(CrackColour.R, CrackColour.G, CrackColour.B, CrackColour.A);
            var rr = new Lcg(seed);
            for (int k = 0; k < 7; k++)
            {
                float a = k * MathF.Tau / 7f + rr.Range(-0.25f, 0.25f), l = r * rr.Range(0.7f, 1f);
                Vertex(c); Vertex(c + (u * MathF.Cos(a) + v * MathF.Sin(a)) * l);
            }
            for (int ring = 1; ring <= 2; ring++)
            {
                float rad = r * (ring == 1 ? 0.35f : 0.6f);
                for (int s = 0; s < 16; s += 2)   // dashed: every other segment
                {
                    float t0 = s * MathF.Tau / 16f, t1 = (s + 1) * MathF.Tau / 16f;
                    Vertex(c + (u * MathF.Cos(t0) + v * MathF.Sin(t0)) * rad); Vertex(c + (u * MathF.Cos(t1) + v * MathF.Sin(t1)) * rad);
                }
            }
            Rlgl.End();
            float d = r * 0.06f;   // the impact dot
            Quad(c - u * d - v * d, c - u * d + v * d, c + u * d + v * d, c + u * d - v * d, CrackColour);
        }
    }

    /// <summary>A few shards left in the broken pane's frame (the mockup's three corners).</summary>
    private static void DrawFrameShards(Vector3 a0, Vector3 a1, Vector3 b1, Vector3 b0, int index)
    {
        var along = b0 - a0; var up = a1 - a0;
        float s = MathF.Min(along.Length(), up.Length()) * 0.12f;
        var u = Vector3.Normalize(along); var v = Vector3.Normalize(up);
        Tri(a0 + u * 0.02f + v * 0.02f, a0 + u * (0.02f + s) + v * 0.04f, a0 + u * 0.05f + v * (0.02f + s), ShardColour);
        Tri(b0 + up - u * 0.02f - v * 0.02f, b0 + up - u * (0.02f + s) - v * 0.05f, b0 + up - u * 0.06f - v * (0.02f + s * 0.8f), ShardColour);
        Tri(a1 - v * 0.03f + u * 0.02f, a1 - v * (0.03f + s) + u * 0.06f, a1 - v * 0.05f + u * (0.02f + s * 0.7f), ShardColour);
    }

    // ── the burst ────────────────────────────────────────────────────────────────────────────

    private struct Shard { public Vector3 At, Velocity, Spin; public float Age, Size; }
    private Shard[] _shards = new Shard[ShardsPerBurst * 4];
    private int _shardCount;

    /// <summary>The panes that turned broken since the last look: each bursts once. The first look over a world only records (a late joiner bursts nothing).</summary>
    private void Notice(TerrainWorld terrain, WindowTable? windows)
    {
        if (windows == null || windows.BrokenCount == 0) { _primed = true; return; }
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (!w.Broken) continue;
            long key = ((long)w.Panel << 8) | w.Opening;
            if (!_broken.Add(key) || !_primed) continue;
            int pane = IndexOfPane(terrain, w.Panel, w.Opening);
            if (pane >= 0) Burst(terrain.PaneLeaves[pane], pane);
        }
        _primed = true;
    }

    private static int IndexOfPane(TerrainWorld terrain, int panel, int opening)
    {
        var panes = terrain.Panes;
        for (int i = 0; i < panes.Count; i++) if (panes[i].Panel == panel && panes[i].Opening == opening) return i;
        return -1;
    }

    private void Burst(TerrainPrism pane, int index)
    {
        Bursts++;
        var (a0, a1, b1, b0, n) = Corners(pane);
        var rnd = new Lcg(Seed(index, 9));
        if (_shardCount + ShardsPerBurst > _shards.Length) Array.Resize(ref _shards, _shards.Length * 2);   // growth only
        for (int k = 0; k < ShardsPerBurst; k++)
        {
            float u = rnd.Range(0.05f, 0.95f), v = rnd.Range(0.05f, 0.95f);
            var at = a0 + (b0 - a0) * u + (a1 - a0) * v;
            float side = rnd.Range(0f, 1f) < 0.65f ? 1f : -1f;   // most out, some in
            var vel = n * side * rnd.Range(1.2f, 3.2f) + new Vector3(rnd.Range(-0.8f, 0.8f), rnd.Range(-0.8f, 0.8f), rnd.Range(0f, 1.5f));
            _shards[_shardCount++] = new Shard { At = at, Velocity = vel, Spin = Vector3.Normalize(new Vector3(rnd.Range(-1f, 1f), rnd.Range(-1f, 1f), 1f)), Size = rnd.Range(0.04f, 0.1f) };
        }
    }

    private void StepShards(float dt)
    {
        if (dt <= 0f) return;
        int keep = 0;
        for (int i = 0; i < _shardCount; i++)
        {
            var s = _shards[i];
            s.Age += dt;
            if (s.Age >= BurstSeconds) continue;
            s.Velocity.Z -= 9.81f * dt;
            s.At += s.Velocity * dt;
            _shards[keep++] = s;
        }
        _shardCount = keep;
    }

    private void DrawShards()
    {
        for (int i = 0; i < _shardCount; i++)
        {
            var s = _shards[i];
            var a = Vector3.Normalize(Vector3.Cross(s.Spin, Vector3.UnitZ) + new Vector3(0.001f, 0f, 0f));
            var b = Vector3.Normalize(Vector3.Cross(s.Spin, a));
            byte alpha = (byte)(ShardColour.A * (1f - s.Age / BurstSeconds));
            Tri(s.At, s.At + a * s.Size, s.At + b * s.Size * 0.7f, new Color(ShardColour.R, ShardColour.G, ShardColour.B, alpha));
        }
    }

    // ── drawing ──────────────────────────────────────────────────────────────────────────────

    private static void Vertex(Vector3 hrot) { var p = HrotToRaylib.Position(hrot); Rlgl.Vertex3f(p.X, p.Y, p.Z); }

    private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
    {
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(colour.R, colour.G, colour.B, colour.A);
        Vertex(a); Vertex(b); Vertex(c);
        Vertex(a); Vertex(c); Vertex(d);
        Rlgl.End();
    }

    private static void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour)
    {
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(colour.R, colour.G, colour.B, colour.A);
        Vertex(a); Vertex(b); Vertex(c);
        Rlgl.End();
    }

    /// <summary>A small deterministic generator (the same shards and cracks every frame and on every host).</summary>
    private struct Lcg
    {
        private uint _s;
        public Lcg(int seed) => _s = (uint)seed * 2654435761u + 1u;
        public float Range(float lo, float hi) { _s = _s * 1664525u + 1013904223u; return lo + (hi - lo) * ((_s >> 8) / 16777216f); }
    }

    /// <summary>The panes of a world by 64 m cell (built once per world — a world is immutable).</summary>
    public sealed class PaneGrid
    {
        public const float Cell = 64f;
        public readonly Dictionary<long, int[]> Cells = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainWorld, PaneGrid> s_of = new();
        public static long Key(int x, int y) => ((long)x << 32) | (uint)y;
        public static PaneGrid Of(TerrainWorld world) => s_of.GetValue(world, static w =>
        {
            var g = new PaneGrid();
            var lists = new Dictionary<long, List<int>>();
            for (int i = 0; i < w.PaneLeaves.Count; i++)
            {
                var c = (w.PaneLeaves[i].Min + w.PaneLeaves[i].Max) * 0.5f;
                long k = Key((int)MathF.Floor(c.X / Cell), (int)MathF.Floor(c.Y / Cell));
                if (!lists.TryGetValue(k, out var l)) lists[k] = l = new List<int>();
                l.Add(i);
            }
            foreach (var (k, l) in lists) g.Cells[k] = l.ToArray();
            return g;
        });
    }
}
