using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;

namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// Primitives for acquiring and refreshing a reference to the active tactical feature
    /// (danger area / navmesh polygon) that the squad is currently working (design §2 primitive 2,
    /// §5.2).
    /// </summary>
    public static class TacticalFeatureHandles
    {
        /// <summary>
        /// Sets the active feature reference in <paramref name="state"/> to
        /// <paramref name="featureId"/>. Idempotent — calling again with the same id
        /// is a no-op.
        /// </summary>
        public static void Acquire(ref SquadCognitiveState state, uint featureId)
        {
            if (state.ActiveFeatureId != featureId)
                state.ActiveFeatureId = featureId;
        }

        /// <summary>
        /// Searches <paramref name="descriptors"/> for a descriptor whose
        /// <see cref="DangerAreaDescriptor.FeatureId"/> matches <c>state.ActiveFeatureId</c>.
        /// Returns <c>true</c> and writes the match into <paramref name="descriptor"/>
        /// when found; <c>false</c> otherwise.
        /// Does NOT modify <c>state.ActiveFeatureId</c> on failure — the caller decides
        /// whether to abort the maneuver.
        /// </summary>
        public static bool TryRefresh(
            ref SquadCognitiveState state,
            ReadOnlySpan<DangerAreaDescriptor> descriptors,
            out DangerAreaDescriptor descriptor)
        {
            foreach (ref readonly var d in descriptors)
            {
                if (d.FeatureId == state.ActiveFeatureId)
                {
                    descriptor = d;
                    return true;
                }
            }
            descriptor = default;
            return false;
        }
    }
}
