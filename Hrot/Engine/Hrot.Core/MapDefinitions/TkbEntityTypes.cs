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

        // Lifeforms
        public const long Infantry_Rifleman = 200;
        public const long Infantry_Officer = 201;

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

        // Civilian & Insurgent Types
        public const long CivilianPedestrian = 501;
        public const long CivilianCar = 502;
        public const long MilitaryApc = 503;
        public const long InfantrySoldier = 504;
        public const long Insurgent = 505;
    }
}
