#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Spatial;
using CarKinem.Tkb;
using Hrot.CGF.Systems;
using Fdp.Toolkit.Combat.Modules;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Services;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Time.Controllers;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Hrot.Editor;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Systems.Routing;
using Hrot.Stride.Animation;
using Hrot.Stride.Core;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace HrotStrideApp;

/// <summary>
/// <b>editor_stride</b> composition skeleton (STR-P0-T6, Mode 1 of the Stride integration).
///
/// <para>
/// Mirrors the simulation+orchestration core of <c>EditorSubsystem</c> (see
/// <c>Hrot.Editor/EditorSubsystem.cs</c> lines 449–1092) without the Raylib/WinForms/ImGui
/// editor panels, AI hot-reload, breakpoints, or replay-UI scaffolding.  Those belong to
/// P5 (STR-P5-T2) and are deliberately omitted here.
/// </para>
///
/// <para>
/// Composition for P0:
/// <list type="bullet">
///   <item>One shared <see cref="EntityRepository"/> + simulation <see cref="FdpEventBus"/> +
///     <see cref="ModuleHostKernel"/> (the single ECS world).</item>
///   <item><see cref="OfflineNetworkFactory"/> — no-op DDS stubs, no network traffic.</item>
///   <item><b>Brain (CGF)</b>: <see cref="CgfLogicPack"/> registered directly with the kernel.</item>
///   <item><b>Muscle (P0 stub)</b>: <see cref="SimHostCoreLogicPack"/> registered directly.
///     ⚠ SEAM (P1): Replace <c>SimHostCoreLogicPack</c> with
///     <c>StrideKinematicsModule</c> (STR-P1-T1) when Phase 1 is implemented.</item>
///   <item>Spawn pipeline: <see cref="EntityLifecycleModule"/> + <see cref="NetworkSpawningSystem"/>
///     + <see cref="CreateEntityRequestSystem"/> feeding a
///     <see cref="ScenarioEntityCreationRequestSource"/>; <c>localNodeId = 0</c> so all
///     spawned entities are instantly <c>WithOwned</c>.</item>
///   <item>Separate <see cref="FdpEventBus"/> <c>_orchestrationBus</c> — never the same
///     instance as <c>world.Bus</c>; required invariant from design §8.1.</item>
///   <item>In-process <see cref="ClusterSlave"/> (nodeId 0, name "Editor") wrapped in
///     <see cref="OrchestrationLogicPack"/> and registered with the kernel.</item>
///   <item><see cref="ClusterMaster"/> with empty <c>Mandatory</c> list — releases its
///     bootstrap latch immediately, publishing <c>ClusterState.Idle</c> (Standby).</item>
///   <item>Per-frame pump: <see cref="Tick"/> calls
///     <c>_orchestrationBus.SwapBuffers(); _clusterMaster.Tick();</c>
///     then advances the kernel, mirroring EditorSubsystem lines 1373–1374.</item>
/// </list>
/// </para>
///
/// <para>
/// Items deliberately omitted from EditorSubsystem (all are P5 or later):
/// <list type="bullet">
///   <item>Raylib/WinForms/ImGui panels (P5 STR-P5-T2).</item>
///   <item>AI hot-reload coordinator / breakpoints / blueprint debug session.</item>
///   <item>MapCullingModule / StyleResolutionModule / EventEffectModule (IG gizmo/culling stacks).</item>
///   <item>MapLayerAssignmentSystem and all map-display components.</item>
///   <item>Replay process managers, seek aggregators, storage gateway (P5 replay).</item>
///   <item>Scenario file service / HrotScenarioLoader (scenario authoring, P5).</item>
///   <item>EditorApplication / IEditorLogic facade (P5).</item>
/// </list>
/// </para>
/// </summary>
public sealed class EditorStrideSubsystem : IDisposable
{
    // ── Constants mirroring EditorSubsystem ───────────────────────────────
    private const int EditorNodeId = 0;

    // ── Network factory (offline — no DDS) ───────────────────────────────
    private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();

    // ── Core world / kernel ───────────────────────────────────────────────

    /// <summary>The single shared ECS world (simulation layer).</summary>
    public EntityRepository World { get; private set; } = null!;

    /// <summary>
    /// Simulation event bus — <c>World.Bus</c>.
    /// <b>Must NOT be the same instance as <see cref="OrchestrationBus"/>.</b>
    /// </summary>
    public FdpEventBus WorldBus => World.Bus;

    /// <summary>The module-host kernel that drives all ECS systems.</summary>
    public ModuleHostKernel Kernel { get; private set; } = null!;

    /// <summary>Time controller (deterministic/paused mode for authoring).</summary>
    public MasterSyncController TimeController { get; private set; } = null!;

    // ── Orchestration layer ───────────────────────────────────────────────

    /// <summary>
    /// Control-plane event bus — a <b>distinct</b> <see cref="FdpEventBus"/> instance
    /// from <see cref="WorldBus"/>.  Required invariant: design §8.1.
    /// </summary>
    public FdpEventBus OrchestrationBus { get; private set; } = null!;

    /// <summary>The in-process cluster master (empty Mandatory list → latch released immediately).</summary>
    public ClusterMaster ClusterMaster { get; private set; } = null!;

    // ── Spawn pipeline (exposed for test inspection) ──────────────────────

    /// <summary>
    /// Entity-creation request source fed by <see cref="CreateEntityRequestSystem"/>.
    /// Enqueue an <see cref="EntityCreationRequest"/> here to spawn via the Brain path.
    /// </summary>
    public ScenarioEntityCreationRequestSource ScenarioSource { get; private set; } = null!;

    /// <summary>Network entity map (local-id ↔ network-id).</summary>
    public NetworkEntityMap EntityMap { get; private set; } = null!;

    // ── Kinematics module (P1, STR-P1-T1) ────────────────────────────────

    /// <summary>
    /// The <see cref="StrideKinematicsModule"/> wired in by P1 (STR-P1-T1), replacing the
    /// <c>GroundKinematicsModule</c> role of <c>SimHostCoreLogicPack</c>.
    /// Exposed so tests can assert on system membership (e.g. integrators absent).
    /// </summary>
    public StrideKinematicsModule? KinematicsModule { get; private set; }

    /// <summary>
    /// The deferred <see cref="DotRecastDtCrowdProvider"/> wired as the Infantry crowd
    /// steering backend (BATCH-19, STR-D19).
    ///
    /// <para>
    /// Constructed at <see cref="Initialize"/> time in no-op mode; initialized with the
    /// real Infantry <c>DtNavMesh</c> by <c>StrideHrotGame.BakeNavmesh</c> after
    /// <c>BeginRun</c> (when scene geometry is available).  Until that call this provider
    /// silently no-ops — same observable behaviour as the old <c>FakeDtCrowdProvider</c>.
    /// Once <c>TryInitializeNavMesh</c> is called all subsequent <c>RegisterAgent</c> /
    /// <c>Update</c> calls use the real DotRecast <c>DtCrowd</c>.
    /// </para>
    /// </summary>
    public DotRecastDtCrowdProvider? InfantryCrowdProvider { get; private set; }

