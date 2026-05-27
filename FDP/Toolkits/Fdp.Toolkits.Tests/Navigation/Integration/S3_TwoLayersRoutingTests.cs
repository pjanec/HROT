using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// S3 -- Two-layer navmesh routing tests.
    /// Verifies that layer masks are respected: infantry layer reaches the destination,
    /// while a vehicle using the vehicle mask cannot reach infantry-only polygons.
    /// </summary>
    public sealed class S3_TwoLayersRoutingTests
    {
        [Fact]
        public void TwoLayers_InfantryMask_Arrives()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadTwoLayers());

            var entity = h.SpawnInfantry(Vector2.Zero);
            h.IssueMoveTo(entity, new Vector2(28f, 0f), layerMask: (uint)NavLayerMask.Infantry);

            h.PumpUntil(
                () => h.Repo.GetComponent<NavigationStatus>(entity).Result == NavigationResult.Arrived,
                maxTicks: 600);

            var status = h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.Arrived, status.Result);
        }

        [Fact]
        public void TwoLayers_VehicleMaskForInfantryArea_Unreachable()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadTwoLayers());

            // SpawnVehicle adds VehicleState which prevents crowd registration.
            // Vehicle layer polygons are at Z=20..30; the start/end at Z=0 are not
            // on any vehicle polygon, so the path will be FailedUnreachable.
            var entity = h.SpawnVehicle(Vector2.Zero);
            h.IssueMoveTo(entity, new Vector2(28f, 0f), layerMask: (uint)NavLayerMask.Vehicle);

            h.PumpFor(10);

            var status = h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.FailedUnreachable, status.Result);
        }
    }
}
