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
        /// Add visual properties — the symbol, colour, model and label the map draws from.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b><c>CE-118</c> / <c>UXI-23 S1</c>, fixed 2026-08-28.</b> 🔴 This method used to
        /// <b>discard everything it was given</b>: it resolved the template, never called
        /// <paramref name="configure"/> at all, and returned — under the comment
        /// <i>"VisualData ECS component will be applied by IG-side translator in Phase 6."</i>
        /// ⛔ <b>Phase 6 never happened</b>, so all nine catalog call sites authored real symbol codes,
        /// colours, models and scales into a delegate that was never invoked.
        ///
        /// <para>📐 <b>Measured consequence.</b> <c>VisualDefinitionDto</c> was produced by NOTHING in
        /// the entire repository, which made <c>PresentationTkbTranslator</c> — its only consumer —
        /// permanently inert on <i>every</i> host. No entity built from the TKB has ever carried
        /// <c>VisualData</c>; the only entities that had it were those whose <c>scenario.json</c>
        /// authored it directly. On the <c>SimHost</c> perspective, which builds from the TKB, that
        /// left the shared entity gizmos with nothing to draw.</para>
        ///
        /// <para>⚠ <b>This is the identical defect as <c>WithPhysics</c>'s dropped
        /// <c>Height</c>/<c>TurnRate</c>/<c>Mobility</c></b> (<c>CE-113</c>), with the same
        /// "Phase 6" comment — 📌 two instances of one pattern: a builder method that takes authored
        /// data, resolves the template, and forgets to attach a descriptor. ⭐ Neither failed loudly,
        /// because an absent descriptor is indistinguishable from an unauthored one.</para>
        /// </remarks>
        public NedTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");

            var visualDef = new IgVisualDef();
            configure(visualDef);

            template.AddDescriptor(new VisualDefinitionDto
            {
                SymbolCode   = visualDef.SymbolCode,
                ModelPath    = visualDef.ModelPath,
                ColorHex     = visualDef.ColorHex,
                Scale        = visualDef.Scale,
                ShowLabel    = visualDef.ShowLabel,
                MapShapeName = visualDef.MapShapeName,
            });
            // IgVisualDef.LayerName is deliberately NOT carried across: layer membership is
            // COMPUTED by MapLayerAssignmentSystem from the entity's DIS type and components,
            // not declared per TKB type. A second, authored source for the same fact would be
            // the two-producer hazard CE-113 was about. If per-type layer overrides are ever
            // wanted, they belong in the S4 layer configuration, not in this descriptor.
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
                TurnRate    = physicsDef.TurnRate,
                VehicleClass = MapMobility(physicsDef.Mobility),
            });
            // Height stays out of VehicleParametersDto on purpose -- nothing on the
            // kinematics path consumes it (VehicleParams has no height field,
            // PhysicsCollider carries only Radius).  Its home is the render/collider
            // descriptor, StrideRenderModelDefDto.ShapeHeight.
            // FuelCapacity / FuelConsumption likewise have no consumer yet.
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

            // Typed DTO consumed by CombatTkbTranslator.
            // Derive max health from armor: ArmorFront * 5 gives roughly 500 HP for a 100 mm armour.
            // Entities without armour default to 100 HP.
            template.AddDescriptor(new CombatPlatformDefDto
            {
                MaxHealth  = combatDef.ArmorFront > 0f ? combatDef.ArmorFront * 5f : 100f,
                ArmorFront = combatDef.ArmorFront,
                ArmorSide  = combatDef.ArmorSide,
                ArmorRear  = combatDef.ArmorRear,
            });

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

                // Typed weapon suite consumed by CombatTkbTranslator.
                var suite = new WeaponSuiteDto();
                foreach (var wm in combatDef.Weapons)
                {
                    suite.Mounts.Add(new WeaponMountDto
                    {
                        InitialAmmunition = wm.Ammunition,
                        MuzzleVelocity    = wm.Range > 0f ? wm.Range * 0.5f : 800f,
                    });
                }
                template.AddDescriptor(suite);
            }

            // Perception DTO from sensor range (if specified).
            if (combatDef.SensorRange > 0f)
            {
                template.AddDescriptor(new SensorCapabilitiesDto
                {
                    VisionRange        = combatDef.SensorRange,
                    HearingRange       = combatDef.SensorRange * 1.5f,
                    FieldOfViewDegrees = 360f,
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

            template.AddDescriptor(new BehaviorProfileDto
            {
                SimTier    = BehaviorConstants.SimTierTactical,
                BrainTier  = BehaviorConstants.BrainTierBTree,
                CanMove    = true,
                CanShoot   = true,
                CanInteract = true
            });

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

        /// <summary>
        /// Maps HROT's authoring-level <see cref="TerrainMobility"/> onto the FDP
        /// kinematics <see cref="VehicleClass"/> that selects a
        /// <see cref="VehiclePresets"/> baseline.
        /// </summary>
        /// <remarks>
        /// This half of the mapping stays here because <see cref="TerrainMobility"/> is an
        /// <c>Hrot.Core</c> concept and <c>Fdp.Toolkits</c> -- which owns the DTO and the
        /// consuming translator -- cannot reference it.  The other half (preset baseline
        /// plus per-field overrides) now lives in <c>VehicleKinematicsTkbTranslator</c>,
        /// the single writer of <c>VehicleParams</c> on the cluster path.
        /// <para>
        /// The mapping is lossy -- <c>Air</c> and <c>Naval</c> both collapse to
        /// <c>PersonalCar</c> -- but that loss predates this routing and is unchanged.
        /// </para>
        /// </remarks>
        internal static VehicleClass MapMobility(TerrainMobility mobility) => mobility switch
        {
            TerrainMobility.Tracked  => VehicleClass.Tank,
            TerrainMobility.Wheeled  => VehicleClass.Truck,
            TerrainMobility.Infantry => VehicleClass.Pedestrian,
            TerrainMobility.Air      => VehicleClass.PersonalCar,
            TerrainMobility.Naval    => VehicleClass.PersonalCar,
            _                        => VehicleClass.PersonalCar
        };
    }
}
