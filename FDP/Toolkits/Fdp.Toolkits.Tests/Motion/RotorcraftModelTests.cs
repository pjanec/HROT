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

        private Entity Drone(Vector3 at, LocomotionClass cls = LocomotionClass.Rotorcraft, float accel = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity());
            _w.AddComponent(e, new Locomotion { Class = cls });
            _w.AddComponent(e, FlightParams.From(new FlightParamsDto { AccelMps2 = accel }));   // 30 m/s, climb 8, 120°/s, 3 m/s² unless stated
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

        /// <summary>⭐ M7 / MD-D — manual: Vertical climbs, Forward flies along the nose, Yaw turns it (≤ 120°/s), nothing ⇒ it brakes to a
        /// hover and holds it. ⭐ R-3007: the axes ask for a velocity the body reaches at its type's acceleration (here 60 m/s²).</summary>
        [Fact]
        public void M7_ARotorcraft_Hovers_ClimbsAndYaws_UnderManualDrive()
        {
            var d = Drone(new Vector3(0, 0, 20), accel: 60f);
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.SetComponent(d, new ManualDrive { Vertical = 1 });
            Run(60);
            Assert.Equal(20f + 8f - 8f * 8f / (2f * 60f), _w.GetComponent<SimTransform>(d).Position.Z, 1);   // 8 m/s, reached in 0.13 s

            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2 });
            Run(30);
            Assert.Equal(MathF.PI / 3, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 2);   // 60° in half a second
            Run(30);
            Assert.Equal(MathF.PI / 2, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 2);

            var before = _w.GetComponent<SimTransform>(d).Position;
            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2, Forward = 1 });
            Run(60);
            var after = _w.GetComponent<SimTransform>(d).Position;
            Assert.InRange(after.Y - before.Y, 22.4f, 22.9f);   // along the nose (+Y): top speed in half a second (22.5 m; 22.75 stepped per frame)
            Assert.Equal(0f, after.X - before.X, 1);

            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2 });
            Run(40);
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimVelocity>(d).Linear);   // no axis ⇒ it braked to a hover
        }

        /// <summary>⭐ R-3007 — each type speeds up and brakes at ITS OWN acceleration: after a second of full forward an FPV drone
        /// (15 m/s²) flies at 15 m/s, a UH-60 (3 m/s²) at 3; keys released, each brakes at that rate to a hover and holds it.</summary>
        [Fact]
        public void R3007_ADroneAndAHelicopter_ChangeSpeed_AtTheirOwnAcceleration()
        {
            var drone = Drone(new Vector3(0, 0, 50), accel: 15f);
            var heli = Drone(new Vector3(0, 100, 50), accel: 3f);
            foreach (var e in new[] { drone, heli })
            {
                _w.SetComponent(e, new NavigationIntent { Mode = NavigationMode.Manual });
                _w.SetComponent(e, new ManualDrive { Forward = 1 });
            }
            Run(60);
            Assert.Equal(15f, _w.GetComponent<SimVelocity>(drone).Linear.Length(), 1);
            Assert.Equal(3f, _w.GetComponent<SimVelocity>(heli).Linear.Length(), 1);

            foreach (var e in new[] { drone, heli }) _w.SetComponent(e, new ManualDrive());
            Run(30);
            Assert.Equal(7.5f, _w.GetComponent<SimVelocity>(drone).Linear.Length(), 1);   // braking at 15 m/s²
            Assert.Equal(1.5f, _w.GetComponent<SimVelocity>(heli).Linear.Length(), 1);    // at 3 m/s²
            Run(60);
            var held = (_w.GetComponent<SimTransform>(drone).Position, _w.GetComponent<SimTransform>(heli).Position);
            Run(60);
            Assert.Equal(held, (_w.GetComponent<SimTransform>(drone).Position, _w.GetComponent<SimTransform>(heli).Position));   // the hover hold
        }

        /// <summary>⭐ B3 (slice 6, E5) — manual flight tilts as a route draws it: the nose down while it flies forward, rolled into a
        /// strafe to the left (left side down); keys released, it holds its hover where it is and levels out.</summary>
        [Fact]
        public void B3_AHelicopter_UnderManualDrive_TiltsAsItFlies_AndHoldsItsHover()
        {
            var d = Drone(new Vector3(0, 0, 50), accel: 60f);
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.SetComponent(d, new ManualDrive { Forward = 1 });
            Run(60);
            var (pitch, roll) = RotorcraftModel.TiltOf(_w.GetComponent<SimTransform>(d).Rotation);
            Assert.Equal(-RotorcraftModel.MaxPitchDeg * MathF.PI / 180f, pitch, 2);   // full forward: the nose fully down
            Assert.Equal(0f, roll, 2);
            Assert.Equal(0f, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 3);   // the tilt does not turn it

            _w.SetComponent(d, new ManualDrive { Lateral = 1 });
            Run(90);
            (pitch, roll) = RotorcraftModel.TiltOf(_w.GetComponent<SimTransform>(d).Rotation);
            Assert.Equal(-RotorcraftModel.MaxBankDeg * MathF.PI / 180f, roll, 2);   // strafing left: left side down
            Assert.Equal(0f, pitch, 2);

            _w.SetComponent(d, new ManualDrive());
            Run(60);
            var held = _w.GetComponent<SimTransform>(d).Position;
            Run(120);
            var tf = _w.GetComponent<SimTransform>(d);
            Assert.Equal(held, tf.Position);   // the hover hold: no drift
            (pitch, roll) = RotorcraftModel.TiltOf(tf.Rotation);
            Assert.Equal(0f, pitch, 3);
            Assert.Equal(0f, roll, 3);
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

        private (Entity heli, AirMotionSystem air, Routes routes) Heli(Vector3 at, bool onTheGround, CarrierDefDto? carrier = null)
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
            if (carrier != null) t.AddDescriptor(carrier);
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
    
        // ── ⭐⭐ Airborne insertion B-3 (docs/designs/airborne/DESIGN_Airborne_Insertion.md D-7, D-8) — Rope / Drop holds ──────────

        /// <summary>A terrain with one 12 m (four-storey) building over (190..210, −10..10).</summary>
        private void ARoofAt200()
        {
            const string wall = "\"walls\":[{\"from\":[0,0],\"to\":[20,0]},{\"from\":[20,0],\"to\":[20,20]},{\"from\":[20,20],\"to\":[0,20]},{\"from\":[0,20],\"to\":[0,0]}]";
            string storey = "{\"height\":3," + wall + "}";
            _w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            _w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,600,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"R\",\"building\":{\"footprint\":[[0,0],[20,0],[20,20],[0,20]]," +
                "\"storeys\":[" + storey + "," + storey + "," + storey + "," + storey + "]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[190,-10]}}]}"));
            Assert.Equal(12f, DescentRules.HighestSurface(Fdp.Toolkit.World.WorldQuery.Of(_w)!, 200f, 0f), 2);
        }

        private void Release(Entity heli, bool on)
        {
            var intent = _w.GetComponent<NavigationIntent>(heli);
            intent.ReleaseHold = on;
            _w.SetComponent(heli, intent);
        }

        /// <summary>⭐ B-3 — a Rope hold hovers its height (12 m by default) above the ROOF under the waypoint, not above the ground: over a
        /// 12 m building at 24 m. It reports <see cref="NavigationPhase.Roping"/>, holds until its Brain releases it, then flies on.
        /// ✅ Red-proof: AirborneHolds without the roof term ⇒ it hovers 12 m above the ground, ON the roof ⇒ red.</summary>
        [Fact]
        public void B3_ARopeHold_HoversItsHeightAboveTheRoof_ReportingRoping_UntilReleased()
        {
            ARoofAt200();
            var (heli, air, routes) = Heli(new Vector3(0, 0, 30), onTheGround: false);
            routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(200, 0, RouteWaypointKind.Pass, RouteHold.Rope), Wp(400, 0) });
            Order(heli);

            int frames = 0;
            while (_w.GetComponent<NavigationStatus>(heli).Phase != NavigationPhase.Roping && frames++ < 60 * 60) air.Execute(_w, Dt);
            var at = _w.GetComponent<SimTransform>(heli).Position;
            Assert.Equal(NavigationPhase.Roping, _w.GetComponent<NavigationStatus>(heli).Phase);
            Assert.True(Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(200, 0)) < 0.5f, $"at {at}");
            Assert.InRange(at.Z, 12f + AirMotionSystem.DefaultRopeHoverM - 0.3f, 12f + AirMotionSystem.DefaultRopeHoverM + 0.3f);
            Assert.Equal(1, _w.GetComponent<FlightState>(heli).Airborne);

            for (int i = 0; i < 60 * 10; i++) air.Execute(_w, Dt);                        // nobody released it: it hangs there
            Assert.Equal(NavigationPhase.Roping, _w.GetComponent<NavigationStatus>(heli).Phase);
            Assert.Equal(at, _w.GetComponent<SimTransform>(heli).Position);

            Release(heli, true);
            for (int i = 0; i < 60 * 60 && _w.GetComponent<NavigationStatus>(heli).Result != NavigationResult.Arrived; i++) air.Execute(_w, Dt);
            var end = _w.GetComponent<SimTransform>(heli).Position;
            Assert.True(Vector2.Distance(new Vector2(end.X, end.Y), new Vector2(400, 0)) < 0.5f, $"at {end}");
        }

        /// <summary>⭐ B-3 — a Rope hold higher above the roof than the carrier's rope is refused (warned) and flown through: a 30 m hover on
        /// a 20 m rope never reports Roping.</summary>
        [Fact]
        public void B3_ARopeHoldHigherThanItsRope_IsRefused_AndFlownThrough()
        {
            var (heli, air, routes) = Heli(new Vector3(0, 0, 30), onTheGround: false, new CarrierDefDto { RopeLengthM = 20f });
            routes.Waypoints.AddRange(new[] { Wp(0, 0), new NavRouteWaypoint(new Vector3(200, 0, 0), 0f, RouteWaypointKind.Pass, 30f, RouteHold.Rope, 0f, 0L), Wp(400, 0) });
            Order(heli);
            var seen = new System.Collections.Generic.HashSet<NavigationPhase>();
            for (int i = 0; i < 60 * 60 && _w.GetComponent<NavigationStatus>(heli).Result != NavigationResult.Arrived; i++)
            {
                air.Execute(_w, Dt);
                seen.Add(_w.GetComponent<NavigationStatus>(heli).Phase);
            }
            Assert.DoesNotContain(NavigationPhase.Roping, seen);
            Assert.Equal(NavigationResult.Arrived, _w.GetComponent<NavigationStatus>(heli).Result);
        }

        /// <summary>⭐ B-3 (D-8) — an Unload hold on an AIR waypoint hovering more than 3 m up is refused as a hover (warned) and LANDS
        /// instead: nobody steps down from 30 m any more (they used to stand beside the hull and be clamped to the ground).</summary>
        [Fact]
        public void B3_AnUnloadHoverAbove3m_LandsInstead()
        {
            var (heli, air, routes) = Heli(new Vector3(0, 0, 30), onTheGround: false);
            routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(200, 0, RouteWaypointKind.Pass, RouteHold.Unload), Wp(400, 0) });
            Order(heli);
            int frames = 0;
            while (_w.GetComponent<NavigationStatus>(heli).Phase != NavigationPhase.Unloading && frames++ < 60 * 60) air.Execute(_w, Dt);
            Assert.Equal(NavigationPhase.Unloading, _w.GetComponent<NavigationStatus>(heli).Phase);
            Assert.InRange(_w.GetComponent<SimTransform>(heli).Position.Z, RestZ - 0.01f, RestZ + 0.01f);   // on its gear
            Assert.Equal(0, _w.GetComponent<FlightState>(heli).Airborne);
        }

        /// <summary>⭐ B-3 (D-7) — a Drop waypoint is NOT a stop: the helicopter flies through it at speed and reports
        /// <see cref="NavigationPhase.Dropping"/> along the DZ leg until its Brain releases it, then plain Following to the route's end.</summary>
        [Fact]
        public void B3_ADropWaypoint_IsFlownThrough_ReportingDropping_UntilReleased()
        {
            var (heli, air, routes) = Heli(new Vector3(0, 0, 30), onTheGround: false);
            routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(300, 0, RouteWaypointKind.Pass, RouteHold.Drop), Wp(1200, 0) });
            Order(heli);
            float speedAtDz = -1f;
            NavigationPhase phaseAt600 = default, phaseAfter = default;
            bool released = false;
            for (int i = 0; i < 60 * 120 && _w.GetComponent<NavigationStatus>(heli).Result != NavigationResult.Arrived; i++)
            {
                float x0 = _w.GetComponent<SimTransform>(heli).Position.X;
                air.Execute(_w, Dt);
                float x = _w.GetComponent<SimTransform>(heli).Position.X;
                if (x0 < 300f && x >= 300f) speedAtDz = (x - x0) / Dt;
                if (!released && x >= 600f) { phaseAt600 = _w.GetComponent<NavigationStatus>(heli).Phase; Release(heli, true); released = true; }
                else if (released && x >= 650f && phaseAfter == default) phaseAfter = _w.GetComponent<NavigationStatus>(heli).Phase;
            }
            Assert.True(speedAtDz > 20f, $"it slowed to {speedAtDz} m/s at the DZ — a Drop is not a stop");
            Assert.Equal(NavigationPhase.Dropping, phaseAt600);
            Assert.Equal(NavigationPhase.Following, phaseAfter);
            Assert.Equal(NavigationResult.Arrived, _w.GetComponent<NavigationStatus>(heli).Result);
        }

        // ── ⭐⭐ CE-2184 (AT3, R-2021, DESIGN_Air_Operations §7c) — a killed aircraft FALLS and comes to rest ───────────────────────

        private void FlatGround()
        {
            _w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            _w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""{"type":"FeatureCollection","features":[]}"""));
        }

        /// <summary>Kills <paramref name="e"/> the way the product does: Health 0, and death ENDS the move (CE-3091: the
        /// dispatcher's OnExit stops the intent) — the reported phase is left as it was.</summary>
        private void Kill(Entity e)
        {
            _w.SetComponent(e, new Health { Current = 0, Max = 800 });
            _w.SetComponent(e, new NavigationIntent { Mode = NavigationMode.None });
        }

        /// <summary>Frames until <paramref name="e"/> is down (Airborne 0), the air model then the impact, as the Air module orders them.</summary>
        private int FramesToTheGround(Entity e, AirMotionSystem air, int max)
        {
            var impact = new DroneImpactSystem();
            for (int i = 1; i <= max; i++)
            {
                air.Execute(_w, Dt);
                impact.Execute(_w, Dt);
                if (_w.GetComponent<FlightState>(e).Airborne == 0) return i;
            }
            return -1;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2184</c> — a helicopter at 60 m killed mid-route falls (its model's dead branch: gravity, the speed it had) and
        /// reaches the ground within the free-fall time (± 10 %), then rests there: down, still, on the ground — a wreck, not a hover.
        /// </summary>
        [Fact]
        public void CE2184_AHelicopterKilledMidRoute_FallsToTheGround_InTheFreeFallTime_AndRestsThere()
        {
            FlatGround();
            var (heli, air, routes) = Heli(new Vector3(0, 0, 60), onTheGround: false);
            _w.SetComponent(heli, FlightParams.From(new FlightParamsDto { MaxSpeedMps = 40f, ClimbMps = 8f, HoverHeightM = 60f, TurnRadiusM = 60f }));
            routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(3000, 0) });
            Order(heli);
            for (int i = 0; i < 60 * 8; i++) air.Execute(_w, Dt);   // cruising east
            var at = _w.GetComponent<SimTransform>(heli).Position;
            Assert.True(_w.GetComponent<SimVelocity>(heli).Linear.X > 10f, "it is flying when it is killed");

            Kill(heli);
            float h = at.Z - FlightParams.From(new FlightParamsDto()).ContactRadiusM;
            float freeFall = MathF.Sqrt(2f * h / RotorcraftModel.Gravity);
            int frames = FramesToTheGround(heli, air, 60 * 30);
            Assert.True(frames > 0, "it never reached the ground (it hovers)");
            Assert.InRange(frames * Dt, 0.9f * freeFall, 1.1f * freeFall);

            for (int i = 0; i < 60; i++) { air.Execute(_w, Dt); new DroneImpactSystem().Execute(_w, Dt); }
            var rest = _w.GetComponent<SimTransform>(heli).Position;
            Assert.InRange(rest.Z, -0.01f, 1f);
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimVelocity>(heli).Linear);
            Assert.True(rest.X > at.X, "it fell forward, along its track");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2184</c> — a helicopter killed on its way DOWN to a landing (the reported phase is still Landing) meets the ground
        /// too. 🔴 Red-proof: the impact skipped every flyer in a ground phase, alive or dead ⇒ it fell through the ground.
        /// </summary>
        [Fact]
        public void CE2184_AHelicopterKilledWhileLanding_DoesNotFallThroughTheGround()
        {
            FlatGround();
            var (heli, air, _) = Heli(new Vector3(0, 0, 12), onTheGround: false);
            _w.SetComponent(heli, new NavigationStatus { Phase = NavigationPhase.Landing });
            Kill(heli);

            Assert.True(FramesToTheGround(heli, air, 60 * 5) > 0, "it fell through the ground");
            Assert.True(_w.GetComponent<SimTransform>(heli).Position.Z >= -0.01f);
        }

        /// <summary>⭐ <c>CE-2184</c> — a plane killed at 300 m falls along its track (ballistic: its speed kept, gravity) and rests where
        /// it lands, about speed × the fall time ahead.</summary>
        [Fact]
        public void CE2184_APlaneKilledAt300m_FallsAlongItsTrack()
        {
            FlatGround();
            var plane = Drone(new Vector3(0, 0, 300), LocomotionClass.FixedWing);
            _w.SetComponent(plane, new SimVelocity { Linear = new Vector3(100f, 0f, 0f) });
            Kill(plane);

            float freeFall = MathF.Sqrt(2f * 300f / RotorcraftModel.Gravity);
            int frames = FramesToTheGround(plane, _air, 60 * 30);
            Assert.InRange(frames * Dt, 0.9f * freeFall, 1.1f * freeFall);
            var rest = _w.GetComponent<SimTransform>(plane).Position;
            Assert.InRange(rest.X, 0.9f * 100f * freeFall, 1.1f * 100f * freeFall);
            Assert.InRange(rest.Y, -0.5f, 0.5f);
        }
}
}
