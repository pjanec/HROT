using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Phase 5 scoring test: accurate LOS via deferred raycast ring buffer.
    ///
    /// <para>For each candidate, checks whether a raycast result is already in
    /// <see cref="RaycastBatchData.Hits"/>. If found, resolves the candidate
    /// immediately (§4.2 flag bit     /// immediately (flag bit 0 = occluded = good cover; EntityId = -1L = exposed).lt;slot    /// immediately (flag bit 0 = occluded = good cover; EntityId = -1L = exposed).gt; = HasLOS; occluded = good cover is kept, exposed is rejected with EntityId = -1L).
    /// If not found, submits a <see cref="RaycastRequestEvent"/> (subject to the
    /// per-tick budget in <see cref="EqsSolverGlobalState"/>) and marks the candidate
    /// with <see cref="FlagPendingRay"/> so the solver knows to yield.</para>
    ///
    /// <para>The threat position is read from the entity in the configured context slot
    /// (default slot 1 = Target). The observer's <see cref="TargetMemory"/> is still
    /// consulted for the threat-score threshold gate, but NOT for the target position.</para>
    /// </summary>
    public sealed class AccurateLineOfSightTest : IEqsTest
    {
        /// <summary>Flag bit 15: raycast submitted but result not yet in ring buffer.</summary>
        public const short FlagPendingRay = unchecked((short)(1 << 15));

        /// <summary>
        /// Index of the sensor context slot whose entity's <see cref="SimTransform"/>
        /// provides the threat position. Default 1 (Target slot by convention).
        /// </summary>
        public byte ContextSlotIndex { get; set; } = 1;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public unsafe void ExecuteBatch(
            Entity observer,
            ref EqsSensor sensor,
            ISimulationView view,
            Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;

            // Step 1-3: Resolve context slot; bypass if null or no SimTransform.
            var slotEntity = GetSlotEntity(ref sensor);
            if (slotEntity.IsNull) return;
            if (!repo.HasComponent<SimTransform>(slotEntity)) return;
            ref readonly var slotTransform = ref repo.GetComponentRO<SimTransform>(slotEntity);

            // Step 4-6: threshold gate on the SELF's TargetMemory when it has one (§19 H4 — a child sensor's carrier never
            //   does, which made this test inert); a self without one is not gated (the slot names the threat explicitly).
            var self = EqsContext.Self(repo, observer, sensor);
            // ⭐ The gate is OPT-IN (ThreatThreshold > 0): a sensor that names its threat in the slot and sets no threshold is
            //   always filtered — an empty memory on the Muscle used to switch the filter off (found by the cross-node rail).
            var memOwner = sensor.ThreatThreshold > 0f ? EqsContext.ThreatMemoryOwner(repo, observer, self) : Entity.Null;
            if (!memOwner.IsNull)
            {
                ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(memOwner);
                if (mem.Count == 0) return;
                if (mem.Freshness[0] < sensor.ThreatThreshold) return;
            }
            short bit = (short)(1 << ContextSlotIndex);
            var mount = EqsTerrainSight.Mount(repo, slotEntity);
            float crouched = EqsTerrainSight.Mount(repo, self).Crouched;

            // Guard: ring buffer not initialized — mark all non-rejected candidates as pending.
            if (!repo.HasSingleton<RaycastBatchData>())
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].EntityId != -1L)
                        candidates[i].Flags = unchecked((short)(candidates[i].Flags | (1 << 15)));
                }
                return;
            }

            // Guard: global budget state not initialized.
            if (!repo.HasSingleton<EqsSolverGlobalState>()) return;

            ref readonly var rayBatch    = ref repo.GetSingleton<RaycastBatchData>();
            ref var          globalState = ref repo.GetSingletonUnmanaged<EqsSolverGlobalState>();

            // Threat position from slot entity's SimTransform (not from TargetMemory).
            // ⭐ §19 — real heights: the threat's eye (its SensorMount) → the self's crouched eye at the candidate.
            //   ⛔ SUPERSEDED: both ends at a fixed Z = 1.5 m, discarding terrain height (CE-210 ③).
            var targetPos3D = slotTransform.Position + new Vector3(0, 0, mount.Standing);
            var cmd         = view.GetCommandBuffer();

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].EntityId == -1L) continue;

                long rayId = ((long)observer.Index << 32) | (uint)i;
                int  slot  = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);

                var hit = rayBatch.Hits[slot];

                if (hit.RayId == rayId)
                {
                    // Result already in ring buffer: resolve now.
                    // Clear FlagPendingRay (bit 15).
                    candidates[i].Flags = unchecked((short)(candidates[i].Flags & ~(1 << 15)));

                    if (hit.HasHit != 0)
                    {
                        // Geometry blocks LOS -> candidate is occluded -> good cover. §4.2: bit <slot> = HasLOS, judged false.
                        candidates[i].FlagsMeaningful |= bit;
                    }
                    else
                    {
                        // Clear LOS -> candidate exposed to threat -> reject.
                        candidates[i].EntityId        = -1L;
                        candidates[i].Flags           |= bit;   // §4.2 HasLOSToContext<slot>
                        candidates[i].FlagsMeaningful |= bit;
                    }
                }
                else
                {
                    // Not yet resolved.
                    if (globalState.AccurateRaysSubmittedThisTick < globalState.MaxAccurateRaycastsPerSolverTick)
                    {
                        cmd.PublishEvent(new RaycastRequestEvent
                        {
                            Start        = new Vector3(candidates[i].PositionX, candidates[i].PositionY,
                                                       candidates[i].PositionZ + crouched),
                            End          = targetPos3D,
                            RayId        = rayId,
                            Observer     = observer,
                            Target       = Entity.Null,
                            LayerMask    = -1,
                            IgnoreEntity = observer,
                            SourceNodeId = 0,
                        });
                        globalState.AccurateRaysSubmittedThisTick++;
                    }

                    // Mark pending regardless (even if budget exhausted).
                    candidates[i].Flags = unchecked((short)(candidates[i].Flags | (1 << 15)));
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
