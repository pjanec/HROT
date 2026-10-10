using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Hrot.Map.Common;

namespace Hrot.Map.Definitions.Tkb
{
    public static class NedTkbCatalog
    {
        /// <summary>⭐ CE-1017 S0 — SISO-REF-010 country codes used by the built-in types (names: <c>DisNameTable</c>).</summary>
        /// <summary>⭐ AQ85 C (R-216) — the rifle's aim dispersion, mils (same as UrbanCombat's).</summary>
        public const float RifleDispersionMils = 6f;
        public const ushort UnitedStates = 225;
        /// <summary>⭐ CE-1017 S0 — SISO-REF-010 Russia.</summary>
        public const ushort Russia = 222;

        public static void RegisterAll(TkbDatabase tkbDb)
        {
            var builder = new NedTkbBuilder(tkbDb);
            
            // M1 Abrams
            builder
                .DefineVehicle(TkbEntityTypes.Tank_M1Abrams, "M1 Abrams")
                .WithVisual(TkbEntityTypes.Tank_M1Abrams, v =>
                {
                    v.IconName = "m1_abrams";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCIZ-------";
                    v.ModelPath = "models/m1_abrams.obj";
                    v.ColorHex = "#2E4057";
                    v.Scale = 1.2f;
                    v.ShowLabel = true;
                })
                .WithPhysics(TkbEntityTypes.Tank_M1Abrams, p =>
                {
                    p.Mass = 61_000; // kg
                    p.Length = 7.93f;
                    p.Width = 3.66f;
                    p.Height = 2.44f;
                    p.MaxSpeed = 20.0f; // m/s
                    p.MaxSpeedRev = 12.0f; // m/s
                    p.Acceleration = 2.5f;
                    p.TurnRate = 15.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_M1Abrams, c =>
                {
                    c.ArmorFront = 600; // mm RHA
                    c.ArmorSide = 350;
                    c.ArmorRear = 200;
                    c.Weapons.Add(new WeaponMount
                    {
                        WeaponType = "120mm_M256",
                        Ammunition = 42,
                        Range = 3000,
                        RateOfFire = 6,
                        Penetration = 650,     // CE-3071 — mm RHA (design §9 calibration)
                        DamagePerHit = 1200,
                    });
                    c.SensorRange = 8000;
                })
                .WithFaction(TkbEntityTypes.Tank_M1Abrams, 1)
                .WithBehavior(TkbEntityTypes.Tank_M1Abrams)
                .WithDisType(TkbEntityTypes.Tank_M1Abrams, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 1 });
            
            // Bradley IFV
            builder
                .DefineVehicle(TkbEntityTypes.IFV_Bradley, "M2 Bradley IFV")
                .WithVisual(TkbEntityTypes.IFV_Bradley, v =>
                {
                    v.IconName = "m2_bradley";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCI--------";
                    v.ModelPath = "models/bradley.obj";
                    v.ColorHex = "#2E4057";
                    v.Scale = 1.0f;
                })
                .WithPhysics(TkbEntityTypes.IFV_Bradley, p =>
                {
                    p.Mass = 27_000;
                    p.Length = 6.55f;
                    p.Width = 3.6f;
                    p.Height = 2.98f;
                    p.MaxSpeed = 18.0f;
                    p.Acceleration = 3.0f;
                    p.TurnRate = 20.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.IFV_Bradley, c =>
                {
                    c.ArmorFront = 100;
                    c.ArmorSide = 60;
                    c.ArmorRear = 40;
                    c.Weapons.Add(new WeaponMount { WeaponType = "25mm_M242", Ammunition = 300, Range = 2500, RateOfFire = 200, Penetration = 60, DamagePerHit = 60 });
                    c.Weapons.Add(new WeaponMount { WeaponType = "TOW_ATGM", Ammunition = 7, Range = 3750, RateOfFire = 2, Penetration = 800, DamagePerHit = 2000 });
                    c.SensorRange = 5000;
                })
                .WithFaction(TkbEntityTypes.IFV_Bradley, 1)
                .WithBehavior(TkbEntityTypes.IFV_Bradley)
                .WithDisType(TkbEntityTypes.IFV_Bradley, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 2 });
            
            // HMMWV
            builder
                .DefineVehicle(TkbEntityTypes.Truck_HMMWV, "HMMWV")
                .WithVisual(TkbEntityTypes.Truck_HMMWV, v =>
                {
                    v.IconName = "hmmwv";   // CE-1017 S0
                    v.SymbolCode = "SFGPUUS--------";
                    v.ModelPath = "models/hmmwv.obj";
                    v.ColorHex = "#3E5641";
                    v.Scale = 0.9f;
                })
                .WithPhysics(TkbEntityTypes.Truck_HMMWV, p =>
                {
                    p.Mass = 2_400;
                    p.Length = 4.57f;
                    p.Width = 2.16f;
                    p.Height = 1.83f;
                    p.MaxSpeed = 25.0f;
                    p.Acceleration = 4.0f;
                    p.TurnRate = 30.0f;
                    p.Mobility = TerrainMobility.Wheeled;
                })
                .WithFaction(TkbEntityTypes.Truck_HMMWV, 1)
                .WithBehavior(TkbEntityTypes.Truck_HMMWV)
                .WithDisType(TkbEntityTypes.Truck_HMMWV, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 6 });   // CE-1017 S0: 6 = small wheeled utility vehicle (SISO-REF-010); 3 read "armored utility vehicle"
            
