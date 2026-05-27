using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T11 (S10). Naval-layer routing: naval entity on water polygons arrives at destination.
    /// Proves: NavLayerMask.Naval routing through FakeNavmeshProvider works end-to-end.
    /// </summary>
    public sealed class S10_NavalLayerRoutingTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S10_NavalLayerRoutingTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadNaval());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S10_NavalEntity_RoutesOnWaterLayer_AndArrives()
        {
            // LoadNaval: 3 polygons centred at (5,5), (15,5), (25,5) in XZ plane.
            // Harness positions: Vector2(x,y) -> Vector3(x,y,0). PointInPolygon uses X,Z.
            // Vector2(5,5) -> Vector3(5,5,0): PointInPolygon(X=5, Z=0) on polygon 0 (X=0..10, Z=0..10) does not match.
            // Use Vector2(5,0) so Vector3(5,0,0): X=5 in [0..10], Z=0 in [0..10] -> inside polygon 0.
            var e = _h.SpawnNaval(new Vector2(5f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f), layerMask: (uint)NavLayerMask.Naval);

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }

        [Fact]
        public void S10_InfantryOnNavalMap_FailsUnreachable()
        {
            // Infantry layer does not exist on LoadNaval -> FailedUnreachable.
            var e = _h.SpawnInfantry(new Vector2(5f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f), layerMask: (uint)NavLayerMask.Infantry);

            _h.PumpFor(15);

            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.Equal(NavigationResult.FailedUnreachable, status.Result);
        }
    }
}
