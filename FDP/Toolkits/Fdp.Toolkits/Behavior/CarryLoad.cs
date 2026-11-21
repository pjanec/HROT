using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-2152</c> C-a (R-2011 C8, docs/DESIGN_Entity_Interactions.md §7.4.8) — how much a casualty slows its PERSON carrier: the
    /// type's carry or drag factor for the mode the casualty rides in, 1 for anyone carrying nobody. Multiplied into a person's top speed by
    /// <c>CarKinematicsSystem</c> beside the stance factor, on a local copy (nothing written back).
    /// </summary>
    public static class CarryLoad
    {
        /// <summary>The top-speed factor for <paramref name="carrier"/>, in (0, 1].</summary>
        public static float Factor(EntityRepository repo, Entity carrier)
        {
            if (!repo.IsComponentTypeRegistered<PersonCarrier>() || !repo.HasComponent<PersonCarrier>(carrier)
                || !repo.HasComponent<PassengerBuffer>(carrier)) return 1f;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(carrier);
            if (buffer.Count == 0) return 1f;
            ref readonly var pc = ref repo.GetComponentRO<PersonCarrier>(carrier);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var load = buffer.Passengers[i];
                if (load.IsNull) continue;
                // ⭐ T-a (R-2017 T3) — a carried WEAPON slows him by its own CarrySpeedFactor (a two-man weapon's is the pair's); a stretcher
                //   by the type's StretcherSpeedFactor. ⚠ WS-2 used the casualty factor for a carried weapon (a LAW halved his speed).
                if (Embarkation.IsWeaponSystem(repo, load))
                {
                    float f = repo.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemParams>(load).CarrySpeedFactor;
                    return f > 0f ? f : 1f;
                }
                return buffer.ModeOf(i) switch
                {
                    CarryMode.Dragged   => pc.DragSpeedFactor,
                    CarryMode.Stretcher => pc.StretcherSpeedFactor > 0f ? pc.StretcherSpeedFactor : pc.CarrySpeedFactor,
                    _                   => pc.CarrySpeedFactor,
                };
            }
            return 1f;
        }
    }
}
