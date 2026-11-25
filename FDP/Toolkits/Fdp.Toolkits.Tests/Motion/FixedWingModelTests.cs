using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §6, A1/B2/B9) — <see cref="FixedWingModel"/>: a plane flies
    /// its route's <see cref="AirPath"/> (rounded to cruise² / (g·tan max bank)), banks into its turns, never flies slower than its stall
    /// speed, flies OVER a Ground waypoint (planes do not land in v1) and circles at the end; told a point, it turns to it and circles it.
    /// </summary>
    public sealed class FixedWingModelTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private const float Stall = 60f, Cruise = 150f, Bank = 60f;
        private readonly EntityRepository _w = new();

        private sealed class Routes : IRouteSource
        {
            public readonly List<NavRouteWaypoint> Waypoints = new();
            public bool TryGetTrajectory(EntityRepository repo, int routeNetworkId, out int trajectoryId) { trajectoryId = 0; return false; }
            public bool TryGetRoute(EntityRepository repo, int routeNetworkId, List<NavRouteWaypoint> into, out bool loop, out int version)
            {
                into.Clear(); into.AddRange(Waypoints); loop = false; version = 1;
                return into.Count > 0;
            }
        }

        private readonly Routes _routes = new();
        private readonly AirMotionSystem _air;

        public FixedWingModelTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<Locomotion>();
            _w.RegisterComponent<FlightParams>();
            _w.RegisterComponent<FlightState>();
            _w.RegisterComponent<NavigationIntent>();
            _w.RegisterComponent<NavigationStatus>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<ManualDrive>();
            _air = new AirMotionSystem { RouteSource = _routes };
        }

        public void Dispose() => _w.Dispose();

        private Entity Plane(Vector3 at, float speed)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });   // heading east
            _w.AddComponent(e, new SimVelocity { Linear = new Vector3(speed, 0f, 0f) });
            _w.AddComponent(e, new Locomotion { Class = LocomotionClass.FixedWing });
            _w.AddComponent(e, FlightParams.From(new FlightParamsDto
            {
                MaxSpeedMps = 200f, CruiseSpeedMps = Cruise, StallSpeedMps = Stall, MaxBankDeg = Bank, ClimbMps = 30f, HoverHeightM = 300f,
            }));
            _w.AddComponent(e, new FlightState { Airborne = 1 });
            _w.AddComponent(e, new NavigationIntent());
            _w.AddComponent(e, new NavigationStatus());
            _w.AddComponent(e, new Health { Current = 600, Max = 600 });
            _w.SetAuthority<SimTransform>(e, true);
            return e;
        }

        private static NavRouteWaypoint Wp(float x, float y, RouteWaypointKind kind = RouteWaypointKind.Pass)
            => new(new Vector3(x, y, 0f), 0f, kind, 300f, RouteHold.None, 0f, 0);

        private static float Roll(Quaternion q) => MathF.Asin(Math.Clamp(Vector3.Transform(Vector3.UnitY, q).Z, -1f, 1f));   // left side up +

        /// <summary>⭐ A1 / B2 — a route at cruise: it passes every corner within its turn radius (the radius it can fly at cruise), rolls
        /// INTO the left turn — steeply, a 1.3 km turn at 150 m/s wants its 60° — but never past its max bank, never slows under its stall
        /// speed, keeps 300 m over the ground, and circles at the end, still flying (Arrived). The floor when ASKED to fly slower: the
        /// point rail below.</summary>
        [Fact]
        public void A1_APlane_FliesItsRoute_BanksInTheTurns_AndNeverStalls()
        {
            float r = FlightParams.From(new FlightParamsDto { CruiseSpeedMps = Cruise, MaxBankDeg = Bank }).TurnRadius;
            Assert.InRange(r, 1250f, 1400f);   // 150 m/s at 60° ≈ 1.3 km (§6)
            _routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(8000, 0), Wp(8000, 8000), Wp(0, 12000) });
            var plane = Plane(new Vector3(0, 0, 300), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 1, RouteEntityId = 77 });

            float minSpeed = float.MaxValue, minRoll = 0f, minZ = float.MaxValue;
            var closest = new float[_routes.Waypoints.Count];
            Array.Fill(closest, float.MaxValue);
            int afterArrival = 0;
            for (int i = 0; i < 60 * 300 && afterArrival < 600; i++)
            {
                _air.Execute(_w, Dt);
                var tf = _w.GetComponent<SimTransform>(plane);
                var v = _w.GetComponent<SimVelocity>(plane).Linear;
                minSpeed = MathF.Min(minSpeed, new Vector2(v.X, v.Y).Length());
                minRoll = MathF.Min(minRoll, Roll(tf.Rotation));
                minZ = MathF.Min(minZ, tf.Position.Z);
                for (int k = 0; k < closest.Length; k++)
                    closest[k] = MathF.Min(closest[k], Vector2.Distance(new Vector2(tf.Position.X, tf.Position.Y),
                        new Vector2(_routes.Waypoints[k].Position.X, _routes.Waypoints[k].Position.Y)));
                if (_w.GetComponent<NavigationStatus>(plane).Result == NavigationResult.Arrived) afterArrival++;
            }

            Assert.Equal(NavigationResult.Arrived, _w.GetComponent<NavigationStatus>(plane).Result);
            // ⚠ measured as displacement per frame: across a bend of the path's 15°-sampled arcs a frame's chord is ≤ 1 % shorter
            //   than the distance flown along it, so the floor is checked to 1 % (the model's own speed is clamped at the stall)
            Assert.True(minSpeed >= Stall * 0.99f, $"flew at {minSpeed} m/s, under its {Stall} m/s stall");
            Assert.True(minRoll < -40f * MathF.PI / 180f, $"rolled only {minRoll * 180f / MathF.PI:F1}° into the left turn");
            Assert.True(minRoll >= -Bank * MathF.PI / 180f - 1e-3f, "never past its max bank");
            Assert.InRange(minZ, 299f, 301f);
            for (int k = 1; k < closest.Length - 1; k++) Assert.True(closest[k] <= r, $"corner {k} passed at {closest[k]:F0} m");
            var end = _w.GetComponent<SimVelocity>(plane).Linear;
            Assert.True(new Vector2(end.X, end.Y).Length() >= Stall, "it circles at the end, still flying");
        }

        /// <summary>⭐⭐ CE-11057 — a plane given a route while standing (velocity 0, as a scenario places it) flies its CRUISE from the order's first
        /// frame even when that frame is a microsecond long (the master clock's resume frame): the velocity a too-short frame cannot measure is
        /// kept, so when the brain's order lands a frame later the route still sees a plane at rest and snaps it to cruise. 📌 fl-napalm-strike live: a 3 µs frame wrote
        /// 4.9 m/s, the route read it as "already flying", clamped the A-10 to its 60 m/s stall and it took ~20 s to reach cruise.
        /// 🔴 Red-proof: <c>StepVelocity.Of</c> divides by max(dt, 1e-4) again ⇒ ~60 m/s after the first second.</summary>
        [Fact]
        public void CE11057_APlaneOrderedFromRest_FliesCruiseAtOnce_EvenAfterAMicrosecondFrame()
        {
            _routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(20000, 0) });
            var plane = Plane(new Vector3(-3500, 300, 300), 0f);
            _air.Execute(_w, 3.3e-6f);                                    // the resume frame: no order yet — it flies straight on
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 1, RouteEntityId = 77 });   // the brain's order lands
            for (int i = 0; i < 60; i++) _air.Execute(_w, Dt);           // one second
            var v = _w.GetComponent<SimVelocity>(plane).Linear;
            Assert.InRange(new Vector2(v.X, v.Y).Length(), Cruise - 1f, Cruise + 1f);
            Assert.InRange(Vector2.Distance(new Vector2(-3500, 300), new Vector2(_w.GetComponent<SimTransform>(plane).Position.X, _w.GetComponent<SimTransform>(plane).Position.Y)),
                           Cruise - 5f, Cruise + 5f);   // it flew ~150 m in the second, not ~60
        }

        /// <summary>⭐ CE-10006 (AC-9, R-10030) — a route broken off mid-way (a transit's missile defence: a point move, then the SAME route
        /// re-issued with a new intent) RESUMES at the first waypoint not yet passed, from where the plane is — ⛔ never back to the first
        /// one; a FRESH route (another route entity) still starts at its first waypoint. ✅ Red-proof (mutant, run): the resume dropped
        /// (resumeAt forced 0) ⇒ the re-issued path's first waypoint is (0, 0) again ⇒ red.</summary>
        [Fact]
        public void CE10006_ARouteBrokenOffMidway_ResumesAtTheWaypointNotYetPassed()
        {
            _routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(8000, 0), Wp(8000, 8000), Wp(0, 12000) });
            var plane = Plane(new Vector3(0, 0, 300), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 1, RouteEntityId = 77 });
            for (int i = 0; i < 60 * 70; i++) _air.Execute(_w, Dt);   // past (8000, 0), short of (8000, 8000)
            var at = _w.GetComponent<SimTransform>(plane).Position;
            Assert.True(at.Y > 1500f && at.Y < 6500f, $"mid-way up the second leg: {at}");
            // the defence: a point move off the route, a few seconds
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 2, FinalDestination = at + new Vector3(-3000f, 0f, 0f) });
            for (int i = 0; i < 60 * 5; i++) _air.Execute(_w, Dt);
            // the same route, re-issued
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 3, RouteEntityId = 77 });
            _air.Execute(_w, Dt);
            var path = ((Fdp.ModuleHost.Abstractions.ISimulationView)_w).GetManagedComponentRO<AirPath>(plane);
            Assert.Equal(3u, path.IntentId);
            float nearFirst = float.MaxValue, nearSecond = float.MaxValue;
            foreach (var p in path.Points)
            {
                nearFirst = MathF.Min(nearFirst, new Vector2(p.X, p.Y).Length());
                nearSecond = MathF.Min(nearSecond, Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(8000f, 0f)));
            }
            Assert.True(nearFirst > 2000f, $"the resumed path goes back to the first waypoint ({nearFirst:F0} m)");
            Assert.True(nearSecond > 1000f, $"the resumed path goes back to the passed second waypoint ({nearSecond:F0} m)");
            var last = path.Points[path.Points.Length - 1];
            Assert.True(Vector2.Distance(new Vector2(last.X, last.Y), new Vector2(0f, 12000f)) < 50f, "it still ends at the last waypoint");

            // the helpers: a loop rotates (the same circuit), a route drops what is passed; a stop keeps its ROUTE waypoint number
            var list = new List<NavRouteWaypoint>(_routes.Waypoints);
            AirRoute.ResumeAt(list, 2, loop: true);
            Assert.Equal(new[] { 8000f, 0f, 0f, 8000f }, list.ConvertAll(w => w.Position.X).ToArray());
            var open = new List<NavRouteWaypoint>(_routes.Waypoints);
            AirRoute.ResumeAt(open, 2, loop: false);
            Assert.Equal(2, open.Count);
            var stops = new AirPath { Stops = System.Collections.Immutable.ImmutableArray.Create(new AirStop { Waypoint = 1 }) };
            Assert.Equal(3, AirRoute.RenumberStops(stops, 2, 4).Stops[0].Waypoint);
            Assert.Equal(0, AirRoute.RenumberStops(stops, 3, 4).Stops[0].Waypoint);
        }

        /// <summary>⭐ B9 — a Ground waypoint on a plane's route is flown OVER (planes do not land in v1): it never comes down.</summary>
        [Fact]
        public void B9_AGroundWaypoint_IsFlownOver_NotLandedOn()
        {
            _routes.Waypoints.AddRange(new[] { Wp(0, 0), Wp(6000, 0, RouteWaypointKind.Ground), Wp(12000, 0) });
            var plane = Plane(new Vector3(0, 0, 300), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.FollowRoute, IntentId = 1, RouteEntityId = 77 });
            float minZ = float.MaxValue, maxX = 0f;
            for (int i = 0; i < 60 * 120 && _w.GetComponent<NavigationStatus>(plane).Result != NavigationResult.Arrived; i++)
            {
                _air.Execute(_w, Dt);
                minZ = MathF.Min(minZ, _w.GetComponent<SimTransform>(plane).Position.Z);
                maxX = MathF.Max(maxX, _w.GetComponent<SimTransform>(plane).Position.X);
            }
            Assert.Equal(NavigationResult.Arrived, _w.GetComponent<NavigationStatus>(plane).Result);
            Assert.True(maxX > 11000f, "it flew past the Ground waypoint to the end");
            Assert.InRange(minZ, 299f, 301f);
            Assert.Equal(1, _w.GetComponent<FlightState>(plane).Airborne);
        }

        /// <summary>⭐ A1 — told a point behind it and asked for 10 m/s, it never flies under its 60 m/s stall, turns to the point and
        /// circles it once there.</summary>
        /// <summary>
        /// ⭐ <c>CE-5046</c> (Air Operations §4 "orbit (Hold)"; the Stechovice A-10 flew 28 km off after its strike) — a plane whose order ENDED
        /// (Mode None, the ended intent's id) holds: it stays within its turn circle of where the order ended, at its height, above stall,
        /// with no jump; a second end anchors a new circle where it then is. A plane that never had an order (IntentId 0) still flies straight.
        /// 🔴 Red-proof: the None branch back to Straight ⇒ the plane leaves the circle ⇒ red.
        /// </summary>
        [Fact]
        public void CE5046_APlaneWhoseOrderEnded_HoldsWhereItIs_ANeverOrderedOneFliesStraight()
        {
            var plane = Plane(new Vector3(0, 0, 500), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.None, IntentId = 7 });
            float r = _w.GetComponent<FlightParams>(plane).TurnRadius;
            var last = _w.GetComponent<SimTransform>(plane).Position;
            float maxStep = 0f, maxOff = 0f, minSpeed = float.MaxValue;
            for (int i = 0; i < 60 * 90; i++)
            {
                _air.Execute(_w, Dt);
                var now = _w.GetComponent<SimTransform>(plane).Position;
                maxStep = MathF.Max(maxStep, Vector3.Distance(now, last)); last = now;
                maxOff = MathF.Max(maxOff, new Vector2(now.X, now.Y).Length());
                var v = _w.GetComponent<SimVelocity>(plane).Linear;
                minSpeed = MathF.Min(minSpeed, new Vector2(v.X, v.Y).Length());
            }
            Assert.True(maxOff <= 2f * r + 1f, $"it left its circle: {maxOff:0} m from where the order ended (turn radius {r:0} m)");
            Assert.True(maxStep <= 200f * Dt * 1.5f, $"a jump of {maxStep:0.0} m in one frame");
            Assert.True(minSpeed >= Stall * 0.99f, $"flew at {minSpeed} m/s, under its {Stall} m/s stall");
            Assert.Equal(500f, last.Z, 1);

            // a SECOND end, after a point order took it 6 km west: a new circle where it then is (never back to the first one)
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 8, FinalDestination = new Vector3(-6000, 0, 500) });
            for (int i = 0; i < 60 * 60; i++) _air.Execute(_w, Dt);
            var there = _w.GetComponent<SimTransform>(plane).Position;
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.None, IntentId = 9 });
            for (int i = 0; i < 60 * 30; i++) _air.Execute(_w, Dt);
            var held = _w.GetComponent<SimTransform>(plane).Position;
            Assert.True(Vector2.Distance(new Vector2(held.X, held.Y), new Vector2(there.X, there.Y)) <= 2f * r + 1f, $"the second hold is not where the second order ended: {there} → {held}");

            // never ordered: straight and level, as B9 says
            var fresh = Plane(new Vector3(0, 5000, 500), Cruise);
            for (int i = 0; i < 60 * 20; i++) _air.Execute(_w, Dt);
            var went = _w.GetComponent<SimTransform>(fresh).Position;
            Assert.True(went.X > Cruise * 15f && MathF.Abs(went.Y - 5000f) < 1f, $"a never-ordered plane flies straight east: {went}");
        }

        [Fact]
        public void A1_APlane_ToldAPoint_TurnsToIt_AndCirclesIt()
        {
            var plane = Plane(new Vector3(0, 0, 500), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 2, FinalDestination = new Vector3(-6000, 0, 500), TargetSpeed = 10f });
            float minSpeed = float.MaxValue; bool arrived = false;
            for (int i = 0; i < 60 * 120; i++)
            {
                _air.Execute(_w, Dt);
                var v = _w.GetComponent<SimVelocity>(plane).Linear;
                minSpeed = MathF.Min(minSpeed, new Vector2(v.X, v.Y).Length());
                arrived |= _w.GetComponent<NavigationStatus>(plane).Result == NavigationResult.Arrived;
            }
            Assert.True(arrived, $"never reached the point: at {_w.GetComponent<SimTransform>(plane).Position}");
            Assert.True(minSpeed >= Stall * 0.99f, $"flew at {minSpeed} m/s, under its {Stall} m/s stall");   // 1 %: a turning frame's chord
            var p = _w.GetComponent<SimTransform>(plane).Position;
            Assert.True(Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(-6000, 0)) < 3000f, $"it stays near the point: {p}");
        }

        private static float Pitch(Quaternion q) => MathF.Asin(Math.Clamp(Vector3.Transform(Vector3.UnitX, q).Z, -1f, 1f));   // nose up +

        /// <summary>⭐ B3 (slice 6, E5) — the mouse-aim pilot: told to look 90° left (ManualDrive.Yaw), the plane banks ITSELF into the
        /// turn — steeply, as far as its max bank — turns onto the look, and levels its wings once the nose is on it; it holds its height
        /// with Vertical at 0 and its cruise with the throttle at 0.</summary>
        [Fact]
        public void B3_TheMouseAimPilot_TurnsToTheLook_BanksItself_AndLevelsOut()
        {
            var plane = Plane(new Vector3(0, 0, 500), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.AddComponent(plane, new ManualDrive { Yaw = MathF.PI / 2 });
            float minRoll = 0f;
            for (int i = 0; i < 60 * 30; i++)
            {
                _air.Execute(_w, Dt);
                var q = _w.GetComponent<SimTransform>(plane).Rotation;
                minRoll = MathF.Min(minRoll, Roll(q));
                Assert.True(RotorcraftModel.YawOf(q) <= MathF.PI / 2 + 1e-3f, $"overshot the look: {RotorcraftModel.YawOf(q) * 180f / MathF.PI:F1}°");
            }
            var tf = _w.GetComponent<SimTransform>(plane);
            Assert.True(minRoll < -50f * MathF.PI / 180f, $"rolled only {minRoll * 180f / MathF.PI:F1}° into the left turn");
            Assert.True(minRoll >= -Bank * MathF.PI / 180f - 1e-3f, "never past its max bank");
            Assert.Equal(MathF.PI / 2, RotorcraftModel.YawOf(tf.Rotation), 2);       // on the look
            Assert.InRange(Roll(tf.Rotation), -1f * MathF.PI / 180f, 1f * MathF.PI / 180f);   // wings level again
            Assert.Equal(500f, tf.Position.Z, 1);
            Assert.Equal(Cruise, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);   // throttle 0 keeps the speed (R-3007)
        }

        /// <summary>⭐ B3 — it cannot stall: throttle fully back and the nose held up for a minute, it slows to its stall speed and no
        /// further, climbing no faster than its climb rate; throttle forward, it speeds up to its top speed.</summary>
        [Fact]
        public void B3_ThrottleBack_AndNoseUp_NeverStalls()
        {
            var plane = Plane(new Vector3(0, 0, 500), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.AddComponent(plane, new ManualDrive { Yaw = 0f, Forward = -1f, Vertical = 1f });
            float minSpeed = float.MaxValue, maxClimb = 0f, maxPitch = 0f;
            for (int i = 0; i < 60 * 60; i++)
            {
                _air.Execute(_w, Dt);
                var v = _w.GetComponent<SimVelocity>(plane).Linear;
                minSpeed = MathF.Min(minSpeed, v.Length());
                maxClimb = MathF.Max(maxClimb, v.Z);
                maxPitch = MathF.Max(maxPitch, Pitch(_w.GetComponent<SimTransform>(plane).Rotation));
            }
            Assert.True(minSpeed >= Stall - 0.01f, $"flew at {minSpeed} m/s, under its {Stall} m/s stall");
            Assert.Equal(Stall, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);
            Assert.True(maxClimb <= 30f + 0.01f, $"climbed at {maxClimb} m/s, over its 30 m/s");
            Assert.True(maxPitch > 15f * MathF.PI / 180f, $"the nose came up only {maxPitch * 180f / MathF.PI:F1}°");
            Assert.True(_w.GetComponent<SimTransform>(plane).Position.Z > 1500f, "it climbed");

            _w.SetComponent(plane, new ManualDrive { Yaw = 0f, Forward = 1f });
            for (int i = 0; i < 60 * 60; i++) _air.Execute(_w, Dt);
            Assert.Equal(200f, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);
        }

        /// <summary>⭐ R-3007 — the throttle is HELD: W speeds the plane up at its acceleration, released it keeps the speed it reached
        /// (any speed between stall and top), S slows it — never under the stall speed.</summary>
        [Fact]
        public void R3007_TheThrottle_IsHeld_AnySpeedBetweenStallAndTop()
        {
            var plane = Plane(new Vector3(0, 0, 500), Cruise);
            _w.SetComponent(plane, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.AddComponent(plane, new ManualDrive { Forward = 1f });
            for (int i = 0; i < 60 * 4; i++) _air.Execute(_w, Dt);
            Assert.Equal(Cruise + 4f * FixedWingModel.AccelMps2, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);   // 150 → 170

            _w.SetComponent(plane, new ManualDrive());
            for (int i = 0; i < 60 * 20; i++) _air.Execute(_w, Dt);
            Assert.Equal(170f, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);   // released: it keeps 170

            _w.SetComponent(plane, new ManualDrive { Forward = -1f });
            for (int i = 0; i < 60 * 10; i++) _air.Execute(_w, Dt);
            Assert.Equal(120f, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);   // 10 s at 5 m/s²
            _w.SetComponent(plane, new ManualDrive { Forward = -0.5f });
            for (int i = 0; i < 60 * 60; i++) _air.Execute(_w, Dt);
            Assert.Equal(Stall, _w.GetComponent<SimVelocity>(plane).Linear.Length(), 1);   // half the push, still floored at the stall
        }

        // ── ⭐⭐ CE-10001 (air lane S1 — docs/designs/air-attack/DESIGN_Air_Combat.md AC-1, AC-3, AC-4) ──────────────────────────────

        /// <summary>A terrain made of a function (the rails' ground); everything above the ground is open air.</summary>
        private sealed class FnWorld : Fdp.Toolkit.World.IWorldQuery
        {
            private readonly Func<float, float, float> _g;
            public FnWorld(Func<float, float, float> g) => _g = g;
            public float GroundHeightAt(float x, float y) => _g(x, y);
            public float SurfaceZ(float x, float y, float zHint) => _g(x, y);
            public IReadOnlyList<float> SurfacesAt(float x, float y, out int groundIndex) { groundIndex = 0; return new[] { _g(x, y) }; }
            public float ResolveLevel(float x, float y, int level) => _g(x, y);
            public bool TryStandAt(float x, float y, float zHint, out float z) { z = _g(x, y); return true; }
            public bool SightBlocked(Vector3 from, Vector3 to) => false;
            public void Trace(Vector3 from, Vector3 to, Fdp.Toolkit.World.TracePurpose purpose, List<Fdp.Toolkit.World.TraceCrossing> into) { }
        }

        /// <summary>The F-16's airframe (§5a), as the catalogue states it.</summary>
        private static FlightParamsDto F16 => new()
        {
            MaxSpeedMps = 400f, CruiseSpeedMps = 220f, StallSpeedMps = 70f, MaxBankDeg = 60f, ClimbMps = 50f, HoverHeightM = 300f,
            AccelMps2 = 12f, MaxG = 9f, CornerSpeedMps = 170f, DragK = 0.203f, RollRateDeg = 240f,
        };

        private Entity Jet(Vector3 at, Quaternion rot, Vector3 velocity)
        {
            var e = Plane(at, 0f);
            _w.SetComponent(e, new SimTransform { Position = at, Rotation = rot });
            _w.SetComponent(e, new SimVelocity { Linear = velocity });
            _w.SetComponent(e, FlightParams.From(F16));
            return e;
        }

        /// <summary>Steps one jet through the model with a terrain (AirMotionSystem's frame, with a ground).</summary>
        private readonly FixedWingModel _model = new();

        private void Fly(Entity e, Fdp.Toolkit.World.IWorldQuery world, int frames, Action<int>? each = null)
        {
            var model = _model;
            for (int i = 0; i < frames; i++)
            {
                var intent = _w.GetComponent<NavigationIntent>(e);
                var frame = new MotionFrame(_w, e, ref _w.GetComponentRW<SimTransform>(e), ref _w.GetComponentRW<SimVelocity>(e)) { Terrain = world };
                model.Autonomous(ref frame, in intent, Dt);
                each?.Invoke(i);
            }
        }

        /// <summary>⭐ AC-3 — the integrator's level turn: asked for 6.5 g at full throttle from 220 m/s, the F-16 SUSTAINS it (its thrust
        /// balances its parasite + induced drag there, §5a), banks acos(1/6.5) ≈ 81°, and holds its height (the lift is net of gravity).
        /// ✅ Red-proof: induced drag × 2 (DragK 0.406) ⇒ the speed bleeds below 190 m/s ⇒ red.</summary>
        [Fact]
        public void AC3_TheF16_Sustains6p5gAt220_BankingAcosOneOverN()
        {
            var l = FighterLimits.Of(FlightParams.From(F16));
            var b = new FlightBody { Pos = new Vector3(0, 0, 3000), Vel = new Vector3(220f, 0, 0), Rot = Quaternion.Identity };
            float n = 0f, minV = float.MaxValue, maxV = 0f;
            for (int i = 0; i < 60 * 30; i++)
            {
                var f = Vector3.Normalize(b.Vel);
                var left = Vector3.Normalize(new Vector3(-f.Y, f.X, 0f));   // always asked to turn 90° left: the pull is the g limit
                n = Steer3D.Step(ref b, left, 6.5f, 1f, in l, Dt);
                if (i > 60 * 5) { minV = MathF.Min(minV, b.Vel.Length()); maxV = MathF.Max(maxV, b.Vel.Length()); }
            }
            Assert.Equal(6.5f, n, 1);
            Assert.True(minV > 205f && maxV < 235f, $"speed {minV:F0}…{maxV:F0} m/s in a 6.5 g turn at full throttle — the §5a balance says ~220");
            float bank = MathF.Acos(Math.Clamp(Vector3.Dot(Steer3D.Up(b.Rot), Vector3.UnitZ), -1f, 1f)) * 180f / MathF.PI;
            Assert.InRange(bank, 78f, 84f);
            Assert.InRange(b.Pos.Z, 2750f, 3250f);   // the roll-in zooms a little (the pull starts before the bank is complete)
        }

        /// <summary>⭐ AC-3 — no Euler readback, no ±25° clamp: pulled straight up and over the top, the attitude stays a valid rotation, the
        /// nose passes the vertical and comes down the other side (a loop), with no NaN anywhere.</summary>
        [Fact]
        public void AC3_ALoop_PassesTheVertical_WithNoGimbalProblem()
        {
            var l = FighterLimits.Of(FlightParams.From(F16));
            var b = new FlightBody { Pos = new Vector3(0, 0, 3000), Vel = new Vector3(350f, 0, 0), Rot = Quaternion.Identity };
            bool vertical = false, inverted = false;
            for (int i = 0; i < 60 * 20; i++)
            {
                var f = Vector3.Normalize(b.Vel);
                var up = Steer3D.Up(b.Rot);
                Steer3D.Step(ref b, Vector3.Normalize(f + up), 9f, 1f, in l, Dt);   // keep pulling through the canopy: a loop
                Assert.False(float.IsNaN(b.Pos.X) || float.IsNaN(b.Rot.W));
                if (f.Z > 0.98f) vertical = true;
                if (vertical && Steer3D.Up(b.Rot).Z < -0.9f) inverted = true;
            }
            Assert.True(vertical, "the nose never reached the vertical");
            Assert.True(inverted, "it never came over the top inverted");
            Assert.Equal(1f, b.Rot.Length(), 3);
        }

        /// <summary>⭐ AC-4 (`ac-gcas-inverted`'s rail) — INVERTED, 60° nose-down at 400 m/s from 3 000 m, the steer still pressing at the ground:
        /// GCAS takes the stick (rolls upright, pulls 9 g), never lets it below its 150 m floor's neighbourhood, and gives it back climbing.
        /// ✅ Red-proof: the reaction-free prediction off (GcasHolds returns false) ⇒ it flies into the ground ⇒ red.</summary>
        [Fact]
        public void AC4_GCAS_RecoversAnInverted60DegreeDive()
        {
            _w.RegisterComponent<AirCombatSteer>();
            var dive = Vector3.Normalize(new Vector3(MathF.Cos(MathF.PI / 3f), 0f, -MathF.Sin(MathF.PI / 3f)));
            var rot = Steer3D.FromBasis(dive, -Steer3D.WingsLevelUp(dive));   // inverted
            var jet = Jet(new Vector3(0, 0, 3000), rot, dive * 400f);
            _w.SetComponent(jet, new NavigationIntent { Mode = NavigationMode.AirCombat, IntentId = 1 });
            _w.AddComponent(jet, new AirCombatSteer { Manoeuvre = Manoeuvre.PurePursuit, Point = new Vector3(5000f, 0f, 0f), Throttle = 1f });
            var flat = new FnWorld((_, _) => 0f);
            float minZ = float.MaxValue;
            Fly(jet, flat, 60 * 40, _ => minZ = MathF.Min(minZ, _w.GetComponent<SimTransform>(jet).Position.Z));
            var pilot = _w.GetComponent<FighterPilotState>(jet);
            Assert.True(pilot.GcasCount >= 1, "GCAS never took the stick");
            Assert.True(minZ > Gcas.MinFloorM, $"it went down to {minZ:F0} m");
            Assert.Equal(pilot.MinAgl, minZ, 0);
        }

        /// <summary>⭐ AC-4 (`ac-gcas-steep-ridge`'s rail) — at 150 m toward a ridge rising 1:1 (no 30° climb clears it): GCAS TURNS AWAY
        /// (a climbing turn) and the jet never touches the ground. ✅ Red-proof: the straight recovery only (no turn) ⇒ it flies into the wall.</summary>
        [Fact]
        public void AC4_GCAS_TurnsAwayFromARidgeTooSteepToClimb()
        {
            var ridge = new FnWorld((x, _) => x < 3000f ? 0f : MathF.Min(4000f, x - 3000f));
            var jet = Jet(new Vector3(0, 0, 150), Quaternion.Identity, new Vector3(220f, 0, 0));
            float minAgl = float.MaxValue; bool turned = false;
            Fly(jet, ridge, 60 * 60, _ =>
            {
                var at = _w.GetComponent<SimTransform>(jet).Position;
                minAgl = MathF.Min(minAgl, at.Z - ridge.GroundHeightAt(at.X, at.Y));
                if (_w.GetComponent<FighterPilotState>(jet).GcasTurn != 0) turned = true;
            });
            Assert.True(_w.GetComponent<FighterPilotState>(jet).GcasCount >= 1, "GCAS never took the stick");
            Assert.True(turned, "it never turned away");
            Assert.True(minAgl > 0f, $"it hit the ridge (lowest {minAgl:F0} m)");
        }

        /// <summary>⭐ AC-4 — the floor follows the order: a point at 50 m over flat ground (a gun run's height) does NOT trip GCAS (floor 30 m);
        /// flying level at 150 m with no order does not either.</summary>
        [Fact]
        public void AC4_AGunRunAt50m_DoesNotTripGcas()
        {
            var jet = Jet(new Vector3(0, 0, 50), Quaternion.Identity, new Vector3(200f, 0, 0));
            _w.SetComponent(jet, new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 1, FinalDestination = new Vector3(20000f, 0f, 50f) });
            Fly(jet, new FnWorld((_, _) => 0f), 60 * 20);
            Assert.Equal(0, _w.GetComponent<FighterPilotState>(jet).GcasCount);
            Assert.Equal(30f, Gcas.FloorFor(50f));
            Assert.Equal(8f, Gcas.FloorFor(10f));     // RS-6's 10 m radar escape keeps working
            Assert.Equal(150f, Gcas.FloorFor(300f));
        }

        /// <summary>⭐ AC-1 — a cruise leg (300 m ordered) over hills flies at ONE altitude, the highest ground under it + 300; the climb to a
        /// higher leg starts before the leg. ✅ Red-proof: climbSlope 0 (the old per-sample AGL) ⇒ the leg's heights vary with the hills ⇒ red.</summary>
        [Fact]
        public void AC1_ACruiseLeg_HoldsOneAltitude_AboveItsHighestGround()
        {
            Func<float, float, float> hills = (x, y) => 100f + 80f * MathF.Sin(x / 300f) + (x > 6000f ? 400f : 0f);
            var wps = new List<NavRouteWaypoint>
            {
                new(new Vector3(0, 0, 0), 0f, RouteWaypointKind.Pass, 300f, RouteHold.None, 0f, 0),
                new(new Vector3(5000, 0, 0), 0f, RouteWaypointKind.Pass, 300f, RouteHold.None, 0f, 0),
                new(new Vector3(9000, 0, 0), 0f, RouteWaypointKind.Pass, 300f, RouteHold.None, 0f, 0),
            };
            var path = AirRoute.Compile(new Vector3(0, 0, 400), wps, false, 1000f, 300f, hills, climbSlope: 50f / 220f);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int j = 0; j < path.Points.Length; j++)
            {
                var p = path.Points[j];
                Assert.True(p.Z >= hills(p.X, p.Y) + 299f, $"below 300 m at x={p.X:F0}");
                if (p.X > 200f && p.X < 3000f) { lo = MathF.Min(lo, p.Z); hi = MathF.Max(hi, p.Z); }
            }
            Assert.True(hi - lo < 1f, $"the first leg's altitude varies {lo:F0}…{hi:F0} m");
            float before = AirRoute.Sample(path, 4800f).Point.Z;   // the second leg reaches 880 m (the 400 m step at 6 km): the climb starts before 5 km
            Assert.True(before > lo + 50f, $"no early climb: {before:F0} m at 4.8 km");
        }

        /// <summary>⭐ AC-3 / AC-4 (handoff §3) — a steady fight frame allocates NOTHING: the integrator, the GCAS prediction and the report.</summary>
        [Fact]
        public void AC3_ASteadyFightFrame_AllocatesNothing()
        {
            _w.RegisterComponent<AirCombatSteer>();
            var jet = Jet(new Vector3(0, 0, 400), Quaternion.Identity, new Vector3(220f, 0, 0));
            _w.SetComponent(jet, new NavigationIntent { Mode = NavigationMode.AirCombat, IntentId = 1 });
            _w.AddComponent(jet, new AirCombatSteer { Manoeuvre = Manoeuvre.PurePursuit, Point = new Vector3(0f, 8000f, 2000f), Throttle = 1f });
            var hills = new FnWorld((x, y) => 50f * MathF.Sin(x / 500f));
            Fly(jet, hills, 120);   // warm: the report is added, every path is jitted
            long before = GC.GetAllocatedBytesForCurrentThread();
            Fly(jet, hills, 60);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐ CE-10002 (AC-9a, AC-9) — the LIVE threat and the reflex: a homing round aimed at the jet is counted in its
        /// <see cref="FighterPilotState"/> (bearing, seeker, closure, time to impact); far off the jet flies its order on; inside 2.5 s it
        /// breaks ACROSS the missile's path whatever it was told. ✅ Red-proof: the reflex's threshold at 0 s ⇒ the jet flies straight on ⇒ red.</summary>
        [Fact]
        public void AC9_TheLiveThreat_IsReported_AndInside2p5sTheJetBreaks()
        {
            _w.RegisterComponent<Fdp.Toolkit.Combat.Components.GuidedRound>();
            var jet = Jet(new Vector3(0, 0, 3000), Quaternion.Identity, new Vector3(220f, 0, 0));
            var round = _w.CreateEntity();
            _w.AddComponent(round, new SimTransform { Position = new Vector3(6000, 0, 3000), Rotation = Quaternion.Identity });
            _w.AddComponent(round, new SimVelocity { Linear = new Vector3(-800f, 0, 0) });
            _w.AddComponent(round, new Fdp.Toolkit.Combat.Components.GuidedRound { Guidance = GuidanceKind.InfraRed, Target = jet, SpeedMps = 800f });
            var flat = new FnWorld((_, _) => 0f);
            Fly(jet, flat, 1);
            var p = _w.GetComponent<FighterPilotState>(jet);
            Assert.Equal(1, p.Inbound);
            Assert.Equal(GuidanceKind.InfraRed, p.ThreatSeeker);
            Assert.InRange(p.ThreatBearingDeg, 85f, 95f);          // from the east
            Assert.InRange(p.ThreatTtiS, 5.5f, 6.5f);              // 6 km at 1 020 m/s closing
            Assert.True(MathF.Abs(_w.GetComponent<SimVelocity>(jet).Linear.Y) < 1f, "it broke 6 s out");

            _w.SetComponent(round, new SimTransform { Position = new Vector3(1500, 0, 3000), Rotation = Quaternion.Identity });   // 1.5 s to go
            Fly(jet, flat, 60);   // a second: the roll in (240°/s), then 9 g across
            Assert.True(MathF.Abs(_w.GetComponent<SimVelocity>(jet).Linear.Y) > 40f, $"no last-ditch break inside 2.5 s (vy {_w.GetComponent<SimVelocity>(jet).Linear.Y:F0})");

            _w.DestroyEntity(round);
            Fly(jet, flat, 1);
            Assert.Equal(0, _w.GetComponent<FighterPilotState>(jet).Inbound);
        }
    }
}
