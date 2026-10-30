using System;
using System.Collections.Generic;
using Hrot.Core.Network;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.Serialization.Migrations;
using CarKinem.Road;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Map.Common.Services;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.SimHost
{
    /// <summary>
    /// Orchestration composition root for Hrot nodes.
    ///
    /// <para>
    /// Handles role-appropriate <see cref="ClusterSlave"/> construction and handler
    /// registration via <see cref="BuildOrchestration"/>.  Simulation-logic module
    /// composition is handled by the role-specific logic packs
    /// (<see cref="SimHostCoreLogicPack"/>, <c>CgfLogicPack</c>, etc.).
    /// </para>
    /// </summary>
    public sealed class NodeBootstrapper
    {
        private readonly INetworkFactory? _networkFactory;

        /// <summary>
        /// ⭐⭐ THE node's road-graph holder — the owner of every published <c>RoadNetworkBlob</c> and the
        /// thing that makes a terrain/zone reload safe against a background reader.
        ///
        /// <para>Two consumers, and they are the two halves of <c>U1</c>:</para>
        /// <list type="number">
        ///   <item>the <c>TerrainLoadClusterStateHandler</c> PUBLISHES into it when terrain commits;</item>
        ///   <item>anything composing <c>NavigationSolverModule</c> must be HANDED it — that module runs
        ///     <c>SlowBackground</c>, so it cannot read the <c>ZoneEnvironmentData</c> singleton and would
        ///     otherwise plan over the construction-time graph forever.</item>
        /// </list>
        ///
        /// <para>⚠ Exposed on the bootstrapper rather than stuffed into the ECS world on purpose: it is
        /// node INFRASTRUCTURE with a lifetime, not entity state, and making it a managed ECS singleton
        /// would need a component id plus an explicit registration to keep <c>[DataPolicy]</c> from being
        /// silently ignored (<c>BP-527</c>) — machinery that buys nothing here.</para>
        /// </summary>
        public RoadNetworkHolder RoadNetworkHolder { get; } = new RoadNetworkHolder();

        /// <summary>
        /// ⭐ THE node's terrain load service — the ONE implementation both invocation paths use: the
        /// local scenario-load call (C3) and the cluster 2PC round (C4/D3). Because both land here, the
        /// idempotency branch is identical on both and "reload" cannot drift from "load on open".
        /// <para>⚠ In slice 1 its tile loader is the ANNOUNCING FAKE, which logs its stub-ness on every
        /// round. Nothing advertises a terrain capability (design §8.3 ruled: declare none).</para>
        /// </summary>
        /// <summary>⭐ CE-1446 B1 — this node's terrain residency, built by <see cref="BuildOrchestration"/> (null before it runs).</summary>
        public TerrainResidency? TerrainResidency { get; private set; }

        /// <summary>⭐ CE-1446 B2 — the handlers <see cref="BuildOrchestration"/> composed (recorder, dry run, change check, view), for a host that drives them (the editor's preview).</summary>
        public EcsHostClusterHandlers? Handlers { get; private set; }

        public TerrainLoadService TerrainLoadService { get; } =
            new TerrainLoadService(new AnnouncingZoneTileLoader());

        private Fdp.Toolkit.Navigation.INavmeshFactory?           _navmeshFactory;
        private Fdp.Toolkit.Navigation.SwitchableNavmeshProvider? _navmeshTarget;

        /// <summary>
        /// ⭐ W6 — make the terrain this node loads bake a navmesh into <paramref name="target"/>
        /// (docs/DESIGN_Terrain_World.md §4.1). Call before <c>BuildOrchestration</c>.
        /// </summary>
        public void AttachNavmesh(
            Fdp.Toolkit.Navigation.INavmeshFactory factory, Fdp.Toolkit.Navigation.SwitchableNavmeshProvider target)
        {
            _navmeshFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            _navmeshTarget  = target  ?? throw new ArgumentNullException(nameof(target));
        }

        /// <param name="networkFactory">Optional network factory (reserved for future use).</param>
        public NodeBootstrapper(INetworkFactory? networkFactory = null)
        {
            _networkFactory = networkFactory;
        }

        /// <summary>
        /// After <see cref="BuildOrchestration"/> is called with a non-null <paramref name="participant"/>
        /// and <paramref name="eventBus"/>, this property holds the <see cref="ISlaveOrchestrationTranslator"/>
        /// that bridges DDS <-> the slave event bus (CMC-S016 / BATCH-06).
        /// <c>null</c> when no DDS participant was supplied.
        /// </summary>
        public Hrot.Core.Network.ISlaveOrchestrationTranslator? SlaveTranslator { get; private set; }

        /// <summary>
        /// After <see cref="BuildOrchestration"/> is called for a Brain or MuscleGround role,
        /// this property exposes the <see cref="EcsRecordReplayController"/> so callers that
        /// cannot supply a <c>simGroup</c>/<c>lifecycleGroup</c> (e.g. CGF) can still
        /// manually register <see cref="Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler"/>
        /// on the returned <see cref="Fdp.Toolkit.Orchestration.ClusterSlave"/>.
        /// <c>null</c> for non-Brain/MuscleGround roles.
        /// </summary>
        public EcsRecordReplayController? RecordReplayController { get; private set; }

        /// <summary>
        /// After <see cref="RegisterMigrationServices"/> is called, exposes the
        /// constructed <see cref="MigrationServices"/> bundle.
        /// </summary>
        public MigrationServices? MigrationServices { get; private set; }

        /// <summary>
        /// Constructs and stores <see cref="MigrationServices"/> for the given node role.
        /// <para>
        /// Roles:
        /// <list type="bullet">
        ///   <item>Brain / MuscleGround -- SimHost/CGF profile</item>
        ///   <item>ImageGenerator -- IG profile</item>
        /// </list>
        /// </para>
        /// </summary>
        public MigrationServices RegisterMigrationServices(NodeRole role,
            string? writerIdentifier = null)
        {
            MigrationServices ms;

            if (role.HasFlag(NodeRole.Map2D))
                ms = HrotMigrationBootstrap.BuildIg();
            else
                ms = HrotMigrationBootstrap.BuildSimHostCgf(
                    writerIdentifier ?? "Hrot.SimHost");

            MigrationServices = ms;
            return ms;
        }

        // ── Orchestration construction ─────────────────────────────────────────

        /// <summary>
        /// Creates a <see cref="ClusterSlave"/> with role-appropriate
        /// <see cref="IClusterOpHandler"/> registrations.
        /// <para>
        /// When <paramref name="participant"/> is provided the slave publishes
        /// heartbeats and subscribes to <c>NodeOpCommand</c> messages. Pass
        /// <c>null</c> in unit tests that only verify handler registration.
        /// </para>
        /// </summary>
        /// <param name="role">Node role that determines which handlers are wired.</param>
        /// <param name="kernel">Kernel used by <see cref="EcsRecordReplayController"/> for module installs.</param>
        /// <param name="world">Live entity repository forwarded to the controller.</param>
        /// <param name="nodeId">Local node identifier embedded in recording file names and heartbeats.</param>
        /// <param name="participant">Optional DDS participant. Supply to enable heartbeat/command DDS I/O.</param>
        /// <param name="subsystemName">Subsystem name published in heartbeats (default <c>"SimHost"</c>).</param>
        /// <param name="eventBus">Optional event bus; when provided, a <c>LiveLoadClusterStateHandler</c> is
        /// registered and <see cref="Hrot.Common.Orchestration.ClusterStateChangedEvent"/> will be
        /// published on commit.</param>
        /// <param name="scenarioSerializer">
        /// Optional scenario serializer; when provided, a <c>ScenarioLoadClusterStateHandler</c> is registered
        /// so the node can load its scenario file during <see cref="NodeOpType.PrepareLive"/> (CGF1-S0307).
        /// Must be supplied together with <paramref name="scenarioExtractor"/>, <paramref name="scenarioSource"/>,
        /// and <paramref name="scenarioIdAllocator"/>.
        /// </param>
        /// <param name="scenarioExtractor">
        /// Extracts <see cref="Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest"/> items from raw scenario JSON.
        /// Required when <paramref name="scenarioSerializer"/> is non-null.
        /// </param>
        /// <param name="scenarioSource">
        /// ⭐ <c>CE-3228</c> — the bus the extracted entity creation requests are published on (the world's, which the creation pack
        /// reads; Entity Creation §3.4d). Required when <paramref name="scenarioSerializer"/> is non-null.
        /// </param>
        /// <param name="scenarioIdAllocator">
        /// Allocates stable network IDs for newly created entities during scenario load.
        /// Required when <paramref name="scenarioSerializer"/> is non-null.
        /// </param>
        /// <param name="localTempRoot">
        /// Local staging directory root used by <c>ScenarioLoadClusterStateHandler</c> to locate pre-fetched
        /// scenario files.  Defaults to <see cref="OrchestrationConstants.ResolveStagingRoot"/>.
        /// </param>
        /// <param name="tkbDb">
        /// Optional TKB database. When non-null, a <see cref="TkbLoadClusterStateHandler"/>
        /// is registered before the scenario handler block so TKB loading precedes scenario
        /// deserialization during <c>PrepareLive</c> and <c>PrepareEdit</c>.
        /// </param>
        /// <param name="simGroup">
        /// Optional <see cref="Fdp.ModuleHost.Scheduling.TogglableSimulationGroup"/>.  When provided (or together
        /// with <paramref name="inputGroup"/>, <paramref name="postSimGroup"/>, or <paramref name="lifecycleGroup"/>),
        /// a <see cref="ReferenceReplayLoadHandler"/> is registered and these objects are disabled /
        /// re-enabled during <c>PrepareReplay</c> / <c>FinalizeReplay</c> transitions
        /// (CGF1-S0304).
        /// </param>
        /// <param name="lifecycleGroup">
        /// Optional <see cref="Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup"/> gating
        /// the three network lifecycle systems during replay.
        /// </param>
        /// <param name="scenarioHandlers">
        /// ⭐ CE-1446 B0 — the host's OWN scenario/episode handlers (CGF: <c>CgfEpisodeLoadHandler</c>), registered in place of the default <c>ReferenceEpisodeLoadHandler</c>. Null = the default.
        /// </param>
        /// <param name="behaviorRemapper">
        /// ⭐ CE-1446 B0 — given to the scenario load step so a load that renumbers ids rewrites the EntityRefs inside mission parameters (CGF and the editor hold a behaviour registry and pass one).
        /// </param>
        /// <param name="offline">
        /// ⭐ CE-1446 B0 — an OFFLINE node (the editor): no DDS participant by design, so a Brain role does not need one. Skips the participant guard; the DDS-only parts already key on the participant.
        /// </param>
        /// <param name="entityMap">
        /// ⭐ CE-1446 B1 — the node's entity map, so the world boundary clears it (CE-2101) and the load chain forgets network ids with it. Null = the map is found on the world (the dry run) and the chain holds none.
        /// </param>
        /// <param name="profileLoadParts">
        /// ⭐ CE-4078 (R-1046) — the load parts the node's PROFILE adds (the Player: <c>LoadPart.Map3D</c>), after its roles' parts. Null/empty = none.
        /// </param>
        /// <param name="mapBuildStatus">
        /// ⭐ CE-4078 — where the 3-D map step reads the map's build status from (<see cref="Fdp.Toolkit.Orchestration.IMapBuildStatus"/>), resolved on every poll. Null result = nothing to build.
        /// </param>
        /// <param name="dryRunIncludesTransient">
        /// ⭐ CE-1446 B2 — the dry run (preview) copies Transient components too (the editor's did, HN-016).
        /// </param>
        /// <param name="ghostCreationSystem">
        /// Optional <see cref="Fdp.Toolkit.Replication.Systems.GhostCreationSystem"/> whose
        /// <see cref="Fdp.Toolkit.Replication.Systems.GhostCreationSystem.BypassLifecycle"/>
        /// flag is toggled during replay transitions.
        /// </param>
        public ClusterSlave BuildOrchestration(
            NodeRole role,
            Fdp.ModuleHost.ModuleHostKernel kernel,
            Fdp.Core.EntityRepository world,
            int nodeId,
            CycloneDDS.Runtime.DdsParticipant? participant = null,
            string subsystemName = "SimHost",
            Fdp.Core.FdpEventBus? eventBus = null,
            Fdp.Toolkit.Scenario.ScenarioSerializer? scenarioSerializer = null,
            IScenarioEntityExtractor? scenarioExtractor = null,
            Fdp.Core.FdpEventBus? scenarioSource = null,
            INetworkIdAllocator? scenarioIdAllocator = null,
            string? localTempRoot = null,
            ITkbDatabase? tkbDb = null,             // TKB-020: used by TkbLoadClusterStateHandler
            Fdp.ModuleHost.Scheduling.TogglableSimulationGroup?    simGroup = null,
            Fdp.ModuleHost.Scheduling.TogglableInputGroup?          inputGroup = null,
            Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup? postSimGroup = null,
            Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup?  lifecycleGroup = null,
            Fdp.Toolkit.Replication.Systems.GhostCreationSystem? ghostCreationSystem = null,
            Fdp.Core.EventAccumulator? eventAccumulator = null,
            Action? afterSeek = null,
            Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler? diagnosticsDumpHandler = null,
            // ⭐ HN-018 — the THIRD rewind participant (§2b): the ELM's in-flight construction/destruction
            //   queues, which a world replacement invalidates and nothing else resets.
            //   ⛔ A caller that HAS an ELM must PASS it — an unpassed one is the silent-default defect, and
            //   the preview/replay boundary then leaves stale entries behind (CE-259ar) and restored
            //   Constructing entities undriven. 📄 docs/designs/replay-and-modules/DESIGN.md §2.1m step 2.
            Fdp.Toolkit.Lifecycle.EntityLifecycleModule? elm = null,
            // ⭐ CE-4013 (§4.1ae T1) — the node's terrain content (HrotNodeContext.TerrainContent). ⛔ A caller that HAS a context passes it.
            Hrot.Map.Common.Services.TerrainContentRegistry? terrainContent = null,
            // ⭐⭐ CE-4013 D15 — the node's ONE cluster slave and ONE slave translator (HrotNodeBuilder step 8). ⛔ This used to build a
            //   second pair, so every host on this path ran two NedSlaveOrchestrationTranslators — the builder's never ticked, its DDS
            //   readers never drained. A caller that HAS them passes them; only a caller with no context builds new ones here.
            ClusterSlave? clusterSlave = null,
            Hrot.Core.Network.ISlaveOrchestrationTranslator? slaveTranslator = null,
            IReadOnlyList<IClusterStateHandler>? scenarioHandlers = null,
            Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper? behaviorRemapper = null,
            bool offline = false,
            Fdp.Toolkit.Replication.Services.NetworkEntityMap? entityMap = null,
            bool dryRunIncludesTransient = false,
            IReadOnlyList<LoadPart>? profileLoadParts = null,
            Func<Fdp.Toolkit.Orchestration.IMapBuildStatus?>? mapBuildStatus = null,
            // ⭐ CE-3354 ② — the node's name for the slave built here (only when no slave is passed; a caller with a context passes ITS slave, which carries it).
            string? nodeName = null)
        {
            if (!offline && participant == null && role.HasFlag(NodeRole.Brain))
                throw new ArgumentNullException(nameof(participant),
                    $"[SimHost] A DDS participant is required for orchestration role '{role}'. " +
                    "ClusterSlave cannot run without DDS in production.");

            localTempRoot ??= OrchestrationConstants.ResolveStagingRoot();

            // P1/CE-285: advertise the full declared role (fdp.role.* tokens → derived mask, CE-286) + fdp.reliable-init.
            // ⭐ + the asset tokens derived from the role (docs/DESIGN_Asset_Management.md §7.3a, CE-3020).
            clusterSlave ??= new ClusterSlave(nodeId, subsystemName, eventBus, role,
                capabilities: Hrot.Map.Common.ClusterLoad.AssetNeeds.HostCapabilities(role), nodeName: nodeName);
            SlaveTranslator = slaveTranslator;
            if (SlaveTranslator == null && participant != null && eventBus != null)
            {
                // HEXAG2-S012: use factory when available; fall back to null-object otherwise.
                var nodeFactory = _networkFactory?.ConfigureForNode(participant, nodeId, role);
                SlaveTranslator = nodeFactory?.CreateSlaveOrchestratorTranslators(eventBus, nodeId)
                    ?? new Hrot.Core.Network.NullSlaveOrchestrationTranslator();
            }
            var storageProvider = new LocalDiskStorageProvider(localTempRoot);

            // ⭐⭐⭐ L2/L4 — THE LOAD-PHASE PROVIDERS. The chain itself is registered by the shared composition below.
            // 🔴 Measured 2026-09-18: TkbLoadClusterStateHandler and TerrainLoadClusterStateHandler used to be separate
            //    handlers, and ClusterSlave gives an operation to the FIRST claimant — the TKB loader shadowed terrain here,
            //    and on CGF terrain shadowed the scenario. ⭐ The ROLE says WHAT must be resident (RoleLoadRequirements),
            //    this host says HOW; a required part with no provider throws at composition. The knowledge base is
            //    UNCONDITIONAL for an ECS node, so the shared factory supplies the hard-coded catalogue as its default.
            // 📄 docs/DESIGN_Cluster_Load_Phase.md §4.1b, §4.1c · DESIGN_Node_Roles_And_Policies.md §3.2.
            // ⭐ CE-4013 (§4.1ae T2) — the knowledge base and the terrain from the ONE shared factory every ECS host uses.
            var (loadProviders, residency) = HrotUniversalLoadParts.Build(tkbDb, localTempRoot, RoadNetworkHolder, terrainContent);
            if (_navmeshFactory != null) residency.AttachNavmesh(_navmeshFactory, _navmeshTarget!);
            TerrainResidency = residency;   // ⭐ CE-1446 B1 — held so a host's terrain picker / save can read it (CGF did, from its own Build call)

            IClusterStateHandler? scenarioSaveHandler = null;
            var handlersToRegister = new List<IClusterStateHandler>();
            if (scenarioSerializer != null)
            {
                // ⭐⭐⭐ CE-275 ③ / CE-279 — the ONE scenario SAVE handler (same class every host registers); it needs ONLY
                //   the serializer, so every ECS host — muscle included — saves its OWNED slice.
                //   📄 DESIGN_Distributed_Scenario_Persistence.md §4 · DESIGN_Unified_Cluster_Handler_Registration.md.
                scenarioSaveHandler = new Hrot.ScenarioEditor.Handlers.HrotScenarioSaveHandler(
                    scenarioSerializer, world, nodeId);

                // ⭐⭐ L4a — the ONE scenario step, offered to the chain; whether it is REQUIRED is the ROLE's answer, and a
                //   Brain node missing these deps fails loudly at composition instead of loading an empty world.
                if (scenarioExtractor != null && scenarioSource != null && scenarioIdAllocator != null)
                {
                    var scenarioLoader = new HrotScenarioLoader(storageProvider, scenarioSerializer.SubsystemType);

                    loadProviders.Add(new ScenarioLoadStep(
                        scenarioSerializer, scenarioLoader, scenarioExtractor, scenarioSource,
                        scenarioIdAllocator,
                        behaviorRemapper: behaviorRemapper,
                        // ⭐ C3 — the LOCAL terrain invocation, made at the one frame the zone entities provably exist.
                        //   ⛔ Not a NodeOp: we are inside the cluster's own load transaction and a nested 2PC would deadlock.
                        terrainLoadService: TerrainLoadService));

                    if (scenarioHandlers != null) handlersToRegister.AddRange(scenarioHandlers);
                    else handlersToRegister.Add(new ReferenceEpisodeLoadHandler(scenarioSerializer, scenarioLoader, world: null));
                }
            }

            // ⭐⭐⭐ D16 (R-3046) — the cluster handlers of an ECS host, composed in ONE place for every host
            //   (this one, CgfSubsystem, EditorSubsystem). 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1b.
            // ⭐⭐ P5a / R-4011 — EVERY ECS host holds the recording controller, whatever its role; ForNode clears the ELM's
            //   bookkeeping at a seek (§2.1m step 3) and gates the ELM while a replay is active (step 1).
            // ⭐ HN-017/HN-018 — the dry run puts back this node's id pool, entity map and ELM queues. ⚠ No entity map is
            //   passed ON PURPOSE: SimHostApp sets the NetworkEntityMap singleton AFTER this composition, so the dry run and
            //   the world boundary (CE-2101) resolve it from the world when they run.
            var handlers = EcsHostClusterHandlers.Compose(new EcsHostClusterHandlerInputs
            {
                Slave              = clusterSlave,
                Kernel             = kernel,
                World              = world,
                NodeId             = nodeId,
                Role               = role,
                HostLabel          = subsystemName,
                StagingRoot        = localTempRoot,
                TransportAfterSeek = afterSeek,
                Elm                = elm,
                InputGroup         = inputGroup,
                SimGroup           = simGroup,
                PostSimGroup       = postSimGroup,
                LifecycleGroup     = lifecycleGroup,
                BypassLifecycle    = ghostCreationSystem != null
                    ? bypass => ghostCreationSystem.BypassLifecycle = bypass
                    : null,
                EventAccumulator   = eventAccumulator,
                IdAllocator        = scenarioIdAllocator,
                EntityMap          = entityMap,
                DryRunIncludesTransient = dryRunIncludesTransient,
                ProfileLoadParts   = profileLoadParts ?? Array.Empty<LoadPart>(),
                MapBuildStatus     = mapBuildStatus,
                LoadProviders      = loadProviders,
                ScenarioHandlers   = handlersToRegister,
                ScenarioSaveHandler = scenarioSaveHandler,
                TerrainService     = TerrainLoadService,
                TerrainStagingRoot = localTempRoot,
            });
            RecordReplayController = handlers.RecordReplayController;
            Handlers = handlers;

            // Wire DiagnosticsDumpClusterOpHandler for cluster-wide diagnostic dumps.
            if (diagnosticsDumpHandler != null)
                clusterSlave.RegisterHandler(diagnosticsDumpHandler);

            return clusterSlave;
        }
    }
}

