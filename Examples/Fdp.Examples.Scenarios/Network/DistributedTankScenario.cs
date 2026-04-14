using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Systems;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Common.Setup;
using Fdp.Examples.DDS;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Messages;
using FDP.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Time.Controllers;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network;
using Fdp.Network.Cyclone.Topics;

namespace Fdp.Examples.Scenarios.Network
{
    /// <summary>
    /// DEM1-D009 DistributedTank: minimal harness proving that two
    /// <see cref="ModuleHostKernel"/> instances communicating via FastCycloneDDS loopback
    /// on Domain 0 can be initialised, stepped, and torn down without exception.
    ///
    /// <para><b>Phase A scope (BATCH-09):</b>
    /// Two DDS participants on Domain 0 (loopback), two kernels (Brain = main kernel,
    /// Muscle = internal), and a trivial success assertion at tick 10.</para>
    ///
    /// <para><b>Phase B Phase 1 scope (BATCH-10):</b>
    /// <see cref="EntityLifecycleModule"/> added to the Brain kernel (zero-participant
    /// auto-promote).  Brain hull reaches <see cref="EntityLifecycle.Active"/> by tick 5.</para>
    ///
    /// <para><b>Phase B DDS slice (BATCH-11):</b>
    /// Brain publishes <see cref="EntityMasterTopic"/> at tick 6 via <see cref="DdsWriter{T}"/>;
    /// Muscle polls via <see cref="DdsReader{T}"/> at tick 7+.
    /// <see cref="ReplicationLogicModule"/> is registered on the Muscle kernel.
    /// <see cref="GhostCreationSystem.CreateGhost"/> is called on the Muscle world,
    /// registering a live ghost entity in <see cref="NetworkEntityMap"/>.
    /// Test asserts <see cref="GhostVisibleOnMuscle"/> is true by tick 10.</para>
    ///
    /// <para><b>Phase B locomotion + split-authority (BATCH-12 / BATCH-13):</b>
    /// CommandTank TKB template registered on Muscle via <see cref="DemoTkbSetup.RegisterAll"/>;
    /// <see cref="GhostPromotionSystem"/> applies the blueprint
    /// (SimTransform, SimVelocity, VehicleState, VehicleParams, NavState) after
    /// <see cref="TkbIdentity"/> is set on the ghost (tick 8).
    /// At tick 20 Brain sets <c>LocomotionChannel.ActiveAction = MoveTo</c> on its hull and
    /// publishes a <c>DemoLocomotionMsg</c> via DDS Domain 0 loopback (one-tick DDS latency);
    /// Muscle polls the message before the tick 21 kernel update and translates it to
    /// <c>NavState</c> on the ghost.
    /// By tick 25 <c>SimVelocity.Linear.X</c> exceeds 0.1 m/s (one full tick of
    /// <see cref="CarKinematicsSystem"/> integration).  At tick 30
    /// <c>WeaponChannel.ActiveAction</c> is set on the Brain Turret; tick 50 asserts
    /// split-authority and returns true.</para>
    ///
    /// <para><b>Node IDs:</b>
    /// <list type="bullet">
    ///   <item>Brain: Node ID 100</item>
    ///   <item>Muscle: Node ID 200</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Resource ownership:</b>
    /// DDS participants, writers/readers, and the Muscle <see cref="ModuleHostKernel"/> are owned by this class.
    /// Released via <see cref="OnShutdown"/> on the runner path and via <see cref="Dispose"/>
    /// on the test-harness path; a <c>_released</c> guard prevents double-free.</para>
    /// </summary>
    public sealed class DistributedTankScenario : IScenario, IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Tick by which Phase B Phase 1 (ELM auto-promote) must be Active.</summary>
        public const int PhaseBElmActiveTick = 5;

        /// <summary>Tick at which Brain publishes <see cref="EntityMasterTopic"/> via DDS.</summary>
        public const int PhaseB2PublishTick = 6;

        /// <summary>Tick from which Muscle starts polling for the EntityMaster ghost.</summary>
        public const int PhaseB2GhostPollTick = 7;

        /// <summary>Tick at which combined Phase A/B milestones are asserted (non-terminal).</summary>
        public const int PhaseABCheckTick = 10;

        /// <summary>Tick at which Brain hull <c>LocomotionChannel</c> is set and <c>DemoLocomotionMsg</c> is published.</summary>
        public const int PhaseB3LocoInjectTick = 20;

