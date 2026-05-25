using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects cover candidates that are exposed to the entity in <see cref="ContextSlotIndex"/>
    /// (default slot 1 = Target). "Exposed" = HasCheapLineOfSight returns true (clear LOS from
    /// candidate to threat). "Covered" = returns false (LOS blocked); flag bit 0 is set.
    ///
    /// Bypass conditions (in order):
    ///   - The configured context slot entity is <see cref="Entity.Null"/> (no threat configured).
    ///   - The slot entity has no <see cref="SimTransform"/> (slot entity not yet ready).
    ///   - Observer has no <see cref="TargetMemory"/> (threshold gate not applicable).
    ///   - TargetMemory.Count == 0 (no threats tracked).
    ///   - ThreatScores[0] &lt; sensor.ThreatThreshold (threat not significant enough).
    ///
    /// Rejection sentinel: EntityId = -1L (NOT 0 -- positional candidates use 0).
    /// FlagsMeaningful bit 0 is set on BOTH exposed (rejected) and covered candidates.
    /// </summary>
    public sealed class CheapLineOfSightTest : IEqsTest
    {
        private readonly ILosService _los;

        /// <summary>
        /// Index of the sensor context slot whose entity's <see cref="SimTransform"/>
        /// provides the threat position. Default 1 (Target slot by convention).
        /// </summary>
        public byte ContextSlotIndex { get; set; } = 1;

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

            // Step 1-2: Resolve context slot; bypass if null (no threat configured).
            var slotEntity = GetSlotEntity(ref sensor);
            if (slotEntity.IsNull) return;

            // Step 3: Lookup SimTransform on the slot entity; bypass if not present.
            if (!repo.HasComponent<SimTransform>(slotEntity)) return;
            ref readonly var slotTransform = ref repo.GetComponentRO<SimTransform>(slotEntity);

            // Step 4: Bypass if observer has no TargetMemory.
            if (!repo.HasComponent<TargetMemory>(observer)) return;
            ref readonly var memRO = ref repo.GetComponentRO<TargetMemory>(observer);

            // Step 5: Bypass if no threats tracked.
            if (memRO.Count == 0) return;

            // Step 6: Bypass if primary threat score is below threshold (not significant).
            if (memRO.ThreatScores[0] < sensor.ThreatThreshold) return;

            // Step 7: Threat position from slot entity's SimTransform.
            var threatPos = new Vector2(slotTransform.Position.X, slotTransform.Position.Y);

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
                    candidate.EntityId        = -1L; // Exposed: reject.
                    candidate.FlagsMeaningful |= 1;  // Bit 0 was computed (result = rejection).
                }
                else
                {
                    candidate.Flags           |= 1; // Covered: set flag bit 0.
                    candidate.FlagsMeaningful |= 1; // Bit 0 was computed by this test.
                }
            }
        }

        private Entity GetSlotEntity(ref EqsSensor sensor)
        {
            return ContextSlotIndex switch
            {
                0 => sensor.ContextSlot0,
                2 => sensor.ContextSlot2,
                _ => sensor.ContextSlot1,
            };
        }
    }
}
