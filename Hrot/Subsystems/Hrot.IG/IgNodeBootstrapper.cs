using CarKinem.Core;
using Fdp.Core.Logging;
using Hrot.Common.EntityCreation;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization.Migrations;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.Common.Diagnostics;
using Hrot.Common.Infrastructure;
using Hrot.Common.Scenario.Migrations;
using Hrot.Common.Orchestration;
using Hrot.Common.Systems;
using Hrot.Core.Diagnostics;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Hrot.IG.Modules;
using Hrot.IG.Systems;
using Hrot.Map.Definitions.Tkb;
using Hrot.Map.Common;
using Hrot.Network.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Presentation.Map;

namespace Hrot.IG;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the IG (Image Generator) node.
/// Implements all abstract hooks to produce a visualization-only node with role
/// <see cref="NodeRole.Map2D"/>.
/// </summary>
internal sealed class IgNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly INetworkFactory? _networkFactory;

    /// <summary>
    /// ⭐⭐ <c>C8</c> — THIS node's road-graph holder (it owns every published <c>RoadNetworkBlob</c>, so a terrain reload is safe against
    /// a background reader). ⭐ CE-4013 D8 — it IS the shared orchestration helper's, as on every host; IG used to keep a mirror copy.
    /// </summary>
    public CarKinem.Road.RoadNetworkHolder RoadNetworkHolder => _nodeBootstrapper.RoadNetworkHolder;

    /// <summary>⭐⭐ <c>C8</c> — THIS node's terrain load service (🔒 "IG/CGF get loaders in batch 3"); the shared helper's (CE-4013 D8).</summary>
    public Hrot.Map.Common.Services.TerrainLoadService TerrainLoadService => _nodeBootstrapper.TerrainLoadService;

    /// <summary>⭐ CE-4013 D8 — the ONE orchestration helper every bootstrapper host uses (SimHost, Stride, traffic, IG).</summary>
    private readonly Hrot.SimHost.NodeBootstrapper _nodeBootstrapper;

    private readonly int _effectiveInstanceId;
    private readonly bool _headless;
    private readonly IIgTranslators? _igTranslatorsProvider;
    private readonly MapUserConfig _userConfig;
    private readonly MapCameraViewport _cameraViewport;
    private readonly IDiagnosticEventHistoryService? _eventHistoryService;
    private readonly HrotNodeConfig _hrotConfig;

    /// <summary>True when DDS network was successfully configured. Valid after BootstrapNode() returns.</summary>
    public bool NetworkEnabled { get; private set; }

    /// <summary>Protocol-neutral IG network adapter. Valid after BootstrapNode() returns.</summary>
    public IIgNetworkAdapter? NetworkAdapter { get; private set; }

    /// <summary>Command gateway obtained from the network adapter. Valid after BootstrapNode() returns.</summary>
    public Hrot.Core.Network.ICommandGateway? CommandGateway { get; private set; }

    // ⛔⛔ CE-164 — `OrchestrationBus` and `IgSlaveTranslator` are GONE.
    //
    // They exposed a SECOND orchestration stack that IG built on top of the shared one and then ticked
    // instead of it. 📐 Measured 2026-09-03 on a four-process cluster: HrotNodeBuilder.Build() Step 8
    // already builds this node's ClusterSlave AND a COMPLETE ISlaveOrchestrationTranslator
    // (NodeOpSlaveTranslator + ClusterOpEgressTranslator) on context.EventBus, and IgNodeBootstrapper:151
    // calls that builder — so the egress half was BUILT on IG and then discarded. IG published
    // TransitionStateIntent onto context.EventBus (IgApplication.OrchestrationBus) whose complete
    // translator nobody ticked, while ticking its own bare ingress-only one on a different bus. Result:
    // `POST /scenario/load/live` on the IG port answered ok/"cluster-intent" and the cluster never moved,
    // where the identical call on CGF moved all three nodes.
    //
    // ⭐ Readers now use context.EventBus and context.SlaveTranslator — the same members SimHostApp:504/560,
    //   StrideNodeBootstrapper:174 and CgfSubsystem:1294 already use. IG was
    //   the only host that did not.
    // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1b.

    /// <summary>Migration services bundle. Valid after BootstrapNode() returns.</summary>
    public MigrationServices? MigrationServices { get; private set; }


    internal IgNodeBootstrapper(
        INetworkFactory? networkFactory,
        int effectiveInstanceId,
        bool headless,
        IIgTranslators? igTranslatorsProvider,
        MapUserConfig userConfig,
        MapCameraViewport cameraViewport,
        IDiagnosticEventHistoryService? eventHistoryService,
        HrotNodeConfig hrotConfig)
    {
        _networkFactory = networkFactory;
        _nodeBootstrapper = new Hrot.SimHost.NodeBootstrapper(networkFactory);
        _effectiveInstanceId = effectiveInstanceId;
        _headless = headless;
        _igTranslatorsProvider = igTranslatorsProvider;
        _userConfig = userConfig;
        _cameraViewport = cameraViewport;
        _eventHistoryService = eventHistoryService;
        _hrotConfig = hrotConfig;
    }

    // ── Phase 1: Build context ────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        // ⭐ CE-197 — remember the role so the capability set can be resolved FROM it (B4b step 3).
        //   ⚠ Captured HERE, at boot phase 1, because every later step declares `requires: ["context"]`
        //     — so the plan cannot run one before this has run (the base's `capabilities` step among them, CE-4013).
        _declaredRole = role;

        // ⭐⭐⭐ CE-141 CLOSED 2026-09-03 — THE TRANSLATOR LIST IS NOT BUILT HERE, AND NOT PER-HOST.
        //
        // 🔒 User ruling: "entity creation needs to be unified. There should be nothing we give just to
        //    IG. every ECS nodes must use same TKB in same way using the same shared code."
        //
        // ⭐ This is a DELETION, not a substitution: IG was the LAST host still calling
        //    .WithTranslators(...). SimHost dropped it at CE-140 step 3 and the reason applies verbatim
        //    here — that argument fed NedReplicationModule's `tkbEntityTranslators`, whose ONLY consumer
        //    is GhostPromotionSystem, and that system falls back to EntityLifecycleModule.Translators
        //    when no explicit list is given (GhostPromotionSystem.cs: `_explicitTranslators ??
        //    _lifecycleModule.Translators`). The ELM's list is the ONE instance EntityCreationPack
        //    composed from TkbTranslatorSet.Base(). ⇒ dropping the call makes tkb-1/DESIGN.md §6.3's
        //    "identical for all three systems within the same node" true BY CONSTRUCTION instead of by
        //    two copies agreeing.
        //
        // ⛔⛔ WHY THE OLD 2-ENTRY LIST WAS WRONG, and it was wrong on the shared type's own terms:
        //    TkbTranslatorSet's contract says "Do NOT subtract from this list to make a host materialise
        //    less ... every ITkbEntityTranslator is contractually required to guard each write with
        //    repo.IsComponentTypeRegistered<T>(), so a translator whose components a host never
        //    registered is ALREADY a no-op there." 📐 Verified 2026-09-03 across all six Base()
        //    translators: guards >= component adds in every one. ⇒ THE NARROWING LEVER IS THE COMPONENT
        //    REGISTRATION SET, never the list. If IG should not seed Health/WeaponState/PerceptionReceptor
        //    /TargetMemory/VehicleParams/PhysicsCollider from TKB, the answer is to stop REGISTERING
        //    them in IgRoleComponentRegistry — one loud decision, and any later write throws.
        //
        // ⚠ Two earlier comments here are SUPERSEDED and are gone: (a) "a shorter list is a real decision
        //    here" (asserted, never measured) and (b) "this list feeds ONLY the ghost projection ... never
        //    a local spawn" — false since CE-144 gave IG the shared NetworkSpawningSystem.
        // 📄 docs/DESIGN_Entity_Creation_Unification.md §2.3 · §3.4c · docs/designs/tkb-1/DESIGN.md §6.3/§6.5b.
        return Hrot.Network.Infrastructure.HrotNodeBuilderReplicationExtensions.BuildReplicatedNode(config, role, networkFactory, GetBehaviorRegistry());   // ⭐ CE-4013 D17
    }

    // ── Phase 2: Register domain ECS components ───────────────────────────────

    /// <inheritdoc/>
    protected override void RegisterDomainComponents(EntityRepository world)
    {
        // ⛔ CE-4013 D4 — this host published a FRESH catalogue here, so its world read a different TKB than the one its load step fills
        //   (context.TkbDb). HrotNodeBuilder now publishes the context's, on every node.

        //  Shared foundation 
        // Registers network replication, geographic, shared definitions, and
        // lifecycle events identically to SimHost (via SimHostComponentRegistry).
        HrotSharedComponentRegistry.RegisterAll(world);

        IgRoleComponentRegistry.RegisterAll(world);


        // SimCombatDef, TkbCompositionDef, VisualData, lifecycle events, and
        // FireInteractionEvent are all handled by HrotSharedComponentRegistry above.
    }

    // ── Phase 3: Build scenario serializer ───────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐ CE-4013 D9 — the SAME serializer every host saves its owned slice with (🔒 user, 2026-09-14: "IG must share same scenario save
    /// handler with other hosts … Unification is our goal"). ⛔ It used to be a bare builder stamping the "Hrot.IG" header (IG's VISUAL
    /// configuration payload) with none of the HROT translators — so an entity IG owned would have saved without its mission plan,
    /// routes or hierarchy, under a header the scenario load does not take.
    /// </remarks>
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        => Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(
            registry ?? new BehaviorRegistry(),
            unknownComponentPolicy: Fdp.Toolkit.Scenario.UnknownComponentPolicy.WarnAndSkip);

    // ── Phase 4a: Populate togglable system groups ────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ CE-2150 (R-2010) — IG composes its capabilities' systems like every other host; 🔒 user, 2026-10-13: "IG is no different".
    /// ⭐ CE-4013 — the base walks them right after this hook (and their modules after <c>additional-modules</c>), so IG keeps no
    /// loop of its own: it has no systems besides its capabilities'. 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1ae.
    /// </remarks>
    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>⭐⭐⭐ <c>B4b</c> step 2, host (b) — IG's units come from a RESOLVED plan: the five presentation modules in
    /// <c>IgCapabilities.Presentation</c> plus the cross-role infrastructure. 📄 §4.1t.</para>
    /// <para>⭐ CE-4013 — resolved once in the base's <c>capabilities</c> step. ⛔ The lazy resolution this replaced existed only because
    /// <c>additional-modules</c> did not require <c>system-groups</c>; the base now orders them, for every host.</para>
    /// <para>⚠ The role is not narrowed — the same stance <c>SimHostNodeBootstrapper</c> takes: this host IS the image generator.</para>
    /// </remarks>
    protected override IReadOnlyList<INodeCapability> ResolveCapabilities(HrotNodeContext context, NodeRole role)
    {

        // ⭐⭐ CE-2144 (R-2008) — the ONE shared plan head. ⚠ IG's PopulateSystems walks no capability today (CE-2150), so nothing
        //    here ticks on IG yet; the plan is shared so that fixing that walk is the only change IG needs.
        var plan = CoreInfrastructureCapabilities.LiveSimulationPlan(NodeRole.Map2D)
            .Capability(
                NodeRole.Map2D,
                new IgCapabilities.Presentation(
                    _userConfig, _effectiveInstanceId, _cameraViewport, _headless))
            // ⭐ CE-221 — cross-role infrastructure, declared LAST so it keeps its tail-of-Simulation
            //    position. Declared once per plan; Resolve de-duplicates by Key, which is what makes
            //    a Brain+Muscle node register it ONCE instead of twice.
            //    ⚠ IG carries UnitHierarchy only: it never had EqsResultUpdateSystem, and Hrot.IG does
            //    not reference Hrot.SimHost where that system lives. Its system set is unchanged.
            .Capability(NodeRole.Map2D, new Hrot.Common.Infrastructure.CoreInfrastructureCapabilities.UnitHierarchy())
            // ⭐ CE-3110 (R-221/R-224) — interactions (doors, embarkation) are cross-role too: any node that can own a live entity can own a door, a vehicle or a passenger.
            .Capability(NodeRole.Map2D, new Hrot.Common.Infrastructure.CoreInfrastructureCapabilities.Interactions())
            // ⭐ CE-2301 ① (R-2058) — no brain here, so the report inbox comes as a capability: the Reports window has a log to read.
            .Capability(NodeRole.Map2D, new Hrot.Common.Infrastructure.CoreInfrastructureCapabilities.ReportInbox())
            // ⭐ AQ93 §12 N6 (R-4056) — SUMO cars mirrored for the map (the road layer and the car layer read its frame); idle without a net.
            .Capability(NodeRole.Map2D, Hrot.Traffic.TrafficPack.Mirroring());

        // ⭐⭐⭐ CE-197 — resolved from the DECLARED role, not a constant (B4b step 3).
        //    📐 Provably a no-op here: IgApplication.cs:923 passes the literal NodeRole.Map2D,
        //    which is exactly what the capability above is declared for. ⚠ Unlike SimHost — whose
        //    declared role was MISSING NavigationSolver and had to be corrected before this swap was
        //    safe — IG's declaration was already true.
        NodeRole composed = _declaredRole;

        var capabilities = plan.Resolve(composed);

        // ⭐ CE-199 / CE-4013 D10 — the declared-vs-needed resource cross-check is the base's `capabilities` step now (every host).
        plan.RequiredResources(composed);   // a selected Need with no provider throws, naming it

        return capabilities;
    }

    /// <summary>The role this node was bootstrapped with, captured in <see cref="BuildContext"/>.</summary>
    private NodeRole _declaredRole = NodeRole.Map2D;

    // ── Phase 5: Build orchestration ─────────────────────────────────────────

    /// <inheritdoc/>
    /// <summary>⭐ P5a — IG's recording controller, the same one every ECS host composes (null until orchestration is built).</summary>
    public Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController? RecordReplayController { get; private set; }

    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        // ⭐⭐⭐ CE-4013 D8 — the SAME orchestration helper every bootstrapper host uses. 🔒 User (CE-164): "IG is no different from
        //   cluster mgmt point of view. unification in as many places as possible". ⛔ This hand-built the whole handler set, a second
        //   ClusterSlave ("IG" / Map2D hard-coded beside the builder's), a replay handler that toggled no group and suspended no time
        //   push, and a preview handler with no world. ⇒ IG now gets exactly what every host gets: the context's ONE slave and
        //   translator (CE-164's lesson, D15), its DECLARED role, replay that suspends its sim/post groups, preview that rewinds.
        //   📄 DESIGN_Subsystem_Composition_Unification.md §4.1b/§4.1c (CE-164), §4.1ae.1 D8.
        MigrationServices = HrotMigrationBootstrap.BuildIg();

        string storageDirectory = !string.IsNullOrWhiteSpace(_hrotConfig.LocalTempRoot)
            ? _hrotConfig.LocalTempRoot
            : OrchestrationConstants.ResolveStagingRoot();
        string logDirectory = !string.IsNullOrWhiteSpace(_hrotConfig.LogDirectory)
            ? _hrotConfig.LogDirectory
            : System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs");

        var slave = _nodeBootstrapper.BuildOrchestration(
            BootRole, context.Kernel, context.World, _effectiveInstanceId,
            participant:         context.Participant,
            subsystemName:       _hrotConfig.SubsystemName,
            eventBus:            context.EventBus,
            scenarioSerializer:  serializer,   // CE-275 ③ — IG saves its owned slice through the shared handler
            localTempRoot:       storageDirectory,
            tkbDb:               context.TkbDb,
            simGroup:            simGroup,
            postSimGroup:        postSimGroup,
            lifecycleGroup:      context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator:    context.EventAccumulator,
            afterSeek:           (context.NedReplication as Hrot.Common.Abstractions.INedReplicationModule)?.AfterSeekCallback,
            // MD-002 — IG's diagnostics dump (its event history, architecture, entities, logs).
            diagnosticsDumpHandler: new DiagnosticsDumpClusterOpHandler(
                _eventHistoryService!,
                new ArchitectureDiagnosticsService(context.Kernel),
                new EntityStateExtractionService(context.World, context.EntityMap),
                new LogArchiveExtractionService(logDirectory, _hrotConfig.SubsystemName, context.NodeId),
                _hrotConfig),
            elm:                 context.BaseModules?.OfType<Fdp.Toolkit.Lifecycle.EntityLifecycleModule>().FirstOrDefault(),
            terrainContent:      context.TerrainContent,
            clusterSlave:        context.ClusterSlave,
            slaveTranslator:     context.SlaveTranslator);

        RecordReplayController = _nodeBootstrapper.RecordReplayController;   // P5a — the same controller every ECS host composes
        return slave;
    }

    // ── Phase 6a: Register spawning pipeline ─────────────────────────────────

    /// <inheritdoc/>
    /// <summary>
    /// ⭐⭐⭐ <b>host (f) — IG composes the SAME entity-creation tier as every other ECS node.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b ·
    /// <c>Architect_Question_65</c> <c>Q65-A′</c>: every ECS node composes the full genesis pipeline;
    /// the pack has no opt-out, and a node that never creates locally simply never enqueues a
    /// self-targeted request.</para>
    ///
    /// <para>🔴 <b>What this replaces, and why the old arrangement was an accident.</b> IG used to
    /// register NO spawn pipeline at all, with the comment <i>"replaces SpawningModule so IG does not
    /// duplicate entities"</i>. ⛔ That prevented the double spawn by OMITTING the systems — an
    /// arrangement that only holds while nothing else consumes the order, and it is exactly what §3.4a
    /// identifies as the hazard: <c>FdpEventBus</c> is a broadcast, not a work queue, so two subscribers
    /// on one <c>SpawnEntityCommand</c> each act on it. ⇒ ⭐ the duplication is now prevented
    /// STRUCTURALLY, one level up: the tools post an INTENT, and
    /// <c>ForwardingEntityCreationRequestSource</c> decides per request whether this node services it or
    /// the NED egress sends it to the node that should.</para>
    ///
    /// <para>⭐⭐ <b>Why registering the spawn system does NOT reintroduce the double spawn.</b>
    /// 📐 Measured: IG's requests carry <c>OwnerAppInstanceId = 0</c> (untargeted — see
    /// <c>IgEntityCreationRequests</c>) and this node is NOT the broadcast arbiter, so
    /// <c>EntityCreationRouting.IsHandledLocally</c> is false for every one of them. The forwarder sends
    /// them, no local order is published, and the authoritative entity still comes back as a replicated
    /// ghost — today's behaviour exactly. ⭐ The systems are present and idle, which is what
    /// <c>Q65-A′</c> asks for: capability by composition, not by node role.</para>
    /// </summary>
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // ⭐ CE-4013 — the capabilities' Register hook, FIRST (Stride's and the editor's point). ⛔ IG never called it: a capability
        //   composing through Register did nothing here. None in IG's plan implements it today, so this changes no order.
        RegisterCapabilities(context);

        var creation = EntityCreationPack.Build(new EntityCreationContext
        {
            World       = context.World,
            EntityMap   = context.EntityMap,
            TkbDb       = context.TkbDb!,
            IdAllocator = context.IdAllocator!,
            Elm         = (EntityLifecycleModule)context.BaseModules
                              .First(m => m is EntityLifecycleModule),
            NodeId      = context.NodeId,

            // ⭐⭐ S2b — the SAME network adapters every ECS host passes. The pack takes the request source,
            //    ack sink, JSON compiler, ownership strategy AND the forwarding egress (D1: without it a
            //    request addressed elsewhere is silently dropped by the Level-1 guard) from this one object,
            //    and builds the delete and poll systems. Null offline. 📄 DESIGN_Ownership_Groups_And_Grants.md §5.6.
            //    ⚠ From the NODE-CONFIGURED factory, as on SimHost and Stride — IG used to take them from the raw
            //    factory, which carries no node id or role.
            NetworkAdapters       = ConfiguredNetworkFactory?.CreateCgfEntityLifecycleAdapters(),

            // ⭐⭐⭐ CE-291 (piece C) — the reliable-init wait-set provider, sourced UNIFORMLY from the shared
            //    NED replication module. 🔒 User ruling 2026-09-16: no node-centric gating — IG is a symmetric
            //    reliable creator. (IG already had the cache via adapters but never passed ExpectedPeers.)
            //    📄 docs/DESIGN_Cross_Node_Construction_Barrier.md §3a.4.
            ExpectedPeers         = context.NedReplication?.ExpectedPeers,

            // ⛔ NOT the cluster's broadcast arbiter — that is CGF, and exactly one node may be it.
            IsBroadcastArbiter = false,
        });

        // ⭐ RegisterSpawningPipeline runs BEFORE RegisterApplicationSystems (SharedApplicationBootstrapper.cs:111 vs :139), so the
        //    registrar callback that constructs MapCommandController always sees the pack. ⭐ CE-3228 — the tools publish their
        //    requests on this world's bus, which the pack reads.
        EntityCreation              = creation;   // CE-515

        // ⭐ CE-4013 D6 — every piece of the pack, scheduled by the ONE shared registration (EntityCreation.RegisterOn). What it carries
        //   that matters most here:
        //   · CE-271 seam ④ — the cluster-cache pump (NodeHeartbeat) is one of the NetworkSystems; without it a Map2D-created tank's
        //     grant set is EMPTY.
        //   · CE-144 — NetworkSpawningSystem is the ONE consumer of DestroyEntityCommand (it begins the ELM teardown, so the wire
        //     dispose is written); IG's private GhostDestructionSystem is gone. 📄 DESIGN_Entity_Creation_Unification.md §3.4c.
        //   · P2 — ghost promotion comes from the pack; IG, which receives ghosts of everything, would otherwise keep every received
        //     entity in EntityLifecycle.Ghost. 📄 DESIGN_Role_Affinity_Ownership.md §3.7.
        //   ⚠ The spawner was a GLOBAL system on this host (a module on SimHost, Stride, the editor); it is the module now, like
        //     everywhere — the one ordering it must keep (LocalAuthorityYieldSystem after it) is declared by [UpdateAfter].
        creation.RegisterOn(context.Kernel);
        // ⭐ CE-4013 D11 — and the shared genesis tail, which IG alone lacked (§4.1f: it belongs with the pack, not a role).
        Hrot.SimHost.Systems.GenesisTail.RegisterOn(context.Kernel, context.EntityMap);

        // ⭐ CE-221 — the standalone SingleSystemModule registration that used to sit here was the
        //    PRECEDENT for treating UnitHierarchySystem as cross-role infrastructure rather than pack
        //    content: IG got it right while three role packs carried their own copy. It is now declared
        //    as CoreInfrastructureCapabilities.UnitHierarchy in this node's plan, so the system set is
        //    unchanged and every host reaches it the same way.
    }

    // ── Phase 6b: Register network translators ────────────────────────────────

    /// <inheritdoc/>
    protected override void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory)
    {
        if (configuredFactory == null || context.Participant == null)
            return;

        // Use raw _networkFactory for methods that require a participant directly.
        NetworkAdapter = _networkFactory != null
            ? _networkFactory.CreateIgNetworkAdapter(context.Participant, _effectiveInstanceId)
            : NullIgNetworkAdapter.Instance;
        CommandGateway = NetworkAdapter?.CommandGateway;

        // ⭐ CE-1148 S3 (docs/DESIGN_Node_Roles_And_Policies.md §3.5, R-3015) — the same auxiliary pack every node builds; its role
        //   gates decide what the IG takes (today: the order egress, so an order given on this station reaches the brain owner).
        configuredFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);

        var translators = new List<INetworkTranslator>();

        // IG-specific ingress translators (entity context-actions, combat, etc.)
        // DO NOT add TimeNetworkModule translators here - base class Phase 6c handles them.
        if (_igTranslatorsProvider != null)
        {
            foreach (var t in _igTranslatorsProvider.GetTranslators(
                context.Participant,
                context.EntityMap,
                context.World.Bus,
                context.GhostCreationSystem,
                _effectiveInstanceId,
                _headless))
            {
                translators.Add(t);
            }
        }

        // D005: ACL egress translators convert bus events back to DDS.
        // Created via network factory to avoid direct NED type references in IG.
        if (_networkFactory != null)
        {
            foreach (var t in _networkFactory.CreateIgEgressTranslators(
                context.Participant, context.World.Bus, _effectiveInstanceId))
            {
                translators.Add(t);
            }
        }

        if (translators.Count > 0)
        {
            context.Kernel.RegisterGlobalSystem(
                new CycloneNetworkIngressSystem(translators.ToArray()));
            context.Kernel.RegisterGlobalSystem(
                new CycloneEgressSystem(translators.ToArray()));
            context.Kernel.RegisterGlobalSystem(
                new CycloneNetworkCleanupSystem(
                    translators.OfType<IDescriptorTranslator>()));
        }

        // ⭐ CE-1139 — the stance topics IN (a viewer neither asks for a stance nor performs one), so people are drawn in their posture.
        Hrot.Animation.Replication.AnimationReplicationModule.RegisterStanceViewerOn(
            context.Kernel, context.Participant, context.EntityMap);

        NetworkEnabled = true;
    }

    // ── Phase 6d: Application-level systems ──────────────────────────────────

}
