using System;
using System.Collections.Generic;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Time;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.ExCon;
using Hrot.Orchestrator;
using Xunit;
using HrotCoreOrchTranslator = Hrot.Core.Network.IOrchestrationTranslator;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// HEXAG2 integration tests that verify the hexagonal architecture constraints:
/// headless boundary, single-swap pipeline, C# event severing, slave egress
/// translation, and infrastructure lifecycle teardown.
/// </summary>
public sealed class HexagonalBoundaryTests
{
    // Domain 228 is reserved for this test class.
    private const int TestDomain = 228;

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static SubsystemConfig HeadlessConfig() => new()
    {
        DomainId      = TestDomain,
        Headless      = true,
        OwnWindow     = false,
        SubsystemName = "Test",
    };

    // ── Test 1: Strict Headless Hexagonal Boundary ────────────────────────────

    /// <summary>
    /// HEXAG2-T001: OrchestratorSubsystem and ExConSubsystem constructed with an
    /// OfflineNetworkFactory (zero DDS translators) survive 100 Update() frames
    /// without exception.
    /// Traps: rogue new NodeOpSlaveTranslator / new ClusterOpEgressTranslator calls that
    /// NPE without a live participant; zero new NodeOpSlaveTranslator success condition.
    /// </summary>
    [Fact]
    public void Hexagonal_HeadlessInit_NoRogueTranslatorCreation()
    {
        INetworkFactory factory = new Hrot.Editor.OfflineNetworkFactory();
        var orch  = new OrchestratorSubsystem(factory);
        var excon = new ExConSubsystem(factory);
        try
        {
            orch.Initialize(HeadlessConfig());
            excon.Initialize(HeadlessConfig());

            for (int i = 0; i < 100; i++)
            {
                orch.Update(0.016f);
                excon.Update(0.016f);
            }
        }
        finally
        {
            orch.Shutdown();
            excon.Shutdown();
        }
        // Passes if no NullReferenceException or DDS init exception was thrown.
    }

    // ── Test 2: Single-Swap Pipeline Determinism ──────────────────────────────

