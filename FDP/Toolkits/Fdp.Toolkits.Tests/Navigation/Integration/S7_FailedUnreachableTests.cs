using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// Integration tests for the FailedUnreachable navigation outcome.
    /// Uses a single-polygon dead-end map (LoadStuck) to verify that a destination
    /// outside the navmesh produces NavigationResult.FailedUnreachable.
    /// </summary>
    public sealed class S7_FailedUnreachableTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S7_FailedUnreachableTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadStuck());
        }

        public void Dispose() => _h.Dispose();

        /// <summary>
        /// When the destination lies outside all navmesh polygons the solver returns
        /// unreachable, and NavigationStatus.Result should be FailedUnreachable.
        /// No MoveCompletedEvent is published for this case; the test reads status directly.
        /// </summary>
        [Fact]
        public void Stuck_InfantryMovesToDisconnectedDest_ReturnsUnreachable()
        {
            var entity = _h.SpawnInfantry(new Vector2(1f, 1f));
            _h.IssueMoveTo(entity, new Vector2(50f, 50f));

            _h.PumpFor(5);

            var status = _h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.FailedUnreachable, status.Result);
        }
    }
}
