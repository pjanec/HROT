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
            public bool TryGetTrajectory(EntityRepository repo, long routeNetworkId, out int trajectoryId) { trajectoryId = 0; return false; }
            public bool TryGetRoute(EntityRepository repo, long routeNetworkId, List<NavRouteWaypoint> into, out bool loop, out int version)
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
            => new(new Vector3(x, y, 0f), 0f, kind, 300f, RouteHold.None, 0f, 0L);

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
    }
}
