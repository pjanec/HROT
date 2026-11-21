using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ <c>CE-4623</c> AB-6 — the polygon flags the navmesh bake gives (docs/designs/animals/DESIGN_Animals.md §9a). Every ground polygon is
    /// <see cref="Walk"/>; an off-mesh jump link is <see cref="Jump"/> ONLY, and every query filter EXCLUDES it unless the mover jumps
    /// (<see cref="NavLayerSelection.JumpModifier"/>) — so a soldier's path never changes.
    /// </summary>
    public static class NavPolyFlags
    {
        public const int Walk = 1;
        public const int Jump = 2;
    }

    /// <summary>
    /// ⭐ <c>CE-4623</c> AB-6 — the off-mesh JUMP LINKS the infantry navmesh carries at every LOW free wall / fence (docs/designs/animals/DESIGN_Animals.md §9a).
    /// A free wall (<see cref="TerrainWallPanel.Building"/> == −1) no higher than <see cref="MaxHeightM"/> gets a bidirectional link every
    /// <see cref="SpacingM"/> along it (at least one, at its middle), from <see cref="ReachM"/> beyond one face to <see cref="ReachM"/> beyond the other.
    /// Bake time only — allocation is fine here. Positions are RECAST space (x east, y up, z north).
    /// </summary>
    public static class JumpLinks
    {
        /// <summary>The highest barrier a jumper clears (m) — AN-13's "a low obstacle up to about 1.2 m". The kinematics' gate reads the same number.</summary>
        public const float MaxHeightM = 1.2f;
        /// <summary>The spacing of the links along a wall (m).</summary>
        public const float SpacingM = 4f;
        /// <summary>How far past the wall's face each end lies (m).</summary>
        public const float ReachM = 1.0f;
        /// <summary>The off-mesh connection's snap radius (m).</summary>
        public const float RadiusM = 0.4f;

        /// <summary>One link: start and end in Recast space, and its stable index (the off-mesh connection's user id).</summary>
        public readonly record struct Link(float X0, float Y0, float Z0, float X1, float Y1, float Z1, int UserId);

        /// <summary>True when a free-wall barrier of this height can be jumped (the one test the links and the kinematics share).</summary>
        public static bool IsJumpable(float heightM) => heightM <= MaxHeightM;

        /// <summary>Every jump link of <paramref name="world"/>, in panel order (so the ids are stable).</summary>
        public static List<Link> For(TerrainWorld world)
        {
            var list = new List<Link>();
            var panels = world.Panels;
            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (p.Building != -1 || !IsJumpable(p.TopZ - p.BaseZ)) continue;
                float len = p.Length;
                if (len < 1e-3f) continue;
                var dir = (p.B - p.A) / len;
                var n = new Vector2(-dir.Y, dir.X) * (p.Thickness * 0.5f + ReachM);
                int count = Math.Max(1, (int)MathF.Ceiling(len / SpacingM));
                for (int k = 0; k < count; k++)
                {
                    float t = (k + 0.5f) * len / count;   // the middle of each equal share ⇒ ≤ SpacingM apart, and a short wall's one link is its middle
                    var c = p.A + dir * t;
                    var s = c - n; var e = c + n;
                    list.Add(new Link(s.X, world.GroundHeightAt(s.X, s.Y), s.Y, e.X, world.GroundHeightAt(e.X, e.Y), e.Y, list.Count));
                }
            }
            return list;
        }
    }
}
