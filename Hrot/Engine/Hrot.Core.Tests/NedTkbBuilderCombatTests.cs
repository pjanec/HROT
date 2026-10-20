using System.Linq;
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

        /// <summary>⭐ <c>CE-3253</c> (BR-8, DA-19) — the two infantry machine gunners carry an LMG as their own mount 0: a MachineGun-category,
        /// belt-fed gun on the M249 / PKM ammunition, a person's body, on their sides. ✅ Red-proof: the M249 back to SmallArms ⇒ red.</summary>
        [Fact]
        public void CE3253_TheInfantryMachineGunners_CarryAnLmg()
        {
            var db = BuildDatabase();
            foreach (var (type, ammo, belt, faction, country) in new[]
                     {
                         (TkbEntityTypes.Infantry_AutomaticRifleman, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball, 200, (byte)1, NedTkbCatalog.UnitedStates),
                         (TkbEntityTypes.Infantry_MachineGunnerPkm, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, 100, (byte)2, NedTkbCatalog.Russia),
                     })
            {
                var t = db.GetByType(type)!;
                var lmg = t.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];
                Assert.Equal((WeaponCategory.MachineGun, ammo, belt), (lmg.Category, lmg.AmmoGuid, lmg.MagazineSize));
                Assert.Equal(CarKinem.Core.VehicleClass.Pedestrian, t.GetDescriptor<VehicleParametersDto>()!.VehicleClass);
                Assert.Equal(country, t.DisType.Country);
                Assert.NotNull(t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.InjuryDefDto>());
            }
        }

        /// <summary>⭐ <c>CE-3254</c> (BR-9, DA-13) — the sniper and the marksmen carry a scoped rifle as mount 0: the design's ≈ 0.3 mil sniper
        /// and ≈ 1 mil DMR spread, a ×4 scope, on their sides. ✅ Red-proof: the M24's scope dropped from the builder's mapping ⇒ red.</summary>
        [Fact]
        public void CE3254_TheSniperAndTheMarksmen_CarryAScopedRifle()
        {
            var db = BuildDatabase();
            foreach (var (type, mils, fov, faction) in new[]
                     {
                         (TkbEntityTypes.Infantry_Sniper, 0.3f, 4f, (byte)1),
                         (TkbEntityTypes.Infantry_Marksman, 1f, 6f, (byte)1),
                         (TkbEntityTypes.Infantry_MarksmanSvd, 1f, 6f, (byte)2),
                     })
            {
                var rifle = db.GetByType(type)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];
                Assert.Equal((mils, 4f, fov, WeaponCategory.SmallArms), (rifle.DispersionMils, rifle.ScopeMagnification, rifle.ScopeFovDegrees, rifle.Category));
            }
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
            foreach (var (type, elevMax, traverse, category, onTheMove, ammo, pen) in new[]
                     {
                         (TkbEntityTypes.Technical_Hmg, 70f, 180f, WeaponCategory.MachineGun, true, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbHmgAp, 0f),
                         (TkbEntityTypes.Technical_Atgm, 20f, 120f, WeaponCategory.Atgm, false, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbKornetMissile, 0f),
                         // ⭐ ui handoff P1 (DESIGN_Weapon_Systems §5a) — the other two bed weapons, presets of the same pickup
                         (TkbEntityTypes.Technical_Dshk, 70f, 180f, WeaponCategory.MachineGun, true, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbHmgAp, 0f),
                         (TkbEntityTypes.Technical_Minigun, 30f, 180f, WeaponCategory.MachineGun, true, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, 8f),   // the M134 states its own 8 mm (the door gun's)
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
                Assert.Equal(pen, weapon.Penetration);   // 0 = the round's own (the munition template)

                var s = t.GetDescriptor<SensorCapabilitiesDto>()!;
                Assert.Equal((2000f, 2.2f), (s.VisionRange, s.EyeHeightStanding));
                Assert.Equal(SoundSourceClass.WheeledEngine, t.GetDescriptor<SignaturesDto>()!.Acoustic!.MovingClass);
                Assert.Equal((1, 1, 6), (t.DisType.Kind, t.DisType.Domain, t.DisType.Category));
                Assert.Equal(Fdp.Core.ForceId.Hostile, t.GetDescriptor<BehaviorProfileDto>()!.Faction);
            }
        }

        /// <summary>
        /// ⭐⭐ ui handoff P1 (DESIGN_Weapon_Systems §5a) — the patrol HMMWV (102) carries an M2 in its roof ring: its three passenger seats
        /// stay first (0–2, the indices saved scenarios name), then a Gunner seat that starts with a rifleman, EXPOSED while the passengers
        /// stay enclosed, and the ring station it operates — a pintle, all round, NeedsGunner — that starts with an M2 whose pivot ends 2.3 m
        /// up. (Red by construction: the old PassengerSeats(3) has three seats.)
        /// </summary>
        [Fact]
        public void P1_TheHmmwv_CarriesAnM2_InItsRoofRing_ItsPassengerSeatsFirst()
        {
            var db = BuildDatabase();
            var carrier = db.GetByType(TkbEntityTypes.Truck_HMMWV)!.GetDescriptor<CarrierDefDto>()!;
            Assert.Equal(Fdp.Toolkit.Behavior.Components.PassengerExposure.Enclosed, carrier.Exposure);
            Assert.Equal(5, carrier.Seats.Count);
            for (int i = 0; i < 3; i++) Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Passenger, carrier.Seats[i].Role);
            var gunner = carrier.Seats[3];
            Assert.Equal((Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, TkbEntityTypes.Infantry_Rifleman, (bool?)true),
                         (gunner.Role, gunner.StartWith, gunner.Exposed));
            var ring = carrier.Seats[4];
            Assert.Equal((Fdp.Toolkit.Behavior.Components.SeatRole.Station, MountKinds.Pintle, 3, true, TkbEntityTypes.WeaponSystem_M2Hmg),
                         (ring.Role, ring.Accepts, ring.OperatedBy, ring.NeedsGunner, ring.StartWith));
            Assert.Null(ring.TraverseMinDeg);   // all round: the M2's own pintle limits
            var m2Turret = db.GetByType(TkbEntityTypes.WeaponSystem_M2Hmg)!.GetDescriptor<TurretSuiteDto>()!.Turrets[0];
            Assert.Equal(2.3f, ring.Z + m2Turret.PivotZ, 4);
        }

        /// <summary>
        /// ⭐ <c>CE-3233</c> (civilians B-11b, AW-12; §7.13 ST-8/ST-9) — the EXPLOSIVE CHARGE (618) is a charge (never fired) that goes worn,
        /// placed or in a boot; its round (6043) takes its warhead from the library's "8 kg demolition charge" (8 kg: a wider blast than a grenade's);
        /// the civilian car (1002) has one concealed, stowed boot after its four seats. ✅ Red-proof: the library entry renamed ⇒ no warhead ⇒ red.
        /// </summary>
        /// <summary>⭐ CE-4030 — every civilian road vehicle (our civilian car and SUMO's traffic types, AQ93 T1) is a soft skin that can be
        /// hit, as the technical's pickup: a burst damages it, and a SUMO car converted by a hit (AQ93 §15 K7) takes the hit.</summary>
        [Fact]
        public void CE4030_CivilianRoadVehicles_AreSoftSkinsThatCanBeHit()
        {
            var full = new TkbDatabase();
            Hrot.Map.Common.HrotEnvironment.RegisterBuiltInTkb(full);
            foreach (var type in new long[] { Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianCar, Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbTrafficHatchback,
                                              Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbTrafficVan, Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbTrafficBus,
                                              Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbTrafficTruck })
            {
                var p = full.GetByType(type)!.GetDescriptor<CombatPlatformDefDto>();
                Assert.NotNull(p);
                Assert.Equal((300f, 0.6f, 5f), (p!.MaxHealth, p.MobilityKillBelowFraction, p.ArmorSide));
            }
        }

        [Fact]
        public void CE3233_TheCharge_IsDetonatedOnly_ItsWarheadIsTheLibrarys_AndTheCarHasABoot()
        {
            var db = BuildDatabase();
            var ws = db.GetByType(TkbEntityTypes.WeaponSystem_ExplosiveCharge)!.GetDescriptor<WeaponSystemDto>()!;
            Assert.True(ws.Charge);
            Assert.Equal(MountKinds.Ground | MountKinds.Carried | MountKinds.Cargo, ws.MountKinds);
            Assert.Equal((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbExplosiveCharge,
                db.GetByType(TkbEntityTypes.WeaponSystem_ExplosiveCharge)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[0].AmmoGuid);

            var full = new TkbDatabase();
            Hrot.Map.Common.HrotEnvironment.RegisterBuiltInTkb(full);
            var charge = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(full, Hrot.Core.Tkb.MunitionTkbCatalog.TkbExplosiveCharge).Warhead;
            var grenade = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(full, Hrot.Core.Tkb.MunitionTkbCatalog.TkbM67Grenade).Warhead;
            Assert.NotNull(charge);
            Assert.True(charge!.BlastLethalRadiusM > grenade!.BlastLethalRadiusM);

            var car = full.GetByType(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianCar)!.GetDescriptor<CarrierDefDto>()!;
            var boot = car.Seats[^1];
            Assert.Equal((Fdp.Toolkit.Behavior.Components.SeatRole.Station, MountKinds.Cargo, true, true, (bool?)false),
                         (boot.Role, boot.Accepts, boot.Stowed, boot.Concealed, boot.Exposed));
            Assert.Equal(5, car.Seats.Count);
        }

        /// <summary>
        /// ⭐ <c>CE-3233</c> (civilians B-11, AW-6; DESIGN_Civilians_And_Crowd_Control §7.13 ST-7) — the PICK-UP rifle and pistol fire the built-in
        /// soldier's rounds at their STATED muzzle speeds (the range fallback would fly them at 150 / 25 m/s), and the pistol LOOKS like one
        /// (B-7's identification reads it); the rifle looks like a rifle by its category. ✅ Red-proof: the builder ignoring
        /// <c>WeaponMount.MuzzleVelocity</c> ⇒ 150 m/s ⇒ red.
        /// </summary>
        [Fact]
        public void CE3233_ThePickUpRifleAndPistol_FireTheSoldiersRounds_AtTheirOwnSpeed_AndThePistolLooksLikeOne()
        {
            var db = BuildDatabase();
            var rifle = db.GetByType(TkbEntityTypes.WeaponSystem_PickUpRifle)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];
            var pistol = db.GetByType(TkbEntityTypes.WeaponSystem_PickUpPistol)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];
            Assert.Equal((800f, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball), (rifle.MuzzleVelocity, rifle.AmmoGuid));
            Assert.Equal((360f, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbPistol9mm), (pistol.MuzzleVelocity, pistol.AmmoGuid));
            Assert.Null(rifle.Appearance);
            Assert.Equal(ItemLook.Pistol, pistol.Appearance!.Look);
            Assert.True(db.GetByType(TkbEntityTypes.WeaponSystem_PickUpPistol)!.GetDescriptor<WeaponSystemDto>()!.FireWhileCarried);
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
                         (TkbEntityTypes.WeaponSystem_PickUpRifle, MountKinds.Ground | MountKinds.Carried, WeaponPortability.OneMan, 1, false, 8),    // ⭐ CE-3233
                         (TkbEntityTypes.WeaponSystem_PickUpPistol, MountKinds.Ground | MountKinds.Carried, WeaponPortability.OneMan, 1, false, 9),   // ⭐ CE-3233
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
        /// ⭐⭐ CE-3203 (DESIGN_Weapon_Systems §5b) — EVERY weapon system type states how it breaks: its presented area (it is EQUIPMENT in a
        /// burst, not a man), its casing (every face), its health and its blast share — and the sophisticated ones (optics, seekers,
        /// electronics: the guided launchers, the sentry) are the fragile ones. ⛔ Before: one helper gave all 13 types 60 health and no
        /// area. A data rail (not red-proved by a mutation: every value is written here).
        /// </summary>
        [Fact]
        public void CE3203_EveryWeaponSystem_StatesHowItBreaks()
        {
            var db = BuildDatabase();
            foreach (var (type, area, casing, health, blast) in new[]
                     {
                         (TkbEntityTypes.WeaponSystem_M2Hmg, 0.35f, 1.5f, 80f, 1f),
                         (TkbEntityTypes.WeaponSystem_DshkHmg, 0.35f, 1.5f, 80f, 1f),
                         (TkbEntityTypes.WeaponSystem_M134Minigun, 0.25f, 1f, 40f, 1f),
                         (TkbEntityTypes.WeaponSystem_Javelin, 0.3f, 0f, 20f, 1f),
                         (TkbEntityTypes.WeaponSystem_Nlaw, 0.15f, 0f, 15f, 1f),
                         (TkbEntityTypes.WeaponSystem_Law, 0.1f, 0f, 10f, 1f),
                         (TkbEntityTypes.WeaponSystem_KornetTripod, 0.35f, 0f, 25f, 1f),
                         (TkbEntityTypes.WeaponSystem_TowTripod, 0.4f, 0f, 25f, 1f),
                         (TkbEntityTypes.WeaponSystem_SentryGun, 0.3f, 1f, 30f, 1f),
                         (TkbEntityTypes.WeaponSystem_Mortar60, 0.2f, 2f, 60f, 1f),
                         (TkbEntityTypes.WeaponSystem_Mortar81, 0.3f, 2f, 80f, 1f),
                         (TkbEntityTypes.WeaponSystem_Mortar120, 0.5f, 2f, 120f, 1f),
                         (TkbEntityTypes.WeaponSystem_Howitzer105, 3.0f, 6f, 400f, 0.5f),
                     })
            {
                var c = db.GetByType(type)!.GetDescriptor<CombatPlatformDefDto>()!;
                Assert.Equal((area, health, blast), (c.PresentedAreaM2, c.MaxHealth, c.BlastSusceptibility));
                Assert.Equal((casing, casing, casing, casing), (c.ArmorFront, c.ArmorSide, c.ArmorRear, c.ArmorTop));
            }
            float Health(long t) => db.GetByType(t)!.GetDescriptor<CombatPlatformDefDto>()!.MaxHealth;
            Assert.True(Health(TkbEntityTypes.WeaponSystem_Javelin) < Health(TkbEntityTypes.WeaponSystem_M2Hmg) / 2f, "a seeker breaks sooner than a receiver");
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
            var carrier = t.GetDescriptor<CarrierDefDto>()!;
            Assert.Equal(12, carrier.Seats.Count);   // ⭐ F4 — 8 riders + 2 door gunners + 2 door stations (the list is 12 wide)
            Assert.Equal(8, System.Linq.Enumerable.Count(carrier.Seats, s => s.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Passenger));
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
            var bombs = t.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];   // ⭐ Air Ops 6a — the rack stays mount 0; the GAU-8 is mount 1 (F1)
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

        /// <summary>
        /// ⭐ Air Ops slice 6a F1 (docs/designs/air-attack/DESIGN_Air_Operations.md §7a, AW-1/AW-2) — the A-10 (405) carries the Mk-82 rack
        /// AND the GAU-8: an Autocannon FIXED forward (no turret) in a 5° nose cone, HEI loaded and an armour-piercing round beside it
        /// (AmmoChoice picks per target). Its rounds' numbers are the library's: the HEI bursts over an area (the "30mm HE" warhead), the
        /// API has no warhead and out-penetrates it; the mount states the HEI's own numbers.
        /// </summary>
        [Fact]
        public void F1_TheAttackPlane_CarriesTheRackAndAFixedForwardGau8_HeiAndAPenetrator()
        {
            var db = BuildDatabase();
            var mounts = db.GetByType(TkbEntityTypes.Jet_AttackA10)!.GetDescriptor<WeaponSuiteDto>()!.Mounts;
            Assert.Equal(3, mounts.Count);   // ⭐ CE-2188 (BS-1 §5.4c) — + the flare / chaff dispenser, last
            Assert.Equal(WeaponCategory.Countermeasure, mounts[2].Category);
            Assert.Equal(WeaponCategory.Bomb, mounts[0].Category);
            var gun = mounts[1];
            Assert.Equal((WeaponCategory.Autocannon, -1, 5f, 1150), (gun.Category, gun.TurretIndex, gun.FixedConeDeg, gun.InitialAmmunition));
            Assert.Equal((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Hei, gun.AmmoGuid);
            Assert.Equal(new[] { (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Hei, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Api },
                System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(gun.Loadout, l => l.AmmoGuid)));

            var munitions = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(munitions);
            var hei = munitions.GetByType(Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Hei)!;
            var api = munitions.GetByType(Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Api)!;
            Assert.True(hei.GetDescriptor<WarheadDto>()!.HasAreaEffect);
            Assert.Null(api.GetDescriptor<WarheadDto>());
            float heiPen = hei.GetDescriptor<AmmoWeaponBallisticsDto>()!.PenetrationMm, apiPen = api.GetDescriptor<AmmoWeaponBallisticsDto>()!.PenetrationMm;
            Assert.True(apiPen > 10f * heiPen, $"the penetrator out-penetrates the HEI: {apiPen} vs {heiPen} mm");
            Assert.Equal((heiPen, hei.GetDescriptor<AmmoWeaponBallisticsDto>()!.Damage), (gun.Penetration, gun.DamagePerHit));
            Assert.True(Hrot.Core.Tkb.MunitionTkbCatalog.IsBuiltIn(Hrot.Core.Tkb.MunitionTkbCatalog.TkbHydra70Rocket));
        }

        /// <summary>
        /// ⭐ Air Ops slice 6a F2 + F3 (§7a, AW-4/AW-5) — the attack helicopter (406): an air-domain DIS type (1.2.225.20), a Rotorcraft
        /// with its gear; mount 0 a chin M134 on turret 0 (±110°, down to −60°), mount 1 the Hydra pod (a Rocket, fixed forward in an 8°
        /// cone, 38 rounds of munition 6025). The Hydra is UNGUIDED at 700 m/s with an area warhead (the library's "70mm rocket HE").
        /// </summary>
        [Fact]
        public void F2_TheAttackHelicopter_HasAChinMinigunTurret_AndAFixedRocketPod()
        {
            var db = BuildDatabase();
            var t = db.GetByType(TkbEntityTypes.Heli_AH64)!;
            Assert.Equal((byte)1, t.DisType.Kind);
            Assert.Equal(((byte)2, (byte)20), (t.DisType.Domain, t.DisType.Category));
            Assert.Equal(LocomotionClass.Rotorcraft, t.GetDescriptor<LocomotionDto>()!.Class);
            Assert.Null(t.GetDescriptor<VehicleParametersDto>());
            Assert.Equal(3, t.GetDescriptor<BodyGeometryDto>()!.GroundContacts.Count);
            var turret = System.Linq.Enumerable.Single(t.GetDescriptor<TurretSuiteDto>()!.Turrets);
            Assert.Equal((-110f, 110f, -60f), (turret.TraverseMinDeg, turret.TraverseMaxDeg, turret.ElevationMinDeg));
            var mounts = t.GetDescriptor<WeaponSuiteDto>()!.Mounts;
            Assert.Equal((WeaponCategory.MachineGun, 0, 0f), (mounts[0].Category, mounts[0].TurretIndex, mounts[0].FixedConeDeg));
            Assert.Equal((WeaponCategory.Rocket, -1, 8f, 38), (mounts[1].Category, mounts[1].TurretIndex, mounts[1].FixedConeDeg, mounts[1].InitialAmmunition));
            Assert.Equal((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbHydra70Rocket, mounts[1].AmmoGuid);

            var munitions = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(munitions);
            var hydra = munitions.GetByType(Hrot.Core.Tkb.MunitionTkbCatalog.TkbHydra70Rocket)!;
            var flight = hydra.GetDescriptor<MissileDto>()!;
            Assert.Equal((GuidanceKind.Unguided, 700f), (flight.Guidance, flight.SpeedMps));
            var warhead = hydra.GetDescriptor<WarheadDto>()!;
            Assert.True(warhead.HasAreaEffect && !warhead.Indirect);
            Assert.Equal(1.04f, warhead.ExplosiveKg, 3);
        }

        /// <summary>
        /// ⭐ Air Ops slice 6a F2 — the B7 rail for the new type, on ITS catalogue numbers: the attack helicopter on its gear takes a route
        /// off the ground, flies it at its hover height and sets down on its gear at the last Ground waypoint.
        /// </summary>
        [Fact]
        public void F2_B7_TheAttackHelicopter_FliesARoute_FromItsGear_ToItsGear()
        {
            var db = BuildDatabase();
            var t = db.GetByType(TkbEntityTypes.Heli_AH64)!;
            var dto = t.GetDescriptor<FlightParamsDto>()!;
            float rest = -t.GetDescriptor<BodyGeometryDto>()!.GroundContacts[0].Z;

            using var w = new Fdp.Core.EntityRepository();
            w.RegisterComponent<Fdp.Core.SimTransform>();
            w.RegisterComponent<Fdp.Core.SimVelocity>();
            w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            w.RegisterComponent<Fdp.Toolkit.Motion.FlightParams>();
            w.RegisterComponent<Fdp.Toolkit.Motion.FlightState>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.NavigationStatus>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.ManualDrive>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            w.RegisterComponent<CarKinem.Core.VehicleState>();
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var heli = w.CreateEntity();
            w.AddComponent(heli, new Fdp.Core.SimTransform { Position = new System.Numerics.Vector3(0, 0, rest), Rotation = System.Numerics.Quaternion.Identity });
            w.AddComponent(heli, new Fdp.Core.SimVelocity());
            w.AddComponent(heli, new Fdp.Toolkit.Motion.Locomotion { Class = LocomotionClass.Rotorcraft });
            w.AddComponent(heli, Fdp.Toolkit.Motion.FlightParams.From(dto));
            w.AddComponent(heli, new Fdp.Toolkit.Motion.FlightState { Airborne = 0 });
            w.AddComponent(heli, new Fdp.Toolkit.Navigation.NavigationIntent
                { Mode = Fdp.Toolkit.Navigation.NavigationMode.FollowRoute, IntentId = 1, RouteEntityId = 900 });
            w.AddComponent(heli, new Fdp.Toolkit.Navigation.NavigationStatus());
            w.AddComponent(heli, new Fdp.Toolkit.Combat.Components.Health { Current = 800, Max = 800 });
            w.AddComponent(heli, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TkbEntityTypes.Heli_AH64 });
            w.SetAuthority<Fdp.Core.SimTransform>(heli, true);

            var routes = new OneRoute();
            routes.Waypoints.AddRange(new[] { Wp(0, 0, Fdp.Toolkit.Navigation.RouteWaypointKind.Ground), Wp(400, 0), Wp(400, 400),
                                              Wp(800, 400, Fdp.Toolkit.Navigation.RouteWaypointKind.Ground) });
            var air = new Fdp.Toolkit.Motion.AirMotionSystem { RouteSource = routes };
            float maxZ = 0f;
            for (int i = 0; i < 60 * 180; i++)
            {
                air.Execute(w, 1f / 60f);
                maxZ = System.MathF.Max(maxZ, w.GetComponent<Fdp.Core.SimTransform>(heli).Position.Z);
                if (w.GetComponent<Fdp.Toolkit.Navigation.NavigationStatus>(heli).Result == Fdp.Toolkit.Navigation.NavigationResult.Arrived) break;
            }
            var end = w.GetComponent<Fdp.Core.SimTransform>(heli).Position;
            var status = w.GetComponent<Fdp.Toolkit.Navigation.NavigationStatus>(heli);
            Assert.Equal((Fdp.Toolkit.Navigation.NavigationResult.Arrived, Fdp.Toolkit.Navigation.NavigationPhase.Landed), (status.Result, status.Phase));
            Assert.True(System.Numerics.Vector2.Distance(new System.Numerics.Vector2(end.X, end.Y), new System.Numerics.Vector2(800, 400)) < 0.5f, $"at {end}");
            Assert.InRange(end.Z, rest - 0.01f, rest + 0.01f);                   // on its gear
            Assert.InRange(maxZ, dto.HoverHeightM - 0.5f, dto.HoverHeightM + 0.5f);   // its hover height over the ground
        }

        /// <summary>
        /// ⭐⭐ Indirect fire IF-1 (I8, IF-G, FM-1; docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §4) — the guns: three mortars (60 mm
        /// one-man, 81 / 120 mm two-man; class Mortar 1.1.*.10, HighOnly 45–85°) and a 105 mm towed howitzer (class TowedArtillery 1.1.*.5,
        /// Both −5–70°, emplaced) — weapon systems crewed through their Gunner seat, each loading its own round; the M109 (class
        /// SelfPropelledArtillery 1.1.225.4, a turret, Both −3–75°, a preset crew of five); the battery (11.1.225.4, three M109s, placed by
        /// hand). No existing type changed: every pre-IF-1 mount still declares no arc.
        /// </summary>
        [Fact]
        public void IF1_TheMortars_TheHowitzers_AndTheBattery()
        {
            var db = BuildDatabase();
            foreach (var (type, cat, sub, portable, seats, category, arc, minEl, maxEl, ammo) in new[]
                     {
                         (TkbEntityTypes.WeaponSystem_Mortar60, 10, 1, WeaponPortability.OneMan, 1, WeaponCategory.Mortar, IndirectArc.HighOnly, 45f, 85f, Hrot.Core.Tkb.MunitionTkbCatalog.Tkb60mmMortarHe),
                         (TkbEntityTypes.WeaponSystem_Mortar81, 10, 2, WeaponPortability.TwoMan, 2, WeaponCategory.Mortar, IndirectArc.HighOnly, 45f, 85f, Hrot.Core.Tkb.MunitionTkbCatalog.Tkb81mmMortarHe),
                         (TkbEntityTypes.WeaponSystem_Mortar120, 10, 3, WeaponPortability.TwoMan, 2, WeaponCategory.Mortar, IndirectArc.HighOnly, 45f, 85f, Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmMortarHe),
                         (TkbEntityTypes.WeaponSystem_Howitzer105, 5, 1, WeaponPortability.Static, 1, WeaponCategory.Howitzer, IndirectArc.Both, -5f, 70f, Hrot.Core.Tkb.MunitionTkbCatalog.Tkb105mmHowitzerHe),
                     })
            {
                var t = db.GetByType(type)!;
                var ws = t.GetDescriptor<WeaponSystemDto>()!;
                Assert.Equal((portable, true), (ws.Portable, ws.NeedsOperator));
                Assert.Equal((1, 1, cat, sub), (t.DisType.Kind, t.DisType.Domain, t.DisType.Category, (int)t.DisType.Subcategory));
                var mount = Assert.Single(t.GetDescriptor<WeaponSuiteDto>()!.Mounts);
                Assert.Equal((category, arc, minEl, maxEl, (ulong)ammo), (mount.Category, mount.Arc, mount.MinElevationDeg, mount.MaxElevationDeg, mount.AmmoGuid));
                Assert.True(mount.DispersionMils > 0f && mount.Range > 0f && mount.FireOnMove != true, $"{t.Name}");
                // ⭐ ui handoff P2 (DESIGN_Indirect_Fire §3.9) — laid by a LAYING turret 0: all round, the mount's elevation window
                Assert.Equal(0, mount.TurretIndex);
                var lay = Assert.Single(t.GetDescriptor<TurretSuiteDto>()!.Turrets);
                Assert.Equal((-180f, 180f, minEl, maxEl), (lay.TraverseMinDeg, lay.TraverseMaxDeg, lay.ElevationMinDeg, lay.ElevationMaxDeg));
                Assert.True(lay.TraverseRateDegPerSec >= 30f && lay.ElevationRateDegPerSec >= 15f, $"{t.Name}: a full lay inside FM-4's 10 s");
                var crew = t.GetDescriptor<CarrierDefDto>()!.Seats;
                Assert.Equal(seats, crew.Count);
                Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, crew[0].Role);   // a man fires it from its Gunner seat
                Assert.NotNull(t.GetDescriptor<BehaviorProfileDto>());
            }

            var m109 = db.GetByType(TkbEntityTypes.Sph_M109)!;
            Assert.Equal((1, 1, 225, 4), (m109.DisType.Kind, m109.DisType.Domain, (int)m109.DisType.Country, m109.DisType.Category));
            var gun = Assert.Single(m109.GetDescriptor<WeaponSuiteDto>()!.Mounts);
            Assert.Equal((WeaponCategory.Howitzer, IndirectArc.Both, 0, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb155mmHowitzerHe),
                         (gun.Category, gun.Arc, gun.TurretIndex, gun.AmmoGuid));
            Assert.NotNull(m109.GetDescriptor<TurretSuiteDto>());
            Assert.Equal(5, System.Linq.Enumerable.Count(m109.GetDescriptor<CarrierDefDto>()!.Seats, s => s.StartWith != 0));   // the preset crew (W-I)
            Assert.NotNull(m109.GetDescriptor<BehaviorProfileDto>());

            var battery = db.GetByType(TkbEntityTypes.Unit_ArtilleryBattery)!;
            Assert.Equal((11, 1, 225, 4), (battery.DisType.Kind, battery.DisType.Domain, (int)battery.DisType.Country, battery.DisType.Category));
            var roster = Assert.Single(battery.GetDescriptor<TkbCompositionDef>()!.Subordinates);
            Assert.Equal((TkbEntityTypes.Sph_M109, 3), (roster.TkbType, roster.Count));

            // no existing type moved: every mount outside the new guns declares no arc (ThrowArc keeps flying it)
            var fresh = new System.Collections.Generic.HashSet<long> { TkbEntityTypes.WeaponSystem_Mortar60, TkbEntityTypes.WeaponSystem_Mortar81,
                TkbEntityTypes.WeaponSystem_Mortar120, TkbEntityTypes.WeaponSystem_Howitzer105, TkbEntityTypes.Sph_M109 };
            foreach (var t in db.GetAll())
                if (!fresh.Contains(t.TkbType) && t.GetDescriptor<WeaponSuiteDto>() is { } suite)
                    Assert.All(suite.Mounts, m => Assert.Equal(IndirectArc.Unset, m.Arc));
        }

        /// <summary>⭐ <c>CE-2262</c> SL-0 — the INFANTRY PLATOON: a land unit of the infantry arm at platoon echelon whose roster is three
        /// squad commanders (a composite of composites), with a brain whose standing orders are BasicPlatoonSop (CC-D, CE-2294).</summary>
        [Fact]
        public void CE2262_TheInfantryPlatoon_IsThreeSquads()
        {
            var db = BuildDatabase();
            var platoon = db.GetByType(TkbEntityTypes.Unit_InfantryPlatoon)!;
            var squad = db.GetByType(TkbEntityTypes.Unit_InfantrySquad)!;
            var roster = Assert.Single(platoon.GetDescriptor<TkbCompositionDef>()!.Subordinates);
            Assert.Equal((TkbEntityTypes.Unit_InfantrySquad, 3), (roster.TkbType, roster.Count));
            Assert.Equal("Platoon", platoon.GetDescriptor<TkbCompositionDef>()!.Echelon);
            Assert.Equal((squad.DisType.Kind, squad.DisType.Domain, squad.DisType.Country, squad.DisType.Category),
                         (platoon.DisType.Kind, platoon.DisType.Domain, platoon.DisType.Country, platoon.DisType.Category));   // infantry, one arm
            Assert.NotEqual(squad.DisType.Subcategory, platoon.DisType.Subcategory);                                            // a different echelon
            var brain = platoon.GetDescriptor<BehaviorProfileDto>();
            Assert.NotNull(brain);
            Assert.Equal("BasicPlatoonSop", brain!.DefaultSop);   // ⭐ CE-2294 CC-D
        }

        /// <summary>
        /// ⭐ R-2019 D-M2 (AE3; DESIGN_Armour_Hit_Model §7.5) — every catalogued VEHICLE that declares a combat platform STATES its blast
        /// susceptibility (none left to the side-armour fallback): the soft skins take it all, the MBTs nothing, the light armour between.
        /// A type with no platform record (the transport aircraft) takes it all by the rule (<c>ArmorModel.BlastSusceptibility</c>); ⭐ BR-15 the
        /// HMMWV now states its own.
        /// </summary>
        [Fact]
        public void AE3_EveryVehicle_StatesItsBlastSusceptibility()
        {
            var db = HrotEnvironment.CreateTkb();
            int vehicles = 0;
            foreach (var t in db.GetAll())
            {
                if (t.DisType.Kind != 1 || t.GetDescriptor<CombatPlatformDefDto>() is not { } p) continue;
                vehicles++;
                Assert.InRange(p.BlastSusceptibility, 0f, 1f);   // stated, not the −1 fallback
            }
            Assert.True(vehicles >= 15, $"only {vehicles} platform types with a combat record");
            float S(long type) => db.GetByType(type)!.GetDescriptor<CombatPlatformDefDto>()!.BlastSusceptibility;
            Assert.Equal(1f, S(TkbEntityTypes.Pickup_Toyota));
            Assert.Equal(1f, S(TkbEntityTypes.Technical_Hmg));
            Assert.Equal(0f, S(TkbEntityTypes.Tank_M1Abrams));
            Assert.Equal(0f, S(TkbEntityTypes.Tank_T72));
            Assert.Equal(0.1f, S(TkbEntityTypes.IFV_Bradley));
            Assert.Equal(0.25f, S(TkbEntityTypes.Sph_M109));
            Assert.Equal(0.3f, S(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbMilitaryApc));
            Assert.Equal(0.8f, S(TkbEntityTypes.Heli_AH64));
            Assert.Equal(0.9f, S(TkbEntityTypes.Truck_HMMWV));   // ⭐ BR-15
            Assert.Equal(-1f, new CombatPlatformDefDto().BlastSusceptibility);   // a TKB file that states none falls back to the side armour
        }

        /// <summary>
        /// ⭐⭐ BR-14 (Urban Battle §4 ②; DESIGN_Thermal_And_Acoustic_Sensing §6 B) — every weapon system that FIRES carries its own report (its
        /// round names the weapon as the shooter, WS-3, so a host's or a man's own firing range never sounds for it): the tubes and guns at a
        /// doctrinal range, HeavyWeapon, each heavier tube louder and a howitzer more than any mortar; the hand weapons as small arms; the
        /// charge (it never fires) none. The SP howitzer's gun is its hull's mount, so the hull carries the report — and its tracks.
        /// </summary>
        [Fact]
        public void BR14_EveryWeaponThatFires_CarriesItsReport_AndTheTubesAreHeardFar()
        {
            var db = HrotEnvironment.CreateTkb();
            AcousticSignatureDto? A(long t) => db.GetByType(t)!.GetDescriptor<SignaturesDto>()?.Acoustic;
            foreach (var t in db.GetAll().Where(t => t.GetDescriptor<WeaponSystemDto>() is { Charge: false }))
            {
                var a = A(t.TkbType);
                Assert.True(a is { FiringAudibleRange: > 0f } && a.FiringClass != SoundSourceClass.Unknown, $"{t.Name} fires unheard");
            }
            Assert.Null(A(TkbEntityTypes.WeaponSystem_ExplosiveCharge));

            float R(long t) => A(t)!.FiringAudibleRange;
            Assert.InRange(R(TkbEntityTypes.WeaponSystem_Mortar60), 1500f, 2000f);                 // "a mortar ≈ 1.5–2 km"
            Assert.True(R(TkbEntityTypes.WeaponSystem_Mortar81) > R(TkbEntityTypes.WeaponSystem_Mortar60));
            Assert.True(R(TkbEntityTypes.WeaponSystem_Mortar120) > R(TkbEntityTypes.WeaponSystem_Mortar81));
            Assert.True(R(TkbEntityTypes.WeaponSystem_Howitzer105) > R(TkbEntityTypes.WeaponSystem_Mortar120));   // "a howitzer more"
            Assert.True(R(TkbEntityTypes.Sph_M109) > R(TkbEntityTypes.WeaponSystem_Howitzer105));
            foreach (var t in new[] { TkbEntityTypes.WeaponSystem_Mortar60, TkbEntityTypes.WeaponSystem_Mortar81, TkbEntityTypes.WeaponSystem_Mortar120,
                                      TkbEntityTypes.WeaponSystem_Howitzer105, TkbEntityTypes.Sph_M109 })
                Assert.Equal(SoundSourceClass.HeavyWeapon, A(t)!.FiringClass);
            Assert.Equal(SoundSourceClass.TrackedEngine, A(TkbEntityTypes.Sph_M109)!.MovingClass);
            Assert.Equal(SoundSourceClass.SmallArms, A(TkbEntityTypes.WeaponSystem_PickUpRifle)!.FiringClass);

            // ⭐ CE-3278 — a weapon whose round BURSTS states its burst's carry (the burst is stamped on the shooter); a ball / AP round none
            foreach (var t in new[] { TkbEntityTypes.WeaponSystem_Mortar60, TkbEntityTypes.WeaponSystem_Mortar81, TkbEntityTypes.WeaponSystem_Mortar120,
                                      TkbEntityTypes.WeaponSystem_Howitzer105, TkbEntityTypes.Sph_M109, TkbEntityTypes.WeaponSystem_Javelin,
                                      TkbEntityTypes.WeaponSystem_KornetTripod, TkbEntityTypes.WeaponSystem_Zu23 })
                Assert.True(A(t)!.DetonationAudibleRange > 0f, $"{db.GetByType(t)!.Name}'s burst is unheard");
            Assert.Equal(R(TkbEntityTypes.WeaponSystem_Mortar81), A(TkbEntityTypes.WeaponSystem_Mortar81)!.DetonationAudibleRange);
            Assert.Equal(0f, A(TkbEntityTypes.WeaponSystem_M2Hmg)!.DetonationAudibleRange);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3277</c> — the catalogue's INTEGRITY: every type a seat preset (<c>SeatDto.StartWith</c>), a composite's slot
        /// (<c>TkbCompositionDef</c>) or a child blueprint names EXISTS in the catalogue every host builds. 🔴 Red before: the squad's leader
        /// slot and the M109's commander seat named <c>Infantry_Officer</c> (201), which had no template — both spawned nothing.
        /// </summary>
        [Fact]
        public void CE3277_EveryTypeAPresetOrASlotNames_Exists()
        {
            var db = HrotEnvironment.CreateTkb();
            var missing = new System.Collections.Generic.List<string>();
            void Check(Fdp.Interfaces.TkbTemplate owner, long named, string where)
            {
                if (named != 0 && !db.TryGetByType(named, out _)) missing.Add($"{owner.Name} ({owner.TkbType}) {where} names {named}");
            }
            int checkedRefs = 0;
            foreach (var t in db.GetAll())
            {
                if (t.GetDescriptor<CarrierDefDto>() is { } carrier)
                    for (int i = 0; i < carrier.Seats.Count; i++) { Check(t, carrier.Seats[i].StartWith, $"seat {i}"); if (carrier.Seats[i].StartWith != 0) checkedRefs++; }
                if (t.GetDescriptor<TkbCompositionDef>() is { } comp)
                    foreach (var slot in comp.Subordinates) { Check(t, slot.TkbType, $"slot {slot.Designation}"); checkedRefs++; }
                foreach (var child in t.ChildBlueprints) { Check(t, child.ChildTkbType, $"child {child.InstanceId}"); checkedRefs++; }
            }
            Assert.True(checkedRefs >= 20, $"only {checkedRefs} references checked");
            Assert.Empty(missing);
        }

        /// <summary>⭐ <c>CE-3277</c> — the officer (201) is the rifleman's man with binoculars in his pocket.</summary>
        [Fact]
        public void CE3277_TheOfficer_IsARiflemanWithBinoculars()
        {
            var db = HrotEnvironment.CreateTkb();
            var officer = db.GetByType(TkbEntityTypes.Infantry_Officer);
            Assert.NotNull(officer);
            Assert.Equal(3, officer!.DisType.Kind);
            Assert.True(officer.GetDescriptor<PocketItemsDto>() is { Binoculars: true });
            Assert.Null(db.GetByType(TkbEntityTypes.Infantry_Rifleman)!.GetDescriptor<PocketItemsDto>());   // the rifleman carries none
            Assert.Equal(db.GetByType(TkbEntityTypes.Infantry_Rifleman)!.GetDescriptor<VehicleParametersDto>(),
                         officer.GetDescriptor<VehicleParametersDto>());                                      // the same body
        }

        /// <summary>
        /// ⭐ <c>CE-1208</c> (R-265 A, missed in this catalogue) — every BDC PERSON's top speed is a sprint (6 m/s, 2.5 m/s²), as the urban
        /// soldiers' and the pedestrian preset's: the operator's Shift runs at it, the AI's moves ask their own speed under it. 🔴 Red before:
        /// 2.5 m/s "walking", so the run was a walk.
        /// </summary>
        [Fact]
        public void CE1208_EveryBdcPerson_RunsAtASprint()
        {
            var db = HrotEnvironment.CreateTkb();
            int people = 0;
            foreach (var t in db.GetAll())
            {
                if (t.DisType.Kind != 3 || t.GetDescriptor<VehicleParametersDto>() is not { } v || t.TkbType >= 1000) continue;   // BDC lifeforms
                people++;
                Assert.True(v.MaxSpeedFwd >= 5f, $"{t.Name} runs at {v.MaxSpeedFwd} m/s");
            }
            Assert.True(people >= 5, $"only {people} BDC people");
            var rifleman = db.GetByType(TkbEntityTypes.Infantry_Rifleman)!.GetDescriptor<VehicleParametersDto>()!;
            Assert.Equal((6f, 2.5f), (rifleman.MaxSpeedFwd, rifleman.MaxAccel));
        }

        /// <summary>
        /// ⭐⭐ BR-15 (Urban Battle §4 ⑧) — the HMMWV can be hit and killed: it declares a light platform; the urban soldier's rifle round
        /// (the ONE hit rule, <c>ArmorModel.HitDamage</c>) does armour-SCALED damage on its side — some, not all; the insurgent's RPG
        /// takes it all at once on any face. Its crewed roof gun firing is pinned by SimHost's P1 rail (the HMMWV row).
        /// </summary>
        [Fact]
        public void BR15_TheHmmwv_IsALightPlatform_ARifleScalesAndAnRpgKills()
        {
            var db = HrotEnvironment.CreateTkb();
            var hmmwv = db.GetByType(TkbEntityTypes.Truck_HMMWV)!.GetDescriptor<CombatPlatformDefDto>();
            Assert.NotNull(hmmwv);
            Assert.True(hmmwv!.MaxHealth > 0f);

            var rifle = db.GetByType(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbInfantrySoldier)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];
            float side = Fdp.Toolkit.Combat.ArmorModel.HitDamage(rifle.Penetration, rifle.DamagePerHit,
                Fdp.Toolkit.Combat.ArmorModel.ArmourFor(hmmwv, Fdp.Toolkit.Combat.Facing.Side));
            Assert.InRange(side, 0.01f, rifle.DamagePerHit - 0.01f);   // armour-scaled: some, not all

            var rpg = db.GetByType(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbInsurgent)!.GetDescriptor<WeaponSuiteDto>()!.Mounts
                .Single(m => m.AmmoGuid == Hrot.Core.Tkb.MunitionTkbCatalog.TkbRpg7Rocket);
            foreach (var face in new[] { Fdp.Toolkit.Combat.Facing.Front, Fdp.Toolkit.Combat.Facing.Side, Fdp.Toolkit.Combat.Facing.Rear, Fdp.Toolkit.Combat.Facing.Top })
                Assert.True(Fdp.Toolkit.Combat.ArmorModel.HitDamage(rpg.Penetration, rpg.DamagePerHit,
                    Fdp.Toolkit.Combat.ArmorModel.ArmourFor(hmmwv, face, Fdp.Toolkit.Combat.Penetrator.ShapedCharge)) >= hmmwv.MaxHealth,
                    $"one RPG on the {face} kills it");
        }

        private sealed class OneRoute : Fdp.Toolkit.Navigation.IRouteSource
        {
            public readonly System.Collections.Generic.List<Fdp.Toolkit.Navigation.NavRouteWaypoint> Waypoints = new();
            public bool TryGetTrajectory(Fdp.Core.EntityRepository repo, long routeNetworkId, out int trajectoryId) { trajectoryId = 0; return false; }
            public bool TryGetRoute(Fdp.Core.EntityRepository repo, long routeNetworkId, System.Collections.Generic.List<Fdp.Toolkit.Navigation.NavRouteWaypoint> into,
                                    out bool loop, out int version)
            {
                into.Clear(); into.AddRange(Waypoints); loop = false; version = 1;
                return into.Count > 0;
            }
        }

        private static Fdp.Toolkit.Navigation.NavRouteWaypoint Wp(float x, float y,
            Fdp.Toolkit.Navigation.RouteWaypointKind kind = Fdp.Toolkit.Navigation.RouteWaypointKind.Pass)
            => new(new System.Numerics.Vector3(x, y, 0f), 0f, kind, 0f, Fdp.Toolkit.Navigation.RouteHold.None, 0f, 0L);

        /// <summary>
        /// ⭐⭐ <c>CE-2182</c> (AT1, R-2021, DESIGN_Air_Operations §7c) — every AIRCRAFT type, built through the production translator set,
        /// carries the combat layer and NOT the round's bit, so a round's mask (everything but its own bit) meets it. 🔴 Red-proof: the
        /// round's bit back on 2 — the combat layer's — ⇒ every flyer ends on the bit a round skips. The flying UH-60 (404) joins with its
        /// combat platform (AT4, CE-2185).
        /// </summary>
        [Theory]
        [InlineData(TkbEntityTypes.Drone_Fpv)]
        [InlineData(TkbEntityTypes.Jet_AttackA10)]
        [InlineData(TkbEntityTypes.Heli_AH64)]
        [InlineData(TkbEntityTypes.Heli_UH60_DoorsOff)]   // CE-2185 (AT4)
        public void CE2182_AnAircraft_IsOnTheCombatLayer_SoARoundMeetsIt(long type)
        {
            using var w = new Fdp.Core.EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(w);
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>())
                w.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            var e = w.CreateEntity();
            var template = BuildDatabase().GetByType(type)!;
            foreach (var t in Hrot.Core.Tkb.TkbTranslatorSet.Base()) t.Inject(w, e, template);

            int layer = w.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e).CollisionLayer;
            Assert.NotEqual(0, layer & Fdp.Toolkit.Combat.CombatConstants.CombatCollisionLayer);
            Assert.Equal(0, layer & Fdp.Toolkit.Combat.CombatConstants.BulletCollisionLayer);
            Assert.NotEqual(0, layer & ~Fdp.Toolkit.Combat.CombatConstants.BulletCollisionLayer);   // the round's mask meets it
        }

        /// <summary>
        /// ⭐ <c>CE-2185</c> (AT4, R-2021) — both Black Hawks carry a health through the production translators, so a hit, a blast or a
        /// fragment can down them (before: no combat platform, immune). 🔴 Red-proof: without the catalogue entry neither has Health.
        /// </summary>
        [Theory]
        [InlineData(TkbEntityTypes.Heli_UH60)]
        [InlineData(TkbEntityTypes.Heli_UH60_DoorsOff)]
        public void CE2185_ABlackHawk_CarriesHealth(long type)
        {
            using var w = new Fdp.Core.EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(w);
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>())
                w.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            var e = w.CreateEntity();
            var template = BuildDatabase().GetByType(type)!;
            foreach (var t in Hrot.Core.Tkb.TkbTranslatorSet.Base()) t.Inject(w, e, template);

            Assert.True(w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e));
            Assert.Equal(800f, w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Max);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (BS-1-DESIGN §5.4c; 🔒 user: "Chaff launcher can be weapon type and slot could be normal one") —
        /// every crewed aircraft with a brain carries its flares and chaff as an ordinary weapon mount, its LAST one: category
        /// Countermeasure, a loadout of the flare (DIS 8.2.2) and the chaff (8.2.1), a rate of fire. Through the production translators the
        /// UH-60 — whose only mount it is — gets the counts on its own WeaponState and AmmoLoadout, the counter the HUD mirrors.
        /// 🔴 Red-proof: before §5.4c no aircraft had a Countermeasure mount (the counts were a SimHost-only CountermeasureState).
        /// </summary>
        [Theory]
        [InlineData(TkbEntityTypes.Heli_UH60, 30, 30)]
        [InlineData(TkbEntityTypes.Heli_UH60_DoorsOff, 30, 30)]
        [InlineData(TkbEntityTypes.Heli_AH64, 60, 60)]
        [InlineData(TkbEntityTypes.Jet_AttackA10, 120, 240)]
        public void CE2188_ACrewedAircraft_CarriesItsDispenser_AsItsLastWeaponMount(long type, int flares, int chaff)
        {
            var db = BuildDatabase();
            var mounts = db.GetByType(type)!.GetDescriptor<WeaponSuiteDto>()!.Mounts;
            var dispenser = mounts[^1];
            Assert.Equal(WeaponCategory.Countermeasure, dispenser.Category);
            Assert.Equal(1, mounts.Count(m => m.Category == WeaponCategory.Countermeasure));
            Assert.True(dispenser.RateOfFire > 0f);
            Assert.Equal(new[] { ((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbFlare, flares), ((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbChaff, chaff) },
                dispenser.Loadout.Select(l => (l.AmmoGuid, l.Rounds)).ToArray());

            var munitions = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(munitions);
            Assert.Equal(Fdp.Toolkit.Combat.Components.DecoyKind.Flare, Fdp.Toolkit.Combat.Munitions.DecoyOf(munitions, Hrot.Core.Tkb.MunitionTkbCatalog.TkbFlare));
            Assert.Equal(Fdp.Toolkit.Combat.Components.DecoyKind.Chaff, Fdp.Toolkit.Combat.Munitions.DecoyOf(munitions, Hrot.Core.Tkb.MunitionTkbCatalog.TkbChaff));

            if (mounts.Count != 1) return;
            using var w = new Fdp.Core.EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(w);
            // the Brain's weapon components (the Brain registry adds them in production)
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponState>()) w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.AmmoLoadout>()) w.RegisterComponent<Fdp.Toolkit.Combat.Components.AmmoLoadout>();
            var e = w.CreateEntity();
            foreach (var t in Hrot.Core.Tkb.TkbTranslatorSet.Base()) t.Inject(w, e, db.GetByType(type)!);
            Assert.Equal(flares + chaff, w.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponState>(e).Ammo);
            var load = w.GetComponentRO<Fdp.Toolkit.Combat.Components.AmmoLoadout>(e);
            Assert.Equal((flares, chaff), (load.RoundsAt(0), load.RoundsAt(1)));
        }

        /// <summary>
        /// ⭐ <c>CE-2186</c> (W2, R-2020, DESIGN_Missiles_And_Ammo §2c) — every built-in munition carries a relative cost, so the weapon brain's
        /// "cheapest round that is enough" applies to every built-in loadout (a loadout with one uncosted round falls back to the pick by
        /// effect alone). Ordered as the rule says: a rifle round 1, a guided missile or a bomb in the hundreds.
        /// </summary>
        [Fact]
        public void CE2186_EveryBuiltInMunition_HasACost_InTheRulesOrder()
        {
            var db = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(db);
            var munitions = db.GetAll().Where(t => t.TkbType >= 6001 && t.TkbType < 6100).ToList();
            Assert.NotEmpty(munitions);
            Assert.All(munitions, t => Assert.True((t.GetDescriptor<MunitionCostDto>()?.RelativeCost ?? 0f) > 0f, $"{t.Name} has no cost"));
            float Cost(long type) => db.GetByType(type)!.GetDescriptor<MunitionCostDto>()!.RelativeCost;
            Assert.Equal(1f, Cost(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball));
            Assert.True(Cost(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb25mmApdsT) < Cost(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmApfsds));
            Assert.True(Cost(Hrot.Core.Tkb.MunitionTkbCatalog.TkbJavelinMissile) >= 100f);
            Assert.True(Cost(Hrot.Core.Tkb.MunitionTkbCatalog.TkbMk82Bomb) >= 100f);
        }

        /// <summary>⭐ <c>CE-3198</c> (DESIGN_Missiles_And_Ammo §3b) — the NLAW flies the OVERFLY top attack (1 m over the roof, the charge
        /// fired down); the Javelin keeps its climb-and-dive (no overfly), so <c>ua-javelin-lock</c> is unchanged.</summary>
        [Fact]
        public void CE3198_TheNlaw_FliesTheOverfly_TheJavelinKeepsItsDive()
        {
            var db = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(db);
            var nlaw = db.GetByType(Hrot.Core.Tkb.MunitionTkbCatalog.TkbNlawMissile)!.GetDescriptor<MissileDto>()!;
            Assert.True(nlaw.Overfly);
            Assert.Equal(1f, nlaw.OverflyHeightM);
            var javelin = db.GetByType(Hrot.Core.Tkb.MunitionTkbCatalog.TkbJavelinMissile)!.GetDescriptor<MissileDto>()!;
            Assert.False(javelin.Overfly);
            Assert.True(javelin.TopAttack);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2187</c> / <c>CE-2190</c> (DESIGN_Missiles_And_Ammo §3c, DESIGN_Weapon_Systems §5c) — the air-defence catalogue: the
        /// Stinger and the Igla are IR, AIR-only, proximity-fused (the Igla more easily decoyed); every ATGM is GROUND-only; the two MANPADS
        /// launchers are carried shoulder weapons of the Sam category that fire carried; the MANPADS gunners start with them in their carry
        /// station; the ZU-23-2 elevates to +90° with an AP / HE-I loadout and rides technical 110's bed.
        /// </summary>
        [Fact]
        public void CE2187_TheAirDefenceCatalogue_MissilesLaunchersGunnersAndTheZu23()
        {
            var munitions = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(munitions);
            MissileDto M(long t) => munitions.GetByType(t)!.GetDescriptor<MissileDto>()!;
            foreach (var sam in new long[] { Hrot.Core.Tkb.MunitionTkbCatalog.TkbStingerMissile, Hrot.Core.Tkb.MunitionTkbCatalog.TkbIglaMissile })
            {
                Assert.Equal((GuidanceKind.InfraRed, TargetDomain.Air), (M(sam).Guidance, M(sam).TargetDomain));
                Assert.True(M(sam).SeekerConeDeg > 0f);
                var w = munitions.GetByType(sam)!.GetDescriptor<WarheadDto>()!;
                Assert.Equal(FuzeKind.Proximity, w.Fuze);
                Assert.True(w.ProximityFuzeM > 0f && w.FragmentRadiusM > w.ProximityFuzeM, "the fragments reach past the fuze");
            }
            Assert.True(M(Hrot.Core.Tkb.MunitionTkbCatalog.TkbIglaMissile).FlareRejection < M(Hrot.Core.Tkb.MunitionTkbCatalog.TkbStingerMissile).FlareRejection);
            foreach (var atgm in new long[] { Hrot.Core.Tkb.MunitionTkbCatalog.TkbTowMissile, Hrot.Core.Tkb.MunitionTkbCatalog.TkbNlawMissile,
                                              Hrot.Core.Tkb.MunitionTkbCatalog.TkbJavelinMissile, Hrot.Core.Tkb.MunitionTkbCatalog.TkbKornetMissile })
                Assert.Equal(TargetDomain.Ground, M(atgm).TargetDomain);

            var db = BuildDatabase();
            foreach (var (launcher, gunner) in new[] { (TkbEntityTypes.WeaponSystem_Stinger, TkbEntityTypes.Infantry_StingerGunner),
                                                       (TkbEntityTypes.WeaponSystem_Igla, TkbEntityTypes.Infantry_IglaGunner) })
            {
                var ws = db.GetByType(launcher)!.GetDescriptor<WeaponSystemDto>()!;
                Assert.True(ws.FireWhileCarried && (ws.MountKinds & MountKinds.Carried) != 0);
                Assert.Equal(WeaponCategory.Sam, db.GetByType(launcher)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[0].Category);
                var seats = db.GetByType(gunner)!.GetDescriptor<CarrierDefDto>()!.Seats;
                Assert.Equal(launcher, seats[1].StartWith);   // ⭐ CE-3233 (B-11) — seat 1, his HANDS (the back and hidden stations follow it)
            }
            var zu = db.GetByType(TkbEntityTypes.WeaponSystem_Zu23)!;
            Assert.Equal(90f, zu.GetDescriptor<TurretSuiteDto>()!.Turrets[0].ElevationMaxDeg);
            // ⭐ CE-2190 (AD5) — it elevates onto a helicopter OVERHEAD; the M2 on the same pintle cannot (its +70°)
            float overhead = 88f * System.MathF.PI / 180f;
            Assert.True(Fdp.Toolkit.Combat.TurretSlew.InArc(zu.GetDescriptor<TurretSuiteDto>()!.Turrets[0], 1f, overhead));
            Assert.False(Fdp.Toolkit.Combat.TurretSlew.InArc(db.GetByType(TkbEntityTypes.WeaponSystem_M2Hmg)!.GetDescriptor<TurretSuiteDto>()!.Turrets[0], 1f, overhead));
            Assert.Equal(2, zu.GetDescriptor<WeaponSuiteDto>()!.Mounts[0].Loadout.Count);
            Assert.Contains(db.GetByType(TkbEntityTypes.Technical_Zu23)!.GetDescriptor<CarrierDefDto>()!.Seats, x => x.StartWith == TkbEntityTypes.WeaponSystem_Zu23);
        }

        /// <summary>⭐ Airborne insertion B-5 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §4) — the paratrooper jumps under a ROUND
        /// canopy, the SF operator under a RAM-AIR one (every figure the canopy's default); the doors-off Black Hawk carries a fast rope over
        /// each cabin door — left (+y) then right, clear of its door guns — 60 ft long; the plain one (400) carries nobody, so no ropes.</summary>
        [Fact]
        public void B5_TheJumpersCarryTheirCanopies_AndTheDoorsOffBlackHawkItsRopes()
        {
            var db = BuildDatabase();
            Assert.Equal(Fdp.Toolkit.Motion.CanopyKind.Round, db.GetByType(TkbEntityTypes.Infantry_Paratrooper)!.GetDescriptor<ParachuteDto>()!.Canopy);
            Assert.Equal(Fdp.Toolkit.Motion.CanopyKind.RamAir, db.GetByType(TkbEntityTypes.Infantry_SfOperator)!.GetDescriptor<ParachuteDto>()!.Canopy);
            foreach (var t in new[] { TkbEntityTypes.Infantry_Paratrooper, TkbEntityTypes.Infantry_SfOperator })
            {
                var tpl = db.GetByType(t)!;
                Assert.NotNull(tpl.GetDescriptor<InjuryDefDto>());
                Assert.Equal((byte)3, tpl.DisType.Kind);                                   // a lifeform: a person target, hit by part
            }
            Assert.Null(db.GetByType(TkbEntityTypes.Infantry_Rifleman)!.GetDescriptor<ParachuteDto>());

            var heli = db.GetByType(TkbEntityTypes.Heli_UH60_DoorsOff)!.GetDescriptor<CarrierDefDto>()!;
            Assert.Equal(2, heli.RopeAnchors.Count);
            Assert.True(heli.RopeAnchors[0].Y > 0f && heli.RopeAnchors[1].Y < 0f);
            var doorGun = heli.Seats.First(x => x.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Station);
            Assert.True(System.Math.Abs(heli.RopeAnchors[0].Y) > System.Math.Abs(doorGun.Y), "the rope hangs outboard of the door gun");
            Assert.Equal(18.3f, heli.RopeLengthM);
            Assert.Null(db.GetByType(TkbEntityTypes.Heli_UH60)!.GetDescriptor<CarrierDefDto>());
        }

        /// <summary>⭐ Airborne insertion B-7 (design §7d) — the A-10 carries an ejection seat for a Pilot (206, a round canopy opening in 1.5 s)
        /// and ejects on its own at 20 % health.</summary>
        [Fact]
        public void B7_TheA10EjectsAPilot_WhoComesDownUnderAQuickRoundCanopy()
        {
            var db = BuildDatabase();
            var ej = db.GetByType(TkbEntityTypes.Jet_AttackA10)!.GetDescriptor<EjectionDto>()!;
            Assert.Equal(TkbEntityTypes.Infantry_Pilot, ej.PilotType);
            Assert.Equal(0.2f, ej.AutoEjectBelowFraction);
            Assert.True(ej.SeatX > 0f, "the cockpit is ahead of the CG");
            var chute = db.GetByType(TkbEntityTypes.Infantry_Pilot)!.GetDescriptor<ParachuteDto>()!;
            Assert.Equal((Fdp.Toolkit.Motion.CanopyKind.Round, 1.5f), (chute.Canopy, chute.OpenSeconds));
        }

        private static TkbDatabase BuildDatabase()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            return db;
        }
    
        /// <summary>
        /// ⭐⭐ <c>CE-3213</c> (R-3011; 🔒 user: "Dis/hla gateway is planned, so dis type for ammo in tkb is desired") — every built-in round
        /// carries its SISO DIS munition type from SISO-REF-010 v37: kind 2 with a SISO munition domain (1 anti-air · 2 anti-armour ·
        /// 8 anti-personnel · 9 battlefield support) and a category (1 guided · 2 ballistic); the decoys stay expendables 8.2.x. Spot checks
        /// pin the entries the catalog names exactly. 🔴 Red-proof: the coarse 2026-10-08 codes put the TOW in domain 9 and the Stinger in 2.
        /// </summary>
        [Fact]
        public void CE3213_EveryBuiltInRound_CarriesItsSisoMunitionType()
        {
            var db = new TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(db);
            int rounds = 0;
            foreach (var t in db.GetAll())
            {
                var d = t.DisType;
                if (d.Kind == 8) { Assert.Equal((byte)2, d.Domain); Assert.InRange(d.Category, (byte)1, (byte)2); continue; }
                rounds++;
                Assert.True(d.Kind == 2, $"{t.Name}: kind {d.Kind}");
                Assert.True(d.Domain is 1 or 2 or 8 or 9, $"{t.Name}: domain {d.Domain} is not a SISO munition domain this catalog uses");
                Assert.True(d.Category is 1 or 2, $"{t.Name}: category {d.Category}");
            }
            Assert.True(rounds >= 30, $"only {rounds} rounds");
            string Code(long type) => db.GetByType(type).DisType.ToString();
            Assert.Equal("2.2.225.1.1.3.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.TkbTowMissile));        // BGM-71D TOW 2
            Assert.Equal("2.2.225.1.8.0.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.TkbJavelinMissile));    // Javelin
            Assert.Equal("2.2.205.1.2.0.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.TkbNlawMissile));       // NLAW/MBT-LAW
            Assert.Equal("2.1.225.1.15.0.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.TkbStingerMissile));   // Stinger — anti-AIR
            Assert.Equal("2.9.225.2.85.1.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.TkbM67Grenade));        // Fragmentation M67
            Assert.Equal("2.8.225.2.1.4.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball));            // 5.56 x 45
            Assert.Equal("2.2.225.2.3.2.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb25mmApdsT));          // M791 APDS-T
            Assert.Equal("8.2.0.2.0.0.0", Code(Hrot.Core.Tkb.MunitionTkbCatalog.TkbFlare));                // an expendable, a flare
        }
}
}

