using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Common.Tests
{
    public class NedTkbBuilderCombatTests
    {
        [Fact]
        public void WithCombat_StoresWeaponCapabilitiesDescriptor()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            Assert.True(template.HasDescriptor<WeaponCapabilitiesDto>());
        }

        [Fact]
        public void WithCombat_WeaponCapabilities_HasExpectedEffectiveRange()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
            Assert.Equal(3000f, dto.EffectiveRange);
        }

        [Fact]
        public void WithCombat_WeaponCapabilities_HasExpectedRateOfFire()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
            Assert.Equal(6f, dto.RateOfFire);
        }

        [Fact]
        public void WithCombat_WeaponCapabilities_HasExpectedMagazineCapacity()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
            Assert.Equal(42, dto.MagazineCapacity);
        }

        // ── ⭐ Buildings programme Stage 0 — the resolver reports what the catalog's numbers ARE and where they came from;
        //    the numbers themselves are UNCHANGED (📄 docs/DESIGN_Terrain_Combat_Tuning.md §2, §6 T-0). ──────────────

        private static Fdp.Toolkit.Tkb.Parameters.ResolvedParameter Get(long type, string name)
            => System.Linq.Enumerable.Single(
                Fdp.Toolkit.Tkb.Parameters.ParameterResolver.ResolveAll(BuildDatabase().GetByType(type)!), p => p.Name == name);

        [Fact]
        public void Stage0_M1_DerivedNumbersAreGenerated_StatedOnesExplicit_EyesOnEngineFallbacks_ValuesUnchanged()
        {
            const long m1 = TkbEntityTypes.Tank_M1Abrams;
            var health = Get(m1, Fdp.Toolkit.Tkb.Parameters.ParameterNames.MaxHealth);
            Assert.Equal(3000f, health.Value);                                   // 600 mm × 5 — as before Stage 0
            // ⭐ M6 (D-F) — RE-PINNED: the M1 now STATES its health (the same 3000), so it reads Explicit, no longer Generated
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, health.Provenance);

            var mv = Get(m1, "Weapon[0].MuzzleVelocity");
            Assert.Equal(1500f, mv.Value);                                       // range 3000 × 0.5 — as before
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Generated, mv.Provenance);

            var dmg = Get(m1, "Weapon[0].DamagePerHit");
            Assert.Equal(1200f, dmg.Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, dmg.Provenance);
            Assert.Equal(650f, Get(m1, "Weapon[0].Penetration").Value);

            // ⭐ M5 — RE-PINNED: the M1 now states where its crew sees from (its sight, 2.5 m), no longer a standing man's 1.7
            var eye = Get(m1, Fdp.Toolkit.Tkb.Parameters.ParameterNames.EyeStanding);
            Assert.Equal(2.5f, eye.Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, eye.Provenance);
        }

        /// <summary>
        /// ⭐⭐ M5 / M6 (DESIGN_Missiles_And_Ammo §5, DESIGN_Armour_Hit_Model §2) — the builder carries the new combat data through:
        /// the tanks' stated health (= the old front × 5, so no fight moves on it), roof and shaped-charge armour, the main gun's
        /// loadout (APFSDS / HEAT-MP / HE, summing to the old ammunition), the coaxial MG on the turret, the TOW's lay time and
        /// every vehicle's eye height.
        /// </summary>
        [Fact]
        public void M5_M6_TheTanks_CarryLoadoutCoaxRoofAndShapedChargeArmour_TheirHealthUnchanged()
        {
            var db = BuildDatabase();
            foreach (var (type, health, top, frontCe, apfsds, eye) in new[]
                     {
                         (TkbEntityTypes.Tank_M1Abrams, 3000f, 60f, 900f, (long)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmApfsds, 2.5f),
                         (TkbEntityTypes.Tank_T72, 2500f, 40f, 700f, (long)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb125mmApfsds, 2.2f),
                     })
            {
                var t = db.GetByType(type)!;
                var p = t.GetDescriptor<CombatPlatformDefDto>()!;
                Assert.Equal(health, p.MaxHealth);
                Assert.Equal(top, p.ArmorTop);
                Assert.Equal(frontCe, p.ArmorFrontCe);
                Assert.Equal(eye, t.GetDescriptor<SensorCapabilitiesDto>()!.EyeHeightStanding);

                var mounts = t.GetDescriptor<WeaponSuiteDto>()!.Mounts;
                Assert.Equal(2, mounts.Count);
                var gun = mounts[0];
                Assert.Equal(unchecked((ulong)apfsds), gun.AmmoGuid);
                Assert.Equal(gun.InitialAmmunition, System.Linq.Enumerable.Sum(gun.Loadout, l => l.Rounds));   // the same rounds, now sorted
                Assert.Equal(new[] { unchecked((ulong)apfsds), (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbTankHeatMp, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbTankHeFrag },
                             System.Linq.Enumerable.Select(gun.Loadout, l => l.AmmoGuid));

                var coax = mounts[1];
                Assert.Equal(WeaponCategory.MachineGun, coax.Category);
                Assert.Equal(0, coax.TurretIndex);
                Assert.Equal((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, coax.AmmoGuid);
                Assert.Equal(200, coax.MagazineSize);
            }

            var bradley = db.GetByType(TkbEntityTypes.IFV_Bradley)!;
            Assert.Equal(500f, bradley.GetDescriptor<CombatPlatformDefDto>()!.MaxHealth);   // = 100 × 5, stated
            Assert.Equal(1.5f, bradley.GetDescriptor<WeaponSuiteDto>()!.Mounts[1].AimSeconds);   // the TOW's lay
        }

        [Fact]
        public void Stage0_Rifleman_HealthGeneratedFromBodyArmour_DamageExplicit()
        {
            const long rifleman = TkbEntityTypes.Infantry_Rifleman;
            Assert.Equal(25f, Get(rifleman, Fdp.Toolkit.Tkb.Parameters.ParameterNames.MaxHealth).Value);   // 5 mm × 5
            Assert.Equal(25f, Get(rifleman, "Weapon[0].DamagePerHit").Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, Get(rifleman, "Weapon[0].DamagePerHit").Provenance);
            Assert.Equal(150f, Get(rifleman, "Weapon[0].MuzzleVelocity").Value);                             // 300 m × 0.5
        }

        /// <summary>
        /// ⭐⭐ M9 (DESIGN_Vehicle_Exposure_Cycle §6, D-K) — the technicals: a pickup that drives like a car (25 m/s, 6 m/s back), no armour
        /// to speak of (5 mm, roof 3), 300 hp and a mobility kill below 60 %, its weapon on a pintle — the HMG all round and up to +70°
        /// (it can fire at a drone) and on the move, the ATGM ±120° and halted for the flight — a 2.2 m eye in the bed, 2 km sight,
        /// heard as a wheeled engine, DIS small wheeled utility vehicle, hostile.
        /// ⭐⭐ WS-4 (W-F, W-I; DESIGN_Weapon_Systems §5) — re-pinned THROUGH THE PRESETS, same numbers: the weapon is no longer the pickup's
        /// type data but the weapon system its bed station starts with (<c>SeatDto.StartWith</c>), its arc that weapon's turret ∩ the
        /// station's limits, its pivot still 1.3 m back and 1.9 m up; and a rifleman starts in the gunner seat (the steer: a gun with
        /// nobody to fire it never fires).
        /// </summary>
        [Fact]
        public void M9_TheTechnicals_APickupWithAPintle_ShootAndScoot()
        {
            var db = BuildDatabase();
            foreach (var (type, elevMax, traverse, category, onTheMove, ammo) in new[]
                     {
                         (TkbEntityTypes.Technical_Hmg, 70f, 180f, WeaponCategory.MachineGun, true, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbHmgAp),
                         (TkbEntityTypes.Technical_Atgm, 20f, 120f, WeaponCategory.Atgm, false, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbKornetMissile),
                     })
            {
                var t = db.GetByType(type)!;
                var kin = t.GetDescriptor<VehicleParametersDto>()!;
                Assert.Equal(CarKinem.Core.VehicleClass.PersonalCar, kin.VehicleClass);
                Assert.Equal((25f, 6f), (kin.MaxSpeedFwd, kin.MaxSpeedRev));

                var p = t.GetDescriptor<CombatPlatformDefDto>()!;
                Assert.Equal((5f, 5f, 5f, 3f), (p.ArmorFront, p.ArmorSide, p.ArmorRear, p.ArmorTop));
                Assert.Equal((300f, 0.6f), (p.MaxHealth, p.MobilityKillBelowFraction));
                Assert.Null(t.GetDescriptor<WeaponSuiteDto>());   // ⭐ WS-4 — the pickup has no gun of its own

                var seats = t.GetDescriptor<CarrierDefDto>()!.Seats;
                var station = System.Linq.Enumerable.Single(seats, s => s.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Station);
                Assert.Equal(TkbEntityTypes.Infantry_Rifleman,
                    System.Linq.Enumerable.Single(seats, s => s.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Gunner).StartWith);
                var gun = db.GetByType(station.StartWith)!;
                Assert.NotNull(gun.GetDescriptor<WeaponSystemDto>());
                var turret = gun.GetDescriptor<TurretSuiteDto>()!.Turrets[0];
                Assert.Equal(elevMax, System.MathF.Min(turret.ElevationMaxDeg, station.ElevationMaxDeg ?? float.MaxValue));
                Assert.Equal(traverse, System.MathF.Min(turret.TraverseMaxDeg, station.TraverseMaxDeg ?? float.MaxValue));
                Assert.Equal(-traverse, System.MathF.Max(turret.TraverseMinDeg, station.TraverseMinDeg ?? float.MinValue));
                Assert.Equal(-1.3f, station.X);                                         // the M9 pintle's pivot
                Assert.Equal(1.9f, station.Z + turret.PivotZ, 4);

                var weapon = System.Linq.Enumerable.Single(gun.GetDescriptor<WeaponSuiteDto>()!.Mounts);
                Assert.Equal((category, onTheMove, ammo, 0), (weapon.Category, weapon.FireOnMove!.Value, weapon.AmmoGuid, weapon.TurretIndex));
                Assert.Equal(0f, weapon.Penetration);   // the round's own (the munition template)

                var s = t.GetDescriptor<SensorCapabilitiesDto>()!;
                Assert.Equal((2000f, 2.2f), (s.VisionRange, s.EyeHeightStanding));
                Assert.Equal(SoundSourceClass.WheeledEngine, t.GetDescriptor<SignaturesDto>()!.Acoustic!.MovingClass);
                Assert.Equal((1, 1, 6), (t.DisType.Kind, t.DisType.Domain, t.DisType.Category));
                Assert.Equal(Fdp.Core.ForceId.Hostile, t.GetDescriptor<BehaviorProfileDto>()!.Faction);
            }
        }

        /// <summary>
        /// ⭐⭐ Weapon systems WS-4 (W-F; DESIGN_Weapon_Systems §5) — the catalogue: nine weapon-system types (600–608), each a
        /// <c>Gen.WeaponSystem</c> entity of DIS class 1.1.*.50 with its gun as mount 0; the crew-served ones are manned (a Gunner seat, a
        /// loader's for a two-man weapon) and turret-mounted, the shoulder ones carried and fired carried; the sentry gun is automatic (no
        /// seat, a sensor). Every one has a brain (it holds unless the weapon controls itself, §6). The bare Toyota (106) has a driver, a gunner and one pintle station the gunner operates, empty.
        /// </summary>
        [Fact]
        public void WS4_TheWeaponSystems_AndTheBarePickup()
        {
            var db = BuildDatabase();
            foreach (var (type, kinds, portable, manned, turret, sub) in new[]
                     {
                         (TkbEntityTypes.WeaponSystem_M2Hmg, MountKinds.Ground | MountKinds.Pintle, WeaponPortability.TwoMan, 2, true, 1),
                         (TkbEntityTypes.WeaponSystem_DshkHmg, MountKinds.Ground | MountKinds.Pintle, WeaponPortability.TwoMan, 2, true, 1),
                         (TkbEntityTypes.WeaponSystem_M134Minigun, MountKinds.Door | MountKinds.Pintle, WeaponPortability.Static, 1, true, 2),
                         (TkbEntityTypes.WeaponSystem_Javelin, MountKinds.Ground | MountKinds.Carried, WeaponPortability.OneMan, 1, false, 3),
                         (TkbEntityTypes.WeaponSystem_Nlaw, MountKinds.Ground | MountKinds.Carried, WeaponPortability.OneMan, 1, false, 4),
                         (TkbEntityTypes.WeaponSystem_Law, MountKinds.Carried, WeaponPortability.OneMan, 1, false, 4),
                         (TkbEntityTypes.WeaponSystem_KornetTripod, MountKinds.Ground | MountKinds.Pintle, WeaponPortability.TwoMan, 2, true, 3),
                         (TkbEntityTypes.WeaponSystem_TowTripod, MountKinds.Ground | MountKinds.Pintle, WeaponPortability.TwoMan, 2, true, 3),
                         (TkbEntityTypes.WeaponSystem_SentryGun, MountKinds.Ground | MountKinds.Pintle, WeaponPortability.Static, 0, true, 5),
                     })
            {
                var t = db.GetByType(type)!;
                var ws = t.GetDescriptor<WeaponSystemDto>()!;
                Assert.Equal((kinds, portable), (ws.MountKinds, ws.Portable));
                Assert.Equal(portable == WeaponPortability.OneMan, ws.FireWhileCarried);
                Assert.Equal((1, 1, 50, sub), (t.DisType.Kind, t.DisType.Domain, t.DisType.Category, (int)t.DisType.Subcategory));
                Assert.Single(t.GetDescriptor<WeaponSuiteDto>()!.Mounts);
                Assert.Equal(turret, t.GetDescriptor<TurretSuiteDto>() != null);
                Assert.Equal(manned, t.GetDescriptor<CarrierDefDto>()?.Seats.Count ?? 0);
                Assert.Equal(manned == 0, !ws.NeedsOperator);
                Assert.NotNull(t.GetDescriptor<BehaviorProfileDto>());   // §6 — every one has a brain (it holds unless the weapon controls itself)
                Assert.Equal(manned == 0, t.GetDescriptor<SensorCapabilitiesDto>() != null);   // W-E — only the automatic one sees
            }

            var pickup = db.GetByType(TkbEntityTypes.Pickup_Toyota)!;
            var seats = pickup.GetDescriptor<CarrierDefDto>()!.Seats;
            Assert.Equal(new[] { Fdp.Toolkit.Behavior.Components.SeatRole.Driver, Fdp.Toolkit.Behavior.Components.SeatRole.Gunner,
                                 Fdp.Toolkit.Behavior.Components.SeatRole.Station }, System.Linq.Enumerable.Select(seats, s => s.Role));
            Assert.Equal((MountKinds.Pintle, 1, true, 0L), (seats[2].Accepts, seats[2].OperatedBy, seats[2].NeedsGunner, seats[2].StartWith));
            Assert.All(seats, s => Assert.Equal(0L, s.StartWith));   // bare
        }

        /// <summary>
        /// ⭐⭐ M7 (DESIGN_FPV_Drone §2) — TKB type 403, the FPV drone: a Rotorcraft (Gen.Locomotion) with Gen.Flight and NO ground physics,
        /// 10 hp, its weapon = its warhead (munition 6016, reach = its contact radius), heard as rotors, sees 1500 m, DIS air / UAV, hostile.
        /// </summary>
        [Fact]
        public void M7_TheFpvDrone_IsARotorcraftWithAWarhead_NotAGroundVehicle()
        {
            var t = BuildDatabase().GetByType(TkbEntityTypes.Drone_Fpv)!;
            Assert.Equal(LocomotionClass.Rotorcraft, t.GetDescriptor<LocomotionDto>()!.Class);
            Assert.NotNull(t.GetDescriptor<FlightParamsDto>());
            Assert.Null(t.GetDescriptor<VehicleParametersDto>());
            Assert.Equal(10f, t.GetDescriptor<CombatPlatformDefDto>()!.MaxHealth);
            var warhead = System.Linq.Enumerable.Single(t.GetDescriptor<WeaponSuiteDto>()!.Mounts);
            Assert.Equal((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbFpvWarhead, warhead.AmmoGuid);
            Assert.Equal(t.GetDescriptor<FlightParamsDto>()!.ContactRadiusM, warhead.Range);
            Assert.Equal(SoundSourceClass.Rotor, t.GetDescriptor<SignaturesDto>()!.Acoustic!.MovingClass);
            Assert.Equal(1500f, t.GetDescriptor<SensorCapabilitiesDto>()!.VisionRange);
            Assert.Equal((1, 2, 50), (t.DisType.Kind, t.DisType.Domain, t.DisType.Category));
            Assert.Equal(Fdp.Core.ForceId.Hostile, t.GetDescriptor<BehaviorProfileDto>()!.Faction);
        }

        /// <summary>⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, §6) — the doors-off Black Hawk (404) FLIES routes:
        /// a rotorcraft with <c>Gen.Flight</c> (it holds 30 m over the ground, rounds its corners at 60 m), standing on its three wheels
        /// (what it lands on), still the eight-seat carrier. The plain UH-60 (400) stays a static body.</summary>
        [Fact]
        public void B7_TheDoorsOffBlackHawk_FliesRoutes_OnItsGear()
        {
            var db = BuildDatabase();
            var t = db.GetByType(TkbEntityTypes.Heli_UH60_DoorsOff)!;
            Assert.Equal(LocomotionClass.Rotorcraft, t.GetDescriptor<LocomotionDto>()!.Class);
            var flight = t.GetDescriptor<FlightParamsDto>()!;
            Assert.Equal((30f, 60f, 70f), (flight.HoverHeightM, flight.TurnRadiusM, flight.MaxSpeedMps));
            Assert.Null(t.GetDescriptor<VehicleParametersDto>());                       // not ground physics
            Assert.Equal(3, t.GetDescriptor<BodyGeometryDto>()!.GroundContacts.Count);    // its gear
            Assert.Equal(8, t.GetDescriptor<CarrierDefDto>()!.Seats.Count);
            Assert.Null(db.GetByType(TkbEntityTypes.Heli_UH60)!.GetDescriptor<FlightParamsDto>());
        }

        /// <summary>⭐ Air Ops slice 4 (DESIGN_Air_Operations.md §6, §8) — the attack plane (405) is a FIXED-WING flyer (cruise 150 m/s, stall 60,
        /// bank 60° ⇒ ~1.3 km turns, routes 300 m over the ground) with four Mk-82s on a Bomb mount; the Mk-82 (6022) is an indirect area
        /// warhead (it falls and bursts where it lands).</summary>
        [Fact]
        public void A1_TheAttackPlane_IsAFixedWing_WithFourBombs()
        {
            var db = BuildDatabase();
            var t = db.GetByType(TkbEntityTypes.Jet_AttackA10)!;
            Assert.Equal(LocomotionClass.FixedWing, t.GetDescriptor<LocomotionDto>()!.Class);
            var flight = t.GetDescriptor<FlightParamsDto>()!;
            Assert.Equal((150f, 60f, 60f, 300f), (flight.CruiseSpeedMps, flight.StallSpeedMps, flight.MaxBankDeg, flight.HoverHeightM));
            Assert.Null(t.GetDescriptor<VehicleParametersDto>());
            var bombs = System.Linq.Enumerable.Single(t.GetDescriptor<WeaponSuiteDto>()!.Mounts);
            Assert.Equal((WeaponCategory.Bomb, 4, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbMk82Bomb),
                (bombs.Category, bombs.InitialAmmunition, bombs.AmmoGuid));
            var munitions = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(munitions);
            var mk82 = munitions.GetByType(Hrot.Core.Tkb.MunitionTkbCatalog.TkbMk82Bomb)!.GetDescriptor<WarheadDto>()!;
            Assert.True(mk82.Indirect && mk82.HasAreaEffect);
        }

        /// <summary>
        /// ⭐ B8 (M6) / CE-2168 — a NED ground type states its hull SIZE (Body.Geometry, ground-centred) and its locomotion class; a person
        /// does not get a hull. 📐 Without the height every NED hull had collider height 0: no M1 height band, and the blast rule took a
        /// tank for a person (hill-attack-close stalled on HEAT-MP, measured 2026-10-14). Air / naval mobility is not ground physics: it
        /// fails loudly.
        /// </summary>
        [Fact]
        public void B8_ANedHull_StatesItsSizeAndLocomotion_APersonGetsNoHull_AirIsNotGroundPhysics()
        {
            var db = BuildDatabase();
            var m1 = db.GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var g = m1.GetDescriptor<BodyGeometryDto>()!;
            Assert.Equal((7.93f, 3.66f, 2.44f, 1.22f), (g.Length, g.Width, g.Height, g.BodyCentreZ));
            Assert.Equal(ReferencePointKind.GroundCentre, g.ReferencePoint);
            Assert.Equal(LocomotionClass.Tracked, m1.GetDescriptor<LocomotionDto>()!.Class);
            Assert.Equal(LocomotionClass.Wheeled, db.GetByType(TkbEntityTypes.Truck_HMMWV)!.GetDescriptor<LocomotionDto>()!.Class);
            var man = db.GetByType(TkbEntityTypes.Infantry_Rifleman)!;
            Assert.Null(man.GetDescriptor<BodyGeometryDto>());
            Assert.Equal(LocomotionClass.Pedestrian, man.GetDescriptor<LocomotionDto>()!.Class);

            var b = new NedTkbBuilder(new TkbDatabase());
            b.DefineVehicle(9500, "a plane on ground physics");
            Assert.Throws<System.NotSupportedException>(() => b.WithPhysics(9500, p => p.Mobility = TerrainMobility.Air));
        }

        private static TkbDatabase BuildDatabase()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            return db;
        }
    }
}

