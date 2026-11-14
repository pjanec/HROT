using System.Numerics;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 E2b (R-4030) — a FLAT SUMO net (no lane carries a z) is laid on OUR ground: the road scene's vertices, the cars and the
/// pedestrians all take <c>ground(x, y)</c> on top of the z the net gives them (0 on a flat net, or a small height above the road
/// plane, e.g. a signal head above its pole foot). A net WITH z is never touched (R-4025).
/// </summary>
/// <remarks>
/// ⭐ One rule for every reader: <see cref="SumoNetTerrainLoader"/> builds the ground function ONCE from the terrain's own
/// <c>TerrainWorld.GroundHeightAt</c> and hands it to the road scene and (through <see cref="TrafficNetCache.Drape"/>) to the mirrors, so no
/// host can differ. ⚠ Not <c>GizmoRenderer3D</c>'s <c>Subdivide</c>: that one is private to the render sink in <c>Fdp.Presentation</c>
/// (which Hrot.Traffic does not reference, and which an engine-agnostic scene must not) and 4-splits a triangle, which turns a long thin
/// road ribbon into hundreds of pieces; here a triangle is cut only where the ground under it is not a plane, a FAT one by bisecting its longest edge and a THIN one (a ribbon's
/// half) across its length, so a ribbon costs ~length / step pieces on a hill and nothing on a plateau. The step plays the same part as its <c>DrapeStep</c>.
/// </remarks>
public static class TrafficDrape
{
    /// <summary>The finest cut (m) of a draped road triangle on ground that really curves: the ground is checked every half of it along a road, and a triangle whose ground is a plane is not cut at all.</summary>
    public const float RoadStep = 8f;

    /// <summary>The ambient ground's finest cut (m): its cells are 40 m and its relief is hills of ≥ 170 m wavelength.</summary>
    public const float GroundStep = 20f;

    private const int MaxDepth = 24;

    /// <summary>How far (m) the ground may leave the plane of a triangle's corners before the triangle is cut further.</summary>
    public const float PlanarTolerance = 0.03f;

    /// <summary>True when no lane of <paramref name="net"/> carries a z (<c>Lane.ShapeZ == null</c>): such a net is draped (R-4030).</summary>
    public static bool IsFlat(NetworkModel net)
    {
        foreach (var lane in net.LanesByHandle)
            if (lane.ShapeZ is { Count: > 0 }) return false;
        return true;
    }

    /// <summary><paramref name="mesh"/> laid on the ground: every triangle whose ground is not a plane (within <see cref="PlanarTolerance"/>, checked every half <paramref name="step"/>) is cut — a fat one by bisecting its longest edge, a thin one across its length — down to <paramref name="step"/>, then every vertex takes <c>z + ground(x, y)</c>.</summary>
    public static TrafficRoadMesh Mesh(TrafficRoadMesh mesh, Func<float, float, float> ground, float step)
    {
        var verts = new List<Vector3>(mesh.Vertices.Length + mesh.Vertices.Length / 2);
        verts.AddRange(mesh.Vertices);
        var inds = new List<int>(mesh.Indices.Length * 2);
        var mids = new Dictionary<(int, int), int>();   // ⚠ a (lo << 32 | hi) long key hashes to lo ^ hi — a lattice's edges collide by the thousand
        float step2 = step * step;

        int Mid(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (mids.TryGetValue(key, out var m)) return m;
            verts.Add((verts[a] + verts[b]) * 0.5f);
            return mids[key] = verts.Count - 1;
        }

        float slab = step * 0.7f;   // a triangle no taller than this is "thin": cut across its length; a taller one is bisected

        void Emit(List<int> poly, int depth)
        {
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                var p = verts[poly[0]]; var q = verts[poly[i]]; var r = verts[poly[i + 1]];
                if (MathF.Abs((q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X)) < 1e-6f) continue;   // a sliver of zero area
                Split(poly[0], poly[i], poly[i + 1], depth);
            }
        }

