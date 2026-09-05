using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Orchestration.Handlers;
using Hrot.UI.Common.Facades;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Spatial;
using CarKinem.Tkb;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Perception.Translators;
using Hrot.CGF;
using Hrot.Core.Network;
using Hrot.Editor;
using Hrot.Editor.Modules;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Offline (no DDS) test harness for editor integration tests.
/// Instantiates <see cref="ModuleHostKernel"/> with the three local packs:
/// <see cref="SimHostCoreLogicPack"/>, <see cref="CgfLogicPack"/>,
/// and <see cref="ScenarioEditorModule"/>.
///
/// <para>No CycloneDDS domain is allocated.</para>
/// </summary>
public sealed class EditorHarness : IDisposable
{
    private const int PumpSleepMs = 5;

    private MasterSyncController? _timeController;
    private readonly SequentialIdAllocator _idAllocator;
    private ScenarioFileService _fileService = null!;
    private ZoneManagerService  _zoneService = null!;
    private IReadOnlyList<IEcsModule> _logicPacks = null!;
    private PhysicsToolkitModule? _physicsModule;
    private PreviewClusterOpHandler? _previewHandler;

    public EntityRepository  Repo      { get; }
    public FdpEventBus        Bus       { get; }
    public FdpEventBus        OrchBus   { get; }
    public ModuleHostKernel   Kernel    { get; }
    public NetworkEntityMap   EntityMap { get; }

    /// <summary>
    /// The shared Blueprint registry this harness's kernel ticks against. Mirrors the
    /// editor's <c>_blueprintRegistry</c>: register blueprints here, attach via
    /// <c>BlueprintAttachService</c>, and the in-kernel <c>BlueprintTickSystem</c> runs them.
    /// </summary>
    public Fdp.Toolkit.Blueprints.BlueprintRegistry BlueprintRegistry { get; }
    public IEditorLogic       Editor    { get; private set; } = null!;
    public ScenarioFileService FileService  => _fileService;
    public ZoneManagerService  ZoneService  => _zoneService;
    public IPreviewController  Preview   { get; private set; } = null!;

    /// <summary>The mirrored editor master's current mode — Deterministic while authoring is paused.</summary>
    public Fdp.ModuleHost.Time.TimeMode TimeControllerMode => _timeController!.GetMode();

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-192</c> — builds a real <see cref="Hrot.Editor.DebugApi.DebugApiService"/> against this
    /// harness, so the ai-debug API can be railed headless.</b>
    /// </summary>
    /// <remarks>
    /// <para>📌 <b>Why this is only ~20 lines when the debt said otherwise.</b> <c>DEBT-MCP-001</c>
    /// (this project's csproj, and <c>docs/MCP_Integration.md</c>) excluded <b>15</b> <c>DebugApi*Tests.cs</c>
    /// files on the stated grounds that they need <i>"9 harness collaborators (<c>_serializer</c>,
    /// <c>History</c>, <c>_tkbDb</c>, <c>_geoTransform</c>, <c>_bpManager</c>, <c>_rrController</c>,
    /// <c>EditorTracer</c>, <c>BTreeSession</c>, <c>HsmSession</c>) that trunk's EditorHarness does not yet
    /// carry"</i>.</para>
    ///
    /// <para>⛔⛔ <b>Measured <c>2026-09-05</c>: EIGHT OF THOSE NINE ARE OPTIONAL CONSTRUCTOR PARAMETERS.</b>
    /// The editor ctor requires exactly nine non-null arguments — <c>world</c>, <c>entityMap</c>,
    /// <c>extraction</c>, <c>time</c>, <c>preview</c>, <c>editor</c>, <c>eventHistory</c>,
    /// <c>timeController</c>, <c>clusterState</c> — and of the debt's list only <c>History</c>
    /// (<c>eventHistory</c>) is among them. The rest are omitted here and the endpoints that need them
    /// degrade, which is precisely what those optional parameters exist for.</para>
    ///
    /// <para>⇒ ⭐ the real gap was three collaborators with trivial constructors over things this harness
    /// already held, plus exposing the <c>MasterSyncController</c> it already built. ⚠ <b>The debt was
    /// priced as a harness reconciliation and was actually a factory method</b> — which is why the API's
    /// swallowed-exception defects (<c>CE-190</c>/<c>CE-191</c>) went two months without a compiled rail
    /// that could see them.</para>
    ///
    /// <para>⛔ <b>What this deliberately does NOT wire</b>, so no caller mistakes silence for absence:
    /// breakpoints, record/replay, the AI tracer, the BTree/HSM/Blueprint debug sessions, the behaviour and
    /// blueprint registries, and the mission service. Endpoints in those groups answer their own
    /// "not available" — they are out of scope for the entity/spawn/command tier this unlocks.</para>
    /// </remarks>
    public Hrot.Editor.DebugApi.DebugApiService BuildDebugApiService()
        => new(
            world:          Repo,
            entityMap:      EntityMap,
            extraction:     new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(Repo, EntityMap),
            time:           new Hrot.Editor.UI.EditorTimeTransportFacade(Preview, _timeController!, Repo),
            preview:        Preview,
            editor:         Editor,
            eventHistory:   _eventHistory,
            timeController: _timeController!,
            // ⭐ The harness is a single offline editor: it is always "the one node, operating".
            clusterState:   () => Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit);

