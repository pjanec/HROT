using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ M4 (D-D) — <see cref="GuidedRoundSystem"/> steers a guided round on the Muscle. The velocity is integrated here by hand
    /// (production: <c>LinearKinematicsSystem</c>). 📄 <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3, §7.
    /// </summary>
    public sealed class GuidedRoundSystemTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _w = new();
        private readonly GuidedRoundSystem _sys = new();

        public GuidedRoundSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<BallisticProjectile>();
            _w.RegisterComponent<GuidedRound>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterEvent<Fdp.Toolkit.Combat.Contracts.DetonationNotification>();   // ⭐ CE-3198 — the overfly fuze publishes it
        }

        public void Dispose() => _w.Dispose();

        private Entity Unit(Vector3 at, float hull = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            if (hull > 0f) _w.AddComponent(e, new PhysicsCollider { Radius = 2f, Height = hull });
            return e;
        }

        private Entity Round(Vector3 at, Vector3 velocity, GuidedRound g)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = velocity });
            _w.AddComponent(e, new BallisticProjectile { PreviousPosition = at });
            g.SpeedMps = velocity.Length();
            _w.AddComponent(e, g);
            return e;
        }

        private void Step(Entity round)
        {
            _sys.Execute(_w, Dt);
            ref var tf = ref _w.GetComponentRW<SimTransform>(round);
            tf.Position += _w.GetComponentRO<SimVelocity>(round).Linear * Dt;
        }

        /// <summary>The distance of <paramref name="p"/> from the line <paramref name="a"/> → <paramref name="b"/>.</summary>
        private static float FromLine(Vector3 p, Vector3 a, Vector3 b)
        {
            var d = Vector3.Normalize(b - a);
            var v = p - a;
            return (v - d * Vector3.Dot(v, d)).Length();
        }

        /// <summary>
        /// ⭐⭐ M4 — a COMMAND round launched straight down the range closes onto the gunner's sight line to a target 60 m off it
        /// (TOW-like, 200 m/s, ≤ 30°/s), and stays on it; with the sight line BLOCKED (a wall between the gunner and the target) it is
        /// <see cref="GuidedPhase.Lost"/> and flies on straight — break the sight and the missile goes stupid.
        /// </summary>
        [Fact]
        public void M4_ACommandRound_FollowsTheTarget_WhileTheGunnerSeesIt_AndGoesStraightWhenSightBreaks()
        {
            var gunner = Unit(Vector3.Zero);
            var target = Unit(new Vector3(600, 90, 0));
            var guided = new GuidedRound { Guidance = GuidanceKind.Command, Target = target, Gunner = gunner };
            var round = Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), guided);
            var eye = new Vector3(0, 0, 1.6f);
            var aim = AimPoint.Middle(_w, target, out _, out _);
            for (int f = 0; f < 120; f++) Step(round);   // 2 s
            var pos = _w.GetComponentRO<SimTransform>(round).Position;
            Assert.True(FromLine(pos, eye, aim) < 2f, $"2 s in, {FromLine(pos, eye, aim):0.0} m off the sight line at {pos}");
            Assert.Equal(GuidedPhase.Cruise, _w.GetComponentRO<GuidedRound>(round).Phase);

            // the same round with a wall between the gunner and the target: lost at once, straight on
            _w.SetSingletonManaged(TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 6, "thickness": 0.5 }, "geometry": { "type": "LineString", "coordinates": [[300,-200],[300,300]] } } ] }
            """));
            var blind = Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), guided);
            for (int f = 0; f < 60; f++) Step(blind);
            Assert.Equal(GuidedPhase.Lost, _w.GetComponentRO<GuidedRound>(blind).Phase);
            Assert.Equal(new Vector3(200, 0, 0), _w.GetComponentRO<SimVelocity>(blind).Linear);
        }

        /// <summary>
        /// ⭐⭐ M4 — a FIRE-AND-FORGET top-attack round (Javelin-like: 140 m/s, cruise 150 m, dive 60°) at a hull 1200 m away climbs to
        /// its cruise height, cruises, and dives: it reaches the hull descending at least 45°, so the face it meets is the ROOF
        /// (Armour Hit Model D-A — nothing marks a hit "top attack"; the arrival angle does).
        /// </summary>
        [Fact]
        public void M4_AJavelin_ClimbsAndDives_AndArrivesOnTheRoof()
        {
            var tank = Unit(new Vector3(1200, 0, 0), hull: 2.4f);
            var round = Round(new Vector3(0, 0, 1), new Vector3(140, 0, 0), new GuidedRound
            {
                Guidance = GuidanceKind.FireAndForget, Target = tank, Gunner = Unit(Vector3.Zero),
                TopAttack = 1, Phase = GuidedPhase.Climb, CruiseZ = 151, DiveAngleDeg = 60,
                LastSeen = new Vector3(1200, 0, 1.2f),
            });
            var middle = AimPoint.Middle(_w, tank, out _, out _);
            float maxZ = 0f, closest = float.MaxValue;
            Vector3 arrival = default;
            for (int f = 0; f < 60 * 30; f++)
            {
                Step(round);
                var p = _w.GetComponentRO<SimTransform>(round).Position;
                maxZ = MathF.Max(maxZ, p.Z);
                float d = Vector3.Distance(p, middle);
                if (d < closest) { closest = d; arrival = _w.GetComponentRO<SimVelocity>(round).Linear; }
                if (d < 2f || p.Z < 0f) break;
            }
            Assert.True(maxZ >= 145f, $"climbed only to {maxZ:0} m");
            Assert.True(closest < 3f, $"missed by {closest:0.0} m");
            float descent = MathF.Asin(-arrival.Z / arrival.Length()) * 180f / MathF.PI;
            Assert.True(descent >= 45f, $"arrived {descent:0} deg down");
            Assert.Equal(Facing.Top, ArmorModel.FacingOfArrival(_w.GetComponentRO<SimTransform>(tank), arrival));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2201</c> (DESIGN_Missiles_And_Ammo §3, "the profile fits") — the Javelin's own numbers (140 m/s, cruise 150 m, dive 60°,
        /// top attack from 150 m) at a hull <paramref name="range"/> m away, launched as <c>FireProcessingSystem</c> launches it
        /// (<see cref="GuidedRoundSystem.TopAttackFits"/> ⇒ Climb to the fitting apex, or direct): EVERY range reaches the hull, and every
        /// range the top attack is chosen for arrives on the ROOF (≥ 45° down). 230 m — the live <c>ua-javelin-lock</c> shot — and 300 m are
        /// below the profile's geometric minimum (≈ 600 m at the 30°/s turn) and fly direct; 650 m flies a capped apex; ≥ 750 m the full one.
        /// 🔴 Red before: at 230 m the round was launched into the 150 m climb, overflew the tank and hit the ground 580 m beyond it.
        /// </summary>
        [Theory]
        [InlineData(230f, false)]
        [InlineData(300f, false)]
        [InlineData(450f, false)]
        [InlineData(620f, true)]
        [InlineData(650f, true)]
        [InlineData(700f, true)]
        [InlineData(750f, true)]
        [InlineData(800f, true)]
        [InlineData(900f, true)]
        [InlineData(1000f, true)]
        [InlineData(1200f, true)]
        [InlineData(1500f, true)]
        [InlineData(2000f, true)]
        [InlineData(2500f, true)]
        public void CE2201_TheTopAttack_ArrivesOnTheRoof_AtEveryRangeItIsChosenFor_ElseFliesDirect(float range, bool top)
        {
            var javelin = new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 140f, TopAttack = true, TopAttackMinRangeM = 150f,
                                           CruiseAltitudeM = 150f, DiveAngleDeg = 60f };
            var tank = Unit(new Vector3(range, 0, 0), hull: 2.4f);
            var middle = AimPoint.Middle(_w, tank, out _, out _);
            var launch = new Vector3(0, 0, 1);
            var horizontal = new Vector2(middle.X - launch.X, middle.Y - launch.Y).Length();
            Assert.Equal(top, GuidedRoundSystem.TopAttackFits(javelin, 140f, horizontal, launch.Z - middle.Z, out float apex));
            if (range >= 750f) Assert.Equal(150f, apex);
            var round = Round(launch, Vector3.Normalize(middle - launch) * 140f, new GuidedRound
            {
                Guidance = GuidanceKind.FireAndForget, Target = tank, Gunner = Unit(Vector3.Zero),
                TopAttack = (byte)(top ? 1 : 0), Phase = top ? GuidedPhase.Climb : GuidedPhase.Cruise,
                CruiseZ = launch.Z + (top ? apex : javelin.CruiseAltitudeM), DiveAngleDeg = 60, LastSeen = middle,
            });
            float closest = float.MaxValue, maxZ = 0f;
            Vector3 arrival = default;
            for (int f = 0; f < 60 * 30; f++)
            {
                Step(round);
                var p = _w.GetComponentRO<SimTransform>(round).Position;
                maxZ = MathF.Max(maxZ, p.Z);
                float d = Vector3.Distance(p, middle);
                if (d < closest) { closest = d; arrival = _w.GetComponentRO<SimVelocity>(round).Linear; }
                if (d < 2f || p.Z < 0f) break;
            }
            Assert.True(closest < 3f, $"{range} m ({(top ? $"top, apex {apex:0}" : "direct")}): missed by {closest:0.0} m, peaked at {maxZ:0} m");
            if (top)
            {
                float descent = MathF.Asin(-arrival.Z / arrival.Length()) * 180f / MathF.PI;
                Assert.True(descent >= 45f, $"{range} m (apex {apex:0}): arrived {descent:0} deg down");
                Assert.Equal(Facing.Top, ArmorModel.FacingOfArrival(_w.GetComponentRO<SimTransform>(tank), arrival));
            }
        }

        /// <summary>⭐ M4 — the steering limit: one frame turns the velocity at most 30°/s × dt toward the aim.</summary>
        [Fact]
        public void M4_TheTurn_IsLimitedTo30DegreesASecond()
        {
            var turned = GuidedRoundSystem.Turn(new Vector3(1, 0, 0), new Vector3(0, 1, 0), 30f * MathF.PI / 180f);
            Assert.Equal(30f, MathF.Acos(Vector3.Dot(turned, Vector3.UnitX)) * 180f / MathF.PI, 2);
        }
    
        // ── ⭐⭐ CE-3198 (DESIGN_Missiles_And_Ammo §3b) — the NLAW's OVERFLY top attack ───────────────────────────────────────────────

        /// <summary>One frame for a round the fuze may spend: the system, then the move if it is still alive.</summary>
        private void StepAlive(Entity round)
        {
            _sys.Execute(_w, Dt);
            if (!_w.IsAlive(round)) return;
            ref var tf = ref _w.GetComponentRW<SimTransform>(round);
            tf.Position += _w.GetComponentRO<SimVelocity>(round).Linear * Dt;
        }

        private Entity OverflyRound(Entity target)
            => Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), new GuidedRound
            {
                Guidance = GuidanceKind.FireAndForget, Phase = GuidedPhase.Cruise, Target = target, Overfly = 1, OverflyHeightM = 1f,
                LastSeen = new Vector3(200, 0, 1.2f),
            });

        /// <summary>
        /// ⭐⭐ <c>CE-3198</c> — an NLAW (200 m/s, overfly) at a tank 200 m out flies OVER its roof and fires DOWN as it passes: one
        /// detonation on the tank, at the roof, travelling straight down — the unchanged chain reads the TOP face — and the round is spent.
        /// It never meets the hull on the way (it is above the roof as it arrives). 🔴 Red-proof: without the overfly goal and fuze it
        /// steers at the hull's middle and no downward detonation is ever published.
        /// </summary>
        [Fact]
        public void CE3198_AnOverflyRound_PassesOverTheRoof_AndFiresDown_OnTheTopFace()
        {
            var tank = Unit(new Vector3(200, 0, 0), hull: 2.4f);
            var round = OverflyRound(tank);
            float lowestOverTheTank = float.MaxValue;
            for (int f = 0; f < 180 && _w.IsAlive(round); f++)
            {
                var p = _w.GetComponentRO<SimTransform>(round).Position;
                if (MathF.Abs(p.X - 200f) <= 2f) lowestOverTheTank = MathF.Min(lowestOverTheTank, p.Z);
                StepAlive(round);
            }
            Assert.False(_w.IsAlive(round), "the round is spent by the fuze");
            _w.Bus.SwapBuffers();
            var d = Assert.Single(_w.Bus.Read<Fdp.Toolkit.Combat.Contracts.DetonationNotification>().ToArray());
            Assert.Equal(tank, d.Target);
            Assert.Equal(2.4f, d.HitZ, 3);
            Assert.True(d.TravelZ < 0f && d.TravelX == 0f && d.TravelY == 0f, "straight down");
            Assert.Equal(Facing.Top, ArmorModel.FacingOfArrival(_w.GetComponentRO<SimTransform>(tank), new Vector3(d.TravelX, d.TravelY, d.TravelZ)));
            Assert.True(lowestOverTheTank == float.MaxValue || lowestOverTheTank >= 2.4f, $"it dipped to {lowestOverTheTank:0.00} over the roof");
        }

        /// <summary>⭐ <c>CE-3198</c> — the fuze fires over a HULL only: at a target with no hull height (a man) nothing is published.</summary>
        [Fact]
        public void CE3198_TheOverflyFuze_DoesNotFireOverATargetWithNoHull()
        {
            var man = Unit(new Vector3(200, 0, 0));
            _w.AddComponent(man, new PhysicsCollider { Radius = 0.4f, Height = 0f });
            var round = OverflyRound(man);
            for (int f = 0; f < 120 && _w.IsAlive(round); f++) StepAlive(round);
            _w.Bus.SwapBuffers();
            Assert.Empty(_w.Bus.Read<Fdp.Toolkit.Combat.Contracts.DetonationNotification>().ToArray());
        }

        // ── ⭐⭐ CE-2187 (DESIGN_Missiles_And_Ammo §3c) — the IR seeker and the proximity fuze ─────────────────────────────────────

        /// <summary>A Stinger-like IR round: 700 m/s, a 20° seeker cone, a 4 m proximity fuze.</summary>
        private Entity IrRound(Vector3 at, Vector3 velocity, Entity target, Vector3 lastSeen)
            => Round(at, velocity, new GuidedRound
            {
                Guidance = GuidanceKind.InfraRed, Phase = GuidedPhase.Cruise, Target = target, LastSeen = lastSeen,
                SeekerCos = MathF.Cos(10f * MathF.PI / 180f), ProximityM = 4f,
            });

        /// <summary>
        /// ⭐⭐ <c>CE-2187</c> (AD1 + AD2) — an IR round launched from the ground at a helicopter crossing at 60 m/s, 300 m up and 1.2 km out,
        /// homes on it — LEADING it, as a homing seeker does — and BURSTS within its 4 m proximity fuze of the helicopter: an off-target
        /// burst (no target on the detonation: the area effect does the damage by distance), and is spent. 🔴 Red-proof (measured): pure
        /// pursuit (no lead) passes 6.8 m behind it and never bursts.
        /// </summary>
        [Fact]
        public void CE2187_AnIrRound_HomesOnACrossingHelicopter_AndBurstsWithinItsProximityFuze()
        {
            var heli = Unit(new Vector3(1200, -100, 300));
            var heliVelocity = new Vector3(0, 60, 0);
            _w.AddComponent(heli, new SimVelocity { Linear = heliVelocity });   // what the seeker's lead reads (ReplicatedMotion)
            var aim = Vector3.Normalize(new Vector3(1200, -40, 300));
            var round = IrRound(Vector3.Zero, aim * 700f, heli, new Vector3(1200, -100, 300));
            float closest = float.MaxValue;
            for (int f = 0; f < 240 && _w.IsAlive(round); f++)
            {
                ref var h = ref _w.GetComponentRW<SimTransform>(heli);
                h.Position += heliVelocity * Dt;
                closest = MathF.Min(closest, Vector3.Distance(_w.GetComponentRO<SimTransform>(round).Position, AimPoint.Middle(_w, heli, out _, out _)));
                StepAlive(round);
            }
            Assert.False(_w.IsAlive(round), $"the round must burst (closest {closest:0.0} m)");
            _w.Bus.SwapBuffers();
            var d = Assert.Single(_w.Bus.Read<Fdp.Toolkit.Combat.Contracts.DetonationNotification>().ToArray());
            Assert.True(d.Target.IsNull, "a proximity burst names no target: the fragments decide");
            float miss = Vector3.Distance(new Vector3(d.HitX, d.HitY, d.HitZ), AimPoint.Middle(_w, heli, out _, out _));
            Assert.True(miss <= 4.5f, $"it burst {miss:0.0} m from the helicopter — the fuze reaches 4 m");
        }

        /// <summary>
        /// ⭐ <c>CE-2187</c> — an IR seeker sees only inside its cone: a target 90° off its flight is never seen, so the round flies to where
        /// it last saw it (the launch point it was given) and does not turn toward the target. 🔴 Red-proof: with no cone (all round) it
        /// updates <c>LastSeen</c> to the target and turns.
        /// </summary>
        [Fact]
        public void CE2187_AnIrSeeker_DoesNotSeeATargetOutsideItsCone()
        {
            var off = Unit(new Vector3(0, 400, 0));
            var round = IrRound(Vector3.Zero, new Vector3(700, 0, 0), off, new Vector3(2000, 0, 0));
            for (int f = 0; f < 30; f++) StepAlive(round);
            Assert.Equal(new Vector3(2000, 0, 0), _w.GetComponentRO<GuidedRound>(round).LastSeen);
            Assert.True(MathF.Abs(_w.GetComponentRO<SimTransform>(round).Position.Y) < 1f, "it flew on, not toward the unseen target");
        }
}
}
