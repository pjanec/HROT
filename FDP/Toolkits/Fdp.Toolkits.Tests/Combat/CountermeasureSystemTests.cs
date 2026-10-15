using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> (<c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3c, Air Ops §7d AD3) — flares and chaff:
    /// <see cref="CountermeasureSystem"/> throws a salvo of decoys when the brain asks and seduces the homing rounds whose seeker the decoy
    /// fools and that see it in their cone, each with one deterministic draw against its rejection.
    /// </summary>
    public sealed class CountermeasureSystemTests : IDisposable
    {
        private const long Helicopter = 9401;
        private readonly EntityRepository _w = new();
        private readonly CountermeasureSystem _sys = new();

        public CountermeasureSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<GuidedRound>();
            _w.RegisterComponent<Decoy>();
            _w.RegisterComponent<CountermeasureState>();
            _w.RegisterEvent<DispenseCountermeasureIntent>();
            var db = new TkbDatabase();
            var heli = new TkbTemplate("countermeasure test helicopter", Helicopter);
            heli.AddDescriptor(new CountermeasuresDto { Flares = 4, Chaff = 2, Salvo = 2, IntervalS = 1f, DecoySeconds = 5f });
            db.Register(heli);
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSingleton(new GlobalTime { TotalTime = 100.0 });
        }

        public void Dispose() => _w.Dispose();

        private void At(double t) => _w.SetSingleton(new GlobalTime { TotalTime = t });

        private Entity Aircraft()
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(1000, 0, 300), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = new Vector3(0, 60, 0) });
            _w.AddComponent(e, new TkbIdentity { TkbType = Helicopter });
            return e;
        }

        /// <summary>A homing round 500 m short of the aircraft, flying at it, with a 20° cone (or one pointed away).</summary>
        private Entity Round(Entity target, GuidanceKind seeker, float rejection, bool lookingAway = false)
        {
            var e = _w.CreateEntity();
            var at = new Vector3(500, 0, 300);
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = (lookingAway ? new Vector3(0, -1, 0) : Vector3.UnitX) * 700f });
            _w.AddComponent(e, new GuidedRound
            {
                Guidance = seeker, Phase = GuidedPhase.Cruise, Target = target, SpeedMps = 700f,
                SeekerCos = MathF.Cos(10f * MathF.PI / 180f), Rejection = rejection, LastSeen = new Vector3(1000, 0, 300),
            });
            return e;
        }

        private bool OnADecoy(Entity round)
        {
            var t = _w.GetComponentRO<GuidedRound>(round).Target;
            return _w.IsAlive(t) && _w.HasComponent<Decoy>(t);
        }

        private bool Dispense(Entity unit, DecoyKind kind) => _sys.Dispense(_w, unit, kind, HitModel.Now(_w));

        /// <summary>
        /// ⭐⭐ <c>CE-2188</c> — a flare salvo seduces an IR round that has a flare in its cone (rejection 0) and not one that is looking
        /// elsewhere; the dispenser loses the salvo (2), a second salvo inside the interval throws nothing, one after it does, and an
        /// empty dispenser spawns nothing. 🔴 Red-proof: with no seduction the round stays on the aircraft.
        /// </summary>
        [Fact]
        public void CE2188_AFlareSalvo_SeducesTheIrRoundThatSeesIt_AndCountsDown()
        {
            var heli = Aircraft();
            var seeing = Round(heli, GuidanceKind.InfraRed, rejection: 0f);
            var blind = Round(heli, GuidanceKind.InfraRed, rejection: 0f, lookingAway: true);
            Assert.True(Dispense(heli, DecoyKind.Flare));
            Assert.True(OnADecoy(seeing), "the round with the flare in its cone takes it");
            Assert.False(OnADecoy(blind), "the round looking elsewhere does not");
            Assert.Equal(2, _w.GetComponentRO<CountermeasureState>(heli).Flares);

            Assert.False(Dispense(heli, DecoyKind.Flare), "inside the interval");
            At(101.5);
            Assert.True(Dispense(heli, DecoyKind.Flare));
            Assert.Equal(0, _w.GetComponentRO<CountermeasureState>(heli).Flares);
            At(103.0);
            Assert.False(Dispense(heli, DecoyKind.Flare), "an empty dispenser throws nothing");
        }

        /// <summary>⭐ <c>CE-2188</c> — a seeker that always rejects (1) is never seduced; chaff fools a RADAR round and not an IR one.</summary>
        [Fact]
        public void CE2188_ARejectingSeeker_StaysOnTarget_AndChaffFoolsOnlyRadar()
        {
            var heli = Aircraft();
            var stubborn = Round(heli, GuidanceKind.InfraRed, rejection: 1f);
            var ir = Round(heli, GuidanceKind.InfraRed, rejection: 0f);
            var radar = Round(heli, GuidanceKind.Radar, rejection: 0f);   // a test-only radar round: no munition uses one yet (AD6)
            Assert.True(Dispense(heli, DecoyKind.Chaff));
            Assert.False(OnADecoy(ir), "chaff does not fool an IR seeker");
            Assert.True(OnADecoy(radar), "chaff fools a radar seeker");
            At(102);
            Assert.True(Dispense(heli, DecoyKind.Flare));
            Assert.False(OnADecoy(stubborn), "rejection 1: never seduced");
            Assert.True(OnADecoy(ir));
        }

        /// <summary>
        /// ⭐ <c>CE-2188</c> — the draw is DETERMINISTIC: the same world and sim time give the same outcome for every round, and across a
        /// spread of rounds at rejection 0.5 some are seduced and some are not (it is a draw, not a rule).
        /// </summary>
        [Fact]
        public void CE2188_TheSeductionDraw_IsTheSameEveryRun_AndADrawNotARule()
        {
            bool[] Run()
            {
                var t = new CountermeasureSystemTests();
                var heli = t.Aircraft();
                var rounds = new Entity[16];
                for (int i = 0; i < rounds.Length; i++) rounds[i] = t.Round(heli, GuidanceKind.InfraRed, rejection: 0.5f);
                t.Dispense(heli, DecoyKind.Flare);
                var seduced = new bool[rounds.Length];
                for (int i = 0; i < rounds.Length; i++) seduced[i] = t.OnADecoy(rounds[i]);
                t.Dispose();
                return seduced;
            }
            var a = Run();
            var b = Run();
            Assert.Equal(a, b);
            Assert.Contains(true, a);
            Assert.Contains(false, a);
        }

        /// <summary>⭐ <c>CE-2188</c> — the decoys fall away and burn out: they sink below the aircraft and are gone after their time.</summary>
        [Fact]
        public void CE2188_DecoysSink_AndBurnOut()
        {
            var heli = Aircraft();
            Assert.True(Dispense(heli, DecoyKind.Flare));
            int Decoys() { int n = 0; foreach (var _ in _w.Query().With<Decoy>().Build()) n++; return n; }
            Assert.Equal(2, Decoys());
            At(102);
            _sys.Execute(_w, 1f);
            foreach (var d in _w.Query().With<Decoy>().Build())
                Assert.True(_w.GetComponentRO<SimTransform>(d).Position.Z < 300f, "a flare sinks");
            At(105.5);
            _sys.Execute(_w, 0.016f);
            Assert.Equal(0, Decoys());
        }
    }
}