    /// <summary>
    /// HEXAG2-T002: Injecting PauseTimeIntent to the unified bus and pumping two
    /// Update() frames causes ClusterUiCache.IsPaused to become true.
    /// Traps: split-bus (UI cache on separate bus) or double-swap (event wiped before
    /// MasterSyncController can drain it) would leave IsPaused false.
    /// </summary>
    [Fact]
    public void Hexagonal_PauseIntent_UpdatesIsPausedinTwoFrames()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        try
        {
            var bus     = subsystem.TimeBusForTest!;
            var uiCache = subsystem.UiCacheForTest!;

            // Publish PauseTimeIntent to the write buffer; no manual swap.
            bus.PublishManaged(new PauseTimeIntent());

            // Frame 1: internal SwapBuffers moves PauseTimeIntent to read;
            // MasterSyncController.Update() drains it and publishes SwitchTimeModeEvent
            // {Deterministic} to the native write buffer.
            subsystem.Update(0f);

            // Frame 2: internal SwapBuffers promotes SwitchTimeModeEvent{Deterministic}
            // to the native read buffer; ClusterUiCache.Update() drains it -> IsPaused = true.
            subsystem.Update(0f);

            Assert.True(uiCache.IsPaused,
                "IsPaused must be true after PauseTimeIntent propagates through the single-bus pipeline.");

            // Resume: publish ResumeTimeIntent and pump two more frames.
            bus.PublishManaged(new ResumeTimeIntent());
            subsystem.Update(0f);
            subsystem.Update(0f);

            Assert.False(uiCache.IsPaused,
                "IsPaused must be false after ResumeTimeIntent propagates through the bus.");
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 3: C# Event Severing ─────────────────────────────────────────────

    /// <summary>
    /// HEXAG2-T003: MasterSyncController consumes PauseTimeIntent directly from the
    /// event bus and emits SwitchTimeModeEvent{Deterministic} without involving
    /// ClusterMaster.HandleClusterOpRequest.
    /// Traps: if the developer routes PauseTimeIntent through ClusterMaster (old path),
    /// this test will only pass if ClusterMaster is not instantiated here — confirming
    /// the direct-bus path works in isolation.
    /// </summary>
    [Fact]
    public void Hexagonal_PauseIntent_MasterSyncHandlesDirectly_NoBridgeThroughClusterMaster()
    {
        var bus  = new FdpEventBus();
        // Drain the initial SwitchTimeModeEvent{Continuous} published by the constructor.
        bus.SwapBuffers();
        bus.Consume<SwitchTimeModeEvent>();

        using var ctrl = new Fdp.Toolkit.Time.Controllers.MasterSyncController(bus);

        // Publish PauseTimeIntent to managed write buffer then swap to read.
        bus.PublishManaged(new PauseTimeIntent());
        bus.SwapBuffers();

        // Update must consume the intent and call SwitchToDeterministic internally,
        // which publishes SwitchTimeModeEvent{Deterministic} to the native write buffer.
        ctrl.Update();

        // Swap once more to promote SwitchTimeModeEvent to the native read buffer.
        bus.SwapBuffers();
        var events = bus.Consume<SwitchTimeModeEvent>().ToArray();

        Assert.True(events.Any(e => e.TargetMode == TimeMode.Deterministic),
            "MasterSyncController must emit SwitchTimeModeEvent{Deterministic} when it " +
            "consumes PauseTimeIntent directly from the bus (no ClusterMaster bridge).");
    }

    // ── Test 4: Slave Egress Translation ─────────────────────────────────────

    /// <summary>
    /// HEXAG2-T004: ExConSubsystem wired with a spy ISlaveOrchestrationTranslator
    /// receives PauseTimeIntent in its Tick() read buffer after one Update() frame.
    /// Traps: if ExCon kept a separate ClusterOp egress bus, the spy translator would
    /// never see the typed intent; if ClusterOpIntent wrapper was still used the
    /// spy would drain nothing.
    /// </summary>
    [Fact]
    public void Hexagonal_PauseIntent_ReachesSlaveTranslatorTick()
    {
        var spy     = new SpySlaveTranslator();
        var factory = new SpyTranslatorFactory(spy);

        var excon = new ExConSubsystem(factory);
        excon.Initialize(HeadlessConfig());
        try
        {
            var bus = excon.BusForTest!;

            // Publish PauseTimeIntent to the write buffer.
            bus.PublishManaged(new PauseTimeIntent());

            // Update() swaps buffers (PauseTimeIntent -> read) then calls
            // _slaveTranslator.Tick() which runs after the swap in ExConSubsystem.Update().
            excon.Update(0.016f);

            Assert.True(spy.PauseIntentSeen,
                "SpySlaveTranslator.Tick() must drain PauseTimeIntent from the read buffer " +
                "after ExConSubsystem.Update() propagates it through the single-bus pipeline.");
        }
        finally
        {
            excon.Shutdown();
        }
    }

    // ── Test 5: Infrastructure Lifecycle Teardown ─────────────────────────────

    /// <summary>
    /// HEXAG2-T005: OrchestratorSubsystem.Shutdown() disposes the IDisposable returned
    /// by INetworkFactory.CreateIdAllocatorServer() exactly once.
    /// Traps: if the handle is lost inside a composite translator or not retained in
    /// _idAllocatorServerHandle, the spy Dispose() count stays 0.
    /// </summary>
    [Fact]
    public void Hexagonal_Shutdown_DisposesIdAllocatorServerHandleOnce()
    {
        var spy     = new SpyDisposable();
        var factory = new SpyDisposableFactory(spy);

        var subsystem = new OrchestratorSubsystem(factory);
        subsystem.Initialize(HeadlessConfig());
        subsystem.Shutdown();

        Assert.True(spy.DisposeCount == 1,
            "IDisposable returned by CreateIdAllocatorServer() must be disposed exactly once " +
            "when OrchestratorSubsystem.Shutdown() is called.");
    }

    // ── Spy / helper types ────────────────────────────────────────────────────

    private sealed class SpySlaveTranslator : ISlaveOrchestrationTranslator
    {
        public FdpEventBus? Bus           { get; set; }
        public bool         PauseIntentSeen { get; private set; }

        public void Tick()
        {
            if (Bus == null) return;
            foreach (var _ in Bus.ConsumeManaged<PauseTimeIntent>())
                PauseIntentSeen = true;
        }

        public void Dispose() { }
    }

    private sealed class SpyDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    /// <summary>
    /// Minimal INetworkFactory that wires a spy ISlaveOrchestrationTranslator and
    /// returns no-op stubs for all other methods (ExCon-compatible subset).
    /// Delegates all non-overridden methods to OfflineNetworkFactory.
    /// </summary>
    private sealed class SpyTranslatorFactory : INetworkFactory
    {
        private readonly Hrot.Editor.OfflineNetworkFactory _base = new();
        private readonly SpySlaveTranslator _spy;
        public SpyTranslatorFactory(SpySlaveTranslator spy) => _spy = spy;

        public DdsParticipant?     Participant          => _base.Participant;
        public long                WorldPosDescriptorId => _base.WorldPosDescriptorId;

        // Return this (not _base) so the spy is preserved after ConfigureForNode.
        public INetworkFactory ConfigureForNode(DdsParticipant? p, int n, NodeRole r) => this;
        public INetworkFactory ConfigureForNode(HrotNodeContext c, NodeRole r, DoctrineRegistry? d = null) => this;

        // Inject the spy instead of a null-object translator.
        public ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(FdpEventBus bus, int nodeId)
        {
            _spy.Bus = bus;
            return _spy;
        }

        // Delegate everything else to _base.
        public HrotCoreOrchTranslator           CreateOrchestratorTranslators(FdpEventBus b, int n)    => _base.CreateOrchestratorTranslators(b, n);
        public IDisposable                      CreateIdAllocatorServer()                              => _base.CreateIdAllocatorServer();
        public IMasterTimeTranslators           CreateMasterTimeTranslators(FdpEventBus b, int n)      => _base.CreateMasterTimeTranslators(b, n);
        public IOrchestrationObserver           CreateOrchestrationObserver(FdpEventBus b)             => _base.CreateOrchestrationObserver(b);
        public ICommandGateway                  CreateCommandGateway()                                 => _base.CreateCommandGateway();
        public IExConEgressWriters              CreateExConEgressWriters()                             => _base.CreateExConEgressWriters();
        public ITimeControlGateway              CreateTimeControlGateway()                             => _base.CreateTimeControlGateway();
        public ISimHostMissionSender            CreateSimHostMissionSender()                           => _base.CreateSimHostMissionSender();
        public ISimHostAuxiliaryTranslators     CreateSimHostAuxiliaryTranslators()                    => _base.CreateSimHostAuxiliaryTranslators();
        public ISimHostPathfindingTranslators   CreateSimHostPathfindingTranslators()                  => _base.CreateSimHostPathfindingTranslators();
        public ISimHostPerceptionTranslators    CreateSimHostPerceptionTranslators()                   => _base.CreateSimHostPerceptionTranslators();
        public IReadOnlyList<Fdp.Core.ComponentSystem> CreateSimHostAttributeUpdateSystems()          => _base.CreateSimHostAttributeUpdateSystems();
        public IIgTranslators                   CreateIgTranslators()                                  => _base.CreateIgTranslators();
        public IIgNetworkAdapter                CreateIgNetworkAdapter(DdsParticipant? p, long n = 0) => _base.CreateIgNetworkAdapter(p, n);
        public ICgfEntityLifecycleAdapters?     CreateCgfEntityLifecycleAdapters()                    => _base.CreateCgfEntityLifecycleAdapters();
        public Hrot.Common.Abstractions.IReplicationModule CreateReplicationModule()               => _base.CreateReplicationModule();

        public IReadOnlyList<IDescriptorTranslator> CreateIgEgressTranslators(
            DdsParticipant participant, FdpEventBus bus, IGeographicTransform geoTransform, long nodeId)
            => _base.CreateIgEgressTranslators(participant, bus, geoTransform, nodeId);

        public IEnumerable<IIngressHandler> CreateExConIngressHandlers(
            DdsParticipant?                  participant,
            long                             localNodeId,
            IDerRepo                         repo,
            Action<MapClickEventDto>         onMapClick,
            Action<SelectionChangedEventDto> onSelectionChanged,
            Action<EntityLifecycleAckDto>    onEntityLifecycleAck,
            Action<MapCommandAckDto>         onMapCommandAck)
            => _base.CreateExConIngressHandlers(participant, localNodeId, repo, onMapClick, onSelectionChanged, onEntityLifecycleAck, onMapCommandAck);
    }

    /// <summary>
    /// Minimal INetworkFactory that returns a spy IDisposable from CreateIdAllocatorServer()
    /// and delegates all other methods to OfflineNetworkFactory.
    /// </summary>
    private sealed class SpyDisposableFactory : INetworkFactory
    {
        private readonly Hrot.Editor.OfflineNetworkFactory _base = new();
        private readonly SpyDisposable _spy;
        public SpyDisposableFactory(SpyDisposable spy) => _spy = spy;

        public DdsParticipant?     Participant          => _base.Participant;
        public long                WorldPosDescriptorId => _base.WorldPosDescriptorId;

        public INetworkFactory ConfigureForNode(DdsParticipant? p, int n, NodeRole r) => this;
        public INetworkFactory ConfigureForNode(HrotNodeContext c, NodeRole r, DoctrineRegistry? d = null) => this;

        // Return spy instead of NullDisposable.
        public IDisposable CreateIdAllocatorServer() => _spy;

        // Delegate everything else to _base.
        public HrotCoreOrchTranslator           CreateOrchestratorTranslators(FdpEventBus b, int n)    => _base.CreateOrchestratorTranslators(b, n);
        public IMasterTimeTranslators           CreateMasterTimeTranslators(FdpEventBus b, int n)      => _base.CreateMasterTimeTranslators(b, n);
        public ISlaveOrchestrationTranslator    CreateSlaveOrchestratorTranslators(FdpEventBus b, int n) => _base.CreateSlaveOrchestratorTranslators(b, n);
        public IOrchestrationObserver           CreateOrchestrationObserver(FdpEventBus b)             => _base.CreateOrchestrationObserver(b);
        public ICommandGateway                  CreateCommandGateway()                                 => _base.CreateCommandGateway();
        public IExConEgressWriters              CreateExConEgressWriters()                             => _base.CreateExConEgressWriters();
        public ITimeControlGateway              CreateTimeControlGateway()                             => _base.CreateTimeControlGateway();
        public ISimHostMissionSender            CreateSimHostMissionSender()                           => _base.CreateSimHostMissionSender();
        public ISimHostAuxiliaryTranslators     CreateSimHostAuxiliaryTranslators()                    => _base.CreateSimHostAuxiliaryTranslators();
        public ISimHostPathfindingTranslators   CreateSimHostPathfindingTranslators()                  => _base.CreateSimHostPathfindingTranslators();
        public ISimHostPerceptionTranslators    CreateSimHostPerceptionTranslators()                   => _base.CreateSimHostPerceptionTranslators();
        public IReadOnlyList<Fdp.Core.ComponentSystem> CreateSimHostAttributeUpdateSystems()          => _base.CreateSimHostAttributeUpdateSystems();
        public IIgTranslators                   CreateIgTranslators()                                  => _base.CreateIgTranslators();
        public IIgNetworkAdapter                CreateIgNetworkAdapter(DdsParticipant? p, long n = 0) => _base.CreateIgNetworkAdapter(p, n);
        public ICgfEntityLifecycleAdapters?     CreateCgfEntityLifecycleAdapters()                    => _base.CreateCgfEntityLifecycleAdapters();
        public Hrot.Common.Abstractions.IReplicationModule CreateReplicationModule()               => _base.CreateReplicationModule();

        public IReadOnlyList<IDescriptorTranslator> CreateIgEgressTranslators(
            DdsParticipant participant, FdpEventBus bus, IGeographicTransform geoTransform, long nodeId)
            => _base.CreateIgEgressTranslators(participant, bus, geoTransform, nodeId);

        public IEnumerable<IIngressHandler> CreateExConIngressHandlers(
            DdsParticipant?                  participant,
            long                             localNodeId,
            IDerRepo                         repo,
            Action<MapClickEventDto>         onMapClick,
            Action<SelectionChangedEventDto> onSelectionChanged,
            Action<EntityLifecycleAckDto>    onEntityLifecycleAck,
            Action<MapCommandAckDto>         onMapCommandAck)
            => _base.CreateExConIngressHandlers(participant, localNodeId, repo, onMapClick, onSelectionChanged, onEntityLifecycleAck, onMapCommandAck);
    }
}
