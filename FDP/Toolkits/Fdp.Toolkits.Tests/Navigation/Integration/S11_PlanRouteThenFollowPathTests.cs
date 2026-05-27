using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T12 (S11). PlanRoute then FollowPath:
    /// 1. IssuePlanRoute -> entity stays still; NavigationStatus.Result == PathFound.
    /// 2. IssueFollowPath with the pre-planned route -> entity arrives at destination.
    /// Proves: two-phase navigation (plan, then follow) works correctly.
    /// </summary>
    public sealed class S11_PlanRouteThenFollowPathTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S11_PlanRouteThenFollowPathTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadCorridor());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S11_PlanRoute_ThenFollowPath_Arrives()
        {
            const int routeHandle = 7;
            var start = new Vector2(3f, 0f);
            var dest  = new Vector2(28f, 0f);

            var e = _h.SpawnInfantry(start);
            _h.IssuePlanRoute(e, dest, routeHandle: routeHandle);

            // Pump until PathFound (solver responds + materialise writes PathFound).
            _h.PumpUntil(
                () => _h.Repo.GetComponent<NavigationStatus>(e).Result == NavigationResult.PathFound,
                maxTicks: 30);

            // Entity must NOT have moved.
            var tf = _h.Repo.GetComponent<SimTransform>(e);
            float distMoved = Vector2.Distance(
                new Vector2(tf.Position.X, tf.Position.Y), start);
            Assert.True(distMoved < 0.5f,
                $"Entity should not move during PlanRoute; moved {distMoved:F2} m.");

            // Now follow the pre-planned path.
            _h.IssueFollowPath(e, routeHandle, dest);

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }
    }
}
