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
    /// NAV-P10-T9 (S8). Frustration watchdog: agents stuck at zero velocity exhaust their
    /// replan budget and surface <see cref="NavigationResult.FailedBlocked"/> to Brain.
    /// </summary>
    public sealed class S8_FrustrationWatchdogTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S8_FrustrationWatchdogTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadFrustration());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S8_AgentsStuck_FailedBlocked_After_ReplanBudgetExhausted()
        {
            var dest  = new Vector2(35f, 0f); // polygon 3 centre
            byte flags = (byte)(1 << NavigationConstants.FlagBitAllowReplan);

            var e1 = _h.SpawnInfantry(new Vector2(5f, 0f));
            var e2 = _h.SpawnInfantry(new Vector2(5f, 0f));
            var e3 = _h.SpawnInfantry(new Vector2(5f, 0f));

            _h.IssueMoveTo(e1, dest, flags: flags);
            _h.IssueMoveTo(e2, dest, flags: flags);
            _h.IssueMoveTo(e3, dest, flags: flags);

            // Set MaxReplans = 1 so the budget exhausts after 2 x FrustrationTickLimit ticks.
            ref var intent1 = ref _h.Repo.GetComponentRW<NavigationIntent>(e1);
            intent1.MaxReplans = 1;
            ref var intent2 = ref _h.Repo.GetComponentRW<NavigationIntent>(e2);
            intent2.MaxReplans = 1;
            ref var intent3 = ref _h.Repo.GetComponentRW<NavigationIntent>(e3);
            intent3.MaxReplans = 1;

            // Advance one tick so the bridge registers all three agents in the crowd
            // before applying the velocity override (OverrideAgentVelocity is a no-op
            // until RegisterAgent has been called for the entity).
            _h.PumpFor(1);

            // Zero velocity -> frustration accumulates deterministically.
            var crowdApi = (IFakeDtCrowdProviderTestApi)_h.Crowd;
            crowdApi.OverrideAgentVelocity(e1, Vector3.Zero);
            crowdApi.OverrideAgentVelocity(e2, Vector3.Zero);
            crowdApi.OverrideAgentVelocity(e3, Vector3.Zero);

            // Wait until at least one agent surfaces FailedBlocked.
            // With FrustrationTickLimit=120 and MaxReplans=1: fails at ~242 ticks.
            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Reason == NavigationResult.FailedBlocked),
                maxTicks: 400);

            // MoveBlockedEvent must have fired at least once (throttled per episode).
            Assert.True(_h.EventLog.MoveBlocked.Count > 0,
                "MoveBlockedEvent must fire when the Muscle-internal replan is triggered.");

            // At least one FailedBlocked.
            Assert.Contains(_h.EventLog.MoveCompleted,
                c => c.Reason == NavigationResult.FailedBlocked);

            // The failing entity's ReplanCount should be >= 1 (it tried replan first).
            var failedTarget = _h.EventLog.MoveCompleted
                .First(c => c.Reason == NavigationResult.FailedBlocked).Target;
            var failedStatus = _h.Repo.GetComponent<NavigationStatus>(failedTarget);
            Assert.True(failedStatus.ReplanCount >= 1,
                "Muscle must have attempted at least one replan before hard-failing.");
        }

        [Fact]
        public void S8_WithoutAllowReplan_FailedBlocked_Immediately_AfterOneFrustrationEpisode()
        {
            var dest = new Vector2(35f, 0f);
            // No AllowReplan flag -> first frustration episode -> FailedBlocked (no replan).
            var e = _h.SpawnInfantry(new Vector2(5f, 0f));
            _h.IssueMoveTo(e, dest, flags: 0);

            // One tick so the bridge registers the agent before the velocity override.
            _h.PumpFor(1);

            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: NavigationExecutionSystem.FrustrationTickLimit + 10);

            Assert.Equal(NavigationResult.FailedBlocked,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);

            // Without AllowReplan, NO MoveBlockedEvent fires (that's only for replan path).
            Assert.Equal(0, _h.EventLog.MoveBlocked.Count);

            // ReplanCount must remain 0.
            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.Equal(0, status.ReplanCount);
        }

        /// <summary>
        /// ⭐ <c>CE-2304</c> — a walker on a path who keeps MOVING but gets NOWHERE (seven men pressed in two small rooms: 0.4 m/s,
        /// his progress flat) ends his move <see cref="NavigationResult.FailedBlocked"/>, so his brain re-plans. The speed watchdog
        /// never fires above its 0.2 m/s; the no-progress one does. A walker who gains ground, however slowly, is left alone.
        /// ✅ Red-proof: without the no-progress count the stalled walker stays <see cref="NavigationResult.InProgress"/>.
        /// </summary>
        [Theory]
        [InlineData(0f, true)]
        [InlineData(0.3f, false)]
        public void CE2304_AWalkerWhoMovesButMakesNoProgress_EndsHisMove_FailedBlocked(float progressPerSecond, bool fails)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<global::CarKinem.Core.FrustrationTicks>();
            repo.RegisterComponent<global::CarKinem.Core.NavState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();

            var e = repo.CreateEntity();
            repo.AddComponent(e, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 1, FinalDestination = new Vector3(50f, 0f, 0f), ArrivalRadius = 0.5f });
            repo.AddComponent(e, new NavigationStatus());
            repo.AddComponent(e, new global::CarKinem.Core.FrustrationTicks());
            repo.AddComponent(e, new global::CarKinem.Core.NavState { Mode = global::CarKinem.Core.KinematicsMode.CustomTrajectory, TargetSpeed = 2f, ArrivalRadius = 0.5f });
            repo.AddComponent(e, new SimTransform());
            repo.AddComponent(e, new SimVelocity { Linear = new Vector3(0.4f, 0f, 0f) });   // jostled: moving, above the 0.2 m/s floor

            var watchdog = new NavigationExecutionSystem();
            const float dt = 1f / 60f;
            for (int i = 0; i < 60 * 8; i++)
            {
                ref var nav = ref repo.GetComponentRW<global::CarKinem.Core.NavState>(e);
                nav.ProgressS += progressPerSecond * dt;
                watchdog.Execute(repo, dt);
            }

            var result = repo.GetComponent<NavigationStatus>(e).Result;
            Assert.Equal(fails ? NavigationResult.FailedBlocked : NavigationResult.InProgress, result);
        }

        /// <summary>
        /// ⭐ <c>CE-2304</c> (N1) — a move RE-SENT to the same point (a new <c>IntentId</c> every half second, as the posted men's brain
        /// did live) is the same move: it keeps its stuck count, so the stalled walker still ends <see cref="NavigationResult.FailedBlocked"/>
        /// (a re-send after that failure starts a new move), and the status answers the newest id. A re-send to a DIFFERENT point is a new move and starts over.
        /// ✅ Red-proof: without the re-send test in the watchdog every re-send resets the count ⇒ InProgress for good.
        /// </summary>
        [Theory]
        [InlineData(0f, true)]
        [InlineData(3f, false)]
        public void CE2304_AMoveReSentToTheSamePoint_KeepsItsStuckCount(float pointShiftPerResend, bool fails)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<global::CarKinem.Core.FrustrationTicks>();
            repo.RegisterComponent<global::CarKinem.Core.NavState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();

            var dest = new Vector3(50f, 0f, 0f);
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NavigationIntent { Mode = NavigationMode.PathToPoint, IntentId = 1, FinalDestination = dest, ArrivalRadius = 0.5f });
            repo.AddComponent(e, new NavigationStatus());
            repo.AddComponent(e, new global::CarKinem.Core.FrustrationTicks());
            repo.AddComponent(e, new global::CarKinem.Core.NavState
                { Mode = global::CarKinem.Core.KinematicsMode.CustomTrajectory, TargetSpeed = 2f, ArrivalRadius = 0.5f, FinalDestination = dest });
            repo.AddComponent(e, new SimTransform());
            repo.AddComponent(e, new SimVelocity { Linear = new Vector3(0.4f, 0f, 0f) });

            var watchdog = new NavigationExecutionSystem();
            const float dt = 1f / 60f;
            uint id = 1;
            bool failed = false;   // a re-send AFTER the failure is a new move (as the brain's re-plan would be) — so record the failure itself
            for (int i = 0; i < 60 * 8; i++)
            {
                if (i > 0 && i % 30 == 0)   // the brain re-sends the move every half second
                {
                    ref var intent = ref repo.GetComponentRW<NavigationIntent>(e);
                    intent.IntentId = ++id;
                    intent.FinalDestination += new Vector3(0f, pointShiftPerResend, 0f);   // the path still leads to the old point
                }
                watchdog.Execute(repo, dt);
                failed |= repo.GetComponent<NavigationStatus>(e).Result == NavigationResult.FailedBlocked;
            }

            Assert.Equal(fails, failed);
            if (!fails) Assert.Equal(id, repo.GetComponent<NavigationStatus>(e).IntentId);
        }
    }
}
