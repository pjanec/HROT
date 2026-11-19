using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Hrot.Look.Terrain;

/// <summary>⭐ CE-5034 GW-3 — one glazed window's pane as drawn: its four corners in the WORLD (A bottom-left, B bottom-right, C top-right, D top-left, seen from
/// <see cref="Normal"/>'s side), its window (<see cref="Panel"/>, <see cref="Opening"/>).</summary>
public readonly record struct PaneQuad(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector3 Normal, int Panel, byte Opening);

/// <summary>A straight line segment in the world — a crack's ray or ring piece.</summary>
public readonly record struct GlassLine(Vector3 From, Vector3 To);

/// <summary>A small triangle in the world — a shard left in a broken window's frame.</summary>
public readonly record struct GlassShard(Vector3 A, Vector3 B, Vector3 C);

/// <summary>
/// ⭐⭐ CE-5034 GW-3 (📄 docs/DESIGN_Glass_Windows.md §5, §6a; the shared look library, DESIGN_Vehicle_Appearance_State §7) — how a glazed window LOOKS, as a pure
/// function of the terrain, the view's window and building damage (replicated, GW-2) and the window's key: the panes still standing, a crack star per kept hit
/// at its (u, v), a few shards in a broken frame, a shard burst's flight. The same on every renderer and every node; never the wall clock.
/// <para>⭐ A pane is drawn unless its window is BROKEN in the view (<see cref="WindowTable.PaneBroken"/> — a breach that overlaps a window marks it broken there,
/// D-10) or its building COLLAPSED. ⛔ Not <see cref="DamagedBuildings.Keeps"/>: it drops every piece of a panel with ANY breach.</para>
/// </summary>
public static class GlassLook
{
    /// <summary>How far each face of a crack star stands off the pane's plane (m): both faces show it (§5) and neither fights the pane for depth.</summary>
    public const float FaceOffset = 0.006f;

    /// <summary>
    /// The pane's OUTSIDE normal: away from its building's middle (the mean of its walls' midpoints) — a panel's own winding says nothing about which side is the
    /// street. A free wall (no building) keeps <see cref="NormalOf"/>. Called once per burst, never per frame.
    /// </summary>
    public static Vector3 OutwardOf(TerrainWorld world, int panel)
    {
        var n = NormalOf(world, panel);
        var p = world.Panels[panel];
        if (p.Building < 0) return n;
        Vector2 sum = Vector2.Zero;
        int count = 0;
        for (int i = 0; i < world.Panels.Count; i++)
            if (world.Panels[i].Building == p.Building) { sum += (world.Panels[i].A + world.Panels[i].B) * 0.5f; count++; }
        var mid = (p.A + p.B) * 0.5f - sum / count;
        return mid.X * n.X + mid.Y * n.Y < 0f ? -n : n;
    }

    /// <summary>A crack star's outer radius as a fraction of the pane's smaller side (the mockup's close-up).</summary>
    public const float StarRadius = 0.22f;

    /// <summary>
    /// Appends the panes standing in <paramref name="doors"/>' view (all of them with no live state) and returns how many. Allocation-free past the caller's
    /// list's growth.
    /// </summary>
    public static int Panes(TerrainWorld world, DoorStates? doors, List<PaneQuad> into)
    {
        var leaves = world.PaneLeaves;
        if (leaves.Count == 0) return 0;
        var panes = world.Panes;
        var windows = doors?.Windows;
        var damage = doors?.Damage;
        int n = 0;
        for (int i = 0; i < leaves.Count; i++)
        {
            var (panel, opening) = panes[i];
            if (windows != null && windows.PaneBroken(panel, opening)) continue;
            int building = world.Panels[panel].Building;
            if (damage != null && building >= 0 && damage.Collapsed(building)) continue;
            into.Add(Quad(world, panel, opening));
            n++;
        }
        return n;
    }

    /// <summary>The pane of window (<paramref name="panel"/>, <paramref name="opening"/>) as a quad in the window's opening, on the wall's centre line.</summary>
    public static PaneQuad Quad(TerrainWorld world, int panel, int opening)
    {
        var a = PointAt(world, panel, opening, 0f, 0f);
        var b = PointAt(world, panel, opening, 1f, 0f);
        var c = PointAt(world, panel, opening, 1f, 1f);
        var d = PointAt(world, panel, opening, 0f, 1f);
        return new PaneQuad(a, b, c, d, NormalOf(world, panel), panel, (byte)opening);
    }

    /// <summary>The world point at pane coordinates (<paramref name="u"/> along the opening from its start, <paramref name="v"/> up from its sill, 0..1) — the
    /// exact inverse of <see cref="WindowDamages.PaneUv"/>, so a crack lands where the round crossed.</summary>
    public static Vector3 PointAt(TerrainWorld world, int panel, int opening, float u, float v)
    {
        var p = world.Panels[panel];
        var o = p.Openings[opening];
        var dir = p.B - p.A;
        float len = dir.Length();
        dir = len > 0f ? dir / len : Vector2.UnitX;
        var at = p.A + dir * (o.At + u * o.Width);
        return new Vector3(at.X, at.Y, o.SillZ + v * (o.HeadZ - o.SillZ));
    }

