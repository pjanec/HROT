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

            template.AddComponent(new EntityInfo
            {
                Name = new FixedString64(name),
                ForceId = ForceId.Neutral
            });
            template.AddMandatoryComponent<EntityInfo>(isHard: true);

            // Include SimTransform so that the Muscle authority path (DeferredTakeoverSystem)
            // can claim it when WorldPos is delegated. Silently skipped on worlds that
            // haven't registered the component (e.g. pure-IG worlds that receive SimTransform
            // via GeoSpatialIngressTranslator; preserveExisting=true protects the live value).
            template.AddComponent(new SimTransform());

            // Include NetworkTransform so entities spawned from this blueprint
            // already have a shadow-state component for GeoSpatial egress change-detection
            // (SimHost) and for ingress interpolation (IG). Silently skipped on worlds
            // that haven't registered the component (safe via TkbTemplate design).
            template.AddComponent(new NetworkTransform());

            // block promotion (and the ownership takeover by SimHost) until the initial coordinates arrive over the network
            template.AddMandatoryComponent<SimTransform>(isHard: true);

            // Override: RegisterTemplate -> Register
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
            
            var visualDef = new IgVisualDef();
            configure(visualDef);

            template.AddComponent(new VisualData
            {
                SymbolCode   = visualDef.SymbolCode ?? string.Empty,
                ModelPath    = visualDef.ModelPath ?? string.Empty,
                ColorHex     = visualDef.ColorHex ?? string.Empty,
                MapShapeName = visualDef.MapShapeName ?? string.Empty,
            });
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

            template.AddComponent(BuildVehicleParams(physicsDef));
            template.AddComponent(new PhysicsCollider
            {
                Radius         = Math.Max(physicsDef.Length, physicsDef.Width) / 2f,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer
            });
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
                    CooldownSecondsRemaining = 0f
                });
            }

            float maxHp = combatDefComponents.ArmorFront > 400f ? 300f
                        : combatDefComponents.ArmorFront > 100f ? 150f
                        : 100f;
            template.AddComponent(new Health { Current = maxHp, Max = maxHp });
            template.AddComponent(new PhysicsCollider
            {
                Radius         = 2.5f,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer
            });

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

            var forceId = factionId switch
            {
                1 => ForceId.Friend,
                2 => ForceId.Hostile,
                _ => ForceId.Neutral,
            };
            template.AddComponent(new EntityInfo { ForceId = forceId });
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

            // CarKinem navigation
            template.AddComponent(new VehicleState());
            template.AddComponent(new NavState());
            template.AddComponent(new SimVelocity());

            // Behavior / mission
            template.AddComponent(new BehaviorState
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

            // CQRS navigation contract (MOD1-P1T1): Brain writes NavigationIntent;
            // Muscle layer (NavigationExecutionSystem) writes NavigationStatus.
            // FrustrationTicks is the per-entity stuck-detection counter.
            // All three must be present on the template so MoveToExecutor never
            // encounters a missing-component exception on entity spawn.
            template.AddComponent(new NavigationIntent());
            template.AddComponent(new NavigationStatus());
            template.AddComponent(new FrustrationTicks());

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

            template.AddManagedComponent(() =>
            {
                var freshDef = new TkbCompositionDef();
                configure(freshDef);
                return freshDef;
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
