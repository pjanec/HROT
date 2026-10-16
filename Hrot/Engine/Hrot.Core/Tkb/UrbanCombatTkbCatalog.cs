using System;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// The ONE shared source of the UrbanCombat TKB templates (types 1001-2003):
    /// CivilianPedestrian, CivilianCar, MilitaryAPC, InfantrySoldier, Insurgent, and (⭐ CE-1032) Grenadier and MortarTeam.
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
        // ── TKB type codes — PUBLIC because UrbanCombatNewScenario's spawn calls need them, and
        //    this class is now the single source of them. ─────────────────────────────────────────
        /// <summary>TKB type code for the CivilianPedestrian template.</summary>
        public const int TkbCivilianPedestrian = 1001;
        /// <summary>TKB type code for the CivilianCar template.</summary>
        public const int TkbCivilianCar        = 1002;
        /// <summary>⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q O-3) — TKB type code for the CivilianChild template.</summary>
        public const int TkbCivilianChild      = 1003;
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

        // ── Tuning constants — moved verbatim with the templates; used only here. ────────────────
        internal const float CivilianVisionRange  = 30f;
        internal const float CivilianHearingRange = 100f;
        private const float SoldierVisionRange   = 150f;
        private const float SoldierHearingRange  = 200f;

        private const float ApcMaxHealth     = 500f;
        private const float SoldierMaxHealth = 100f;

        // ⭐ CE-2106 — what each type gives off to EARS (AcousticSignatureDto, R-205 / R-207). First-cut ranges (m), heard when
        //   within both the sound's range and the listener's HearingRange. 📄 docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.1.
        internal const float FootstepsRange   = 25f;
        private const float CarEngineRange   = 80f;
        private const float ApcEngineRange   = 300f;
        private const float RifleShotRange   = 400f;
        private const float RpgShotRange     = 500f;
        private const float RpgBurstRange    = 800f;

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
        private const float RpgRange   = 300f, RpgPenetration   = 300f, RpgDamage   = 400f;

        // ⭐ CE-1032 (W-8) — the warhead mounts. Their numbers are the munition's (MunitionTkbCatalog → the reference library by name);
        //   the mount states only how it is fired: a THROW (15 m/s ⇒ ~23 m at 45°) and a mortar on a reduced charge (60 m/s ⇒ up to
        //   ~370 m, fired high). No DamagePerHit/Penetration: a direct hit is the flat default; the warhead does the work.
        private const int   GrenadeCount = 4;
        private const float GrenadeThrowSpeed = 15f, GrenadeRange = 25f;
        private const int   MortarBombs = 10;
        private const float MortarMuzzleVelocity = 60f, MortarRange = 360f;

        /// <summary>
        /// Registers all the UrbanCombat entity blueprints (1001–2007) into <paramref name="tkb"/>.
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

            // CivilianPedestrian (1001)
            {
                var t = new TkbTemplate("CivilianPedestrian", TkbCivilianPedestrian);
                Master(t, "CivilianPedestrian", new DISEntityType { Kind = 3, Domain = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.7f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)   // ⭐ CE-3112 — a human: the Infantry navmesh layer + the pedestrian presets (was unset ⇒ PersonalCar)
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true, CanInteract = true });   // ⭐ 5d-2 — a person opens doors (the interaction channel requires CanInteract)
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = CivilianVisionRange, HearingRange = CivilianHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps));
                tkb.Register(t);
            }

            // ⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q O-3) — CivilianChild (1003): a pedestrian 1.25 m tall who runs a little slower; the 3-D
            //   map draws a person under 1.5 m as a child (a larger head, brighter clothes). Otherwise a CivilianPedestrian.
            {
                var t = new TkbTemplate("CivilianChild", TkbCivilianChild);
                Master(t, "CivilianChild", new DISEntityType { Kind = 3, Domain = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.22f, ShapeHeight = 1.25f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.45f, Width = 0.32f, MaxSpeedFwd = 4.5f, MaxAccel = 2.5f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true, CanInteract = true });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = CivilianVisionRange, HearingRange = CivilianHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps));
                tkb.Register(t);
            }

            // CivilianCar (1002)
            {
                var t = new TkbTemplate("CivilianCar", TkbCivilianCar);
                Master(t, "CivilianCar", new DISEntityType { Kind = 1, Domain = 1, Category = 81 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 1.5f });
                t.AddDescriptor(CarrierDefDto.PassengerSeats(4));   // ⭐ CE-2151 (R-2009) — a car seats four beside its (abstract) driver
                t.AddDescriptor(new VehicleParametersDto { Length = 4.5f, Width = 2.0f, MaxSpeedFwd = 25.0f, MaxAccel = 3.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
                t.AddDescriptor(Sounds(CarEngineRange, SoundSourceClass.WheeledEngine));
                tkb.Register(t);
            }

            // MilitaryAPC (2001)
            {
                var t = new TkbTemplate("MilitaryAPC", TkbMilitaryApc);
                Master(t, "MilitaryAPC", new DISEntityType { Kind = 1, Domain = 1, Category = 2 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 2.5f });
                t.AddDescriptor(CarrierDefDto.PassengerSeats(8));   // ⭐ CE-2151 (R-2009) — the APC carries a squad (the convoy ambush boards five)
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
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)   // ⭐ CE-3112 — a human: the Infantry navmesh layer + the pedestrian presets (was unset ⇒ PersonalCar)
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });   // ⭐ 5d-2 — doors
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity, Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5,   /* ⭐ LR-1 (R-271) */ DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils } } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms));
                t.AddDescriptor(BuildMannequinAnimationDef());  // ST-011
                tkb.Register(t);
            }

            // ⭐ CE-1032 — Grenadier (2004): the InfantrySoldier, plus hand grenades on mount 1
            {
                var t = new TkbTemplate("Grenadier", TkbGrenadier);
                Master(t, "Grenadier", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts =
                {
                    new WeaponMountDto { InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity, Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5,   /* ⭐ LR-1 (R-271) */ DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils },
                    new WeaponMountDto { InitialAmmunition = GrenadeCount, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbM67Grenade, Category = WeaponCategory.Grenade },   // ⭐ WL-4 (W-5) — the category the weapon brain reads
                } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms, RpgBurstRange));
                t.AddDescriptor(BuildMannequinAnimationDef());
                tkb.Register(t);
            }

            // ⭐ CE-1032 — MortarTeam (2005): an 81 mm mortar, fired high at a point
            {
                var t = new TkbTemplate("MortarTeam", TkbMortarTeam);
                Master(t, "MortarTeam", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 4.0f, MaxAccel = 2.0f });   // ⭐ AQ88 A (R-265) — a loaded crew runs slower than a rifleman (was 1.5)
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = MortarBombs, MuzzleVelocity = MortarMuzzleVelocity, Range = MortarRange, AmmoGuid = MunitionTkbCatalog.Tkb81mmMortarHe } } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RpgShotRange, SoundSourceClass.HeavyWeapon, RpgBurstRange));
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
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)   // ⭐ CE-3112 — a human: the Infantry navmesh layer + the pedestrian presets (was unset ⇒ PersonalCar)
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });   // ⭐ 5d-2 — doors
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
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RpgShotRange, SoundSourceClass.HeavyWeapon, RpgBurstRange));
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

        /// <summary>⭐ M5 — an anti-tank soldier: the InfantrySoldier's body, sensors and sounds, <paramref name="launcher"/> on mount 0 (its
        /// penetration is the munition's own — <c>MunitionTkbCatalog</c> — so the mount states none) and a rifle on mount 1.</summary>
        private static void AntiTank(ITkbDatabase tkb, string name, int type, float sightRange, WeaponMountDto launcher)
        {
            var t = new TkbTemplate(name, type);
            Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
            t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
            t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 5.0f, MaxAccel = 2.0f });   // a loaded man runs a little slower
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
            t.AddDescriptor(new InjuryDefDto());
            t.AddDescriptor(CarrierDefDto.PersonCarrier());
            t.AddDescriptor(new WeaponSuiteDto { Mounts = { launcher, Rifle() } });
            // the launcher's sight (the Javelin's CLU) sees as far as the weapon reaches; a rifleman's eyes would cap it at 150 m
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = MathF.Max(SoldierVisionRange, sightRange), HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
            t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RpgShotRange, SoundSourceClass.HeavyWeapon, RpgBurstRange));
            t.AddDescriptor(BuildMannequinAnimationDef());
            tkb.Register(t);
        }

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — the master descriptor and the template's DIS type, set together so they never disagree. ⛔ Before,
        /// these five types carried NO DIS type: their entities were DIS 0, so the map's layer filters never matched them (they
        /// fell into the always-visible fallback) and the Add Entity picker could not group them. Country 0 = generic (the
        /// picker skips an unknown level); 81 = car, 2 = armoured fighting vehicle (SISO-REF-010 land categories).
        /// </summary>
        internal static void Master(TkbTemplate t, string name, DISEntityType dis)
        {
            t.DisType = dis;
            t.AddDescriptor(new TkbMasterDto { CustomName = name, DisType = dis.ToString() });
        }

        /// <summary>⭐ <c>CE-2106</c> — a type's sounds (movement always; shots and bursts when it has a weapon).</summary>
        internal static SignaturesDto Sounds(float moving, SoundSourceClass movingClass,
                                            float firing = 0f, SoundSourceClass firingClass = SoundSourceClass.Unknown, float detonation = 0f)
            => new SignaturesDto
            {
                Acoustic = new AcousticSignatureDto
                {
                    MovingAudibleRange     = moving,
                    MovingClass            = movingClass,
                    FiringAudibleRange     = firing,
                    FiringClass            = firingClass,
                    DetonationAudibleRange = detonation,
                    DetonationClass        = detonation > 0f ? SoundSourceClass.Explosion : SoundSourceClass.Unknown,
                },
            };

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
