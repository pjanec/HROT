using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects candidates whose <see cref="EntityInfo.ForceId"/> does not match
    /// the <see cref="EqsSensor.FactionFilter"/> bitmask. Runs in the FilterCheap phase.
    /// Rejection sentinel: EntityId = -1L.
    /// </summary>
    public sealed class FactionFilterTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                // Skip positional candidates (EntityId = 0 means no entity).
                if (candidate.EntityId == 0L) continue;

                var target = new Entity((ulong)candidate.EntityId);

                if (!repo.IsAlive(target) || !repo.HasComponent<EntityInfo>(target))
                {
                    candidate.EntityId = -1L; // Reject dead or missing faction info.
                    continue;
                }

                ref readonly var info = ref repo.GetComponentRO<EntityInfo>(target);

                // ForceId: Neutral=0, Friend=1, Hostile=2.
                // FactionFilter bitmask: bit N set means ForceId N is included.
                uint forceBit = 1u << (int)info.ForceId;
                if ((sensor.FactionFilter & forceBit) == 0)
                {
                    candidate.EntityId = -1L; // Reject faction mismatch.
                }
            }
        }
    }
}
