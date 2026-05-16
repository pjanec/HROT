using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Tkb;
using Fdp.Interfaces;
using Fdp.Core;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial;

namespace Fdp.Examples.UrbanCombat
{
    /// <summary>
    /// Spawns the full Urban Ambush scenario cast and sets up initial entity state.
    ///
    /// <para>
    /// <b>Spawn manifest (DESIGN.md §9.1):</b>
    /// <list type="table">
    ///   <item><term>5 × CivilianPedestrian (TKB 1001)</term><description>Scattered ±30–50 m from intersection centre. WanderCivil behavior.</description></item>
    ///   <item><term>3 × CivilianCar (TKB 1002)</term><description>On N, S, and E road arms. WanderCivil behavior.</description></item>
    ///   <item><term>1 × MilitaryAPC (TKB 2001)</term><description>South arm at (0,−80,0), heading north. ConvoyEscort behavior.</description></item>
    ///   <item><term>4 × InfantrySoldier (TKB 2002)</term><description>Co-located with APC. InfantryCombat behavior. Pre-embarked in APC.</description></item>
    ///   <item><term>1 × Insurgent (TKB 2003)</term><description>Building corner at (60,20,0). Ambush behavior. TargetMemory seeded with APC.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// One civilian pedestrian also has its <see cref="TargetMemory"/> pre-seeded with the
    /// insurgent entity so that <c>TrafficBrainSystem</c> triggers the <c>FLEE</c> locomotion
    /// action from the first frame (satisfying the T9 milestone without requiring
    /// <c>AudioPerceptionSystem</c> to propagate a gunshot stimulus).
    /// </para>
    /// </summary>
    public class ScenarioDirector
    {
        // ── Civilian pedestrian scatter positions (±30–50 m from centre) ─────────
        private static readonly Vector3[] CivilianPositions =
        {
            new Vector3(-35f,  40f, 0f),
            new Vector3( 30f, -45f, 0f),
            new Vector3(-20f,  50f, 0f),
            new Vector3( 45f,  30f, 0f),
            new Vector3(  0f, -35f, 0f),
        };

        // ── Civilian car positions — on road arms N / S / E ───────────────────────
        private static readonly Vector3[] CarPositions =
        {
            new Vector3(  0f,  60f, 0f),   // N arm
            new Vector3(  0f, -60f, 0f),   // S arm
            new Vector3( 60f,   0f, 0f),   // E arm
        };

        // ── Constructor ──────────────────────────────────────────────────────────

        private readonly EntityRepository  _world;
        private readonly ITkbDatabase      _tkb;
        private readonly RoadNetworkBlob   _road;
        private readonly BehaviorRegistry  _registry;
        private readonly NetworkEntityMap? _entityMap;
        private readonly IReadOnlyList<ITkbEntityTranslator> _translators;

        // Auto-incrementing network ID counter used when _entityMap is provided.
        private long _nextNetId = 1;

        // Cached APC HSM structure hash so BrainHsm128 can be pre-initialised.
        private readonly uint _apcHsmStructureHash;

