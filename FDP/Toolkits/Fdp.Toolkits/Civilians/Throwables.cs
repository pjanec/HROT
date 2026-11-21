using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 3 (AW-8, AW-9; docs/DESIGN_Civilians_And_Crowd_Control.md §5i.3 PK-1, PK-3) — what a rioter can pick up off
    /// the ground: the nearest LYING hand item (backend's <see cref="RoundPickUp"/>, B-13 — a stone that missed, a canister still venting)
    /// that is not lethal and fits his hands. A round he sees is a lightweight replica (<see cref="LightweightRound"/>) or, on the node that
    /// flies it, the round itself (<see cref="BallisticProjectile"/>). The pick-up itself is backend's: <see cref="RoundPickUp.Ask"/>.
    /// </summary>
    public static class Throwables
    {
        /// <summary>How far a rioter looks for something lying to throw (m).</summary>
        public const float SearchMetres = 15f;

        /// <summary>The nearest lying, non-lethal hand item within <paramref name="radius"/> of <paramref name="man"/> that his hands have
        /// room for. False when there is none.</summary>
        public static bool Nearest(EntityRepository world, Entity man, float radius, out Entity round)
        {
            round = Entity.Null;
            if (!world.IsAlive(man) || !world.HasComponent<SimTransform>(man)) return false;
            var me = world.GetComponentRO<SimTransform>(man).Position;
            float best = radius * radius;
            if (world.IsComponentTypeRegistered<LightweightRound>())
                foreach (var e in world.Query().With<LightweightRound>().With<SimTransform>().Build())
                    Consider(world, man, me, e, ref best, ref round);
            if (world.IsComponentTypeRegistered<BallisticProjectile>())
                foreach (var e in world.Query().With<BallisticProjectile>().With<SimTransform>().Build())
                    Consider(world, man, me, e, ref best, ref round);
            return !round.IsNull;
        }

        private static void Consider(EntityRepository world, Entity man, Vector3 me, Entity e, ref float best, ref Entity found)
        {
            var at = world.GetComponentRO<SimTransform>(e).Position;
            float d2 = (at.X - me.X) * (at.X - me.X) + (at.Y - me.Y) * (at.Y - me.Y);
            if (d2 >= best || !RoundPickUp.IsLying(world, e) || !RoundPickUp.IsHandItem(world, e)) return;
            long ammo = RoundPickUp.AmmoOf(world, e);
            if (ammo == 0 || ItemAppearances.IsLethal(world, ammo) || !PersonHands.HasRoom(world, man, ammo)) return;   // PK-3: a live grenade is the armed path
            best = d2;
            found = e;
        }
    }
}
