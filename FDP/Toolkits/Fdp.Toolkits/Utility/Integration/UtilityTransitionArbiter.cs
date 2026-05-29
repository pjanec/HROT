using System;
using Fdp.Core;

namespace Fdp.Toolkit.Utility.Integration
{
    /// <summary>
    /// HSM guard: returns true when the entity's utility result buffer indicates that
    /// <paramref name="optionId"/> is the winning posture option.
    ///
    /// Note: This class provides utility-based transition logic but does NOT use the [HsmGuard]
    /// attribute because FastHSM guards expect (void*, void*, ushort) signatures,
    /// whereas this method operates on Entity/Component data.
    /// For FSM integration, call this method from a custom transition evaluator.
    /// </summary>
    public static class UtilityTransitionArbiter
    {
        /// <summary>
        /// Returns true iff the entity's <see cref="UtilityResultBuffer"/> top entry's
        /// <see cref="UtilityResultEntry.WinningPostureId"/> equals <paramref name="optionId"/>.
        /// Returns false if the entity has no result buffer or buffer is empty.
        /// </summary>
        public static bool Evaluate(EntityRepository repo, Entity entity, byte optionId)
        {
            if (!repo.HasComponent<UtilityResultBuffer>(entity)) return false;
            ref readonly var buf = ref repo.GetComponentRO<UtilityResultBuffer>(entity);
            if (buf.Count == 0) return false;
            return buf.Top().WinningPostureId == optionId;
        }
    }
}
