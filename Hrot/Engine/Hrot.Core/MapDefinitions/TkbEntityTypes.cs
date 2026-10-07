namespace Hrot.Map.Common
{
    public static class TkbEntityTypes
    {
        // Ground Platforms
        public const long Tank_M1Abrams = 100;
        public const long IFV_Bradley = 101;
        public const long Truck_HMMWV = 102;
        public const long Tank_T72 = 103;

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
