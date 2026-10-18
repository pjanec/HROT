using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-3266</c> (G3) — what a blow is struck with (ui draws the swing by it). ⚠ Member names unique (one IDL scope).</summary>
    public enum MeleeWeapon : byte
    {
        MeleeFist = 0,
        MeleeBaton = 1,
        MeleeKnife = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3266</c> (G3, ST-1; docs/DESIGN_Civilians_And_Crowd_Control.md §7.18) — a blow was STRUCK: who, whom, with what. Raised by
    /// <see cref="Melee.Strike"/> beside the damage, and on the wire like <see cref="Events.WeaponFireNotification"/> (topic <c>MeleeStrike</c>)
    /// so every map host draws the swing once. Independent of the damage chain (which reaches the victim's Health owner, <c>CE-3246</c>): a swing that
    /// hurts no one is still drawn.
    /// </summary>
    [EventId(CombatConstants.StrikeNotificationEventId)]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct StrikeNotification
    {
        public Entity Striker;
        public Entity Target;
        public MeleeWeapon Weapon;
        /// <summary>Set by the wire's ingress (it is never sent back out).</summary>
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-2238</c> part 1 (CIV-4, D5; docs/DESIGN_Civilians_And_Crowd_Control.md §5i) — a BLOW: a fist, a club, a baton. Contact damage
    /// with a knock-down, through the ONE damage chain — a <see cref="DamageAssessedEvent"/> {Blunt} on the striker's node's bus, applied
    /// only where the victim's Health is claimed (<c>HealthApplicationSystem</c>), which stores the knock-down in <see cref="EffectState"/>
    /// (replicated). ⛔ No round, no muzzle: the ballistic path is for things that fly.
    /// <para>⭐ <c>CE-3246</c> — a victim whose Health another node claims is hurt THERE: the hit egress (every node) sends a hit this node
    /// does not apply to its owner (docs/designs/brain-split/BS-1-DESIGN.md §6.4a). ⛔ Before, the blow was dropped (§5i ①).</para>
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

        /// <summary>Strikes <paramref name="victim"/> once when he is in reach: a Blunt <see cref="DamageAssessedEvent"/>, and ⭐ <c>CE-3266</c>
        /// a <see cref="StrikeNotification"/> with <paramref name="weapon"/> (when registered). False when out of reach, gone, or the damage
        /// event is not on this node's bus.</summary>
        public static bool Strike(EntityRepository repo, Entity striker, Entity victim, float damage = Damage, float knockdownSeconds = KnockdownSeconds,
                                  MeleeWeapon weapon = MeleeWeapon.MeleeFist)
        {
            if (!repo.Bus.IsRegistered<DamageAssessedEvent>() || !InReach(repo, striker, victim)) return false;
            if (repo.Bus.IsRegistered<StrikeNotification>())
                repo.Bus.Publish(new StrikeNotification { Striker = striker, Target = victim, Weapon = weapon });
            repo.Bus.Publish(new DamageAssessedEvent
            {
                HitEntity = victim, TotalDamage = damage, Effect = HitEffect.Blunt, EffectSeconds = knockdownSeconds,
                Source = striker,   // ⭐ CE-3237 (B-15, DS-1)
            });
            Strikes++;
            return true;
        }
    }
}
