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
using Hrot.Common.Orchestration.Handlers;
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
        public TerrainLoadService TerrainLoadService { get; } =
            new TerrainLoadService(new AnnouncingZoneTileLoader());

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
        /// Extracts <see cref="Hrot.Core.Network.EntityCreationRequest"/> items from raw scenario JSON.
        /// Required when <paramref name="scenarioSerializer"/> is non-null.
        /// </param>
        /// <param name="scenarioSource">
        /// Queue through which extracted entity creation requests are fed into the genesis pipeline.
        /// Required when <paramref name="scenarioSerializer"/> is non-null.
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
        /// <param name="checkpointWorker">
        /// Optional <see cref="CheckpointIOWorker"/> owned by the caller.  When provided a
        /// <see cref="CheckpointClusterOpHandler"/> is registered (handles <c>TakeSnapshot</c>) and the
        /// same worker is forwarded to <see cref="LiveLoadClusterStateHandler"/> so that
        /// <c>FinalizeLive</c> awaits checkpoint drain before unloading (CGF1-S0303 A.1).
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
            ScenarioEntityCreationRequestSource? scenarioSource = null,
            INetworkIdAllocator? scenarioIdAllocator = null,
            string? localTempRoot = null,
            ITkbDatabase? tkbDb = null,             // TKB-020: used by TkbLoadClusterStateHandler
            CheckpointIOWorker? checkpointWorker = null,
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
            Fdp.Toolkit.Lifecycle.EntityLifecycleModule? elm = null)
        {
            if (participant == null && role.HasFlag(NodeRole.Brain))
                throw new ArgumentNullException(nameof(participant),
                    $"[SimHost] A DDS participant is required for orchestration role '{role}'. " +
                    "ClusterSlave cannot run without DDS in production.");

            localTempRoot ??= OrchestrationConstants.ResolveStagingRoot();

            // P1/CE-285: advertise the full declared role (fdp.role.* tokens → derived mask, CE-286) + fdp.reliable-init.
            var clusterSlave = new ClusterSlave(nodeId, subsystemName, eventBus, role,
                capabilities: new[] { Fdp.Toolkit.Replication.CapabilityTokens.ReliableInit });
            SlaveTranslator = null;
            if (participant != null && eventBus != null)
            {
                // HEXAG2-S012: use factory when available; fall back to null-object otherwise.
                var nodeFactory = _networkFactory?.ConfigureForNode(participant, nodeId, role);
                SlaveTranslator = nodeFactory?.CreateSlaveOrchestratorTranslators(eventBus, nodeId)
                    ?? new Hrot.Core.Network.NullSlaveOrchestrationTranslator();
            }
            var storageProvider = new LocalDiskStorageProvider(localTempRoot);

            // Create EcsRecordReplayController for Brain-tier and MuscleGround nodes.
            // MuscleGround (SimHost) must also handle PrepareReplay/FinalizeReplay so that
            // replay transitions can ACK back to ClusterMaster and not time out.
            // ⭐⭐ §2.1m step 3 — A SEEK IS A WORLD REPLACEMENT TOO. Composed into the existing afterSeek
            //   chain, right beside the NetworkEntityMap.RebuildFromWorld that already lives there for
            //   exactly this reason (a non-recorded index that time travel invalidates).
            //   ⛔ Clear only — a seek stays inside the replay, so the re-derive is NOT armed.
            Action<bool>? elmWorldReplaced = elm == null ? null : elm.OnWorldReplaced;
            Action? afterSeekWithLifecycle = elm == null
                ? afterSeek
                : () => { elm.OnWorldReplaced(resumingToLive: false); afterSeek?.Invoke(); };

            EcsRecordReplayController? controller = null;
            if (role.HasFlag(NodeRole.Brain) || role.HasFlag(NodeRole.MuscleGround))
                controller = new EcsRecordReplayController(kernel, nodeId, world,
                    afterSeek: afterSeekWithLifecycle);
            RecordReplayController = controller;

            // ⭐⭐ Step 1 of DESIGN.md §2.1m: gate the ELM during replay, so LifecycleSystem's
            //    CheckTimeouts cannot run while a seek has rewound the frame counter behind a recorded
            //    StartFrame (the unsigned wrap of CE-259ar). ⭐ The producer already existed —
            //    IRecordReplayController.IsReplayActive — so no new state type was introduced.
            //    ⛔ Gated IN PLACE rather than relocated into NetworkLifecycleSystemGroup: that group's
            //    ExecuteGroup has exactly ONE caller (NedReplicationModule.Tick), so it never ticks on the
            //    editor or on BDC nodes. Authorised deviation from mgmt-1/DESIGN.md §8.10.
            if (elm != null && controller != null)
                elm.IsReplayActive = () => controller.IsReplayActive;

            // Wire ReferenceReplayLoadHandler BEFORE ReferenceLiveLoadHandler so the
            // dispatch loop considers the Live-from-Replay branch first (CGF1-S0305).
            if (controller != null && (inputGroup != null || simGroup != null || postSimGroup != null || lifecycleGroup != null))
            {
                Action<bool>? bypassToggle = ghostCreationSystem != null
                    ? bypass => ghostCreationSystem.BypassLifecycle = bypass
                    : (Action<bool>?)null;

                clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
                    controller, inputGroup, simGroup, postSimGroup, lifecycleGroup, bypassToggle,
                    localTempRoot,
                    suspendGlobalTimePush: kernel.SuspendGlobalTimePush,
                    resumeGlobalTimePush:  kernel.ResumeGlobalTimePush,
                    // ⭐⭐⭐ §2.1m step 3 — the ELM's bookkeeping is discarded at EVERY world replacement,
                    //   and the re-derive armed only when resuming to a LIVE world.
                    worldReplaced: elmWorldReplaced));
            }

            // Wire ReferenceCheckpointHandler when a checkpoint worker is provided (CGF1-S0303).
            if (checkpointWorker != null)
                clusterSlave.RegisterHandler(new ReferenceCheckpointHandler(
                    checkpointWorker, world, eventAccumulator ?? new Fdp.Core.EventAccumulator()));

            // Wire ReferencePreviewHandler for LoadingPreview / UnloadingPreview (CGF1-S0309).
            // ⭐⭐⭐ HN-017 — and this node restores its OWN id pool and entity map.
            // 📄 docs/DESIGN_Deterministic_Network_Ids.md §2b (the enumeration) · §4c (the approach) · §4d
            //    (as-built). 🔒 User `2026-08-23`: "the reset must be cluster wide" — the master's
            //    PrepareState broadcast reaches every node and each commits LOCALLY, so a per-node
            //    capture/restore here IS the cluster-wide reset. ⛔ No new protocol, and ⛔ nothing here
            //    talks to the central id authority.
            // ⚠⚠ THIS SITE HAD BOTH DEPENDENCIES AND PASSED NEITHER — the 2026-08-16 silent-default shape:
            //    `scenarioIdAllocator` is a parameter of this very method (used at the scenario handlers
            //    below) and the map is reachable through `world`. Leaving them out would have shipped a
            //    capability that does nothing on the one production node that runs the 2PC preview with a
            //    real repo.
            // ⚠ The map is resolved LATE (see EntityMapFromRepository): SimHostApp sets the singleton AFTER
            //   this method returns, so an eager lookup here would throw.
            var previewRewindables = new List<Fdp.Toolkit.Orchestration.Preview.IPreviewRewindable>();
            if (scenarioIdAllocator != null)
                previewRewindables.Add(
                    Fdp.Toolkit.Orchestration.Preview.PreviewParticipants.IdAllocator(scenarioIdAllocator));
            previewRewindables.Add(
                Fdp.Toolkit.Orchestration.Preview.PreviewParticipants.EntityMapFromRepository(world));
            // ⭐ HN-018 — the third participant. Independent of the map: an ELM exists on every node.
            if (elm != null)
                previewRewindables.Add(
                    Fdp.Toolkit.Orchestration.Preview.PreviewParticipants.LifecycleModule(elm));
            clusterSlave.RegisterHandler(new ReferencePreviewHandler(world, previewRewindables));

            // Wire ReferencePrefetchHandler so this node can stage scenario files and ACK.
            clusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider));

            // CE-279 Layer A — build the SerializeLocal-family handlers as locals and register them TOGETHER,
            // in canonical order, via SerializeLocalRegistrar (below) — the ONE way every host does it.
            var archiveHandler = new ReferenceArchiveHandler(localTempRoot, nodeId);
            IClusterStateHandler? scenarioSaveHandler = null;

            // ⭐⭐⭐ L2/L4 — THE LOAD-PHASE CHAIN. One participant, every required part, one acknowledgement.
            //
            // 🔴 What it replaces and WHY, measured 2026-09-18: TkbLoadClusterStateHandler and
            //    TerrainLoadClusterStateHandler were registered here as separate handlers, and ClusterSlave
            //    gives an operation to the FIRST claimant and RETURNS. So on this host the TKB loader
            //    shadowed the terrain loader and terrain had NEVER loaded; on CGF the terrain loader
            //    shadowed the scenario loader and the cluster loaded ZERO entities. Neither logged a thing.
            //
            // ⭐⭐ The chain is composed from roles × providers: the ROLE says WHAT must be resident
            //    (RoleLoadRequirements), this host says HOW. A required part with no provider throws HERE,
            //    at composition, instead of producing an empty world and ok:true.
            // ⚠ Providers are offered, not ordered — the chain orders them and drops what this role does
            //   not require (a Perception-only node needs no terrain; every ECS node needs the TKB).
            // 📄 docs/DESIGN_Cluster_Load_Phase.md §4.1b, §4.1c · DESIGN_Node_Roles_And_Policies.md §3.2.
            var loadProviders = new List<ILoadPartProvider>();

            // ⭐⭐⭐ The knowledge base is UNCONDITIONAL for an ECS node, so this host SUPPLIES a default
            //   rather than making every caller remember. 🔒 "every ECS enable node should be able to
            //   create entities so every needs the TKB loaded" — a node that can be asked to create an
            //   entity must be able to resolve its template.
            // ⚠ The fallback is the hard-coded catalogue, which is exactly where a node with no named TKB
            //   starts anyway; a scenario that names one then replaces it through the step. ⛔ This is the
            //   host supplying a HOW, not the requirement being relaxed — the chain still throws if the
            //   part is genuinely unsatisfiable.
            loadProviders.Add(new KnowledgeBaseLoadStep(
                tkbDb ?? Hrot.Map.Common.HrotEnvironment.CreateTkb(), localTempRoot));

            loadProviders.Add(new TerrainLoadStep(
                new TerrainResidency(localTempRoot, RoadNetworkHolder), localTempRoot));

            // ⭐⭐⭐ D3 — the ONE terrain/zone OP handler, via the shared registrar. Unconditional on every
            //   ECS host: a host with nothing to make resident still ACKs, which is what removes the
            //   role × kind matrix from the protocol entirely (§8.3 N6).
            TerrainAssetRegistrar.Register(clusterSlave, TerrainLoadService, world, nodeId, localTempRoot);

            // Scenario handlers when a serializer is provided.
            if (scenarioSerializer != null)
            {
                // ⭐⭐⭐ CE-275 ③ / CE-279 — the ONE scenario SAVE handler (same class every host registers). It
                //   needs ONLY the serializer (+ world/tkb), so it is built here INDEPENDENT of the LOAD
                //   deps: every ECS host — muscle included — saves its OWNED slice (empty by the gate when it
                //   owns nothing). Registered below via SerializeLocalRegistrar.
                //   📄 DESIGN_Distributed_Scenario_Persistence.md §4 · DESIGN_Unified_Cluster_Handler_Registration.md.
                scenarioSaveHandler = new Hrot.ScenarioEditor.Handlers.HrotScenarioSaveHandler(
                    scenarioSerializer, tkbDb, world, nodeId);

                // ⭐⭐ L4a — the ONE scenario step, offered to the chain. It is still conditional on the
                //   authoring deps, but the conditional now means only "this host can satisfy the part":
                //   whether the part is REQUIRED is the ROLE's answer, and a Brain node missing these deps
                //   now fails loudly at composition instead of loading an empty world.
                // ⛔ HrotScenarioLoadHandler / HrotEditLoadHandler are GONE — one step serves both the live
                //   and the edit target, with the target as a payload field rather than a second class.
                if (scenarioExtractor != null && scenarioSource != null && scenarioIdAllocator != null)
                {
                    var scenarioLoader = new HrotScenarioLoader(storageProvider, scenarioSerializer.SubsystemType);

                    loadProviders.Add(new ScenarioLoadStep(
                        scenarioSerializer, scenarioLoader, scenarioExtractor, scenarioSource,
                        scenarioIdAllocator,
                        // ⭐ C3 — the LOCAL terrain invocation, made at the one frame the zone entities
                        //   provably exist. ⛔ Not a NodeOp: we are inside the cluster's own load
                        //   transaction and a nested 2PC would deadlock.
                        terrainLoadService: TerrainLoadService));

                    clusterSlave.RegisterHandler(
                        new ReferenceEpisodeLoadHandler(scenarioSerializer, scenarioLoader, world: null));
                }
            }

            // ⭐⭐⭐ L2 — register the chain ONCE, after every provider this host can offer is known.
            //   ⚠ Before the SerializeLocal pair and the fallback live handler, so it claims the load
            //     operations; ReferenceLiveLoadHandler keeps FinalizeLive, which the chain never claims.
            clusterSlave.RegisterHandler(LoadPhaseChain.FromRoles(
                role, loadProviders, world,
                recordingController: controller,
                storageDirectory:    localTempRoot,
                hostLabel:           subsystemName));

            // CE-279 Layer A — register the SerializeLocal pair uniformly (save before archive; payload-aware
            //   CanHandle makes order non-load-bearing, but every host's slave is now identical here).
            SerializeLocalRegistrar.Register(clusterSlave, scenarioSaveHandler, archiveHandler);

            // Wire ReferenceLiveLoadHandler AFTER the scenario handler so it only claims
            // FinalizeLive and cold PrepareLive (when no scenario serializer was registered).
            clusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(
                checkpointWorker, controller, localTempRoot));

            // Wire DiagnosticsDumpClusterOpHandler for cluster-wide diagnostic dumps.
            if (diagnosticsDumpHandler != null)
                clusterSlave.RegisterHandler(diagnosticsDumpHandler);

            return clusterSlave;
        }
    }
}