    /// <summary>
    /// The <see cref="VehicleNavigationIntentSystem"/> wired into the Simulation phase (BATCH-20,
    /// STR-D19): production navmesh navigation for VEHICLE entities driven by the FDP
    /// <see cref="Fdp.Toolkit.Navigation.NavigationIntent"/> front door.  It reads the
    /// <c>INavmeshProvider</c> singleton each tick and steers vehicles with
    /// <see cref="VehicleNavigationIntentSystem"/>'s internal <see cref="VehicleWaypointController"/>.
    /// Exposed for the F7 "FDP Move Order (vehicle)" harness case and for tests.
    /// </summary>
    public VehicleNavigationIntentSystem? VehicleNavIntentSystem => _vehicleNavIntentSystem;

    // ── Visual binding system (T7/T8) ─────────────────────────────────────

    /// <summary>
    /// The Stride visual binding system that reconciles FDP entities to Stride visuals.
    /// Exposed for test inspection and for the host-loop sync step.
    /// </summary>
    public StrideVisualBindingSystem? VisualBindingSystem { get; private set; }

    /// <summary>
    /// The TKB database used by this subsystem.
    /// Exposed so tests can inspect which templates were registered.
    /// </summary>
    public TkbDatabase TkbDb { get; private set; } = null!;

    // ── Physics body service + lifecycle (P1, STR-P1-T2) ─────────────────

    /// <summary>
    /// The physics body service wired into <c>editor_stride</c>.
    ///
    /// <para>
    /// In P1 this is a <see cref="NoOpPhysicsBodyService"/> — a no-op stub that accepts all
    /// body lifecycle calls without creating real Bullet bodies. The concrete
    /// <c>BulletPhysicsBodyService</c> will replace it at GPU bring-up when a running
    /// <c>Stride.Physics.Simulation</c> is available (STR-D11).
    /// </para>
    /// </summary>
    public IPhysicsBodyService PhysicsBodyService { get; private set; } = null!;

    /// <summary>
    /// The physics body lifecycle system (creates/destroys bodies on authority change).
    /// Exposed for test inspection.
    /// </summary>
    public PhysicsBodyLifecycleSystem? PhysicsBodyLifecycle { get; private set; }

    // ── Reverse-sync (P1, STR-P1-T5) ─────────────────────────────────────

    /// <summary>
    /// The togglable group wrapping <see cref="BulletReverseSyncSystem"/>.
    ///
    /// <para>
    /// Exposed so tests can assert the group is enabled/disabled, and so that
    /// P5's <c>ReferenceReplayLoadHandler</c> can flip <c>Enabled = false</c>
    /// during replay (design §9, STR-D5 resolution).
    /// </para>
    ///
    /// <para>
    /// The group is driven <b>manually</b> in <see cref="Tick"/> — it is NOT registered
    /// as a kernel system. This ensures it executes <b>before</b>
    /// <see cref="ModuleHostKernel.Update()"/> so FDP Simulation-phase consumers
    /// (SpatialHashSystem, vision broadphase, EQS) read the post-physics
    /// <see cref="SimTransform"/> the same frame (no one-frame lag). See design §8.3.
    /// </para>
    /// </summary>
    public Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup? ReverseSyncGroup { get; private set; }

    // ── Record / replay (P5, STR-P5-T4, STR-D5) ──────────────────────────

    /// <summary>
    /// The record/replay lifecycle controller for <c>editor_stride</c> (STR-P5-T4, design §9).
    ///
    /// <para>
    /// A <see cref="EcsRecordReplayController"/> — the same factory <c>EditorSubsystem</c> /
    /// <c>SimHostSubsystem</c> use. It installs a <c>RecordingModule</c> (captures this node's
    /// authoritative <c>SimTransform</c> every tick) or a <c>ReplayModule</c> (registers a
    /// <c>PlaybackTickSystem</c> that drives <c>SimTransform</c> from recorded keyframes) into
    /// the <see cref="Kernel"/> on demand. Exposed for harness cases and tests.
    /// </para>
    /// </summary>
    public EcsRecordReplayController RecordReplayController { get; private set; } = null!;

    /// <summary>
    /// The replay-load Cluster handler (STR-P5-T4, design §9 / STR-D5 resolution).
    ///
    /// <para>
    /// On <c>PrepareReplay</c> its <c>Commit</c> flips the <see cref="ReverseSyncGroup"/>'s
    /// <c>Enabled = false</c> so Bullet reverse-sync cannot overwrite historical
    /// <c>SimTransform</c> positions while <c>PlaybackTickSystem</c> drives them from the
    /// recording. On <c>FinalizeReplay</c> / <c>PrepareLive</c> it flips <c>Enabled = true</c>,
    /// returning authority to Bullet. The reverse-sync group (BATCH-06) is the
    /// <c>postSimGroup</c> passed to it. Exposed for harness cases and tests.
    /// </para>
    /// </summary>
    public ReferenceReplayLoadHandler ReplayLoadHandler { get; private set; } = null!;

