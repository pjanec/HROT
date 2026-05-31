using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Xunit;

using NavMode   = Fdp.Toolkit.Navigation.NavigationMode;
using NavResult = Fdp.Toolkit.Navigation.NavigationResult;

namespace CarKinem.Tests.Systems
{
    /// <summary>
    /// Unit tests for <see cref="NavigationExecutionSystem"/> (MOD1-P1T4).
    /// Verifies the CQRS Muscle-layer status writer: arrival detection, frustration
    /// detection, and per-intent-id reset logic.
    /// </summary>
    public class NavigationExecutionSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<FrustrationTicks>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterEvent<MoveStartedEvent>();
            repo.RegisterEvent<MoveCompletedEvent>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            return repo;
        }

        private static Entity AddNavigatingEntity(
            EntityRepository repo,
            Vector2 position,
            Vector2 destination,
            float arrivalRadius,
            float velocityX = 0f,
            float velocityY = 0f,
            uint intentId   = 1)
        {
            var entity = repo.CreateEntity();

            repo.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(position.X, position.Y, 0f),
                Rotation = Quaternion.Identity,
            });
            repo.AddComponent(entity, new SimVelocity
            {
                Linear = new Vector3(velocityX, velocityY, 0f),
            });
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavMode.DirectPoint,
                FinalDestination = new Vector3(destination.X, destination.Y, 0f),
                ArrivalRadius    = arrivalRadius,
                TargetSpeed      = 15f,
                IntentId         = intentId,
            });
            repo.AddComponent(entity, new NavigationStatus
            {
                IntentId = intentId,   // matching → no reset on first tick
                Result   = NavResult.InProgress,
            });
            repo.AddComponent(entity, new FrustrationTicks { Ticks = 0 });
            return entity;
        }

        // ── Test 1: Arrival ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// MOD1-P1T4 T1: When the entity's XY position is within <c>ArrivalRadius</c> of the
        /// target, <see cref="NavigationStatus.Result"/> must become
        /// <see cref="NavigationResult.Arrived"/>.
        /// </summary>
        [Fact]
        public void NavigationExecution_WritesArrivedWhenEntityReachesTarget()
        {
            using var repo = CreateWorld();
            var system = new NavigationExecutionSystem();

            // Entity is placed 3 m from target; radius is 5 m → within threshold.
            var entity = AddNavigatingEntity(repo,
                position:     new Vector2(97f, 0f),
                destination:  new Vector2(100f, 0f),
                arrivalRadius: 5f,
                velocityX:    0f);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavResult.Arrived, status.Result);
        }

        // ── Test 2: Frustration / stuck ───────────────────────────────────────────────────────────

        /// <summary>
        /// MOD1-P1T4 T2: When the entity's speed stays below
        /// <see cref="NavigationExecutionSystem.FrustrationSpeedThreshold"/> for more than
        /// <see cref="NavigationExecutionSystem.FrustrationTickLimit"/> ticks,
        /// the status must become <see cref="NavigationResult.FailedBlocked"/>.
        /// </summary>
        [Fact]
        public void NavigationExecution_WritesFailedWhenEntityStuck()
        {
            using var repo = CreateWorld();
            var system = new NavigationExecutionSystem();

            // Entity at origin; destination far away; speed = 0 → stuck every tick.
            var entity = AddNavigatingEntity(repo,
                position:     Vector2.Zero,
                destination:  new Vector2(1000f, 0f),
                arrivalRadius: 5f,
                velocityX:    0f);

            // Run FrustrationTickLimit + 2 ticks.
            NavigationResult? lastResult = null;
            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
            {
                system.Execute(repo, 0.016f);
                lastResult = repo.GetComponent<NavigationStatus>(entity).Result;
                if (lastResult == NavResult.FailedBlocked)
                    break;
            }

            Assert.Equal(NavResult.FailedBlocked, lastResult);
        }

        // ── Test 3: Intent ID mismatch resets status ──────────────────────────────────────────────

        /// <summary>
        /// MOD1-P1T4 T3: When <see cref="NavigationStatus.IntentId"/> differs from
        /// <see cref="NavigationIntent.IntentId"/>, the system must reinitialise the status
        /// to <see cref="NavigationResult.InProgress"/> with the new intent ID.
        /// </summary>
        [Fact]
        public void NavigationExecution_IntentIdMismatch_ResetsOnNewCommand()
        {
            using var repo = CreateWorld();
            var system = new NavigationExecutionSystem();

            // Entity at origin far from destination.
            var entity = AddNavigatingEntity(repo,
                position:     Vector2.Zero,
                destination:  new Vector2(500f, 0f),
                arrivalRadius: 5f,
                intentId:     3);   // intent ID = 3

            // Simulate that the Brain issued a new command by bumping the intent ID.
            var intent = repo.GetComponent<NavigationIntent>(entity);
            intent.IntentId = 7;    // new intent
            intent.FinalDestination = new Vector3(100f, 0f, 0f);
            repo.SetComponent(entity, intent);

            // Status still carries old intent id (3) — mismatch.
            var beforeStatus = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(3u, beforeStatus.IntentId);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(7u, status.IntentId);
            Assert.Equal(NavResult.InProgress, status.Result);
        }

        // ── Test 4: Inactive intent is skipped ────────────────────────────────────────────────────

        /// <summary>
        /// When <see cref="NavigationIntent.Mode"/> is <see cref="NavigationMode.None"/>,
        /// the system must leave <see cref="NavigationStatus"/> untouched.
        /// </summary>
        [Fact]
        public void NavigationExecution_SkipsInactiveIntents()
        {
            using var repo = CreateWorld();
            var system = new NavigationExecutionSystem();

            var entity = AddNavigatingEntity(repo,
                position:     Vector2.Zero,
                destination:  new Vector2(100f, 0f),
                arrivalRadius: 5f);

            // Clear the mode to None → inactive.
            var intent = repo.GetComponent<NavigationIntent>(entity);
            intent.Mode = NavMode.None;
            repo.SetComponent(entity, intent);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            // Status should remain InProgress (unchanged).
            Assert.Equal(NavResult.InProgress, status.Result);
        }

        // ── Test 5: FrustrationTicks component increments (CT-MOD1-A) ────────────────────────────

        /// <summary>
        /// CT-MOD1-A: <see cref="FrustrationTicks.Ticks"/> must increment each tick when the
        /// entity is stuck (speed below threshold).  The internal dictionary
        /// <c>_frustrationTicks</c> must not exist (proven by the field not being present on
        /// the system type).
        /// </summary>
        [Fact]
        public void FrustrationTicks_ComponentIncrementsEachStuckTick()
        {
            using var repo = CreateWorld();
            var system = new NavigationExecutionSystem();

            var entity = AddNavigatingEntity(repo,
                position:     Vector2.Zero,
                destination:  new Vector2(500f, 0f),
                arrivalRadius: 2f,
                velocityX:    0f);  // stuck — speed = 0

            // Run 3 ticks.
            system.Execute(repo, 0.016f);
            system.Execute(repo, 0.016f);
            system.Execute(repo, 0.016f);

            var ticks = repo.GetComponent<FrustrationTicks>(entity).Ticks;
            Assert.Equal(3, ticks);

            // Verify dictionary field does not exist on the system type (CT-MOD1-A requirement).
            var dictField = typeof(NavigationExecutionSystem).GetField(
                "_frustrationTicks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.Null(dictField);
        }

        // ── PACK-N002: ProgressS mirroring from NavState ──────────────────────

        private static EntityRepository CreateWorldWithNavState()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<FrustrationTicks>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<NavState>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            return repo;
        }

        private static Entity AddNavigatingEntityWithNavState(
            EntityRepository repo,
            float navStateProgressS,
            float statusProgressS = 0f,
            uint intentId = 1)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = new System.Numerics.Vector3(0f, 0f, 0f), Rotation = System.Numerics.Quaternion.Identity });
            repo.AddComponent(entity, new SimVelocity { Linear = new System.Numerics.Vector3(5f, 0f, 0f) });
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavMode.FollowRoute,
                FinalDestination = new Vector3(1000f, 0f, 0f),
                ArrivalRadius    = 5f,
                TargetSpeed      = 15f,
                IntentId         = intentId,
            });
            repo.AddComponent(entity, new NavigationStatus
            {
                IntentId  = intentId,
                Result    = NavResult.InProgress,
                ProgressS = statusProgressS,
            });
            repo.AddComponent(entity, new FrustrationTicks { Ticks = 0 });
            repo.AddComponent(entity, new NavState { ProgressS = navStateProgressS });
            return entity;
        }

        /// <summary>
        /// PACK-N002 SC-1: After a tick, <see cref="NavigationStatus.ProgressS"/> must equal
        /// <see cref="NavState.ProgressS"/> on the same entity.
        /// </summary>
        [Fact]
        public void NavigationExecution_MapsProgressS_FromNavState()
        {
            using var repo = CreateWorldWithNavState();
            var system = new NavigationExecutionSystem();

            var entity = AddNavigatingEntityWithNavState(repo, navStateProgressS: 0.73f, statusProgressS: 0f);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(0.73f, status.ProgressS, precision: 4);
        }

        /// <summary>
        /// PACK-N002 SC-2: When <see cref="NavState.ProgressS"/> is 0, the output
        /// <see cref="NavigationStatus.ProgressS"/> must also be 0 (not left at any prior value).
        /// </summary>
        [Fact]
        public void NavigationExecution_ZeroProgressS_Passthrough()
        {
            using var repo = CreateWorldWithNavState();
            var system = new NavigationExecutionSystem();

            // Pre-seed a non-zero ProgressS on status to verify it gets overwritten with 0.
            var entity = AddNavigatingEntityWithNavState(repo, navStateProgressS: 0f, statusProgressS: 0.5f);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(0f, status.ProgressS, precision: 4);
        }

        /// <summary>
        /// PACK-N002 SC-3: After the tick, existing <see cref="NavigationStatus.IntentId"/>
        /// and <see cref="NavigationStatus.Result"/> must retain their pre-tick values
        /// (mapping ProgressS must not accidentally zero other fields).
        /// </summary>
        [Fact]
        public void NavigationExecution_PreservesExistingFields_WhenProgressSMapped()
        {
            using var repo = CreateWorldWithNavState();
            var system = new NavigationExecutionSystem();

            const uint expectedIntentId = 7u;
            var entity = AddNavigatingEntityWithNavState(
                repo, navStateProgressS: 0.3f, intentId: expectedIntentId);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(expectedIntentId, status.IntentId);
            Assert.Equal(NavResult.InProgress, status.Result);
            Assert.Equal(0.3f, status.ProgressS, precision: 4);
        }

        // ── OFX-018: ReplanTimeBudget stops replanning when elapsed >= budget ────

        /// <summary>
        /// OFX-018: When <see cref="NavigationIntent.ReplanTimeBudget"/> is set to a small
        /// positive value, replanning must stop once the cumulative stuck time exceeds the
        /// budget — even when the replan count is still below <see cref="NavigationIntent.MaxReplans"/>.
        /// </summary>
        [Fact]
        public void ReplanTimeBudget_ExceededBeforeCountLimit_CausesFailedBlocked()
        {
            using var repo = CreateWorld();
            var system = new NavigationExecutionSystem();

            byte allowReplanFlag = (byte)(1 << NavigationConstants.FlagBitAllowReplan);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform
            {
                Position = new System.Numerics.Vector3(0f, 0f, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });
            // velocity = 0 → always stuck
            repo.AddComponent(entity, new SimVelocity { Linear = System.Numerics.Vector3.Zero });
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavMode.DirectPoint,
                FinalDestination = new System.Numerics.Vector3(1000f, 0f, 0f),
                ArrivalRadius    = 2f,
                TargetSpeed      = 15f,
                IntentId         = 1u,
                Flags            = allowReplanFlag,
                // High count limit: without time-budget this would allow many replans.
                MaxReplans       = 10,
                // Small budget: will be exceeded by the time FrustrationTickLimit fires.
                ReplanTimeBudget = 0.01f,
            });
            repo.AddComponent(entity, new NavigationStatus
            {
                IntentId = 1u,
                Result   = NavResult.InProgress,
            });
            repo.AddComponent(entity, new FrustrationTicks { Ticks = 0 });

            // Run enough ticks for the frustration guard to fire.
            // dt = 0.016f; after FrustrationTickLimit + 2 ticks the guard fires.
            // ElapsedSinceFirstReplan = (FrustrationTickLimit + 2) * 0.016 >> 0.01 budget.
            NavigationResult? lastResult = null;
            for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 2; i++)
            {
                system.Execute(repo, 0.016f);
                lastResult = repo.GetComponent<NavigationStatus>(entity).Result;
                if (lastResult == NavResult.FailedBlocked)
                    break;
            }

            var finalStatus = repo.GetComponent<NavigationStatus>(entity);
            // Entity should fail.
            Assert.Equal(NavResult.FailedBlocked, finalStatus.Result);
            // The time-budget guard prevented any actual replan from occurring.
            Assert.Equal(0, finalStatus.ReplanCount);
        }
    }
}
