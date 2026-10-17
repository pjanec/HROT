using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Common;
using Hrot.Common.EntityCreation;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Network.Infrastructure;
using Hrot.SimHost;
using Hrot.SimHost.Serializers;
using Hrot.Traffic;

namespace Hrot.TrafficNode;

/// <summary>
/// ⭐ AQ93 §12 N1 (R-4054) — the dedicated SUMO traffic node: the shared seven-phase bootstrap with role
/// <see cref="NodeRole.Traffic"/> and ONE capability, <see cref="TrafficPack.Serving"/>. Everything else — terrain residency (which
/// loads the SUMO net), the TKB, the load-phase chain, entity creation and ghost promotion (the scenario's <c>TrafficDemand</c> entity
/// arrives as a ghost), NED replication — is the shared code every ECS node runs.
/// </summary>
/// <remarks>
/// <para>🔒 User, 2026-10-17: <i>"it should use pretty similar or same bootstrap code, because we are unifying across hosts, and the
/// differences are only based on data-driven roles."</i> Several traffic nodes may run at once (R-4055): nothing here assumes one.</para>
/// <para>⚠ The <c>UNIFY:</c> methods below are the pieces every host bootstrapper repeats; they are kept one-per-method so the shared
/// replacement is a one-line swap each.</para>
/// </remarks>
public sealed class TrafficNodeBootstrapper : SharedApplicationBootstrapper, IDisposable
{
    /// <summary>The traffic node's role: it serves the traffic settings it is granted (S2).</summary>
    public const NodeRole Role = NodeRole.Traffic;

    private HrotNodeConfig?   _savedConfig;
    private NodeRole          _savedRole = Role;
    private NodeBootstrapper? _nodeBootstrapper;
    private IReadOnlyList<INodeCapability>? _capabilities;

    /// <summary>The serving traffic pack (runtime + mirror are set once the node is booted).</summary>
    public TrafficPack Traffic { get; } = TrafficPack.Serving();

    /// <summary>The fully wired node context. Valid after <see cref="BootstrapNode"/> returns.</summary>
    public HrotNodeContext Context { get; private set; } = default!;

    /// <summary>The capabilities this node composed, resolved from its role.</summary>
    public IReadOnlyList<INodeCapability> Capabilities => ResolveCapabilities();