        /// <summary>X coordinate of the locomotion command's final destination (metres east).</summary>
        private const float LocoDestinationX = 200f;

        /// <summary>Commanded speed sent in the locomotion channel (m/s).</summary>
        private const float LocoTargetSpeed = 15f;

        /// <summary>Tick at which Muscle ghost <c>SimVelocity.Linear.X</c> must exceed 0.1 m/s.</summary>
        public const int PhaseB3LocoAssertTick = 25;

        /// <summary>Tick at which WeaponChannel is set on the Brain Turret (split-authority entry).</summary>
        public const int PhaseB4WeaponInjectTick = 30;

        /// <summary>Tick at which Brain Turret position is checked to track Brain Hull position (±0.1 m).</summary>
        public const int PhaseB4TurretTrackTick = 40;

        /// <summary>Tick at which all Phase 4 success conditions are asserted; scenario returns true.</summary>
        public const int SuccessTick = 50;

        /// <summary>TKB blueprint type for TankTurret (child entity, split-authority).</summary>
        public const long TankTurretTkbType = 101L;

        /// <summary>DIS network ID assigned to the Brain turret entity.</summary>
        public const long BrainTurretNetId = 101L;

        /// <summary>Fixed simulation delta shared by both kernels.</summary>
        public const float FixedDelta = 1.0f / 60.0f;

        /// <summary>DIS network ID assigned to the Brain hull entity.</summary>
        public const long BrainHullNetId = 100L;

        /// <summary>TKB blueprint type for CommandTank.</summary>
        public const long CommandTankTkbType = 100L;

        /// <summary>Brain node identifier used in EntityMasterTopic.OwnerId.</summary>
        private static readonly NetworkAppId BrainAppId =
            new NetworkAppId { AppDomainId = 1, AppInstanceId = 100 };

        // ── DDS participants (FastCycloneDDS loopback Domain 0) ───────────────

        // Brain participant (Node 100). Released in ReleaseResources().
        private DdsParticipant? _brainParticipant;
        // Muscle participant (Node 200). Released in ReleaseResources().
        private DdsParticipant? _muscleParticipant;

        // ── Phase B DDS: EntityMaster topic (Brain → Muscle) ─────────────────
        // Writer and reader must be Disposed before the participants.
        private DdsWriter<EntityMasterTopic>? _masterWriter;
        private DdsReader<EntityMasterTopic>? _masterReader;
        private bool _masterPublished;
        private bool _ghostCreated;

        // ── Phase C DDS: DemoLocomotionMsg (Brain → Muscle) ──────────────────
        // Carries LocomotionChannel.ActiveAction from Brain hull to Muscle ghost translation.
        private DdsWriter<DemoLocomotionMsg>? _locoMsgWriter;
        private DdsReader<DemoLocomotionMsg>? _locoMsgReader;
        // True after Brain has written DemoLocomotionMsg at tick 20.
        private bool _locoMsgPublished;
        // True after Muscle has consumed the DemoLocomotionMsg and translated it to NavState.
        private bool _locoMsgConsumed;
        // True once GhostPromotionSystem has applied the CommandTank TKB template (has NavState).
        // Set in EvaluateTick; triggers authority grant and locomotion injection sequence.
        private bool _ghostPromoted;
        // Cached ghost entity handle (resolved from _muscleEntityMap after creation).
        private Entity _ghostEntity;

        // ── Muscle kernel (Brain == main scenario kernel passed to Configure) ──

        private EntityRepository? _muscleWorld;
        private ModuleHostKernel? _muscleKernel;
        private SteppingTimeController? _muscleTimeController;

        // ── Muscle replication ────────────────────────────────────────────────
        // ReplicationLogicModule registered on Muscle kernel drives ghost lifecycle.
        // GhostCreationSystem is exposed for use in EvaluateTick.
        private NetworkEntityMap? _muscleEntityMap;
        private ReplicationLogicModule? _muscleReplicationModule;

        // ── Phase B: ELM on Brain kernel ──────────────────────────────────────

        // Zero-participant ELM — construction orders are auto-promoted to Active
        // by LifecycleSystem.DrainInstantComplete on the first Brain kernel tick.
        private EntityLifecycleModule? _brainElm;
        private Entity _brainHull;
        private bool _elmConstructionOrdered;

        // ── Brain turret (BATCH-12 split-authority) ────────────────────────────
        // Spawned in Configure; WeaponChannel injected at tick 30.
        private Entity _brainTurret;

