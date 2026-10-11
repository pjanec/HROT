using System;
using System.Numerics;

namespace Fdp.Toolkit.World
{
    /// <summary>
    /// ⭐ CE-1033 S2 (M20, M22 step 1) — which LEVEL a world point is on: 0 = the ground, +n = the n-th surface above it (a roof, a
    /// deck, an upper floor), −n below. A 3-D pick knows a HEIGHT; placement and areas want a level (a ground area over a hill
    /// follows the hill, R-248), so this turns one into the other at the point's own (x, y).
    /// </summary>
    public static class Levels
    {
        /// <summary>The level whose surface at (<paramref name="point"/>.X, .Y) is nearest <paramref name="point"/>.Z.</summary>
        public static int LevelOf(IWorldQuery world, Vector3 point)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));
            var surfaces = world.SurfacesAt(point.X, point.Y, out int ground);
            if (surfaces.Count == 0) return 0;
            int best = ground;
            float bestGap = float.MaxValue;
            for (int i = 0; i < surfaces.Count; i++)
            {
                float gap = MathF.Abs(surfaces[i] - point.Z);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best - ground;
        }
    }
}
