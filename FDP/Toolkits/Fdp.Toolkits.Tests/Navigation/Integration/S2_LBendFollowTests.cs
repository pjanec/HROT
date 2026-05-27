using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// S2 -- Multi-segment L-bend path following.
    /// Verifies that an infantry entity can navigate all the way through a 4-polygon
    /// L-bend map and arrive at the destination.
    /// </summary>
    public sealed class S2_LBendFollowTests
    {
        [Fact]
        public void LBend_InfantryFollowsMultiSegmentPath_Arrives()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadLBend());

            var entity = h.SpawnInfantry(Vector2.Zero);
            h.IssueMoveTo(entity, new Vector2(28f, 0f));

            h.PumpUntil(
                () => h.Repo.GetComponent<NavigationStatus>(entity).Result == NavigationResult.Arrived,
                maxTicks: 600);

            var status = h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.Arrived, status.Result);
        }
    }
}
