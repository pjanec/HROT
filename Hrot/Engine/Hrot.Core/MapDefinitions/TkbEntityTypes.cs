namespace Hrot.Map.Common
{
    public static class TkbEntityTypes
    {
        // Ground Platforms
        public const long Tank_M1Abrams = 100;
        public const long IFV_Bradley = 101;
        public const long Truck_HMMWV = 102;
        public const long Tank_T72 = 103;
        /// <summary>⭐ M9 (DESIGN_Vehicle_Exposure_Cycle §6) — a pickup with a 12.7 mm HMG on a pintle: shoot and scoot.</summary>
        public const long Technical_Hmg = 104;
        /// <summary>⭐ M9 — a pickup with a command-guided ATGM: halts for the flight, then scoots.</summary>
        public const long Technical_Atgm = 105;
        /// <summary>⭐⭐ Weapon systems WS-4 (W-F; DESIGN_Weapon_Systems §5) — a bare Toyota pickup: a driver, a gunner, and one bed station
        /// (pintle) the gunner operates. 104 / 105 are this pickup with a weapon and a gunner in its seats (W-I, SeatPresets).</summary>
        public const long Pickup_Toyota = 106;
        /// <summary>⭐ IF-1 (I8, IF-G) — the 155 mm self-propelled howitzer (SelfPropelledArtillery 1.1.*.4), a vehicle with a brain.</summary>
        public const long Sph_M109 = 107;
        /// <summary>⭐ ui handoff P1 (DESIGN_Weapon_Systems §5a) — the pickup (106) with a DShK in the bed and a rifleman at it.</summary>
        public const long Technical_Dshk = 108;
        /// <summary>⭐ ui handoff P1 — the pickup (106) with an M134 minigun in the bed and a rifleman at it.</summary>
        public const long Technical_Minigun = 109;
        /// <summary>⭐ <c>CE-2190</c> (DESIGN_Weapon_Systems §5c) — the pickup (106) with a ZU-23-2 in the bed and a rifleman at it.</summary>
        public const long Technical_Zu23 = 110;

        // ⭐⭐ Weapon systems WS-4 (600–699) — separable weapons, entities of their own (Gen.WeaponSystem, DIS 1.1.*.50).
        public const long WeaponSystem_M2Hmg = 600;
        public const long WeaponSystem_DshkHmg = 601;
        public const long WeaponSystem_M134Minigun = 602;
        public const long WeaponSystem_Javelin = 603;
        public const long WeaponSystem_Nlaw = 604;
        public const long WeaponSystem_Law = 605;
        public const long WeaponSystem_KornetTripod = 606;
        public const long WeaponSystem_TowTripod = 607;
        /// <summary>⭐ W-E — an AUTOMATIC remote weapon station: its own sensor and brain, it fights unmanned.</summary>
        public const long WeaponSystem_SentryGun = 608;
        /// <summary>⭐ Indirect fire IF-1 (I8, IF-G) — the tubes (class Mortar 1.1.*.10) and the towed howitzer (TowedArtillery 1.1.*.5).</summary>
        public const long WeaponSystem_Mortar60 = 609;
        public const long WeaponSystem_Mortar81 = 610;
        public const long WeaponSystem_Mortar120 = 611;
        public const long WeaponSystem_Howitzer105 = 612;
        /// <summary>⭐ <c>CE-2187</c> (§5c) — a Stinger-class MANPADS launcher (carried, fires carried).</summary>
        public const long WeaponSystem_Stinger = 613;
        /// <summary>⭐ <c>CE-2187</c> (§5c) — an Igla-class MANPADS launcher.</summary>
        public const long WeaponSystem_Igla = 614;
        /// <summary>⭐ <c>CE-2190</c> (§5c) — the ZU-23-2 twin 23 mm anti-aircraft gun (two-man, ground or a pintle).</summary>
        public const long WeaponSystem_Zu23 = 615;
        /// <summary>⭐ CE-3233 (civilians B-11, AW-6; docs/DESIGN_Civilians_And_Crowd_Control.md §7.13 ST-7) — a PICK-UP rifle: one man carries
        /// it in a carry station and fires it from his hands (the covert insurgent's, a dropped soldier's).</summary>
        public const long WeaponSystem_PickUpRifle = 616;
        /// <summary>⭐ CE-3233 (B-11, ST-7) — a PICK-UP pistol (it looks like one).</summary>
        public const long WeaponSystem_PickUpPistol = 617;
        /// <summary>⭐ CE-3233 (B-11b, AW-12; §7.13 ST-8) — an EXPLOSIVE CHARGE: worn in a hidden station, placed, or in a car's cargo
        /// station; never fired, only set off where it stands.</summary>
        public const long WeaponSystem_ExplosiveCharge = 618;

        // Lifeforms
        public const long Infantry_Rifleman = 200;
        public const long Infantry_Officer = 201;
        /// <summary>⭐ <c>CE-2187</c> (§5c) — a rifleman carrying a Stinger (his carry station starts with 613).</summary>
        public const long Infantry_StingerGunner = 202;
        /// <summary>⭐ <c>CE-2187</c> (§5c) — a rifleman carrying an Igla (614).</summary>
        public const long Infantry_IglaGunner = 203;
        /// <summary>⭐ Airborne insertion B-5 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §4) — a rifleman who jumps on a static line
        /// under a ROUND canopy (Gen.Parachute).</summary>
        public const long Infantry_Paratrooper = 204;
        /// <summary>⭐ Airborne insertion B-5 (§4) — a special-forces operator: fast-rope trained, and a military free fall under a steerable
        /// RAM-AIR canopy (Gen.Parachute).</summary>
        public const long Infantry_SfOperator = 205;
        /// <summary>⭐ Airborne insertion B-7 (§7d) — a PILOT: spawned by his aircraft's ejection seat (Gen.Ejection), down under a round canopy.</summary>
        public const long Infantry_Pilot = 206;
        /// <summary>⭐ <c>CE-3253</c> (BR-8, DA-19) — an AUTOMATIC RIFLEMAN with an M249-class LMG (5.56, 200-round belt). ⛔ PERMANENT WIRE VALUE.</summary>
        public const long Infantry_AutomaticRifleman = 207;
        /// <summary>⭐ <c>CE-3253</c> (BR-8, DA-19) — a MACHINE GUNNER with a PKM-class gun (7.62, 100-round belt), the other side's (faction 2).
        /// ⛔ PERMANENT WIRE VALUE.</summary>
        public const long Infantry_MachineGunnerPkm = 208;
        /// <summary>⭐ <c>CE-3254</c> (BR-9, DA-13) — a SNIPER: an M24-class bolt rifle (7.62, ≈ 0.3 mil) under a ×4 scope. ⛔ PERMANENT WIRE VALUE.</summary>
        public const long Infantry_Sniper = 209;
        /// <summary>⭐ <c>CE-3254</c> (BR-9) — a MARKSMAN: an M110-class designated-marksman rifle (7.62, ≈ 1 mil), ×4. ⛔ PERMANENT WIRE VALUE.</summary>
        public const long Infantry_Marksman = 210;
        /// <summary>⭐ <c>CE-3254</c> (BR-9) — the other side's marksman: an SVD-class DMR (≈ 1 mil), ×4, faction 2. ⛔ PERMANENT WIRE VALUE.</summary>
        public const long Infantry_MarksmanSvd = 211;

        // Tactical Graphics
        public const long TacGraphic_FireLine = 8801;
        public const long TacGraphic_Route = 8802;
        public const long TacGraphic_Area = 8803;

        // ── Terrain ───────────────────────────────────────────────────────────────────────────
        // ⛔ PERMANENT WIRE VALUE. This id reaches replays and saved scenarios — deprecate, never
        //    recycle, never renumber.
        // ⭐ Deliberately NOT a TacGraphic_* value: a zone is a LOAD DIRECTIVE that happens to be
        //    drawn, not a tactical graphic. Consumers of 8803 (symbology, ORBAT, templates) must not
        //    have to branch to exclude zones, which is exactly what a second discriminator field on
        //    TacGraphic_Area would have forced.
        // 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1.
        public const long TerrainZone = 8804;

        // ⛔ PERMANENT WIRE VALUE (as above). ⭐ Buildings Stage 5b — a terrain DOOR as an entity: created once per terrain door
        //    by the scenario load step, its runtime id from the one allocator, its key a TerrainObjectKey, its state a replicated
        //    DoorState. Never in the palette (a bare template with no visual). 📄 docs/DESIGN_Building_Interiors.md §3a, §3b, §3j.
        public const long Door = 8805;

        // ⭐ CE-3136 P-7a (O4, R-242) — STATIC OBSTACLES: entities that are TERRAIN (a box of a wall-library material, baked into every
        //   node's world). 📄 docs/DESIGN_Peek_And_Fire.md §9.
        public const long Obstacle_Car           = 8806;
        public const long Obstacle_SandbagWall   = 8807;
        public const long Obstacle_ConcreteBlock = 8808;
        public const long Obstacle_Crate         = 8809;

        /// <summary>The four starter obstacle types (O4).</summary>
        public static readonly long[] Obstacles = { Obstacle_Car, Obstacle_SandbagWall, Obstacle_ConcreteBlock, Obstacle_Crate };

        // ⛔ PERMANENT WIRE VALUE (as above). ⭐ CE-3216 (R-3012, W-0) — the WORLD ENVIRONMENT: one per scenario, the FIRST entity with
        //   no SimTransform (presence of SimTransform is the engine's "is this spatial?" predicate, docs/designs/tkb-1/DESIGN.md
        //   2026-09-13 — so it is never a perception, EQS or ballistics candidate). Carries the ScenarioClock (and, S1, the global
        //   weather). Never in the palette; the load creates it (C-3). 📄 docs/designs/weather/DESIGN_Weather_And_Time.md.
        public const long WorldEnvironment = 8810;

        // ⛔ PERMANENT WIRE VALUE (as above). ⭐ CE-3216 S1 (R-3012, W-1) — a WEATHER REGION: an area (EditablePolyline + a SimTransform)
        //   carrying WeatherConditions, blended into the global weather by WeatherField. Its OWN type, like TerrainZone (8804) — consumers of
        //   the tactical area never have to branch to exclude it. 📄 docs/designs/weather/DESIGN_Weather_And_Time.md §1.
        public const long WeatherRegion = 8811;

        // ⛔ PERMANENT WIRE VALUE (as above). ⭐ CE-3235 (B-13, AW-8) — a STONE SOURCE: an area (EditablePolyline + a SimTransform) whose
        //   inside tops a man's hands up with stones (ItemSourceDto on its template; ItemSources.Refill). Its OWN type, like the Weather
        //   Region. 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §7.10 PU-7.
        public const long StoneSource = 8812;
        // ⭐ AQ93 — the TRAFFIC block 8850–8859 (SUMO), kept clear of the sequential 88xx run so two lanes cannot collide again:
        //   8812 was allocated twice on 2026-10-17 (the backend's Stone Source kept it, the traffic types moved here).
        // ⛔ PERMANENT WIRE VALUE (as above). ⭐ AQ93 R-4021 — the scenario's TRAFFIC settings: positionless like the World Environment,
        //   carrying TrafficDemand. Never in the palette. 📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §11.3 (D4).
        public const long TrafficDemand = 8850;

        // ⛔ PERMANENT WIRE VALUE (as above). ⭐ AQ93 R-4038 — a SUMO CAR's replica: an inert body SUMO drives (no brain, no car
        //   kinematics — the mirror writes its pose every frame), with the civilian car's DIS type. Never in the palette.
        public const long TrafficCar = 8851;

        // ⭐ CE-1041 — Aircraft (400–499): CG-referenced bodies resting on their gear (Body.Geometry).
        //   📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md G4.
        public const long Heli_UH60  = 400;
        public const long Jet_F16    = 401;
        public const long Cargo_C130 = 402;
        /// <summary>⭐ M7 (DESIGN_FPV_Drone §2) — a small FPV attack quadcopter: a Rotorcraft with a shaped-charge warhead (munition 6016).</summary>
        public const long Drone_Fpv  = 403;
        /// <summary>⭐ CE-1083 (DESIGN_Map_3D_Mode §6m C-3) — a UH-60 flown with its cabin doors off: eight riders sit in the doors, legs out.</summary>
        public const long Heli_UH60_DoorsOff = 404;
        /// <summary>⭐ Air Ops slice 4 (DESIGN_Air_Operations §6, §8) — an A-10 class ATTACK AIRCRAFT (~150 m/s): a FixedWing flyer with four
        /// Mk-82 bombs (munition 6022) on a Bomb mount, released by the weapon brain's Release profile.</summary>
        public const long Jet_AttackA10 = 405;
        /// <summary>⭐ Air Ops slice 6a (DESIGN_Air_Operations §7a, AW-5) — an AH-64-class ATTACK HELICOPTER: a Rotorcraft with a chin M134
        /// turret (mount 0) and a fixed-forward Hydra-70 rocket pod (mount 1, munition 6025).</summary>
        public const long Heli_AH64 = 406;

        // Composite Units
        public const long Unit_TankPlatoon = 301;
        public const long Unit_InfantrySquad = 302;
        public const long Unit_TankPlatoon_Auto = 303;
        /// <summary>⭐ IF-1 (I8, FM-1) — an artillery battery (ArtilleryUnit 11.1.*.4): the commander unit of a fire mission.</summary>
        public const long Unit_ArtilleryBattery = 304;
        /// <summary>⭐ <c>CE-2262</c> SL-0 (CB-16, R-2030) — an INFANTRY PLATOON: a body-less commander whose roster is three
        /// <see cref="Unit_InfantrySquad"/> commanders — the first composite of composites (the platoon plays, Command Chain CC-D).</summary>
        public const long Unit_InfantryPlatoon = 305;

        // Civilian & Insurgent Types
        public const long CivilianPedestrian = 501;
        public const long CivilianCar = 502;
        public const long MilitaryApc = 503;
        public const long InfantrySoldier = 504;
        public const long Insurgent = 505;
    }
}
