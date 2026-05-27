using System;
using System.Linq;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T13 (S12). FetchPathDetails populates BrainPathRegistry; cache entry is
    /// invalidated (stale miss) when ReplanCount advances; re-fetching refreshes the cache.
    /// </summary>
    public sealed class S12_FetchPathDetailsAndCacheInvalidationTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S12_FetchPathDetailsAndCacheInvalidationTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadReplan());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S12_FetchPathDetails_PopulatesBrainRegistry()
        {
            const int routeHandle = 1;
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f),
                flags: (byte)(1 << NavigationConstants.FlagBitAllowReplan),
                routeHandle: routeHandle);

            // Wait for path to be found and entity to be following.
            _h.PumpFor(15);

            // Issue FetchPathDetails -- bridge processes it next tick and publishes
            // NavigationPathDetailsResponseEvent, then PathDetailsUpdate ingests it.
            _h.IssueFetchPathDetails(e, routeHandle);
            _h.PumpFor(2); // bridge tick + PathDetailsUpdate tick

            // BrainRegistry should now have a fresh entry for replanCount=0.
            var waypointBuf = new NavWaypoint[256];
            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            bool hit = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)status.ReplanCount, waypointBuf.AsSpan(), out int count);

            Assert.True(hit, "BrainRegistry must have a cache entry after FetchPathDetails.");
            Assert.True(count > 0, "Cached path must contain at least one waypoint.");
        }

        [Fact]
        public void S12_CacheInvalidatedOnReplan_ThenRefreshedOnNextFetch()
        {
            const int routeHandle = 2;
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            byte flags = (byte)(
                (1 << NavigationConstants.FlagBitAllowReplan) |
                (1 << NavigationConstants.FlagBitAutoSendPathOnReplan));
            _h.IssueMoveTo(e, new Vector2(28f, 0f), flags: flags, routeHandle: routeHandle);

            // Let path be found.
            _h.PumpFor(15);

            // Fetch initial path details.
            _h.IssueFetchPathDetails(e, routeHandle);
            _h.PumpFor(2);

            var statusBefore = _h.Repo.GetComponent<NavigationStatus>(e);
            var waypointBuf = new NavWaypoint[256];
            bool firstHit = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)statusBefore.ReplanCount, waypointBuf.AsSpan(), out _);
            Assert.True(firstHit, "Initial FetchPathDetails should populate BrainRegistry.");

            // Force frustration -> replan fires (AutoSendPathOnReplan populates BrainRegistry again).
            _h.NavmeshApi.BlockPolygon(1);
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            // PathReplannedEvent should have fired.
            Assert.True(_h.EventLog.PathReplanned.Count > 0, "PathReplannedEvent must fire.");

            var statusAfter = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.True(statusAfter.ReplanCount > 0, "ReplanCount must be > 0 after replan.");

            // Old cache (replanCount=0) is now stale.
            bool staleMiss = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)statusBefore.ReplanCount, waypointBuf.AsSpan(), out _);
            Assert.False(staleMiss,
                "Old cache entry (stale replanCount) should return false.");

            // BrainRegistry stats should have at least one stale miss.
            var stats = ((IFakeBrainPathRegistryTestApi)_h.BrainRegistry).GetStats();
            Assert.True(stats.StaleMisses > 0, "StaleMisses counter must be > 0.");

            // With AutoSendPathOnReplan, BrainRegistry was auto-refreshed during replan.
            // New cache entry (replanCount=current) should be a hit.
            bool newHit = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)statusAfter.ReplanCount, waypointBuf.AsSpan(), out int newCount);
            Assert.True(newHit, "Auto-refreshed cache entry must be a hit for current replanCount.");
            Assert.True(newCount > 0, "Refreshed cache must contain waypoints.");
        }
    }
}
