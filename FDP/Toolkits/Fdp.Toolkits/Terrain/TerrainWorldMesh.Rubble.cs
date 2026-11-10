using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    public static partial class TerrainWorldMesh
    {
        /// <summary>⭐ CE-4392 X-2 — the top of a rubble heap, as a share of the footprint (scaled round its centroid).</summary>
        public const float HeapTopShare = 0.55f;

        /// <summary>
        /// ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-2) — a collapsed building's mound drawn as a HEAP: the footprint at the base, the footprint shrunk to <see cref="HeapTopShare"/> round
        /// its centroid at the top, sloped sides between — debris, not a low building. The scaling is affine, so the footprint's own triangulation serves the top. Material
        /// <see cref="BuildingDamageTable.RubbleMaterial"/> (its colour and the earth pattern in the 3-D layer). Presentation only.
        /// </summary>
        public static void AddHeap(TerrainPrism prism, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var fp = prism.Footprint;
            int n = fp.Length;
            if (n < 3) return;
            var c = Vector2.Zero;
            foreach (var p in fp) c += p;
            c /= n;
            Vector2 Top(Vector2 p) => c + ((p - c) * HeapTopShare);
            var topTag = new TerrainTriangleTag(TerrainTriangleKind.Roof, BuildingDamageTable.RubbleMaterial);
            var sideTag = new TerrainTriangleTag(TerrainTriangleKind.Wall, BuildingDamageTable.RubbleMaterial);

            int top = v.Count;
            foreach (var p in fp) v.Add(new Vector3(Top(p), prism.TopZ));
            for (int i = 0; i + 2 < prism.Triangles.Length; i += 3)
            {
                AddUp(v, t, top + prism.Triangles[i], top + prism.Triangles[i + 1], top + prism.Triangles[i + 2]);
                g.Add(topTag);
            }
            bool ccw = PolygonMath.SignedArea2(fp) > 0f;
            for (int i = 0; i < n; i++)
            {
                var a = fp[i]; var b = fp[(i + 1) % n];
                if (!ccw) (a, b) = (b, a);   // walk CCW so the outward side is to the right
                int q = v.Count;
                v.Add(new Vector3(a, prism.BaseZ)); v.Add(new Vector3(b, prism.BaseZ)); v.Add(new Vector3(Top(b), prism.TopZ)); v.Add(new Vector3(Top(a), prism.TopZ));
                t.Add(q); t.Add(q + 1); t.Add(q + 2); t.Add(q); t.Add(q + 2); t.Add(q + 3);
                g.Add(sideTag); g.Add(sideTag);
            }
            AddDebris(prism, c, v, t, g);
        }

        /// <summary>⭐ CE-4392 X-2 — broken blocks on the heap, so it reads as debris and not a platform: one per ~6 m² of footprint (6…40), each a yawed box 0.6–1.6 m wide,
        /// 0.3–0.9 m high, sunk into the top; placed by a hash of the building, so the same every build.</summary>
        private static void AddDebris(TerrainPrism prism, Vector2 centre, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var fp = prism.Footprint;
            float area = MathF.Abs(PolygonMath.SignedArea2(fp)) * 0.5f;
            int count = Math.Clamp((int)(area / 6f), 6, 40);
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Wall, BuildingDamageTable.RubbleMaterial);
            uint seed = (uint)(prism.Building * 2654435761u) ^ 0x9E3779B9u;
            float R() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / (float)0x1000000; }
            for (int k = 0; k < count; k++)
            {
                // a point on the heap's top: a random footprint corner pulled toward the centre
                var corner = fp[(int)(R() * fp.Length) % fp.Length];
                var at = centre + ((corner - centre) * HeapTopShare * R());
                float w = 0.6f + (R() * 1.0f), d = 0.5f + (R() * 0.8f), h = 0.3f + (R() * 0.6f), yaw = R() * MathF.PI;
                float z0 = prism.TopZ - (h * 0.4f), z1 = z0 + h;
                var ax = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw)) * (w * 0.5f); var ay = new Vector2(-MathF.Sin(yaw), MathF.Cos(yaw)) * (d * 0.5f);
                var q = new[] { at - ax - ay, at + ax - ay, at + ax + ay, at - ax + ay };   // CCW
                int top = v.Count;
                foreach (var p in q) v.Add(new Vector3(p, z1));
                t.Add(top); t.Add(top + 1); t.Add(top + 2); t.Add(top); t.Add(top + 2); t.Add(top + 3); g.Add(tag); g.Add(tag);
                for (int i = 0; i < 4; i++)
                {
                    var a = q[i]; var b = q[(i + 1) % 4];
                    int s0 = v.Count;
                    v.Add(new Vector3(a, z0)); v.Add(new Vector3(b, z0)); v.Add(new Vector3(b, z1)); v.Add(new Vector3(a, z1));
                    t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2); t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 3); g.Add(tag); g.Add(tag);
                }
            }
        }
    }
}