            // T-72 (OPFOR)
            builder
                .DefineVehicle(TkbEntityTypes.Tank_T72, "T-72")
                .WithVisual(TkbEntityTypes.Tank_T72, v =>
                {
                    v.IconName = "t72";   // CE-1017 S0
                    v.SymbolCode = "SHGPUCIZ-------"; // Hostile
                    v.ModelPath = "models/t72.obj";
                    v.ColorHex = "#8B0000";
                    v.Scale = 1.1f;
                })
                .WithPhysics(TkbEntityTypes.Tank_T72, p =>
                {
                    p.Mass = 41_000;
                    p.Length = 6.95f;
                    p.Width = 3.59f;
                    p.Height = 2.23f;
                    p.MaxSpeed = 17.0f;
                    p.MaxSpeedRev = 10.0f;
                    p.Acceleration = 2.0f;
                    p.TurnRate = 12.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_T72, c =>
                {
                    c.ArmorFront = 500;
                    c.ArmorSide = 250;
                    c.ArmorRear = 150;
                    c.Weapons.Add(new WeaponMount { WeaponType = "125mm_2A46", Ammunition = 39, Range = 2800, RateOfFire = 8, Penetration = 600, DamagePerHit = 1100 });
                    c.SensorRange = 6000;
                })
                .WithFaction(TkbEntityTypes.Tank_T72, 2)
                .WithBehavior(TkbEntityTypes.Tank_T72)
                .WithDisType(TkbEntityTypes.Tank_T72, new DISEntityType { Kind = 1, Domain = 1, Country = Russia, Category = 1 });
            
            // Infantry Rifleman
            builder
                .DefineVehicle(TkbEntityTypes.Infantry_Rifleman, "Rifleman")
                .WithVisual(TkbEntityTypes.Infantry_Rifleman, v =>
                {
                    v.IconName = "rifleman";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCI--------";
                    v.ModelPath = "models/soldier.obj";
                    v.ColorHex = "#556B2F";
                    v.Scale = 0.6f;
                })
                .WithPhysics(TkbEntityTypes.Infantry_Rifleman, p =>
                {
                    p.Mass = 100;
                    p.Length = 0.6f;
                    p.Width = 0.4f;
                    p.Height = 1.75f;
                    p.MaxSpeed = 2.5f; // Walking
                    p.Acceleration = 1.0f;
                    p.TurnRate = 90.0f;
                    p.Mobility = TerrainMobility.Infantry;
                })
                .WithCombat(TkbEntityTypes.Infantry_Rifleman, c =>
                {
                    c.ArmorFront = 5; // Body armor
                    c.Weapons.Add(new WeaponMount { WeaponType = "M4_Carbine", Ammunition = 210, Range = 300, RateOfFire = 700, Penetration = 5, DamagePerHit = 25,
                        DispersionMils = RifleDispersionMils });   // ⭐ AQ85 C — the first opt-in (~50 % at 100 m standing still)
                    c.SensorRange = 500;
                })
                .WithFaction(TkbEntityTypes.Infantry_Rifleman, 1)
                .WithBehavior(TkbEntityTypes.Infantry_Rifleman)
                .WithDisType(TkbEntityTypes.Infantry_Rifleman, new DISEntityType { Kind = 3, Domain = 1, Country = UnitedStates, Category = 1 });
            
            // Tank Platoon (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_TankPlatoon, "Tank Platoon")
                .WithVisual(TkbEntityTypes.Unit_TankPlatoon, v =>
                {
                    v.IconName = "tank_platoon";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCIZ--H----"; // Platoon echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.5f;
                })
                .WithFaction(TkbEntityTypes.Unit_TankPlatoon, 1)
                .WithBehavior(TkbEntityTypes.Unit_TankPlatoon)
                .AsComposite(TkbEntityTypes.Unit_TankPlatoon, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Tank_M1Abrams, Count = 4, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Platoon";
                    comp.AutoCreateChildren = false; // Manual creation
                })
                .WithDisType(TkbEntityTypes.Unit_TankPlatoon, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates });
            
