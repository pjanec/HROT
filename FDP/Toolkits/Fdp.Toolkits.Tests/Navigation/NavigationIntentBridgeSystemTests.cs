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
            repo.RegisterComponent<NavigationApplied>();   // ⭐ R-3039 S1 — what the bridge already applied
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
        /// ⭐⭐ <c>R-3039</c> S1 (docs/DESIGN_System_State_Inventory.md §6 #5) — an intent already applied is not applied AGAIN after a checkpoint
        /// restored into a FRESH system: applied, the mover then arrives (<c>HasArrived</c> = 1), keyframe, applied to a new world with a NEW
        /// bridge ⇒ he stays arrived. 🔴 Red-proof: the applied-id back in a system field ⇒ the fresh bridge re-applies ⇒ HasArrived 0 ⇒ red.
        /// </summary>
        [Fact]
        public void R3039_AnAppliedIntent_IsNotReappliedAfterACheckpointRestoredIntoAFreshSystem()
        {
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(100f, 200f, 0f), TargetSpeed = 10f, ArrivalRadius = 3f, IntentId = 7u,
            });
            repo.AddComponent(entity, new NavState());
            repo.Bus.SwapBuffers();
            new NavigationIntentBridgeSystem().Execute(repo, 0.016f);
            var nav = repo.GetComponent<NavState>(entity);
            nav.HasArrived = 1;   // the kinematics brought him there
            repo.SetComponent(entity, nav);

            var kf = Fdp.Toolkit.Replay.WorldKeyframes.Capture(repo, Fdp.Toolkit.Replay.CaptureOptions.Recording);
            using var fresh = CreateWorld();
            Fdp.Toolkit.Replay.WorldKeyframes.Apply(fresh, kf);
            fresh.Bus.SwapBuffers();
            new NavigationIntentBridgeSystem().Execute(fresh, 0.016f);
            Assert.Equal(1, fresh.GetComponent<NavState>(entity).HasArrived);
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
        /// ⭐⭐ <c>CE-4032</c> — a vehicle starting a one-shot route where it already stands PART WAY along it starts at its own place on
        /// it, not at the first waypoint behind it: 📌 live on <c>uc-urban-battle</c> the lead APC, 10 m past the route's start, turned
        /// back to it while the IFV behind drove on and took the lead, every run. One standing before the start starts at 0; a looped
        /// route starts at 0. ✅ Red-proof: the start progress back to 0 ⇒ 10 expected, 0 seen.
        /// </summary>
        [Fact]
        public void CE4032_AFollowRoute_StartsWhereTheVehicleStandsAlongIt_NotBehindIt()
        {
            float Start(Vector3 at, bool loop = false)
            {
                var repo = CreateWorld();
                repo.RegisterComponent<SimTransform>();
                var system = new NavigationIntentBridgeSystem { RouteSource = new OneRoute(loop) };
                var e = repo.CreateEntity();
                repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                repo.AddComponent(e, new NavigationIntent { Mode = NavigationMode.FollowRoute, RouteEntityId = 9811, IntentId = 1u });
                repo.AddComponent(e, new NavState());
                repo.Bus.SwapBuffers();
                system.Execute(repo, 0.016f);
                var nav = repo.GetComponent<NavState>(e);
                Assert.Equal(KinematicsMode.CustomTrajectory, nav.Mode);
                repo.Dispose();
                return nav.ProgressS;
            }

            Assert.Equal(10f, Start(new Vector3(652, 40, 0)), 3);     // the APC, 10 m past the start
            Assert.Equal(0f, Start(new Vector3(652, 15, 0)), 3);      // the IFV, 15 m before it
            Assert.Equal(350f, Start(new Vector3(653, 390, 0)), 3);   // past the end: the end (350 m long)
            Assert.Equal(0f, Start(new Vector3(652, 40, 0), loop: true));
        }

        private sealed class OneRoute : IRouteSource
        {
            private readonly bool _loop;
            public OneRoute(bool loop) => _loop = loop;
            public bool TryGetTrajectory(EntityRepository repo, long routeNetworkId, out int trajectoryId) { trajectoryId = 5; return true; }
            public bool TryGetRoute(EntityRepository repo, long routeNetworkId, System.Collections.Generic.List<NavRouteWaypoint> into, out bool loop, out int version)
            {
                into.Clear();
                into.Add(new NavRouteWaypoint(new Vector3(652, 30, 0), 8f, RouteWaypointKind.Ground, 0f, RouteHold.None, 0f, 0L));
                into.Add(new NavRouteWaypoint(new Vector3(652, 380, 0), 8f, RouteWaypointKind.Ground, 0f, RouteHold.None, 0f, 0L));
                loop = _loop; version = 1;
                return true;
            }
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
    
        /// <summary>⭐ M7 / CE-2168 (Motion Domains MD-E) — the bridge skips a FLYER: its air model reads the intent itself (straight legs
        /// to the 3-D point); no ground NavState is written and no ground path is asked for.</summary>
        [Fact]
        public void M7_TheBridge_SkipsAFlyer()
        {
            var repo = CreateWorld();
            repo.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            var flyer = repo.CreateEntity();
            repo.AddComponent(flyer, new NavigationIntent { Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(100, 0, 50), TargetSpeed = 10, IntentId = 1 });
            repo.AddComponent(flyer, new NavState());
            repo.AddComponent(flyer, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            var car = repo.CreateEntity();
            repo.AddComponent(car, new NavigationIntent { Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(100, 0, 0), TargetSpeed = 10, IntentId = 1 });
            repo.AddComponent(car, new NavState());
            repo.AddComponent(car, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled });

            repo.Bus.SwapBuffers();
            new NavigationIntentBridgeSystem().Execute(repo, 0.016f);

            Assert.Equal(KinematicsMode.None, repo.GetComponent<NavState>(flyer).Mode);
            Assert.Equal(KinematicsMode.Direct, repo.GetComponent<NavState>(car).Mode);
            repo.Dispose();
        }

}
}