        // A THIN triangle (a long road ribbon's half): cut it ACROSS its longest edge, at the middle of that edge, and look at each half again.
        // (Bisecting its longest edge instead would halve only one of its two long edges per step and cost the square of its length.)
        void Cut(int a, int b, int c, int depth)
        {
            float ab = XY(a, b), bc = XY(b, c), ca = XY(c, a);
            if (bc >= ab && bc >= ca) (a, b, c) = (b, c, a);
            else if (ca >= ab && ca >= bc) (a, b, c) = (c, a, b);
            var pa = verts[a]; var pb = verts[b];
            var axis = new Vector2(pb.X - pa.X, pb.Y - pa.Y);
            float len = axis.Length();
            axis /= len;
            float T(int i) => (verts[i].X - pa.X) * axis.X + (verts[i].Y - pa.Y) * axis.Y;
            float tc = T(c), cut = (MathF.Min(0f, tc) + MathF.Max(len, tc)) * 0.5f;
            var poly = new[] { a, b, c };
            var left = new List<int>(4); var right = new List<int>(4);
            for (int i = 0; i < 3; i++)
            {
                int p = poly[i], q = poly[(i + 1) % 3];
                float sp = T(p) - cut, sq = T(q) - cut;
                if (sp <= 1e-5f) left.Add(p);
                if (sp >= -1e-5f) right.Add(p);
                if ((sp < -1e-5f && sq > 1e-5f) || (sp > 1e-5f && sq < -1e-5f))
                {
                    verts.Add(Vector3.Lerp(verts[p], verts[q], sp / (sp - sq)));
                    left.Add(verts.Count - 1); right.Add(verts.Count - 1);
                }
            }
            if (left.Count >= 3) Emit(left, depth + 1);
            if (right.Count >= 3) Emit(right, depth + 1);
        }

        float XY(int i, int j) => Vector2.DistanceSquared(new Vector2(verts[i].X, verts[i].Y), new Vector2(verts[j].X, verts[j].Y));

        void Split(int a, int b, int c, int depth)
        {
            float ab = XY(a, b), bc = XY(b, c), ca = XY(c, a);
            float longest = MathF.Max(ab, MathF.Max(bc, ca));
            if (longest <= step2 || depth >= MaxDepth) { inds.Add(a); inds.Add(b); inds.Add(c); return; }
            var A = verts[a]; var B = verts[b]; var C = verts[c];
            float twiceArea = MathF.Abs((B.X - A.X) * (C.Y - A.Y) - (B.Y - A.Y) * (C.X - A.X));
            float height = twiceArea / MathF.Sqrt(longest);
            if (IsPlanar(a, b, c, longest, height)) { inds.Add(a); inds.Add(b); inds.Add(c); return; }   // the ground under it is a plane: cutting it adds nothing
            if (height <= slab && height * height * 9f < longest) { Cut(a, b, c, depth); return; }   // thin (the long edge is over 3x the height); a squat one is bisected, which always shortens its long edge
            // a fat triangle: bisect the longest edge (the two halves keep the winding; the edge's midpoint is one shared vertex)
            if (ab >= bc && ab >= ca) { int m = Mid(a, b); Split(a, m, c, depth + 1); Split(m, b, c, depth + 1); }
            else if (bc >= ca) { int m = Mid(b, c); Split(a, b, m, depth + 1); Split(a, m, c, depth + 1); }
            else { int m = Mid(c, a); Split(a, b, m, depth + 1); Split(m, b, c, depth + 1); }
        }

        // ⭐ Is the ground under this triangle (within PlanarTolerance) the plane through its three corners? Sampled on a lattice no coarser than step/2
        // along its longest edge: a flat or evenly sloping stretch (the common case, and every plateau) costs nothing; a hill is cut as far as it curves.
        bool IsPlanar(int a, int b, int c, float longestSq, float height)
        {
            // the longest edge is the lattice's axis
            float ab = XY(a, b), bc = XY(b, c), ca = XY(c, a);
            if (bc >= ab && bc >= ca) (a, b, c) = (b, c, a);
            else if (ca >= ab && ca >= bc) (a, b, c) = (c, a, b);
            var A = verts[a]; var B = verts[b]; var C = verts[c];
            float gA = ground(A.X, A.Y), gB = ground(B.X, B.Y), gC = ground(C.X, C.Y);
            int nU = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(longestSq) / (step * 0.5f)), 2, 1024);
            int nV = Math.Clamp((int)MathF.Ceiling(height / (step * 0.5f)), 1, 16);
            for (int i = 0; i <= nU; i++)
            {
                float u = (float)i / nU;
                for (int j = 0; j <= nV; j++)
                {
                    float v = (float)j / nV * (1f - u);
                    float x = A.X + u * (B.X - A.X) + v * (C.X - A.X), y = A.Y + u * (B.Y - A.Y) + v * (C.Y - A.Y);
                    if (MathF.Abs(ground(x, y) - (gA + u * (gB - gA) + v * (gC - gA))) > PlanarTolerance) return false;
                }
            }
            return true;
        }

        for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            Split(mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2], 0);

        var outV = new Vector3[verts.Count];
        for (int i = 0; i < outV.Length; i++)
        {
            var v = verts[i];
            outV[i] = new Vector3(v.X, v.Y, v.Z + ground(v.X, v.Y));
        }
        return mesh with { Vertices = outV, Indices = inds.ToArray() };
    }

    /// <summary>One point laid on the ground: <c>z + ground(x, y)</c>.</summary>
    public static Vector3 Point(Vector3 p, Func<float, float, float> ground) => new(p.X, p.Y, p.Z + ground(p.X, p.Y));
}
