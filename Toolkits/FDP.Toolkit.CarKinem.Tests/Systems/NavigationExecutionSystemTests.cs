using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Navigation;
using Xunit;

// Disambiguate from CarKinem.Core.NavigationMode which exists for the legacy NavState.
using NavMode   = FDP.Toolkit.Navigation.NavigationMode;
using NavResult = FDP.Toolkit.Navigation.NavigationResult;

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
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
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
                FinalDestination = destination,
                ArrivalRadius    = arrivalRadius,
                TargetSpeed      = 15f,
                IntentId         = intentId,
            });
            repo.AddComponent(entity, new NavigationStatus
            {
                IntentId = intentId,   // matching → no reset on first tick
                Result   = NavResult.InProgress,
            });
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
            system.Create(repo);

            // Entity is placed 3 m from target; radius is 5 m → within threshold.
            var entity = AddNavigatingEntity(repo,
                position:     new Vector2(97f, 0f),
                destination:  new Vector2(100f, 0f),
                arrivalRadius: 5f,
                velocityX:    0f);

            system.Run();

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavResult.Arrived, status.Result);

            system.Dispose();
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
            system.Create(repo);

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
                system.Run();
                lastResult = repo.GetComponent<NavigationStatus>(entity).Result;
                if (lastResult == NavResult.FailedBlocked)
                    break;
            }

            Assert.Equal(NavResult.FailedBlocked, lastResult);

            system.Dispose();
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
            system.Create(repo);

            // Entity at origin far from destination.
            var entity = AddNavigatingEntity(repo,
                position:     Vector2.Zero,
                destination:  new Vector2(500f, 0f),
                arrivalRadius: 5f,
                intentId:     3);   // intent ID = 3

            // Simulate that the Brain issued a new command by bumping the intent ID.
            var intent = repo.GetComponent<NavigationIntent>(entity);
            intent.IntentId = 7;    // new intent
            intent.FinalDestination = new Vector2(100f, 0f);
            repo.SetComponent(entity, intent);

            // Status still carries old intent id (3) — mismatch.
            var beforeStatus = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(3u, beforeStatus.IntentId);

            system.Run();

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(7u, status.IntentId);
            Assert.Equal(NavResult.InProgress, status.Result);

            system.Dispose();
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
            system.Create(repo);

            var entity = AddNavigatingEntity(repo,
                position:     Vector2.Zero,
                destination:  new Vector2(100f, 0f),
                arrivalRadius: 5f);

            // Clear the mode to None → inactive.
            var intent = repo.GetComponent<NavigationIntent>(entity);
            intent.Mode = NavMode.None;
            repo.SetComponent(entity, intent);

            system.Run();

            var status = repo.GetComponent<NavigationStatus>(entity);
            // Status should remain InProgress (unchanged).
            Assert.Equal(NavResult.InProgress, status.Result);

            system.Dispose();
        }
    }
}
