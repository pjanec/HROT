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

        /// <summary>The launch velocity for a round of <paramref name="speed"/> from <paramref name="from"/> to <paramref name="to"/>
        /// (<see cref="TryLaunch"/>; when no arc clears, the full-speed lob as before).</summary>
        public static Vector3 Launch(Fdp.Toolkit.World.IWorldQuery? world, Vector3 from, Vector3 to, float speed, float penetration)
        {
            TryLaunch(world, from, to, speed, penetration, out var velocity);
            return velocity;
        }

        /// <summary>⭐ <c>CE-3296</c> — the soft tosses tried before the full-speed lob: multiples of the slowest throw that reaches.</summary>
        private static readonly float[] s_tossFactors = { 1.05f, 1.2f, 1.4f, 1.7f, 2.0f, 2.5f, 3.2f };

        /// <summary>
        /// ⭐ <c>CE-3296</c> — a THROW (below <see cref="CombatConstants.HighArcMinMuzzleVelocity"/>): the low arc at full speed first; when the
        /// terrain stops it, the SOFTEST toss that clears — from just above the slowest speed that reaches, low then high, up to full speed —
        /// and only then the full-speed lob. False when no arc clears (the lob is returned anyway). 📌 behaviors' note
        /// (<c>bc-room-clear</c>): a 2–3 m throw through a doorway whose low arc was judged blocked was lobbed at 15 m/s — apex ≈ 11 m,
        /// flight ≈ 3 s — and the stun grenade's 1.5 s fuze burst it 12 m up; a toss at the slowest speed peaks under a metre.
        /// A launcher keeps the high arc it always flew (true).
        /// </summary>
        public static bool TryLaunch(Fdp.Toolkit.World.IWorldQuery? world, Vector3 from, Vector3 to, float speed, float penetration, out Vector3 velocity)
        {
            if (speed >= CombatConstants.HighArcMinMuzzleVelocity)
            {
                velocity = FireProcessingSystem.ArcLaunch(from, to, speed, high: true);
                return true;
            }
            velocity = FireProcessingSystem.ArcLaunch(from, to, speed, high: false);
            if (world == null || Clears(world, from, to, velocity, penetration)) return true;
            float slowest = SlowestReaching(from, to);
            foreach (float f in s_tossFactors)
            {
                float v = slowest * f;
                if (v >= speed) break;
                for (int arc = 0; arc < 2; arc++)
                {
                    var toss = FireProcessingSystem.ArcLaunch(from, to, v, high: arc == 1);
                    if (Clears(world, from, to, toss, penetration)) { velocity = toss; return true; }
                }
            }
            velocity = FireProcessingSystem.ArcLaunch(from, to, speed, high: true);
            return Clears(world, from, to, velocity, penetration);
        }

        /// <summary>The slowest launch speed that reaches <paramref name="to"/> from <paramref name="from"/> (v² = g·(h + √(d² + h²))).</summary>
        public static float SlowestReaching(Vector3 from, Vector3 to)
        {
            float d = new Vector2(to.X - from.X, to.Y - from.Y).Length(), h = to.Z - from.Z;
            return MathF.Sqrt(CombatConstants.Gravity * (h + MathF.Sqrt(d * d + h * h)));
        }

        /// <summary>True when a round launched at <paramref name="velocity"/> reaches (near) <paramref name="to"/> with nothing stopping it.</summary>
        public static bool Clears(Fdp.Toolkit.World.IWorldQuery world, Vector3 from, Vector3 to, Vector3 velocity, float penetration)
            => Clears(world, from, to, velocity, penetration, Samples);

        /// <summary>⭐ Indirect fire IF-1 — <see cref="Clears(Fdp.Toolkit.World.IWorldQuery, Vector3, Vector3, Vector3, float)"/> in
        /// <paramref name="samples"/> segments (a gun's kilometres need more than a throw's 12 — <c>FireSolution.ClearsDensely</c>).</summary>
        public static bool Clears(Fdp.Toolkit.World.IWorldQuery world, Vector3 from, Vector3 to, Vector3 velocity, float penetration, int samples)
        {
            var flat = new Vector2(to.X - from.X, to.Y - from.Y);
            float distance = flat.Length();
            float horizontal = new Vector2(velocity.X, velocity.Y).Length();
            if (distance < 1e-3f || horizontal < 1e-3f) return true;
            float until = MathF.Max(distance - EndClearanceMetres, 0f) / horizontal;   // seconds to just short of the point
            var previous = from;
            for (int i = 1; i <= samples; i++)
            {
                float t = until * i / samples;
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
