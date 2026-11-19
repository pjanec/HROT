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
