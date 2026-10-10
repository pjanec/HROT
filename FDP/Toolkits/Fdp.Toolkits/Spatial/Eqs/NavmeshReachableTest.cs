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
    public sealed class NavmeshReachableTest : IEqsTest, IEqsCostWeight
    {
        /// <inheritdoc/>
        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Path;   // CE-3037 — path weight (DESIGN_Sensors_And_Doctrine §5.3)

        public EqsTestPhase Phase => EqsTestPhase.FilterExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!EqsContext.SelfPosition(repo, observer, sensor, out var obsPos)) return;   // ⭐ §19 H3
            uint layer = EqsContext.SelfLayer(repo, observer, sensor);                      // ⭐ §19 H5

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            var doors = Fdp.Toolkit.Terrain.DoorStates.Of(repo);   // ⭐ R-219 — this EQS view's doors (its snapshot)
            // INavmeshProvider is Z-up like Sim (R-182 / W7): no swizzle (P3D-205).

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector3(candidate.PositionX, candidate.PositionY, candidate.PositionZ);

                if (!navmesh.PathExists(obsPos, targetPos, layer, doors))
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
