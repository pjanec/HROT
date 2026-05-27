using System;
using System.Linq;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T6 (S5b). Auto-refresh side of the replan scenario.
    /// With <c>FlagBitAutoSendPathOnReplan</c> set, a Muscle-internal replan additionally
    /// fires <see cref="NavigationPathDetailsResponseEvent"/> with <c>IsAutoRefresh = 1</c>.
    /// Without the flag (sibling control), no such event fires.
    /// </summary>
    public sealed class S5b_ReplanWithAutoRefreshTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S5b_ReplanWithAutoRefreshTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadReplan());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S5b_AutoSendPathOnReplan_FiresPathDetailsResponseEvent_IsAutoRefresh()
        {
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));

            // AllowReplan + AutoSendPathOnReplan flags.
            byte flags = (byte)(
                (1 << NavigationConstants.FlagBitAllowReplan) |
                (1 << NavigationConstants.FlagBitAutoSendPathOnReplan));

            _h.IssueMoveTo(e, new Vector2(28f, 0f), flags: flags, routeHandle: 1);

            _h.PumpFor(15);

            _h.NavmeshApi.BlockPolygon(1);
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            // NavigationPathDetailsResponseEvent with IsAutoRefresh=1 must fire.
            Assert.True(_h.EventLog.PathDetailsResponses.Count > 0,
                "NavigationPathDetailsResponseEvent must fire when AutoSendPathOnReplan is set.");

            var resp = _h.EventLog.PathDetailsResponses[0];
            Assert.Equal(1, resp.IsAutoRefresh);
            Assert.Equal(1, resp.RouteHandle);

            // PathReplannedEvent must also fire.
            Assert.True(_h.EventLog.PathReplanned.Count > 0, "PathReplannedEvent must fire.");

            // Entity arrives after velocity is restored.
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).ClearAgentVelocityOverride(e);
            _h.PumpUntil(() => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 1000);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }

        [Fact]
        public void S5b_WithoutAutoSendFlag_NoPathDetailsResponseFired()
        {
            // Sibling control: same setup but WITHOUT AutoSendPathOnReplan flag.
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));

            // AllowReplan only -- no AutoSendPathOnReplan.
            byte flags = (byte)(1 << NavigationConstants.FlagBitAllowReplan);
            _h.IssueMoveTo(e, new Vector2(28f, 0f), flags: flags, routeHandle: 2);

            _h.PumpFor(15);
            _h.NavmeshApi.BlockPolygon(1);
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            // Replan should have fired...
            Assert.True(_h.EventLog.PathReplanned.Count > 0, "PathReplannedEvent must fire.");

            // ...but NO PathDetailsResponseEvent (flag not set).
            Assert.Equal(0, _h.EventLog.PathDetailsResponses.Count);
        }
    }
}
