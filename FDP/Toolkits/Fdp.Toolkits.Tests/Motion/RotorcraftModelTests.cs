using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ CE-2168 (docs/designs/motion-models/DESIGN_Motion_Domains.md MD-C, MD-D, MD-H) + M7 (DESIGN_FPV_Drone.md §2, §5) — the air
    /// domain: <see cref="AirMotionSystem"/> routes an air body to its model through <see cref="MotionModelTable"/>, the first model is
    /// <see cref="RotorcraftModel"/> (autonomous AND manual), a dead flyer falls, and nothing else moves it twice.
    /// </summary>
    public sealed class RotorcraftModelTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly AirMotionSystem _air = new();
        private const float Dt = 1f / 60f;

        public RotorcraftModelTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<Locomotion>();
            _w.RegisterComponent<FlightParams>();
            _w.RegisterComponent<FlightState>();
            _w.RegisterComponent<NavigationIntent>();
            _w.RegisterComponent<NavigationStatus>();
            _w.RegisterComponent<ManualDrive>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<VehicleState>();
        }

        public void Dispose() => _w.Dispose();

        private Entity Drone(Vector3 at, LocomotionClass cls = LocomotionClass.Rotorcraft)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity());
            _w.AddComponent(e, new Locomotion { Class = cls });
            _w.AddComponent(e, FlightParams.From(new FlightParamsDto()));   // 30 m/s, climb 8, 120°/s
            _w.AddComponent(e, new FlightState { Airborne = 1 });
            _w.AddComponent(e, new NavigationIntent());
            _w.AddComponent(e, new NavigationStatus());
            _w.AddComponent(e, new Health { Current = 10, Max = 10 });
            _w.SetAuthority<SimTransform>(e, true);   // this node owns its position (the air system moves only owned bodies)
            return e;
        }

        private void Run(int frames) { for (int i = 0; i < frames; i++) _air.Execute(_w, Dt); }

        /// <summary>⭐ M7 — autonomous: it turns toward a 3-D point (≤ 120°/s), climbs (≤ 8 m/s) and flies there at the intent's speed,
        /// then reports Arrived on NavigationStatus (the contract LocomotionMoveTo reads) and hovers.</summary>
        [Fact]
        public void M7_ARotorcraft_FliesToA3DPoint_AndReportsArrival()
        {
            var d = Drone(new Vector3(0, 0, 10));
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 7, FinalDestination = new Vector3(0, 200, 60), TargetSpeed = 20 });

            Run(30);   // half a second: a 90° turn at 120°/s is not done yet, the climb is capped
            var mid = _w.GetComponent<SimTransform>(d);
            Assert.InRange(mid.Position.Z, 10f, 10f + 8f * 0.5f + 0.01f);
            Assert.InRange(RotorcraftModel.YawOf(mid.Rotation), 0.9f, 1.1f);   // 60° of the 90° turn
            Assert.Equal(NavigationResult.InProgress, _w.GetComponent<NavigationStatus>(d).Result);

            Run(60 * 20);
            var end = _w.GetComponent<SimTransform>(d);
            Assert.True(Vector3.Distance(end.Position, new Vector3(0, 200, 60)) <= RotorcraftModel.DefaultArrivalRadiusM, $"at {end.Position}");
            var status = _w.GetComponent<NavigationStatus>(d);
            Assert.Equal((NavigationResult.Arrived, 7u), (status.Result, status.IntentId));
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimVelocity>(d).Linear);   // it hovers there
        }

        /// <summary>⭐ M7 / MD-D — manual: Vertical climbs, Forward flies along the nose, Yaw turns it (≤ 120°/s), nothing ⇒ hover.</summary>
        [Fact]
        public void M7_ARotorcraft_Hovers_ClimbsAndYaws_UnderManualDrive()
        {
            var d = Drone(new Vector3(0, 0, 20));
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.SetComponent(d, new ManualDrive { Vertical = 1 });
            Run(60);
            Assert.Equal(28f, _w.GetComponent<SimTransform>(d).Position.Z, 1);   // 8 m/s for a second

            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2 });
            Run(30);
            Assert.Equal(MathF.PI / 3, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 2);   // 60° in half a second
            Run(30);
            Assert.Equal(MathF.PI / 2, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 2);

            var before = _w.GetComponent<SimTransform>(d).Position;
            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2, Forward = 1 });
            Run(60);
            var after = _w.GetComponent<SimTransform>(d).Position;
            Assert.Equal(30f, after.Y - before.Y, 0);   // along the nose (+Y), at top speed
            Assert.Equal(0f, after.X - before.X, 1);

            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2 });
            Run(10);
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimVelocity>(d).Linear);   // no axis ⇒ it hovers
        }

        /// <summary>⭐ M7 — a dead rotorcraft falls under gravity whatever it is told.</summary>
        [Fact]
        public void M7_ADeadRotorcraft_Falls()
        {
            var d = Drone(new Vector3(0, 0, 50));
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(0, 0, 80), TargetSpeed = 10 });
            _w.SetComponent(d, new Health { Current = 0, Max = 10 });
            Run(60);
            var z = _w.GetComponent<SimTransform>(d).Position.Z;
            Assert.InRange(z, 50f - 0.5f * 9.81f - 0.3f, 50f - 0.5f * 9.81f + 0.3f);
            Assert.True(_w.GetComponent<FlightState>(d).VerticalSpeed < -9f);
        }

        /// <summary>⭐ CE-2168 (MD-C, MD-H) — the air module moves only air bodies; a class whose model is not built fails LOUDLY; and the
        /// projectile integrator leaves a body with a motion model alone (it was moving every SimTransform + SimVelocity body).</summary>
        [Fact]
        public void CE2168_OnlyAirBodies_FailLoudlyForAnUnbuiltClass_AndNoBodyIsIntegratedTwice()
        {
            var ground = Drone(new Vector3(0, 0, 0), LocomotionClass.Wheeled);
            _w.SetComponent(ground, new NavigationIntent { Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(100, 0, 0), TargetSpeed = 10 });
            Run(10);
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimTransform>(ground).Position);

            var drone = Drone(new Vector3(0, 0, 10));
            _w.SetComponent(drone, new SimVelocity { Linear = new Vector3(5, 0, 0) });
            new LinearKinematicsSystem().Execute(_w, 1f);
            Assert.Equal(new Vector3(0, 0, 10), _w.GetComponent<SimTransform>(drone).Position);

            // ⭐ Air Ops slice 4 — MD-H's FixedWing row is built (FixedWingModel): a plane is moved, not refused
            var plane = Drone(new Vector3(0, 0, 100), LocomotionClass.FixedWing);
            Run(1);
            Assert.IsType<FixedWingModel>(MotionModelTable.Model(LocomotionClass.FixedWing));
            Assert.NotEqual(new Vector3(0, 0, 100), _w.GetComponent<SimTransform>(plane).Position);
            Assert.Throws<NotSupportedException>(() => MotionModelTable.Model(LocomotionClass.Surface));
            Assert.Null(MotionModelTable.Model(LocomotionClass.Tracked));   // v1: the ground still runs CarKinematics
            Assert.Equal(MotionDomain.Air, Locomotions.DomainOf(LocomotionClass.Rotorcraft));
            Assert.Equal(MotionDomain.Ground, Locomotions.DomainOf(LocomotionClass.Amphibious));
        }

        // ── ⭐⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, §6, as-built §5b) — the route ─────────────

        private const long HeliType = 9404;
        private const float RestZ = 1.75f;   // the gear's tyres 1.75 m under the reference point (the UH-60's figures)

        private sealed class Routes : IRouteSource
        {
            public readonly System.Collections.Generic.List<NavRouteWaypoint> Waypoints = new();
            public bool TryGetTrajectory(EntityRepository repo, long routeNetworkId, out int trajectoryId) { trajectoryId = 0; return false; }
            public bool TryGetRoute(EntityRepository repo, long routeNetworkId, System.Collections.Generic.List<NavRouteWaypoint> into, out bool loop, out int version)
            {
                into.Clear(); into.AddRange(Waypoints); loop = false; version = 1;
                return into.Count > 0;
            }
        }

        private (Entity heli, AirMotionSystem air, Routes routes) Heli(Vector3 at, bool onTheGround)
        {
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("heli", HeliType);
            t.AddDescriptor(new BodyGeometryDto
            {
                ReferencePoint = ReferencePointKind.CentreOfGravity,
                GroundContacts =
                {
                    new GroundContactPoint { Name = "main left",  X = 2.6f,  Y = 1.35f, Z = -RestZ, Kind = GroundContactKind.Wheel },
                    new GroundContactPoint { Name = "main right", X = 2.6f,  Y = -1.35f, Z = -RestZ, Kind = GroundContactKind.Wheel },
                    new GroundContactPoint { Name = "tail",       X = -6.2f, Y = 0f,    Z = -RestZ, Kind = GroundContactKind.Wheel },
                },
            });
            db.Register(t);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var e = Drone(at);
            _w.SetComponent(e, FlightParams.From(new FlightParamsDto { MaxSpeedMps = 40f, ClimbMps = 8f, HoverHeightM = 30f, TurnRadiusM = 60f }));
            _w.SetComponent(e, new FlightState { Airborne = (byte)(onTheGround ? 0 : 1) });
            _w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = HeliType });
            var routes = new Routes();
            return (e, new AirMotionSystem { RouteSource = routes }, routes);
        }

        private static NavRouteWaypoint Wp(float x, float y, RouteWaypointKind kind = RouteWaypointKind.Pass, RouteHold hold = RouteHold.None)
            => new(new Vector3(x, y, 0f), 0f, kind, 0f, hold, 0f, 0L);

        private void Order(Entity e) => _w.SetComponent(e, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 1, RouteEntityId = 900 });

        /// <summary>⭐ B7 — a route from the ground: the helicopter on its gear takes off straight up to the hover height, flies the route
        /// (the nose down as it gathers speed, rolled into the left turn) and, at the last Ground waypoint, sets down on its gear.</summary>
        [Fact]
        public void B7_ARouteFromTheGround_TakesOff_FliesAndLands_OnItsGear()
        {
            var (heli, air, routes) = Heli(new Vector3(0, 0, RestZ), onTheGround: true);
            routes.Waypoints.AddRange(new[] { Wp(0, 0, RouteWaypointKind.Ground), Wp(300, 0), Wp(300, 300), Wp(600, 300, RouteWaypointKind.Ground) });
            Order(heli);

            var seen = new System.Collections.Generic.HashSet<NavigationPhase>();
            float maxZ = 0f, minPitch = 0f, minRoll = 0f;
            Vector2 firstMove = default; bool movedSideways = false;
            for (int i = 0; i < 60 * 120; i++)
            {
                air.Execute(_w, Dt);
                var tf = _w.GetComponent<SimTransform>(heli);
                var st = _w.GetComponent<NavigationStatus>(heli);
                seen.Add(st.Phase);
                maxZ = MathF.Max(maxZ, tf.Position.Z);
                var fwd = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                var left = Vector3.Transform(Vector3.UnitY, tf.Rotation);
                minPitch = MathF.Min(minPitch, fwd.Z);
                minRoll = MathF.Min(minRoll, left.Z);
                if (!movedSideways && new Vector2(tf.Position.X, tf.Position.Y).Length() > 0.5f) { movedSideways = true; firstMove = new Vector2(tf.Position.Z, 0); }
                if (st.Result == NavigationResult.Arrived) break;
            }

            var end = _w.GetComponent<SimTransform>(heli);
            var status = _w.GetComponent<NavigationStatus>(heli);
            Assert.Equal((NavigationResult.Arrived, NavigationPhase.Landed), (status.Result, status.Phase));
            Assert.True(Vector2.Distance(new Vector2(end.Position.X, end.Position.Y), new Vector2(600, 300)) < 0.5f, $"at {end.Position}");
            Assert.InRange(end.Position.Z, RestZ - 0.01f, RestZ + 0.01f);                 // on its gear
            Assert.Equal(0, _w.GetComponent<FlightState>(heli).Airborne);
            Assert.InRange(maxZ, 29.5f, 30.5f);                                            // the hover height over the ground
            Assert.True(firstMove.X >= 29f, $"it left the spot only at the route height, at Z {firstMove.X}");   // straight up first
            Assert.Contains(NavigationPhase.TakingOff, seen);
            Assert.Contains(NavigationPhase.Landing, seen);
            Assert.True(minPitch < -0.05f, "the nose goes down while it gathers speed");
            Assert.True(minRoll < -0.05f, "it rolls into the left turn (left side down)");
        }

        /// <summary>⭐ B7 — a Ground waypoint with an Unload hold: it lands and reports Unloading, and stays there until its Brain
        /// releases it (<see cref="NavigationIntent.ReleaseHold"/>); then it takes off and flies on to the end of the route.</summary>
        [Fact]
        public void B7_AGroundWaypoint_HoldsUntilReleased_ThenTakesOff()
        {
            var (heli, air, routes) = Heli(new Vector3(0, 0, 30), onTheGround: false);
            routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(200, 0, RouteWaypointKind.Ground, RouteHold.Unload), Wp(400, 0) });
            Order(heli);

            int frames = 0;
            while (_w.GetComponent<NavigationStatus>(heli).Phase != NavigationPhase.Unloading && frames++ < 60 * 60) air.Execute(_w, Dt);
            var down = _w.GetComponent<SimTransform>(heli).Position;
            Assert.Equal(NavigationPhase.Unloading, _w.GetComponent<NavigationStatus>(heli).Phase);
            Assert.True(Vector2.Distance(new Vector2(down.X, down.Y), new Vector2(200, 0)) < 0.5f, $"at {down}");
            Assert.InRange(down.Z, RestZ - 0.01f, RestZ + 0.01f);
            Assert.Equal(0, _w.GetComponent<FlightState>(heli).Airborne);

            for (int i = 0; i < 60 * 10; i++) air.Execute(_w, Dt);                        // ten seconds: nobody released it
            Assert.Equal(NavigationPhase.Unloading, _w.GetComponent<NavigationStatus>(heli).Phase);
            Assert.Equal(down, _w.GetComponent<SimTransform>(heli).Position);

            var intent = _w.GetComponent<NavigationIntent>(heli);
            intent.ReleaseHold = true;
            _w.SetComponent(heli, intent);
            var seen = new System.Collections.Generic.HashSet<NavigationPhase>();
            for (int i = 0; i < 60 * 60 && _w.GetComponent<NavigationStatus>(heli).Result != NavigationResult.Arrived; i++)
            {
                air.Execute(_w, Dt);
                seen.Add(_w.GetComponent<NavigationStatus>(heli).Phase);
            }
            Assert.Contains(NavigationPhase.TakingOff, seen);
            var end = _w.GetComponent<SimTransform>(heli).Position;
            Assert.Equal(NavigationResult.Arrived, _w.GetComponent<NavigationStatus>(heli).Result);
            Assert.True(Vector2.Distance(new Vector2(end.X, end.Y), new Vector2(400, 0)) < 0.5f, $"at {end}");
            Assert.Equal(1, _w.GetComponent<FlightState>(heli).Airborne);                  // a Pass end: it hovers there
        }

        /// <summary>⭐ B2 — the smoothed path: every corner an arc no tighter than the turn radius, and the helicopter that flies it passes
        /// every waypoint within that radius — each corner at exactly the fillet's R·(1/cos(φ/2) − 1).</summary>
        [Fact]
        public void B2_TheSmoothedPath_PassesEveryWaypoint_WithinTheTurnRadius()
        {
            const float R = 60f;
            var wps = new[] { Wp(0, 0), Wp(200, 0), Wp(250, 150), Wp(450, 100), Wp(500, 300) };
            var path = AirRoute.Compile(new Vector3(0, 0, 30), wps, loop: false, turnRadius: R, hoverAgl: 30f, ground: null);
            for (int j = 0; j < path.Curvature.Length; j++)
                Assert.True(MathF.Abs(path.Curvature[j]) <= 1.05f / R, $"curvature {path.Curvature[j]} at {path.Points[j]} is tighter than 1/{R}");

            var (heli, air, routes) = Heli(new Vector3(0, 0, 30), onTheGround: false);
            routes.Waypoints.AddRange(wps);
            Order(heli);
            var closest = new float[wps.Length];
            Array.Fill(closest, float.MaxValue);
            for (int i = 0; i < 60 * 120 && _w.GetComponent<NavigationStatus>(heli).Result != NavigationResult.Arrived; i++)
            {
                air.Execute(_w, Dt);
                var p = _w.GetComponent<SimTransform>(heli).Position;
                for (int k = 0; k < wps.Length; k++)
                    closest[k] = MathF.Min(closest[k], Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(wps[k].Position.X, wps[k].Position.Y)));
            }
            Assert.Equal(NavigationResult.Arrived, _w.GetComponent<NavigationStatus>(heli).Result);
            for (int k = 0; k < wps.Length; k++) Assert.True(closest[k] <= R, $"waypoint {k} passed at {closest[k]:F1} m");
            for (int k = 1; k < wps.Length - 1; k++)
            {
                // a plain fillet passes the corner at R·(1/cos(φ/2) − 1), φ the turn there
                var a = Vector2.Normalize(new Vector2(wps[k].Position.X - wps[k - 1].Position.X, wps[k].Position.Y - wps[k - 1].Position.Y));
                var b = Vector2.Normalize(new Vector2(wps[k + 1].Position.X - wps[k].Position.X, wps[k + 1].Position.Y - wps[k].Position.Y));
                float turn = MathF.Acos(Math.Clamp(Vector2.Dot(a, b), -1f, 1f));
                float expected = R * (1f / MathF.Cos(turn / 2f) - 1f);
                Assert.InRange(closest[k], expected - 1f, expected + 1f);
            }
        }
    }
}
