using System;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// The ONE shared source of the UrbanCombat TKB templates (types 1002-2007):
    /// CivilianCar, MilitaryAPC, InfantrySoldier, Insurgent, and (⭐ CE-1032) Grenadier and MortarTeam. ⛔ The pedestrian (1001) and child
    /// (1003) are retired (CE-3220 part 2): people are the civilian family, <see cref="CivilianTkbCatalog"/>.
    ///
    /// <para><b>Why this lives in Hrot.Core.</b> These templates used to live in
    /// <c>Fdp.Examples.Scenarios</c>, an assembly referenced by exactly TWO production projects
    /// (Hrot.Editor and HrotStrideApp.Game). SimHost, CGF and IG therefore <i>could not</i> resolve
    /// TkbTypes 1001-2003 even if they wanted to: a scenario referencing 1001 loaded in the Editor and
    /// failed on a cluster node. User ruling 2026-08-30: "if editor builds UrbanCombat stuff then
    /// everyone should, editor is the most advanced in that matter." Seeded from
    /// <c>HrotEnvironment.CreateTkb()</c>, so every host gets identical catalogue CONTENTS.</para>
    ///
    /// <para><b>There used to be TWO copies</b>, both inside <c>UrbanCombatNewScenario</c>: five private
    /// per-template methods used by the scenario's own run, and this public one used by the Editor. They
    /// were identical EXCEPT that the private copy omitted <see cref="StrideRenderModelDefDto"/> from all
    /// five templates - so entities spawned through the scenario's own path had no render model and no
    /// collider. THIS (the Editor's, richer) version is authoritative; the private copy was deleted and
    /// <c>UrbanCombatNewScenario</c> now forwards here. That is the "one unified TKB template source"
    /// the user asked for on 2026-08-31.</para>
    ///
    /// <para>⚠ <b>DEVELOPMENT DEFAULT, NOT THE END STATE.</b> User, 2026-08-31: "these exist as default
    /// for development now, real system would read everything from files synced to all nodes." So this
    /// class is a code-registered seed to make development and tests self-contained; the production
    /// system loads TKB content from files replicated across nodes. Do NOT grow it into the product's
    /// authoring surface, and do not treat its contents as a contract.</para>
    ///
    /// <para>📄 docs/DESIGN_Entity_Creation_Unification.md §3.3.</para>
    /// </summary>
    public static class UrbanCombatTkbCatalog
    {
        /// <summary>⭐ <c>CE-2322</c> (AQ97 A, R-2062) — every soldier type's own SOP: a lone soldier reacts when shot at or hit (under command or
        /// aboard a vehicle its rows stand aside). <c>InertSop</c> on a unit says "no response" explicitly.</summary>
        public const string SoldierSop = "BasicInfantrySop";

        // ── TKB type codes — PUBLIC because UrbanCombatNewScenario's spawn calls need them, and
        //    this class is now the single source of them. ─────────────────────────────────────────
        // ⛔ CE-3220 part 2 — 1001 (CivilianPedestrian) and 1003 (CivilianChild) are retired, never reused: see CivilianTkbCatalog.
        /// <summary>TKB type code for the CivilianCar template.</summary>
        public const int TkbCivilianCar        = 1002;
        /// <summary>⭐ AQ93 §12 T1 — the TRAFFIC car block 1050–1059 (SUMO drives these; kept clear of the sequential run): a hatchback.</summary>
        public const int TkbTrafficHatchback   = 1050;
        /// <summary>⭐ AQ93 T1 — a delivery van.</summary>
        public const int TkbTrafficVan         = 1051;
        /// <summary>⭐ AQ93 T1 — a city bus.</summary>
        public const int TkbTrafficBus         = 1052;
        /// <summary>⭐ AQ93 T1 — a box truck.</summary>
        public const int TkbTrafficTruck       = 1053;
        /// <summary>TKB type code for the MilitaryAPC template.</summary>
        public const int TkbMilitaryApc        = 2001;
        /// <summary>TKB type code for the InfantrySoldier template.</summary>
        public const int TkbInfantrySoldier    = 2002;
        /// <summary>TKB type code for the Insurgent template.</summary>
        public const int TkbInsurgent          = 2003;
        /// <summary>⭐ <c>CE-1032</c> — an infantry soldier who also carries hand grenades (mount 1).</summary>
        public const int TkbGrenadier          = 2004;
        /// <summary>⭐ <c>CE-1032</c> — an 81 mm mortar team (one mount, indirect).</summary>
        public const int TkbMortarTeam         = 2005;
        /// <summary>⭐ M5 (DESIGN_Missiles_And_Ammo §5) — an anti-tank team with a Javelin (fire-and-forget, top attack) and a rifle.</summary>
        public const int TkbJavelinTeam        = 2006;
        /// <summary>⭐ M5 — an anti-tank gunner with NLAWs (fire-and-forget, short range) and a rifle.</summary>
        public const int TkbNlawGunner         = 2007;
        /// <summary>⭐ <c>CE-3220</c> part 2 (Civilians §7.2b) — an UNARMED FIGHTER: an Infantry-class person (3.1.*.1, a combatant by STATUS —
        /// AW-21) with no weapon and no brain. The stand-in the retired 1001 used to play by accident (an unclassed LandPerson), now explicit.</summary>
        public const int TkbUnarmedFighter     = 2008;
        /// <summary>⭐ <c>CE-2260</c> BC-4 (CB-71) — an ASSAULT RIFLEMAN: the InfantrySoldier plus stun grenades (mount 1) and M67 (mount 2) — what
        /// the clearing demos field, so the room drill can bang a room out. 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.3.</summary>
        public const int TkbAssaultRifleman    = 2009;

        // ── Tuning constants — moved verbatim with the templates; used only here. ────────────────
        internal const float CivilianVisionRange  = 30f;
        internal const float CivilianHearingRange = 100f;
        private const float SoldierVisionRange   = 150f;
        private const float SoldierHearingRange  = 200f;

        private const float ApcMaxHealth     = 500f;
        private const float SoldierMaxHealth = 100f;

        /// <summary>⭐ CE-4030 — a civilian road vehicle's combat platform: a soft skin, as the technical's pickup (5 mm sheet, 300 hit points,
        /// immobilised below 60 %, a near burst wrecks it). 📌 Measured 2026-10-18: no civilian car type had one, so a burst never damaged a
        /// civilian car and a SUMO car converted by a hit (AQ93 §15 K7) had no health to take the hit on.</summary>
        internal static CombatPlatformDefDto SoftSkinCar() => new()
        {
            ArmorFront = 5, ArmorSide = 5, ArmorRear = 5, ArmorTop = 3,
            MaxHealth = 300f, BlastSusceptibility = 1f, MobilityKillBelowFraction = 0.6f,
        };

        // ⭐ CE-2106 — what each type gives off to EARS (AcousticSignatureDto, R-205 / R-207). First-cut ranges (m), heard when
        //   within both the sound's range and the listener's HearingRange. 📄 docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.1.
        internal const float FootstepsRange   = 25f;
        private const float CarEngineRange   = 80f;
        private const float ApcEngineRange   = 300f;
        /// <summary>⭐ BR-16 — the RWS station's height on the APC (m): the 2.5 m hull's roof; the weapon's own 0.3 m pivot sits above it.</summary>
        private const float ApcRwsZ = 2.5f;
        private const float RifleShotRange   = 400f;
        private const float RpgShotRange     = 500f;
        private const float RpgBurstRange    = 800f;
        /// <summary>⭐ CE-3284 (Q95 B1) — the 81 mm tube's real carry, shot and burst (the BDC catalogue's <c>Mortar81Report</c>; Sensing §8.5).</summary>
        private const float Mortar81ReportRange = 5000f;

        // ⭐ CE-3136 P-5 (peek-and-fire D9, R-239) — 5 magazines of 30 (was one load of 30, G4: "a duel runs dry in a minute"); a
        //   3 s magazine change. RifleAmmo stays the TOTAL carried (WeaponState.Ammo), the magazine is beside it.
        private const int   RifleMagazine       = 30;
        private const int   RifleAmmo           = 5 * RifleMagazine;
        private const float RifleReloadSeconds  = 3f;
        private const float RifleMuzzleVelocity = 800f;
        // ⭐ M5 (DESIGN_Missiles_And_Ammo §5, programme §2) — the RPG man carries FOUR rockets (was one), lays the launcher 2 s before
        //   each and needs 6 s to load the next; he stands to fire (D-L).
        private const int   RpgAmmo             = 4;
        private const float RpgMuzzleVelocity   = 300f;
        private const float RpgAimSeconds = 2f, RpgReloadSeconds = 6f;

        // ⭐ M5 — the anti-tank teams (§5): the Javelin locks 4 s, carries two, 20 s to mount the next; the NLAW tracks 2.5 s, two tubes.
        private const int   JavelinMissiles = 2;
        private const float JavelinAimSeconds = 4f, JavelinReloadSeconds = 20f, JavelinRange = 2500f, JavelinDamage = 1500f, JavelinSpeed = 140f;
        private const int   NlawTubes = 2;
        private const float NlawAimSeconds = 2.5f, NlawReloadSeconds = 3f, NlawRange = 800f, NlawDamage = 900f, NlawSpeed = 200f;

        // ⭐ M6 (DESIGN_Armour_Hit_Model §2, D-F) — the APC is armoured against rifles, not against a rocket (it was unarmoured: a rifle
        //   took 25 off it per hit). Its health stays as stated.
        private const float ApcArmorFront = 30f, ApcArmorSide = 20f, ApcArmorRear = 15f, ApcArmorTop = 10f;

        // ⭐ CE-3071 — the munitions (docs/DESIGN_Utility_AI_Demo_Scenarios.md §9 calibration). The rifle keeps today's 25 per hit.
        private const float RifleRange = 300f, RiflePenetration = 5f,   RifleDamage = 25f;
        /// <summary>⭐ AQ85 C (R-216) — the rifle's aim dispersion: σ = 6 mils ⇒ a 0.3 m target is hit ~50 % at 100 m standing
        /// still, ~always inside 37 m, ~42 % at 120 m; half that while moving or under fire.</summary>
        public const float RifleDispersionMils = 6f;
        // ⭐ CE-3231 (B-9, §7.12 PI-3) — the pistol: 15-round magazines, a 2 s change, 360 m/s, 50 m, twice the rifle's dispersion.
        private const int   PistolMagazine = 15;
        private const float PistolReloadSeconds = 2f, PistolMuzzleVelocity = 360f, PistolRange = 50f, PistolDispersionMils = 12f;
        private const float RpgRange   = 300f, RpgPenetration   = 300f, RpgDamage   = 400f;

        // ⭐ CE-1032 (W-8) — the warhead mounts. Their numbers are the munition's (MunitionTkbCatalog → the reference library by name);
        //   the mount states only how it is fired: a THROW (15 m/s ⇒ ~23 m at 45°) and a mortar on a reduced charge (60 m/s ⇒ up to
        //   ~370 m, fired high). No DamagePerHit/Penetration: a direct hit is the flat default; the warhead does the work.
        private const int   GrenadeCount = 4;
        /// <summary>⭐ CE-2260 (CB-71) — the assault rifleman's stun grenades and fragmentation grenades.</summary>
        private const int   AssaultStunGrenades = 2, AssaultFragGrenades = 2;
        private const float GrenadeThrowSpeed = 15f, GrenadeRange = 25f;
        private const int   MortarBombs = 10;
        private const float MortarMuzzleVelocity = 60f, MortarRange = 360f;

        /// <summary>
        /// Registers all the UrbanCombat entity blueprints (1002–2007) into <paramref name="tkb"/>.
        ///
        /// <para>⚠ <c>TkbDatabase.Register</c> THROWS on a duplicate name or type, so call this exactly
        /// once per database. <c>HrotEnvironment.CreateTkb()</c> already does; a host that uses
        /// <c>CreateTkb()</c> must NOT call this again.</para>
        /// </summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));

            // ⭐⭐⭐ P3 step 0 — EVERY template here declares SimTransform BIRTH-CRITICAL.
            //   📄 docs/DESIGN_Role_Affinity_Ownership.md §3.1. Under role-affinity ownership a node
            //   owns a component only if it holds that component's role — applied to POSITION that is
            //   wrong: a Brain-role node (CGF) creating one of these would produce SimTransform
            //   UNOWNED, and since every egress translator gates on HasAuthority the spawn coordinate
            //   would be written correctly and NEVER PUBLISHED. Peers would see the origin.
            //   ⭐ Safe on all five even though none lists SimTransform as a MandatoryComponent: the
            //   create leg intersects this set with the entity's LIVE component mask, so a template
            //   whose entity never receives one contributes no bits. ⛔ UNDER-declaring is the
            //   dangerous direction, and it is silent.
            //   ⚠ Nothing reads it yet — steps 1-3 of that design are the consumers.

            // ⛔ CE-3220 part 2 (civilians B-2, B2-c; docs/DESIGN_Civilians_And_Crowd_Control.md §7.2b) — CivilianPedestrian (1001) and
            //   CivilianChild (1003) are RETIRED, never reused: a person is one of the civilian family (CivilianTkbCatalog 1101–1104).

            // CivilianCar (1002)
            {
                var t = new TkbTemplate("CivilianCar", TkbCivilianCar);
                Master(t, "CivilianCar", new DISEntityType { Kind = 1, Domain = 1, Category = 81 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 1.5f });
                var car = CarrierDefDto.PassengerSeats(4);   // ⭐ CE-2151 (R-2009) — a car seats four beside its (abstract) driver
                // ⭐ CE-3233 (civilians B-11b, AW-12, §7.13 ST-9) — and its BOOT: one cargo station, stowed and concealed (a car bomb's load
                //   is seen by nobody and fired by nobody), appended after the seats
                car.Seats.Add(new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, Accepts = MountKinds.Cargo, Stowed = true, Concealed = true, Exposed = false });
                t.AddDescriptor(car);
                t.AddDescriptor(new VehicleParametersDto { Length = 4.5f, Width = 2.0f, MaxSpeedFwd = 25.0f, MaxAccel = 3.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
                t.AddDescriptor(Sounds(CarEngineRange, SoundSourceClass.WheeledEngine));
                t.AddDescriptor(new Fdp.Toolkit.Traffic.TrafficVehicleDto { VClass = "passenger", Weight = 6f });   // ⭐ AQ93 T1 — SUMO drives it too
                t.AddDescriptor(SoftSkinCar());   // ⭐ CE-4030 — it can be hit
                tkb.Register(t);
            }

            // ⭐ AQ93 §12 T1 (R-4056) — the rest of the traffic: each its own type (its own model and size), SUMO's share by weight.
            TrafficCar(tkb, "Hatchback", TkbTrafficHatchback, 3.9f, 1.75f, 1.45f, 22f, "passenger", 4f);
            TrafficCar(tkb, "DeliveryVan", TkbTrafficVan, 5.3f, 2.0f, 2.3f, 20f, "delivery", 1.5f);
            TrafficCar(tkb, "CityBus", TkbTrafficBus, 12.0f, 2.55f, 3.0f, 15f, "bus", 0.4f);
            TrafficCar(tkb, "BoxTruck", TkbTrafficTruck, 8.0f, 2.5f, 3.4f, 18f, "truck", 0.6f);

            // MilitaryAPC (2001)
            {
                var t = new TkbTemplate("MilitaryAPC", TkbMilitaryApc);
                Master(t, "MilitaryAPC", new DISEntityType { Kind = 1, Domain = 1, Category = 2 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 2.5f });
                var apc = CarrierDefDto.PassengerSeats(8);   // ⭐ CE-2151 (R-2009) — the APC carries a squad (the convoy ambush boards five)
                // ⭐ BR-16 (Urban Battle §4 ⑧; R-1001 — a vehicle's weapon is a TKB preset) — a 12.7 mm REMOTE weapon station: a Gunner
                //   seat (8, enclosed — he works it from inside) that starts with a soldier, and the roof station (9) it operates — a pintle,
                //   the M2's own all-round limits, NeedsGunner — that starts with an M2. Appended: seats 0–7 stay the indices scenarios name.
                apc.Seats.Add(new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = TkbInfantrySoldier, Exposed = false });
                apc.Seats.Add(new SeatDto
                {
                    Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, Accepts = MountKinds.Pintle, OperatedBy = 8, NeedsGunner = true,
                    Z = ApcRwsZ, StartWith = Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M2Hmg,
                });
                t.AddDescriptor(apc);
                t.AddDescriptor(new VehicleParametersDto { Length = 7.0f, Width = 3.5f, MaxSpeedFwd = 12.0f, MaxAccel = 2.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierHsm, CanMove = true, CanInteract = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = ApcMaxHealth, MobilityKillBelowFraction = 1f,   // CE-3092 — the APC's mobility kill (PACK-M002)
                    ArmorFront = ApcArmorFront, ArmorSide = ApcArmorSide, ArmorRear = ApcArmorRear, ArmorTop = ApcArmorTop,   // ⭐ M6
                    BlastSusceptibility = 0.3f });   // ⭐ R-2019 D-M2 (AE3) — a light APC (20 mm sides): close blast hurts it
                t.AddDescriptor(Sounds(ApcEngineRange, SoundSourceClass.WheeledEngine));
                tkb.Register(t);
            }

            // InfantrySoldier (2002)
            {
                var t = new TkbTemplate("InfantrySoldier", TkbInfantrySoldier);
                Master(t, "InfantrySoldier", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)   // ⭐ CE-3112 — a human: the Infantry navmesh layer + the pedestrian presets (was unset ⇒ PersonalCar)
                t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });   // ⭐ 5d-2 — doors
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity, Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5,   /* ⭐ LR-1 (R-271) */ DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils },
                    Pistol() } });   // ⭐ CE-3231 (B-9) — his sidearm, mount 1 (the hands go to 2)
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms));
                t.AddDescriptor(BuildMannequinAnimationDef());  // ST-011
                tkb.Register(t);
            }

            // ⭐ CE-3220 part 2 (Civilians §7.2b) — UnarmedFighter (2008): the retired 1001's body (no weapon, no brain, a civilian's senses and
            //   footsteps, health from the scenario) under the Infantry class, so a soldier judges him on STATUS (AW-21) — a Civilian-class person
            //   who has done nothing hostile is no target, whatever his side. The scenarios' unarmed hostiles and decoys are these.
            {
                var t = new TkbTemplate("UnarmedFighter", TkbUnarmedFighter);
                Master(t, "UnarmedFighter", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.7f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = 0, CanMove = true, CanInteract = true });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = CivilianVisionRange, HearingRange = CivilianHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps));
                tkb.Register(t);
            }

            // ⭐ CE-1032 — Grenadier (2004): the InfantrySoldier, plus hand grenades on mount 1
            {
                var t = new TkbTemplate("Grenadier", TkbGrenadier);
                Master(t, "Grenadier", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)
                t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts =
                {
                    new WeaponMountDto { InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity, Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5,   /* ⭐ LR-1 (R-271) */ DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils },
                    new WeaponMountDto { InitialAmmunition = GrenadeCount, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbM67Grenade, Category = WeaponCategory.Grenade },   // ⭐ WL-4 (W-5) — the category the weapon brain reads
                    Pistol(),   // ⭐ CE-3231 (B-9) — his sidearm, mount 2 AFTER the grenades (PI-2: the grenade stays mount 1; the hands go to 3)
                } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms, RpgBurstRange));
                t.AddDescriptor(BuildMannequinAnimationDef());
                tkb.Register(t);
            }

            // ⭐ CE-2260 BC-4 (CB-71) — AssaultRifleman (2009): the InfantrySoldier, plus stun grenades (mount 1) and M67 (mount 2) for the room
            //   drill's "bang out" (§7.3); the pistol after them (the hands go to 4). Only the clearing demos field him — the InfantrySoldier
            //   stays grenade-free, so no other demo's weapon brain starts throwing (R-261).
            {
                var t = new TkbTemplate("AssaultRifleman", TkbAssaultRifleman);
                Master(t, "AssaultRifleman", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });
                t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());
                t.AddDescriptor(CarrierDefDto.PersonCarrier());
                t.AddDescriptor(new WeaponSuiteDto { Mounts =
                {
                    new WeaponMountDto { InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity, Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5, DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils },
                    new WeaponMountDto { InitialAmmunition = AssaultStunGrenades, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbStunGrenade, Category = WeaponCategory.Grenade },
                    new WeaponMountDto { InitialAmmunition = AssaultFragGrenades, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbM67Grenade, Category = WeaponCategory.Grenade },
                    Pistol(),
                } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms));
                t.AddDescriptor(BuildMannequinAnimationDef());
                tkb.Register(t);
            }

            // ⭐ CE-1032 — MortarTeam (2005): an 81 mm mortar, fired high at a point
            {
                var t = new TkbTemplate("MortarTeam", TkbMortarTeam);
                Master(t, "MortarTeam", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 4.0f, MaxAccel = 2.0f });   // ⭐ AQ88 A (R-265) — a loaded crew runs slower than a rifleman (was 1.5)
                t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = MortarBombs, MuzzleVelocity = MortarMuzzleVelocity, Range = MortarRange, AmmoGuid = MunitionTkbCatalog.Tkb81mmMortarHe } } });
                t.AddDescriptor(new PocketItemsDto { Binoculars = true });   // ⭐ CE-3234 (B-12, HI-3) — the crew watches its fall of shot
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, Mortar81ReportRange, SoundSourceClass.HeavyWeapon, Mortar81ReportRange, SoundFamily.Mortar));   // ⭐ CE-3283 / CE-3284 — an 81 mm, heard 5 km (was the RPG's 500 m)
                t.AddDescriptor(BuildMannequinAnimationDef());
                tkb.Register(t);
            }

            // ⭐ M5 — JavelinTeam (2006) and NlawGunner (2007): an anti-tank weapon (mount 0) and a rifle (mount 1)
            AntiTank(tkb, "JavelinTeam", TkbJavelinTeam, JavelinRange, new WeaponMountDto { InitialAmmunition = JavelinMissiles, MuzzleVelocity = JavelinSpeed, Range = JavelinRange,
                DamagePerHit = JavelinDamage, AmmoGuid = MunitionTkbCatalog.TkbJavelinMissile, Category = WeaponCategory.Atgm,
                MagazineSize = 1, ReloadSeconds = JavelinReloadSeconds, AimSeconds = JavelinAimSeconds });
            AntiTank(tkb, "NlawGunner", TkbNlawGunner, NlawRange, new WeaponMountDto { InitialAmmunition = NlawTubes, MuzzleVelocity = NlawSpeed, Range = NlawRange,
                DamagePerHit = NlawDamage, AmmoGuid = MunitionTkbCatalog.TkbNlawMissile, Category = WeaponCategory.Atgm,
                MagazineSize = 1, ReloadSeconds = NlawReloadSeconds, AimSeconds = NlawAimSeconds });

            // Insurgent (2003)
            {
                var t = new TkbTemplate("Insurgent", TkbInsurgent);
                Master(t, "Insurgent", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new PocketItemsDto { Phone = true });   // ⭐ CE-3234 (B-12, HI-3/HI-6) — a phone: his charge's remote trigger
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)   // ⭐ CE-3112 — a human: the Infantry navmesh layer + the pedestrian presets (was unset ⇒ PersonalCar)
                t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });   // ⭐ 5d-2 — doors
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                // ⭐ CE-3210 (hit location HL-12) — the class template (bodies.json "Soldier") gives a helmet and a vest; an insurgent wears
                //   neither: a SPARSE override of the two armour fields it changes, nothing else restated.
                t.AddDescriptor(new BodyZonesDto { Zones =
                {
                    new BodyZoneDto { Zone = "Head",  ArmourSide = 0, ArmourRear = 0, ArmourTop = 0 },
                    new BodyZoneDto { Zone = "Torso", ArmourFront = 0, ArmourRear = 0 },
                } });
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts =
                {
                    new WeaponMountDto { InitialAmmunition = RpgAmmo, MuzzleVelocity = RpgMuzzleVelocity, Range = RpgRange, Penetration = RpgPenetration, DamagePerHit = RpgDamage, AmmoGuid = MunitionTkbCatalog.TkbRpg7Rocket /* ⭐ LR-1 (R-271) */,
                                         MagazineSize = 1, ReloadSeconds = RpgReloadSeconds, AimSeconds = RpgAimSeconds, FireOnMove = false },   // ⭐ M5
                    Rifle(),   // ⭐ M5 — and a rifle, for men (the RPG for vehicles: the weapon brain picks)
                } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RpgShotRange, SoundSourceClass.HeavyWeapon, RpgBurstRange, SoundFamily.Launcher));   // ⭐ CE-3283 — an RPG / an ATGM
                t.AddDescriptor(BuildMannequinAnimationDef());  // ST-011
                tkb.Register(t);
            }
        }

        /// <summary>⭐ M5 — the infantry rifle (the InfantrySoldier's mount), as a second mount.</summary>
        private static WeaponMountDto Rifle() => new()
        {
            InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity,
            Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer,
            TracerEvery = 5, DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils, Category = WeaponCategory.SmallArms,
        };

        /// <summary>⭐ <c>CE-3231</c> (civilians B-9, AW-4; docs/DESIGN_Civilians_And_Crowd_Control.md §7.12) — the soldier's PISTOL, a built-in
        /// mount AFTER his others (PI-2: every authored mount index stays): 3 magazines of 15, 50 m, a 9 mm round a vest stops. The weapon brain
        /// ranks it (PI-4): the rifle wins while it has ammo; the rifle empty, the pistol fires inside its range. Looks like a pistol (B-7).</summary>
        private static WeaponMountDto Pistol() => new()
        {
            InitialAmmunition = PistolMagazine * 3, MagazineSize = PistolMagazine, ReloadSeconds = PistolReloadSeconds,
            MuzzleVelocity = PistolMuzzleVelocity, Range = PistolRange, Penetration = MunitionTkbCatalog.Pistol9mmPenetrationMm,
            DamagePerHit = MunitionTkbCatalog.Pistol9mmDamage, DispersionMils = PistolDispersionMils, AmmoGuid = MunitionTkbCatalog.TkbPistol9mm,
            Category = WeaponCategory.SmallArms, Appearance = new ItemAppearanceDto { Look = ItemLook.Pistol },
        };

        /// <summary>⭐ M5 — an anti-tank soldier: the InfantrySoldier's body, sensors and sounds, <paramref name="launcher"/> on mount 0 (its
        /// penetration is the munition's own — <c>MunitionTkbCatalog</c> — so the mount states none) and a rifle on mount 1.</summary>
        private static void AntiTank(ITkbDatabase tkb, string name, int type, float sightRange, WeaponMountDto launcher)
        {
            var t = new TkbTemplate(name, type);
            Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
            t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
            t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 5.0f, MaxAccel = 2.0f });   // a loaded man runs a little slower
            t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
            t.AddDescriptor(new InjuryDefDto());
            t.AddDescriptor(CarrierDefDto.PersonCarrier());
            t.AddDescriptor(new WeaponSuiteDto { Mounts = { launcher, Rifle() } });
            // the launcher's sight (the Javelin's CLU) sees as far as the weapon reaches; a rifleman's eyes would cap it at 150 m
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = MathF.Max(SoldierVisionRange, sightRange), HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
            t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RpgShotRange, SoundSourceClass.HeavyWeapon, RpgBurstRange, SoundFamily.Launcher));   // ⭐ CE-3283 — an RPG / an ATGM
            t.AddDescriptor(BuildMannequinAnimationDef());
            tkb.Register(t);
        }

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — the master descriptor and the template's DIS type, set together so they never disagree. ⛔ Before,
        /// these five types carried NO DIS type: their entities were DIS 0, so the map's layer filters never matched them (they
        /// fell into the always-visible fallback) and the Add Entity picker could not group them. Country 0 = generic (the
        /// picker skips an unknown level); 81 = car, 2 = armoured fighting vehicle (SISO-REF-010 land categories).
        /// </summary>
        /// <summary>⭐ AQ93 T1 — a civilian road vehicle SUMO may drive: a car's descriptors at its own size, plus its SUMO class and weight.</summary>
        private static void TrafficCar(ITkbDatabase tkb, string name, int type, float length, float width, float height, float maxSpeed,
                                       string vClass, float weight)
        {
            var t = new TkbTemplate(name, type);
            Master(t, name, new DISEntityType { Kind = 1, Domain = 1, Category = 81 });
            t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = height });
            t.AddDescriptor(new VehicleParametersDto { Length = length, Width = width, MaxSpeedFwd = maxSpeed, MaxAccel = 2.0f });
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
            t.AddDescriptor(Sounds(CarEngineRange, SoundSourceClass.WheeledEngine));
            t.AddDescriptor(new Fdp.Toolkit.Traffic.TrafficVehicleDto { VClass = vClass, Weight = weight });
            t.AddDescriptor(SoftSkinCar());   // ⭐ CE-4030 — it can be hit
            tkb.Register(t);
        }

        internal static void Master(TkbTemplate t, string name, DISEntityType dis)
        {
            t.DisType = dis;
            t.AddDescriptor(new TkbMasterDto { CustomName = name, DisType = dis.ToString() });
        }

        /// <summary>⭐ <c>CE-2106</c> — a type's sounds (movement always; shots and bursts when it has a weapon).</summary>
        /// <param name="family">⭐ CE-3283 (Q95 A) — the weapon family a trained ear tells its shots by; Unknown ⇒ SmallArms for a small-arms
        /// shooter, else none. ⭐ CE-3285 — each sound's band is its class's (Q95 B3).</param>
        internal static SignaturesDto Sounds(float moving, SoundSourceClass movingClass,
                                            float firing = 0f, SoundSourceClass firingClass = SoundSourceClass.Unknown, float detonation = 0f,
                                            SoundFamily family = SoundFamily.Untold)
        {
            var detonationClass = detonation > 0f ? SoundSourceClass.Explosion : SoundSourceClass.Unknown;
            return new SignaturesDto
            {
                Acoustic = new AcousticSignatureDto
                {
                    MovingAudibleRange     = moving,
                    MovingClass            = movingClass,
                    FiringAudibleRange     = firing,
                    FiringClass            = firingClass,
                    DetonationAudibleRange = detonation,
                    DetonationClass        = detonationClass,
                    Family         = family != SoundFamily.Untold ? family
                                   : firingClass == SoundSourceClass.SmallArms ? SoundFamily.Rifle : SoundFamily.Untold,
                    MovingBand     = SoundBands.Of(movingClass),
                    FiringBand     = SoundBands.Of(firingClass),
                    DetonationBand = SoundBands.Of(detonationClass),
                },
            };
        }

        /// <summary>
        /// Builds the mannequin character-class animation descriptor (STR-P4-T2, DD-4 §2).
        ///
        /// <para>Carries the idle/walk/run locomotion clips (driven by the
        /// <c>StrideAnimationBackend</c>'s blend tree on slot 0 = Locomotion) and the
        /// three jump traversal montages (<c>Jump_Start</c>/<c>Jump_Loop</c>/<c>Jump_End</c>
        /// on slot 100 = FullBody). All six <see cref="MontageDefDto.AssetRef"/>s point at the
        /// template-seeded Stride asset URLs (<c>Animations/*</c>, §12). Shared by the
        /// InfantrySoldier (2002) and Insurgent (2003) humanoid templates — both use the
        /// mannequin model.</para>
        /// </summary>
        public static CharacterAnimationDefDto BuildMannequinAnimationDef()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new[]
                {
                    new SlotDefDto { SlotId = 0,   Name = "Locomotion", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 0 },
                    new SlotDefDto { SlotId = 100, Name = "FullBody",   BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                },
                Montages = new[]
                {
                    // Locomotion clips — blended (not played as one-shots) by the backend's
                    // idle/walk/run blend tree; carried here so the AssetRefs resolve and the
                    // backend can look up the Stride AnimationClip URLs.
                    new MontageDefDto { Name = "Idle", AssetRef = "Animations/Idle", Slot = 0, DefaultBlendInTime = 0.2f, DefaultBlendOutTime = 0.2f, DurationSeconds = 1.0f, Sections = Array.Empty<string>(), Notifies = Array.Empty<MontageNotifyRefDto>() },
                    new MontageDefDto { Name = "Walk", AssetRef = "Animations/Walk", Slot = 0, DefaultBlendInTime = 0.2f, DefaultBlendOutTime = 0.2f, DurationSeconds = 1.0f, Sections = Array.Empty<string>(),
                        Notifies = new[]
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left",  TimeSeconds = 0.25f, PayloadByte = 0 },
                            new MontageNotifyRefDto { MarkerName = "Footstep_Right", TimeSeconds = 0.75f, PayloadByte = 1 },
                        } },
                    new MontageDefDto { Name = "Run", AssetRef = "Animations/Run", Slot = 0, DefaultBlendInTime = 0.15f, DefaultBlendOutTime = 0.15f, DurationSeconds = 0.7f, Sections = Array.Empty<string>(),
                        Notifies = new[]
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left",  TimeSeconds = 0.2f, PayloadByte = 0 },
                            new MontageNotifyRefDto { MarkerName = "Footstep_Right", TimeSeconds = 0.6f, PayloadByte = 1 },
                        } },

                    // Jump traversal montages (off-mesh-link discrete playback, §6.4).
                    new MontageDefDto { Name = "Jump_Start", AssetRef = "Animations/Jump_Start", Slot = 100, DefaultBlendInTime = 0.08f, DefaultBlendOutTime = 0.1f, DurationSeconds = 0.4f, Sections = new[] { "Launch" }, Notifies = Array.Empty<MontageNotifyRefDto>() },
                    new MontageDefDto { Name = "Jump_Loop",  AssetRef = "Animations/Jump_Loop",  Slot = 100, DefaultBlendInTime = 0.1f,  DefaultBlendOutTime = 0.1f, DurationSeconds = 0.6f, Sections = new[] { "Airborne" }, Notifies = Array.Empty<MontageNotifyRefDto>() },
                    new MontageDefDto { Name = "Jump_End",   AssetRef = "Animations/Jump_End",   Slot = 100, DefaultBlendInTime = 0.1f,  DefaultBlendOutTime = 0.12f, DurationSeconds = 0.5f, Sections = new[] { "Land" },
                        Notifies = new[]
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left", TimeSeconds = 0.05f, PayloadByte = 0 },
                        } },
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched, StanceId.Prone },   // ⭐ CE-2121: HoldProne (CE-3090) lies down
                StanceTransitions = Array.Empty<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new[]
                {
                    new NotifyMarkerDefDto { Name = "Footstep_Left",  Hash = 0xC1D2E3F4u, Kind = AnimNotifyCategory.Footstep },
                    new NotifyMarkerDefDto { Name = "Footstep_Right", Hash = 0xD1E2F3A4u, Kind = AnimNotifyCategory.Footstep },
                },
            };
        }
    }
}