    /// <summary>
    /// The real ring-buffer history the debug API reads for <c>GET /events</c>.
    /// </summary>
    /// <remarks>
    /// ⭐ A REAL service rather than a null one: <c>DebugApiBatch04</c>'s command rails assert that a
    /// published event turns up in history, so a null implementation would make them vacuously green —
    /// the exact failure this whole line of work is about.
    /// ⚠ Nothing pumps <c>Capture</c> here; a test that needs populated history must call it itself.
    /// </remarks>
    public Fdp.Core.Diagnostics.DiagnosticEventHistoryService EventHistory => _eventHistory;

    private readonly Fdp.Core.Diagnostics.DiagnosticEventHistoryService _eventHistory = new();

    // ── Nested test stub ─────────────────────────────────────────────────────

    private sealed class SequentialIdAllocator : INetworkIdAllocator
    {
        private long _next = 1000;
        public long AllocateId()            => _next++;
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    /// <summary>
    /// Lightweight <see cref="IPreviewController"/> used by tests that need to
    /// enter / exit the dry-run preview session without a full subsystem stack.
    /// </summary>
    private sealed class EditorPreviewController : IPreviewController
    {
        private readonly PreviewClusterOpHandler _handler;
        private readonly MasterSyncController    _timeController;
        private bool _inPreview;

        internal EditorPreviewController(
            MasterSyncController     timeController,
            PreviewClusterOpHandler  handler)
        {
            _handler        = handler;
            _timeController = timeController;
        }

        public bool IsInPreviewMode => _inPreview;

        public void EnterPreviewMode(bool startPaused = false)
        {
            _handler.TriggerLoadingPreview();
            if (!startPaused)
                _timeController.SwitchToContinuous();
            _inPreview = true;
        }

