using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb;

namespace Bagira.Map.Definitions.Tkb
{
    public class BdcTkbBuilder
    {
        private readonly TkbDatabase _db;
        
        public BdcTkbBuilder(TkbDatabase db)
        {
            _db = db;
        }
        
        /// <summary>
        /// Define new vehicle entity type.
        /// </summary>
        public BdcTkbBuilder DefineVehicle(long tkbId, string name)
        {
            var template = new TkbTemplate(name, tkbId);
            
            // Override: RegisterTemplate -> Register
            _db.Register(template);
            return this;
        }
        
        /// <summary>
        /// Add visual properties (IG).
        /// </summary>
        public BdcTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            var visualDef = new IgVisualDef();
            configure(visualDef);

            template.AddComponent(new VisualData
            {
                SymbolCode = visualDef.SymbolCode ?? string.Empty,
                ModelPath = visualDef.ModelPath ?? string.Empty,
                ColorHex = visualDef.ColorHex ?? string.Empty
            });
            return this;
        }
        
        /// <summary>
        /// Add physics properties (SimHost).
        /// </summary>
        public BdcTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            var physicsDef = new SimVehicleDef();
            configure(physicsDef);

            template.AddComponent(BuildVehicleParams(physicsDef));
            return this;
        }
        
        /// <summary>
        /// Add combat properties (future).
        /// </summary>
        public BdcTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // Keep managed definition for IG inspector / ORBAT display.
            template.AddManagedComponent(() =>
            {
                var combatDef = new SimCombatDef();
                configure(combatDef);
                return combatDef;
            });

            // Translate to real FDP ECS unmanaged components.
            var combatDefComponents = new SimCombatDef();
            configure(combatDefComponents);

            if (combatDefComponents.SensorRange > 0f)
            {
                template.AddComponent(new PerceptionReceptor
                {
                    VisionRange    = combatDefComponents.SensorRange,
                    HearingRange   = combatDefComponents.SensorRange * 0.5f,
                    FieldOfViewCos = 0f
                });
                template.AddComponent(new TargetMemory());
            }

            if (combatDefComponents.Weapons.Count > 0)
            {
                var primary = combatDefComponents.Weapons[0];
                template.AddComponent(new WeaponState
                {
                    Ammo                   = primary.Ammunition,
                    MuzzleVelocity         = primary.Range > 0f ? primary.Range : 800f,
                    CooldownTicksRemaining = 0
                });
            }

            float maxHp = combatDefComponents.ArmorFront > 400f ? 300f
                        : combatDefComponents.ArmorFront > 100f ? 150f
                        : 100f;
            template.AddComponent(new Health { Current = maxHp, Max = maxHp });
            template.AddComponent(new HealthData { Current = maxHp, Max = maxHp });
            template.AddComponent(new PhysicsCollider
            {
                Radius         = 2.5f,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer
            });

            return this;
        }

        /// <summary>
        /// Add faction identification for perception/combat systems.
        /// </summary>
        public BdcTkbBuilder WithFaction(long tkbId, byte factionId)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            template.AddComponent(new Faction { FactionId = factionId });
            return this;
        }
        
        /// <summary>
        /// Add composite (ORBAT) definition.
        /// </summary>
        public BdcTkbBuilder AsComposite(long tkbId, Action<TkbCompositionDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            template.AddManagedComponent(() => 
            {
                var compositionDef = new TkbCompositionDef();
                configure(compositionDef);
                return compositionDef;
            });
            return this;
        }

        private static VehicleParams BuildVehicleParams(SimVehicleDef def)
        {
            var vehicleClass = def.Mobility switch
            {
                TerrainMobility.Tracked => VehicleClass.Tank,
                TerrainMobility.Wheeled => VehicleClass.Truck,
                TerrainMobility.Infantry => VehicleClass.Pedestrian,
                TerrainMobility.Air => VehicleClass.PersonalCar,
                TerrainMobility.Naval => VehicleClass.PersonalCar,
                _ => VehicleClass.PersonalCar
            };

            var preset = VehiclePresets.GetPreset(vehicleClass);
            preset.Class = vehicleClass;

            if (def.Length > 0f)
            {
                preset.Length = def.Length;
                preset.WheelBase = def.Length * 0.6f;
            }
            if (def.Width > 0f)
                preset.Width = def.Width;
            if (def.MaxSpeed > 0f)
                preset.MaxSpeedFwd = def.MaxSpeed;
            if (def.Acceleration > 0f)
                preset.MaxAccel = def.Acceleration;
            if (def.TurnRate > 0f)
                preset.MaxSteerRate = def.TurnRate * (MathF.PI / 180f);

            return preset;
        }
    }
}
