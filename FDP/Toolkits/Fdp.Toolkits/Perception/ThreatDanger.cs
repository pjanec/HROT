using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐⭐ <c>CE-3054</c> B — how DANGEROUS a contact is, judged at READ time (R-194 / R-201: memory stores identity +
    /// freshness, never danger). The ONE function every threat reader calls. 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.8a.
    /// <para>First cut: armed (the target carries <see cref="WeaponState"/>, which <c>CombatTkbTranslator</c> stamps exactly
    /// when its TKB has a weapon mount — replicas included) = 1, unarmed = <see cref="Unarmed"/>. A target this node does
    /// not hold is UNKNOWN and counts as armed: hidden does not mean harmless.</para>
    /// </summary>
    public static class ThreatDanger
    {
        /// <summary>The danger of an unarmed contact.</summary>
        public const float Unarmed = 0.3f;

        /// <summary>The danger of <paramref name="target"/> to <paramref name="self"/>, in [0, 1].</summary>
        public static float Of(ISimulationView view, Entity self, Entity target)
        {
            if (target.IsNull || !view.IsAlive(target)) return 1f;
            return view.HasComponent<WeaponState>(target) ? 1f : Unarmed;
        }

        /// <summary>The unit's own fighting strength in [0, 1]: its health fraction, scaled down when it is unarmed.</summary>
        public static float OwnStrength(ISimulationView view, Entity self)
        {
            float health = 1f;
            if (view.HasComponent<Health>(self))
            {
                ref readonly var h = ref view.GetComponentRO<Health>(self);
                health = h.Max > 0f ? System.Math.Clamp(h.Current / h.Max, 0f, 1f) : 0f;
            }
            return health * (view.HasComponent<WeaponState>(self) ? 1f : Unarmed);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3054</c> C — how FRESH a remembered contact is. <see cref="TargetMemory.ThreatScores"/> IS the freshness
    /// score (lean A; the backend renames it later): it climbs while tracked and fades while not.
    /// </summary>
    public static class ThreatFreshness
    {
        /// <summary>A memory score as a freshness in [0, 1] (<see cref="PerceptionConstants.FreshnessSaturation"/> = 1).</summary>
        public static float Of(float score)
            => System.Math.Clamp(score / PerceptionConstants.FreshnessSaturation, 0f, 1f);

        /// <summary>
        /// True when slot <paramref name="i"/> of <paramref name="self"/>'s memory is LIVE: a sensor tracks it now, or its
        /// freshness is at least <see cref="PerceptionConstants.LiveFreshness"/>.
        /// </summary>
        public static unsafe bool IsLive(ISimulationView view, Entity self, in TargetMemory mem, int i)
        {
            if (Of(mem.ThreatScores[i]) >= PerceptionConstants.LiveFreshness) return true;
            if (!view.HasComponent<ActiveSensorTracks>(self)) return false;
            ref readonly var tracks = ref view.GetComponentRO<ActiveSensorTracks>(self);
            long id = mem.EntityIds[i];
            for (int t = 0; t < tracks.Count; t++)
                if (tracks.EntityIds[t] == id) return true;
            return false;
        }
    }
}
