using System;
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
    /// ⭐⭐ Airborne insertion B-1 / B-2 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §5, §7 acceptance) — a body with a
    /// <see cref="Descent"/> is a Rope / Parachute body (<see cref="Locomotions.Of"/>), moved by <see cref="RopeSlideModel"/> /
    /// <see cref="ParachuteModel"/> through the existing <see cref="AirMotionSystem"/>, and a Pedestrian again once
    /// <see cref="DescentLandingSystem"/> takes the Descent away.
    /// </summary>
    public sealed class DescentModelsTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly AirMotionSystem _air = new();
        private readonly DescentLandingSystem _landing = new();
        private double _t;

        public DescentModelsTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<Locomotion>();
            _w.RegisterComponent<Descent>();
            _w.RegisterComponent<NavigationIntent>();
            _w.RegisterComponent<Health>();
            _w.RegisterEvent<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
            _w.SetSingleton(new GlobalTime());
        }

        public void Dispose() => _w.Dispose();

        private void Step(float dt)
        {
            _t += dt;
            _w.SetSingleton(new GlobalTime { TotalTime = _t, DeltaTime = dt });
            _air.Execute(_w, dt);
            _landing.Execute(_w, dt);
        }

        private Entity Body(Vector3 at, Descent d, Vector3 velocity = default)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = velocity });
            _w.AddComponent(e, new Locomotion { Class = LocomotionClass.Pedestrian });
            _w.AddComponent(e, new NavigationIntent());
            d.PhaseSince = _t;
            _w.AddComponent(e, d);
            _w.SetAuthority<SimTransform>(e, true);
            return e;
        }

        private Entity Hover(Vector3 at)
        {
            var h = _w.CreateEntity();
            _w.AddComponent(h, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(h, new SimVelocity());
            _w.AddComponent(h, new Health { Current = 100, Max = 100 });
            return h;
        }

        /// <summary>⭐ B-1 — the descent decides the class: a Pedestrian TYPE with a parachute Descent is a Parachute body, a roped one a
        /// Rope body; both in the AIR domain, neither an aircraft (D-4); without the Descent he is a Pedestrian again.</summary>
        [Fact]
        public void B1_AManWithADescent_ReportsItsClass_AndIsNoAircraft()
        {
            var jumper = Body(new Vector3(0, 0, 400), new Descent { Kind = DescentKind.StaticLine, Phase = DescentPhase.Canopy, Canopy = CanopyKind.Round });
            var roper = Body(new Vector3(0, 0, 10), new Descent { Kind = DescentKind.FastRope, Phase = DescentPhase.Slide });
            Assert.Equal(LocomotionClass.Parachute, Locomotions.Of(_w, jumper));
            Assert.Equal(LocomotionClass.Rope, Locomotions.Of(_w, roper));
            Assert.Equal(MotionDomain.Air, Locomotions.DomainOf(Locomotions.Of(_w, jumper)));
            Assert.False(Locomotions.IsAircraft(_w, jumper));
            Assert.False(Locomotions.IsAircraft(_w, roper));
            _w.RemoveComponent<Descent>(jumper);
            Assert.Equal(LocomotionClass.Pedestrian, Locomotions.Of(_w, jumper));
        }

        /// <summary>⭐ B-2 (§5.1) — down a 12 m rope in ~3.5 s, x, y under the anchor the whole way (the helicopter's right door), landed on
        /// the surface, and the Descent gone a second later. ✅ Red-proof: a flat 1 m/s brake over the last 3 m ⇒ ~5 s ⇒ red.</summary>
        [Fact]
        public void B2_ARopedMan_Slides12m_InAbout3Point5s_UnderTheAnchor()
        {
            var heli = Hover(new Vector3(50, 20, 12f - 0.5f));                               // the anchor 0.5 m up ⇒ 12 m of rope to the ground
            var man = Body(new Vector3(50, 20, 11.5f), new Descent { Kind = DescentKind.FastRope, Phase = DescentPhase.Exit, Anchor = heli, AnchorIndex = 0 });
            const float dt = 1f / 60f;
            double landedAt = -1;
            float worstXy = 0f;
            for (int i = 0; i < 60 * 10 && _w.HasComponent<Descent>(man); i++)
            {
                Step(dt);
                var p = _w.GetComponent<SimTransform>(man).Position;
                worstXy = MathF.Max(worstXy, Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(50f, 21.2f)));
                if (landedAt < 0 && _w.GetComponent<Descent>(man).Phase == DescentPhase.Landed) landedAt = _t;
            }
            Assert.InRange(landedAt, 3.0, 4.0);
            Assert.True(worstXy < 1e-3f, $"off the rope by {worstXy} m");
            Assert.Equal(0f, _w.GetComponent<SimTransform>(man).Position.Z, 3);
            Assert.False(_w.HasComponent<Descent>(man));                                     // cleared the rope: a Pedestrian again
        }

        /// <summary>⭐ B-2 (§5.1, §5.3) — the helicopter killed while he is on the rope: he falls (gravity) and lands.</summary>
        [Fact]
        public void B2_TheAnchorKilledMidRope_HeFalls()
        {
            var heli = Hover(new Vector3(0, 0, 20f));
            var man = Body(new Vector3(0, 1.2f, 20.5f), new Descent { Kind = DescentKind.FastRope, Phase = DescentPhase.Exit, Anchor = heli });
            for (int i = 0; i < 60; i++) Step(1f / 60f);
            _w.SetComponent(heli, new Health { Current = 0, Max = 100 });
            Step(1f / 60f);
            Assert.Equal(DescentPhase.Falling, _w.GetComponent<Descent>(man).Phase);
            for (int i = 0; i < 60 * 3 && _w.GetComponent<Descent>(man).Phase != DescentPhase.Landed; i++) Step(1f / 60f);
            Assert.Equal(DescentPhase.Landed, _w.GetComponent<Descent>(man).Phase);
            Assert.Equal(0f, _w.GetComponent<SimTransform>(man).Position.Z, 3);
        }

        /// <summary>
        /// ⭐ B-6 (§5.3) — a fall hurts by the speed he hits at: 1.5 m (5.4 m/s) nothing, 3.5 m (8.3 m/s) 75 % of his health (incapacitated),
        /// 20 m all of it — a whole-body damage, through the existing chain. ✅ Red-proof: Fall without the impact ⇒ no damage ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(1.5f, 0f)]
        [InlineData(3.5f, 75f)]
        [InlineData(20f, 100f)]
        public void B6_AFall_HurtsByTheSpeedItHitsAt(float height, float damage)
        {
            var heli = Hover(new Vector3(0, 0, 40));
            _w.SetComponent(heli, new Health { Current = 0, Max = 100 });                   // the anchor is dead: he falls from where he is
            var man = Body(new Vector3(5, 5, height), new Descent { Kind = DescentKind.FastRope, Phase = DescentPhase.Falling, Anchor = heli });
            _w.AddComponent(man, new Health { Current = 100, Max = 100 });
            float hurt = 0f;
            for (int i = 0; i < 60 * 5 && _w.GetComponent<Descent>(man).Phase != DescentPhase.Landed; i++)
            {
                Step(1f / 60f);
                _w.Bus.SwapBuffers();
                foreach (var e in _w.Bus.Read<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>()) if (e.HitEntity == man) hurt += e.TotalDamage;
            }
            _w.Bus.SwapBuffers();
            foreach (var e in _w.Bus.Read<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>()) if (e.HitEntity == man) hurt += e.TotalDamage;
            Assert.Equal(DescentPhase.Landed, _w.GetComponent<Descent>(man).Phase);
            Assert.Equal(damage, hurt, 3);
        }

        /// <summary>Flies a ram-air jumper from <paramref name="height"/> above <paramref name="start"/> to the ground; returns where he landed.</summary>
        private Vector3 FlyRamAir(Vector3 start, Vector3 target, float height)
        {
            var man = Body(start + new Vector3(0, 0, height),
                           new Descent { Kind = DescentKind.FreeFall, Phase = DescentPhase.Exit, Canopy = CanopyKind.RamAir, Target = target });
            const float dt = 1f / 30f;
            for (int i = 0; i < 30 * 600 && _w.GetComponent<Descent>(man).Phase != DescentPhase.Landed; i++) Step(dt);
            Assert.Equal(DescentPhase.Landed, _w.GetComponent<Descent>(man).Phase);
            return _w.GetComponent<SimTransform>(man).Position;
        }

        /// <summary>⭐ B-2 (§5.2) — a ram-air canopy from 1 000 m is flown to his point: within 10 m, in calm air and in a 5 m/s wind.
        /// ✅ Red-proof: guidance returning (0, 0) ⇒ he lands where the glide takes him, hundreds of metres off ⇒ red.</summary>
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(5f, 0f)]
        [InlineData(-3f, 4f)]
        public void B2_ARamAirJumper_From1000m_LandsWithin10mOfHisPoint(float windX, float windY)
        {
            if (windX != 0f || windY != 0f) _w.SetSingleton(new Wind { Velocity = new Vector2(windX, windY) });
            var target = new Vector3(400, -250, 0);
            var at = FlyRamAir(Vector3.Zero, target, 1000f);
            Assert.True(Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(target.X, target.Y)) <= 10f, $"landed at {at}, wind ({windX},{windY})");
        }

        /// <summary>⭐ B-2 (§5.2) — a round canopy cannot fly to a point: it drifts with the wind, so with no point it lands downwind by the
        /// wind × its time under the canopy (the release point is the real aim, §6).</summary>
        [Fact]
        public void B2_ARoundCanopy_LandsWithinItsDrift()
        {
            _w.SetSingleton(new Wind { Velocity = new Vector2(3f, 0f) });
            var man = Body(new Vector3(0, 0, 400), new Descent { Kind = DescentKind.StaticLine, Phase = DescentPhase.Exit, Canopy = CanopyKind.Round });
            const float dt = 1f / 30f;
            for (int i = 0; i < 30 * 300 && _w.GetComponent<Descent>(man).Phase != DescentPhase.Landed; i++) Step(dt);
            Assert.Equal(DescentPhase.Landed, _w.GetComponent<Descent>(man).Phase);
            var at = _w.GetComponent<SimTransform>(man).Position;
            float expected = 3f * (float)_t;                                                 // the wind for the whole descent
            Assert.InRange(at.X, 0.85f * expected, 1.05f * expected);
            Assert.InRange(at.Y, -1f, 1f);
            Assert.InRange(_t, 400 / 5.5 - 5, 400 / 5.5 + 10);                               // the round canopy's sink
        }

        /// <summary>
        /// ⭐ B-5 (design §6) — the RELEASE POINT: a static-line jumper who leaves a 35 m/s helicopter 400 m up at the point the helper gives,
        /// in a 4 m/s crosswind and with no point to steer to, lands within 30 m of the DZ point — where leaving over the point lands him
        /// some 300 m downwind. Calm and at rest, the release point IS the point. ✅ Red-proof: a helper returning the point itself ⇒ ~300 m off
        /// ⇒ red.
        /// </summary>
        [Fact]
        public void B5_AStaticLineJumper_LeavingAtTheReleasePoint_LandsOnTheDz()
        {
            var chute = ParachuteModel.Defaults(CanopyKind.Round);
            var dz = new Vector2(1000f, 500f);
            Assert.Equal(dz, ReleasePoint.For(dz, 400f, chute, Vector2.Zero, Vector2.Zero));

            var wind = new Vector2(1f, 4f);
            var carrier = new Vector2(35f, 0f);
            _w.SetSingleton(new Wind { Velocity = wind });
            var at = ReleasePoint.For(dz, 400f, chute, wind, carrier);
            var man = Body(new Vector3(at, 400f), new Descent { Kind = DescentKind.StaticLine, Phase = DescentPhase.Exit, Canopy = CanopyKind.Round },
                           velocity: new Vector3(carrier, 0f));
            const float dt = 1f / 30f;
            for (int i = 0; i < 30 * 300 && _w.GetComponent<Descent>(man).Phase != DescentPhase.Landed; i++) Step(dt);
            var landed = _w.GetComponent<SimTransform>(man).Position;
            Assert.True(Vector2.Distance(new Vector2(landed.X, landed.Y), dz) <= 30f, $"released at {at}, landed at {landed}");
        }

        /// <summary>
        /// ⭐ B-2 (§5.2, D-5) — under a ram-air canopy in a steady turn the BANK is the body's roll and obeys φ = atan(V ψ̇ / g) (± 2°), and
        /// the spiral sinks faster than a straight glide. A point straight below a jumper far too high is the spiral (§5.2 guidance ①).
        /// </summary>
        [Fact]
        public void B2_TheBank_IsAtanOfVPsiDotOverG_AndASpiralSinksFasterThanAStraightGlide()
        {
            // (a Target of exactly zero means "no point" — Descent.Target's contract)
            var spiral = Body(new Vector3(100, 100, 900), new Descent { Kind = DescentKind.StaticLine, Phase = DescentPhase.Canopy, Canopy = CanopyKind.RamAir, Target = new Vector3(100, 100, 0) });
            var straight = Body(new Vector3(5000, 0, 900), new Descent { Kind = DescentKind.StaticLine, Phase = DescentPhase.Canopy, Canopy = CanopyKind.RamAir });
            const float dt = 1f / 60f;
            for (int i = 0; i < 60 * 8; i++) Step(dt);                                      // the bank lag settles

            var t0 = _w.GetComponent<SimTransform>(spiral);
            float z0s = _w.GetComponent<SimTransform>(straight).Position.Z;
            Step(dt);
            var t1 = _w.GetComponent<SimTransform>(spiral);
            float psiDot = MathF.IEEERemainder(RotorcraftModel.YawOf(t1.Rotation) - RotorcraftModel.YawOf(t0.Rotation), 2f * MathF.PI) / dt;
            float v = Vector2.Distance(new Vector2(t1.Position.X, t1.Position.Y), new Vector2(t0.Position.X, t0.Position.Y)) / dt;
            var (_, roll) = RotorcraftModel.TiltOf(t1.Rotation);
            float expected = MathF.Atan(v * MathF.Abs(psiDot) / DescentRules.Gravity);
            Assert.True(MathF.Abs(psiDot) > 0.3f, $"turning at {psiDot} rad/s");
            Assert.InRange(MathF.Abs(roll) * 180f / MathF.PI, expected * 180f / MathF.PI - 2f, expected * 180f / MathF.PI + 2f);

            float sinkSpiral = (t0.Position.Z - t1.Position.Z) / dt;
            float sinkStraight = (z0s - _w.GetComponent<SimTransform>(straight).Position.Z) / dt;
            Assert.True(sinkSpiral > sinkStraight + 0.5f, $"spiral {sinkSpiral} m/s vs straight {sinkStraight} m/s");
        }
    }
}