        /// <summary>
        /// Initialises the director with the ECS world, TKB database and road network.
        /// No entities are created yet; call <see cref="SetupAmbushScenario"/> to spawn them.
        /// </summary>
        /// <param name="entityMap">
        /// Optional <see cref="NetworkEntityMap"/> to register spawned entities in.
        /// When provided every spawned entity is registered with a sequential network ID
        /// so the combat CQRS chain (<see cref="Fdp.Toolkit.Combat.Events.WeaponFireIntent"/>
        /// → translator → Muscle) can resolve shooter/target IDs in headless mode.
        /// </param>
        public ScenarioDirector(
            EntityRepository world,
            ITkbDatabase tkb,
            RoadNetworkBlob road,
            BehaviorRegistry registry,
            NetworkEntityMap? entityMap = null,
            IReadOnlyList<ITkbEntityTranslator>? translators = null)
        {
            _world     = world    ?? throw new ArgumentNullException(nameof(world));
            _tkb       = tkb      ?? throw new ArgumentNullException(nameof(tkb));
            _road      = road;
            _registry  = registry ?? throw new ArgumentNullException(nameof(registry));
            _entityMap = entityMap;
            _translators = translators ?? new List<ITkbEntityTranslator>
            {
                new SpatialCoreTkbTranslator(),
                new VehicleKinematicsTkbTranslator(),
                new BehaviorTkbTranslator(),
                new CombatTkbTranslator(),
                new PerceptionTkbTranslator(),
            }.AsReadOnly();

            // Pre-build the APC HSM blob to extract the StructureHash for brain initialization.
            // The blob is also registered independently in HeadlessDemoApp.RegisterBehaviors();
            // both calls produce the same deterministic hash.
            _apcHsmStructureHash = Brains.ApcHsmSetup.Build().Header.StructureHash;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns the full Urban Ambush cast (14 entities) at their initial positions
        /// and sets up embark state for the four soldiers riding in the APC.
        ///
        /// <para>Must be called <em>after</em> <c>HeadlessDemoApp.Initialize()</c> so that
        /// all component types are registered in the world.</para>
        /// </summary>
        public unsafe void SetupAmbushScenario()
        {
            // ── 1. CivilianPedestrians ────────────────────────────────────────────
            var pedestrians = new Entity[5];
            for (int i = 0; i < 5; i++)
            {
                pedestrians[i] = SpawnEntity(
                    tkbTypeId:  1001,
                    position:   CivilianPositions[i],
                    yawRadians: 0f,
                    behaviorId: BehaviorIds.WanderCivil);
            }

            // ── 2. CivilianCars ───────────────────────────────────────────────────
            for (int i = 0; i < 3; i++)
            {
                SpawnEntity(
                    tkbTypeId:  1002,
                    position:   CarPositions[i],
                    yawRadians: 0f,
                    behaviorId: BehaviorIds.WanderCivil);
            }

            // ── 3. MilitaryAPC (heading north, π/2 yaw = north in ENU XY) ────────
            var apc = SpawnEntity(
                tkbTypeId:  2001,
                position:   new Vector3(0f, -80f, 0f),
                yawRadians: MathF.PI / 2f,   // north
                behaviorId: BehaviorIds.ConvoyEscort);

            // ── 4. InfantrySoldiers — spawn then embark in APC ────────────────────
            var soldiers = new Entity[4];
            for (int i = 0; i < 4; i++)
            {
                soldiers[i] = SpawnEntity(
                    tkbTypeId:  2002,
                    position:   new Vector3(0f, -80f, 0f),
                    yawRadians: 0f,
                    behaviorId: BehaviorIds.InfantryCombat);
            }

            EmbarkSoldiers(apc, soldiers);

            // ── 5. Insurgent ──────────────────────────────────────────────────────
            var insurgent = SpawnEntity(
                tkbTypeId:  2003,
                position:   new Vector3(60f, 20f, 0f),
                yawRadians: 0f,
                behaviorId: BehaviorIds.Ambush);

            // ── 6. Seed TargetMemory — insurgent targets the APC ─────────────────
            // Pre-populates the insurgent's threat table so that Condition_HasTarget
            // succeeds from the first BTree tick, without requiring VisionBroadphaseSystem.
            if (_world.HasComponent<TargetMemory>(insurgent))
            {
                ref var insurgentMem = ref _world.GetComponentRW<TargetMemory>(insurgent);
                var apcPos = _world.GetComponent<SimTransform>(apc).Position;
                TargetMemory.AddOrUpdateTarget(
                    ref insurgentMem,
                    entityId:   (long)apc.PackedValue,
                    posX:       apcPos.X,
                    posY:       apcPos.Y,
                    scoreBoost: 100f,
                    tick:       0u);
            }

            // ── 7. Seed TargetMemory — one civilian perceives the insurgent ───────
            // This guarantees TrafficBrainSystem writes LocomotionChannel.ActiveAction =
            // ActionIdFlee from frame 1 onward (FLEE milestone for T9).
            if (pedestrians.Length > 0 && _world.HasComponent<TargetMemory>(pedestrians[0]))
            {
                ref var civMem = ref _world.GetComponentRW<TargetMemory>(pedestrians[0]);
                var insurgentPos = _world.GetComponent<SimTransform>(insurgent).Position;
                TargetMemory.AddOrUpdateTarget(
                    ref civMem,
                    entityId:   (long)insurgent.PackedValue,
                    posX:       insurgentPos.X,
                    posY:       insurgentPos.Y,
                    scoreBoost: 100f,
                    tick:       0u);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Spawns a single entity from the TKB, sets its spawn position and rotation,
        /// and assigns the initial behavior.
        /// </summary>
        private unsafe Entity SpawnEntity(int tkbTypeId, Vector3 position, float yawRadians, int behaviorId)
        {
            var template = _tkb.GetByType(tkbTypeId)
                ?? throw new InvalidOperationException($"TKB template not found for type {tkbTypeId}.");

            var entity = _world.CreateEntity();

            foreach (var translator in _translators)
                translator.Inject(_world, entity, template);

            // Set spawn position.
            ref var tf = ref _world.GetComponentRW<SimTransform>(entity);
            tf.Position = position;
            tf.Rotation = SimMath.FromYaw(yawRadians);

            // Assign initial behavior.
            ref var behavior = ref _world.GetComponentRW<BehaviorState>(entity);
            behavior.ActiveBehaviorHash = behaviorId;
            unchecked { behavior.InstanceId++; }   // trigger ChannelArbitrationSystem preemption

            // Set BrainTier from registry so BTreeTickSystem / HsmTickSystem processes this entity.
            if (_registry.TryGetDefinition(behaviorId, out var def))
                behavior.BrainTier = def.BrainTier;

            // Pre-initialise the APC HSM brain so HsmKernel.Update processes it correctly.
            // Without this, BrainHsm128.Header.MachineId = 0 and ValidateInstance rejects it.
            if (tkbTypeId == 2001 && _world.HasComponent<BrainHsm128>(entity))
            {
                ref var brain = ref _world.GetComponentRW<BrainHsm128>(entity);
                brain.State.Header.MachineId     = _apcHsmStructureHash;
                brain.State.Header.Phase         = InstancePhase.RTC;         // already running
                brain.State.ActiveLeafIds[0]     = Brains.ApcHsmSetup.CruisingStateIndex;
            }

            // Register entity in NetworkEntityMap with a sequential network ID so the
            // combat CQRS chain (WeaponFireIntent → DDS → FireProcessingSystem) can
            // resolve shooter/target IDs in headless mode (TD-1).
            _entityMap?.Register(_nextNetId++, entity);

            return entity;
        }

        /// <summary>
        /// Pre-fills the APC's <see cref="PassengerBuffer"/> with the given soldiers,
        /// strips their <c>CanMove | CanShoot</c> capabilities, and adds
        /// <see cref="IsEmbarkedTag"/> to each soldier.
        /// </summary>
        private void EmbarkSoldiers(Entity apc, Entity[] soldiers)
        {
            ref var buffer = ref _world.GetComponentRW<PassengerBuffer>(apc);

            foreach (var soldier in soldiers)
            {
                buffer.Passengers[buffer.Count] = soldier;
                buffer.Count++;

                ref var caps = ref _world.GetComponentRW<ActorCapabilityState>(soldier);
                caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);

                _world.AddComponent(soldier, new IsEmbarkedTag { VehicleEntity = apc });
            }
        }
    }
}
