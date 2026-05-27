using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// S2b -- Corridor-preview opt-in tests on the L-bend map.
    /// Verifies that the NavigationCorridorPreview component is added only when
    /// the FlagBitStreamCorridorPreview flag is set, and that normal arrival
    /// still occurs with the flag active.
    /// </summary>
    public sealed class S2b_LBendCorridorPreviewTests
    {
        private const byte CorridorPreviewFlag = (byte)(1 << NavigationConstants.FlagBitStreamCorridorPreview);

        [Fact]
        public void WithPreviewFlag_CorridorPreviewComponentAdded()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadLBend());

            var entity = h.SpawnInfantry(Vector2.Zero);
            h.IssueMoveTo(entity, new Vector2(28f, 0f), flags: CorridorPreviewFlag);

            // Run enough ticks for pathfinding to complete and corridor to be assigned.
            h.PumpFor(5);

            Assert.True(h.Repo.HasComponent<NavigationCorridorPreview>(entity),
                "NavigationCorridorPreview should be present after corridor preview flag is set.");
        }

        [Fact]
        public void WithoutPreviewFlag_NoCorridorPreviewComponent()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadLBend());

            var entity = h.SpawnInfantry(Vector2.Zero);
            h.IssueMoveTo(entity, new Vector2(28f, 0f));

            h.PumpFor(5);

            Assert.False(h.Repo.HasComponent<NavigationCorridorPreview>(entity),
                "NavigationCorridorPreview should NOT be present when corridor preview flag is not set.");
        }

        [Fact]
        public void WithPreviewFlag_ArrivesNormally()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadLBend());

            var entity = h.SpawnInfantry(Vector2.Zero);
            h.IssueMoveTo(entity, new Vector2(28f, 0f), flags: CorridorPreviewFlag);

            h.PumpUntil(
                () => h.Repo.GetComponent<NavigationStatus>(entity).Result == NavigationResult.Arrived,
                maxTicks: 600);

            var status = h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.Arrived, status.Result);
        }
    }
}
