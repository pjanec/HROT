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
    [TickMode(TickPolicy.EveryFrame)]
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
            => new TestBootstrapper(factory)
            {
                _declaredKeys = keys, _providers = providers,
                // ⭐ CE-4013 D10 — a resource is declared because a capability needs it (the base checks the two agree).
                TestCapabilities = keys.Length == 0 ? Array.Empty<INodeCapability>() : new INodeCapability[] { new BorrowingCapability(keys) },
            };

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
                .WithTimeRole(IgnoreTimeRoleWhenBuilding ? Fdp.Toolkit.Time.Controllers.TimeRole.Slave : TimeRole)   // ⭐ CE-1446 B0 — what a subclass does via BuildReplicatedNode(…, timeRole)
                .WithNetworkFactory(networkFactory)
                .WithReplication(role)
                .Build();
        }

        // ⭐ CE-1446 B0 — the two new base inputs, settable per test.
        public Fdp.Toolkit.Time.Controllers.TimeRole DeclaredTimeRole { get; init; } = Fdp.Toolkit.Time.Controllers.TimeRole.Slave;
        public bool DeclaredBuildsPostSimGroup { get; init; } = true;
        /// <summary>Models a subclass that declares a TimeRole but forgets to build the node with it.</summary>
        public bool IgnoreTimeRoleWhenBuilding { get; init; }
        protected override Fdp.Toolkit.Time.Controllers.TimeRole TimeRole => DeclaredTimeRole;
        protected override bool BuildsPostSimGroup => DeclaredBuildsPostSimGroup;

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
        {
            CallLog.Add(nameof(RegisterSpawningPipeline));
            if (!SkipRegisterCapabilities) RegisterCapabilities(ctx);
        }

        /// <summary>⭐ CE-4013 — the capabilities the base walks.</summary>
        public IReadOnlyList<INodeCapability> TestCapabilities { get; init; } = Array.Empty<INodeCapability>();

        /// <summary>⭐ CE-4013 — models a subclass that forgets the Register phase.</summary>
        public bool SkipRegisterCapabilities { get; init; }

        protected override IReadOnlyList<INodeCapability> ResolveCapabilities(HrotNodeContext context, NodeRole role) => TestCapabilities;

        protected override void RegisterNetworkTranslators(HrotNodeContext ctx, INetworkFactory? factory)
            => CallLog.Add(nameof(RegisterNetworkTranslators));
    }

    /// <summary>⭐ CE-4013 — a capability contributing through ALL THREE hooks, recording which ran.</summary>
    private sealed class AllHooksCapability : INodeCapability
    {
        public List<string> Calls { get; } = new();
        public TestSimSystem System { get; } = new();
        public string Key => "cap:test-all-hooks";
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();
        public void PopulateSystems(HrotNodeContext c, List<IEcsModuleSystem> i, List<IEcsModuleSystem> s, List<IEcsModuleSystem> p)
        { Calls.Add(nameof(PopulateSystems)); s.Add(System); }
        public IEnumerable<IEcsModule> ProvideModules() { Calls.Add(nameof(ProvideModules)); return Array.Empty<IEcsModule>(); }
        public void Register(HrotNodeContext c, NodeBootValues v) => Calls.Add(nameof(Register));
    }

    /// <summary>⭐ CE-1446 B0 — a capability that contributes a POST-simulation system.</summary>
    private sealed class PostSimCapability : INodeCapability
    {
        public string Key => "cap:test-post-sim";
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();
        public void PopulateSystems(HrotNodeContext c, List<IEcsModuleSystem> i, List<IEcsModuleSystem> s, List<IEcsModuleSystem> p) => p.Add(new TestSimSystem());
    }

    /// <summary>⭐ CE-1446 B0 — a declared TimeRole reaches the node's context and its clock (the editor's Standalone is a master-type controller); the default stays Slave — through the ONE build path
    /// every subclass uses, <c>BuildReplicatedNode(…, timeRole)</c>. 🔴 Red-proof: <c>BuildReplicatedNode</c> dropping its <c>.WithTimeRole(timeRole)</c> ⇒ the Standalone row reads Slave.</summary>
    [Fact]
    public void CE1446_ADeclaredTimeRole_ReachesTheContext_AndTheDefaultIsSlave()
    {
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var standalone = new TestBootstrapper(factory) { DeclaredTimeRole = Fdp.Toolkit.Time.Controllers.TimeRole.Standalone }
            .BootstrapNode(HeadlessConfig(), NodeRole.None, factory);
        var def = new TestBootstrapper(factory).BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.Equal(Fdp.Toolkit.Time.Controllers.TimeRole.Standalone, standalone.TimeRole);
        Assert.IsType<Fdp.Toolkit.Time.Controllers.MasterSyncController>(standalone.TimeController);
        Assert.Equal(Fdp.Toolkit.Time.Controllers.TimeRole.Slave, def.TimeRole);
        Assert.IsNotType<Fdp.Toolkit.Time.Controllers.MasterSyncController>(def.TimeController);

        var viaTheSharedBuilder = HrotNodeBuilderReplicationExtensions.BuildReplicatedNode(
            HeadlessConfig(), NodeRole.None, factory, null, Fdp.Toolkit.Time.Controllers.TimeRole.Standalone);
        Assert.Equal(Fdp.Toolkit.Time.Controllers.TimeRole.Standalone, viaTheSharedBuilder.TimeRole);
        Assert.IsType<Fdp.Toolkit.Time.Controllers.MasterSyncController>(viaTheSharedBuilder.TimeController);
    }

    /// <summary>⭐ CE-1446 B0 — a subclass that declares a TimeRole but built the node with another fails AT BOOT, naming it (no silent time-authority split).</summary>
    [Fact]
    public void CE1446_ATimeRoleTheNodeWasNotBuiltWith_FailsAtBoot()
    {
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var sut = new TestBootstrapper(factory) { DeclaredTimeRole = Fdp.Toolkit.Time.Controllers.TimeRole.Standalone, IgnoreTimeRoleWhenBuilding = true };

        var ex = Assert.Throws<InvalidOperationException>(() => sut.BootstrapNode(HeadlessConfig(), NodeRole.None, factory));
        Assert.Contains("declares TimeRole", ex.ToString());
    }

    private static bool KernelHasPostSimGroup(HrotNodeContext c) => c.Kernel.SystemScheduler.GetAllSystems().Any(x => x is TogglablePostSimulationGroup);

    /// <summary>⭐ CE-1446 B0 — BuildsPostSimGroup=false registers NO post-simulation group (the orchestration still receives a placeholder, never registered); true (default) registers one.
    /// 🔴 Red-proof: the `if (BuildsPostSimGroup)` guard around the group's registration removed ⇒ the false case finds one.</summary>
    [Fact]
    public void CE1446_BuildsPostSimGroupFalse_RegistersNoPostSimGroup_TheDefaultDoes()
    {
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var off = new TestBootstrapper(factory) { DeclaredBuildsPostSimGroup = false };
        var offCtx = off.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);
        var onCtx = new TestBootstrapper(factory).BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.False(KernelHasPostSimGroup(offCtx));
        Assert.NotNull(off.ReceivedPostSimGroup);
        Assert.True(KernelHasPostSimGroup(onCtx));
    }

    /// <summary>⭐ CE-1446 B0 — with no post-sim group, a capability contributing post-sim systems throws a clear message instead of being dropped; with the group (default) it is fine.
    /// 🔴 Red-proof: the throw removed ⇒ the false case boots and the systems vanish silently.</summary>
    [Fact]
    public void CE1446_APostSimCapability_OnAHostWithoutThatGroup_Throws_ClearlyOnlyThen()
    {
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var caps = new INodeCapability[] { new PostSimCapability() };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new TestBootstrapper(factory) { DeclaredBuildsPostSimGroup = false, TestCapabilities = caps }.BootstrapNode(HeadlessConfig(), NodeRole.None, factory));
        Assert.Contains("post-simulation", ex.Message);
        Assert.Contains("BuildsPostSimGroup", ex.ToString());

        new TestBootstrapper(factory) { TestCapabilities = caps }.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);   // default: no throw
    }

    /// <summary>⭐⭐ CE-4013 — the base asks every hook, in its step order, on every subclass: no host can drop one.</summary>
    // ── CE-1446 B3: no ECS host builds its node inline ───────────────────────────────────────

    /// <summary>The ECS hosts and the file each one calls <c>BootstrapNode</c> from, plus the bootstrapper subclass it builds with. Stride targets net8.0-windows, so every host is reached by SOURCE.</summary>
    private static readonly (string Host, string EntryFile, string BootstrapperFile)[] EcsHosts =
    {
        ("CGF",     "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",                  "Hrot/Subsystems/Hrot.CGF/CgfNodeBootstrapper.cs"),
        ("SimHost", "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs",                "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs"),
        ("IG",      "Hrot/Subsystems/Hrot.IG/IgApplication.cs",                  "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs"),
        ("Editor",  "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",            "Hrot/Subsystems/Hrot.Editor/EditorNodeBootstrapper.cs"),
        ("Traffic", "Hrot/Subsystems/Hrot.TrafficNode/TrafficSubsystem.cs",      "Hrot/Subsystems/Hrot.TrafficNode/TrafficNodeBootstrapper.cs"),
        ("Stride",  "Stride/HrotStrideApp.Game/StrideNodeShell.cs",              "Hrot/Subsystems/Hrot.NodeComposition/StrideNodeBootstrapper.cs"),
    };

    private static readonly System.Text.RegularExpressions.Regex NewClusterSlave = new(@"new\s+(?:[\w.]+\.)?ClusterSlave\s*\(", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string CodeOf(string path)   // the file's code lines: comments (// and ///) dropped, so a doc mention is not a call
        => string.Join("\n", System.IO.File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>
    /// ⭐⭐ <c>CE-1446</c> B3 (R-1045) — <b>no ECS host builds its node inline.</b> Every host (CGF, SimHost, IG, the editor, the traffic node, Stride) calls <c>BootstrapNode</c> on a
    /// <c>SharedApplicationBootstrapper</c> subclass, and NO production source outside those bootstrappers and the builder's own files calls <c>new HrotNodeBuilder(</c> or
    /// <c>BuildReplicatedNode(</c>. A SOURCE scan (Stride targets net8.0-windows, out of reflection's reach); it cannot pass empty — it names the six hosts and fails if one is missing.
    /// ⚠ Stride's hand-built RUN CONTROLS (CE-1449) are not a node build — Stride's node IS built by <c>StrideNodeBootstrapper</c> (<c>StrideNodeShell.cs</c>) — so no allow-list entry is needed here.
    /// 🔴 Red-proof: a <c>new HrotNodeBuilder(…)</c> added to <c>CgfSubsystem.Initialize</c> ⇒ the first assertion names it; ⭐ CE-3348: a <c>new ClusterSlave(…)</c> in any host file (it was <c>CgfApplication.cs</c>) ⇒ the slave assertion names it.
    /// </summary>
    [Fact]
    public void CE1446_EveryEcsHostBuildsItsNodeThroughASharedBootstrapperSubclass_AndNoneInline()
    {
        string root = Fdp.Testing.TestRepo.Root;

        // ① the hosts are FOUND (a scan that finds nothing must fail) and each uses a bootstrapper subclass
        Assert.Equal(new[] { "CGF", "Editor", "IG", "SimHost", "Stride", "Traffic" }, EcsHosts.Select(h => h.Host).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        foreach (var (host, entry, bootstrapper) in EcsHosts)
        {
            string entryPath = System.IO.Path.Combine(root, entry), bootPath = System.IO.Path.Combine(root, bootstrapper);
            Assert.True(System.IO.File.Exists(entryPath), $"{host}: host file '{entry}' not found — update the rail with the host's new home.");
            Assert.True(System.IO.File.Exists(bootPath), $"{host}: bootstrapper file '{bootstrapper}' not found.");
            Assert.Contains(".BootstrapNode(", CodeOf(entryPath));
            Assert.Matches(@"class\s+\w+\s*:\s*SharedApplicationBootstrapper", CodeOf(bootPath));
        }

        // ② no production file outside the bootstrappers and the builder's own files builds a node itself
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "HrotNodeBuilder.cs", "HrotNodeBuilderReplicationExtensions.cs", "SharedApplicationBootstrapper.cs" };
        var offenders = new List<string>();
        var slaveOffenders = new List<string>();
        int scanned = 0;
        foreach (string top in new[] { "Hrot", "Stride" })
            foreach (string file in Fdp.Testing.RepoSources.CsFiles(System.IO.Path.Combine(root, top)))   // ⭐ CE-1366 — the one scan: skips obj/ bin/ .claude/ .git/
            {
                string rel = System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("Tests/") || rel.Contains(".Tests") ) continue;   // production code only
                string name = System.IO.Path.GetFileName(file);
                if (allowed.Contains(name) || name.EndsWith("NodeBootstrapper.cs", StringComparison.Ordinal)) continue;
                scanned++;
                string code = CodeOf(file);
                if (code.Contains("new HrotNodeBuilder(") || code.Contains("BuildReplicatedNode(")) offenders.Add(rel);
                // ⭐ CE-3348 — and NO host builds a ClusterSlave itself either: the node's ONE slave is made by the builder / bootstrapper, so every host
                //   gets the shared handler set (EcsHostClusterHandlers.Compose) by construction. That was the shape `CgfApplication` had (a second root with its
                //   own `new ClusterSlave(`) and the rail above missed it. ⚠ The orchestrator is not an ECS host (no world, no node): its slave is its own.
                if (NewClusterSlave.IsMatch(code) && name != "OrchestratorSubsystem.cs") slaveOffenders.Add(rel);   // ⚠ matches a qualified `new Fdp.Toolkit.Orchestration.ClusterSlave(` too — CgfApplication wrote it that way
            }

        Assert.True(scanned > 500, $"the scan saw only {scanned} production files — it is not looking where the hosts are.");
        Assert.True(offenders.Count == 0,
            "These production files build a node inline (new HrotNodeBuilder / BuildReplicatedNode) instead of through a SharedApplicationBootstrapper subclass (CE-1446 / R-1045): " + string.Join(", ", offenders));
        Assert.True(slaveOffenders.Count == 0,
            "These production files build a ClusterSlave themselves (new ClusterSlave) instead of getting the node's one slave from the builder / bootstrapper (CE-3348): " + string.Join(", ", slaveOffenders));
    }

    [Fact]
    public void CE4013_TheBaseWalksEveryCapabilityHook_InStepOrder()
    {
        var cap = new AllHooksCapability();
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var sut = new TestBootstrapper(factory) { TestCapabilities = new INodeCapability[] { cap } };

        sut.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);

        Assert.Equal(new[] { "PopulateSystems", "ProvideModules", "Register" }, cap.Calls);
    }

    /// <summary>⭐⭐ CE-4013 — a subclass whose spawning pipeline never calls RegisterCapabilities fails at boot, naming the phase.</summary>
    [Fact]
    public void CE4013_ASubclassThatSkipsRegister_FailsAtBoot_NamingIt()
    {
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var sut = new TestBootstrapper(factory) { TestCapabilities = new INodeCapability[] { new AllHooksCapability() }, SkipRegisterCapabilities = true };

        var ex = Assert.Throws<InvalidOperationException>(() => sut.BootstrapNode(HeadlessConfig(), NodeRole.None, factory));
        Assert.Contains("NodeCapabilityWalk.Register", ex.ToString());
    }

    /// <summary>A capability that borrows exactly <c>keys</c> — what makes a declared resource legitimate.</summary>
    private sealed class BorrowingCapability : INodeCapability
    {
        public BorrowingCapability(string[] keys) => Needs = keys;
        public string Key => "cap:test-borrowing";
        public IReadOnlyList<string> Needs { get; }
    }

    private sealed class NeedyCapability : INodeCapability
    {
        public string Key => "cap:test-needy";
        public IReadOnlyList<string> Needs { get; } = new[] { "res:test-undeclared" };
    }

    /// <summary>⭐ CE-4013 D10 — the base checks, for EVERY host, that its declared resources equal what its capabilities need.</summary>
    [Fact]
    public void CE4013_ACapabilityNeedingAnUndeclaredResource_FailsAtBoot()
    {
        var factory = new Hrot.Editor.OfflineNetworkFactory();
        var sut = new TestBootstrapper(factory) { TestCapabilities = new INodeCapability[] { new NeedyCapability() } };

        var ex = Assert.Throws<InvalidOperationException>(() => sut.BootstrapNode(HeadlessConfig(), NodeRole.None, factory));
        Assert.Contains("DeclaredResourceKeys must match", ex.ToString());
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

        /// <summary>S2b — what <see cref="CreateSimHostAttributeUpdateSystems"/> returns; null delegates to the base.</summary>
        public IReadOnlyList<IEcsModuleSystem>? EditHandlers { get; init; }

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
        public ICommandGateway CreateCommandGateway() => _base.CreateCommandGateway();
        public ISimHostMissionSender CreateSimHostMissionSender() => _base.CreateSimHostMissionSender();
        public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators(CarKinem.Trajectory.TrajectoryPoolManager? pool = null) => _base.CreateSimHostPathfindingTranslators(pool);
        public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators(GhostCreationSystem? ghost = null) => _base.CreateSimHostPerceptionTranslators(ghost);
        public IReadOnlyList<IEcsModuleSystem> CreateSimHostAttributeUpdateSystems() => EditHandlers ?? _base.CreateSimHostAttributeUpdateSystems();
        public IIgTranslators CreateIgTranslators() => _base.CreateIgTranslators();
        public IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? p, long n = 0) => _base.CreateIgNetworkAdapter(p, n);
        public ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters() => _base.CreateCgfEntityLifecycleAdapters();
        public IReadOnlyList<IDescriptorTranslator> CreateIgEgressTranslators(
            CycloneDDS.Runtime.DdsParticipant participant, FdpEventBus bus, long nodeId)
            => _base.CreateIgEgressTranslators(participant, bus, nodeId);
        public IReadOnlyList<INetworkTranslator> CreateGizmoTranslators(FdpEventBus b, long id, bool headless) => _base.CreateGizmoTranslators(b, id, headless);
        public IEcsModuleSystem? CreateGizmoPublisherSystem(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer buf, long id) => _base.CreateGizmoPublisherSystem(buf, id);
    }

    /// <summary>
    /// Mock INedReplicationModule that tracks whether RegisterSystems was called.
    /// </summary>
    [TickMode(TickPolicy.EveryFrame)]
    private sealed class MockNedReplicationModule : INedReplicationModule
    {
        public bool WasRegistered { get; private set; }
        public GhostCreationSystem GhostCreationSystem { get; } = new GhostCreationSystem(new NetworkEntityMap());
        public bool DriveFromNetwork => false;
        public NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; } = new NetworkLifecycleSystemGroup();
        public Action? AfterSeekCallback => null;
        public Fdp.Toolkit.Replication.Services.DescriptorOwnershipMap? DescriptorOwnershipMap => null;
        // ⚠ UNRELATED TO O7c — a PRE-EXISTING compile break in this project, which has no
        //   obj/project.assets.json in a fresh container and was therefore never built (trap ③,
        //   §31.19.3). INedReplicationModule gained ExpectedPeers and this test double was never
        //   updated. Implemented as null, matching every production `?.ExpectedPeers` call site.
        public Fdp.Toolkit.Replication.Abstractions.IExpectedPeersProvider? ExpectedPeers => null;

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
            "ResolveCapabilities",   // ⭐ CE-4013 — the base owns the capability walk
            "get_TimeRole",          // ⭐ CE-1446 B0 — the two new INPUTS (property getters): a time role and whether a post-sim group is built
            "get_BuildsPostSimGroup",
            "get_DeduplicateSystemsByType",   // ⭐ CE-1446 B2 — opt-in CE-165 dedupe (the editor)
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

    // ── S2b / F-10 ────────────────────────────────────────────────────────────

    [TickMode(TickPolicy.EveryFrame)]
    private sealed class CountingSystem : IEcsModuleSystem
    {
        public int Runs;
        public void Execute(ISimulationView view, float dt) => Runs++;
    }

    /// <summary>
    /// ⭐⭐ <b>S2b / F-10 — every host built on this base applies another node's edit request.</b> The
    /// owner-side handlers come from the CONFIGURED factory and run every frame. Before S2b only SimHost
    /// registered them, so an edit addressed to an entity IG or Stride owned was lost.
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §3 F-10, §5.6 S2b.
    /// </summary>
    [Fact]
    public void TheBaseRegistersTheConfiguredFactorysEditRequestHandlers_ForEveryHost()
    {
        var handler = new CountingSystem();
        var factory = new MockNedFactory(new MockNedReplicationModule()) { EditHandlers = new IEcsModuleSystem[] { handler } };
        var bootstrapper = new TestBootstrapper(factory);

        var context = bootstrapper.BootstrapNode(HeadlessConfig(), NodeRole.None, factory);
        context.Kernel.Update();

        Assert.Equal(1, handler.Runs);
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
