using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects candidates that are not reachable via the navmesh.
    /// Reachable candidates are marked with flag bit 3. Runs in FilterExpensive phase.
    /// Rejection sentinel: EntityId = -1L.
    /// </summary>
    public sealed class NavmeshReachableTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!repo.HasComponent<SimTransform>(observer)) return;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new Vector3(tf.Position.X, 0f, tf.Position.Y);

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector3(candidate.PositionX, 0f, candidate.PositionY);

                // TODO NAV-P0-T5: use NavAgentProfile.PreferredLayerMask from ctx.Self
                if (!navmesh.PathExists(obsPos, targetPos))
                {
                    candidate.EntityId        = -1L; // Reject: unreachable.
                    candidate.FlagsMeaningful |= (short)(1 << 3); // Bit 3 was computed (result = rejection).
                }
                else
                {
                    candidate.Flags         |= (1 << 3); // Bit 3: NavmeshReachable.
                    candidate.FlagsMeaningful |= (short)(1 << 3); // Bit 3 was computed by this test.
                }
            }
        }
    }
}
