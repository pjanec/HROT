using System;
using System.Collections.Generic;
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
        /// <summary>⭐ CE-1457 (§6ad-3 TC3) — a sedan, an estate and an SUV: the passenger mix's own body styles.</summary>
        public const int TkbTrafficSedan       = 1054;
        public const int TkbTrafficEstate      = 1055;
        public const int TkbTrafficSuv         = 1056;
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
        /// <summary>⭐ <c>CE-4648</c> (OF-10, docs/designs/person-looks/DESIGN_Person_Outfits.md) — a Middle-East FIGHTER with a rifle: the Insurgent's body,
        /// senses, temperament and brain in the local dress of <see cref="OutfitCatalog.MiddleEastFighter"/>.</summary>
        public const int TkbMiddleEastFighter  = 2010;
        /// <summary>⭐ <c>CE-4648</c> — a Middle-East fighter with an RPG and a rifle (what 2003 carries).</summary>
        public const int TkbMiddleEastFighterRpg = 2011;
        /// <summary>⭐ <c>CE-4648</c> — a Middle-East machine gunner: a man-portable machine gun (the PKM class) as his mount 0.</summary>
        public const int TkbMiddleEastMachineGunner = 2012;
        /// <summary>⭐ <c>CE-4648</c> (R-1066) — the Afghan fighters (the insurgent template in <see cref="OutfitCatalog.AfghanFighter"/>): a rifleman, an RPG + rifle, a machine gunner (PKM class).</summary>
        public const int TkbAfghanFighter = 2019, TkbAfghanFighterRpg = 2020, TkbAfghanMachineGunner = 2021;   // ⭐ 2016 is backend's Native American warrior (CE-4650); 2017 / 2018 left free at the merge of 2026-11-17
        /// <summary>⭐ <c>CE-4649</c> (bows BW-12, docs/designs/weapons/DESIGN_Bows_And_Crossbows.md) — an ARCHER (a bow, 20 arrows) and a CROSSBOWMAN (15 bolts): the insurgent
        /// template (a soldier brain, no armour) in everyday clothes, with the weapon of <see cref="Bow"/> / <see cref="Crossbow"/>. Silent: no shot sound (BW-9).</summary>
        public const int TkbArcher = 2013, TkbCrossbowman = 2014;
        /// <summary>⭐ <c>CE-4635</c> FL-1 (R-3111) — the man-portable flamethrower man (2022 free on every lane, <c>2026-11-19</c>). ⛔ A permanent wire value.</summary>
        public const int TkbFlamethrowerSoldier = 2022;
        /// <summary>⭐ CE-4635 — his fuel as rounds: 10 a second (a fuel shot every 0.1 s keeps the jet lit, FlameJets.LitSeconds 0.25) for ~9 s of flame.</summary>
        public const int FlameShotsPerMinute = 600, FlameFuelShots = 90;
        /// <summary>⭐ <c>CE-4649</c> (BW-11) — the ARCHERY TARGET prop: a straw boss on a stand (a thin box with an <see cref="ArcheryTargetDto"/> scoring face).</summary>
        public const int TkbArcheryTarget = 2015;

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
        private const float RifleShotRange   = 1500f;   // ⭐ CE-2480 (R-2143) — a rifle's crack at a human's audibility (was 400, and capped by the 200 m ear)
        /// <summary>⭐ CE-4635 — a flamethrower's roar (m): a loud hiss and a whoomph, heard closer than a rifle's crack.</summary>
        internal const float FlameRoarRange  = 200f;
        private const float RpgShotRange     = 1500f;   // ⭐ CE-2480 — a shoulder launch, as NedTkbCatalog's LauncherReport
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
        /// <summary>⭐ <c>CE-2278</c> (DA-12, DA-G) — smoke grenades a Grenadier / AssaultRifleman carries beside his M67s: a second LOADOUT entry of
        /// the same grenade mount (⛔ not a new mount — the replicated per-mount ammo carries mounts 0…3 and the AssaultRifleman's are full). The
        /// M67 stays entry 0 (the weapon brain's throw); a squad's <c>ThrowSmoke</c> loads the smoke for one throw (FireAtPoint, round Smoke).</summary>
        private const int   SmokeGrenades = 2;

        private static List<AmmoLoadDto> FragAndSmoke(int frags) => new()
        {
            new AmmoLoadDto { AmmoGuid = MunitionTkbCatalog.TkbM67Grenade, Rounds = frags },
            new AmmoLoadDto { AmmoGuid = MunitionTkbCatalog.TkbSmokeGrenade, Rounds = SmokeGrenades },
        };
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
                Master(t, "CivilianCar", new DISEntityType { Kind = 1, Domain = 1, Category = 81, Subcategory = 10 });   // ⭐ CE-11042 — SISO-REF-010 Car › Generic
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 1.5f });
                var car = CarrierDefDto.PassengerSeats(4);   // ⭐ CE-2151 (R-2009) — a car seats four beside its (abstract) driver
                // ⭐ CE-3233 (civilians B-11b, AW-12, §7.13 ST-9) — and its BOOT: one cargo station, stowed and concealed (a car bomb's load
                //   is seen by nobody and fired by nobody), appended after the seats
                car.Seats.Add(new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, Accepts = MountKinds.Cargo, Stowed = true, Concealed = true, Exposed = false });
                t.AddDescriptor(car);
                // ⭐ CE-3473 — its MASS (a sedan, 1.4 t): it was 0 ("unknown"), so the contact rule's ramming row (≥ 800 kg) never took a car on RamTarget. Below the
                //   AT mine's 1500 kg and the crush 3500 kg, so neither changes; a strike's harm is × √(1400/1500) ≈ 0.97 of the unknown-mass default.
                t.AddDescriptor(new VehicleParametersDto { Length = 4.5f, Width = 2.0f, MaxSpeedFwd = 25.0f, MaxAccel = 3.0f, Mass = 1400f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
                t.AddDescriptor(Sounds(CarEngineRange, SoundSourceClass.WheeledEngine));
                t.AddDescriptor(new Fdp.Toolkit.Traffic.TrafficVehicleDto { VClass = "passenger", Weight = 6f });   // ⭐ AQ93 T1 — SUMO drives it too
                t.AddDescriptor(new VisualDefinitionDto { ColorHex = "", BodyStyle = "Sedan" });   // ⭐ CE-1457 — a sedan; no type colour
                t.AddDescriptor(SoftSkinCar());   // ⭐ CE-4030 — it can be hit
                tkb.Register(t);
            }

            // ⭐ AQ93 §12 T1 (R-4056) — the rest of the traffic: each its own type (its own model and size), SUMO's share by weight.
            // ⭐ CE-1457 (§6ad-3 TC3) — every traffic type names its BODY STYLE (a kit of its own in the 3-D map); the passenger mix is Sedan 3 · Estate 2 ·
            //   Hatchback 3 · Suv 2 · CivilianCar (a Sedan, above) 6
            // ⭐ CE-11042 (R-11020) — each kind its own SISO-REF-010 DIS type, so a DIS-only image generator tells a bus from a sedan:
            //   Car 81 › 43 sedan / 63 wagon / 52 5-door hatchback (compact) / 113 SUV; Single Unit Cargo Truck 83 › 74 delivery van / 101 box
            //   delivery truck; Bus 82 › 1 commuter (flat nose). Values from open-dis7's enumerations (generated from SISO-REF-010).
            TrafficCar(tkb, "Sedan", TkbTrafficSedan, 4.6f, 1.8f, 1.45f, 24f, "passenger", 3f, "Sedan", 81, 43);
            TrafficCar(tkb, "Estate", TkbTrafficEstate, 4.7f, 1.82f, 1.5f, 24f, "passenger", 2f, "Estate", 81, 63);
            TrafficCar(tkb, "Hatchback", TkbTrafficHatchback, 3.7f, 1.66f, 1.5f, 22f, "passenger", 3f, "SmallHatch", 81, 52);
            TrafficCar(tkb, "Suv", TkbTrafficSuv, 4.6f, 1.9f, 1.75f, 23f, "passenger", 2f, "Suv", 81, 113);
            TrafficCar(tkb, "DeliveryVan", TkbTrafficVan, 5.3f, 2.0f, 2.3f, 20f, "delivery", 1.5f, "Van", 83, 74);
            TrafficCar(tkb, "CityBus", TkbTrafficBus, 12.0f, 2.55f, 3.0f, 15f, "bus", 0.4f, "Bus", 82, 1);
            TrafficCar(tkb, "BoxTruck", TkbTrafficTruck, 8.0f, 2.5f, 3.4f, 18f, "truck", 0.6f, "BoxTruck", 83, 101);

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
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { Rifle() /* ⭐ CE-4146 — the ONE rifle (it carries Category SmallArms; the inline copies did not, so their barrel heat was a tank gun's) */,
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
                    Rifle() /* ⭐ CE-4146 — the ONE rifle (it carries Category SmallArms; the inline copies did not, so their barrel heat was a tank gun's) */,
                    new WeaponMountDto { InitialAmmunition = GrenadeCount, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbM67Grenade, Category = WeaponCategory.Grenade, Loadout = FragAndSmoke(GrenadeCount) },   // ⭐ CE-2278 — + smoke (entry 1)   // ⭐ WL-4 (W-5) — the category the weapon brain reads
                    Pistol(),   // ⭐ CE-3231 (B-9) — his sidearm, mount 2 AFTER the grenades (PI-2: the grenade stays mount 1; the hands go to 3)
                } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms, RpgBurstRange));
                t.AddDescriptor(BuildMannequinAnimationDef());
                tkb.Register(t);
            }

            // ⭐ CE-4635 FL-1 (R-3111; docs/designs/weapons/DESIGN_Flame_Weapons.md F-6) — FlamethrowerSoldier (2022): the InfantrySoldier with a man-portable
            //   FLAMETHROWER as his ONLY mount (so an order to fire uses it — mount 0, the one FlameJets reads); its fuel is the flame munition
            {
                var t = new TkbTemplate("FlamethrowerSoldier", TkbFlamethrowerSoldier);
                Master(t, "FlamethrowerSoldier", new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, AvoidanceRadius = VehicleParametersDto.CloseOrderAvoidanceRadius, Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 6.0f, MaxAccel = 2.5f });   // ⭐ AQ88 A (R-265) — a person can RUN (was 2.0, every run clamped to a walk); the stance scales it (StanceSpeed)
                t.AddDescriptor(new BehaviorProfileDto { DefaultSop = SoldierSop, SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true, CanInteract = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new InjuryDefDto());   // ⭐ CE-2155 (R-2011) — wounded, incapacitated, bleeds out
                t.AddDescriptor(CarrierDefDto.PersonCarrier());   // ⭐ CE-2152 (R-2011 C1) — carries or drags one casualty
                t.AddDescriptor(new WeaponSuiteDto { Mounts =
                {
                    new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = MunitionTkbCatalog.TkbFlameFuel, InitialAmmunition = FlameFuelShots,
                                         RateOfFire = FlameShotsPerMinute, Range = MunitionTkbCatalog.FlameReachM, MuzzleVelocity = 40f },
                } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(Sounds(FootstepsRange, SoundSourceClass.Footsteps, FlameRoarRange, SoundSourceClass.Flame));
                t.AddDescriptor(BuildMannequinAnimationDef());
                tkb.Register(t);
            }

            // ⭐ CE-2260 BC-4 (CB-71) — AssaultRifleman (2009): the InfantrySoldier, plus stun grenades (mount 1) and M67 (mount 2) for the room
            //   drill's "bang out" (§7.3); the hands go to 3 — no pistol (four mounts is all dtWeaponAmmo carries). Only the clearing demos field him — the InfantrySoldier
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
                    Rifle() /* ⭐ CE-4146 — the ONE rifle (it carries Category SmallArms; the inline copies did not, so their barrel heat was a tank gun's) */,
                    new WeaponMountDto { InitialAmmunition = AssaultStunGrenades, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbStunGrenade, Category = WeaponCategory.Grenade },
                    new WeaponMountDto { InitialAmmunition = AssaultFragGrenades, MuzzleVelocity = GrenadeThrowSpeed, Range = GrenadeRange, AmmoGuid = MunitionTkbCatalog.TkbM67Grenade, Category = WeaponCategory.Grenade, Loadout = FragAndSmoke(AssaultFragGrenades) },   // ⭐ CE-2278 — + smoke (entry 1)
                } });   // ⚠ no pistol: the hands make mount 3, the last the replicated per-mount ammo carries (dtWeaponAmmo, 0…3)
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

            // Insurgent (2003) + the Middle-East fighters (2010-2012, CE-4648 OF-10) — ONE helper builds them all from the insurgent template
            //   (⛔ no second copy of its descriptors); ⭐ OF-12: the Insurgent himself wears the fighter wardrobe now (a camouflaged soldier read as a
            //   soldier of the hostile side). Behaviour, body, senses and the RPG + rifle of the 2003 are unchanged.
            Insurgent(tkb, "Insurgent", TkbInsurgent, OutfitCatalog.MiddleEastFighter, RpgLauncher(), Rifle());
            Insurgent(tkb, "MiddleEastFighter", TkbMiddleEastFighter, OutfitCatalog.MiddleEastFighter, Rifle());
            Insurgent(tkb, "MiddleEastFighterRpg", TkbMiddleEastFighterRpg, OutfitCatalog.MiddleEastFighter, RpgLauncher(), Rifle());
            Insurgent(tkb, "MiddleEastMachineGunner", TkbMiddleEastMachineGunner, OutfitCatalog.MiddleEastFighter, MachineGun());
            // ⭐ CE-4648 (R-1066) — the Afghan fighters: the SAME helper, the Afghan wardrobe
            Insurgent(tkb, "AfghanFighter", TkbAfghanFighter, OutfitCatalog.AfghanFighter, Rifle());
            Insurgent(tkb, "AfghanFighterRpg", TkbAfghanFighterRpg, OutfitCatalog.AfghanFighter, RpgLauncher(), Rifle());
            Insurgent(tkb, "AfghanMachineGunner", TkbAfghanMachineGunner, OutfitCatalog.AfghanFighter, MachineGun());
            // ⭐ CE-4649 (BW-12) — the Archer and the Crossbowman: the same template, everyday clothes (OutfitCatalog.Archer), their silent weapon
            Insurgent(tkb, "Archer", TkbArcher, OutfitCatalog.Archer, Bow());
            Insurgent(tkb, "Crossbowman", TkbCrossbowman, OutfitCatalog.Archer, Crossbow());
            ArcheryTarget(tkb);
        }

        /// <summary>
        /// ⭐ <c>CE-4648</c> (OF-10, OF-12) — the INSURGENT template, one place: an irregular with <paramref name="mounts"/>, no helmet or vest, the
        /// insurgent temperament, the soldier SOP and brain, wearing <paramref name="outfit"/>. 2003 (RPG + rifle) is its first instance; the Middle-East
        /// fighters are the same descriptors with another weapon set.
        /// </summary>
        internal static void Insurgent(ITkbDatabase tkb, string name, int type, OutfitDto outfit, params WeaponMountDto[] mounts)
        {
            var t = new TkbTemplate(name, type);
            Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
            t.AddDescriptor(TemperamentDto.Insurgent());   // ⭐ CE-2269 TS-2/TS-3 — an insurgent obeys little (0.3, ± his own spread)
            t.AddDescriptor(new PocketItemsDto { Phone = true });   // ⭐ CE-3234 (B-12, HI-3/HI-6) — a phone: his charge's remote trigger
            t.AddDescriptor(outfit);   // ⭐ CE-4648 (OF-11) — what he wears; the wardrobe wins over his Infantry class
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
            var suite = new WeaponSuiteDto();
            foreach (var m in mounts) suite.Mounts.Add(m);
            t.AddDescriptor(suite);
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
            bool rpg = System.Array.Exists(mounts, m => m.Category == WeaponCategory.Atgm || m.AmmoGuid == MunitionTkbCatalog.TkbRpg7Rocket);
            bool silent = mounts.Length > 0 && System.Array.TrueForAll(mounts, m => m.Category == WeaponCategory.Bow || m.AmmoGuid == (ulong)MunitionTkbCatalog.TkbThrowingHatchet);   // ⭐ CE-4650 — a thrown hatchet makes no report either   // ⭐ CE-4649 (BW-9) — a bow is heard by nobody
            t.AddDescriptor(silent ? Sounds(FootstepsRange, SoundSourceClass.Footsteps) : rpg ? Sounds(FootstepsRange, SoundSourceClass.Footsteps, RpgShotRange, SoundSourceClass.HeavyWeapon, RpgBurstRange, SoundFamily.Launcher)   // ⭐ CE-3283 — an RPG / an ATGM
                                : Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms));
            t.AddDescriptor(BuildMannequinAnimationDef());  // ST-011
            tkb.Register(t);
        }

        /// <summary>⭐ M5 — the insurgent's RPG (mount 0 where he has one): a one-round launcher, aimed, fired standing.</summary>
        private static WeaponMountDto RpgLauncher() => new()
        {
            InitialAmmunition = RpgAmmo, MuzzleVelocity = RpgMuzzleVelocity, Range = RpgRange, Penetration = RpgPenetration, DamagePerHit = RpgDamage, AmmoGuid = MunitionTkbCatalog.TkbRpg7Rocket /* ⭐ LR-1 (R-271) */,
            MagazineSize = 1, ReloadSeconds = RpgReloadSeconds, AimSeconds = RpgAimSeconds, FireOnMove = false,   // ⭐ M5
        };

        /// <summary>
        /// ⭐ <c>CE-4649</c> (BW-11) — the ARCHERY TARGET: a prop (no brain, no weapon) the rounds can strike — a thin oriented box 1.2 m wide and 1.4 m high with a plenty of health, so it
        /// is a body an arrow's raycast meets; its <see cref="ArcheryTargetDto"/> is the scoring face (centre 1.0 m up, 10 rings of 6 cm).
        /// </summary>
        private static void ArcheryTarget(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("ArcheryTarget", TkbArcheryTarget);
            // a culture feature (a prop). ⚠ Hidden from the Add Entity palette until ui gives it a 3-D look (an unknown box otherwise — rail
            //   S1_EveryPlaceableBuiltInType_HasAFamily): a scenario still places it by its id (2015).
            var dis = new DISEntityType { Kind = 5 };
            t.DisType = dis;
            t.AddDescriptor(new TkbMasterDto { CustomName = "ArcheryTarget", DisType = dis.ToString(), HideFromPalette = true });
            t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 1.4f });
            t.AddDescriptor(new BodyGeometryDto { Length = 0.3f, Width = 1.2f, Height = 1.4f });
            t.AddDescriptor(new VehicleParametersDto { Length = 0.3f, Width = 1.2f, MaxSpeedFwd = 0f, MaxAccel = 0f });
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100000f });
            t.AddDescriptor(new ArcheryTargetDto());
            tkb.Register(t);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4649</c> (bows BW-1, BW-2, BW-6, BW-7; §5) — a BOW as a man's mount: 60 m/s, drawn in 1.0 s and the next arrow nocked in 1.0 s (a magazine of ONE: every
        /// arrow is a draw), 20 in the quiver, a 100 m engagement limit (the brain fires inside Mount.Range; §5's 60 m is the EFFECTIVE range, its 150 m the flight's), 4 mm / 45 damage, 8 mils of spread; the LOW arc only (<see cref="IndirectArc.LowOnly"/>); fires on the move
        /// (BW-8: from a horse); no heat. Its round is the silent, sticking <see cref="MunitionTkbCatalog.TkbArrow"/>. ⚠ Starter numbers — tuning starts here.
        /// </summary>
        public static WeaponMountDto Bow() => new()
        {
            Category = WeaponCategory.Bow, InitialAmmunition = 20, MagazineSize = 1, ReloadSeconds = 1.0f, AimSeconds = 1.0f, MuzzleVelocity = 60f, Range = 100f,
            Penetration = MunitionTkbCatalog.ArrowPenetrationMm, DamagePerHit = MunitionTkbCatalog.ArrowDamage, DispersionMils = 8f,
            AmmoGuid = MunitionTkbCatalog.TkbArrow, Arc = IndirectArc.LowOnly, FireOnMove = true,
        };

        /// <summary>⭐ <c>CE-4649</c> (BW-1, §5) — a CROSSBOW: the bow's kind with its own numbers — 90 m/s, drawn (aimed) in 0.5 s but SPANNED in 6 s per bolt, 15 bolts, 80 m, 6 mm /
        /// 55 damage, 4 mils; its round is <see cref="MunitionTkbCatalog.TkbCrossbowBolt"/>.</summary>
        public static WeaponMountDto Crossbow() => new()
        {
            Category = WeaponCategory.Bow, InitialAmmunition = 15, MagazineSize = 1, ReloadSeconds = 6.0f, AimSeconds = 0.5f, MuzzleVelocity = 90f, Range = 120f,
            Penetration = MunitionTkbCatalog.BoltPenetrationMm, DamagePerHit = MunitionTkbCatalog.BoltDamage, DispersionMils = 4f,
            AmmoGuid = MunitionTkbCatalog.TkbCrossbowBolt, Arc = IndirectArc.LowOnly, FireOnMove = true,
        };

        /// <summary>⭐ <c>CE-4648</c> (OF-10) — a man's machine gun (the PKM class of NedTkbCatalog's infantry machine gunner, CE-3253: 7.62, ~650 rpm,
        /// 1000 m, a 100-round box, 1 in 5 traces), as a mount of his own (mount 0, where the rifleman has his rifle).</summary>
        private static WeaponMountDto MachineGun() => new()
        {
            InitialAmmunition = 500, MagazineSize = 100, ReloadSeconds = 7f, MuzzleVelocity = RifleMuzzleVelocity, Range = 1000f, RateOfFire = 650f, Penetration = 8f,
            DamagePerHit = 30f, DispersionMils = RifleDispersionMils, AmmoGuid = MunitionTkbCatalog.Tkb762Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb762Tracer,
            TracerEvery = 5, Category = WeaponCategory.MachineGun,
        };

        /// <summary>⭐ M5 — the infantry rifle (the InfantrySoldier's mount), as a second mount.</summary>
        private static WeaponMountDto Rifle() => new()
        {
            InitialAmmunition = RifleAmmo, MagazineSize = RifleMagazine, ReloadSeconds = RifleReloadSeconds, MuzzleVelocity = RifleMuzzleVelocity,
            Range = RifleRange, Penetration = RiflePenetration, AmmoGuid = MunitionTkbCatalog.Tkb556Ball, TracerAmmoGuid = MunitionTkbCatalog.Tkb556Tracer,
            TracerEvery = 5, DamagePerHit = RifleDamage, DispersionMils = RifleDispersionMils, Category = WeaponCategory.SmallArms,
            RateOfFire = 700f,   // ⭐ CE-11033 (§5l) — the M4 class's cyclic rate (as NedTkbCatalog.M4Carbine): the gap inside an AI burst; it had none, so an asked 0 meant 'every frame'
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
                                       string vClass, float weight, string bodyStyle, byte disCategory, byte disSubcategory)
        {
            var t = new TkbTemplate(name, type);
            Master(t, name, new DISEntityType { Kind = 1, Domain = 1, Category = disCategory, Subcategory = disSubcategory });
            t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = height });
            t.AddDescriptor(new VehicleParametersDto { Length = length, Width = width, MaxSpeedFwd = maxSpeed, MaxAccel = 2.0f });
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
            t.AddDescriptor(Sounds(CarEngineRange, SoundSourceClass.WheeledEngine));
            t.AddDescriptor(new Fdp.Toolkit.Traffic.TrafficVehicleDto { VClass = vClass, Weight = weight });
            t.AddDescriptor(new VisualDefinitionDto { ColorHex = "", BodyStyle = bodyStyle });   // ⭐ CE-1457 — no type colour (each car is painted per car), a body style
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
