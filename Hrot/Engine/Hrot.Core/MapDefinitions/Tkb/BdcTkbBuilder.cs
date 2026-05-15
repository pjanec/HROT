using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;

namespace Hrot.Map.Definitions.Tkb
{
    public class NedTkbBuilder
    {
        private readonly TkbDatabase _db;
        
        public NedTkbBuilder(TkbDatabase db)
        {
            _db = db;
        }
        
        /// <summary>
        /// Define new vehicle entity type.
        /// </summary>
        public NedTkbBuilder DefineVehicle(long tkbId, string name)
        {
            var template = new TkbTemplate(name, tkbId);
            template.AddDescriptor(new TkbMasterDto { CustomName = name });
            template.AddMandatoryComponent<EntityInfo>(isHard: true);
            // SimTransform will be stamped by translator in Phase 6.
            template.AddMandatoryComponent<SimTransform>(isHard: true);
            _db.Register(template);
            return this;
        }
        
        /// <summary>
        /// Assigns a DIS Entity Type to the template.
        /// The type is stamped onto the entity header via
        /// <see cref="Fdp.Interfaces.TkbTemplate.DisType"/> when the blueprint is applied,
        /// enabling the <c>MapLayerAssignmentSystem</c> to classify the entity into the
        /// correct rendering layer without string look-ups in the hot path.
        /// </summary>
        /// <param name="tkbId">The TKB type identifier to look up.</param>
        /// <param name="disType">The DIS entity type to assign.</param>
        public NedTkbBuilder WithDisType(long tkbId, DISEntityType disType)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            template.DisType = disType;
            return this;
        }

        /// <summary>
        /// Add visual properties (IG).
        /// </summary>
        public NedTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // VisualData ECS component will be applied by IG-side translator in Phase 6.
            return this;
        }
        
        /// <summary>
        /// Add physics properties (SimHost).
        /// </summary>
        public NedTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var physicsDef = new SimVehicleDef();
            configure(physicsDef);

            template.AddDescriptor(new VehicleParametersDto
            {
                Mass        = physicsDef.Mass,
                Length      = physicsDef.Length,
                Width       = physicsDef.Width,
                MaxSpeedFwd = physicsDef.MaxSpeed,
                MaxSpeedRev = physicsDef.MaxSpeedRev,
                MaxAccel    = physicsDef.Acceleration,
            });
            // Height, TurnRate, Mobility mapped to VehicleParams by translator in Phase 6.
            return this;
        }
        
        /// <summary>
        /// Add combat properties (future).
        /// </summary>
        public NedTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var combatDef = new SimCombatDef();
            configure(combatDef);

            // Store the combat definition as a descriptor for inspector / ORBAT display.
            template.AddDescriptor(combatDef);

            // Derived capability DTO for the general TKB pipeline.
            if (combatDef.Weapons.Count > 0)
            {
                var primary = combatDef.Weapons[0];
                template.AddDescriptor(new WeaponCapabilitiesDto
                {
                    EffectiveRange   = primary.Range,
                    RateOfFire       = primary.RateOfFire,
                    MagazineCapacity = primary.Ammunition,
                });
            }
            // ECS components (PerceptionReceptor, WeaponState, Health, PhysicsCollider)
            // will be stamped by translators in Phase 6.

            return this;
        }

        /// <summary>
        /// Add force affiliation for perception/combat systems.
        /// </summary>
        public NedTkbBuilder WithFaction(long tkbId, byte factionId)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // EntityInfo.ForceId will be stamped by translator in Phase 6.
            return this;
        }

        /// <summary>
        /// Stamps the navigation and behavior-brain components required for the
        /// CarKinem locomotion and FDP BTree behavior systems to process this entity.
        ///
        /// Must be called for every vehicle that should accept mission assignments
        /// (e.g. WanderMilitary behavior via <c>MissionControlRequest</c>).
        ///
        /// Components added (all zero-initialised; systems populate them at runtime):
        /// <list type="bullet">
        ///   <item><see cref="VehicleState"/> / <see cref="NavState"/> — CarKinem kinematics.</item>
        ///   <item><see cref="SimVelocity"/> — world-space velocity written by locomotion.</item>
        ///   <item><see cref="BehaviorState"/> (BrainTier = BTree) — active behavior hash.</item>
        ///   <item><see cref="MissionPlanQueue"/> — phase queue maintained by MissionAdapterSystem.</item>
        ///   <item><see cref="BrainBTreeState"/> / <see cref="BrainBlackboard"/> — brain execution state.</item>
        ///   <item><see cref="LocomotionChannel"/> / <see cref="WeaponChannel"/> / <see cref="InteractionChannel"/> — action dispatch channels.</item>
        ///   <item><see cref="ActorCapabilityState"/> (CanMove | CanShoot) — capability bits.</item>
        /// </list>
        /// </summary>
        public NedTkbBuilder WithBehavior(long tkbId)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // Behavior and navigation ECS components will be applied by translators in Phase 6.
            return this;
        }

        /// <summary>
        /// Adds the <see cref="Blackboard1024"/> heavy working-memory component to the
        /// template.  Required for commander entities that project
        /// <c>Blackboard1024.Memory</c> onto a behavior-specific mutable-state struct
        /// (e.g., <c>HillAttackMutableState</c>).
        /// </summary>
        public NedTkbBuilder WithHeavyMemory(long tkbId)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            // Blackboard1024 ECS component will be applied by translator in Phase 6.
            return this;
        }
        
        /// <summary>
        /// Add composite (ORBAT) definition.
        /// </summary>
        public NedTkbBuilder AsComposite(long tkbId, Action<TkbCompositionDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var entityInfoTypeId = ComponentTypeRegistry.GetOrRegisterManaged(typeof(EntityInfo));
            if (!template.MandatoryComponents.Exists(c => c.ComponentTypeId == entityInfoTypeId))
            {
                template.AddMandatoryComponent<EntityInfo>(isHard: true);
            }
            
            // Evaluate composition immediately to populate TkbTemplate metadata.
            var compositionDef = new TkbCompositionDef();
            configure(compositionDef);

            if (compositionDef.AutoCreateChildren)
            {
                foreach (var slot in compositionDef.Subordinates)
                {
                    for (int i = 0; i < slot.Count; i++)
                    {
                        template.ChildBlueprints.Add(new ChildBlueprintDefinition(
                            instanceId:   template.ChildBlueprints.Count + 1,
                            childTkbType: slot.TkbType,
                            designation:  slot.Designation
                        ));
                    }
                }
            }

            // Store as descriptor instead of managed component.
            template.AddDescriptor(compositionDef);
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
            if (def.MaxSpeedRev > 0f)
                preset.MaxSpeedRev = def.MaxSpeedRev;
            if (def.Acceleration > 0f)
                preset.MaxAccel = def.Acceleration;
            if (def.TurnRate > 0f)
                preset.MaxSteerRate = def.TurnRate * (MathF.PI / 180f);

            return preset;
        }
    }
}