    /// <summary>
    /// Root directory under which exercise recordings are staged. Defaults to a
    /// <c>recordings</c> folder under the process base directory; overridable before
    /// <see cref="Initialize"/> for tests.
    /// </summary>
    public string RecordReplayStorageDirectory { get; set; } =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "recordings");

    // ── Shared editor selection (P5, STR-P5-T3, BATCH-23) ────────────────

    /// <summary>
    /// Shared selection state for the dual-window pair (inspector ↔ Stride 3D view).
    ///
    /// <para>
    /// The <see cref="StrideInspectorWindow"/> writes to this (clicking a row calls
    /// <see cref="EditorSelectionState.Select"/>); <see cref="StrideHrotGame"/> reads it
    /// each frame to emit the selection highlight gizmo and execute
    /// <c>CenterOnEntityCommand</c>. Both run on the same host thread (BATCH-22 Option A)
    /// so no locking is required.
    /// </para>
    /// </summary>
    public EditorSelectionState SelectionState { get; } = new EditorSelectionState();

    // ── 3D gizmos (P5, STR-P5-T1, design §11) ────────────────────────────

    /// <summary>
    /// The local gizmo <b>ProducerBuffer</b> for <c>editor_stride</c> (design §11).
    ///
    /// <para>
    /// Local ECS gizmo producers (data-driven / stateless gizmo systems) write
    /// <c>DebugPrimitive</c>s here; the <see cref="GizmoRenderer3D"/> sweeps it each frame.
    /// In Mode 1 there is no DDS, so this is the single buffer the 3D renderer reads (the
    /// raylib 2-D renderer would sweep the same buffer). Exposed so harness cases can write a
    /// known primitive and tests can assert. The buffer is created in <see cref="Initialize"/>.
    /// </para>
    /// </summary>
    public Fdp.Toolkit.Diagnostics.Gizmos.GizmoPrimitiveBuffer ProducerBuffer { get; private set; } = null!;

    /// <summary>
    /// The Stride 3-D gizmo renderer (STR-P5-T1). Sweeps the <see cref="ProducerBuffer"/> with a
    /// two-pass anchor-resolve + <c>FdpStrideTransform</c> swizzle and emits each resolved shape
    /// to its <see cref="IDebugDrawSink3D"/>. In headless mode the sink is a no-op/logging sink;
    /// the live GPU sink (compositor DebugRenderer render-stage / dynamic mesh) is GPU-deferred.
    /// Exposed for harness cases and test inspection.
    /// </summary>
    public Hrot.Stride.Core.DebugPrimitiveRenderer3D GizmoRenderer3D { get; private set; } = null!;

    // ── Split-authority forward-sync (P1, STR-P1-T6) ─────────────────────

    /// <summary>
    /// The split-authority sync script (Pass A: visual existence; Pass B: non-owned
    /// forward-sync). Replaces the P0 flat forward-sync.
    /// Exposed for test inspection.
    /// </summary>
    public SplitAuthorityStrideSyncScript? SplitSync { get; private set; }

    // ── Animation backend + bridge (P4, STR-P4-T3/T4) ────────────────────

    /// <summary>
    /// The real <see cref="StrideAnimationBackend"/> wired as the <c>IAnimationBackend</c>
    /// for editor_stride (STR-P4-T3, design §6.4). Drives the idle/walk/run locomotion blend
    /// and the jump-traversal montage slot state machine headlessly; the GPU-bound
    /// <c>PerEntityBlendTreeBuilder</c> is attached to a mannequin's <c>AnimationComponent</c>
    /// separately (GPU-deferred). Exposed for harness cases and test inspection.
    /// </summary>
    public StrideAnimationBackend AnimationBackend { get; private set; } = null!;

    /// <summary>
    /// The locomotion + montage bridge (STR-P4-T3/T4, DD-1 §10). Reconciles backend
    /// registration with the live mannequin set, pumps <c>SimVelocity</c> →
    /// <c>UpdateLocomotionInputs</c> each tick, routes off-mesh-link traversals to the jump
    /// montage path, and ticks the backend. Driven manually in <see cref="Tick"/> after the
    /// kernel update so it reads the post-physics <see cref="SimTransform"/>/<see cref="SimVelocity"/>.
    /// Exposed for harness cases (Walk/Run/Jump) and test inspection.
    /// </summary>
    public StrideAnimationBridge AnimationBridge { get; private set; } = null!;

    /// <summary>
    /// The live animation glue (STR-P4, BATCH-16 Fix A). Reconciles the set of mannequins that
    /// have a <c>PerEntityBlendTreeBuilder</c> attached to their <c>AnimationComponent</c> against
    /// the live visual set: on appearance it loads the clips, builds the per-entity blend tree,
    /// and attaches it to the backend so the backend's per-frame blend/montage state actually
    /// drives the GPU skeleton; on disappearance it releases the builder. Created only when both a
    /// visual binding system and an <see cref="IMannequinBlendTreeInstaller"/> are supplied (i.e.
    /// the live GPU app); <c>null</c> in headless runs without a clip installer. Exposed for tests.
    /// </summary>
    public MannequinAnimationBinder? AnimationBinder { get; private set; }

    // ── Internal helpers ─────────────────────────────────────────────────
    private bool _disposed;

    // The concrete GPU debug-draw sink (may be null in headless mode).
    // Stored so it can be disposed with the subsystem.
    private IDisposable? _debugDrawSinkDisposable;

    // True only when a REAL (non-NoOp) physics service was injected via Initialize().
    // When false, PhysicsBodyLifecycle.Execute is not called so phantom NoOp bodies
    // are never created and BulletReverseSyncSystem cannot clobber SimVelocity.
    private bool _physicsIsActive;

    private BulletCharacterMotor?   _characterMotor;
    private KinematicVehicleMotor?  _vehicleMotor;
    private VehicleNavigationIntentSystem? _vehicleNavIntentSystem;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs and wires the minimal headless composition.
    /// Safe to call in headless CI — no GPU, no window, no DDS.
    /// </summary>
    /// <param name="visualFactory">
    /// Optional <see cref="IStrideVisualFactory"/> to enable visual binding.
    /// Pass <c>null</c> (default) to run headlessly without creating Stride visuals —
    /// useful for test harnesses and for running the FDP kernel without a GPU.
    /// Pass a <see cref="StrideVisualFactory"/> (concrete GPU factory) for the real
    /// game to render entities.  Pass a recording fake factory for integration tests.
    /// </param>
    /// <param name="blendTreeInstaller">
    /// Optional GPU-bound clip-loader + <c>PerEntityBlendTreeBuilder</c> installer (STR-P4 live
    /// animation glue, BATCH-16 Fix A). Supply <see cref="StrideMannequinBlendTreeInstaller"/>
    /// (backed by the running game's <c>Content</c>) in the live app so mannequins actually
    /// animate. Pass <c>null</c> (default) headlessly — the backend still computes the blend, but
    /// no GPU builder is attached (there is no skeleton to drive). Requires a non-null
    /// <paramref name="visualFactory"/> to take effect (the builder needs an <c>AnimationComponent</c>).
    /// </param>
    /// <param name="physicsBodyService">
    /// Optional concrete <see cref="IPhysicsBodyService"/> to use for physics body lifecycle and
    /// motor operations (BATCH-17, STR-D11).
    /// Pass <c>null</c> (default) or omit to use <see cref="NoOpPhysicsBodyService"/> — safe for
    /// headless tests and CI where no running <c>Stride.Physics.Simulation</c> is available.
    /// Pass a <see cref="BulletPhysicsBodyServiceDeferred"/> (or concrete
    /// <c>BulletPhysicsBodyService</c>) in the live app (after <c>BeginRun</c> where the
    /// <c>PhysicsProcessor</c> is initialised) so entities actually move under Bullet physics.
    /// </param>
    /// <param name="debugDrawSink">
    /// Optional concrete GPU <see cref="Hrot.Stride.Core.IDebugDrawSink3D"/> that replaces the
    /// default logging-only sink (STR-D16 resolution, BATCH-21).
    /// Pass a <see cref="Hrot.Stride.Core.PooledEntityDebugDrawSink3D"/> in the live app so 3-D
    /// gizmo shapes are actually rendered in the Stride window. Pass <c>null</c> (default) for
    /// headless runs (CI / tests) — the logging sink is used instead.
    /// </param>
    public void Initialize(
        IStrideVisualFactory? visualFactory = null,
        IMannequinBlendTreeInstaller? blendTreeInstaller = null,
        IPhysicsBodyService? physicsBodyService = null,
        Hrot.Stride.Core.IDebugDrawSink3D? debugDrawSink = null)
    {
        // ── 1. ECS world ────────────────────────────────────────────────
        World = new EntityRepository();

        // Orchestration bus is SEPARATE from world.Bus (design §8.1 invariant).
        OrchestrationBus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(OrchestrationBus);
        OrchestratorEventRegistry.RegisterInternalEvents(OrchestrationBus);

        var accumulator = new EventAccumulator();
        Kernel          = new ModuleHostKernel(World, accumulator);

        // ── 2. Component registration ────────────────────────────────────
        // Mirror EditorSubsystem §1b: all component types must be registered before
        // the kernel builds its query plans (Initialize() below).
        SimHostComponentRegistry.RegisterAll(World);
        CgfComponentRegistry.RegisterAll(World);

        // CrowdMotorIntent (BATCH-17, STR-D11): the steering-output component written by
        // CrowdAgentUpdateSystem (P2) and read by BulletCharacterMotor. Not included in
        // SimHostComponentRegistry because it was added to the seam in P1; register it here
        // so the motor can query it and the harness Physics Walk case can add it to entities.
        World.RegisterComponent<Fdp.Toolkit.Navigation.CrowdMotorIntent>();

        // CrowdAgent (BATCH-19 FIX): tag component that opts an entity into DotRecast crowd
        // steering. CrowdAgentUpdateSystem.Execute guards on IsComponentTypeRegistered<CrowdAgent>()
        // and returns early if absent — the F5 NavmeshWalk demo also guards on the same check and
        // bails with "[Navmesh Walk] WARNING: CrowdAgent component type not registered — cannot proceed."
        // NavigationIntent and NavigationStatus are already registered via SimHostComponentRegistry →
        // MuscleRoleComponentRegistry → KinematicComponentRegistry (confirmed). Only CrowdAgent was missing.
        World.RegisterComponent<Fdp.Toolkit.Navigation.CrowdAgent>();

        // NavAgentProfile (BATCH-20): the per-agent locomotion profile read by
        // NavigationIntentBridgeSystem when it auto-registers an infantry crowd agent from a
        // LocomotionChannel MoveTo action. Not registered by the SimHost registries; register it
        // here so the F6 "FDP Move Order (char)" demo can supply the correct infantry radius/height
        // (and so HasComponent<NavAgentProfile> in the bridge is a safe registered-type query).
        World.RegisterComponent<Fdp.Toolkit.Navigation.NavAgentProfile>();

        // ── 3. Time controller ───────────────────────────────────────────
        var timeConfig  = new TimeControllerConfig { Role = TimeRole.Standalone };
        TimeController  = (MasterSyncController)TimeControllerFactory.Create(World.Bus, timeConfig);
        Kernel.SetTimeController(TimeController);
        TimeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());

        // ── 4. Shared services ────────────────────────────────────────────
        EntityMap = new NetworkEntityMap();
        World.SetSingletonManaged<NetworkEntityMap>(EntityMap);

        var behaviorRegistry = new BehaviorRegistry();
        var mapperRegistry   = new TacticalIntentMapperRegistry();
        // Register Urban-Combat mappers so CgfLogicPack resolves tactical intents.
        mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.DefendAreaMapper());
        mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.HullDownAttackMapper());

        // ── 5. Spawn pipeline ─────────────────────────────────────────────
        // BATCH-03 (STR-D8 discharge): Replace the P0 TestUnit placeholder with the real
        // UrbanCombat TKB templates.  UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates()
        // attaches StrideRenderModelDefDto to CivilianPedestrian, CivilianCar, MilitaryAPC,
        // InfantrySoldier, and Insurgent — which enables StrideVisualBindingSystem to resolve
        // visuals for all UrbanCombat entity classes.
        TkbDb = new TkbDatabase();
        UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(TkbDb);
        var tkbDb = TkbDb;

        var translators = BuildTranslators();
        var elm         = new EntityLifecycleModule(tkbDb, System.Array.Empty<int>());
        elm.SetTranslators(translators);

        var idAllocator = new SequentialIdAllocator();
        var spawnSys    = new NetworkSpawningSystem(
            tkbDb, elm, EntityMap, idAllocator,
            localNodeId: EditorNodeId,
            translators: translators);

        ScenarioSource = new ScenarioEntityCreationRequestSource();

        var requestSystem = new CreateEntityRequestSystem(
            requestSource:      ScenarioSource,
            ackSink:            new NullEntityAckSink(),
            tkbDb:              tkbDb,
            idAllocator:        idAllocator,
            localNodeId:        EditorNodeId,
            isDefaultProcessor: true);

        // ── 6. Orchestration slave ────────────────────────────────────────
        // Mirror EditorSubsystem line 581 exactly.
        var clusterSlave = new ClusterSlave(EditorNodeId, "Editor", OrchestrationBus);
        var orchPack     = new OrchestrationLogicPack(clusterSlave);

        // ── 7. Logic packs ────────────────────────────────────────────────
        // Brain (CGF) — direct system registration mirroring EditorHarness pattern.
        var cgfPack = new CgfLogicPack(behaviorRegistry, EntityMap, ScenarioSource, mapperRegistry);

        // ── P1 (STR-P1-T1): StrideKinematicsModule replaces SimHostCoreLogicPack's GroundKinematicsModule role.
        //   StrideKinematicsModule registers SpatialHash/FormationTarget/VehicleCommand/
        //   NavigationExecution/CrowdAgentUpdate (Simulation phase) and DeadReckoningSyncSystem
        //   with DriveFromNetwork=false (PostSimulation phase).
        //   CarKinematicsSystem and LinearKinematicsSystem are intentionally absent — Bullet
        //   physics (wired in T2–T5) drives movement.  Combat/damage/nav-bridge systems are
        //   registered individually below to preserve their behaviour.
        //
        // Recomposition rationale (vs reusing SimHostCoreLogicPack):
        //   SimHostCoreLogicPack bundles GroundKinematicsModule which includes the two FDP
        //   integrators.  We cannot suppress just those systems without forking the class.
        //   The cleanest approach is to register combat/nav-bridge systems individually and
        //   substitute StrideKinematicsModule for GroundKinematicsModule.  This mirrors the
        //   SimHostCoreLogicPack composition exactly (same phase ordering) minus the two
        //   integrators.  See design §5.1–§5.2.
        //
        // BATCH-19 (STR-D19 discharge): use a deferred DotRecastDtCrowdProvider instead of
        // FakeDtCrowdProvider.  The provider starts in "no-op" mode and is initialized with
        // the real Infantry DtNavMesh by StrideHrotGame.BakeNavmesh() after BeginRun (when scene
        // geometry is available).  Until TryInitializeNavMesh is called, RegisterAgent / Update
        // return silently — no crash, same behaviour as the old fake.
        // Infantry max-agent-radius: 0.4 m (slightly > 0.3 m agent radius for grid margin).
        var deferredCrowd    = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        InfantryCrowdProvider = deferredCrowd;
        var strideKinematics = new StrideKinematicsModule(dtCrowd: deferredCrowd);
        KinematicsModule = strideKinematics;
        var combatModule     = new CombatModule();
        var damageModule     = new DamageAssessmentModule();
        // Pass the crowd provider to the bridge so it registers infantry entities on MoveTo.
        var navIntentBridge  = new NavigationIntentBridgeSystem(strideKinematics.TrajectoryPool, deferredCrowd);
        var routeTrajSync    = new RouteTrajectorySyncSystem(strideKinematics.TrajectoryPool);
        var personalRoute    = new PersonalRouteAuthoringSystem();

        // Register modules and systems on the kernel.
        // Simulation-phase systems MUST go through an IEcsModule (kernel restriction).
        Kernel.RegisterModule(elm);
        Kernel.RegisterModule(new SimHostModule(spawnSys));
        Kernel.RegisterModule(orchPack);
        Kernel.RegisterGlobalSystem(requestSystem);
        Kernel.RegisterGlobalSystem(new GenesisMaterializationSystem(EntityMap));

        // CGF input + sim systems
        foreach (var sys in cgfPack.InputSystems)      Kernel.RegisterGlobalSystem(sys);

        // Build the combined Simulation-phase system list:
        //   DamageAssessmentModule + nav-bridge + StrideKinematicsModule (no integrators)
        //   + UnitHierarchySystem + EqsResultUpdateSystem.
        // This mirrors SimHostCoreLogicPack.SimulationSystems with GroundKinematicsModule
        // replaced by StrideKinematicsModule.
        var simSystems = new System.Collections.Generic.List<IEcsModuleSystem>();
        foreach (var s in damageModule.SimulationSystems) simSystems.Add(s);
        simSystems.Add(navIntentBridge);
        simSystems.Add(routeTrajSync);
        foreach (var s in strideKinematics.SimulationSystems) simSystems.Add(s);

        // VehicleNavigationIntentSystem (BATCH-20, STR-D19): production navmesh navigation for
        // VEHICLE entities driven by NavigationIntent (the crowd bridge excludes vehicles).
        // Registered AFTER strideKinematics.SimulationSystems (which contains NavigationExecutionSystem
        // + CrowdAgentUpdateSystem) and BEFORE the physics step / KinematicVehicleMotor (which runs
        // pre-physics in Tick), so the VehicleState it writes is consumed the same frame. Reads the
        // INavmeshProvider singleton each tick (registered by StrideHrotGame.BakeNavmesh); no-op when
        // absent. Exposed for the F7 harness case.
        _vehicleNavIntentSystem = new VehicleNavigationIntentSystem();
        simSystems.Add(_vehicleNavIntentSystem);

        simSystems.Add(new UnitHierarchySystem());
        simSystems.Add(new EqsResultUpdateSystem());

        Kernel.RegisterModule(new EditorStrideSimulationModule(
            cgfPack.SimulationSystems,
            simSystems));

        // Muscle input systems (combat input)
        foreach (var sys in combatModule.InputSystems)  Kernel.RegisterGlobalSystem(sys);
        Kernel.RegisterGlobalSystem(personalRoute);

        // Muscle post-sim systems: combat post-sim + StrideKinematicsModule post-sim
        // (DeadReckoningSyncSystem with DriveFromNetwork=false).
        foreach (var sys in combatModule.PostSimulationSystems)       Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in strideKinematics.PostSimulationSystems)   Kernel.RegisterGlobalSystem(sys);

        Kernel.Initialize();

        // ── 8. ClusterMaster — latch released immediately (empty Mandatory) ──
        // Mirror EditorSubsystem lines 1091–1092.
        var offlineConfig = new ClusterConfiguration { Mandatory = System.Array.Empty<string>() };
        ClusterMaster     = new ClusterMaster(OrchestrationBus, offlineConfig);
        // Because Mandatory is empty, the constructor calls PublishStandby() immediately,
        // setting _bootstrapLatch = true and publishing ClusterState.Idle ("Standby").

        // ── 9. Visual binding system (STR-P0-T7/T8) ──────────────────────────
        // Wired only when a factory is provided — headless tests pass null.
        // The StrideVisualBindingSystem reconciles FDP entities to Stride visuals each
        // frame via the two-pass differential sync (design §7 Pass-A).
        if (visualFactory != null)
        {
            VisualBindingSystem = new StrideVisualBindingSystem(visualFactory, TkbDb);
        }

        // ── 10. Physics body service + lifecycle (STR-P1-T2, STR-D11) ────────
        // Use the caller-supplied physicsBodyService if provided (live GPU path: BulletPhysicsBodyService).
        // Fall back to NoOpPhysicsBodyService for headless tests/CI (no running Simulation).
        // The real service is passed from StrideHrotGame.BootEditorSubsystem after BeginRun
        // where PhysicsProcessor is guaranteed to be initialised (STR-D11).
        PhysicsBodyService = physicsBodyService ?? new NoOpPhysicsBodyService();
        // _physicsIsActive is true ONLY when a real (non-NoOp) service was supplied.
        // When false, Tick will not call PhysicsBodyLifecycle.Execute — no phantom
        // NoOp bodies are created and BulletReverseSyncSystem cannot clobber SimVelocity.
        _physicsIsActive = physicsBodyService != null;
        if (VisualBindingSystem != null)
        {
            PhysicsBodyLifecycle = new PhysicsBodyLifecycleSystem(PhysicsBodyService, VisualBindingSystem);
        }

        // ── 11. Motors (STR-P1-T3, STR-P1-T4) ───────────────────────────────
        // Wired only when a lifecycle system is available (requires visual binding).
        // BulletCharacterMotor + KinematicVehicleMotor run in the Simulation phase
        // (pre-physics) to push intents/commands into the (no-op) physics service.
        // NOTE: The no-op service accepts calls without errors, so motors execute
        // harmlessly in headless mode. They are registered via a SimulationPhaseAdapter
        // so the kernel can schedule them correctly.
        if (PhysicsBodyLifecycle != null)
        {
            var characterMotor = new BulletCharacterMotor(PhysicsBodyService, PhysicsBodyLifecycle);
            var vehicleMotor   = new KinematicVehicleMotor(PhysicsBodyService, PhysicsBodyLifecycle);

            // Register motors as global simulation-phase systems.
            // They need access to the kernel but must be registered before Kernel.Initialize().
            // Since Kernel.Initialize() was already called above, we use a post-init workaround:
            // The motors are stored and called manually in Tick() in the simulation slot.
            // They cannot be added post-Initialize, so they are not wired here.
            // TODO STR-D11: Move motor registration before Kernel.Initialize() once the
            // physics service is concrete. For now, store references for manual invocation.
            _characterMotor = characterMotor;
            _vehicleMotor   = vehicleMotor;
        }

        // ── 12. Reverse-sync group (STR-P1-T5, STR-D5) ───────────────────────
        // BulletReverseSyncSystem wrapped in a TogglablePostSimulationGroup.
        // Driven manually in Tick() BEFORE Kernel.Update() so FDP Simulation-phase
        // consumers read post-physics SimTransform the same frame (design §8.3).
        // NOT registered with the kernel (would run inside Update, causing one-frame lag).
        //
        // The group is ALWAYS created so the P5 replay handler (STR-P5-T4) has a togglable
        // post-sim group to sever during replay even in headless mode (no visual factory).
        // When there is a PhysicsBodyLifecycle (visual factory present) it wraps the real
        // BulletReverseSyncSystem; headless it is an empty group whose Enabled flag is still the
        // replay sever/restore switch (an empty enabled group is a harmless no-op each Tick).
        if (PhysicsBodyLifecycle != null)
        {
            var reverseSync = new BulletReverseSyncSystem(PhysicsBodyService, PhysicsBodyLifecycle);
            ReverseSyncGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                "BulletReverseSync", reverseSync);
        }
        else
        {
            ReverseSyncGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                "BulletReverseSync");
        }

        // ── 13. Split-authority sync (STR-P1-T6) ─────────────────────────────
        // Replaces the P0 flat forward-sync (VisualBindingSystem.Sync).
        // Driven manually in Tick() AFTER Kernel.Update().
        if (VisualBindingSystem != null && visualFactory != null)
        {
            SplitSync = new SplitAuthorityStrideSyncScript(VisualBindingSystem, visualFactory);
        }

        // ── 14. Animation backend + locomotion/montage bridge (STR-P4-T3/T4) ──
        // The real StrideAnimationBackend is the IAnimationBackend for editor_stride
        // (design §6.4). The bridge (DD-1 §10) reconciles backend registration with the
        // live mannequin set, pumps SimVelocity → UpdateLocomotionInputs each tick (so a
        // moving mannequin blends idle→walk→run), routes off-mesh-link traversals to the
        // jump montage, and ticks the backend. It is driven manually in Tick() after
        // Kernel.Update() so it reads the post-physics SimTransform/SimVelocity.
        //
        // A class is "animated" (a mannequin) iff its TKB template carries a
        // CharacterAnimationDefDto (STR-P4-T2 attaches it to InfantrySoldier/Insurgent).
        AnimationBackend = new StrideAnimationBackend();
        AnimationBackend.Initialize(new Hrot.MuscleCharacter.Animation.Contracts.AnimationBackendConfig
        {
            MaxEntities = 256,
            DefaultPlayRate = 1f,
        });

        AnimationBridge = new StrideAnimationBridge(
            AnimationBackend,
            isAnimatedClass: IsAnimatedClass,
            jumpStartMontageId: StableIdHasher.ComputeMontageAssetId("Jump_Start"),
            jumpLoopMontageId:  StableIdHasher.ComputeMontageAssetId("Jump_Loop"),
            jumpEndMontageId:   StableIdHasher.ComputeMontageAssetId("Jump_End"));

        // ── 14b. Live animation glue (STR-P4, BATCH-16 Fix A) ─────────────────
        // The binder is the missing live-path connection: it loads the clips, creates the
        // PerEntityBlendTreeBuilder per mannequin AnimationComponent, registers the montage clips,
        // and attaches the builder to the backend (so Tick() drives the skeleton). It needs both a
        // live visual set (AnimationComponent) and a GPU clip-loader, so it is created only when
        // both VisualBindingSystem and a blendTreeInstaller are present (the live GPU app).
        // Headless runs leave it null — the backend still computes the blend, but there is no
        // skeleton to drive. Driven manually in Tick() after the bridge reconciles registration.
        if (VisualBindingSystem != null && blendTreeInstaller != null)
        {
            AnimationBinder = new MannequinAnimationBinder(
                AnimationBackend, AnimationBridge, VisualBindingSystem, blendTreeInstaller);
        }

        // ── 15. Record / replay (STR-P5-T4, STR-D5 resolution, design §9) ─────
        // EcsRecordReplayController is the same factory EditorSubsystem/SimHostSubsystem use:
        //   - PrepareRecordingAsync installs a RecordingModule (RecorderTickSystem captures
        //     this node's authoritative SimTransform each PostSimulation tick).
        //   - PrepareReplayAsync installs a ReplayModule (PlaybackTickSystem drives SimTransform
        //     from recorded keyframes, registered OUTSIDE any togglable group so it always runs).
        // Both are installed/uninstalled into THIS kernel on demand.
        RecordReplayController = new EcsRecordReplayController(Kernel, nodeId: EditorNodeId, World);

        // ReferenceReplayLoadHandler severs the reverse-sync group during replay (design §9):
        //   PrepareReplay → ReverseSyncGroup.Enabled = false (Bullet reverse-sync cannot overwrite
        //                   historical SimTransform; PlaybackTickSystem drives it instead).
        //   FinalizeReplay / PrepareLive → ReverseSyncGroup.Enabled = true (authority back to Bullet).
        // Only the post-sim (reverse-sync) group is wired here; editor_stride has no separate
        // Togglable input/simulation/lifecycle groups (CGF/sim run inside the kernel module graph),
        // and the NoOp physics service means there is no Bullet step to pause — severing the
        // reverse-sync group is sufficient in Mode 1 (see report §"sever-suffices"). bypassLifecycle
        // is null (no GhostCreationSystem in this composition).
        ReplayLoadHandler = new ReferenceReplayLoadHandler(
            controller:            RecordReplayController,
            inputGroup:            null,
            simGroup:              null,
            postSimGroup:          ReverseSyncGroup,
            lifecycleGroup:        null,
            bypassLifecycleToggle: null,
            storageDirectory:      RecordReplayStorageDirectory);

        // ── 16. 3D gizmo ProducerBuffer + renderer (STR-P5-T1, design §11) ────
        // Local gizmo producers write DebugPrimitives into ProducerBuffer; GizmoRenderer3D
        // sweeps it (two-pass anchor-resolve + FdpStrideTransform swizzle) and emits to a sink.
        // In the live GPU app a PooledEntityDebugDrawSink3D is passed via debugDrawSink
        // (STR-D16 resolution, BATCH-21); headless/tests get the logging sink.
        ProducerBuffer  = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoPrimitiveBuffer();
        var effectiveSink = debugDrawSink ?? (Hrot.Stride.Core.IDebugDrawSink3D)new LoggingDebugDrawSink3D();
        _debugDrawSinkDisposable = effectiveSink as IDisposable;
        GizmoRenderer3D = new Hrot.Stride.Core.DebugPrimitiveRenderer3D(effectiveSink);
    }

    /// <summary>
    /// Default headless <see cref="Hrot.Stride.Core.IDebugDrawSink3D"/> for editor_stride: logs
    /// each resolved+swizzled shape/line at Trace level rather than issuing a GPU call.
    /// The live GPU sink (<see cref="Hrot.Stride.Core.PooledEntityDebugDrawSink3D"/>) is passed
    /// via <see cref="Initialize"/> when a GPU scene is available (STR-D16 resolution).
    /// </summary>
    private sealed class LoggingDebugDrawSink3D : Hrot.Stride.Core.IDebugDrawSink3D
    {
        private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("GizmoRenderer3D");

        public void DrawLine(in Hrot.Stride.Core.DebugDrawLine3D line) =>
            Log.Trace("[gizmo3d] line {0}->{1}", line.Start, line.End);

        public void DrawShape(in Hrot.Stride.Core.DebugDrawShape3D shape) =>
            Log.Trace("[gizmo3d] {0} @ {1} scale {2}", shape.Kind, shape.Position, shape.Scale);
    }

    /// <summary>
    /// True if the TKB class identified by <paramref name="tkbType"/> is an animated mannequin
    /// — i.e. its template carries a <see cref="CharacterAnimationDefDto"/> (attached by
    /// STR-P4-T2 to InfantrySoldier/Insurgent). Used by the animation bridge to decide which
    /// entities to register with the backend and locomotion-drive.
    /// </summary>
    private bool IsAnimatedClass(long tkbType)
        => TkbDb.TryGetByType(tkbType, out var template)
           && template.GetDescriptor<CharacterAnimationDefDto>() != null;

    /// <summary>
    /// Advances one simulation frame.  Called by the external host loop or test harness.
    ///
    /// <para>
    /// Frame ordering (design §8.3, STR-P1-T7):
    /// <list type="number">
    ///   <item>Orchestration bus pump + <see cref="ClusterMaster.Tick"/>.</item>
    ///   <item><b>Pre-physics motors</b> (BulletCharacterMotor, KinematicVehicleMotor) —
    ///     push intents/commands into the physics service before the conceptual physics step.
    ///     In P1 with <see cref="NoOpPhysicsBodyService"/> these are no-ops.</item>
    ///   <item><b>Reverse-sync</b> (<see cref="ReverseSyncGroup"/>) — executed manually
    ///     BEFORE <see cref="ModuleHostKernel.Update()"/> so FDP Simulation-phase consumers
    ///     (SpatialHashSystem, vision broadphase, EQS) read the post-physics
    ///     <see cref="SimTransform"/> the same frame (no one-frame lag). With
    ///     <see cref="NoOpPhysicsBodyService"/> this writes identity pose + zero velocity.</item>
    ///   <item><b>FDP kernel tick</b>: <see cref="ModuleHostKernel.Update()"/> — all FDP
    ///     Simulation-phase systems run and read the already-reverse-synced SimTransform.</item>
    ///   <item><b>Split-authority forward-sync</b> (<see cref="SplitSync"/>): Pass A
    ///     reconciles the Stride visual entity set; Pass B forward-syncs non-owned entities.
    ///     Replaces the P0 flat forward-sync (STR-P1-T6).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="dt">Simulation delta-time in seconds.</param>
    public void Tick(float dt)
    {
        // ── Step 1: Orchestration pump ────────────────────────────────────
        // Mirror EditorSubsystem 1373–1374: swap orch bus then tick master.
        OrchestrationBus.SwapBuffers();
        ClusterMaster.Tick();

        // ── Step 2: Physics body lifecycle (STR-P1-T2, design §5.6) ─────
        // Create/destroy Bullet bodies keyed on the authority bit.
        // Must run BEFORE the motors so newly authoritative entities have a body
        // by the time the motors try to act on them.
        // Guard: only execute when a real (non-NoOp) physics service is active.
        // Without this guard, NoOp phantom bodies would be created and
        // BulletReverseSyncSystem would clobber SimVelocity (animation harness regression).
        if (_physicsIsActive)
            PhysicsBodyLifecycle?.Execute(World, dt);

        // ── Step 2b: Pre-physics motors (STR-P1-T3/T4) ──────────────────
        // Push motor intents into physics service before the physics step.
        // In P1 with NoOpPhysicsBodyService these are no-ops.
        //
        // STR-D21 F7 fix: run VehicleNavigationIntentSystem HERE (before the motor)
        // instead of relying solely on its kernel-phase execution (Step 4).
        // Problem: the kernel runs at Step 4 (AFTER the motor at Step 2b), so the motor
        // always reads VehicleState that is 1 tick stale — on the very first tick after a new
        // NavigationIntent, VehicleState is still zero, the motor drives zero velocity, and
        // Bullet's deferred-body activation window is wasted.  Running the system here means
        // the motor sees the freshly-computed VehicleState on the same frame it was written,
        // eliminating the 1-tick lag that prevented the APC from ever moving.
        // The kernel-phase execution is retained so the system still participates in the normal
        // ECS scheduling (e.g. for correct ordering with NavigationExecutionSystem and correct
        // diagnostics from the kernel health monitor).  The double-execution per frame is
        // idempotent: on the second run the same IntentId is already in _routes so PlanRoute
        // is skipped; only the steering output (VehicleState) is rewritten — same values.
        _vehicleNavIntentSystem?.Execute(World, dt);
        _characterMotor?.Execute(World, dt);
        _vehicleMotor?.Execute(World, dt);

        // ── Step 3: Reverse-sync BEFORE kernel tick (STR-P1-T5/T7) ───────
        // Writes Bullet-resolved pose+velocity into SimTransform/SimVelocity for
        // owned entities. Must run before Kernel.Update() so FDP Simulation-phase
        // consumers read the post-physics SimTransform the same frame (design §8.3).
        // The TogglablePostSimulationGroup's Enabled flag allows replay severability (§9).
        ReverseSyncGroup?.Execute(World, dt);

        // ── Step 4: FDP kernel tick ───────────────────────────────────────
        // Step() puts dt into the time controller; Kernel.Update() reads from it.
        TimeController.Step(dt);
        Kernel.Update();

        // ── Step 4b: Animation bridge (STR-P4-T3/T4) ─────────────────────
        // Runs after the kernel update so it reads the post-physics SimTransform/SimVelocity
        // (DD-1 §10 phase placement). Reconciles backend registration with the live mannequin
        // set, pumps SimVelocity → idle/walk/run locomotion blend, routes off-mesh-link
        // traversal events to the jump montage path, and ticks the backend once.
        var traversals = ((ISimulationView)World)
            .ReadEvents<OffMeshTraversalStartedEvent>();
        AnimationBridge.DispatchTraversals(traversals);
        AnimationBridge.Execute(World, dt);

        // ── Step 5: Split-authority forward-sync (STR-P1-T6) ─────────────
        // Pass A: reconcile Stride visual entity set (appear/disappear).
        // Pass B: forward-sync non-owned entities from SimTransform.
        // Replaces the P0 flat forward-sync.
        if (SplitSync != null)
        {
            SplitSync.Sync(World);
        }
        else
        {
            // Fallback: P0 flat forward-sync (headless without visual factory).
            // This branch is taken when VisualBindingSystem is null (no factory provided).
            // With a factory, SplitSync is always set (see Initialize step 13).
        }

        // ── Step 5b: Live animation glue reconcile (STR-P4, BATCH-16 Fix A) ──
        // After the bridge has registered mannequins with the backend (Step 4b) and the visual
        // sync has created their AnimationComponents (Step 5), bind a PerEntityBlendTreeBuilder to
        // each new mannequin (loading clips + attaching to the backend) and release it for any that
        // disappeared. Only runs in the live GPU app (binder is null otherwise).
        AnimationBinder?.Reconcile();

        // ── Step 6: 3D gizmo render (STR-P5-T1 / STR-D16, BATCH-21) ────────
        // BeginFrame hides last frame's pool entities; Render resolves+swizzles primitives and
        // activates the needed pool entries; EndFrame is a no-op for the pooled sink (cleanup
        // already done in BeginFrame). Then advance the buffer's persistence clock.
        GizmoRenderer3D.Sink.BeginFrame();
        GizmoRenderer3D.Render(ProducerBuffer.GetFrame());
        GizmoRenderer3D.Sink.EndFrame();
        ProducerBuffer.EndFrame(dt);

        // ── Step 7: Selection alive-guard + highlight gizmo (STR-P5-T3, BATCH-23) ──
        // Must run AFTER the gizmo render step so the selection highlight is written to the
        // NEXT frame's buffer (which the renderer will sweep on the following Tick call).
        // ClearIfDead removes the selection if the entity was destroyed this tick.
        SelectionState.ClearIfDead(World);
        EmitSelectionHighlight();
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AnimationBinder?.ReleaseAll();
        PhysicsBodyLifecycle?.DestroyAll();
        VisualBindingSystem?.DestroyAll();
        _debugDrawSinkDisposable?.Dispose();
        Kernel?.Dispose();
        World?.Dispose();
        ClusterMaster?.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // ── Selection highlight gizmo (STR-P5-T3, BATCH-23) ─────────────────

    // Highlight color: bright cyan (0,255,255) for high contrast against scene geometry.
    private static readonly Rgba32 SelectionColor = new Rgba32(0, 230, 255, 255);

    // Half-extents of the selection box in metres (world-space; constant for v1).
    // 1.0 m on each side → 2 m total; tall enough to encircle a standing infantry soldier.
    private const float SelectionBoxHalfExtent = 1.0f;

    /// <summary>
    /// Emits a bright cyan bounding-box gizmo into the <see cref="ProducerBuffer"/> for the
    /// currently-selected entity (if any and alive).  Called once per tick after the main gizmo
    /// render step so the box is written to the <em>next</em> frame's buffer and thus rendered
    /// on the following Tick (one-frame latency is imperceptible at interactive rates).
    ///
    /// <para>
    /// Implementation: the selection highlight is a set of 12 edges of an axis-aligned box
    /// (3×4 line pairs in FDP space, world-coordinate) centred on the entity's
    /// <see cref="Fdp.Toolkit.Spatial.SimTransform.Position"/>.  Using explicit lines rather
    /// than a SemanticShape box avoids the anchor-resolution path and keeps the code simple.
    /// Lines persist for exactly <c>dt+ε</c> (1 frame); they are re-emitted every tick while
    /// the entity remains selected so the box tracks the entity as it moves.
    /// </para>
    /// </summary>
    private void EmitSelectionHighlight()
    {
        if (!SelectionState.HasSelection) return;

        var entity = SelectionState.SelectedEntity;
        if (!World.IsAlive(entity)) return;
        if (!World.IsComponentTypeRegistered<SimTransform>()) return;
        if (!World.HasComponent<SimTransform>(entity)) return;

        ref readonly var t = ref World.GetComponentRO<SimTransform>(entity);
        var c = t.Position; // centre in FDP space (X=East, Y=North, Z=Up)

        float h = SelectionBoxHalfExtent;
        // Emit 1-frame-lifetime lines (lifetime ≤ 0 means 1 tick in GizmoPrimitiveBuffer convention;
        // we use a tiny positive value so EndFrame(dt) expires them correctly).
        const float oneFrame = 0.05f; // > any fixed dt (1/60 ≈ 0.0167); expires after 1 tick

        // 8 corners of the AABB:
        var p000 = new System.Numerics.Vector3(c.X - h, c.Y - h, c.Z - h);
        var p001 = new System.Numerics.Vector3(c.X - h, c.Y - h, c.Z + h);
        var p010 = new System.Numerics.Vector3(c.X - h, c.Y + h, c.Z - h);
        var p011 = new System.Numerics.Vector3(c.X - h, c.Y + h, c.Z + h);
        var p100 = new System.Numerics.Vector3(c.X + h, c.Y - h, c.Z - h);
        var p101 = new System.Numerics.Vector3(c.X + h, c.Y - h, c.Z + h);
        var p110 = new System.Numerics.Vector3(c.X + h, c.Y + h, c.Z - h);
        var p111 = new System.Numerics.Vector3(c.X + h, c.Y + h, c.Z + h);

        // 12 edges (each axis pair):
        EmitSelectionLine(p000, p100, oneFrame);  // bottom face
        EmitSelectionLine(p000, p010, oneFrame);
        EmitSelectionLine(p100, p110, oneFrame);
        EmitSelectionLine(p010, p110, oneFrame);
        EmitSelectionLine(p001, p101, oneFrame);  // top face
        EmitSelectionLine(p001, p011, oneFrame);
        EmitSelectionLine(p101, p111, oneFrame);
        EmitSelectionLine(p011, p111, oneFrame);
        EmitSelectionLine(p000, p001, oneFrame);  // vertical edges
        EmitSelectionLine(p100, p101, oneFrame);
        EmitSelectionLine(p010, p011, oneFrame);
        EmitSelectionLine(p110, p111, oneFrame);
    }

    private void EmitSelectionLine(System.Numerics.Vector3 from, System.Numerics.Vector3 to, float lifetime)
    {
        var line = Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeLine(
            from, to, SelectionColor,
            sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters,
            target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All);
        line.Space           = Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.World;
        line.LifetimeSeconds = lifetime;
        ProducerBuffer.EmitRaw(line);
    }

    private static IReadOnlyList<ITkbEntityTranslator> BuildTranslators()
    {
        return new List<ITkbEntityTranslator>
        {
            new SpatialCoreTkbTranslator(),                               // Fdp.Toolkit.Spatial
            new VehicleKinematicsTkbTranslator(),                         // CarKinem.Tkb
            new Fdp.Toolkit.Behavior.Translators.BehaviorTkbTranslator(),
            new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator(),
            new Fdp.Toolkit.Perception.Translators.PerceptionTkbTranslator(),
        }.AsReadOnly();
    }

    // ── Nested: simulation-phase module adapter ──────────────────────────

    /// <summary>
    /// Routes simulation-phase systems from both CGF and SimHost packs through
    /// a single module so the kernel's "no global Simulation systems" constraint
    /// is honoured (mirrors EditorHarness.EditorSimulationModule pattern).
    /// </summary>
    private sealed class EditorStrideSimulationModule : IEcsModule
    {
        private readonly IEnumerable<IEcsModuleSystem> _cgfSim;
        private readonly IEnumerable<IEcsModuleSystem> _muscleSim;

        public string          Name   => "EditorStrideSimulation";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public EditorStrideSimulationModule(
            IEnumerable<IEcsModuleSystem> cgfSim,
            IEnumerable<IEcsModuleSystem> muscleSim)
        {
            _cgfSim    = cgfSim;
            _muscleSim = muscleSim;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            var seen = new System.Collections.Generic.HashSet<Type>();
            foreach (var sys in _cgfSim.Concat(_muscleSim))
                if (seen.Add(sys.GetType())) registry.RegisterSystem(sys);
        }

        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
