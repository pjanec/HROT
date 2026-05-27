using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for the Phase 5 replan flow in <see cref="NavigationExecutionSystem"/>
    /// (NAV-P9-T4). Validates event emission: MoveStartedEvent, MoveCompletedEvent,
    /// PathReplannedEvent, MoveBlockedEvent, NavigationPathDetailsResponseEvent,
    /// and correct hard-failure when the replan budget is exhausted.
    /// </summary>
    public class NavigationProgressTrackerSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an entity that is actively moving (velocity = 5 m/s on X).
        /// <c>NavigationStatus.IntentId</c> defaults to 0 while <c>NavigationIntent.IntentId</c>
        /// is 1, causing a new-intent mismatch on the very first tick.
        /// </summary>
        private static Entity CreateMovingEntity(
            EntityRepository repo,
            NavigationMode mode = NavigationMode.DirectPoint,
            byte intentFlags = 0,
            byte maxReplans  = 0)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(5f, 0f, 0f) });
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = mode,
                IntentId         = 1,
                FinalDestination = new Vector2(100f, 0f),
                ArrivalRadius    = 1f,
                Flags            = intentFlags,
                MaxReplans       = maxReplans,
            });
            repo.AddComponent(entity, new NavigationStatus());
            repo.AddComponent(entity, new FrustrationTicks());
            repo.AddComponent(entity, new NavState());
            return entity;
        }

        /// <summary>
        /// Creates an entity that is stuck (velocity = 0).
        /// Same IntentId mismatch as <see cref="CreateMovingEntity"/>.
        /// </summary>
        private static Entity CreateStuckEntity(
            EntityRepository repo,
            byte intentFlags = 0,
            byte maxReplans  = 0)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            repo.AddComponent(entity, new SimVelocity { Linear = Vector3.Zero });
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavigationMode.DirectPoint,
                IntentId         = 1,
                FinalDestination = new Vector2(100f, 0f),
                ArrivalRadius    = 1f,
                Flags            = intentFlags,
                MaxReplans       = maxReplans,
            });
            repo.AddComponent(entity, new NavigationStatus());
            repo.AddComponent(entity, new FrustrationTicks());
            repo.AddComponent(entity, new NavState());
            return entity;
        }

        // ── Test 1: MoveStartedEvent fires once on first tick ────────────────────────────────────

        [Fact]
        public void FirstTickOfMove_EmitsMoveStartedEvent()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            CreateMovingEntity(repo);
            sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var events = view.ReadEvents<MoveStartedEvent>();
            Assert.Equal(1, events.Length);
        }

        // ── Test 2: MoveStartedEvent does not fire again on subsequent ticks ─────────────────────

        [Fact]
        public void FirstTickOfMove_MoveStartedEvent_NotFiredOnSubsequentTicks()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            CreateMovingEntity(repo);

            // First tick — fires the event.
            sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();
            view.ReadEvents<MoveStartedEvent>(); // drain

            // Second tick — must NOT fire again.
            sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();
            var events = view.ReadEvents<MoveStartedEvent>();
            Assert.Equal(0, events.Length);
        }

        // ── Test 3: MoveCompletedEvent fires with Arrived ────────────────────────────────────────

        [Fact]
        public void Arrived_EmitsMoveCompletedEventWithArrived()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(5f, 0f, 0f) });
            // Destination within arrival radius on first tick.
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavigationMode.DirectPoint,
                IntentId         = 1,
                FinalDestination = new Vector2(0.1f, 0f),  // 0.1 m away
                ArrivalRadius    = 1f,                      // radius 1 m > 0.1 m
                Flags            = 0,
            });
            repo.AddComponent(entity, new NavigationStatus());
            repo.AddComponent(entity, new FrustrationTicks());
            repo.AddComponent(entity, new NavState { HasArrived = 1 });

            sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var events = view.ReadEvents<MoveCompletedEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(NavigationResult.Arrived, events[0].Reason);
        }

        // ── Test 4: Hard failure without replan ──────────────────────────────────────────────────

        [Fact]
        public void FailedBlocked_WithoutReplan_WritesMoveCompletedFailedBlocked()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            CreateStuckEntity(repo, intentFlags: 0 /* AllowReplan off */);

            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
                sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var completed = view.ReadEvents<MoveCompletedEvent>();
            Assert.Equal(1, completed.Length);
            Assert.Equal(NavigationResult.FailedBlocked, completed[0].Reason);
        }

        // ── Test 5: MoveBlockedEvent is throttled to once per episode ────────────────────────────

        [Fact]
        public void MoveBlocked_ThrottledEmission()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            const byte AllowReplan = 1; // bit 0
            CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 1);

            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
                sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var blocked = view.ReadEvents<MoveBlockedEvent>();
            // Exactly one event per blocking episode, not one per stuck tick.
            Assert.Equal(1, blocked.Length);
        }

        // ── Test 6: PathReplannedEvent fires on internal replan ──────────────────────────────────

        [Fact]
        public void MuscleInternalReplan_EmitsPathReplannedEvent()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            const byte AllowReplan = 1;
            var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 3);

            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
                sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var replanned = view.ReadEvents<PathReplannedEvent>();
            Assert.Equal(1, replanned.Length);
            Assert.Equal(entity, replanned[0].Target);
        }

        // ── Test 7: ReplanCount is incremented ───────────────────────────────────────────────────

        [Fact]
        public void MuscleInternalReplan_BumpsReplanCount()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            const byte AllowReplan = 1;
            var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 3);

            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
                sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(1, status.ReplanCount);
        }

        // ── Test 8: AutoSendPathOnReplan fires NavigationPathDetailsResponseEvent ────────────────

        [Fact]
        public void AutoSendPathOnReplan_FiresPathDetailsResponse()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            const byte AllowReplan          = 1 << NavigationConstants.FlagBitAllowReplan;
            const byte AutoSendPathOnReplan = 1 << NavigationConstants.FlagBitAutoSendPathOnReplan;
            byte flags = (byte)(AllowReplan | AutoSendPathOnReplan);

            CreateStuckEntity(repo, intentFlags: flags, maxReplans: 3);

            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
                sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var details = view.ReadEvents<NavigationPathDetailsResponseEvent>();
            Assert.Equal(1, details.Length);
            Assert.Equal(1, details[0].IsAutoRefresh);
        }

        // ── Test 9: Without AutoSendPathOnReplan, no response event fires ────────────────────────

        [Fact]
        public void AutoSendPathOnReplan_NotSet_NoResponseFired()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            const byte AllowReplan = 1 << NavigationConstants.FlagBitAllowReplan;
            CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 3);

            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
                sys.Execute(view, 0.016f);
            repo.Bus.SwapBuffers();

            var details = view.ReadEvents<NavigationPathDetailsResponseEvent>();
            Assert.Equal(0, details.Length);
        }

        // ── Test 10: Replan budget exhausted → hard FailedBlocked ───────────────────────────────

        [Fact]
        public void ReplanBudgetExhausted_WritesFailedBlocked()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view = (ISimulationView)repo;
            var sys  = new NavigationExecutionSystem();

            const byte AllowReplan = 1 << NavigationConstants.FlagBitAllowReplan;
            var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 1);

            // Drive through 2 frustration episodes (episode 1 replans, episode 2 hard-fails).
            int ticksPerEpisode = NavigationExecutionSystem.FrustrationTickLimit + 2;
            for (int ep = 0; ep < 2; ep++)
            {
                for (int i = 0; i <= ticksPerEpisode; i++)
                    sys.Execute(view, 0.016f);
                repo.Bus.SwapBuffers();
                // Drain events between episodes.
                view.ReadEvents<PathReplannedEvent>();
                view.ReadEvents<MoveBlockedEvent>();
            }

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.FailedBlocked, status.Result);
        }
    }
}