        // ── Resource lifecycle guard ──────────────────────────────────────────

        // Guards against double-dispose when both OnShutdown() and Dispose() run
        // (e.g. in tests that call ScenarioTestHarness.Run() with a using var).
        private bool _released;

        // ── Observation properties for test assertions ────────────────────────

        /// <summary>True after <see cref="Configure"/> completes without exception.</summary>
        public bool BrainInitialized  { get; private set; }

        /// <summary>True after the Muscle kernel is initialized inside <see cref="Configure"/>.</summary>
        public bool MuscleInitialized { get; private set; }

        /// <summary>
        /// True when the Brain hull entity reaches <see cref="EntityLifecycle.Active"/>
        /// (Phase B Phase 1 — ELM auto-promote with zero participants).
        /// Set at <see cref="PhaseBElmActiveTick"/>.
        /// </summary>
        public bool PhaseBElmActive { get; private set; }

        /// <summary>
        /// True when the Muscle world has a live ghost entity for <see cref="BrainHullNetId"/>,
        /// proving the Cyclone DDS loopback path (EntityMasterTopic Brain→Muscle).
        /// Set at or after <see cref="PhaseB2GhostPollTick"/>.
        /// </summary>
        public bool GhostVisibleOnMuscle { get; private set; }

        /// <summary>
        /// True when the Muscle ghost entity's <c>SimVelocity.Linear.X</c> exceeds 0.1 m/s
        /// at tick <see cref="PhaseB3LocoAssertTick"/> (locomotion command round-trip).
        /// </summary>
        public bool LocoObservable { get; private set; }

        /// <summary>
        /// True when the Muscle node consumed a <see cref="DemoLocomotionMsg"/> DDS sample and
        /// translated it to <see cref="NavState"/> on the ghost entity via the DDS loopback path.
        /// Set in the same poll loop that sets the internal <c>_locoMsgConsumed</c> flag,
        /// providing test-visible evidence that the DDS path (not accidental direct injection)
        /// drove the locomotion command.
        /// </summary>
        public bool LocoCommandReceivedViaDds { get; private set; }

        /// <summary>
        /// True when at tick <see cref="PhaseB4TurretTrackTick"/> (40) the Brain Turret
        /// <see cref="SimTransform"/> position is within ±0.1 m of the Brain hull position
        /// (Phase 3 — turret tracks hull).
        /// </summary>
        public bool PhaseBTurretTracksHull { get; private set; }

        /// <summary>
        /// True at tick <see cref="SuccessTick"/> when the Brain Turret's
        /// <c>WeaponChannel.ActiveAction == CombatConstants.ActionIdAimAndFire</c>
        /// AND the Muscle ghost <c>SimVelocity.Linear.X</c> is still positive.
        /// </summary>
        public bool SplitAuthorityActive { get; private set; }

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => ScenarioNames.DistributedTank;

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Two DDS participants on Domain 0 (FastCycloneDDS loopback) ──────
            // Within a single process both participants share memory; no network
            // traffic escapes the host.  Released in ReleaseResources().
            _brainParticipant  = new DdsParticipant(domainId: 0);
            _muscleParticipant = new DdsParticipant(domainId: 0);

            // ── Phase B: register lifecycle events and ELM on Brain kernel ──────
            // Events must be registered before kernel.Initialize() is called so
            // that EntityCommandBuffer.Playback can publish them at tick 1.
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<ConstructionAck>();
            world.RegisterEvent<DestructionOrder>();
            world.RegisterEvent<DestructionAck>();

            // Zero-participant ELM: LifecycleSystem.DrainInstantComplete immediately
            // promotes any newly constructed entity to Active (no ACK round-trip needed).
            var brainTkb = new TkbDatabase();  // empty — Brain side needs no TKB templates here
            _brainElm = new EntityLifecycleModule(brainTkb, Array.Empty<int>());
            kernel.RegisterModule(_brainElm);

            // ── Brain world component registration ────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<NetworkIdentity>();   // Phase B: hull carries a network ID
            world.RegisterComponent<WeaponChannel>();     // BATCH-12: turret split-authority
            world.RegisterComponent<LocomotionChannel>(); // BATCH-13: Brain hull command channel

