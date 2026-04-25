using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception.Modules;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Orchestration.Handlers;
using Hrot.UI.Common.Facades;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
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
    public ModuleHostKernel   Kernel    { get; }
    public NetworkEntityMap   EntityMap { get; }
    public IEditorLogic       Editor    { get; private set; } = null!;
    public ScenarioFileService FileService  => _fileService;
    public ZoneManagerService  ZoneService  => _zoneService;
    public IPreviewController  Preview   { get; private set; } = null!;

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

        public void EnterPreviewMode()
        {
            _handler.TriggerLoadingPreview();
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

    public EditorHarness()
    {
        Repo   = new EntityRepository();
        Bus    = Repo.Bus;

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

        var doctrineRegistry = new DoctrineRegistry();
        var clusterSlave     = new ClusterSlave(0, "EditorHarness");
        var serializer       = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var zoneService      = new ZoneManagerService();
        _zoneService = zoneService;
        var fileService      = new ScenarioFileService(serializer, Bus, zoneService);
        _fileService = fileService;

        // ── TKB + ELM + spawn system ─────────────────────────────────────────
        var tkbDb = new TkbDatabase();
        tkbDb.Register(new TkbTemplate("TestUnit", tkbType: 1L));

        var elm      = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
        _idAllocator = new SequentialIdAllocator();
        var spawnSys = new NetworkSpawningSystem(tkbDb, elm, EntityMap, _idAllocator, localNodeId: 0);

        // ── Module registration (offline — no translator packs) ───────────────
        var simHostCorePack  = new SimHostCoreLogicPack(EntityMap);
        var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, EntityMap, new ScenarioEntityCreationRequestSource());
        var scenarioMod      = new ScenarioEditorModule(fileService);
        var simHostMod       = new SimHostModule(spawnSys);

        Kernel.RegisterModule(new AutonomousPerceptionModule());
        Kernel.RegisterModule(scenarioMod);
        Kernel.RegisterModule(elm);
        Kernel.RegisterModule(simHostMod);

        // ── Multi-phase system registration for SimHostCorePack and CgfLogicPack ──
        // CGF Brain systems -- register directly (no toggling needed in the editor harness)
        foreach (var sys in cgfLogicPackInst.InputSystems)      Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in cgfLogicPackInst.SimulationSystems) Kernel.RegisterGlobalSystem(sys);

        // Muscle systems -- register directly
        foreach (var sys in simHostCorePack.InputSystems)          Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in simHostCorePack.SimulationSystems)     Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in simHostCorePack.PostSimulationSystems) Kernel.RegisterGlobalSystem(sys);

        // Register editor-specific ECS systems (cargo, perception, zone authoring).
        Kernel.RegisterModule(new EditorSystemsModule(Repo, zoneService));

        Kernel.Initialize();

        // ── Editor application facade ─────────────────────────────────────────
        var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst, simHostMod };
        _logicPacks = logicPacks;
        Editor = new EditorApplication(fileService, Bus, Repo, Kernel, logicPacks);
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
        Editor = new EditorApplication(_fileService, Bus, Repo, Kernel, _logicPacks, translatorPacks: packs);
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
        Repo.Dispose();
        _idAllocator.Dispose();
    }
}
