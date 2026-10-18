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

    private NodeBootstrapper? _nodeBootstrapper;

    /// <summary>The serving traffic pack (runtime + mirror are set once the node is booted).</summary>
    public TrafficPack Traffic { get; } = TrafficPack.Serving();


    // UNIFY: identical to StrideNodeBootstrapper.cs:178-200 (minus its gizmo buffers) — the time controller owns time, not dt.
    public void Tick()
    {
        Context.Tick();   // ⭐ CE-4013 D7 — the one node frame
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
        => Hrot.Network.Infrastructure.HrotNodeBuilderReplicationExtensions.BuildReplicatedNode(config, role, networkFactory, GetBehaviorRegistry());   // ⭐ CE-4013 D17

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
        // ⭐ CE-4013 — nothing of its own: the capabilities' systems are added by the base, right after this hook, and the node's TKB and
        //   entity map are world singletons from HrotNodeBuilder (D4/D5).
    }

    // UNIFY: identical to StrideNodeBootstrapper.cs:380-411, plus tkbDb (the silent-default rule: this caller HAS the node's catalogue,
    //   so the knowledge-base load step loads INTO it rather than into a fresh one — SimHostNodeBootstrapper.cs:453 passes it too).
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
        => (_nodeBootstrapper = new NodeBootstrapper(NetworkFactory)).BuildOrchestration(
            BootRole,
            context.Kernel,
            context.World,
            context.NodeId,
            participant:         context.Participant,
            subsystemName:       BootConfig!.SubsystemName,
            eventBus:            context.EventBus,
            scenarioSerializer:  serializer,
            localTempRoot:       BootConfig.LocalTempRoot,
            tkbDb:               context.TkbDb,
            simGroup:            simGroup,
            postSimGroup:        postSimGroup,
            lifecycleGroup:      context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator:    context.EventAccumulator,
            afterSeek:           context.NedReplication?.AfterSeekCallback,
            elm:                 context.BaseModules?.OfType<EntityLifecycleModule>().FirstOrDefault(),
            terrainContent:      context.TerrainContent,   // ⭐ CE-4013 — the node's terrain content (the SUMO net)
            clusterSlave:        context.ClusterSlave,     // ⭐ CE-4013 D15 — the node's ONE slave and translator
            slaveTranslator:     context.SlaveTranslator);

    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        RegisterCapabilities(context);         // ⭐ CE-4013 — the shared walk's Register phase, first (Stride's point)
        RegisterEntityCreationPack(context);   // ghost promotion lives here: the TrafficDemand entity arrives as a ghost (S3)
    }

    protected override void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory? configuredFactory)
    {
        // The NED shared pack (EntityTrafficDemandIngressTranslator among it) comes with the replication module the base registers.
        // UNIFY: identical to StrideNodeBootstrapper.cs:535, IgNodeBootstrapper.cs:620, SimHostNodeBootstrapper.cs:614 — the
        //   auxiliary pack every node builds; its role gates decide what this node takes.
        configuredFactory?.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);
    }


    // ── The shared pieces, one per method ─────────────────────────────────────

    /// <summary>⭐ The role decides the composition: the shared plan head plus the serving traffic pack; the base walks it (CE-4013).</summary>
    protected override IReadOnlyList<INodeCapability> ResolveCapabilities(HrotNodeContext context, NodeRole role)
    {
        var plan = CoreInfrastructureCapabilities.LiveSimulationPlan(Role)
            .Capability(Role, Traffic);
        // The declared-vs-needed resource check is the base's (CE-4013 D10); a Need with no provider throws here, naming it.
        plan.RequiredResources(role);
        return plan.Resolve(role);
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

        // ⭐ CE-4013 D6 — every piece of the pack, scheduled by the ONE shared registration.
        creation.RegisterOn(context.Kernel);
        Hrot.SimHost.Systems.GenesisTail.RegisterOn(context.Kernel, context.EntityMap);   // ⭐ CE-4013 D11 — the shared genesis tail
    }
}