            // Spawn the Brain hull entity in Constructing state.
            // BeginConstruction is called at tick 1 in EvaluateTick (after kernel.Initialize).
            _brainHull = world.CreateEntity();
            world.AddComponent(_brainHull, new SimTransform());
            world.AddComponent(_brainHull, new NetworkIdentity(BrainHullNetId));
            world.AddComponent(_brainHull, new LocomotionChannel()); // written at tick 20 via DemoLocomotionMsg
            world.SetLifecycleState(_brainHull, EntityLifecycle.Constructing);

            // Spawn Brain Turret (TKB 101) — split-authority entity on the Brain node.
            // Turret starts Active (ELM not needed for the simple scenario-controlled entity).
            // WeaponChannel is injected at tick 30 in EvaluateTick.
            _brainTurret = world.CreateEntity();
            world.AddComponent(_brainTurret, new SimTransform()); // same initial position as hull
            world.AddComponent(_brainTurret, new WeaponChannel());
            world.SetLifecycleState(_brainTurret, EntityLifecycle.Active);

            BrainInitialized = true;

            // ── CommandTank TKB template for Muscle (BATCH-13: via DemoTkbSetup) ──────
            // Enables GhostPromotionSystem (inside ReplicationLogicModule) to apply a
            // physics-capable blueprint to the ghost entity once TkbIdentity is set.
            // Template includes all components queried by CarKinematicsSystem.
            var muscleTkb = new TkbDatabase();
            DemoTkbSetup.RegisterAll(muscleTkb);

            // ── Muscle kernel (Node 200): separate world + kernel ─────────────
            _muscleWorld = new EntityRepository();
            _muscleWorld.RegisterComponent<SimTransform>();
            _muscleWorld.RegisterComponent<SimVelocity>();     // BATCH-12: locomotion physics
            _muscleWorld.RegisterComponent<VehicleState>();    // BATCH-12: car kinematics state
            _muscleWorld.RegisterComponent<VehicleParams>();   // BATCH-12: vehicle preset parameters
            _muscleWorld.RegisterComponent<NavState>();        // BATCH-12: navigation target + mode
            _muscleWorld.RegisterComponent<LocomotionChannel>(); // BATCH-12: channel on ghost
            _muscleWorld.RegisterComponent<SpatialGridData>(); // BATCH-12: singleton for SpatialHashSystem

            // Register components required by ReplicationLogicModule systems so their
            // ECS queries build without exception even when no entities are present.
            _muscleWorld.RegisterComponent<NetworkIdentity>();
            _muscleWorld.RegisterComponent<GhostStateTracker>();
            _muscleWorld.RegisterComponent<TkbIdentity>();
            _muscleWorld.RegisterComponent<NetworkOwnership>();
            _muscleWorld.RegisterComponent<PartMetadata>();
            _muscleWorld.RegisterEvent<ConstructionOrder>();
            _muscleWorld.RegisterEvent<ConstructionAck>();
            _muscleWorld.RegisterEvent<DestructionOrder>();
            _muscleWorld.RegisterEvent<DestructionAck>();

            // Muscle ELM (zero-participant) + ReplicationLogicModule.
            // muscleTkb is shared so GhostPromotionSystem can find the CommandTank template.
            // muscleReplicationElm must ALSO be registered with the Muscle kernel so that
            // LifecycleSystem.DrainInstantComplete runs each tick (Ghost→Constructing→Active).
            _muscleEntityMap = new NetworkEntityMap();
            var muscleReplicationElm = new EntityLifecycleModule(muscleTkb, Array.Empty<int>());
            _muscleReplicationModule = new ReplicationLogicModule(_muscleEntityMap, muscleTkb, muscleReplicationElm);

            // ── GroundKinematicsModule systems for Muscle kernel ─────────────
            // Wraps SpatialHashSystem + CarKinematicsSystem in a DirectSystemsModule
            // so the Muscle kernel drives vehicle physics each tick.
            // Systems are manually Created with _muscleWorld before kernel.Initialize().
            var muscleSpatialHash = new SpatialHashSystem();
            var muscleKinematics  = new CarKinematicsSystem(new TrajectoryPoolManager())
            {
                ForceSerial = true   // deterministic: no parallel partitioning in CI
            };
            muscleSpatialHash.Create(_muscleWorld);
            muscleKinematics.Create(_muscleWorld);