    /// <summary>Optional phase-6d callback (diagnostics, test systems).</summary>
    public Action<HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }

    // UNIFY: identical to StrideNodeBootstrapper.cs:149-167 — capture config/role, patch the SlaveTranslator NodeBootstrapper built.
    public new HrotNodeContext BootstrapNode(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        _savedConfig      = config ?? throw new ArgumentNullException(nameof(config));
        _savedRole        = role;
        _nodeBootstrapper = new NodeBootstrapper(networkFactory);

        var ctx = base.BootstrapNode(config, role, networkFactory);
        if (_nodeBootstrapper.SlaveTranslator != null)
            ctx = ctx with { SlaveTranslator = _nodeBootstrapper.SlaveTranslator };

        Context = ctx;
        return Context;
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:178-200 (minus its gizmo buffers) — the time controller owns time, not dt.
    public void Tick()
    {
        Context.SlaveTranslator?.Tick();
        Context.ClusterSlave.Tick();
        Context.Kernel.Update();
        Context.EventBus.SwapBuffers();
    }

    public void Dispose()
    {
        Traffic.Dispose();
        DisposeResources();
        Context?.Participant?.Dispose();
    }

    // ── Hooks ─────────────────────────────────────────────────────────────────

    // UNIFY: identical to StrideNodeBootstrapper.cs:218-226 and IgNodeBootstrapper.cs:180-185.
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
        => new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .Build();

    protected override void RegisterDomainComponents(EntityRepository world)
    {
        // The shared foundation every ECS node registers: replication, geographic, definitions, lifecycle events, and TrafficDemand
        // (S1). Nothing more is needed — measured by booting this node (TrafficNodeBootstrapperTests) and the --mode traffic smoke.
        // ⛔ No muscle/combat/cognitive tables: TkbTemplate.ApplyTo skips unregistered components, so ghosts of our units promote here
        //    with only the shared shape, and no system on this node reads more.
        HrotSharedComponentRegistry.RegisterAll(world);
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:326-329 (SimHostNodeBootstrapper.cs:272-281 differs only by its behavior registry).
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        => HrotScenarioSerializerFactory.Build(
            registry ?? new BehaviorRegistry(),
            unknownComponentPolicy: UnknownComponentPolicy.WarnAndSkip);

    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        PublishNodeSingletons(context);
        WalkCapabilitiesPopulateSystems(context, input, sim, postSim);
    }

    protected override IEnumerable<IEcsModule> GetAdditionalModules() => WalkCapabilitiesProvideModules();

    // UNIFY: identical to StrideNodeBootstrapper.cs:380-411, plus tkbDb (the silent-default rule: this caller HAS the node's catalogue,
    //   so the knowledge-base load step loads INTO it rather than into a fresh one — SimHostNodeBootstrapper.cs:453 passes it too).
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
        => _nodeBootstrapper!.BuildOrchestration(
            _savedRole,
            context.Kernel,
            context.World,
            context.NodeId,
            participant:         context.Participant,
            subsystemName:       _savedConfig!.SubsystemName,
            eventBus:            context.EventBus,
            scenarioSerializer:  serializer,
            localTempRoot:       _savedConfig.LocalTempRoot,
            tkbDb:               context.TkbDb,
            simGroup:            simGroup,
            postSimGroup:        postSimGroup,
            lifecycleGroup:      context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator:    context.EventAccumulator,
            afterSeek:           context.NedReplication?.AfterSeekCallback,
            elm:                 context.BaseModules?.OfType<EntityLifecycleModule>().FirstOrDefault());

    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        WalkCapabilitiesRegister(context);
        RegisterEntityCreationPack(context);   // ghost promotion lives here: the TrafficDemand entity arrives as a ghost (S3)
    }

    protected override void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory? configuredFactory)
    {
        // The NED shared pack (EntityTrafficDemandIngressTranslator among it) comes with the replication module the base registers.
        // UNIFY: identical to StrideNodeBootstrapper.cs:535, IgNodeBootstrapper.cs:620, SimHostNodeBootstrapper.cs:614 — the
        //   auxiliary pack every node builds; its role gates decide what this node takes.
        configuredFactory?.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);
    }

    protected override void RegisterApplicationSystems(HrotNodeContext context)
        => ApplicationSystemsRegistrar?.Invoke(context);

    // ── The shared pieces, one per method ─────────────────────────────────────

    /// <summary>
    /// ⭐ The role decides the composition: the shared plan head plus the serving traffic pack. Resolved lazily (the
    /// <c>additional-modules</c> step does not require <c>system-groups</c>, so it may run first — IgNodeBootstrapper.cs:245-251).
    /// </summary>
    private IReadOnlyList<INodeCapability> ResolveCapabilities()
    {
        if (_capabilities != null) return _capabilities;
        var plan = CoreInfrastructureCapabilities.LiveSimulationPlan(Role)
            .Capability(Role, Traffic);
        _capabilities = plan.Resolve(_savedRole);
        // UNIFY: identical in intent to IgNodeBootstrapper.cs:307-316 / SimHostNodeBootstrapper.cs:397-414 — no capability here needs
        //   a node resource, so the declared set (none) must stay equal to the resolved one.
        if (plan.RequiredResources(_savedRole).Count != 0)
            throw new InvalidOperationException(
                $"Traffic node role {_savedRole} composed capabilities that need node resources; declare them in DeclaredResourceKeys.");
        return _capabilities;
    }

    // UNIFY: identical to the node-singleton publication of SimHostNodeBootstrapper.cs:268 (TKB), SimHostApp.cs:566 and
    //   CgfSubsystem.cs:734 (entity map), CgfSubsystem.cs:754-755 (TKB). The mirror reads the vehicle catalogue from the world's
    //   ITkbDatabase (TrafficMirrorSystem.TkbOf), and the load-phase world boundary resolves the entity map from the world.
    private static void PublishNodeSingletons(HrotNodeContext context)
    {
        if (context.TkbDb != null)
            context.World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(context.TkbDb);
        context.World.SetSingletonManaged(context.EntityMap);
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:350-351, IgNodeBootstrapper.cs:231-232, SimHostNodeBootstrapper.cs:377-378.
    private void WalkCapabilitiesPopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input,
                                                 List<IEcsModuleSystem> sim, List<IEcsModuleSystem> postSim)
    {
        foreach (INodeCapability capability in ResolveCapabilities())
            capability.PopulateSystems(context, input, sim, postSim);
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:355-364 and IgNodeBootstrapper.cs:258-263.
    private IEnumerable<IEcsModule> WalkCapabilitiesProvideModules()
    {
        foreach (INodeCapability capability in ResolveCapabilities())
            foreach (IEcsModule module in capability.ProvideModules())
                yield return module;
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:447-448 (SimHostNodeBootstrapper.cs:596-598 passes a free-standing bag instead).
    private void WalkCapabilitiesRegister(HrotNodeContext context)
    {
        foreach (INodeCapability capability in ResolveCapabilities())
            capability.Register(context, BootValues);
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:459-525 (same pack fields and schedule as IgNodeBootstrapper.cs:516-593 and
    //   SimHostNodeBootstrapper.cs:495-575, which differ in order and in how the spawn system is wrapped).
    private void RegisterEntityCreationPack(HrotNodeContext context)
    {
        if (context.IdAllocator == null) return;   // headless rails with SkipAllocatorRouting, as on Stride

        var creation = EntityCreationPack.Build(new EntityCreationContext
        {
            World              = context.World,
            EntityMap          = context.EntityMap,
            TkbDb              = context.TkbDb!,
            IdAllocator        = context.IdAllocator,
            Elm                = context.BaseModules.OfType<EntityLifecycleModule>().First(),
            NodeId             = context.NodeId,
            NetworkAdapters    = ConfiguredNetworkFactory?.CreateCgfEntityLifecycleAdapters(),
            ExpectedPeers      = context.NedReplication?.ExpectedPeers,
            IsBroadcastArbiter = false,   // CGF is the one arbiter (Q65 §1)
        });
        EntityCreation = creation;   // CE-515

        context.Kernel.RegisterModule(new SingleSystemModule("NetworkSpawning", creation.SpawnSystem));
        foreach (var sys in creation.NetworkSystems)
            context.Kernel.RegisterGlobalSystem(sys);
        context.Kernel.RegisterGlobalSystem(creation.RequestSystem);        // Input
        context.Kernel.RegisterGlobalSystem(creation.FinalizationSystem);   // PostSimulation
        context.Kernel.RegisterGlobalSystem(creation.PromotionSystem);      // BeforeSync
        context.Kernel.RegisterGlobalSystem(creation.ObstacleBakeSystem);
        context.Kernel.RegisterGlobalSystem(creation.SeatPresetSystem);

        var unserviceable = creation.Unserviceable(new object[]
        {
            creation.SpawnSystem, creation.RequestSystem, creation.FinalizationSystem,
            creation.PromotionSystem, creation.ObstacleBakeSystem, creation.SeatPresetSystem,
        }.Concat(creation.NetworkSystems));
        if (unserviceable.Length > 0)
            FdpLog<TrafficNodeBootstrapper>.Warn(unserviceable);
    }
}