            // Infantry Squad (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad")
                .WithVisual(TkbEntityTypes.Unit_InfantrySquad, v =>
                {
                    v.IconName = "infantry_squad";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCI---H----"; // Squad echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.2f;
                })
                .WithFaction(TkbEntityTypes.Unit_InfantrySquad, 1)
                .AsComposite(TkbEntityTypes.Unit_InfantrySquad, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Officer, Count = 1, Designation = TacticalDesignation.SquadLeader });
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Rifleman, Count = 9, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Squad";
                    comp.AutoCreateChildren = false;
                })
                .WithDisType(TkbEntityTypes.Unit_InfantrySquad, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates });

            // Tank Platoon (Auto-Spawning)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_TankPlatoon_Auto, "Tank Platoon (Auto Spawn)")
                .WithVisual(TkbEntityTypes.Unit_TankPlatoon_Auto, v =>
                {
                    v.IconName = "tank_platoon";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCIZ--H----"; // Platoon echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.5f;
                })
                .WithFaction(TkbEntityTypes.Unit_TankPlatoon_Auto, 1)
                .WithBehavior(TkbEntityTypes.Unit_TankPlatoon_Auto)
                .AsComposite(TkbEntityTypes.Unit_TankPlatoon_Auto, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Tank_M1Abrams, Count = 4, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Platoon";
                    comp.AutoCreateChildren = true; // The engine will now auto-spawn 4x M1 Abrams when this is created
                })
                .WithDisType(TkbEntityTypes.Unit_TankPlatoon_Auto, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates });

            // ⭐⭐ CE-1041 — AIRCRAFT. CG-referenced bodies resting on their gear: the gear positions are what landing and parking
            //   use (BodyGeometry.RestingPose). ⛔ No WithPhysics — that adds VehicleParametersDto, i.e. CAR kinematics; an aircraft
            //   carries its size in Body.Geometry. ⛔ No WithBehavior — there is no flight model for a brain to drive yet.
            //   Public figures, rounded; the gear's X is from the CG. DIS subcategory left 0 (generic) — not verified.
            //   📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md G4.
            DefineAircraft(builder, TkbEntityTypes.Heli_UH60, "UH-60A Black Hawk", "SFAPMHU-------", "#56604F",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 21 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 19.76f, Width = 16.36f, Height = 5.13f,   // rotors turning; width = main-rotor diameter
                    BodyCentreX = -1.70f, BodyCentreZ = 0.815f,        // box bottom = the tyres (−1.75)
                    GroundContacts =
                    {
                        Wheel("main left",  2.6f,  1.35f, -1.75f, 0.38f, 0.9f, retractable: false),
                        Wheel("main right", 2.6f, -1.35f, -1.75f, 0.38f, 0.9f, retractable: false),
                        Wheel("tail",      -6.2f,  0f,    -1.75f, 0.20f, 0.7f, retractable: false),
                    },
                });
            DefineAircraft(builder, TkbEntityTypes.Jet_F16, "F-16C Fighting Falcon", "SFAPMFF-------", "#8A929C",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 1 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 15.06f, Width = 9.96f, Height = 4.88f,
                    BodyCentreX = 0.50f, BodyCentreZ = 0.54f,          // box bottom = the tyres (−1.90)
                    GroundContacts =
                    {
                        Wheel("nose",        3.4f,  0f,    -1.90f, 0.24f, 1.2f, retractable: true),
                        Wheel("main left",  -0.6f,  1.18f, -1.90f, 0.36f, 1.0f, retractable: true),
                        Wheel("main right", -0.6f, -1.18f, -1.90f, 0.36f, 1.0f, retractable: true),
                    },
                });
            DefineAircraft(builder, TkbEntityTypes.Cargo_C130, "C-130H Hercules", "SFAPMFC-------", "#7A8076",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 4 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 29.79f, Width = 40.41f, Height = 11.66f,
                    BodyCentreX = 1.50f, BodyCentreZ = 2.83f,          // box bottom = the tyres (−3.00)
                    GroundContacts =
                    {
                        Wheel("nose",        9.4f,  0f,    -3.00f, 0.50f, 1.6f, retractable: true),
                        Wheel("main left",  -0.3f,  2.18f, -3.00f, 0.60f, 1.2f, retractable: true),
                        Wheel("main right", -0.3f, -2.18f, -3.00f, 0.60f, 1.2f, retractable: true),
                    },
                });

            // Tactical graphic: area overlay
            var areaTemplate = new TkbTemplate("TacGraphic_Area", TkbEntityTypes.TacGraphic_Area);
            // TKB-014 (Phase 6): ECS components will be injected by translators.
            tkbDb.Register(areaTemplate);

            // Tactical graphic: route entity (ROUTES1-T003)
            // RoutePlan (managed) is added by RouteTkbExtensions.ApplyRoutePlanToBlueprint()
            // in Phase 6 via translator.
            var routeTemplate = new TkbTemplate("TacGraphic_Route", TkbEntityTypes.TacGraphic_Route);
            // TKB-014 (Phase 6): ECS components will be injected by translators.
            tkbDb.Register(routeTemplate);

            // ⭐ Buildings Stage 5b — a terrain door (📄 docs/DESIGN_Building_Interiors.md §3j). A bare template like the two above:
            //   its state (DoorState) and key (TerrainObjectKey) arrive with the creation request on the creator and through the
            //   EntityDoorState descriptor everywhere else — nothing for a TKB translator to inject.
            tkbDb.Register(new TkbTemplate("Door", TkbEntityTypes.Door));

            // ⭐ CE-3136 P-7a (O4, R-242) — the four starter STATIC OBSTACLES: terrain made of a wall-library material
            //   (📄 docs/DESIGN_Peek_And_Fire.md §9). ⚠ The sizes and the car-body material are starter values to tune (§3c).
            RegisterObstacle(tkbDb, "Car",            TkbEntityTypes.Obstacle_Car,           4.5f, 1.8f, 1.5f, "car-body");
            RegisterObstacle(tkbDb, "Sandbag wall",   TkbEntityTypes.Obstacle_SandbagWall,   3.0f, 0.6f, 1.0f, "sandbags");
            RegisterObstacle(tkbDb, "Concrete block", TkbEntityTypes.Obstacle_ConcreteBlock, 2.0f, 1.0f, 1.0f, "concrete");
            RegisterObstacle(tkbDb, "Crate",          TkbEntityTypes.Obstacle_Crate,         1.2f, 1.0f, 1.0f, "fence-wood");
        }

        private static void RegisterObstacle(TkbDatabase tkbDb, string name, long type, float length, float width, float height, string material)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.StaticObstacleDto { Length = length, Width = width, Height = height, Material = material });
            tkbDb.Register(t);
        }

        /// <summary>⭐ CE-1041 — an aircraft: master, visual, DIS, faction and its body geometry — no car kinematics, no brain.</summary>
        private static void DefineAircraft(NedTkbBuilder builder, long type, string name, string symbol, string colour,
                                           DISEntityType dis, Fdp.Toolkit.Tkb.Domain.BodyGeometryDto geometry)
            => builder
                .DefineVehicle(type, name)
                .WithVisual(type, v => { v.SymbolCode = symbol; v.ColorHex = colour; v.ShowLabel = true; })
                .WithFaction(type, 1)
                .WithBodyGeometry(type, geometry)
                .WithDisType(type, dis);

        private static Fdp.Toolkit.Tkb.Domain.GroundContactPoint Wheel(string name, float x, float y, float z, float radius,
                                                                       float strut, bool retractable)
            => new() { Name = name, X = x, Y = y, Z = z, WheelRadius = radius, StrutLength = strut, Retractable = retractable,
                       Kind = Fdp.Toolkit.Tkb.Domain.GroundContactKind.Wheel };
    }
}
