using Fdp.Toolkit.Tkb;
using Bagira.Map.Common;

namespace Bagira.Map.Definitions.Tkb
{
    public static class BdcTkbCatalog
    {
        public static void RegisterAll(TkbDatabase tkbDb)
        {
            var builder = new BdcTkbBuilder(tkbDb);
            
            // M1 Abrams
            builder
                .DefineVehicle(TkbEntityTypes.Tank_M1Abrams, "M1 Abrams")
                .WithVisual(TkbEntityTypes.Tank_M1Abrams, v =>
                {
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
                        RateOfFire = 6
                    });
                    c.SensorRange = 8000;
                })
                .WithFaction(TkbEntityTypes.Tank_M1Abrams, 1);
            
            // Bradley IFV
            builder
                .DefineVehicle(TkbEntityTypes.IFV_Bradley, "M2 Bradley IFV")
                .WithVisual(TkbEntityTypes.IFV_Bradley, v =>
                {
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
                    c.Weapons.Add(new WeaponMount { WeaponType = "25mm_M242", Ammunition = 300, Range = 2500, RateOfFire = 200 });
                    c.Weapons.Add(new WeaponMount { WeaponType = "TOW_ATGM", Ammunition = 7, Range = 3750, RateOfFire = 2 });
                    c.SensorRange = 5000;
                })
                .WithFaction(TkbEntityTypes.IFV_Bradley, 1);
            
            // HMMWV
            builder
                .DefineVehicle(TkbEntityTypes.Truck_HMMWV, "HMMWV")
                .WithVisual(TkbEntityTypes.Truck_HMMWV, v =>
                {
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
                .WithFaction(TkbEntityTypes.Truck_HMMWV, 1);
            
            // T-72 (OPFOR)
            builder
                .DefineVehicle(TkbEntityTypes.Tank_T72, "T-72")
                .WithVisual(TkbEntityTypes.Tank_T72, v =>
                {
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
                    p.Acceleration = 2.0f;
                    p.TurnRate = 12.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_T72, c =>
                {
                    c.ArmorFront = 500;
                    c.ArmorSide = 250;
                    c.ArmorRear = 150;
                    c.Weapons.Add(new WeaponMount { WeaponType = "125mm_2A46", Ammunition = 39, Range = 2800, RateOfFire = 8 });
                    c.SensorRange = 6000;
                })
                .WithFaction(TkbEntityTypes.Tank_T72, 2);
            
            // Infantry Rifleman
            builder
                .DefineVehicle(TkbEntityTypes.Infantry_Rifleman, "Rifleman")
                .WithVisual(TkbEntityTypes.Infantry_Rifleman, v =>
                {
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
                    c.Weapons.Add(new WeaponMount { WeaponType = "M4_Carbine", Ammunition = 210, Range = 300, RateOfFire = 700 });
                    c.SensorRange = 500;
                })
                .WithFaction(TkbEntityTypes.Infantry_Rifleman, 1);
            
            // Tank Platoon (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_TankPlatoon, "Tank Platoon")
                .WithVisual(TkbEntityTypes.Unit_TankPlatoon, v =>
                {
                    v.SymbolCode = "SFGPUCIZ--H----"; // Platoon echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.5f;
                })
                .WithFaction(TkbEntityTypes.Unit_TankPlatoon, 1)
                .AsComposite(TkbEntityTypes.Unit_TankPlatoon, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Tank_M1Abrams, Count = 4, RoleTag = "Tank" });
                    comp.Echelon = "Platoon";
                    comp.AutoCreateChildren = false; // Manual creation
                });
            
            // Infantry Squad (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad")
                .WithVisual(TkbEntityTypes.Unit_InfantrySquad, v =>
                {
                    v.SymbolCode = "SFGPUCI---H----"; // Squad echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.2f;
                })
                .WithFaction(TkbEntityTypes.Unit_InfantrySquad, 1)
                .AsComposite(TkbEntityTypes.Unit_InfantrySquad, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Officer, Count = 1, RoleTag = "SquadLeader" });
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Rifleman, Count = 9, RoleTag = "Rifleman" });
                    comp.Echelon = "Squad";
                    comp.AutoCreateChildren = false;
                });
        }
    }
}
