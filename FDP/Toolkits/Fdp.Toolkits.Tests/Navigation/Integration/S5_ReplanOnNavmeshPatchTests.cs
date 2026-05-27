using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T6 (S5). Muscle-internal replan triggered by frustration after a navmesh patch.
    /// Proves: PathReplannedEvent fires, ReplanCount > 0, entity arrives via alternate route.
    /// </summary>
    public sealed class S5_ReplanOnNavmeshPatchTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S5_ReplanOnNavmeshPatchTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadReplan());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S5_AgentReroutes_ViaAlternate_AndArrives()
        {
            // LoadReplan pre-blocks polygon 1. Unblock so initial path goes 0->1->2.
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f),
                flags: (byte)(1 << NavigationConstants.FlagBitAllowReplan));

            // Pump for 15 ticks: bridge -> solver -> materialize (path resolves).
            _h.PumpFor(15);

            // Block polygon 1 to cut the main route.
            _h.NavmeshApi.BlockPolygon(1);

            // Override crowd velocity to zero so frustration accumulates.
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);

            // Pump FrustrationTickLimit + 5 ticks -> PathReplannedEvent fires.
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            Assert.True(_h.EventLog.PathReplanned.Count > 0,
                "PathReplannedEvent must fire after FrustrationTickLimit ticks with AllowReplan set.");

            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.True(status.ReplanCount > 0, "NavigationStatus.ReplanCount must be > 0 after replan.");

            // Restore velocity -- crowd drives entity to destination.
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).ClearAgentVelocityOverride(e);

            // Pump until arrival (alternate path 0->3->2 takes a few extra ticks).
            _h.PumpUntil(() => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 1000);

            var completed = _h.EventLog.MoveCompleted.First(c => c.Target == e);
            Assert.Equal(NavigationResult.Arrived, completed.Reason);

            var tf = _h.Repo.GetComponent<SimTransform>(e);
            float dist = Vector2.Distance(
                new Vector2(tf.Position.X, tf.Position.Y),
                new Vector2(28f, 0f));
            Assert.True(dist <= 2.0f,
                $"Final position should be within 2 m of destination; actual dist={dist:F2}");

            // NavigationStatus must NOT have reached FailedBlocked.
            Assert.NotEqual(NavigationResult.FailedBlocked, status.Result);
        }
    }
}