        public void ExitPreviewMode()
        {
            _handler.TriggerUnloadingPreview();
            _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());
            _inPreview = false;
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public EditorHarness(IEcsModuleSystem[]? extraGlobalSystems = null)
    {
        Repo   = new EntityRepository();
        Bus    = Repo.Bus;
        OrchBus = new FdpEventBus();

        // Register all shared HROT components and events before module setup.
        HrotSharedComponentRegistry.RegisterAll(Repo);

        // Register SimHost-specific components needed for editor authoring systems
        // (PassengerBuffer, IsEmbarkedTag, ActorCapabilityState, EmbarkEntityCommand,
        //  DisembarkEntityCommand, TargetMemory, SeedTargetCommand, PhysicsCollider, etc.).
        CognitiveComponentRegistry.RegisterAll(Repo);
        CombatComponentRegistry.RegisterAll(Repo);
        CgfComponentRegistry.RegisterAll(Repo);
        Repo.RegisterManagedComponent<ZoneMembership>();

        var accumulator = new EventAccumulator();
        Kernel = new ModuleHostKernel(Repo, accumulator);
        _physicsModule = new PhysicsToolkitModule();
        _physicsModule.Initialize(Repo);
        _previewHandler = new PreviewClusterOpHandler(Repo);

        // MasterSyncController in Deterministic mode — no DDS sync, starts paused.
        // LookaheadWallTicks=0 ensures SwitchToDeterministic sets a barrier at "now" so Step()
        // advances immediately in tests (default 200ms lookahead would cause PumpFrames no-ops).
        var timeConfig  = new TimeControllerConfig
        {
            Role       = TimeRole.Standalone,
            SyncConfig = new TimeConfig { LookaheadWallTicks = 0 },
        };
        // T3: the master goes on the ORCHESTRATION bus, because that is where the intents live —
        // the same rule EditorSubsystem now follows, and the Orchestrator always did. This harness
        // MIRRORS the editor's composition rather than constructing EditorSubsystem, so when the
        // real wiring moves this must move with it: a mirror that has drifted from the thing it
        // mirrors is worse than no harness, because its tests stay green while production breaks.
        Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(OrchBus);
        _timeController = (MasterSyncController)TimeControllerFactory.Create(OrchBus, timeConfig);
        Kernel.SetTimeController(_timeController);
        _timeController.SwitchToDeterministic(new HashSet<int>());
        CrossTheDeterministicBarrier();

        EntityMap = new NetworkEntityMap();

        var behaviorRegistry = new BehaviorRegistry();
        var clusterSlave     = new ClusterSlave(0, "EditorHarness", OrchBus);
        var serializer       = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var zoneService      = new ZoneManagerService();
        _zoneService = zoneService;
        var fileService      = new ScenarioFileService(serializer, Bus, zoneService);
        _fileService = fileService;

        // ── TKB + ELM + spawn system ─────────────────────────────────────────
        var tkbDb = new TkbDatabase();
        tkbDb.Register(new TkbTemplate("TestUnit", tkbType: 1L));

        var translators = new List<ITkbEntityTranslator>
        {
            new SpatialCoreTkbTranslator(),
            new VehicleKinematicsTkbTranslator(),
            new BehaviorTkbTranslator(),
            new CombatTkbTranslator(),
            new PerceptionTkbTranslator()
        }.AsReadOnly();

        var elm      = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
        elm.SetTranslators(translators);
        _idAllocator = new SequentialIdAllocator();
        var spawnSys = new NetworkSpawningSystem(tkbDb, elm, EntityMap, _idAllocator, localNodeId: 0, translators: translators);

        // ── Module registration (offline — no translator packs) ───────────────
        var simHostCorePack  = new SimHostCoreLogicPack(EntityMap);
        var mapperRegistry = new TacticalIntentMapperRegistry();
        mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.DefendAreaMapper());
        mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.HullDownAttackMapper());
        var cgfLogicPackInst = new CgfLogicPack(behaviorRegistry, EntityMap,
            new ScenarioEntityCreationRequestSource(), mapperRegistry);
        var scenarioMod      = new ScenarioEditorModule(fileService);
        var simHostMod       = new Fdp.ModuleHost.Scheduling.SingleSystemModule("NetworkSpawning", spawnSys);

        Kernel.RegisterModule(new CognitiveSpatialModule(Repo));
        Kernel.RegisterModule(scenarioMod);
        Kernel.RegisterModule(elm);
        Kernel.RegisterModule(simHostMod);
        Kernel.RegisterModule(new Hrot.SimHost.Modules.EqsModule());
        Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(EntityMap));

        // ⭐⭐ CE-192 — mirror the editor's event-history capture, so GET /events has a PRODUCER.
        //   📐 EditorSubsystem.cs:1437 registers exactly this system for the world bus (and two more for
        //   the orchestration and interaction buses). ⛔ Without it the history service is real but never
        //   written, and a rail asserting "the published command appears in history" fails for a reason
        //   that has nothing to do with the command path — the store simply has no writer.
        //   ⚠ Only the WORLD bus is mirrored: it is the one the debug API's default `bus:"world"` reads.
        Kernel.RegisterGlobalSystem(
            new Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem("World", _eventHistory, Bus));

        // ── Multi-phase system registration for SimHostCorePack and CgfLogicPack ──
        // CGF Brain systems -- register directly (no toggling needed in the editor harness)
        foreach (var sys in cgfLogicPackInst.InputSystems)      Kernel.RegisterGlobalSystem(sys);

