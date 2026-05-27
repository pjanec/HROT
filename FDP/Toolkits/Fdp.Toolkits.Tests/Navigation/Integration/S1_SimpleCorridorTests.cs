using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// Integration tests for the basic straight-corridor navigation scenario.
    /// Uses a three-polygon corridor (LoadCorridor) to verify that an infantry
    /// entity can pathfind across the navmesh and arrive at the far end.
    /// </summary>
    public sealed class S1_SimpleCorridorTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S1_SimpleCorridorTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadCorridor());
        }

        public void Dispose() => _h.Dispose();

        /// <summary>
        /// An infantry entity starting at the near end of the corridor should
        /// navigate to the far end and produce a MoveCompletedEvent with Arrived.
        /// </summary>
        [Fact]
        public void Corridor_InfantryMovesToFarEnd_Arrives()
        {
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f));

            _h.PumpUntil(() => _h.EventLog.HasMoveCompleted(entity));

            var evt = _h.EventLog.GetMoveCompleted(entity);
            Assert.Equal(NavigationResult.Arrived, evt.Reason);
        }
    }
}