    /// <summary>The pane's normal: the wall's left-hand side (the side <see cref="TerrainWorld.PaneLeaves"/> builds with +n).</summary>
    public static Vector3 NormalOf(TerrainWorld world, int panel)
    {
        var p = world.Panels[panel];
        var dir = p.B - p.A;
        float len = dir.Length();
        dir = len > 0f ? dir / len : Vector2.UnitX;
        return new Vector3(-dir.Y, dir.X, 0f);
    }

    /// <summary>
    /// The crack star of kept hit <paramref name="hit"/> (0 or 1) of <paramref name="entry"/>, on BOTH faces (§5, D-6): radial rays of varied length and two broken
    /// rings round the impact point — appended as segments; 0 when that hit is not kept (a broken window keeps none, an entry with one hit has no second).
    /// The shape is seeded by the window and the hit, so every renderer draws the same star.
    /// </summary>
    public static int CrackStar(TerrainWorld world, in WindowEntry entry, int hit, List<GlassLine> into)
    {
        if (entry.Broken || hit < 0 || hit >= Math.Min((int)entry.Hits, 2)) return 0;
        float u = (hit == 0 ? entry.U0 : entry.U1) / 255f, v = (hit == 0 ? entry.V0 : entry.V1) / 255f;
        var p = world.Panels[entry.Panel];
        var o = p.Openings[entry.Opening];
        var dir2 = p.B - p.A;
        float len = dir2.Length();
        dir2 = len > 0f ? dir2 / len : Vector2.UnitX;
        var along = new Vector3(dir2.X, dir2.Y, 0f);
        var normal = new Vector3(-dir2.Y, dir2.X, 0f);
        float r = StarRadius * MathF.Min(o.Width, o.HeadZ - o.SillZ);
        var centre = PointAt(world, entry.Panel, entry.Opening, u, v);
        uint seed = Seed(entry.Panel, entry.Opening, hit);
        int n = 0;
        for (int face = -1; face <= 1; face += 2)
        {
            var c = centre + normal * (FaceOffset * face);
            uint s = seed;
            int rays = 6 + (int)(Next(ref s) * 3f);   // 6..8 rays
            for (int k = 0; k < rays; k++)
            {
                float ang = (k + 0.5f * Next(ref s)) * MathF.Tau / rays;
                float reach = r * (0.55f + 0.45f * Next(ref s));
                into.Add(new GlassLine(c + Dir(along, ang) * (r * 0.05f), c + Dir(along, ang) * reach));
                n++;
            }
            for (int ring = 1; ring <= 2; ring++)   // two broken rings: each drawn as arcs with gaps
            {
                float rr = r * (ring == 1 ? 0.35f : 0.6f);
                const int Steps = 12;
                for (int k = 0; k < Steps; k++)
                {
                    if (Next(ref s) < 0.35f) continue;   // the breaks in the ring
                    float a0 = k * MathF.Tau / Steps, a1 = (k + 1) * MathF.Tau / Steps;
                    into.Add(new GlassLine(c + Dir(along, a0) * rr, c + Dir(along, a1) * rr));
                    n++;
                }
            }
        }
        return n;
    }

    /// <summary>The impact point of kept hit <paramref name="hit"/> (the star's centre, on the pane's plane) — the white dot; null when the hit is not kept.</summary>
    public static Vector3? ImpactOf(TerrainWorld world, in WindowEntry entry, int hit)
        => entry.Broken || hit < 0 || hit >= Math.Min((int)entry.Hits, 2) ? null
         : PointAt(world, entry.Panel, entry.Opening, (hit == 0 ? entry.U0 : entry.U1) / 255f, (hit == 0 ? entry.V0 : entry.V1) / 255f);

