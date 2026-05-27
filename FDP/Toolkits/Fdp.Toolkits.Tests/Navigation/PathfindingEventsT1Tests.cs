using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// T1 -- Verifies that <see cref="PathfindingRequestEvent"/> and
    /// <see cref="PathfindingResultEvent"/> carry the new fields added in navigation
    /// subsystem v2 Phase 1 slice-1 (enums + event extensions).
    /// </summary>
    public class PathfindingEventsT1Tests
    {
        [Fact]
        public void PathfindingRequestEvent_NewFields_AreZeroDefaulted()
        {
            var evt = new PathfindingRequestEvent();
            Assert.Equal(0, evt.RouteHandle);
            Assert.Equal(0, evt.NavLayerMask);
            Assert.Equal(NavigationBackend.Auto, evt.BackendForce);
            Assert.Equal(0f, evt.MaxCost);
            Assert.Equal(0, evt.NavmeshVersionAtRequest);
        }

        [Fact]
        public void PathfindingResultEvent_NewFields_AreZeroDefaulted()
        {
            var evt = new PathfindingResultEvent();
            Assert.Equal(0, evt.NavmeshVersionAtPlan);
            Assert.Equal(NavigationFailureReason.NoFailure, evt.FailureReason);
            Assert.Equal(NavigationBackend.Auto, evt.PrimaryBackend);
        }

        [Fact]
        public void NavigationBackend_HasExpectedValues()
        {
            Assert.Equal(0, (int)NavigationBackend.Auto);
            Assert.Equal(1, (int)NavigationBackend.NavRoadGraph);
            Assert.Equal(2, (int)NavigationBackend.Navmesh);
            Assert.Equal(3, (int)NavigationBackend.Hybrid);
            Assert.Equal(4, (int)NavigationBackend.Volumetric);
        }

        [Fact]
        public void NavigationFailureReason_HasExpectedValues()
        {
            Assert.Equal(0, (int)NavigationFailureReason.NoFailure);
            Assert.Equal(1, (int)NavigationFailureReason.Unreachable);
            Assert.Equal(2, (int)NavigationFailureReason.Timeout);
            Assert.Equal(3, (int)NavigationFailureReason.InvalidHandle);
            Assert.Equal(4, (int)NavigationFailureReason.ProviderError);
        }
    }
}
