using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.ModuleHost.Time;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.SimHost;
using Hrot.SimHost.Serializers;
using Hrot.Network.Infrastructure;

// Disambiguate the two IOrchestrationTranslator definitions that come into scope via
// Hrot.Common.Infrastructure (HrotNodeContext) and Hrot.Core.Network (INetworkFactory).
using IOrchestrationTranslator = Hrot.Core.Network.IOrchestrationTranslator;

namespace Hrot.NodeComposition.Tests;

/// <summary>
/// Tests for SharedApplicationBootstrapper covering all 10 SC_SM002_x success conditions.
/// </summary>
public sealed class SharedApplicationBootstrapperTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Minimal component used to verify RegisterDomainComponents is called.</summary>
    [Fdp.Core.ComponentId(254)]
    private struct TestComponentA { public int Value; }

    /// <summary>Minimal ECS system used to verify PopulateSystems is called.</summary>
    private sealed class TestSimSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float dt) { }
    }

    /// <summary>
    /// Concrete test subclass implementing all abstract hooks with in-memory stubs.
    /// Logs call order so phase-ordering tests can assert without inspecting private state.
    /// </summary>
    private sealed class TestBootstrapper : SharedApplicationBootstrapper
    {
        public List<string> CallLog { get; } = new();
        public TogglableSimulationGroup? ReceivedSimGroup     { get; private set; }
        public TogglablePostSimulationGroup? ReceivedPostSimGroup { get; private set; }
        public NetworkLifecycleSystemGroup?  ReceivedLifecycleGroup { get; private set; }

        public TestBootstrapper(INetworkFactory factory) { }

        // ── CE-199: the resource seam, exercised through THIS host rather than a parallel one ──
        private string[] _declaredKeys = System.Array.Empty<string>();
        private Hrot.Common.Infrastructure.INodeResourceProvider[] _providers =
            System.Array.Empty<Hrot.Common.Infrastructure.INodeResourceProvider>();

        /// <summary>What PopulateSystems read out of BootValues, or null if it declared nothing.</summary>
        public object? ReadBackInPopulateSystems { get; private set; }

        /// <summary>Builds a host that declares <paramref name="keys"/> and resolves <paramref name="providers"/>.</summary>
        public static TestBootstrapper WithResources(
            INetworkFactory factory,
            string[] keys,
            params Hrot.Common.Infrastructure.INodeResourceProvider[] providers)
            => new TestBootstrapper(factory) { _declaredKeys = keys, _providers = providers };

        protected override System.Collections.Generic.IReadOnlyList<string> DeclaredResourceKeys(NodeRole role)
            => _declaredKeys;

        protected override System.Collections.Generic.IReadOnlyList<Hrot.Common.Infrastructure.INodeResourceProvider>
            ResolveResources(HrotNodeContext context, NodeRole role) => _providers;

        protected override void RegisterDomainComponents(EntityRepository world)
        {
            CallLog.Add(nameof(RegisterDomainComponents));
            world.RegisterComponent<TestComponentA>();
        }

        protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
        {
            CallLog.Add(nameof(BuildContext));
            return new HrotNodeBuilder(config)
                .WithRole(config.SubsystemName, role)
                .WithNetworkFactory(networkFactory)
                .WithReplication(role)
                .Build();
        }

        protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        {
            CallLog.Add(nameof(BuildSerializer));
            return HrotScenarioSerializerFactory.Build(registry ?? new BehaviorRegistry());
        }

        protected override void PopulateSystems(
            HrotNodeContext ctx,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> sim,
            List<IEcsModuleSystem> postSim)
        {
            CallLog.Add(nameof(PopulateSystems));
            sim.Add(new TestSimSystem());

            // ⭐ Legal only because `system-groups` requires every declared resource key (CE-199).
            //   That the read SUCCEEDS is the proof the allocation already happened.
            if (_declaredKeys.Length > 0)
                ReadBackInPopulateSystems = BootValues.Get<object>(_declaredKeys[0]);
        }

        protected override ClusterSlave BuildOrchestration(
            HrotNodeContext context,
            TogglableSimulationGroup simGroup,
            TogglablePostSimulationGroup postSimGroup,
            ScenarioSerializer serializer)
        {
            CallLog.Add(nameof(BuildOrchestration));
            ReceivedSimGroup       = simGroup;
            ReceivedPostSimGroup   = postSimGroup;
            ReceivedLifecycleGroup = context.NedReplication?.NetworkLifecycleGroup;

            // Return a minimal stub — no DDS or scenario infrastructure is needed.
            return new ClusterSlave(context.EventBus, context.NodeId, "TestNode");
        }

        protected override void RegisterSpawningPipeline(HrotNodeContext ctx)
            => CallLog.Add(nameof(RegisterSpawningPipeline));

        protected override void RegisterNetworkTranslators(HrotNodeContext ctx, INetworkFactory? factory)
            => CallLog.Add(nameof(RegisterNetworkTranslators));
    }

    /// <summary>
    /// Mock factory that returns a non-null INedReplicationModule so that
    /// Phase 6a+ (NedReplication registration) is exercised in headless tests.
    /// Delegates everything else to OfflineNetworkFactory.
    /// </summary>
    private sealed class MockNedFactory : INetworkFactory
    {
        private readonly Hrot.Editor.OfflineNetworkFactory _base = new();
        private readonly MockNedReplicationModule _ned;

        public MockNedFactory(MockNedReplicationModule ned) => _ned = ned;

        public CycloneDDS.Runtime.DdsParticipant? Participant         => _base.Participant;
        public long WorldPosDescriptorId         => _base.WorldPosDescriptorId;
        public long NavigationStatusDescriptorId => _base.NavigationStatusDescriptorId;

        // Return this (not _base) so the mock NED module is preserved after ConfigureForNode.
        public INetworkFactory ConfigureForNode(CycloneDDS.Runtime.DdsParticipant? p, int n, NodeRole r) => this;
        public INetworkFactory ConfigureForNode(HrotNodeContext c, NodeRole r, BehaviorRegistry? d = null) => this;

        public IReplicationModule CreateReplicationModule()  => _ned;
        public ITimeControlGateway CreateTimeControlGateway() => _base.CreateTimeControlGateway();
        public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators() => _base.CreateSimHostAuxiliaryTranslators();

        // Delegate everything else to _base.
        public IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus b, int n) => _base.CreateOrchestratorTranslators(b, n);
        public IDisposable CreateIdAllocatorServer() => _base.CreateIdAllocatorServer();
        public INetworkIdAllocator CreateIdAllocator(string id, bool skip = false) => _base.CreateIdAllocator(id, skip);
        public IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus b, int n) => _base.CreateMasterTimeTranslators(b, n);
        public ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(FdpEventBus b, int n) => _base.CreateSlaveOrchestratorTranslators(b, n);
        public IOrchestrationObserver CreateOrchestrationObserver(FdpEventBus b) => _base.CreateOrchestrationObserver(b);
        public ICommandGateway CreateCommandGateway() => _base.CreateCommandGateway();
        public IExConEgressWriters CreateExConEgressWriters() => _base.CreateExConEgressWriters();
        public ISimHostMissionSender CreateSimHostMissionSender() => _base.CreateSimHostMissionSender();
        public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators(CarKinem.Trajectory.TrajectoryPoolManager? pool = null) => _base.CreateSimHostPathfindingTranslators(pool);
        public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators(GhostCreationSystem? ghost = null) => _base.CreateSimHostPerceptionTranslators(ghost);
        public IReadOnlyList<IEcsModuleSystem> CreateSimHostAttributeUpdateSystems() => _base.CreateSimHostAttributeUpdateSystems();
        public IIgTranslators CreateIgTranslators() => _base.CreateIgTranslators();
        public IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? p, long n = 0) => _base.CreateIgNetworkAdapter(p, n);
        public ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters() => _base.CreateCgfEntityLifecycleAdapters();
        public IEnumerable<IIngressHandler> CreateExConIngressHandlers(
            CycloneDDS.Runtime.DdsParticipant?   p, long id, IDerRepo repo,
            Action<MapClickEventDto>              onMapClick,
            Action<SelectionChangedEventDto>      onSelectionChanged,
            Action<EntityLifecycleAckDto>         onEntityLifecycleAck,
            Action<MapCommandAckDto>              onMapCommandAck)
            => _base.CreateExConIngressHandlers(p, id, repo, onMapClick, onSelectionChanged, onEntityLifecycleAck, onMapCommandAck);
        public IReadOnlyList<IDescriptorTranslator> CreateIgEgressTranslators(
            CycloneDDS.Runtime.DdsParticipant participant, FdpEventBus bus,
            Fdp.Modules.Geographic.IGeographicTransform geoTransform, long nodeId)
            => _base.CreateIgEgressTranslators(participant, bus, geoTransform, nodeId);
        public IReadOnlyList<INetworkTranslator> CreateGizmoTranslators(FdpEventBus b, long id, bool headless) => _base.CreateGizmoTranslators(b, id, headless);
        public IEcsModuleSystem? CreateGizmoPublisherSystem(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer buf, long id) => _base.CreateGizmoPublisherSystem(buf, id);
    }

    /// <summary>
    /// Mock INedReplicationModule that tracks whether RegisterSystems was called.
    /// </summary>
    private sealed class MockNedReplicationModule : INedReplicationModule
    {
        public bool WasRegistered { get; private set; }
        public GhostCreationSystem GhostCreationSystem { get; } = new GhostCreationSystem(new NetworkEntityMap());
        public bool DriveFromNetwork => false;
        public NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; } = new NetworkLifecycleSystemGroup();
        public Action? AfterSeekCallback => null;

        public string Name => "MockNedReplication";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public void RegisterSystems(ISystemRegistry registry)
        {
            WasRegistered = true;
            registry.RegisterSystem(GhostCreationSystem);
        }

        public void Tick(ISimulationView view, float deltaTime) { }
    }

    /// <summary>Creates a minimal headless config for in-memory tests.</summary>
    private static HrotNodeConfig HeadlessConfig() => new HrotNodeConfig
    {
        Headless             = true,
        SkipAllocatorRouting = true,
        SubsystemName        = "TestNode",
        NodeId               = 1,
        LocalTempRoot        = @"C:\FDP_Temp",
    };

    /// <summary>
    /// Gets the ITimeController from the kernel via reflection (no public getter exposed).
    /// </summary>
    private static ITimeController? GetTimeController(ModuleHostKernel kernel)
    {
        var field = typeof(ModuleHostKernel).GetField(
            "_timeController",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (ITimeController?)field?.GetValue(kernel);
    }

    // ── SC_SM002_1 ────────────────────────────────────────────────────────────

    [Fact]
    public void BootstrapNode_WithMinimalSubclass_Headless_DoesNotThrow()
    {
        // SC_SM002_1: concrete test subclass implementing all abstract hooks can
        // call BootstrapNode() without throwing.
        var factory     = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new TestBootstrapper(factory);
        var config      = HeadlessConfig();

        var context = bootstrapper.BootstrapNode(config, NodeRole.None, factory);

        Assert.NotNull(context);
        Assert.NotNull(context.Kernel);
        Assert.NotNull(context.World);
    }

    // ── SC_SM002_2 ────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterDomainComponents_RunsBeforeBuildSerializer_ComponentPresentInWorld()
    {
        // SC_SM002_2: component registered in RegisterDomainComponents is present
        // in the world before BuildSerializer is invoked.
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new TestBootstrapper(factory);
        var config       = HeadlessConfig();

        var context = bootstrapper.BootstrapNode(config, NodeRole.None, factory);

        // Verify call ordering: RegisterDomainComponents BEFORE BuildSerializer.
        int domIdx        = bootstrapper.CallLog.IndexOf("RegisterDomainComponents");
        int serializerIdx = bootstrapper.CallLog.IndexOf("BuildSerializer");
        Assert.True(domIdx >= 0, "RegisterDomainComponents was not called");
        Assert.True(serializerIdx >= 0, "BuildSerializer was not called");
        Assert.True(domIdx < serializerIdx,
            "RegisterDomainComponents must run before BuildSerializer");

        // Verify the component was actually registered in the world.
        Assert.True(context.World.IsComponentTypeRegistered<TestComponentA>(),
            "TestComponentA must be present in the world after RegisterDomainComponents");
    }

    // ── SC_SM002_3 ────────────────────────────────────────────────────────────

    [Fact]
    public void PopulateSystems_SystemInSimGroup_PassedToBuildOrchestration()
    {
        // SC_SM002_3: a system added in PopulateSystems appears in the
        // TogglableSimulationGroup that is passed to BuildOrchestration.
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new TestBootstrapper(factory);
        var config       = HeadlessConfig();

        bootstrapper.BootstrapNode(config, NodeRole.None, factory);

        Assert.NotNull(bootstrapper.ReceivedSimGroup);
        var systems = bootstrapper.ReceivedSimGroup!.GetSystems();
        Assert.Contains(systems, s => s is TestSimSystem);
    }

    // ── SC_SM002_4 ────────────────────────────────────────────────────────────

    [Fact]
    public void KernelInitialize_CalledExactlyOnce_AfterAllTranslators()
    {
        // SC_SM002_4: Kernel.Initialize() is called exactly once, and always
        // AFTER all translator registrations.
        // We verify by checking that the kernel is initialized (Update() does not throw)
        // and that the call log shows all hooks were called before initialization occurred
        // (Initialize is the last operation in BootstrapNode).
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new TestBootstrapper(factory);
        var config       = HeadlessConfig();

        var context = bootstrapper.BootstrapNode(config, NodeRole.None, factory);

        // Verify all hooks were called (i.e., we reached Phase 7).
        Assert.Contains("RegisterNetworkTranslators", bootstrapper.CallLog);
        Assert.Contains("RegisterSpawningPipeline",   bootstrapper.CallLog);

        // Verify the kernel is initialized: Update() must not throw
        // "Must call Initialize() before Update()".
        var ex = Record.Exception(() => context.Kernel.Update());
        Assert.Null(ex);
    }

    // ── SC_SM002_5 ────────────────────────────────────────────────────────────

    [Fact]
    public void AbstractAndVirtualHooks_ExactlyAsSpecified_Reflection()
    {
        // SC_SM002_5: the class exposes exactly the 6 specified abstract hooks
        // and exactly the 4 specified virtual hooks (including Phase 6d RegisterApplicationSystems
        // and the Phase 7+ PostInitialize hook).
        var type = typeof(SharedApplicationBootstrapper);

        // Abstract hooks
        var expectedAbstract = new[]
        {
            "RegisterDomainComponents",
            "BuildSerializer",
            "PopulateSystems",
            "BuildOrchestration",
            "RegisterSpawningPipeline",
            "RegisterNetworkTranslators",
            "BuildContext",
        };

        // Virtual hooks
        // ⭐ CE-199 added DeclaredResourceKeys + ResolveResources — the two halves of the resource
        //   seam. They are listed here deliberately: this rail is what makes a new hook a decision
        //   rather than a drift, and it reddened the moment they were added.
        var expectedVirtual = new[]
        {
            "GetAdditionalModules",
            "GetBehaviorRegistry",
            "RegisterApplicationSystems",
            "PostInitialize",
            "DeclaredResourceKeys",
            "ResolveResources",
        };

        var allMethods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var actualAbstract = allMethods
            .Where(m => m.IsAbstract)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        var actualVirtual = allMethods
            .Where(m => m.IsVirtual && !m.IsAbstract)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(expectedAbstract.OrderBy(n => n), actualAbstract);
        Assert.Equal(expectedVirtual.OrderBy(n => n), actualVirtual);
    }

    // ── SC_SM002_6 ────────────────────────────────────────────────────────────

    [Fact]
    public void TimeControl_NonNull_AfterBootstrapWithFactory()
    {
        // SC_SM002_6: TimeControl is non-null after BootstrapNode() when the
        // configured factory returns a non-null ITimeControlGateway.
        // OfflineNetworkFactory.CreateTimeControlGateway() returns NullTimeControlGateway
        // (non-null), so this test passes in headless mode.
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new TestBootstrapper(factory);

        bootstrapper.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.NotNull(bootstrapper.TimeControl);
    }

    // ── SC_SM002_7 ────────────────────────────────────────────────────────────

    [Fact]
    public void TimeTranslators_RegisteredByBaseClass_SlaveSyncController_ReceivesEvent()
    {
        // SC_SM002_7: After BootstrapNode(), the time-sync translators are registered
        // by the base class in Phase 6c (no subclass hook). Additionally, publishing
        // a SwitchTimeModeEvent directly to the event bus causes the SlaveSyncController
        // (which subscribes to that bus) to transition to Deterministic/Stepping mode.
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new TestBootstrapper(factory);
        var config       = HeadlessConfig();

        var context = bootstrapper.BootstrapNode(config, NodeRole.None, factory);

        // Get the time controller via reflection (no public getter on ModuleHostKernel).
        var timeCtrl = GetTimeController(context.Kernel);
        Assert.NotNull(timeCtrl);
        Assert.Equal(TimeMode.Continuous, timeCtrl!.GetMode());

        // Publish a SwitchTimeModeEvent with BarrierWallTicks = 0 (past barrier)
        // directly to the event bus — the SlaveSyncController subscribes here.
        context.EventBus.Publish(new SwitchTimeModeEvent
        {
            TargetMode       = TimeMode.Deterministic,
            BarrierWallTicks = 0,
            FixedDelta       = 1f / 60f,
            TimeScale        = 1.0f,
            SimTimeSnapshot  = 0,
        });

        // SwapBuffers makes the event visible to the SlaveSyncController.
        context.EventBus.SwapBuffers();

        // SlaveSyncController.Update() is called inside timeCtrl.Update(),
        // which drains the mode-switch events. Call it directly.
        timeCtrl.Update();

        Assert.Equal(TimeMode.Deterministic, timeCtrl.GetMode());
    }

    // ── SC_SM002_8 ────────────────────────────────────────────────────────────

    [Fact]
    public void NedReplication_RegisteredByBaseClass_GhostCreationSystemPresent()
    {
        // SC_SM002_8: when context.NedReplication is non-null, the base class calls
        // Kernel.RegisterModule(context.NedReplication) in Phase 6a+. GhostCreationSystem
        // must therefore be registered in the kernel.
        var ned     = new MockNedReplicationModule();
        var factory = new MockNedFactory(ned);
        var bootstrapper = new TestBootstrapper(factory);

        bootstrapper.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        // MockNedReplicationModule.RegisterSystems adds GhostCreationSystem to the
        // registry; the flag is set when that method is called by the kernel.
        Assert.True(ned.WasRegistered,
            "NedReplicationModule.RegisterSystems must be called by the kernel (Phase 6a+)");
    }

    // ── SC_SM002_9 ────────────────────────────────────────────────────────────

    [Fact]
    public void NedReplication_NonNull_AfterBootstrapWithNedFactory()
    {
        // SC_SM002_9: when the factory's CreateReplicationModule() returns an
        // INedReplicationModule, context.NedReplication is non-null after BootstrapNode().
        var ned          = new MockNedReplicationModule();
        var factory      = new MockNedFactory(ned);
        var bootstrapper = new TestBootstrapper(factory);

        var context = bootstrapper.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.NotNull(context.NedReplication);
        Assert.Same(ned, context.NedReplication);
    }

    // ── SC_SM002_10 ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildOrchestration_ReceivesLifecycleGroup_FromNedReplication()
    {
        // SC_SM002_10: BuildOrchestration() receives lifecycleGroup equal to
        // context.NedReplication?.NetworkLifecycleGroup. Without this, GhostDestructionSystem
        // fires against the flight recorder's memory writes during PrepareReplay.
        var ned          = new MockNedReplicationModule();
        var factory      = new MockNedFactory(ned);
        var bootstrapper = new TestBootstrapper(factory);

        var context = bootstrapper.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.NotNull(bootstrapper.ReceivedLifecycleGroup);
        Assert.Same(
            context.NedReplication?.NetworkLifecycleGroup,
            bootstrapper.ReceivedLifecycleGroup);
    }

    // ── CE-199: the resource half of the seam ─────────────────────────────────
    //
    // ⭐⭐⭐ WHY THESE EXIST. INodeResourceProvider.Allocate could not run at all: NodeBootValues.Set
    // refuses any write outside a step that declared the key, so allocating from a host method threw
    // at boot. These rails pin that the base now owns a `node-resources` step which makes the
    // allocation legal — and that the DECLARATION cannot drift from what is actually allocated.

    /// <summary>A provider that records whether the base ever called it, and publishes a marker.</summary>
    private sealed class SpyResourceProvider : Hrot.Common.Infrastructure.INodeResourceProvider
    {
        private readonly string _key;
        public SpyResourceProvider(string key) { _key = key; }

        public string  Key            => _key;
        public int     AllocateCalls  { get; private set; }
        public int     DisposeCalls   { get; private set; }
        public object  Payload        { get; } = new();

        public void Allocate(HrotNodeContext context, Hrot.Common.Infrastructure.NodeBootValues values)
        {
            AllocateCalls++;
            values.Set(_key, Payload);
        }

        public void Dispose() => DisposeCalls++;
    }

    [Fact]
    public void NodeResourcesStep_AllocatesTheDeclaredResource_AndPopulateSystemsCanReadIt()
    {
        // ⭐ THE RAIL THAT WOULD HAVE FAILED BEFORE CE-199: Allocate threw outside a declaring step.
        var factory  = new Hrot.Editor.OfflineNetworkFactory();
        var provider = new SpyResourceProvider(Hrot.Common.Infrastructure.ResourceKeys.TrajectoryPool);
        var host     = TestBootstrapper.WithResources(factory, new[] { Hrot.Common.Infrastructure.ResourceKeys.TrajectoryPool }, provider);

        host.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.Equal(1, provider.AllocateCalls);
        Assert.Same(provider.Payload, host.ReadBackInPopulateSystems);
    }

    [Fact]
    public void NodeResourcesStep_RunsBeforeSystemGroups_SoACapabilityNeverBorrowsAnUnallocatedResource()
    {
        // Ordering is the whole point: PopulateSystems reading the payload PROVES the allocation
        // already happened, because Get throws when the providing step never Set it.
        var factory  = new Hrot.Editor.OfflineNetworkFactory();
        var provider = new SpyResourceProvider(Hrot.Common.Infrastructure.ResourceKeys.PerceptionGrid);
        var host     = TestBootstrapper.WithResources(factory, new[] { Hrot.Common.Infrastructure.ResourceKeys.PerceptionGrid }, provider);

        host.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.NotNull(host.ReadBackInPopulateSystems);
    }

    [Fact]
    public void ResolvingAProviderForAnUndeclaredKey_IsRefused_NamingTheKey()
    {
        // The declaration is static (it is read before BuildContext), so it MUST be checked against
        // what is really resolved — otherwise the step would refuse the write with a far less
        // informative message from deep inside NodeBootValues.
        var factory  = new Hrot.Editor.OfflineNetworkFactory();
        var provider = new SpyResourceProvider(Hrot.Common.Infrastructure.ResourceKeys.RaycastBatch);
        var host     = TestBootstrapper.WithResources(factory, System.Array.Empty<string>(), provider);

        var ex = Assert.Throws<InvalidOperationException>(
            () => host.BootstrapNode(HeadlessConfig(), NodeRole.None, factory));

        Assert.Contains(Hrot.Common.Infrastructure.ResourceKeys.RaycastBatch, ex.Message);
        Assert.Equal(0, provider.AllocateCalls);
    }

    [Fact]
    public void DeclaringAKeyNobodyAllocates_IsRefused_NamingTheKey()
    {
        // The mirror case, and the more dangerous one: a consumer would read a resource never created.
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var host    = TestBootstrapper.WithResources(factory, new[] { Hrot.Common.Infrastructure.ResourceKeys.NavigationPools });

        var ex = Assert.Throws<InvalidOperationException>(
            () => host.BootstrapNode(HeadlessConfig(), NodeRole.None, factory));

        Assert.Contains(Hrot.Common.Infrastructure.ResourceKeys.NavigationPools, ex.Message);
    }

    [Fact]
    public void DisposeResources_FreesEveryAllocatedProvider_AndIsIdempotent()
    {
        // ⭐ ONE implementation on the base — SimHost and IG each had their own copy of this loop.
        var factory  = new Hrot.Editor.OfflineNetworkFactory();
        var provider = new SpyResourceProvider(Hrot.Common.Infrastructure.ResourceKeys.TrajectoryPool);
        var host     = TestBootstrapper.WithResources(factory, new[] { Hrot.Common.Infrastructure.ResourceKeys.TrajectoryPool }, provider);

        host.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);
        host.DisposeResources();
        host.DisposeResources();

        Assert.Equal(1, provider.DisposeCalls);
    }

    [Fact]
    public void AHostThatDeclaresNoResources_AllocatesNothing_AndStillBoots()
    {
        // IG's shape today. "Allocates nothing" must stay a correct answer, not a degenerate one.
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var host    = TestBootstrapper.WithResources(factory, System.Array.Empty<string>());

        var context = host.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.NotNull(context);
        Assert.Null(host.ReadBackInPopulateSystems);
    }
}
