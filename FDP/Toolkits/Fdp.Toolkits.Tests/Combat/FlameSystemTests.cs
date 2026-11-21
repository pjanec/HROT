using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-4635</c> FL-1 (R-3111; docs/designs/weapons/DESIGN_Flame_Weapons.md F-1…F-4) — what a lit flame jet sets alight: a man in the cone after
    /// 0.3 s of it, a soft vehicle after 1.5 s; never one outside the cone or the reach, never one behind a hull, never an armoured hull; and the burning
    /// man then burns (<see cref="BurnSystem"/>).
    /// </summary>
    public sealed class FlameSystemTests : IDisposable
    {
        private const long FlameMan = 0x6B01, Fuel = 0x6B02, Rifleman = 0x6B03, Car = 0x6B04, ArmouredCar = 0x6B05;
        private const float Dt = 0.05f;
        private const string Flat = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,200,200],"groundZ":0},"features":[]}
            """;
        private readonly EntityRepository _w = new();
        private readonly FlameSystem _flame = new();
        private readonly BurnSystem _burn = new();
        private double _now = 100.0;
        private Entity _thrower;

        public FlameSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<WeaponPosture>();
            _w.RegisterComponent<WeaponAmmo>();
            _w.RegisterComponent<EffectState>();
            _w.RegisterComponent<FireState>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<VehicleParams>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterManagedComponent<TerrainWorld>();
            _w.SetSingletonManaged(TerrainWorldParser.Parse(Flat));
            var db = new TkbDatabase();
            var flameMan = new TkbTemplate("test flamethrower man", FlameMan);
            flameMan.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = Fuel, InitialAmmunition = 100 } } });
            db.Register(flameMan);
            var fuel = new TkbTemplate("test flame fuel", Fuel);
            fuel.AddDescriptor(new WarheadDto { Kind = WarheadKind.Incendiary, FlameRangeM = 40f, FlameHalfAngleDeg = 8f, FlameIntensity = 1f, BurnSeconds = 60f });
            db.Register(fuel);
            db.Register(new TkbTemplate("test rifleman", Rifleman));
            db.Register(new TkbTemplate("test car", Car));
            var armoured = new TkbTemplate("test armoured car", ArmouredCar);
            armoured.AddDescriptor(new CombatPlatformDefDto { ArmorFront = 60f, ArmorSide = 45f, ArmorRear = 30f });
            db.Register(armoured);
            _w.SetSingletonManaged<ITkbDatabase>(db);
            Clock();
            _thrower = Body(FlameMan, new Vector3(50, 100, 0), VehicleClass.Pedestrian);
            _w.AddComponent(_thrower, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Point = new Vector3(80, 100, 1), ActiveMount = 0 });
            _w.AddComponent(_thrower, new WeaponAmmo { Count = 1, Load0 = Fuel, LastShotAt0 = double.NegativeInfinity, BurstStartedAt0 = double.NegativeInfinity });
        }

        public void Dispose() => _w.Dispose();

        private void Clock() => _w.SetSingleton(new GlobalTime { TotalTime = _now, DeltaTime = Dt });

        private Entity Body(long type, Vector3 at, VehicleClass cls, float length = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            _w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new VehicleParams { Class = cls, Length = length, Width = length > 0f ? 1.8f : 0f });
            if (cls != VehicleClass.Pedestrian) _w.AddComponent(e, new PhysicsCollider { Radius = 2.4f, Height = 1.6f, CollisionLayer = 1 });
            return e;
        }

        /// <summary>Fires for <paramref name="seconds"/>: a fuel shot every tick (the brain's mirror would stamp it), the flame then the burn.</summary>
        private void Fire(double seconds)
        {
            for (double t = 0; t < seconds - 1e-9; t += Dt)
            {
                _now += Dt; Clock();
                ref var ammo = ref _w.GetComponentRW<WeaponAmmo>(_thrower);
                if (double.IsNegativeInfinity(ammo.BurstStartedAt0)) ammo.BurstStartedAt0 = _now;
                ammo.LastShotAt0 = _now;
                _flame.Execute(_w, Dt);
                _burn.Execute(_w, Dt);
            }
        }

        private bool Burning(Entity man) => Effects.IsBurning((Fdp.ModuleHost.Abstractions.ISimulationView)_w, man, _now);

        /// <summary>A carrier whose flamethrower's muzzle stands <paramref name="mouthAheadM"/> ahead of its body (a dragon's mouth at the end of its neck) — the thrower
        /// from now on.</summary>
        private Entity Carrier(float mouthAheadM)
        {
            const long Dragonish = 0x6B06;
            var db = (TkbDatabase)_w.GetSingletonManaged<ITkbDatabase>();
            if (!db.TryGetByType(Dragonish, out _))
            {
                var t = new TkbTemplate("test flame carrier", Dragonish);
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = Fuel, InitialAmmunition = 100,
                                                                                     MuzzleOffsetX = mouthAheadM, MuzzleOffsetZ = 1f } } });
                db.Register(t);
            }
            _thrower = Body(Dragonish, new Vector3(50, 100, 0), VehicleClass.Pedestrian);
            _w.AddComponent(_thrower, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Point = new Vector3(80, 100, 1), ActiveMount = 0 });
            _w.AddComponent(_thrower, new WeaponAmmo { Count = 1, Load0 = Fuel, LastShotAt0 = double.NegativeInfinity, BurstStartedAt0 = double.NegativeInfinity });
            return _thrower;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4634</c> D-1 (DESIGN_Dragon DR-5) — the jet LEAVES THE MOUTH: a carrier whose type puts the flamethrower's muzzle 10 m ahead of its body
        /// reaches 40 m from there — a man 45 m from the body (35 from the mouth) burns; a man-portable flamethrower (no muzzle) still reaches 40 m from his
        /// eye. 🔴 Red-proof: the origin back at the eye ⇒ the far man never burns.
        /// </summary>
        [Fact]
        public void CE4634_TheJetLeavesTheMuzzle_WhenTheTypePlacesOne()
        {
            Carrier(mouthAheadM: 10f);
            var far = Body(Rifleman, new Vector3(95, 100, 0), VehicleClass.Pedestrian);   // 45 m from the body, 35 from the mouth
            Fire(0.5);
            Assert.True(Burning(far));
            Assert.Equal(new Vector3(60f, 100f, 1f), FlameJets.Origin(_w, _thrower, FlameJet()));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4634</c> D-1 (DR-6) — a carrier's OWN RIDERS are never in its own jet: a man aboard it (its pillion) standing in the cone does not burn; the
        /// same man not aboard does. 🔴 Red-proof: the riders' test dropped ⇒ the rider burns.
        /// </summary>
        [Fact]
        public void CE4634_ACarriersOwnRider_IsNeverInItsOwnJet()
        {
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var carrier = Carrier(mouthAheadM: 2f);
            var rider = Body(Rifleman, new Vector3(62, 100, 0), VehicleClass.Pedestrian);
            _w.AddComponent(rider, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = carrier, Seat = 1 });
            var stranger = Body(Rifleman, new Vector3(64, 100, 0), VehicleClass.Pedestrian);
            Fire(0.5);
            Assert.False(Burning(rider));
            Assert.True(Burning(stranger));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3490</c> BA-S1 (DESIGN_Breath_Attack BA-6) — the Protocol III hold judges THE jet this system burns with: a flyer 12 m up whose mouth is 10 m ahead
        /// of its body holds its breath for a neutral 45 m from the body (35 m along the slant from the mouth, inside the 40 m reach) — and that man WOULD burn; a
        /// neutral 58 m out (beyond the reach from the mouth) holds nothing and never burns. 🔴 Red-proof: the old hold (2-D, 40 m from the body's centre) misses
        /// the 45 m man.
        /// </summary>
        [Fact]
        public void CE3490_TheBreathHold_CoversTheJetFromTheMouth_InThreeD()
        {
            _w.RegisterComponent<EntityInfo>();
            var flyer = Carrier(mouthAheadM: 10f);
            _w.GetComponentRW<SimTransform>(flyer).Position = new Vector3(50, 100, 12);
            _w.AddComponent(flyer, new EntityInfo { ForceId = ForceId.Hostile });
            var aim = new Vector3(85, 100, 1);
            var mount = CombatTkb.MountOf(_w, flyer, 0);
            Entity Neutral(float x)
            {
                var e = Body(Rifleman, new Vector3(x, 100, 0), VehicleClass.Pedestrian);
                _w.AddComponent(e, new EntityInfo { ForceId = ForceId.Neutral });
                _w.AddComponent(e, new PhysicsCollider { Radius = 0.4f });
                return e;
            }
            var far = Neutral(108f);
            Assert.False(LineOfFire.NeutralInFlame(_w, flyer, 0, mount, 0L, aim), "58 m out: beyond the reach from the mouth");
            var near = Neutral(95f);
            Assert.True(LineOfFire.NeutralInFlame(_w, flyer, 0, mount, 0L, aim), "45 m from the body, 35 m along the slant from the mouth");
            _w.GetComponentRW<WeaponPosture>(flyer).Point = aim;
            Fire(0.5);
            Assert.True(Burning(near), "the man the hold protects is the man the jet burns");
            Assert.False(Burning(far));
        }

        private FlameJets.Jet FlameJet() => FlameJets.TryGet(_w, _thrower, _now, out var jet) ? jet : throw new InvalidOperationException("no lit jet");

        [Fact]
        public void AManInTheCone_IsSetAlight_After0_3sOfFlame_NotBefore()
        {
            var man = Body(Rifleman, new Vector3(70, 100, 0), VehicleClass.Pedestrian);
            Fire(0.2);
            Assert.False(Burning(man));
            Fire(0.15);
            Assert.True(Burning(man));
            Assert.Equal(1, _flame.Ignited);
        }

        [Fact]
        public void AManOutsideTheCone_OrBeyondTheReach_NeverBurns()
        {
            var aside = Body(Rifleman, new Vector3(70, 115, 0), VehicleClass.Pedestrian);   // 15 m off the axis at 20 m out (the cone: ~2.8 m)
            var far = Body(Rifleman, new Vector3(95, 100, 0), VehicleClass.Pedestrian);     // 45 m out (reach 40)
            Fire(2.0);
            Assert.False(Burning(aside));
            Assert.False(Burning(far));
        }

        /// <summary>🔴 Red-proof: drop the sight test in <see cref="FlameSystem"/> ⇒ the man behind the car burns.</summary>
        [Fact]
        public void ASoftCar_CatchesFireAfter1_5s_AndHidesTheManBehindIt_AnArmouredHullNever()
        {
            var car = Body(Car, new Vector3(62, 100, 0), VehicleClass.PersonalCar, length: 4.5f);
            var behind = Body(Rifleman, new Vector3(68, 100, 0), VehicleClass.Pedestrian);
            var armoured = Body(ArmouredCar, new Vector3(60, 110, 0), VehicleClass.Truck, length: 6f);
            _w.GetComponentRW<WeaponPosture>(_thrower).Point = new Vector3(80, 100, 1);
            Fire(1.4);
            Assert.False(_w.HasComponent<FireState>(car) && _w.GetComponentRO<FireState>(car).IsBurning(_now));
            Fire(0.2);
            Assert.True(_w.HasComponent<FireState>(car) && _w.GetComponentRO<FireState>(car).IsBurning(_now), "the car caught fire");
            Assert.False(Burning(behind), "the car stopped the flame");

            _w.GetComponentRW<WeaponPosture>(_thrower).Point = new Vector3(70, 120, 1);   // swing onto the armoured hull
            Fire(3.0);
            Assert.False(_w.HasComponent<FireState>(armoured), "an armoured hull does not catch fire");
        }

        [Fact]
        public void TheBurningMan_LosesATenthOfHisHealthEverySecond_AndANewStayStartsTheCountAgain()
        {
            var man = Body(Rifleman, new Vector3(70, 100, 0), VehicleClass.Pedestrian);
            Fire(0.35);
            Assert.True(Burning(man));
            float hp = _w.GetComponentRO<Health>(man).Current;
            Fire(2.0);   // still in the flame: burning on, one tenth a whole second
            Assert.InRange(_w.GetComponentRO<Health>(man).Current, hp - 21f, hp - 9f);
        }
    }
}
