using System;
using System.Numerics;
using Fdp.Toolkit.Combat.Systems;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>WL-4</c> L-1 (📄 <c>docs/DESIGN_Weapon_Layers.md</c> §5f) — the arc a THROWN round flies. The low arc first (a throw);
    /// when the terrain would stop it on the way — a wall the target crouches behind — the high arc (a lob), which reaches the same
    /// point whenever the low one does. 📐 Measured: from 12–16 m a low M67 throw passes 0.6–1.2 m above a wall standing 2–3 m in
    /// front of the aim point, i.e. it hits a 1.2 m sandbag wall. The test walks the arc through the SAME terrain rule the flight
    /// uses (<see cref="TerrainPenetration.Carry"/>), so "clears" means the round will not be stopped. A launcher (≥ 40 m/s) keeps
    /// the high arc it always flew.
    /// </summary>
    public static class ThrowArc
    {
        /// <summary>Segments the arc is checked in (a 20 m throw ⇒ ~1.7 m per segment).</summary>
        public const int Samples = 12;

        /// <summary>The last segment stops this far short of the aim point, so the ground AT the point does not count as cover.</summary>
        public const float EndClearanceMetres = 0.3f;

        /// <summary>The launch velocity for a round of <paramref name="speed"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static Vector3 Launch(Fdp.Toolkit.World.IWorldQuery? world, Vector3 from, Vector3 to, float speed, float penetration)
        {
            if (speed >= CombatConstants.HighArcMinMuzzleVelocity)
                return FireProcessingSystem.ArcLaunch(from, to, speed, high: true);
            var low = FireProcessingSystem.ArcLaunch(from, to, speed, high: false);
            if (world == null || Clears(world, from, to, low, penetration)) return low;
            return FireProcessingSystem.ArcLaunch(from, to, speed, high: true);
        }

        /// <summary>True when a round launched at <paramref name="velocity"/> reaches (near) <paramref name="to"/> with nothing stopping it.</summary>
        public static bool Clears(Fdp.Toolkit.World.IWorldQuery world, Vector3 from, Vector3 to, Vector3 velocity, float penetration)
        {
            var flat = new Vector2(to.X - from.X, to.Y - from.Y);
            float distance = flat.Length();
            float horizontal = new Vector2(velocity.X, velocity.Y).Length();
            if (distance < 1e-3f || horizontal < 1e-3f) return true;
            float until = MathF.Max(distance - EndClearanceMetres, 0f) / horizontal;   // seconds to just short of the point
            var previous = from;
            for (int i = 1; i <= Samples; i++)
            {
                float t = until * i / Samples;
                var at = from + velocity * t;
                at.Z -= 0.5f * CombatConstants.Gravity * t * t;
                float damage = 1f, pen = penetration;
                if (TerrainPenetration.Carry(world, previous, at, ref damage, ref pen, out _)) return false;
                previous = at;
            }
            return true;
        }
    }
}