        // Muscle systems -- register directly
        foreach (var sys in simHostCorePack.InputSystems)          Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in simHostCorePack.PostSimulationSystems) Kernel.RegisterGlobalSystem(sys);

        // ── Blueprint runtime (MVE-BATCH-02) ──────────────────────────────────────
        // Mirror the EditorSubsystem wiring through the SAME shared helper so the headless
        // real-kernel test exercises the identical composition (no sandbox world). The helper
        // registers the tier components on Repo and the BeforeSync maintenance system as a
        // global, and returns the Simulation-phase tick system to splice into the sim module.
        BlueprintRegistry = new Fdp.Toolkit.Blueprints.BlueprintRegistry();
        var bpTick = Hrot.Blueprints.Editor.Runtime.BlueprintRuntimeWiring.WireBlueprintRuntime(
            Kernel, Repo, BlueprintRegistry);

        // Simulation-phase systems must go through a module (kernel forbids global registration).
        // FC-1·G2: bpTick is SPLICED before the action dispatchers (its [UpdateBefore] targets) --
        // module-group order is array position and the kernel does not re-apply ordering attributes
        // inside the group, so the old append ran the tick AFTER the dispatchers (intent writes
        // dispatched one tick late). Same splice as EditorSubsystem, via the shared helper.
        var cgfSimWithBlueprint = Hrot.Blueprints.Editor.Runtime.BlueprintRuntimeWiring
            .SpliceIntoSimulation(cgfLogicPackInst.SimulationSystems, bpTick);
        Kernel.RegisterModule(new EditorSimulationModule(
            cgfSimWithBlueprint,
            simHostCorePack.SimulationSystems));

        // Register editor-specific ECS systems (cargo, perception, zone authoring).
        Kernel.RegisterModule(new EditorSystemsModule(zoneService));

        // Register caller-injected global systems (e.g. mock physics solvers in unit tests).
        // Must happen BEFORE Kernel.Initialize().
        if (extraGlobalSystems != null)
            foreach (var sys in extraGlobalSystems)
                Kernel.RegisterGlobalSystem(sys);

        Kernel.Initialize();

        // ── Editor application facade ─────────────────────────────────────────
        var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst, simHostMod };
        _logicPacks = logicPacks;
        Editor = new EditorApplication(fileService, Bus, OrchBus, Repo, Kernel, logicPacks);
        Preview = new EditorPreviewController(_timeController!, _previewHandler!);
    }

    // ── Feature-switch helper ─────────────────────────────────────────────────

    /// <summary>
    /// Provides translator packs to install when <see cref="IEditorLogic.SwitchToExternalAsync"/>
    /// is called. Must be called BEFORE the first SwitchToExternalAsync call.
    /// Re-creates the <see cref="EditorApplication"/> to capture the new translator pack list.
    /// </summary>
    public void SetTranslatorPacks(IReadOnlyList<IEcsModule> packs)
    {
        Editor = new EditorApplication(_fileService, Bus, OrchBus, Repo, Kernel, _logicPacks, translatorPacks: packs);
    }

    // ── Pump API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 102 (<c>102c</c>) — THE HARNESS ENTERS STEPPING BEFORE A TEST PUMPS.</b>
    /// <c>BP-379</c>.
    ///
    /// <para>🔴🔴 <b>The mechanism, traced end to end.</b> <c>SwitchToDeterministic</c> does NOT enter
    /// <c>Stepping</c> — it arms a FUTURE BARRIER and sets <c>MasterMode.BarrierPending</c>
    /// *(<c>MasterSyncController:253</c>)*. ⇒ on the first pumped frame:
    /// <list type="number">
    ///   <item>⛔ <c>Step(0.005f)</c> hits <c>if (_mode != MasterMode.Stepping) return
    ///   GetCurrentState();</c> — <b>a SILENT no-op</b>: nothing accumulates into
    ///   <c>_pendingStepDelta</c>.</item>
    ///   <item>⛔ <c>Kernel.Update()</c> lands in <c>UpdateBarrierPending</c>, which crosses the barrier
    ///   *(<c>LookaheadWallTicks = 0</c>)*, switches to <c>Stepping</c> — and returns
    ///   <c>BuildGlobalTime(0.0f, 0.0f)</c>, <b>an explicit zero</b>.</item>
    /// </list>
    /// ⇒ ⭐⭐⭐ <b>the harness's first pumped frame was ALWAYS <c>dt = 0</c></b>, and
    /// <c>BlueprintTickSystem:51</c> opens with <c>if (deltaTime &lt;= 0f) return;</c> — so every
    /// behaviour tick lost exactly one frame at startup.</para>
    ///
    /// <para>⭐⭐ <b>Why the HARNESS is the right place</b> *(📌 Batch 101's instruction: do NOT edit the
    /// eight expectations)*. Crossing the barrier is a TIME-CONTROLLER state transition, ⛔ not a
    /// simulation frame — ⚠ so it is driven through the controller alone and <b>no system runs with a
    /// zero <c>dt</c></b>. ⭐ In production the barrier is crossed by ordinary frames while the editor
    /// sits paused; a test that pumps N frames and asserts N ticks is asserting the steady state, and
    /// this makes the harness start there.</para>
    ///
    /// <para>⛔ <b>It THROWS rather than returning</b> if the barrier never falls. ⚠ A harness that
    /// silently stayed in <c>BarrierPending</c> is precisely the failure this closes, and it cost a
    /// batch to find once already.</para>
    /// </summary>
    private void CrossTheDeterministicBarrier()
    {
        // ⭐ LookaheadWallTicks = 0 ⇒ the barrier is "now", so one Update crosses it. ⚠ The loop is a
        //   bound, not an expectation: the check reads the PHYSICAL clock, so a coarse timer could in
        //   principle need a second look.
        for (int i = 0; i < 64; i++)
        {
            if (_timeController!.GetMode() == Fdp.ModuleHost.Time.TimeMode.Deterministic) return;
            _timeController.Update();
        }

        throw new InvalidOperationException(
            "EditorHarness never entered deterministic Stepping: the time controller is still "
          + "BarrierPending, so PumpFrames' first Step() would be a silent no-op and the first "
          + "frame would arrive with dt = 0 (BP-379).");
    }

    /// <summary>Advances <paramref name="frames"/> simulation frames.</summary>
    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            _timeController?.Step(PumpSleepMs / 1000f);
            Kernel.Update();
            // Mirrors EditorSubsystem.Update: the kernel runs first, then the control-plane bus is
            // swapped so intents published by the UI this frame are readable next frame. Without
            // this the harness silently drops every orchestration intent, because ReadManaged on an
            // unswapped bus returns empty.
            OrchBus.SwapBuffers();
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> returns <c>true</c>
    /// or <paramref name="timeoutMs"/> milliseconds have elapsed.
    /// </summary>
    public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        if (condition()) return true;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            _timeController?.Step(PumpSleepMs / 1000f);
            Kernel.Update();
            OrchBus.SwapBuffers();
            if (condition()) return true;
        }

        return false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Kernel.Dispose();
        _physicsModule?.Dispose();
        _physicsModule = null;
        Repo.Dispose();
        _idAllocator.Dispose();
    }

    // IEcsModule wrapper for Simulation-phase systems in the offline Editor harness.
    // The kernel forbids registering SystemPhase.Simulation systems as global systems;
    // they must be routed through a module.
    private sealed class EditorSimulationModule : IEcsModule
    {
        private readonly IEnumerable<IEcsModuleSystem> _cgfSimSystems;
        private readonly IEnumerable<IEcsModuleSystem> _muscleSimSystems;

        public string Name => "EditorSimulation";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public EditorSimulationModule(
            IEnumerable<IEcsModuleSystem> cgfSimSystems,
            IEnumerable<IEcsModuleSystem> muscleSimSystems)
        {
            _cgfSimSystems    = cgfSimSystems;
            _muscleSimSystems = muscleSimSystems;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            // B2 -- the harness must fuse the packs exactly as the editor does, or it stops
            // mirroring the composition it exists to mirror. Same helper, same first-wins order.
            foreach (var sys in Fdp.ModuleHost.Scheduling.SystemComposition.DistinctByType(_cgfSimSystems, _muscleSimSystems))
                registry.RegisterSystem(sys);
        }

        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
