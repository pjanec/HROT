using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Scenario;
using FDP.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
using Hrot.CGF;
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
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

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

    private SteppingTimeController? _stepping;
    private readonly SequentialIdAllocator _idAllocator;
    private ScenarioFileService _fileService = null!;
    private ZoneManagerService  _zoneService = null!;
    private IReadOnlyList<IEcsModule> _logicPacks = null!;

    public EntityRepository  Repo      { get; }
    public FdpEventBus        Bus       { get; }
    public ModuleHostKernel   Kernel    { get; }
    public NetworkEntityMap   EntityMap { get; }
    public IEditorLogic       Editor    { get; private set; } = null!;
    public ScenarioFileService FileService  => _fileService;
    public ZoneManagerService  ZoneService  => _zoneService;

    // ── Nested test stub ─────────────────────────────────────────────────────

    private sealed class SequentialIdAllocator : INetworkIdAllocator
    {
        private long _next = 1000;
        public long AllocateId()            => _next++;
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
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
        Repo.RegisterManagedComponent<ZoneMembership>();

        var accumulator = new EventAccumulator();
        Kernel = new ModuleHostKernel(Repo, accumulator);

        // Stepping time controller — offline, no DDS sync.
        var stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
        _stepping = stepping;
        Kernel.SetTimeController(stepping);

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
        var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, EntityMap);
        var scenarioMod      = new ScenarioEditorModule(fileService);
        var simHostMod       = new SimHostModule(spawnSys);

        Kernel.RegisterModule(simHostCorePack);
        Kernel.RegisterModule(cgfLogicPackInst);
        Kernel.RegisterModule(scenarioMod);
        Kernel.RegisterModule(elm);
        Kernel.RegisterModule(simHostMod);

        // Register editor-specific ECS systems (cargo, perception, zone authoring).
        Kernel.RegisterModule(new EditorSystemsModule(Repo, zoneService));

        Kernel.Initialize();

        // ── Editor application facade ─────────────────────────────────────────
        var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst, simHostMod };
        _logicPacks = logicPacks;
        Editor = new EditorApplication(fileService, Bus, Repo, Kernel, logicPacks);
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
            _stepping?.Step(PumpSleepMs / 1000f);
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
            _stepping?.Step(PumpSleepMs / 1000f);
            Kernel.Update();
            if (condition()) return true;
        }

        return false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Kernel.Dispose();
        Repo.Dispose();
        _idAllocator.Dispose();
    }
}
