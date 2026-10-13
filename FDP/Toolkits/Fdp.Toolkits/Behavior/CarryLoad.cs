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
                if (buffer.Passengers[i].IsNull) continue;
                return buffer.ModeOf(i) == CarryMode.Dragged ? pc.DragSpeedFactor : pc.CarrySpeedFactor;
            }
            return 1f;
        }
    }
}
