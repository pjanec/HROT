using System.Collections.Generic;
using Fdp.Core;
using Hrot.Network.NED.SimHost;
using Hrot.Network.Routing;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-4 (R-239) — a unit's sensors share one solver: a new pick prefers the node already solving a SIBLING
    /// sensor of the same unit, when that node carries the sensor's role. 📄 docs/DESIGN_Peek_And_Fire.md §7 P-4.
    /// </summary>
    public sealed class EqsSensorSolverPickTests
    {
        private static SimpleClusterStateCache Cache()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(new NodeCapability { NodeId = 2, Role = NodeRole.Perception | NodeRole.NavigationSolver, CpuUsagePercent = 90f });
            cache.UpdateNode(new NodeCapability { NodeId = 3, Role = NodeRole.Perception | NodeRole.MuscleGround, CpuUsagePercent = 5f });
            return cache;
        }

        [Fact]
        public void ANewSensor_JoinsItsSiblingsSolver_EvenWhenAnotherNodeIsLessLoaded()
        {
            var cache = Cache();
            var solverOf = new Dictionary<(long, int), int> { [(7L, 1000)] = 2, [(8L, 1000)] = 3 };

            Assert.Equal(2, EqsSensorConfigEgressTranslator.SiblingSolver(solverOf, (7L, 1001), NodeRole.Perception, cache.AllNodeIds(), cache));
            Assert.Equal(3, cache.GetLeastLoadedNode(NodeRole.Perception));   // what the old pick would have chosen
        }

        [Fact]
        public void NoSibling_OrASiblingSolverWithoutTheRole_OrGone_FallsBackToTheLeastLoaded()
        {
            var cache = Cache();
            var solverOf = new Dictionary<(long, int), int> { [(8L, 1000)] = 3, [(9L, 1000)] = 4 };

            Assert.Null(EqsSensorConfigEgressTranslator.SiblingSolver(solverOf, (7L, 1001), NodeRole.Perception, cache.AllNodeIds(), cache));
            // node 3 has no NavigationSolver ⇒ a danger-area sensor does not join it
            Assert.Null(EqsSensorConfigEgressTranslator.SiblingSolver(solverOf, (8L, 1001),
                NodeRole.Perception | NodeRole.NavigationSolver, cache.AllNodeIds(), cache));
            // node 4 is not in the cache (left the cluster)
            Assert.Null(EqsSensorConfigEgressTranslator.SiblingSolver(solverOf, (9L, 1001), NodeRole.Perception, cache.AllNodeIds(), cache));
        }

        [Fact]
        public void TwoSiblingsOnTwoNodes_TheLowestPartWins_Deterministically()
        {
            var cache = Cache();
            var solverOf = new Dictionary<(long, int), int> { [(7L, 1002)] = 3, [(7L, 1000)] = 2 };
            Assert.Equal(2, EqsSensorConfigEgressTranslator.SiblingSolver(solverOf, (7L, 1003), NodeRole.Perception, cache.AllNodeIds(), cache));
        }
    }
}
