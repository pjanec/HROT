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
        }
    }
}
