using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
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
    }
}