    /// <summary>A broken window's frame: a few shards left at its edges (§5), seeded by the window — appended; 3..5 of them.</summary>
    public static int Shards(TerrainWorld world, int panel, int opening, List<GlassShard> into)
    {
        uint s = Seed(panel, opening, 7);
        int count = 3 + (int)(Next(ref s) * 3f);
        for (int k = 0; k < count; k++)
        {
            // a shard grows in from a corner or an edge: its base on the frame, its tip a little inward
            float edgeU = Next(ref s) < 0.5f ? 0f : 1f, edgeV = Next(ref s);
            bool onSide = Next(ref s) < 0.6f;
            float u0 = onSide ? edgeU : Next(ref s), v0 = onSide ? edgeV : (Next(ref s) < 0.5f ? 0f : 1f);
            float w = 0.06f + 0.08f * Next(ref s);
            float du = onSide ? 0f : w, dv = onSide ? w : 0f;
            float tipU = Math.Clamp(u0 + (u0 < 0.5f ? 1 : -1) * (0.08f + 0.12f * Next(ref s)) * (onSide ? 1f : 0.3f), 0f, 1f);
            float tipV = Math.Clamp(v0 + (v0 < 0.5f ? 1 : -1) * (0.08f + 0.12f * Next(ref s)) * (onSide ? 0.3f : 1f), 0f, 1f);
            into.Add(new GlassShard(PointAt(world, panel, opening, u0 - du * 0.5f, v0 - dv * 0.5f), PointAt(world, panel, opening, u0 + du * 0.5f, v0 + dv * 0.5f),
                                    PointAt(world, panel, opening, tipU, tipV)));
        }
        return count;
    }

    /// <summary>How long a shard burst flies (s of sim time) — out and down, then gone.</summary>
    public const float BurstSeconds = 1.1f;

    /// <summary>How many shards a burst throws.</summary>
    public const int BurstShards = 18;

    /// <summary>
    /// Where shard <paramref name="index"/> of a burst from <paramref name="origin"/> (the pane's centre) is <paramref name="age"/> seconds after it broke: thrown
    /// OUTWARD along <paramref name="outward"/> (either face — the side the round left by) and spread, falling under gravity (§5 "outward and down"); null once the
    /// burst is over. Seeded by <paramref name="seed"/> (the window), so it is the same on every renderer.
    /// </summary>
    public static Vector3? BurstShard(Vector3 origin, Vector3 outward, uint seed, int index, float age)
    {
        if (age < 0f || age > BurstSeconds || index < 0 || index >= BurstShards) return null;
        uint s = seed * 2654435761u + (uint)index * 40503u + 1u;
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, outward.LengthSquared() > 1e-6f ? outward : Vector3.UnitX));
        float speed = 1.5f + 2.5f * Next(ref s);
        var v0 = Vector3.Normalize(outward) * speed + side * ((Next(ref s) - 0.5f) * 2.4f) + Vector3.UnitZ * ((Next(ref s) - 0.3f) * 1.6f);
        var start = origin + side * ((Next(ref s) - 0.5f) * 0.8f) + Vector3.UnitZ * ((Next(ref s) - 0.5f) * 0.8f);
        return start + v0 * age + new Vector3(0f, 0f, -0.5f * 9.81f * age * age);
    }

    /// <summary>The window's seed (deterministic): its panel, opening and a salt.</summary>
    public static uint Seed(int panel, int opening, int salt) => unchecked((uint)panel * 73856093u ^ (uint)opening * 19349663u ^ (uint)(salt + 1) * 83492791u);

    private static Vector3 Dir(Vector3 along, float angle) => along * MathF.Cos(angle) + Vector3.UnitZ * MathF.Sin(angle);

    private static float Next(ref uint s)
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xFFFFFF) / (float)0x1000000;
    }
}

/// <summary>
/// ⭐ CE-5034 GW-3 (§6a, D-8) — ONE renderer's memory of the windows it has seen, so it plays a shard burst when a window TURNS broken between two tables it saw:
/// ⛔ none on the first table (a late joiner, a load — "a state change, so a late joiner plays none"), none for a window whose building collapsed or whose panel a
/// breach re-cut in the same change (D-10: the breach's own debris is the effect). Each renderer owns one (the <c>BrakeLights</c> shape).
/// </summary>
public sealed class ShatterWatch
{
    private HashSet<long> _broken = new(), _now = new();
    private bool _seen;

    /// <summary>
    /// The windows broken in <paramref name="table"/> that were not broken in the last table seen, appended as (panel, opening); returns how many. The first call
    /// only remembers.
    /// </summary>
    public int Seen(WindowTable? table, TerrainWorld world, BuildingDamageTable? damage, List<(int Panel, int Opening)> into)
    {
        int n = 0;
        var now = _now;
        now.Clear();
        if (table != null)
            for (int i = 0; i < table.Count; i++)
            {
                var w = table[i];
                if (!w.Broken) continue;
                long key = ((long)w.Panel << 8) | w.Opening;
                now.Add(key);
                if (!_seen || _broken.Contains(key)) continue;
                if (damage != null && (uint)w.Panel < (uint)world.Panels.Count)
                {
                    int b = world.Panels[w.Panel].Building;
                    if ((b >= 0 && damage.Collapsed(b)) || damage.HasBreaches(w.Panel)) continue;   // D-10: the breach's debris is the effect
                }
                into.Add((w.Panel, w.Opening));
                n++;
            }
        (_broken, _now) = (_now, _broken);
        _seen = true;
        return n;
    }

    /// <summary>Forget everything (a new world): the next table is a first sight again.</summary>
    public void Reset() { _broken.Clear(); _seen = false; }
}
