using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// PACK-I003 SC3 — NavigationIntentBridgeSystem still translates NavigationIntent → NavState
    /// correctly after the removal of legacy Cmd* movement events from VehicleCommandSystem.
    /// </summary>
    public class NavigationIntentBridgeSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavState>();
            return repo;
        }

        /// <summary>
        /// DirectPoint intent is mapped to KinematicsMode.Direct with FinalDestination,
        /// TargetSpeed and ArrivalRadius propagated.
        /// </summary>
        [Fact]
        public void DirectPoint_Intent_MapsToDirectKinematics()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            var dest = new Vector2(100f, 200f);
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavigationMode.DirectPoint,
                FinalDestination = new Vector3(dest.X, dest.Y, 0f),
                TargetSpeed      = 10f,
                ArrivalRadius    = 3.0f,
                IntentId         = 1u,
            });
            repo.AddComponent(entity, new NavState());

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Direct, nav.Mode);
            Assert.Equal(new Vector3(dest.X, dest.Y, 0f), nav.FinalDestination);
            Assert.Equal(10f, nav.TargetSpeed);
            Assert.Equal(3.0f, nav.ArrivalRadius);

            repo.Dispose();
        }

        /// <summary>
        /// CE-498 — an intent written while the entity is still <c>Constructing</c> must reach
        /// <see cref="NavState"/>. The bridge scans by DELTA and activation bumps no component
        /// version, so with the default Active-only filter the intent was skipped while
        /// Constructing and then never seen again once Active — the unit never moved.
        /// </summary>
        // ── ⭐ CE-1035 Q0b (DESIGN_World_Query_Seam.md WQ-H) — a 2-D destination is put on the surface by the motion side ──

        /// <summary>A world with a 3 m deck over (100..140, 0..40).</summary>
        private static Fdp.Toolkit.Terrain.TerrainWorld Deck() => Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
        {
          "type": "FeatureCollection",
          "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 },
          "features": [
            { "type": "Feature", "properties": { "kind": "slab", "label": "Deck" },
              "geometry": { "type": "Polygon", "coordinates": [[[100,0,3],[140,0,3],[140,40,3],[100,40,3],[100,0,3]]] } }
          ]
        }
        """);

        private static Vector3 Applied(Vector3 moverAt, Vector3 destination, byte flags, bool withWorld = true)
        {
            var repo = CreateWorld();
            repo.RegisterComponent<SimTransform>();
            if (withWorld) repo.SetSingletonManaged(Deck());
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = moverAt });
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode = NavigationMode.DirectPoint, FinalDestination = destination, TargetSpeed = 5f, IntentId = 1u, Flags = flags,
            });
            repo.AddComponent(entity, new NavState());
            repo.Bus.SwapBuffers();
            new NavigationIntentBridgeSystem().Execute(repo, 0.016f);
            var applied = repo.GetComponent<NavState>(entity).FinalDestination;
            repo.Dispose();
            return applied;
        }

        [Fact]
        public void Q0b_A2DDestination_LandsOnTheSurfaceAtTheMoversLevel()
        {
            const byte onSurface = NavigationConstants.FlagDestinationOnSurface;
            // a mover on the deck, sent to a 2-D point over the deck: the deck, not the ground under it
            Assert.Equal(3f, Applied(new Vector3(110, 10, 3), new Vector3(120, 20, 0), onSurface).Z);
            // the same point from a mover on the ground: the ground under the deck
            Assert.Equal(0f, Applied(new Vector3(60, 10, 0), new Vector3(120, 20, 0), onSurface).Z);
            // the flag clear: the brain's Z is real and is kept exactly
            Assert.Equal(7f, Applied(new Vector3(110, 10, 3), new Vector3(120, 20, 7), 0).Z);
            // no world at all: the point as sent
            Assert.Equal(0f, Applied(new Vector3(110, 10, 3), new Vector3(120, 20, 0), onSurface, withWorld: false).Z);
            // the X and Y are never touched
            var p = Applied(new Vector3(110, 10, 3), new Vector3(120, 20, 0), onSurface);
            Assert.Equal(120f, p.X); Assert.Equal(20f, p.Y);
        }

        [Fact]
        public void Intent_WrittenWhileConstructing_IsAppliedAndNotLostOnActivation()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            repo.SetLifecycleState(entity, EntityLifecycle.Constructing);
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavigationMode.DirectPoint,
                FinalDestination = new Vector3(500f, 500f, 0f),
                TargetSpeed      = 15f,
                IntentId         = 1u,
            });
            repo.AddComponent(entity, new NavState());

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            repo.SetLifecycleState(entity, EntityLifecycle.Active);
            repo.Tick();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Direct, nav.Mode);
            Assert.Equal(new Vector3(500f, 500f, 0f), nav.FinalDestination);

            repo.Dispose();
        }

        /// <summary>CE-498 guard — widening the scan to Constructing must not drive a TearDown entity.</summary>
        [Fact]
        public void Intent_OnTearDownEntity_IsNotApplied()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            repo.SetLifecycleState(entity, EntityLifecycle.TearDown);
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavigationMode.DirectPoint,
                FinalDestination = new Vector3(10f, 10f, 0f),
                TargetSpeed      = 5f,
                IntentId         = 1u,
            });
            repo.AddComponent(entity, new NavState());

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            Assert.NotEqual(KinematicsMode.Direct, repo.GetComponent<NavState>(entity).Mode);

            repo.Dispose();
        }

        /// <summary>
        /// FollowRoute intent with a new TrajectoryId is mapped to CustomTrajectory mode
        /// and ProgressS is reset to 0 on a new intent.
        /// </summary>
        [Fact]
        public void FollowRoute_NewIntent_ResetsProgressS()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode         = NavigationMode.FollowRoute,
                TrajectoryId = 7,
                IntentId     = 3u,
            });
            repo.AddComponent(entity, new NavState { ProgressS = 0.8f });

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.CustomTrajectory, nav.Mode);
            Assert.Equal(7, nav.TrajectoryId);
            Assert.Equal(0f, nav.ProgressS); // reset on new intent

            repo.Dispose();
        }

        /// <summary>
        /// None intent halts navigation — NavState.Mode = KinematicsMode.None, TargetSpeed = 0.
        /// </summary>
        [Fact]
        public void NoneIntent_HaltsNavigation_NavStateSetToNone()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent { Mode = NavigationMode.None });
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Direct, TargetSpeed = 99f });

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.None, nav.Mode);
            Assert.Equal(0f, nav.TargetSpeed);

            repo.Dispose();
        }
    }
}
