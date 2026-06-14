using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception;
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
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Editor.Debug;
using Hrot.CGF;
using Hrot.Core.Network;
using Hrot.Diagnostics.Breakpoints;
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
using StructEdit.Reflection;

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
    private TkbDatabase _tkbDb = null!;
    private WGS84Transform _geoTransform = null!;
    private DataBreakpointManager? _bpManager;
    private DataBreakpointSystem? _bpSystem;
    private EntityRepository? _bpPreTickSnapshot;

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

    /// <summary>The harness's master sync time controller (deterministic mode). Test accessor.</summary>
    public MasterSyncController TimeController => _timeController!;

    /// <summary>Scenario serializer used by this harness (for building extraction services). Test accessor.</summary>
    public ScenarioSerializer Serializer => _serializer!;

    /// <summary>Data breakpoint manager wired by this harness (ADA-BATCH-07). Test accessor.</summary>
    public IDataBreakpointManager? BpManager => _bpManager;

    /// <summary>
    /// Event-history service populated by a World-bus capture system registered in the
    /// PostSimulation phase. Used by the AI Debug API event-history tests.
    /// </summary>
    public Fdp.Core.Diagnostics.DiagnosticEventHistoryService History { get; } = new();

    private ScenarioSerializer? _serializer;

    /// <summary>
    /// Builds a <see cref="Hrot.Editor.DebugApi.DebugApiService"/> wired to this harness's world,
    /// entity map, serializer-injected extraction service, time facade, preview, editor logic, and
    /// event history. Mirrors the production wiring in <c>EditorSubsystem</c> for Tier-1 tests.
    /// </summary>
    public Hrot.Editor.DebugApi.DebugApiService BuildDebugApiService()
    {
        var extraction = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(Repo, EntityMap, _serializer);
        var timeFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(Preview, _timeController!, Repo);
        return new Hrot.Editor.DebugApi.DebugApiService(
            Repo,
            EntityMap,
            extraction,
            timeFacade,
            Preview,
            Editor,
            History,
            _timeController!,
            clusterState: () => (Editor as EditorApplication)?.CurrentClusterState
                                ?? Fdp.Toolkit.Orchestration.ClusterState.Idle,
            tkbDb: _tkbDb,
            geoTransform: _geoTransform,
            spatialGridCellSize: PerceptionConstants.LocalGridCellSize,
            spatialGridOriginX: 0f,
            spatialGridOriginY: 0f,
            spatialGridWidth: PerceptionConstants.LocalGridWidth,
            spatialGridHeight: PerceptionConstants.LocalGridHeight,
            bpManager: _bpManager);
    }

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
        var timeConfig  = new TimeControllerConfig { Role = TimeRole.Standalone };
        _timeController = (MasterSyncController)TimeControllerFactory.Create(Bus, timeConfig);
        Kernel.SetTimeController(_timeController);
        _timeController.SwitchToDeterministic(new HashSet<int>());

        EntityMap = new NetworkEntityMap();

        var behaviorRegistry = new BehaviorRegistry();
        var clusterSlave     = new ClusterSlave(0, "EditorHarness", OrchBus);
        var serializer       = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        _serializer = serializer;
        var zoneService      = new ZoneManagerService();
        _zoneService = zoneService;
        var fileService      = new ScenarioFileService(serializer, Bus, zoneService);
        _fileService = fileService;

        // ── TKB + ELM + spawn system ─────────────────────────────────────────
        var tkbDb = new TkbDatabase();
        tkbDb.Register(new TkbTemplate("TestUnit", tkbType: 1L));
        _tkbDb = tkbDb;

        // ── Geo transform (Berlin origin, matches HrotEnvironment.CreateGeoTransform) ─────────
        _geoTransform = new WGS84Transform();
        _geoTransform.SetOrigin(52.52, 13.405, 0.0);

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
        var simHostMod       = new SimHostModule(spawnSys);

        Kernel.RegisterModule(new CognitiveSpatialModule(Repo));
        Kernel.RegisterModule(scenarioMod);
        Kernel.RegisterModule(elm);
        Kernel.RegisterModule(simHostMod);
        Kernel.RegisterModule(new Hrot.SimHost.Modules.EqsModule());
        Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(EntityMap));

        // Capture World-bus events into the history service (PostSimulation phase) so the
        // AI Debug API event-history endpoint has data to serve in Tier-1 tests.
        Kernel.RegisterGlobalSystem(
            new Fdp.ModuleHost.Diagnostics.EventHistoryCaptureSystem("World", History, Bus));

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
        // bpTick is appended to the CGF sim list; its [UpdateBefore] dispatcher ordering is not
        // re-applied inside the group, but the demo blueprint only mutates its own slot state.
        var cgfSimWithBlueprint = new List<IEcsModuleSystem>(cgfLogicPackInst.SimulationSystems) { bpTick };
        Kernel.RegisterModule(new EditorSimulationModule(
            cgfSimWithBlueprint,
            simHostCorePack.SimulationSystems));

        // Register editor-specific ECS systems (cargo, perception, zone authoring).
        Kernel.RegisterModule(new EditorSystemsModule(zoneService));

        // ── DataBreakpointManager + DataBreakpointSystem (ADA-BATCH-07) ──────────
        _bpPreTickSnapshot = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_bpPreTickSnapshot);
        CognitiveComponentRegistry.RegisterAll(_bpPreTickSnapshot);
        CombatComponentRegistry.RegisterAll(_bpPreTickSnapshot);
        CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);
        _bpPreTickSnapshot.RegisterManagedComponent<ZoneMembership>();

        var bpTimeAdapter          = new MasterSyncTimeControllerAdapter(_timeController!);
        var bpEditSvc              = new ComponentEditServiceBuilder().Build();
        var bpPredicateCompiler    = new PredicateCompiler(bpEditSvc, behaviorRegistry);
        var bpEventScannerCompiler = new EventScannerCompiler(bpEditSvc);
        var bpSnapshotProvider     = new DebugSnapshotProvider(_bpPreTickSnapshot);
        _bpManager                 = new DataBreakpointManager(Repo, _bpPreTickSnapshot, bpSnapshotProvider, bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
        _bpSystem                  = new DataBreakpointSystem(_bpManager, Bus);

        Kernel.RegisterGlobalSystem(bpSnapshotProvider);
        Kernel.RegisterGlobalSystem(_bpSystem);

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

    /// <summary>Advances <paramref name="frames"/> simulation frames.</summary>
    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            _timeController?.Step(PumpSleepMs / 1000f);
            Kernel.Update();
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
        _bpPreTickSnapshot?.Dispose();
        _bpPreTickSnapshot = null;
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
            var registeredTypes = new System.Collections.Generic.HashSet<System.Type>();

            foreach (var sys in _cgfSimSystems)
            {
                if (registeredTypes.Add(sys.GetType()))
                    registry.RegisterSystem(sys);
            }
            foreach (var sys in _muscleSimSystems)
            {
                if (registeredTypes.Add(sys.GetType()))
                    registry.RegisterSystem(sys);
            }
        }

        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
