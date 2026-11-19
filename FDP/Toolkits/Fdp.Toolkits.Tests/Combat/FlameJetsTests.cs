using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐ <c>CE-4635</c> (R-3111; docs/designs/weapons/DESIGN_Flame_Weapons.md §6) — the flame weapons' state as every node reads it, with NO component
    /// of its own: a jet is the active Flamethrower mount's last fuel shot, aimed by the posture, reaching what its incendiary munition says; a burning
    /// man is <see cref="EffectState.BurningUntil"/>.
    /// </summary>
    public sealed class FlameJetsTests : IDisposable
    {
        private const long FlameTank = 0x6A11, Fuel = 0x6A12, Rifle = 0x6A13, Bullet = 0x6A14;
        private readonly EntityRepository _w = new();

        public FlameJetsTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<WeaponPosture>();
            _w.RegisterComponent<WeaponAmmo>();
            _w.RegisterComponent<EffectState>();
            var db = new TkbDatabase();
            var tank = new TkbTemplate("test flame tank", FlameTank);
            tank.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = Fuel, InitialAmmunition = 100 } } });
            db.Register(tank);
            var fuel = new TkbTemplate("test flame fuel", Fuel);
            fuel.AddDescriptor(new WarheadDto { Kind = WarheadKind.Incendiary, FlameRangeM = 60f, FlameHalfAngleDeg = 10f, FlameIntensity = 0.8f, BurnSeconds = 30f });
            db.Register(fuel);
            var rifle = new TkbTemplate("test rifleman", Rifle);
            rifle.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = Bullet, InitialAmmunition = 30 } } });
            db.Register(rifle);
            db.Register(new TkbTemplate("test bullet", Bullet));
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _w.Dispose();

        private Entity Unit(long type, long ammo, double lastShot, double burstStart, Entity target = default, Vector3 point = default)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            _w.AddComponent(e, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = target, Point = point, ActiveMount = 0 });
            _w.AddComponent(e, new WeaponAmmo { Count = 1, Load0 = ammo, LastShotAt0 = lastShot, BurstStartedAt0 = burstStart });
            return e;
        }

        [Fact]
        public void AFlamethrowerThatJustSpentFuel_HasAJet_WithTheMunitionsReachAndCone_AimedWhereThePostureAims()
        {
            var u = Unit(FlameTank, Fuel, lastShot: 100.0, burstStart: 99.0, point: new Vector3(40, 5, 0));
            Assert.True(FlameJets.TryGet(_w, u, 100.1, out var jet));
            Assert.Equal(0, jet.Mount);
            Assert.Equal(60f, jet.RangeM);
            Assert.Equal(10f * MathF.PI / 180f, jet.HalfAngleRad, 5);
            Assert.Equal(0.8f, jet.Intensity, 5);
            Assert.Equal(99.0, jet.BurstStartedAt);
            Assert.Equal(new Vector3(40, 5, 0), (Vector3)jet.AimPoint);
        }

        /// <summary>📌 Measured in the editor (`fl-flamethrower-cone`): a mount with no loadout leaves <c>WeaponAmmo.Load0</c> at 0 — the mount's OWN round. The jet reads
        /// the mount's munition then. 🔴 Red-proof: drop the fallback ⇒ no jet.</summary>
        [Fact]
        public void AMountWithNoLoadout_ReadsItsOwnMunition()
        {
            var u = Unit(FlameTank, ammo: 0, lastShot: 100.0, burstStart: 99.0);
            Assert.True(FlameJets.TryGet(_w, u, 100.1, out var jet));
            Assert.Equal(60f, jet.RangeM);
        }

        [Fact]
        public void TheJet_GoesOut_WhenNoFuelWasSpentLately_AndNeverLightsForAnotherWeaponOrBeforeTheFirstShot()
        {
            var u = Unit(FlameTank, Fuel, lastShot: 100.0, burstStart: 99.0);
            Assert.False(FlameJets.TryGet(_w, u, 100.0 + FlameJets.LitSeconds + 0.01, out _));
            Assert.False(FlameJets.TryGet(_w, Unit(FlameTank, Fuel, lastShot: double.NegativeInfinity, burstStart: double.NegativeInfinity), 100.0, out _));
            Assert.False(FlameJets.TryGet(_w, Unit(Rifle, Bullet, lastShot: 100.0, burstStart: 100.0), 100.05, out _));
        }

        [Fact]
        public void TheJet_FollowsItsTarget_AndAllocatesNothing()
        {
            var victim = _w.CreateEntity();
            _w.AddComponent(victim, new SimTransform { Position = new Vector3(25, -3, 0), Rotation = Quaternion.Identity });
            var u = Unit(FlameTank, Fuel, lastShot: 100.0, burstStart: 99.5, target: victim);
            FlameJets.TryGet(_w, u, 100.1, out _);   // warm the TKB caches
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool lit = FlameJets.TryGet(_w, u, 100.1, out var jet);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(lit);
            Assert.Equal(victim, jet.Target);
            Assert.Equal(new Vector3(25, -3, FlameJets.TargetMiddleM), (Vector3)jet.AimPoint);   // its middle, not its feet
            Assert.Equal(0L, bytes);
        }

        /// <summary>⭐ F-4 — a man set alight burns until <see cref="EffectState.BurningUntil"/>; a second flame never shortens it; any view answers.</summary>
        [Fact]
        public void AManSetAlight_BurnsUntilHisTime_AndASecondFlameOnlyExtendsIt()
        {
            var state = new EffectState();
            Assert.True(Effects.Apply(ref state, HitEffect.Burning, 6f, now: 10.0));
            Assert.False(Effects.Apply(ref state, HitEffect.Burning, 0f, now: 11.0));
            Effects.Apply(ref state, HitEffect.Burning, 2f, now: 11.0);   // 13 < 16: kept
            Assert.Equal(16.0, (double)state.BurningUntil);
            var man = _w.CreateEntity();
            _w.AddComponent(man, state);
            Assert.True(Effects.IsBurning((Fdp.ModuleHost.Abstractions.ISimulationView)_w, man, 15.9));
            Assert.False(Effects.IsBurning((Fdp.ModuleHost.Abstractions.ISimulationView)_w, man, 16.0));
        }
    }
}
