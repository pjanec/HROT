using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 1 (CIV-4, D5; docs/DESIGN_Civilians_And_Crowd_Control.md §5i) — a BLOW: a fist, a club, a baton. Contact damage
    /// with a knock-down, through the ONE damage chain — a <see cref="DamageAssessedEvent"/> {Blunt} on the striker's node's bus, applied
    /// only where the victim's Health is claimed (<c>HealthApplicationSystem</c>), which stores the knock-down in <see cref="EffectState"/>
    /// (replicated). ⛔ No round, no muzzle: the ballistic path is for things that fly.
    /// <para>⚠ A victim whose Health another node claims is not hurt here (the gate drops it) — a scenario cast is CGF's, so a CGF brain's
    /// blow lands; a SimHost-created soldier is the known limit (§5i ①).</para>
    /// </summary>
    public static class Melee
    {
        /// <summary>How close a striker must stand (flat, centre to centre), m.</summary>
        public const float ReachMetres = 1.5f;
        /// <summary>How often a man can strike, s.</summary>
        public const float CooldownSeconds = 2f;
        /// <summary>A blow's damage (a fist or a club: bruising, not killing — 20 blows from full health).</summary>
        public const float Damage = 5f;
        /// <summary>How long a blow knocks the victim down, s.</summary>
        public const float KnockdownSeconds = 3f;

        /// <summary>Blows struck on this process (a diagnostics counter and a rail read it).</summary>
        public static int Strikes { get; private set; }

        /// <summary>True when <paramref name="victim"/> is within <see cref="ReachMetres"/> of <paramref name="striker"/>.</summary>
        public static bool InReach(EntityRepository repo, Entity striker, Entity victim)
        {
            if (!repo.IsAlive(striker) || !repo.IsAlive(victim) || !repo.HasComponent<SimTransform>(striker) || !repo.HasComponent<SimTransform>(victim))
                return false;
            var a = repo.GetComponentRO<SimTransform>(striker).Position;
            var b = repo.GetComponentRO<SimTransform>(victim).Position;
            return Vector2.DistanceSquared(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) <= ReachMetres * ReachMetres;
        }

        /// <summary>Strikes <paramref name="victim"/> once when he is in reach: a Blunt <see cref="DamageAssessedEvent"/>. False when out of
        /// reach, gone, or the damage event is not on this node's bus.</summary>
        public static bool Strike(EntityRepository repo, Entity striker, Entity victim, float damage = Damage, float knockdownSeconds = KnockdownSeconds)
        {
            if (!repo.Bus.IsRegistered<DamageAssessedEvent>() || !InReach(repo, striker, victim)) return false;
            repo.Bus.Publish(new DamageAssessedEvent
            {
                HitEntity = victim, TotalDamage = damage, Effect = HitEffect.Blunt, EffectSeconds = knockdownSeconds,
            });
            Strikes++;
            return true;
        }
    }
}
