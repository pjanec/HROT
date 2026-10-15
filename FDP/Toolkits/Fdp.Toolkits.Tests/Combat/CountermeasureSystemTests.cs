using System;
using System.Linq;
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
    /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (<c>docs/designs/brain-split/BS-1-DESIGN.md</c> §5.4c) — the flare / chaff DISPENSER is an
    /// ordinary weapon mount: the brain's <see cref="Countermeasures.Release"/> spends one round of its loadout and raises an ordinary
    /// <see cref="WeaponFireIntent"/>; the SimHost's <see cref="CountermeasureSystem"/> throws one decoy on <see cref="DecoyFlight.At"/>,
    /// seduces the homing rounds whose seeker it fools and that see it, each with one deterministic draw, and publishes the release as a
    /// <see cref="WeaponFireNotification"/>. ⛔ A dispenser is not a weapon.
    /// </summary>
    public sealed class CountermeasureSystemTests : IDisposable
    {
        private const long Helicopter = 9401, Flare = 9402, Chaff = 9403, Bullet = 9404;
        private readonly EntityRepository _w = new();
        private readonly CountermeasureSystem _sys = new();

        public CountermeasureSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<GuidedRound>();
            _w.RegisterComponent<Decoy>();
            _w.RegisterComponent<WeaponState>();
            _w.RegisterComponent<AmmoLoadout>();
            _w.RegisterComponent<WeaponMountInfo>();
            _w.RegisterComponent<PartMetadata>();
            _w.RegisterEvent<WeaponFireIntent>();
            _w.RegisterEvent<WeaponFireNotification>();
            var db = new TkbDatabase();
            // an UNARMED helicopter: its only mount is the dispenser (two flares, one chaff, 120 rpm)
            var heli = new TkbTemplate("countermeasure test helicopter", Helicopter);
            heli.AddDescriptor(new WeaponSuiteDto
            {
                Mounts =
                {
                    new WeaponMountDto
                    {
                        Category = WeaponCategory.Countermeasure, InitialAmmunition = 3, RateOfFire = 120f, AmmoGuid = Flare,
                        Loadout = { new AmmoLoadDto { AmmoGuid = Flare, Rounds = 2 }, new AmmoLoadDto { AmmoGuid = Chaff, Rounds = 1 } },
                    },
                },
            });
            db.Register(heli);
            var flare = new TkbTemplate("test flare", Flare) { DisType = new DISEntityType { Kind = 8, Domain = 2, Category = 2 } };
            flare.AddDescriptor(new ExpendableDto { LifeSeconds = 5f, SinkMps = 8f });
            db.Register(flare);
            var chaff = new TkbTemplate("test chaff", Chaff) { DisType = new DISEntityType { Kind = 8, Domain = 2, Category = 1 } };
            chaff.AddDescriptor(new ExpendableDto { LifeSeconds = 8f, SinkMps = 2f });
            db.Register(chaff);
            db.Register(new TkbTemplate("test bullet", Bullet) { DisType = new DISEntityType { Kind = 2, Domain = 8, Category = 2 } });
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
            // what CombatTkbTranslator gives mount 0 on the owner: the total load and the loadout
            _w.AddComponent(e, new WeaponState { Ammo = 3, MaxAmmo = 3 });
            _w.AddComponent(e, AmmoLoadout.From(new[] { new AmmoLoadDto { AmmoGuid = Flare, Rounds = 2 }, new AmmoLoadDto { AmmoGuid = Chaff, Rounds = 1 } }));
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

        /// <summary>The brain releases; the bus swaps (the frame boundary); the SimHost's system runs. True when it was released.</summary>
        private bool ReleaseAndThrow(Entity unit, DecoyKind kind)
        {
            bool released = Countermeasures.Release(_w, unit, kind);
            _w.Bus.SwapBuffers();
            _sys.Execute(_w, 0.016f);
            return released;
        }

        private System.Collections.Generic.List<Entity> Decoys()
        {
            var list = new System.Collections.Generic.List<Entity>();
            foreach (var e in _w.Query().With<Decoy>().Build()) list.Add(e);
            return list;
        }

        private WeaponFireNotification[] Notifications()
        {
            _w.Bus.SwapBuffers();
            return _w.Bus.Read<WeaponFireNotification>().ToArray();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2188</c> (§5.4c; 🔒 user: <i>"Number of countermeasures left same as number of weapon rounds left"</i>) — a release is
        /// a round spent on the BRAIN: the loadout's flare count and the mount's total drop by one, the mount waits 60 ÷ its rate of fire,
        /// and the request is an ordinary <see cref="WeaponFireIntent"/> on the dispenser mount with the flare's type. Inside the interval
        /// nothing leaves; with the flares gone a flare is refused while the chaff still goes. 🔴 Red-proof: with the spend removed the
        /// third flare leaves.
        /// </summary>
        [Fact]
        public void CE2188_ARelease_SpendsOneRoundOfTheDispenserMount_AndIsAnOrdinaryFireRequest()
        {
            var heli = Aircraft();
            Assert.True(Countermeasures.Release(_w, heli, DecoyKind.Flare));
            Assert.Equal(1, Countermeasures.Left(_w, heli, DecoyKind.Flare));
            Assert.Equal(2, _w.GetComponentRO<WeaponState>(heli).Ammo);
            Assert.Equal(0.5f, _w.GetComponentRO<WeaponState>(heli).CooldownSecondsRemaining, 3);
            _w.Bus.SwapBuffers();
            var intent = Assert.Single(_w.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal(heli, intent.Shooter);
            Assert.Equal(0, intent.WeaponIndex);
            Assert.Equal(Flare, intent.AmmoGuid);
            Assert.True(intent.AtPoint);
            Assert.Equal(new Vector3(1000, 0, 300), intent.TargetPoint);

            Assert.False(Countermeasures.Release(_w, heli, DecoyKind.Flare), "inside the interval");
            _w.GetComponentRW<WeaponState>(heli).CooldownSecondsRemaining = 0f;
            Assert.True(Countermeasures.Release(_w, heli, DecoyKind.Flare));
            _w.GetComponentRW<WeaponState>(heli).CooldownSecondsRemaining = 0f;
            Assert.False(Countermeasures.Release(_w, heli, DecoyKind.Flare), "no flares left");
            Assert.True(Countermeasures.Release(_w, heli, DecoyKind.Chaff), "the chaff is counted apart");
            Assert.Equal(0, _w.GetComponentRO<WeaponState>(heli).Ammo);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2188</c> — the SimHost throws ONE decoy per release: an IR round with the flare in its cone (rejection 0) takes it, one
        /// looking elsewhere does not; the release is published as a fire with the flare's type, quantity 1, the aircraft's position and
        /// velocity and the sim time — what every viewer draws it from. 🔴 Red-proof: with no seduction the round stays on the aircraft.
        /// </summary>
        [Fact]
        public void CE2188_TheSimHost_ThrowsOneDecoy_SeducesTheRoundThatSeesIt_AndPublishesTheFire()
        {
            var heli = Aircraft();
            var seeing = Round(heli, GuidanceKind.InfraRed, rejection: 0f);
            var blind = Round(heli, GuidanceKind.InfraRed, rejection: 0f, lookingAway: true);
            Assert.True(ReleaseAndThrow(heli, DecoyKind.Flare));
            Assert.Single(Decoys());
            Assert.True(OnADecoy(seeing), "the round with the flare in its cone takes it");
            Assert.False(OnADecoy(blind), "the round looking elsewhere does not");

            var n = Assert.Single(Notifications());
            Assert.Equal(heli, n.Shooter);
            Assert.Equal(Flare, n.AmmoGuid);
            Assert.Equal(1, n.Quantity);
            Assert.Equal(Entity.Null, n.Round);
            Assert.Equal(new Vector3(1000, 0, 300), n.Origin);
            Assert.Equal(new Vector3(0, 60, 0), n.Velocity);
            Assert.Equal(100.0, n.FiredAt, 6);
            Assert.True(Munitions.IsExpendable(_w, n.AmmoGuid), "a viewer reads it as an expendable");
        }

        /// <summary>⭐ <c>CE-2188</c> — a seeker that always rejects (1) is never seduced; chaff fools a RADAR round and not an IR one.</summary>
        [Fact]
        public void CE2188_ARejectingSeeker_StaysOnTarget_AndChaffFoolsOnlyRadar()
        {
            var heli = Aircraft();
            var stubborn = Round(heli, GuidanceKind.InfraRed, rejection: 1f);
            var ir = Round(heli, GuidanceKind.InfraRed, rejection: 0f);
            var radar = Round(heli, GuidanceKind.Radar, rejection: 0f);   // a test-only radar round: no munition uses one yet (AD6)
            Assert.True(ReleaseAndThrow(heli, DecoyKind.Chaff));
            Assert.False(OnADecoy(ir), "chaff does not fool an IR seeker");
            Assert.True(OnADecoy(radar), "chaff fools a radar seeker");
            _w.GetComponentRW<WeaponState>(heli).CooldownSecondsRemaining = 0f;
            Assert.True(ReleaseAndThrow(heli, DecoyKind.Flare));
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
                t.ReleaseAndThrow(heli, DecoyKind.Flare);
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

        /// <summary>
        /// ⭐ <c>CE-2188</c> (F6) — the decoy is exactly where <see cref="DecoyFlight.At"/> puts it — THE path a viewer draws from the
        /// notification — it sinks below the aircraft, and it is gone after its munition's life.
        /// </summary>
        [Fact]
        public void CE2188_TheDecoy_FollowsDecoyFlight_SinksAndBurnsOut()
        {
            var heli = Aircraft();
            Assert.True(ReleaseAndThrow(heli, DecoyKind.Flare));
            var n = Assert.Single(Notifications());
            At(102);
            _sys.Execute(_w, 1f);
            var decoy = Assert.Single(Decoys());
            var p = _w.GetComponentRO<SimTransform>(decoy).Position;
            Assert.Equal(DecoyFlight.At(n.Origin, n.Velocity, 8f, (float)(102 - n.FiredAt)), p);
            Assert.True(p.Z < 300f, "a flare sinks");
            Assert.True(p.Y > 0f && p.Y < 60f * 2f, "it keeps some of the aircraft's speed, and loses it");
            At(105.5);
            _sys.Execute(_w, 0.016f);
            Assert.Empty(Decoys());
        }

        /// <summary>⭐ <c>CE-2188</c> (§5.4c) — the expendable check reads the DIS type: flare 8.2.2, chaff 8.2.1; a bullet is no expendable.</summary>
        [Fact]
        public void CE2188_Munitions_ReadsTheExpendableFromItsDisType()
        {
            var tkb = _w.GetSingletonManaged<ITkbDatabase>();
            Assert.Equal(DecoyKind.Flare, Munitions.DecoyOf(tkb, Flare));
            Assert.Equal(DecoyKind.Chaff, Munitions.DecoyOf(tkb, Chaff));
            Assert.False(Munitions.IsExpendable(tkb, Bullet));
            Assert.Null(Munitions.DecoyOf(tkb, Bullet));
            Assert.False(Munitions.IsExpendable(tkb, 0));
            Assert.Equal(8f, Munitions.ExpendableOf(tkb, Chaff)!.LifeSeconds);
        }

        /// <summary>
        /// ⛔⛔ <c>CE-2188</c> (§5.4c) — a dispenser is NOT a weapon: an aircraft whose only mount is one reads UNARMED to a threat reader
        /// and to its own strength, has no primary weapon, and its flares are worth nothing against any target. 🔴 Red-proof: without the
        /// checks the helicopter reads armed (danger 1) the moment it gets its dispenser.
        /// </summary>
        [Fact]
        public void CE2188_ADispenserOnlyAircraft_IsUnarmed()
        {
            var heli = Aircraft();
            var other = _w.CreateEntity();
            _w.AddComponent(other, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            Assert.True(Countermeasures.DispenserOnly(_w, heli));
            Assert.Equal(Fdp.Toolkit.Perception.ThreatDanger.Unarmed, Fdp.Toolkit.Perception.ThreatDanger.Of(_w, other, heli));
            Assert.True(WeaponSystems.WeaponOf(_w, heli).IsNull, "no primary weapon");
            var mount = CombatTkb.MountOf(_w, heli, 0);
            Assert.Equal(0f, AmmoChoice.ExpectedDamage(_w, heli, other, mount, Flare));
        }
    }
}
