#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial;
using CarKinem.Core;
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
using Hrot.Stride.Animation;
using Hrot.Stride.Core;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Runner;

namespace HrotStrideApp;

/// <summary>
/// <b>editor_stride</b> composition skeleton (STR-P0-T6, Mode 1 of the Stride integration).
///
/// <para>
/// FIX-PERF-1 (hosted-mode substepping): <see cref="TickHosted"/> is designed to be called
/// ONCE per render frame with the render wall delta — NOT through the fixed-step loop driver.
/// See <see cref="StrideHrotGame.Update"/> which bypasses <see cref="StrideHostLoopDriver.AdvanceFrame"/>
/// when <see cref="HostRealEditor"/> is true.
/// </para>
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
    /// Exposed for test inspection. Delegates to <see cref="StridePhysicsBracket"/>.
    /// </summary>
    public PhysicsBodyLifecycleSystem? PhysicsBodyLifecycle => _physicsBracket?.PhysicsBodyLifecycle;

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
    /// The group is driven <b>manually</b> in <see cref="Tick"/> via
    /// <see cref="StridePhysicsBracket.RunPreKernelStep"/> — it is NOT registered
    /// as a kernel system. This ensures it executes <b>before</b>
    /// <see cref="ModuleHostKernel.Update()"/> so FDP Simulation-phase consumers
    /// (SpatialHashSystem, vision broadphase, EQS) read the post-physics
    /// <see cref="SimTransform"/> the same frame (no one-frame lag). See design §8.3.
    /// </para>
    /// </summary>
    public Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup? ReverseSyncGroup => _physicsBracket?.ReverseSyncGroup;

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
    /// Exposed for test inspection. Delegates to <see cref="StridePhysicsBracket"/>.
    /// </summary>
    public SplitAuthorityStrideSyncScript? SplitSync => _physicsBracket?.SplitSync;

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

    // ── Logging ───────────────────────────────────────────────────────────
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    // ── Hosted-mode tick timing (FIX-PERF-1) ─────────────────────────────
    // Fine-grained breakdown: reuse Stopwatch instances, accumulate, log ~once per second.
    // A  = TimeController.Step(dt)              [inside PreKernelUpdateHook]
    // B  = _physicsBracket.RunPreKernelStep     [inside PreKernelUpdateHook]
    // C  = _kernel.Update()                     [PreKernelHook end → PostKernelHook start]
    // D  = _editor.Update(dt)                   [whole call, wraps A+B+C]
    // E1 = AnimationBridge (DispatchTraversals+Execute)
    // E2 = RunPostKernelStep (forward-sync)
    // E3 = Gizmo render
    // E4 = Selection alive-guard + highlight
    // Total = whole TickHosted body
    private readonly System.Diagnostics.Stopwatch _tickHostedSw    = new(); // Total
    private readonly System.Diagnostics.Stopwatch _editorTotalSw   = new(); // D
    private readonly System.Diagnostics.Stopwatch _stepSw          = new(); // A
    private readonly System.Diagnostics.Stopwatch _bracketPreSw    = new(); // B
    private readonly System.Diagnostics.Stopwatch _kernelSw        = new(); // C
    private readonly System.Diagnostics.Stopwatch _animBridgeSw    = new(); // E1
    private readonly System.Diagnostics.Stopwatch _postSyncSw      = new(); // E2
    private readonly System.Diagnostics.Stopwatch _gizmoSw         = new(); // E3
    private readonly System.Diagnostics.Stopwatch _selectionSw     = new(); // E4

    private double _accTotal, _accEditorTotal, _accStep, _accBracketPre,
                   _accKernel, _accAnimBridge, _accPostSync, _accGizmo, _accSelection;
    private int    _tickHostedFrameCount;
    private const int TickHostedLogIntervalFrames = 60;

    // ── [Authority] vehicle ownership oscillation probe (DIAG-AUTH) ──────────
    // Tracks per-entity: whether HasAuthority<SimTransform> was true last tick,
    // and how many times it flipped within the current ~1-second window.
    private readonly Dictionary<Entity, bool>  _authLastValue  = new();
    private readonly Dictionary<Entity, int>   _authFlipCount  = new();
    private readonly System.Diagnostics.Stopwatch _authThrottleSw = System.Diagnostics.Stopwatch.StartNew();
    private const double AuthThrottleWindowSec = 1.0;

    // ── Internal helpers ─────────────────────────────────────────────────
    private bool _disposed;

    // The concrete GPU debug-draw sink (may be null in headless mode).
    // Stored so it can be disposed with the subsystem.
    private IDisposable? _debugDrawSinkDisposable;

    // Reusable physics bracket (encapsulates host-driven pre/post-kernel muscle steps).
    private StridePhysicsBracket _physicsBracket = null!;

    private VehicleNavigationIntentSystem? _vehicleNavIntentSystem;

    // ── Hosted-editor mode (STRIDE_HOST_REAL_EDITOR=1) ────────────────────
    // When true, this subsystem delegates World/Kernel/TimeController to a real
    // EditorSubsystem and drives it via _editor.Update(dt) each Tick.
    // Default = false (today's self-contained kernel path).
    private bool _hostRealEditor;

    /// <summary>
    /// True when this subsystem is hosting a real <see cref="Hrot.Editor.EditorSubsystem"/>
    /// (enabled via <c>STRIDE_HOST_REAL_EDITOR=1</c> env flag or the <c>hostRealEditor</c>
    /// parameter to <see cref="Initialize"/>).
    /// </summary>
    public bool HostRealEditor => _hostRealEditor;

    // The hosted real EditorSubsystem (non-null only when _hostRealEditor == true).
    private EditorSubsystem? _editor;

    /// <summary>
    /// The <see cref="IEditorLogic"/> facade of the hosted real editor.
    /// Non-null only when <see cref="HostRealEditor"/> is <c>true</c> AND
    /// <see cref="Initialize"/> has been called.
    /// </summary>
    public IEditorLogic? HostedEditorLogic => _editor?.EditorLogic;

    /// <summary>
    /// The hosted <see cref="EditorSubsystem"/> instance (implements
    /// <c>IWindowRegistrar</c>, <c>DrawWorld</c>, and <c>DrawUI</c>).
    /// Non-null only when <see cref="HostRealEditor"/> is <c>true</c> AND
    /// <see cref="Initialize"/> has been called.
    ///
    /// <para>
    /// Used by <see cref="StrideInspectorWindow"/> to wire the full editor UI:
    /// <c>HostedEditor.RegisterWindows(wm)</c> registers ALL editor panels with the
    /// <c>WindowManager</c>; <c>HostedEditor.DrawWorld()</c> renders the 2-D map canvas;
    /// <c>HostedEditor.DrawUI()</c> renders menus + popups outside the window manager.
    /// Non-headless (i.e. <c>buildEditorUi=true</c> was passed to <see cref="Initialize"/>)
    /// is a precondition — these are no-ops when the editor is headless.
    /// </para>
    /// </summary>
    public EditorSubsystem? HostedEditor => _editor;

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
    /// <param name="hostRealEditor">
    /// When <c>true</c>, skip building this subsystem's own kernel and instead construct a real
    /// <see cref="Hrot.Editor.EditorSubsystem"/> (headless) with the Stride muscle injected via
    /// <see cref="Hrot.Editor.EditorSubsystem.MuscleModuleFactory"/>. The subsystem's
    /// <see cref="World"/>, <see cref="Kernel"/>, <see cref="TimeController"/>, and
    /// <see cref="ScenarioSource"/> are repointed to the editor's equivalents.
    /// Default = <c>false</c> (today's behavior, byte-identical).
    /// Activated at runtime by setting the <c>STRIDE_HOST_REAL_EDITOR=1</c> environment variable.
    /// </param>
    /// <param name="buildEditorUi">
    /// When <c>true</c> (and <paramref name="hostRealEditor"/> is also <c>true</c>), the hosted
    /// <see cref="Hrot.Editor.EditorSubsystem"/> is initialized with
    /// <see cref="Hrot.Editor.SubsystemConfig.Headless"/> = <c>false</c>, enabling MapCanvas,
    /// adapters, layers, and all non-GPU editor UI.  Must be paired with a live GLFW/OpenGL
    /// context (call after <c>rlImGui.Setup</c>).
    ///
    /// <para>
    /// Default = <c>false</c> so headless tests and CI are byte-identical (editor remains headless).
    /// Activated at runtime when <c>STRIDE_EDITOR_WINDOW=1</c> is also set — threaded from
    /// <see cref="StrideHrotGame"/> which reads that flag.
    /// </para>
    /// </param>
    public void Initialize(
        IStrideVisualFactory? visualFactory = null,
        IMannequinBlendTreeInstaller? blendTreeInstaller = null,
        IPhysicsBodyService? physicsBodyService = null,
        Hrot.Stride.Core.IDebugDrawSink3D? debugDrawSink = null,
        bool hostRealEditor = false,
        bool buildEditorUi = false)
    {
        _hostRealEditor = hostRealEditor;

        if (_hostRealEditor)
        {
            InitializeHosted(visualFactory, blendTreeInstaller, physicsBodyService, debugDrawSink, buildEditorUi);
            return;
        }

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

        // ── P1 (STR-P1-T1) + BATCH refactor: build the reusable kernel-resident muscle module
        //   set via StrideMuscleModules.Build().  This creates the same instances as before
        //   (StrideKinematicsModule, CombatModule, DamageAssessmentModule, nav-bridge systems,
        //   VehicleNavigationIntentSystem, PersonalRouteAuthoringSystem) without inlining
        //   their construction here.
        //
        // BATCH-19 (STR-D19 discharge): use a deferred DotRecastDtCrowdProvider instead of
        // FakeDtCrowdProvider.  The provider starts in "no-op" mode and is initialized with
        // the real Infantry DtNavMesh by StrideHrotGame.BakeNavmesh() after BeginRun (when scene
        // geometry is available).  Until TryInitializeNavMesh is called, RegisterAgent / Update
        // return silently — no crash, same behaviour as the old fake.
        // Infantry max-agent-radius: 0.4 m (slightly > 0.3 m agent radius for grid margin).
        var deferredCrowd = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        InfantryCrowdProvider = deferredCrowd;

        var muscleSet = StrideMuscleModules.Build(deferredCrowd);
        KinematicsModule        = muscleSet.StrideKinematics;
        _vehicleNavIntentSystem = muscleSet.VehicleNavIntent;

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
        //   + VehicleNavigationIntentSystem + UnitHierarchySystem + EqsResultUpdateSystem.
        // This mirrors the original composition exactly (same system instances, same order).
        var simSystems = new System.Collections.Generic.List<IEcsModuleSystem>();
        foreach (var s in muscleSet.Damage.SimulationSystems) simSystems.Add(s);
        simSystems.Add(muscleSet.NavIntentBridge);
        simSystems.Add(muscleSet.RouteTrajSync);
        foreach (var s in muscleSet.StrideKinematics.SimulationSystems) simSystems.Add(s);
        simSystems.Add(muscleSet.VehicleNavIntent);
        simSystems.Add(new UnitHierarchySystem());
        simSystems.Add(new EqsResultUpdateSystem());

        Kernel.RegisterModule(new EditorStrideSimulationModule(
            cgfPack.SimulationSystems,
            simSystems));

        // Muscle input systems (combat input)
        foreach (var sys in muscleSet.Combat.InputSystems)  Kernel.RegisterGlobalSystem(sys);
        Kernel.RegisterGlobalSystem(muscleSet.PersonalRoute);

        // Muscle post-sim systems: combat post-sim + StrideKinematicsModule post-sim
        // (DeadReckoningSyncSystem with DriveFromNetwork=false).
        foreach (var sys in muscleSet.Combat.PostSimulationSystems)                  Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in muscleSet.StrideKinematics.PostSimulationSystems)        Kernel.RegisterGlobalSystem(sys);

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
        // physicsIsActive is true ONLY when a real (non-NoOp) service was supplied.
        // When false, StridePhysicsBracket.RunPreKernelStep skips PhysicsBodyLifecycle.Execute —
        // no phantom NoOp bodies are created and BulletReverseSyncSystem cannot clobber SimVelocity.
        bool physicsIsActive = physicsBodyService != null;
        PhysicsBodyLifecycleSystem? physicsBodyLifecycle = null;
        if (VisualBindingSystem != null)
        {
            physicsBodyLifecycle = new PhysicsBodyLifecycleSystem(PhysicsBodyService, VisualBindingSystem);
        }

        // ── 11. Motors (STR-P1-T3, STR-P1-T4) ───────────────────────────────
        // Wired only when a lifecycle system is available (requires visual binding).
        // BulletCharacterMotor + KinematicVehicleMotor run pre-physics (inside the bracket)
        // to push intents/commands into the physics service.
        // NOTE: The no-op service accepts calls without errors, so motors execute
        // harmlessly in headless mode.
        BulletCharacterMotor?  characterMotor = null;
        KinematicVehicleMotor? vehicleMotor   = null;
        if (physicsBodyLifecycle != null)
        {
            characterMotor = new BulletCharacterMotor(PhysicsBodyService, physicsBodyLifecycle);
            vehicleMotor   = new KinematicVehicleMotor(PhysicsBodyService, physicsBodyLifecycle);
        }

        // ── 12. Reverse-sync group (STR-P1-T5, STR-D5) ───────────────────────
        // BulletReverseSyncSystem wrapped in a TogglablePostSimulationGroup.
        // Driven inside StridePhysicsBracket.RunPreKernelStep BEFORE Kernel.Update() so FDP
        // Simulation-phase consumers read post-physics SimTransform the same frame (design §8.3).
        // NOT registered with the kernel (would run inside Update, causing one-frame lag).
        //
        // The group is ALWAYS created so the P5 replay handler (STR-P5-T4) has a togglable
        // post-sim group to sever during replay even in headless mode (no visual factory).
        Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup reverseSyncGroup;
        if (physicsBodyLifecycle != null)
        {
            var reverseSync = new BulletReverseSyncSystem(PhysicsBodyService, physicsBodyLifecycle);
            reverseSyncGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                "BulletReverseSync", reverseSync);
        }
        else
        {
            reverseSyncGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                "BulletReverseSync");
        }

        // ── 13. Split-authority sync (STR-P1-T6) ─────────────────────────────
        // Replaces the P0 flat forward-sync (VisualBindingSystem.Sync).
        // Driven inside StridePhysicsBracket.RunPostKernelStep AFTER Kernel.Update().
        SplitAuthorityStrideSyncScript? splitSync = null;
        if (VisualBindingSystem != null && visualFactory != null)
        {
            splitSync = new SplitAuthorityStrideSyncScript(VisualBindingSystem, visualFactory);
        }

        // ── 13b. Physics bracket (BATCH refactor) ────────────────────────────
        // Assemble StridePhysicsBracket from the parts constructed above (steps 10–13).
        // Wire VehicleNavIntentSystem from the muscle set (STR-D21: pre-kernel extra execute).
        _physicsBracket = new StridePhysicsBracket(
            physicsIsActive:      physicsIsActive,
            physicsBodyLifecycle: physicsBodyLifecycle,
            characterMotor:       characterMotor,
            vehicleMotor:         vehicleMotor,
            reverseSyncGroup:     reverseSyncGroup,
            splitSync:            splitSync)
        {
            VehicleNavIntentSystem = _vehicleNavIntentSystem,
        };

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

    // ── Hosted-editor initialization (STRIDE_HOST_REAL_EDITOR=1) ─────────────────────────────

    /// <summary>
    /// Hosted-mode initialization path (enabled when <c>STRIDE_HOST_REAL_EDITOR=1</c> or
    /// <c>hostRealEditor=true</c> is passed to <see cref="Initialize"/>).
    ///
    /// <para>
    /// Constructs a real <see cref="Hrot.Editor.EditorSubsystem"/> headlessly with the
    /// Stride muscle injected via <see cref="EditorSubsystem.MuscleModuleFactory"/>
    /// (mirrors <see cref="EditorSubsystemHeadlessBootTests"/> recipe exactly).
    /// Repoints <see cref="World"/>, <see cref="Kernel"/>, <see cref="TimeController"/>,
    /// and <see cref="ScenarioSource"/> to the editor's equivalents, then builds all Stride
    /// view systems (visual binding, physics bracket, animation backend/bridge, gizmo renderer,
    /// selection) bound to the editor's shared world — same steps 9–16 as the OFF path.
    /// </para>
    ///
    /// <para>
    /// Frame ordering in hosted Tick():
    /// <list type="number">
    ///   <item><c>_editor.Update(dt)</c> — runs editor orchestration pump, PreKernelUpdateHook
    ///     (TimeController.Step + bracket pre-kernel), kernel.Update(), PostKernelUpdateHook (null).
    ///     The real editor also runs its own AI hot-reload/orchestration/cluster pump.</item>
    ///   <item>Animation bridge (Step 4b)</item>
    ///   <item>Physics bracket post-kernel step (Step 5 = forward-sync)</item>
    ///   <item>Animation binder reconcile (Step 5b)</item>
    ///   <item>Gizmo render (Step 6)</item>
    ///   <item>Selection alive-guard + highlight (Step 7)</item>
    /// </list>
    /// Post-kernel view-step ORDER is identical to the OFF path.
    /// </para>
    /// </summary>
    private void InitializeHosted(
        IStrideVisualFactory? visualFactory,
        IMannequinBlendTreeInstaller? blendTreeInstaller,
        IPhysicsBodyService? physicsBodyService,
        Hrot.Stride.Core.IDebugDrawSink3D? debugDrawSink,
        bool buildEditorUi = false)
    {
        // ── H1. Build deferred crowd and EditorSubsystem ─────────────────
        // Mirror EditorStrideSubsystem.Initialize step 7 + boot-test recipe.
        var deferredCrowd = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        InfantryCrowdProvider = deferredCrowd;

        StrideMuscleModuleSet? capturedMuscleSet = null;

        _editor = new EditorSubsystem();

        // Set MuscleModuleFactory BEFORE Initialize (mirrors boot-test pattern exactly).
        // The lambda registers the 3 extra muscle-specific component types on ctx.World,
        // builds the Stride muscle set, captures it for the physics bracket, and returns it.
        _editor.MuscleModuleFactory = ctx =>
        {
            // Mirror EditorStrideSubsystem.Initialize step 2: extra muscle-specific components.
            if (!ctx.World.IsComponentTypeRegistered<CrowdMotorIntent>())
                ctx.World.RegisterComponent<CrowdMotorIntent>();
            if (!ctx.World.IsComponentTypeRegistered<CrowdAgent>())
                ctx.World.RegisterComponent<CrowdAgent>();
            if (!ctx.World.IsComponentTypeRegistered<NavAgentProfile>())
                ctx.World.RegisterComponent<NavAgentProfile>();

            var ms = StrideMuscleModules.Build(deferredCrowd);
            capturedMuscleSet = ms;
            return ms.ToEditorModuleList();
        };

        // Boot the real EditorSubsystem.
        // Headless = !buildEditorUi:
        //   false  → full non-headless editor (MapCanvas + adapters + layers + all ImGui panels)
        //             required when the second raylib window is active (STRIDE_EDITOR_WINDOW=1).
        //   true   → headless (default, keeps CI/tests GL-free).
        // OwnWindow = false: the host (StrideInspectorWindow) provides the GLFW/OpenGL context.
        // IsActiveMapOwner = () => false matches the boot-test recipe.
        var config = new SubsystemConfig
        {
            Headless         = !buildEditorUi,
            OwnWindow        = false,
            Deterministic    = true,
            SubsystemName    = "Editor",
            NodeId           = EditorNodeId,
            IsActiveMapOwner = () => false,
        };
        _editor.Initialize(config);

        // capturedMuscleSet is assigned by the factory lambda above (called during Initialize).
        if (capturedMuscleSet == null)
            throw new InvalidOperationException(
                "[EditorStrideSubsystem] Hosted mode: MuscleModuleFactory was not invoked " +
                "during EditorSubsystem.Initialize — capturedMuscleSet is null. " +
                "Check that EditorSubsystem calls MuscleModuleFactory when MuscleModuleFactory != null.");

        KinematicsModule        = capturedMuscleSet.StrideKinematics;
        _vehicleNavIntentSystem = capturedMuscleSet.VehicleNavIntent;

        // ── H2. Repoint public accessors to the editor's live objects ─────
        // World/Kernel/TimeController are now the editor's; all subsequent steps that
        // reference World/Kernel operate on the single shared repository.
        World          = _editor.World;
        Kernel         = _editor.Kernel;
        TimeController = _editor.TimeController;

        // Repoint ScenarioSource so StrideHrotGame.EnqueueDemoSpawns() and all harness
        // cases (which spawn via ctx.ScenarioSource = _editorSubsystem.ScenarioSource)
        // go through the production EditorSubsystem spawn path.
        ScenarioSource = _editor.EntityCreationRequestSource;

        // TkbDb UNIFICATION (BATCH-S2-E): bind the Stride view to the editor's authoritative spawn DB
        // — the exact instance NetworkSpawningSystem + translators resolve from — instead of a duplicate.
        // Then augment its NED platform/infantry templates with Stride render+collision descriptors
        // (generic placeholders). UrbanCombat types already carry render-defs (added inside CreateTkb path),
        // so we must NOT re-register them here (would throw "already exists").
        TkbDb = _editor.TkbDatabase
                ?? throw new InvalidOperationException(
                    "[EditorStrideSubsystem] Hosted mode: _editor.TkbDatabase is null after Initialize.");
        StrideNedRenderDescriptors.Apply(TkbDb);

        // ── H3. Build Stride view systems (steps 9-16, bound to editor's World) ──
        // These are identical to the OFF path because they all operate on World (= editor's World).

        // ── (step 9) Visual binding system ───────────────────────────────
        if (visualFactory != null)
        {
            VisualBindingSystem = new StrideVisualBindingSystem(visualFactory, TkbDb);
        }

        // ── (step 10) Physics body service ───────────────────────────────
        PhysicsBodyService = physicsBodyService ?? new NoOpPhysicsBodyService();
        bool physicsIsActive = physicsBodyService != null;
        PhysicsBodyLifecycleSystem? physicsBodyLifecycle = null;
        if (VisualBindingSystem != null)
        {
            physicsBodyLifecycle = new PhysicsBodyLifecycleSystem(PhysicsBodyService, VisualBindingSystem);
        }

        // ── (step 11) Motors ──────────────────────────────────────────────
        BulletCharacterMotor?  characterMotor = null;
        KinematicVehicleMotor? vehicleMotor   = null;
        if (physicsBodyLifecycle != null)
        {
            characterMotor = new BulletCharacterMotor(PhysicsBodyService, physicsBodyLifecycle);
            vehicleMotor   = new KinematicVehicleMotor(PhysicsBodyService, physicsBodyLifecycle);
        }

        // ── (step 12) Reverse-sync group ─────────────────────────────────
        Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup reverseSyncGroup;
        if (physicsBodyLifecycle != null)
        {
            var reverseSync = new BulletReverseSyncSystem(PhysicsBodyService, physicsBodyLifecycle);
            reverseSyncGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                "BulletReverseSync", reverseSync);
        }
        else
        {
            reverseSyncGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                "BulletReverseSync");
        }

        // ── (step 13) Split-authority sync ───────────────────────────────
        SplitAuthorityStrideSyncScript? splitSync = null;
        if (VisualBindingSystem != null && visualFactory != null)
        {
            splitSync = new SplitAuthorityStrideSyncScript(VisualBindingSystem, visualFactory);
        }

        // ── (step 13b) Physics bracket ───────────────────────────────────
        _physicsBracket = new StridePhysicsBracket(
            physicsIsActive:      physicsIsActive,
            physicsBodyLifecycle: physicsBodyLifecycle,
            characterMotor:       characterMotor,
            vehicleMotor:         vehicleMotor,
            reverseSyncGroup:     reverseSyncGroup,
            splitSync:            splitSync)
        {
            VehicleNavIntentSystem = _vehicleNavIntentSystem,
        };

        // ── H4. Wire the pre-kernel hook onto the editor ──────────────────
        // The hook runs inside EditorSubsystem.Update() just before _kernel.Update().
        // It advances the time controller (making the deterministic editor step by dt)
        // then runs the physics bracket's pre-kernel steps (body lifecycle, motors, reverse-sync).
        // This replaces the manual TimeController.Step + _physicsBracket.RunPreKernelStep
        // calls that the OFF-path Tick does before Kernel.Update().
        // DIAG: wrap each sub-step with its own Stopwatch (no allocation — fields reused).
        _editor.PreKernelUpdateHook = dt =>
        {
            // A: (BATCH-S2-L) time is no longer force-stepped here. The TimeController
            // self-advances via Kernel.Update()->controller.Update() — Continuous (preview)
            // runs, Deterministic (edit) stays frozen — exactly like the standalone editor.
            bool simRunning = _editor.TimeController.GetMode() == TimeMode.Continuous;

            // B: physics bracket pre-kernel (lifecycle + reposition + reverse-sync ALWAYS run;
            // the sim-advancing motors run only when simRunning).
            _bracketPreSw.Restart();
            _physicsBracket.RunPreKernelStep(World, dt, simRunning);
            _bracketPreSw.Stop();

            // C-start: kernel.Update() begins immediately after this hook returns.
            // Start the kernel stopwatch here so it covers only the kernel call itself.
            _kernelSw.Restart();
        };
        // PostKernelUpdateHook: stop the kernel stopwatch (C-end).
        // No other functional work — diagnostics only.
        _editor.PostKernelUpdateHook = () =>
        {
            _kernelSw.Stop();
        };

        // ── (step 14) Animation backend + bridge ──────────────────────────
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

        // ── (step 14b) Live animation glue ────────────────────────────────
        if (VisualBindingSystem != null && blendTreeInstaller != null)
        {
            AnimationBinder = new MannequinAnimationBinder(
                AnimationBackend, AnimationBridge, VisualBindingSystem, blendTreeInstaller);
        }

        // RecordReplayController / ReplayLoadHandler: in hosted mode these are not wired
        // (the real editor has its own replay scaffolding). The OFF-path public properties
        // remain null in hosted mode — callers that use them must guard.
        RecordReplayController = new EcsRecordReplayController(Kernel, nodeId: EditorNodeId, World);
        ReplayLoadHandler = new ReferenceReplayLoadHandler(
            controller:            RecordReplayController,
            inputGroup:            null,
            simGroup:              null,
            postSimGroup:          ReverseSyncGroup,
            lifecycleGroup:        null,
            bypassLifecycleToggle: null,
            storageDirectory:      RecordReplayStorageDirectory);

        // ── (step 16) 3D gizmo ProducerBuffer + renderer ─────────────────
        ProducerBuffer = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoPrimitiveBuffer();
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
        if (_hostRealEditor)
        {
            TickHosted(dt);
            return;
        }

        // ── Step 1: Orchestration pump ────────────────────────────────────
        // Mirror EditorSubsystem 1373–1374: swap orch bus then tick master.
        OrchestrationBus.SwapBuffers();
        ClusterMaster.Tick();

        // ── Steps 2, 2b, 3: Physics bracket pre-kernel step ─────────────
        // Delegates to StridePhysicsBracket.RunPreKernelStep in the identical order:
        //   2.  PhysicsBodyLifecycle.Execute  (if physicsIsActive)
        //   2b. VehicleNavIntentSystem.Execute → CharacterMotor.Execute → VehicleMotor.Execute
        //   3.  ReverseSyncGroup.Execute  (BEFORE Kernel.Update — design §8.3)
        _physicsBracket.RunPreKernelStep(World, dt, simRunning: true);

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

        // ── Step 5: Physics bracket post-kernel step ─────────────────────
        // Delegates to StridePhysicsBracket.RunPostKernelStep:
        //   5.  SplitSync.Sync  (Pass A: visual existence; Pass B: non-owned forward-sync)
        //       Fallback is a no-op in headless mode (same as the original else-branch).
        _physicsBracket.RunPostKernelStep(World);

        // ── Step 5b: Live animation glue reconcile (STR-P4, BATCH-16 Fix A) ──
        // After the bridge has registered mannequins with the backend (Step 4b) and the visual
        // sync has created their AnimationComponents (Step 5), bind a PerEntityBlendTreeBuilder to
        // each new mannequin (loading clips + attaching to the backend) and release it for any that
        // disappeared. Only runs in the live GPU app (binder is null otherwise).
        AnimationBinder?.Reconcile();

        // ── Step 7 (MOVED EARLIER, BATCH-S2-AG): emit selection/marker into THIS frame's buffer ──
        // (was after Step 6, which rendered them one tick late → trail when dragging fast)
        // ClearIfDead removes the selection if the entity was destroyed this tick.
        SelectionState.ClearIfDead(World);
        EmitSelectionHighlight();
        EmitMoveMarker(dt); // BATCH-S2-O: destination marker

        // ── Step 6: 3D gizmo render (STR-P5-T1 / STR-D16, BATCH-21) — now renders the selection/marker emitted just above (same tick) ──
        // BeginFrame hides last frame's pool entities; Render resolves+swizzles primitives and
        // activates the needed pool entries; EndFrame is a no-op for the pooled sink (cleanup
        // already done in BeginFrame). Then advance the buffer's persistence clock.
        GizmoRenderer3D.Sink.BeginFrame();
        GizmoRenderer3D.Render(ProducerBuffer.GetFrame());
        GizmoRenderer3D.Sink.EndFrame();
        ProducerBuffer.EndFrame(dt);
    }

    /// <summary>
    /// Hosted-mode Tick: delegates kernel advancement to the real
    /// <see cref="EditorSubsystem.Update"/> (which fires <see cref="EditorSubsystem.PreKernelUpdateHook"/>
    /// → <c>_kernel.Update()</c> → <see cref="EditorSubsystem.PostKernelUpdateHook"/>),
    /// then runs the post-kernel Stride view steps in the identical ORDER as the OFF path.
    ///
    /// <para>
    /// The orchestration pump (bus swap + cluster master tick) is intentionally NOT called
    /// here — it is handled inside <c>EditorSubsystem.Update</c> which runs its own complete
    /// orchestration + cluster logic. Duplicating it would cause double-pump invariant violations.
    /// </para>
    ///
    /// <para>
    /// Post-kernel view-step ORDER (proves identical to OFF path):
    /// <list type="number">
    ///   <item>_editor.Update(dt) → fires PreKernelUpdateHook (TimeController.Step + bracket pre-kernel)
    ///     → kernel.Update() → PostKernelUpdateHook (null)</item>
    ///   <item>Animation bridge (Step 4b)</item>
    ///   <item>Physics bracket post-kernel step (Step 5 = forward-sync)</item>
    ///   <item>Animation binder reconcile (Step 5b)</item>
    ///   <item>Selection alive-guard + highlight (Step 7, MOVED EARLIER, BATCH-S2-AG)</item>
    ///   <item>Gizmo render (Step 6, now renders same-tick selection)</item>
    /// </list>
    /// </para>
    /// </summary>
    private void TickHosted(float dt)
    {
        // FIX-PERF-1: called ONCE per render frame with the wall dt — no fixed-step substepping.
        // DIAG: fine-grained breakdown (no per-frame allocation; accumulate → log ~1/sec).
        _tickHostedSw.Restart();

        // ── Step 1+4: Real editor Update ──────────────────────────────────
        // Internally: orchestration → PreKernelUpdateHook(dt) → kernel.Update() → PostKernelUpdateHook
        // PreKernelUpdateHook times A (TimeController.Step) and B (BracketPre), then starts C.
        // PostKernelUpdateHook stops C.  All three Stopwatches are fields (no allocation).
        _editorTotalSw.Restart();
        _editor!.Update(dt);
        _editorTotalSw.Stop();

        // ── DIAG-AUTH: vehicle entity authority probe (throttled ~1/sec) ───
        ProbeVehicleAuthority();

        // ── Step 4b: Animation bridge (E1) ───────────────────────────────
        // Identical to OFF path: runs after kernel.Update() to read post-physics state.
        _animBridgeSw.Restart();
        var traversals = ((ISimulationView)World)
            .ReadEvents<OffMeshTraversalStartedEvent>();
        AnimationBridge.DispatchTraversals(traversals);
        AnimationBridge.Execute(World, dt);
        _animBridgeSw.Stop();

        // ── Step 5: Physics bracket post-kernel step (forward-sync) (E2) ─
        _postSyncSw.Restart();
        _physicsBracket.RunPostKernelStep(World);
        // Step 5b: Live animation glue reconcile (folded into PostSync bucket — tiny)
        AnimationBinder?.Reconcile();
        _postSyncSw.Stop();

        // ── Step 7 (MOVED EARLIER): selection sync + alive-guard + emit ──
        _selectionSw.Restart();
        SyncSelection2D3D(); // BATCH-S2-R: two-way 2D↔3D selection mirror (before ClearIfDead so sync sees live state)
        SelectionState.ClearIfDead(World);
        EmitSelectionHighlight();
        EmitMoveMarker(dt); // BATCH-S2-O: destination marker
        _selectionSw.Stop();

        // ── Step 6: gizmo render (E3) — renders what Step 7 just emitted ──
        _gizmoSw.Restart();
        GizmoRenderer3D.Sink.BeginFrame();
        GizmoRenderer3D.Render(ProducerBuffer.GetFrame());
        GizmoRenderer3D.Sink.EndFrame();
        ProducerBuffer.EndFrame(dt);
        _gizmoSw.Stop();

        // ── Throttled breakdown log (~once per second at 60 fps) ──────────
        _tickHostedSw.Stop();
        _accTotal       += _tickHostedSw.Elapsed.TotalMilliseconds;
        _accEditorTotal += _editorTotalSw.Elapsed.TotalMilliseconds;
        _accStep        += _stepSw.Elapsed.TotalMilliseconds;
        _accBracketPre  += _bracketPreSw.Elapsed.TotalMilliseconds;
        _accKernel      += _kernelSw.Elapsed.TotalMilliseconds;
        _accAnimBridge  += _animBridgeSw.Elapsed.TotalMilliseconds;
        _accPostSync    += _postSyncSw.Elapsed.TotalMilliseconds;
        _accGizmo       += _gizmoSw.Elapsed.TotalMilliseconds;
        _accSelection   += _selectionSw.Elapsed.TotalMilliseconds;

        if (++_tickHostedFrameCount >= TickHostedLogIntervalFrames)
        {
            double n        = _tickHostedFrameCount;
            double edTotal  = _accEditorTotal / n;
            double step     = _accStep        / n;
            double bracket  = _accBracketPre  / n;
            double kernel   = _accKernel      / n;
            double overhead = edTotal - (step + bracket + kernel); // editor's own UI/orchestration work
            Log.Info(
                "[TickHosted breakdown] avg/{0}f — " +
                "Total={1:F1} | EditorUpdate={2:F1} (Step={3:F1} Bracket={4:F1} Kernel={5:F1} Overhead={6:F1}) | " +
                "View: Anim={7:F1} PostSync={8:F1} Gizmo={9:F1} Sel={10:F1}  (all ms)",
                _tickHostedFrameCount,
                _accTotal        / n,
                edTotal, step, bracket, kernel, overhead,
                _accAnimBridge   / n,
                _accPostSync     / n,
                _accGizmo        / n,
                _accSelection    / n);

            _accTotal = _accEditorTotal = _accStep = _accBracketPre =
            _accKernel = _accAnimBridge = _accPostSync = _accGizmo = _accSelection = 0;
            _tickHostedFrameCount = 0;
        }
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
        if (_hostRealEditor)
        {
            // Hosted mode: the real editor owns World/Kernel/ClusterMaster — shut it down.
            // EditorSubsystem.Shutdown() flushes the regeneration scheduler and disposes everything.
            try { _editor?.Shutdown(); } catch { /* ignore dispose-time errors */ }
        }
        else
        {
            // OFF path: this subsystem owns its kernel/world/cluster — dispose them.
            Kernel?.Dispose();
            World?.Dispose();
            ClusterMaster?.Dispose();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // ── Selection highlight gizmo (STR-P5-T3, BATCH-23) ─────────────────

    // Highlight color: bright cyan (0,255,255) for high contrast against scene geometry.
    private static readonly Rgba32 SelectionColor = new Rgba32(0, 230, 255, 255);

    // BATCH-S2-P: frame counter for throttled [SelDiag] log (~1/sec at 60 fps).
    private int _selDiagFrame;

    // BATCH-S2-R: 2D↔3D selection sync version trackers.
    private int _last2dSelVersion = -1;
    private int _last3dSelVersion = -1;

    // Half-extents of the selection box in metres (world-space; constant for v1).
    // 1.0 m on each side → 2 m total; tall enough to encircle a standing infantry soldier.
    private const float SelectionBoxHalfExtent = 1.0f;

    // BATCH-S2-O: click-to-move destination marker (FDP world position + remaining lifetime).
    private System.Numerics.Vector3? _moveMarkerFdp;
    private float _moveMarkerSecondsRemaining;
    private const float MoveMarkerTotalSeconds = 3.0f;
    private static readonly Rgba32 MoveMarkerColor = new Rgba32(255, 215, 0, 255); // amber
    private const float MoveMarkerHalfSizeM = 0.6f;

    // BATCH-S2-AD: transient on-screen toast (auto-expiring), driven by the same dt countdown as the move marker.
    private string _toastMessage = string.Empty;
    private float  _toastSecondsRemaining;
    private const float ToastTotalSeconds = 4.0f;

    /// <summary>Currently-visible toast text (empty when none). Read by the editor-window overlay.</summary>
    public string ToastMessage => _toastMessage;
    /// <summary>Seconds the toast remains visible; &gt; 0 means draw it. Read by the editor-window overlay.</summary>
    public float ToastSecondsRemaining => _toastSecondsRemaining;

    /// <summary>Show a short auto-expiring toast (BATCH-S2-AD).</summary>
    public void ShowToast(string message, float seconds = ToastTotalSeconds)
    {
        _toastMessage = message ?? string.Empty;
        _toastSecondsRemaining = seconds;
    }

    /// <summary>
    /// Keeps the 2D editor selection (<see cref="EditorSubsystem.Selected2DEntity"/>) and the 3D
    /// <see cref="SelectionState"/> in sync, one direction per frame (whichever changed), using
    /// version counters to prevent feedback bounce. (BATCH-S2-R)
    /// </summary>
    private void SyncSelection2D3D()
    {
        if (_editor == null) return;
        int v2d = _editor.Selection2DVersion;
        if (v2d != _last2dSelVersion)
        {
            // 2D changed this frame → push to 3D.
            _last2dSelVersion = v2d;
            var e = _editor.Selected2DEntity;
            if (e.HasValue && e.Value != Fdp.Core.Entity.Null && World != null && World.IsAlive(e.Value))
                SelectionState.Select(e.Value);
            else
                SelectionState.Clear();
            _last3dSelVersion = SelectionState.Version; // sync tracker so we don't bounce back
        }
        else if (SelectionState.Version != _last3dSelVersion)
        {
            // 3D changed this frame (e.g. click-to-select) → push to 2D.
            _last3dSelVersion = SelectionState.Version;
            _editor.SetSelection2D(SelectionState.HasSelection ? SelectionState.SelectedEntity : (Fdp.Core.Entity?)null);
            _last2dSelVersion = _editor.Selection2DVersion; // sync tracker
        }
    }

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
        // BATCH-S2-P diagnostic (throttled ~1/s): is a selection present and is the box being emitted?
        if (++_selDiagFrame >= 60)
        {
            _selDiagFrame = 0;
            bool has = SelectionState.HasSelection;
            int idx = has ? SelectionState.SelectedEntity.Index : -1;
            bool alive = has && World != null && World.IsAlive(SelectionState.SelectedEntity);
            Log.Info("[SelDiag] HasSelection={0} entity=#{1} alive={2} (if true, 12 box lines emitted to ProducerBuffer)",
                has, idx, alive);
        }

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

    /// <summary>Show a destination marker at the given FDP world position for a few seconds (BATCH-S2-O).</summary>
    public void ShowMoveMarker(System.Numerics.Vector3 fdpPos)
    {
        _moveMarkerFdp = fdpPos;
        _moveMarkerSecondsRemaining = MoveMarkerTotalSeconds;
    }

    /// <summary>
    /// Cancels any active navigation/move order on <paramref name="entity"/> so it stops where it is
    /// (used when the operator drags the entity in 3D — BATCH-S2-X). Handles both vehicle
    /// (NavigationIntent + VehicleState) and character (DotRecast crowd + CrowdMotorIntent) drives.
    /// </summary>
    public void CancelMove(Fdp.Core.Entity entity)
    {
        if (World == null || !World.IsAlive(entity)) return;

        // Vehicle: stop DirectPoint steering and zero the commanded VehicleState.
        if (World.IsComponentTypeRegistered<NavigationIntent>() && World.HasComponent<NavigationIntent>(entity))
        {
            var intent = World.GetComponent<NavigationIntent>(entity);
            intent.Mode     = NavigationMode.None;       // VehicleNavigationIntentSystem drops the route
            intent.IntentId = intent.IntentId + 1;       // mark as a new (idle) command
            World.SetComponent(entity, intent);
        }
        if (World.IsComponentTypeRegistered<VehicleState>() && World.HasComponent<VehicleState>(entity))
        {
            var vs = World.GetComponent<VehicleState>(entity);
            vs.Speed = 0f; vs.SteerAngle = 0f;           // route-drop does NOT zero this — do it here
            World.SetComponent(entity, vs);
        }

        // Character: pull the agent out of the crowd and zero its motor intent.
        InfantryCrowdProvider?.UnregisterAgent(entity);
        if (World.IsComponentTypeRegistered<CrowdMotorIntent>() && World.HasComponent<CrowdMotorIntent>(entity))
            World.SetComponent(entity, new CrowdMotorIntent { Velocity = System.Numerics.Vector3.Zero });
    }

    private void EmitMoveMarker(float dt)
    {
        if (_toastSecondsRemaining > 0f) _toastSecondsRemaining -= dt; // BATCH-S2-AD
        if (_moveMarkerFdp is not { } c) return;
        _moveMarkerSecondsRemaining -= dt;
        if (_moveMarkerSecondsRemaining <= 0f) { _moveMarkerFdp = null; return; }
        float h = MoveMarkerHalfSizeM;
        void Seg(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
        {
            var line = Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeLine(
                a, b, MoveMarkerColor,
                sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters,
                target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All);
            line.Space           = Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.World;
            line.LifetimeSeconds = 0.05f;
            ProducerBuffer.EmitRaw(line);
        }
        Seg(new(c.X - h, c.Y, c.Z), new(c.X + h, c.Y, c.Z));
        Seg(new(c.X, c.Y - h, c.Z), new(c.X, c.Y + h, c.Z));
        Seg(new(c.X, c.Y, c.Z - h), new(c.X, c.Y, c.Z + h));
    }

    // ── DIAG-AUTH: vehicle authority oscillation probe ────────────────────────
    /// <summary>
    /// Probes every entity that has VehicleState and logs whether
    /// HasAuthority&lt;SimTransform&gt; flipped since the previous tick.
    /// Throttled: emits a per-entity summary line ~once per second.
    /// </summary>
    private void ProbeVehicleAuthority()
    {
        if (!World.IsComponentTypeRegistered<SimTransform>()) return;
        if (!World.IsComponentTypeRegistered<VehicleState>())  return;

        var vehicleQuery = World.Query()
            .With<VehicleState>()
            .With<SimTransform>()
            .Build();

        foreach (var entity in vehicleQuery)
        {
            bool hasAuth = World.HasAuthority<SimTransform>(entity);

            // Detect flip vs last tick.
            if (_authLastValue.TryGetValue(entity, out bool prev) && prev != hasAuth)
            {
                // Flipped — count it.
                _authFlipCount.TryGetValue(entity, out int flips);
                _authFlipCount[entity] = flips + 1;
            }
            _authLastValue[entity] = hasAuth;
        }

        // Throttled summary flush.
        if (_authThrottleSw.Elapsed.TotalSeconds >= AuthThrottleWindowSec)
        {
            double elapsed = _authThrottleSw.Elapsed.TotalSeconds;
            foreach (var entity in _authFlipCount.Keys)
            {
                bool hasAuth = _authLastValue.TryGetValue(entity, out bool a) && a;
                Log.Info("[Authority] vehicle entity={0} HasAuthority<SimTransform>={1} (flips this second={2})",
                    entity, hasAuth, _authFlipCount[entity]);
            }
            // Also log entities that have never flipped (present in _authLastValue but not _authFlipCount).
            foreach (var kv in _authLastValue)
            {
                if (!_authFlipCount.ContainsKey(kv.Key))
                    Log.Info("[Authority] vehicle entity={0} HasAuthority<SimTransform>={1} (flips this second=0)",
                        kv.Key, kv.Value);
            }
            _authFlipCount.Clear();
            _authThrottleSw.Restart();
        }
    }
    // ── End DIAG-AUTH ─────────────────────────────────────────────────────────

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
