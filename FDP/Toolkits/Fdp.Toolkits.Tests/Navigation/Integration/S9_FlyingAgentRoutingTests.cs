using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T10 (S9). Flying agent with MobilityProfile=4 routes via FakeVolumetricPathProvider.
    /// Proves: bridge passes MobilityProfile=4 from NavAgentProfile, solver invokes volumetric provider.
    /// </summary>
    public sealed class S9_FlyingAgentRoutingTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S9_FlyingAgentRoutingTests()
        {
            // LoadCorridor map: Infantry navmesh, but FakeVolumetricPathProvider ignores navmesh.
            // Default altitude bounds: minAltitude=0, maxAltitude=0 -> any Y=0 position is flyable.
            _h = new NavTestHarness(NavTestMaps.LoadCorridor());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S9_FlyingEntity_RoutesViaVolumetricProvider_AndArrives()
        {
            var e = _h.SpawnFlying(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f));

            // Pump enough for: bridge -> solver (volumetric PlanPath) -> materialize -> crowd -> arrival.
            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            // The volumetric provider must have been called.
            var stats = ((IFakeVolumetricPathProviderTestApi)_h.Volumetric).GetStats();
            Assert.True(stats.PlanPathCalls > 0,
                $"FakeVolumetricPathProvider.PlanPath should have been called at least once; actual={stats.PlanPathCalls}");

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }

        [Fact]
        public void S9_GroundEntity_DoesNotInvokeVolumetricProvider()
        {
            // Control: infantry entity (MobilityProfile=0) must NOT invoke volumetric provider.
            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f));

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            var stats = ((IFakeVolumetricPathProviderTestApi)_h.Volumetric).GetStats();
            Assert.Equal(0, stats.PlanPathCalls);

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }
    }
}
