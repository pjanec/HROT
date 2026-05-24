using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects cover candidates that are exposed to the primary threat in TargetMemory.
    /// "Exposed" = HasCheapLineOfSight returns true (clear LOS from candidate to threat).
    /// "Covered" = returns false (LOS blocked); flag bit 0 is set.
    ///
    /// Bypass conditions:
    ///   - TargetMemory.Count == 0 (no threats tracked)
    ///   - ThreatScores[0] &lt; sensor.ThreatThreshold (threat not significant enough)
    ///
    /// Rejection sentinel: EntityId = -1L (NOT 0 -- positional candidates use 0).
    /// </summary>
    public sealed class CheapLineOfSightTest : IEqsTest
    {
        private readonly ILosService _los;

        public CheapLineOfSightTest(ILosService los)
        {
            _los = los;
        }

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public unsafe void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;

            // Bypass: no TargetMemory on observer.
            if (!repo.HasComponent<TargetMemory>(observer)) return;
            ref readonly var memRO = ref repo.GetComponentRO<TargetMemory>(observer);

            // Bypass: no threats tracked.
            if (memRO.Count == 0) return;

            // Bypass: primary threat score is below threshold (not significant).
            if (memRO.ThreatScores[0] < sensor.ThreatThreshold) return;

            // Primary threat position.
            var threatPos = new Vector2(memRO.PositionsX[0], memRO.PositionsY[0]);

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var candidatePos = new Vector2(candidate.PositionX, candidate.PositionY);

                // HasCheapLineOfSight: true = clear (exposed) = reject.
                //                      false = blocked (cover valid) = keep + set flag bit 0.
                if (_los.HasCheapLineOfSight(candidatePos, threatPos))
                {
                    candidate.EntityId = -1L; // Exposed: reject.
                }
                else
                {
                    candidate.Flags |= 1; // Covered: set flag bit 0.
                }
            }
        }
    }
}
