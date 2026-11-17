using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
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
            => FlyAtAHull(new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 140f, TopAttack = true, TopAttackMinRangeM = 150f,
                                           CruiseAltitudeM = 150f, DiveAngleDeg = 60f }, range, top);

        /// <summary>
        /// ⭐ <c>CE-2222</c> — the Javelin's OWN turn (120 °/s) and climb (45°), as <c>MunitionTkbCatalog</c> now states them: the profile fits
        /// from its declared 150 m — every range from 150 m top-attacks and arrives on the ROOF (≥ 45° down), the live <c>ua-javelin-lock</c>
        /// shot (~190 m) included. 📐 At the engine's 30 °/s it fitted only beyond ~610 m (the sweep above), so every closer Javelin flew direct
        /// onto the front. 🔴 Red-proof: drop the per-missile turn from <c>TopAttackFits</c> ⇒ 150 m … 600 m fly direct ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(150f)]
        [InlineData(190f)]
        [InlineData(230f)]
        [InlineData(300f)]
        [InlineData(450f)]
        [InlineData(620f)]
        [InlineData(1000f)]
        [InlineData(2000f)]
        public void CE2222_TheJavelinsOwnTurn_TopAttacksFromItsDeclared150m(float range)
            => FlyAtAHull(new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 140f, TopAttack = true, TopAttackMinRangeM = 150f,
                                           CruiseAltitudeM = 150f, DiveAngleDeg = 60f, TurnRateDegPerSec = 120f, ClimbAngleDeg = 45f }, range, top: true);

        /// <summary>A round launched as <c>FireProcessingSystem</c> launches it at a hull <paramref name="range"/> m off: it must reach the
        /// hull, and a top attack must arrive on the roof.</summary>
        private void FlyAtAHull(MissileDto javelin, float range, bool top)
        {
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
                TurnRateDegPerSec = javelin.TurnRateDegPerSec, ClimbAngleDeg = javelin.ClimbAngleDeg,
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

        // ── ⭐⭐ CE-4617 (B-2, RS-3 / RV-6; docs/designs/air-attack/DESIGN_Radar_SAM.md) — the radar SAM's missile ─────────────────────

        /// <summary>The radar SAM's missile as <c>FireProcessingSystem</c> builds it from its <c>MissileDto</c>: radar, 800 m/s, a 60° seeker
        /// cone (or <paramref name="coneDeg"/>), 18 s of flight, the 4 m proximity fuze, the engine's turn rate.</summary>
        private Entity RadarSamRound(Vector3 velocity, Entity target, Vector3 lastSeen, float coneDeg = 60f)
            => Round(Vector3.Zero, velocity, new GuidedRound
            {
                Guidance = GuidanceKind.Radar, Phase = GuidedPhase.Cruise, Target = target, LastSeen = lastSeen,
                SeekerCos = MathF.Cos(0.5f * coneDeg * MathF.PI / 180f), ProximityM = 4f, LifetimeTicks = 18 * 60, Rejection = 0.9f,
            });

        /// <summary>Flies the round at <paramref name="target"/> (moving at <paramref name="v"/>) until it bursts or <paramref name="seconds"/>
        /// pass; returns the burst's distance from the target (∞ when it never burst).</summary>
        private float FlyAt(Entity round, Entity target, Vector3 v, float seconds)
        {
            for (int f = 0; f < seconds * 60 && _w.IsAlive(round); f++)
            {
                _w.GetComponentRW<SimTransform>(target).Position += v * Dt;
                StepAlive(round);
            }
            if (_w.IsAlive(round)) return float.PositiveInfinity;
            _w.Bus.SwapBuffers();
            var d = Assert.Single(_w.Bus.Read<Fdp.Toolkit.Combat.Contracts.DetonationNotification>().ToArray());
            return Vector3.Distance(new Vector3(d.HitX, d.HitY, d.HitZ), AimPoint.Middle(_w, target, out _, out _));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4617</c> (RV-6) — the radar SAM's missile, launched straight at where an F-16 IS (no lead at the launch), hits it crossing
        /// at 250 m/s 3 km out and 800 m up: the 60° cone keeps the crossing jet inside it while the seeker leads. 🔴 Red-proof: with the
        /// MANPADS' 20° cone the same shot loses the jet (the lead ≈ 21° leaves the cone) and never bursts — why RV-6 asked for ≥ 60°.
        /// </summary>
        [Fact]
        public void CE4617_TheRadarSamMissile_HitsAJetCrossingAt250_ThreeKilometresOut_WhereATwentyDegreeConeLosesIt()
        {
            var start = new Vector3(3000, -1000, 800);
            var v = new Vector3(0, 250, 0);
            var jet = Unit(start);
            _w.AddComponent(jet, new SimVelocity { Linear = v });
            float miss = FlyAt(RadarSamRound(Vector3.Normalize(start) * 800f, jet, start), jet, v, 18f);
            Assert.True(miss <= 4.5f, $"it burst {miss:0.0} m from the jet — the fuze reaches 4 m");

            var jet2 = Unit(start);
            _w.AddComponent(jet2, new SimVelocity { Linear = v });
            float narrow = FlyAt(RadarSamRound(Vector3.Normalize(start) * 800f, jet2, start, coneDeg: 20f), jet2, v, 18f);
            Assert.True(narrow > 4.5f, $"a 20° cone should lose the crossing jet (burst {narrow:0.0} m off)");
        }

        /// <summary>⭐ <c>CE-4617</c> — the same missile downs a UH-60-class helicopter 4 km out, 150 m up, crossing at 60 m/s.</summary>
        [Fact]
        public void CE4617_TheRadarSamMissile_HitsAHelicopterFourKilometresOut()
        {
            var start = new Vector3(4000, 0, 150);
            var v = new Vector3(0, 60, 0);
            var heli = Unit(start);
            _w.AddComponent(heli, new SimVelocity { Linear = v });
            float miss = FlyAt(RadarSamRound(Vector3.Normalize(start) * 800f, heli, start), heli, v, 18f);
            Assert.True(miss <= 4.5f, $"it burst {miss:0.0} m from the helicopter");
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

        // ── ⭐⭐ CE-10002 (air lane S2 — docs/designs/air-attack/DESIGN_Air_Combat.md AC-6, RV-1) — missiles that can be beaten ──────────

        /// <summary>The §5b IR air-to-air round (AIM-9X / R-73 class) as the catalogue states it.</summary>
        private static GuidedRound IrAam(Vector3 launchVel) => new()
        {
            Guidance = GuidanceKind.InfraRed, SpeedMps = 850f, SpeedNow = MathF.Max(30f, launchVel.Length()),
            BurnLeftS = 5f, BoostMps2 = (850f - MathF.Max(30f, launchVel.Length())) / 5f, MaxG = 40f, DragPerS = 40f, MinSpeedMps = 300f,
            SteerLagS = 0.4f, SteerDir = Vector3.Normalize(launchVel), ProximityM = 8f,
        };

        /// <summary>
        /// One engagement, both bodies stepped at 60 Hz: the round homes on the jet's lead (<c>TargetLead.Point</c>, as the system does) through
        /// <see cref="GuidedRoundSystem.Motor"/>; the jet (an F-16, <see cref="Fdp.Toolkit.Motion.Steer3D"/>) defends after <paramref name="reactS"/>:
        /// DRAG while more than 4 s to go, BEAM inside it, the LAST-DITCH break inside 2.5 s (AC-9). True = the fuze met the jet.
        /// </summary>
        private static bool Engage(int seed, float reactS) => Engage(seed, reactS, out _, out _);

        internal static bool Engage(int seed, float reactS, out float missM, out string end)
        {
            missM = float.MaxValue; end = "timeout";
            var rng = new Random(seed);
            var l = Fdp.Toolkit.Motion.FighterLimits.Of(Fdp.Toolkit.Motion.FlightParams.From(new FlightParamsDto
            {
                MaxSpeedMps = 400f, CruiseSpeedMps = 220f, StallSpeedMps = 70f, AccelMps2 = 12f, MaxG = 9f, CornerSpeedMps = 170f, DragK = 0.203f, RollRateDeg = 240f,
            }));
            float heading = (float)(rng.NextDouble() * 2 * Math.PI);
            var fwd = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0f);
            float jetSpeed = 200f + 80f * (float)rng.NextDouble();
            var jet = new Fdp.Toolkit.Motion.FlightBody { Pos = new Vector3(0, 0, 3000), Vel = fwd * jetSpeed, Rot = Fdp.Toolkit.Motion.Steer3D.FromBasis(fwd, Vector3.UnitZ) };
            // the shooter: 1.2–3 km away at ANY aspect (a fight's shots, AC-8's zone inside the show's cap), at 250 m/s
            float off = (float)((rng.NextDouble() * 2 - 1) * Math.PI), range = 1200f + 1800f * (float)rng.NextDouble();
            var from = -new Vector3(MathF.Cos(heading + off), MathF.Sin(heading + off), 0f) * range + new Vector3(0, 0, 3000);
            var launchVel = Vector3.Normalize(jet.Pos - from) * 250f;
            var g = IrAam(launchVel);
            var mPos = from; var mVel = launchVel;
            const float dt = 1f / 60f;
            for (int i = 0; i < 60 * 30; i++)
            {
                float t = i * dt;
                // the round: lead pursuit through the lag and the motor
                var lead = TargetLead.Point(mPos, Vector3.Zero, g.SpeedMps, jet.Pos, jet.Vel);
                var to = lead - mPos;
                bool defeated = GuidedRoundSystem.Motor(ref g, ref mVel, to.LengthSquared() > 1e-6f, to.LengthSquared() > 1e-6f ? Vector3.Normalize(to) : mVel, dt);
                if (defeated) { end = $"defeated at {t:F1} s"; return false; }
                var step = mVel * dt;
                // the jet: its defence once it reacted
                var rel = jet.Pos - mPos;
                float closure = MathF.Max(1f, -Vector3.Dot(jet.Vel - mVel, Vector3.Normalize(rel)));
                float tti = rel.Length() / closure;
                var away = Vector3.Normalize(new Vector3(rel.X, rel.Y, 0f));
                var across = new Vector3(-away.Y, away.X, 0f);
                if (Vector3.Dot(across, jet.Vel) < 0f) across = -across;
                Vector3 want = Vector3.Normalize(jet.Vel);
                if (t >= reactS)
                    want = tti > 4f ? Vector3.Normalize(away + new Vector3(0, 0, -0.1f)) : Vector3.Normalize(across + new Vector3(0, 0, -0.15f));
                if (t >= reactS && tti < 2.5f) want = across;
                Fdp.Toolkit.Motion.Steer3D.Step(ref jet, want, 9f, 1f, in l, dt);
                // the proximity fuze over this step (the system's own rule: the closest point of the round's step)
                float len2 = step.LengthSquared();
                float k = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(jet.Pos - mPos, step) / len2, 0f, 1f) : 0f;
                float miss = Vector3.Distance(mPos + step * k, jet.Pos);
                if (miss < missM) missM = miss;
                if (miss <= g.ProximityM) { end = $"hit at {t:F1} s"; return true; }
                mPos += step;
            }
            return false;
        }

        /// <summary>
        /// ⭐⭐ AC-6 / RV-1 — THE acceptance, measured not argued: 300 seeded IR shots from 1.2–3 km at ANY aspect at an F-16 (the §5b round,
        /// lead pursuit through its 0.4 s lag, the motor). A Veteran reacting in 0.7 s (drag, beam, last-ditch) is hit 6…30 times; the same
        /// pilot in a LateWarning lapse (+2 s, AC-12) 25…60 times; a pilot who never defends 120…180 times (R-10007, the stated bands) — the
        /// defence is what saves it, and a late one is measurably worse. ✅ Red-proof: the defence off in the timely run ⇒ ~150 ⇒ out of band.
        /// </summary>
        [Fact]
        public void AC6_MonteCarlo_ATimelyDefenceBeatsMostShots_ALateOneDoesNot()
        {
            int timely = 0, late = 0, none = 0;
            for (int seed = 1; seed <= Shots; seed++)
            {
                if (Engage(seed, reactS: 0.7f)) timely++;   // a Veteran (AC-12)
                if (Engage(seed, reactS: 2.7f)) late++;     // the same pilot in a LateWarning lapse (+2 s)
                if (Engage(seed, reactS: 999f)) none++;     // no defence at all
            }
            string all = $"of {Shots}: timely {timely}, late {late}, none {none}";
            Assert.True(timely >= 6 && timely <= 30, "timely outside its stated band 6…30 — " + all);
            Assert.True(late >= 25 && late <= 60, "late outside its stated band 25…60 — " + all);
            Assert.True(none >= 120 && none <= 180, "no defence outside its stated band 120…180 — " + all);
            Assert.True(late >= 2 * timely && none >= 2 * late, "the defence is not what saves it — " + all);
        }

        /// <summary>⭐ R-10007 — the seeded shots per profile. 📐 Measured `2026-11-17` (300 seeds): timely 16 (5 %), late 38 (13 %), none 150 (50 %).</summary>
        private const int Shots = 300;

        /// <summary>⭐ AC-6 — the motor: it boosts to its speed over the burn, coasts losing DragPerS, and is DEFEATED below its minimum.</summary>
        [Fact]
        public void AC6_TheMotor_BoostsCoasts_AndIsDefeatedWhenSlow()
        {
            var vel = new Vector3(250f, 0, 0);
            var g = IrAam(vel);
            for (int i = 0; i < 60 * 5; i++) Assert.False(GuidedRoundSystem.Motor(ref g, ref vel, false, Vector3.UnitX, 1f / 60f));
            Assert.Equal(850f, g.SpeedNow, 0);                                // burnt out at full speed
            int frames = 0;
            while (!GuidedRoundSystem.Motor(ref g, ref vel, false, Vector3.UnitX, 1f / 60f)) frames++;
            Assert.InRange(frames / 60f, 13.5f, 14.5f);                       // (850 − 300) / 40 = 13.75 s of coasting, straight
        }

        /// <summary>⭐ AC-6 — the g falls with speed²: coasted to 425 m/s (half its speed) the round turns at a quarter of its full-speed g.</summary>
        [Fact]
        public void AC6_ASlowRound_TurnsLess()
        {
            float Turned(float speed)
            {
                var vel = new Vector3(speed, 0, 0);
                var g = IrAam(vel); g.BurnLeftS = 0f; g.SpeedNow = speed; g.SteerLagS = 0f; g.DragPerS = 0f;
                GuidedRoundSystem.Motor(ref g, ref vel, true, Vector3.UnitY, 1f / 60f);
                return MathF.Acos(Vector3.Dot(Vector3.Normalize(vel), Vector3.UnitX));
            }
            float full = Turned(850f), half = Turned(425f);
            // turn rate = g·9.81 / v ⇒ at half speed: a quarter of the g at half the speed = half the rate
            Assert.Equal(0.5f, half / full, 2);
        }

        // ── ⭐⭐ CE-10003 (air combat AC-8, RV-10) — the employment zone and the accelerating gun lead ─────────────────────────────────

        private static MissileDto Ir() => new MissileDto { Guidance = GuidanceKind.InfraRed, SpeedMps = 850f, BurnSeconds = 5f, MaxG = 40f, DragPerS = 40f, MinSpeedMps = 300f };

        /// <summary>
        /// ⭐ CE-10003 — the zone from the motor model (§5b IR): a tail chase on a 220 m/s target reaches ~6.4 km; the no-escape range is shorter
        /// than the max; a head-on target (closing) is reachable much farther; a constant-speed round (no motor) is not limited by a burn-out.
        /// 🔴 Red-proof (mutant): the coast's drag ignored ⇒ the tail-chase max runs to the 60 s cap (> 30 km) ⇒ red.
        /// </summary>
        [Fact]
        public void CE10003_TheZone_TailChaseShorterThanHeadOn_NoEscapeInsideMax()
        {
            float tail = EmploymentZone.MaxRange(Ir(), 220f, 220f);
            float head = EmploymentZone.MaxRange(Ir(), 220f, -220f);
            float noEscape = EmploymentZone.MaxRange(Ir(), 220f, 400f);
            Assert.InRange(tail, 5500f, 7500f);
            Assert.True(head > tail + 4000f, $"head-on {head:0} vs tail {tail:0}");
            Assert.True(noEscape < tail, $"no-escape {noEscape:0} vs max {tail:0}");
            var (max, ne) = EmploymentZone.Of(Ir(), Vector3.Zero, new Vector3(220f, 0f, 0f), new Vector3(3000f, 0f, 0f), new Vector3(220f, 0f, 0f));
            Assert.InRange(max, 5500f, 7500f);
            Assert.True(EmploymentZone.Inside(Ir(), Vector3.Zero, new Vector3(220f, 0f, 0f), new Vector3(3000f, 0f, 0f), new Vector3(220f, 0f, 0f), 0.8f, false));
            Assert.False(EmploymentZone.Inside(Ir(), Vector3.Zero, new Vector3(220f, 0f, 0f), new Vector3(6900f, 0f, 0f), new Vector3(220f, 0f, 0f), 0.8f, false));
            Assert.False(EmploymentZone.Inside(Ir(), Vector3.Zero, new Vector3(220f, 0f, 0f), new Vector3(300f, 0f, 0f), new Vector3(220f, 0f, 0f), 1f, false));   // inside the minimum
            Assert.True(ne < max);
            // ⛔ no allocation in the zone maths (CLAUDE.md hot path): a steady call allocates nothing
            var ir = Ir();
            EmploymentZone.Of(ir, Vector3.Zero, new Vector3(220f, 0f, 0f), new Vector3(3000f, 0f, 0f), new Vector3(220f, 0f, 0f));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) EmploymentZone.Of(ir, Vector3.Zero, new Vector3(220f, 0f, 0f), new Vector3(3000f + i, 0f, 0f), new Vector3(220f, 0f, 0f));
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>
        /// ⭐ CE-10003 (RV-10) — the gun lead on a TURNING target adds ½·a·t²: a 9 g turn at 800 m is ~28 m the straight lead misses; zero
        /// acceleration is the straight lead. 🔴 Red-proof (mutant): the acceleration term dropped ⇒ the two leads equal ⇒ red.
        /// </summary>
        [Fact]
        public void CE10003_TheGunLead_AddsTheTargetsAcceleration()
        {
            var shooter = Vector3.Zero; var sv = new Vector3(200f, 0f, 0f);
            var target = new Vector3(800f, 0f, 0f); var tv = new Vector3(0f, 200f, 0f); var a = new Vector3(-88f, 0f, 0f);   // 9 g toward its turn centre
            var straight = TargetLead.Point(shooter, sv, 1030f, target, tv);
            Assert.Equal(straight, TargetLead.PointAccelerating(shooter, sv, 1030f, target, tv, Vector3.Zero));
            var turning = TargetLead.PointAccelerating(shooter, sv, 1030f, target, tv, a);
            float t = TargetLead.FlightTime(shooter, sv, 1030f, target, tv);
            Assert.InRange(Vector3.Distance(straight, turning), 0.5f * 88f * t * t - 0.5f, 0.5f * 88f * t * t + 0.5f);
            Assert.InRange(Vector3.Distance(straight, turning), 20f, 40f);
        }
    }
}