            var muscleAccumulator = new EventAccumulator();
            _muscleKernel = new ModuleHostKernel(_muscleWorld, muscleAccumulator);
            _muscleTimeController = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f, DeltaTime = FixedDelta });
            _muscleKernel.SetTimeController(_muscleTimeController);
            // Registration order: ELM first (BeforeSync promotes Ghost→Active), then Replication,
            // then GroundKinematics (Simulation group processes physics).
            _muscleKernel.RegisterModule(muscleReplicationElm);
            _muscleKernel.RegisterModule(_muscleReplicationModule);
            _muscleKernel.RegisterModule(new MuscleDirectSystemsModule(muscleSpatialHash, muscleKinematics));
            _muscleKernel.Initialize();
            MuscleInitialized = true;

            // ── DDS topics: Brain → Muscle Cyclone loopback ─────────────────
            // Disposed before participants in ReleaseResources() (CycloneDDS requirement).
            _masterWriter    = new DdsWriter<EntityMasterTopic>(_brainParticipant);
            _masterReader    = new DdsReader<EntityMasterTopic>(_muscleParticipant);
            _locoMsgWriter   = new DdsWriter<DemoLocomotionMsg>(_brainParticipant);
            _locoMsgReader   = new DdsReader<DemoLocomotionMsg>(_muscleParticipant);

            FdpLog<DistributedTankScenario>.Info(
                "[distributedtank] Configure: brain={0} muscle={1} participants=2 elm=registered",
                BrainInitialized, MuscleInitialized);
        }

        /// <inheritdoc/>
        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // ── Poll incoming DemoLocomotionMsg BEFORE Muscle kernel update ─────────
            // Polling here ensures that a command written by Brain at tick N is applied
            // to the Muscle ghost's NavState before the Muscle kernel runs at tick N+1,
            // giving CarKinematicsSystem the full tick to integrate velocity.
            if (!_locoMsgConsumed && _ghostPromoted)
            {
                using var locoLoan = _locoMsgReader!.Take();
                foreach (var sample in locoLoan)
                {
                    if (!sample.IsValid) continue;
                    if (sample.Data.NetworkId != BrainHullNetId) continue;

                    // Translate DemoLocomotionMsg → Muscle ghost NavState.
                    // Destination and speed are scenario-level knowledge; DemoLocomotionMsg
                    // carries the channel action ID to identify the command type.
                    // ArrivalRadius is preserved from the TKB template default (2 m).
                    var navState = _muscleWorld!.GetComponent<NavState>(_ghostEntity);
                    navState.Mode             = KinematicsMode.None;
                    navState.FinalDestination = new Vector2(LocoDestinationX, 0f);
                    navState.TargetSpeed      = LocoTargetSpeed;
                    _muscleWorld.SetComponent(_ghostEntity, navState);
                    _locoMsgConsumed = true;
                    LocoCommandReceivedViaDds = true;

                    FdpLog<DistributedTankScenario>.Info(
                        "[distributedtank] tick={0} DemoLocomotionMsg received (action={1}) → NavState injected (dest=({2},0) speed={3})",
                        currentTick, sample.Data.ActiveAction, LocoDestinationX, LocoTargetSpeed);
                    break;
                }
            }

            // ── Step Muscle kernel in lock-step with the Brain kernel ─────────────
            // (Brain kernel is stepped by ScenarioSubsystem after this method returns.)
            _muscleTimeController!.Step(FixedDelta);
            _muscleKernel!.Update();

            // ── Tick 1: order construction of the Brain hull via ELM ─────────
            // Called in EvaluateTick (before kernel.Update) so the ConstructionOrder
            // event is on the bus when LifecycleSystem runs this frame.
            // DrainInstantComplete (zero participants) promotes to Active immediately.
            if (currentTick == 1 && !_elmConstructionOrdered)
            {
                _elmConstructionOrdered = true;
                var cmd = new EntityCommandBuffer();
                _brainElm!.BeginConstruction(_brainHull, blueprintId: 0, currentTick, cmd);
                cmd.Playback(world);

                FdpLog<DistributedTankScenario>.Trace(
                    "[distributedtank] tick={0} BeginConstruction ordered for brainHull={1}",
                    currentTick, _brainHull.Index);
            }

            // ── Tick 5: Phase B Phase 1 — assert ELM auto-promoted to Active ─
            if (currentTick == PhaseBElmActiveTick)
            {
                var lc = world.GetLifecycleState(_brainHull);
                if (lc != EntityLifecycle.Active)
                    throw new ScenarioFailureException(1,
                        $"[distributedtank] Phase B Phase 1 FAILED tick={currentTick}: " +
                        $"expected brainHull lifecycle=Active, got {lc}");

                PhaseBElmActive = true;
                FdpLog<DistributedTankScenario>.Info(
                    "[distributedtank] Phase 1 PASSED tick={0} brainHull lifecycle=Active (ELM auto-promote)",
                    currentTick);
            }

            // ── Tick 6: Brain publishes EntityMasterTopic via DDS ────────────
            // The Muscle DdsReader on Domain 0 (loopback) picks it up on the next tick.
            if (currentTick == PhaseB2PublishTick && !_masterPublished)
            {
                _masterPublished = true;
                _masterWriter!.Write(new EntityMasterTopic
                {
                    EntityId     = BrainHullNetId,
                    OwnerId      = BrainAppId,
                    TkbTypeValue = CommandTankTkbType
                });
                FdpLog<DistributedTankScenario>.Trace(
                    "[distributedtank] tick={0} EntityMaster published for NetID={1}",
                    currentTick, BrainHullNetId);
            }

            // ── Tick 7+: Muscle polls EntityMasterTopic and creates ghost ────
            // GhostCreationSystem.CreateGhost registers the entity in _muscleEntityMap.
            if (currentTick >= PhaseB2GhostPollTick && !_ghostCreated)
            {
                using var loan = _masterReader!.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid) continue;
                    if (_muscleEntityMap!.TryGetEntity(sample.Data.EntityId, out _)) continue;

                    var ghost = _muscleReplicationModule!.GhostCreationSystem.CreateGhost(
                        _muscleWorld!, sample.Data.EntityId, currentTick);

                    // Set TkbIdentity on the ghost so GhostPromotionSystem (runs on next
                    // Muscle kernel Update) can apply the CommandTank blueprint template.
                    _muscleWorld!.AddComponent(ghost, new TkbIdentity { TkbType = CommandTankTkbType });

                    _ghostCreated = true;
                    break;
                }

                if (_ghostCreated)
                {
                    GhostVisibleOnMuscle = _muscleEntityMap!.TryGetEntity(BrainHullNetId, out _ghostEntity)
                                           && _muscleWorld!.IsAlive(_ghostEntity);
                    if (GhostVisibleOnMuscle)
                        FdpLog<DistributedTankScenario>.Info(
                            "[distributedtank] Phase 2 PASSED tick={0} ghost visible on Muscle (EntityMaster DDS loopback)",
                            currentTick);
                }
            }

            // ── Tick 8+: poll for ghost promotion (template applied by GhostPromotionSystem) ─
            // GhostPromotionSystem (BeforeSync) applies CommandTank blueprint on the tick AFTER
            // TkbIdentity is set. Once NavState is present, set SimTransform authority so
            // CarKinematicsSystem's WithOwned<SimTransform> filter passes for the ghost.
            if (_ghostCreated && !_ghostPromoted)
            {
                if (_muscleEntityMap!.TryGetEntity(BrainHullNetId, out _ghostEntity)
                    && _muscleWorld!.IsAlive(_ghostEntity)
                    && _muscleWorld.HasComponent<NavState>(_ghostEntity))
                {
                    // Muscle owns the ghost's physics transform (split-authority design).
                    _muscleWorld.SetAuthority<SimTransform>(_ghostEntity, true);
                    _ghostPromoted = true;

                    FdpLog<DistributedTankScenario>.Info(
                        "[distributedtank] tick={0} ghost promoted: CommandTank template applied; SimTransform authority set",
                        currentTick);
                }
            }

            // ── Tick 10: Phase A/B combined checkpoint (non-terminal) ────────
            // All early milestones must pass here; scenario continues to tick 50.
            if (currentTick == PhaseABCheckTick)
            {
                if (!BrainInitialized || !MuscleInitialized)
                    throw new ScenarioFailureException(2,
                        $"[distributedtank] Phase A FAILED tick={currentTick}: " +
                        $"brain={BrainInitialized} muscle={MuscleInitialized}");

                if (!PhaseBElmActive)
                    throw new ScenarioFailureException(1,
                        $"[distributedtank] Phase B Phase 1 not completed by tick={currentTick}");

                if (!GhostVisibleOnMuscle)
                    throw new ScenarioFailureException(3,
                        $"[distributedtank] Phase B Ghost not visible on Muscle by tick={currentTick}. " +
                        $"masterPublished={_masterPublished} ghostCreated={_ghostCreated}");

                FdpLog<DistributedTankScenario>.Info(
                    "[distributedtank] checkpoint tick={0} brain=initialized muscle=initialized elm=active ghost=visible",
                    currentTick);
            }

            // ── Tick 20: set LocomotionChannel on Brain hull → publish DemoLocomotionMsg ──
            // Brain sets ActiveAction = MoveTo on its hull's LocomotionChannel, then
            // publishes DemoLocomotionMsg on Domain 0 (loopback).  The Muscle polls this
            // message at the START of tick 21 (before the Muscle kernel update), translating
            // it to NavState so CarKinematicsSystem sees the command on tick 21's run.
            if (currentTick == PhaseB3LocoInjectTick && _ghostPromoted && !_locoMsgPublished)
            {
                _locoMsgPublished = true;

                var loco = new LocomotionChannel { ActiveAction = NavigationConstants.ActionIdMoveTo };
                world.SetComponent(_brainHull, loco);

                _locoMsgWriter!.Write(new DemoLocomotionMsg
                {
                    NetworkId    = BrainHullNetId,
                    ActiveAction = NavigationConstants.ActionIdMoveTo,
                });

                FdpLog<DistributedTankScenario>.Trace(
                    "[distributedtank] tick={0} LocomotionChannel set on Brain hull (action={1}); DemoLocomotionMsg published",
                    currentTick, NavigationConstants.ActionIdMoveTo);
            }

            if (currentTick == PhaseB3LocoAssertTick)
            {
                if (!_ghostPromoted)
                    throw new ScenarioFailureException(4,
                        $"[distributedtank] Phase B3 FAILED tick={currentTick}: ghost not promoted in time");

                var vel = _muscleWorld!.GetComponent<SimVelocity>(_ghostEntity);
                if (vel.Linear.X <= 0.1f)
                    throw new ScenarioFailureException(4,
                        $"[distributedtank] Phase B3 FAILED tick={currentTick}: " +
                        $"ghost SimVelocity.X={vel.Linear.X:F3} expected > 0.1 m/s");

                LocoObservable = true;
                FdpLog<DistributedTankScenario>.Info(
                    "[distributedtank] Phase B3 PASSED tick={0} ghost SimVelocity.X={1:F3} m/s (locomotion observable)",
                    currentTick, vel.Linear.X);
            }

            // ── Tick 30: inject WeaponChannel on Brain Turret (split-authority entry) ─
            // Sets ActiveAction = ActionIdAimAndFire on the turret entity in the Brain world.
            // Nothing clears this in the Brain kernel (no ActionDispatchModule in this harness),
            // so the value persists to tick 50 for the split-authority assertion.
            if (currentTick == PhaseB4WeaponInjectTick)
            {
                var weaponChannel = new WeaponChannel { ActiveAction = CombatConstants.ActionIdAimAndFire };
                world.SetComponent(_brainTurret, weaponChannel);

                FdpLog<DistributedTankScenario>.Trace(
                    "[distributedtank] tick={0} WeaponChannel injected on Brain Turret: ActionIdAimAndFire={1}",
                    currentTick, CombatConstants.ActionIdAimAndFire);
            }

            // ── Tick 40: Phase B4 Part 1 — Brain Turret position tracks Brain Hull ──
            // Both were spawned at the origin (0,0,0). Brain side has no CarKinem, so
            // neither has moved. |turretPos - hullPos| ≈ 0 < 0.1 m.
            if (currentTick == PhaseB4TurretTrackTick)
            {
                var hullTf   = world.GetComponent<SimTransform>(_brainHull);
                var turretTf = world.GetComponent<SimTransform>(_brainTurret);
                float dist = Vector3.Distance(hullTf.Position, turretTf.Position);
                if (dist > 0.1f)
                    throw new ScenarioFailureException(5,
                        $"[distributedtank] Phase B4 Turret-Track FAILED tick={currentTick}: " +
                        $"dist={dist:F3} m expected \u2264 0.1m");

                PhaseBTurretTracksHull = true;
                FdpLog<DistributedTankScenario>.Info(
                    "[distributedtank] Phase B4 Part 1 PASSED tick={0} turret-hull dist={1:F3}m (split-authority layout)",
                    currentTick, dist);
            }

            // ── Tick 50: Phase B4 — split-authority success assertion ────────
            // Turret weapon channel still active + hull ghost still moving.
            if (currentTick == SuccessTick)
            {
                if (!BrainInitialized || !MuscleInitialized)
                    throw new ScenarioFailureException(2,
                        $"[distributedtank] Phase A FAILED tick={currentTick}: " +
                        $"brain={BrainInitialized} muscle={MuscleInitialized}");

                if (!LocoObservable)
                    throw new ScenarioFailureException(4,
                        $"[distributedtank] Phase B3 locomotion observable not set by tick={currentTick}");

                // Turret weapon must still have ActionIdAimAndFire (injected at tick 30).
                var turretWeapon = world.GetComponent<WeaponChannel>(_brainTurret);
                if (turretWeapon.ActiveAction != CombatConstants.ActionIdAimAndFire)
                    throw new ScenarioFailureException(5,
                        $"[distributedtank] Phase B4 FAILED tick={currentTick}: " +
                        $"turret WeaponChannel.ActiveAction={turretWeapon.ActiveAction} expected={CombatConstants.ActionIdAimAndFire}");

                // Ghost hull on Muscle must still be moving.
                var ghostVel = _muscleWorld!.GetComponent<SimVelocity>(_ghostEntity);
                if (ghostVel.Linear.X <= 0f)
                    throw new ScenarioFailureException(5,
                        $"[distributedtank] Phase B4 FAILED tick={currentTick}: " +
                        $"ghost SimVelocity.X={ghostVel.Linear.X:F3} expected > 0");

                SplitAuthorityActive = true;
                FdpLog<DistributedTankScenario>.Info(
                    "[distributedtank] Phase B4 SUCCESS tick={0} turretWeapon=AimAndFire ghostVelocity.X={1:F3} split-authority=ACTIVE",
                    currentTick, ghostVel.Linear.X);

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(FDP.Toolkit.Vis2D.MapCanvas? canvas, EntityRepository world) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Called by <see cref="ScenarioSubsystem"/> during teardown, covering the
        /// <c>fdp-demo-runner --scenario distributedtank</c> CLI path where the scenario
        /// is never explicitly disposed.  Delegates to <see cref="ReleaseResources"/> with
        /// a double-dispose guard so it is safe even when a test also calls
        /// <see cref="Dispose"/> via a <c>using var</c> block.
        /// </remarks>
        public void OnShutdown() => ReleaseResources();

        /// <summary>
        /// Disposes the Muscle <see cref="ModuleHostKernel"/>, its world, and both DDS
        /// participants, releasing all native DDS and ECS resources.
        /// Safe to call after <see cref="OnShutdown"/>; guarded against double-release.
        /// </summary>
        public void Dispose() => ReleaseResources();

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Releases the Muscle kernel, Muscle world, and both DDS participants.
        /// Idempotent — safe to call from both <see cref="OnShutdown"/> and
        /// <see cref="Dispose"/> without double-freeing native handles.
        /// </summary>
        private void ReleaseResources()
        {
            if (_released) return;
            _released = true;

            // Dispose DDS writers/readers before participants (CycloneDDS requirement).
            _masterWriter?.Dispose();
            _masterReader?.Dispose();
            _locoMsgWriter?.Dispose();
            _locoMsgReader?.Dispose();

            _muscleKernel?.Dispose();
            _muscleWorld?.Dispose();
            _brainParticipant?.Dispose();
            _muscleParticipant?.Dispose();
        }

        // ── Inner helper ──────────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IEcsModule"/> that runs a fixed list of
        /// <see cref="ComponentSystem"/> instances on the main thread each tick.
        /// Used to host <see cref="SpatialHashSystem"/> and <see cref="CarKinematicsSystem"/>
        /// on the Muscle <see cref="ModuleHostKernel"/> without depending on
        /// <c>SimulationLogicModule</c>.
        /// </summary>
        private sealed class MuscleDirectSystemsModule : IEcsModule
        {
            private readonly ComponentSystem[] _systems;

            public string Name => "MuscleGroundKinem";
            public ExecutionPolicy Policy     => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public MuscleDirectSystemsModule(params ComponentSystem[] systems)
            {
                _systems = systems;
            }

            public void RegisterSystems(ISystemRegistry registry) { }

            public void Tick(ISimulationView view, float deltaTime)
            {
                foreach (var sys in _systems)
                    sys.Run();
            }

            public IReadOnlyList<Type>? GetRequiredComponents() => null;
        }
    }
}
