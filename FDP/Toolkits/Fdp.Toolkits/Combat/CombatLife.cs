using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-466</c> — "is this unit still in the fight?", as the engine defines it.
    /// </summary>
    /// <remarks>
    /// Since the <c>CE-267</c> revert (<c>2026-09-13</c>) combat death is the STATE <c>Health.Current &lt;= 0</c> and the
    /// body stays in the world (<c>DamageSystem</c>, <c>HealthApplicationSystem</c>). ⛔ So <c>IsAlive</c> — ECS
    /// existence — is NOT a combat-death test: a knocked-out tank still exists. This helper is both halves: the entity
    /// exists AND, if it has a <see cref="Health"/> pool, the pool is above zero. An entity with no <see cref="Health"/>
    /// (an area, a marker) is "alive" while it exists.
    /// </remarks>
    public static class CombatLife
    {
        /// <summary>True while <paramref name="entity"/> exists and, when it has <see cref="Health"/>, has HP above zero.</summary>
        public static bool IsAlive(ISimulationView view, Entity entity)
        {
            if (!view.IsAlive(entity)) return false;
            return !view.HasComponent<Health>(entity) || view.GetComponentRO<Health>(entity).Current > 0f;
        }

        /// <summary>
        /// ⭐ <c>CE-3092</c> — what a hit does to a unit's CAPABILITIES, the ONE rule (both damage systems call it).
        /// Death (<c>Current &lt;= 0</c>) strips moving and shooting. A NON-lethal hit strips <c>CanMove</c> only on a unit type
        /// that opted in (<see cref="MobilityKill"/>) and only once health is below its fraction of max; every other unit keeps
        /// moving — so a hurt infantryman can still take cover or fall back. 🔒 User, <c>2026-10-06</c>: "death always stops; a
        /// non-lethal hit stops a unit only when its TKB platform says so". 📄 <c>docs/designs/packs-1/DESIGN.md</c> §2.B.
        /// <para>⚠ Was: every non-lethal hit stripped <c>CanMove</c> on every unit (PACK-M002, written for the UrbanCombat APC) —
        /// measured: one rifle round froze an infantryman for good (CE-3084 U4 red; CE-3094's soldier died where he stood).</para>
        /// </summary>
        public static void ApplyHitCapabilities(EntityRepository repo, Entity entity, in Health health)
        {
            if (!repo.HasComponent<ActorCapabilityState>(entity)) return;
            if (health.Current <= 0f)
            {
                ref var dead = ref repo.GetComponentRW<ActorCapabilityState>(entity);
                dead.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                return;
            }
            if (health.Current >= health.Max) return;
            if (!repo.IsComponentTypeRegistered<MobilityKill>() || !repo.HasComponent<MobilityKill>(entity)) return;
            float below = repo.GetComponentRO<MobilityKill>(entity).BelowFraction;
            if (below <= 0f || health.Current >= health.Max * below) return;
            ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(entity);
            caps.Capabilities &= ~ActorCapabilities.CanMove;
        }
    }
}
