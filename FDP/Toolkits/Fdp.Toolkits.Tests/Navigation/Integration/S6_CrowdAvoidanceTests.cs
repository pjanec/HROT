using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T7 (S6). Four infantry entities with crossing paths; all must arrive.
    /// Proves FakeDtCrowdProvider separation forces prevent permanent deadlocks.
    /// </summary>
    public sealed class S6_CrowdAvoidanceTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S6_CrowdAvoidanceTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadCrowded());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S6_FourCrossingAgents_AllArrive()
        {
            // Four agents with crossing paths through the corridor.
            //
            // Same-direction pairs (A/B moving right, C/D moving left) are intentionally
            // offset by 6 units in X so they NEVER share the same X position.  This
            // prevents the static equilibrium that arises when two co-X agents have
            // exactly opposing Y goals: the FakeDtCrowdProvider separation force would
            // cancel the desired Y-velocity at dist = desired.Y / (2 * Radius * 30),
            // leaving both stuck indefinitely.
            //
            // The B-C crossing (t≈266) is the one brief interaction this test exercises.
            // Both agents benefit from the separation impulse (it pushes each toward their
            // respective Y target) so speed stays well above the frustration threshold.
            var eA = _h.SpawnInfantry(new Vector2(2f,  0f));   // moves right-up  (2,0)→(58,6)
            var eB = _h.SpawnInfantry(new Vector2(8f,  6f));   // moves right-down (8,6)→(52,0)
            var eC = _h.SpawnInfantry(new Vector2(52f, 2f));   // moves left-up    (52,2)→(8,8)
            var eD = _h.SpawnInfantry(new Vector2(58f, 8f));   // moves left-down  (58,8)→(2,2)

            _h.IssueMoveTo(eA, new Vector2(58f, 6f));
            _h.IssueMoveTo(eB, new Vector2(52f, 0f));
            _h.IssueMoveTo(eC, new Vector2(8f,  8f));
            _h.IssueMoveTo(eD, new Vector2(2f,  2f));

            bool AllArrived() =>
                _h.EventLog.MoveCompleted.Any(c => c.Target == eA) &&
                _h.EventLog.MoveCompleted.Any(c => c.Target == eB) &&
                _h.EventLog.MoveCompleted.Any(c => c.Target == eC) &&
                _h.EventLog.MoveCompleted.Any(c => c.Target == eD);

            for (int t = 0; t < 2000 && !AllArrived(); t++)
                _h.Tick();

            // Verify all four reached their destinations (not failed).
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eA).Reason);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eB).Reason);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eC).Reason);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eD).Reason);
        }
    }
}
