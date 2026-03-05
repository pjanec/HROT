using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Replication.Components;

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

            // Include NetworkTransform so entities spawned from this blueprint
            // already have a shadow-state component for GeoSpatial egress change-detection
            // (SimHost) and for ingress interpolation (IG). Silently skipped on worlds
            // that haven't registered the component (safe via TkbTemplate design).
            template.AddComponent(new NetworkTransform());

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
        /// Stamps the navigation and behavior-brain components required for the
        /// CarKinem locomotion and FDP BTree doctrine systems to process this entity.
        ///
        /// Must be called for every vehicle that should accept mission assignments
        /// (e.g. WanderMilitary doctrine via <c>MissionControlRequest</c>).
        ///
        /// Components added (all zero-initialised; systems populate them at runtime):
        /// <list type="bullet">
        ///   <item><see cref="VehicleState"/> / <see cref="NavState"/> — CarKinem kinematics.</item>
        ///   <item><see cref="SimVelocity"/> — world-space velocity written by locomotion.</item>
        ///   <item><see cref="DoctrineState"/> (BrainTier = BTree) — active doctrine hash.</item>
        ///   <item><see cref="MissionPlanQueue"/> — phase queue maintained by MissionAdapterSystem.</item>
        ///   <item><see cref="BrainBTreeState"/> / <see cref="BrainBlackboard"/> — brain execution state.</item>
        ///   <item><see cref="LocomotionChannel"/> / <see cref="WeaponChannel"/> / <see cref="InteractionChannel"/> — action dispatch channels.</item>
        ///   <item><see cref="ActorCapabilityState"/> (CanMove | CanShoot) — capability bits.</item>
        /// </list>
        /// </summary>
        public BdcTkbBuilder WithBehavior(long tkbId)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // CarKinem navigation
            template.AddComponent(new VehicleState());
            template.AddComponent(new NavState());
            template.AddComponent(new SimVelocity());

            // Doctrine / mission
            template.AddComponent(new DoctrineState
            {
                BrainTier = BehaviorConstants.BrainTierBTree
            });
            template.AddComponent(new MissionPlanQueue());

            // Brain execution
            template.AddComponent(new BrainBTreeState());
            template.AddComponent(new BrainBlackboard());

            // Action dispatch channels
            template.AddComponent(new LocomotionChannel());
            template.AddComponent(new WeaponChannel());
            template.AddComponent(new InteractionChannel());

            // Capability bits — vehicles can move and shoot by default
            template.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });

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
