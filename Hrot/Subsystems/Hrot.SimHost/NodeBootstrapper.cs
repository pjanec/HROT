using System;
using System.Collections.Generic;
using Hrot.Core.Network;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using CarKinem.Road;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Hrot.Common.Orchestration.Handlers;
using Hrot.Map.Common.Services;
using Hrot.SimHost.Orchestration.Handlers;
using Hrot.Common.Scenario;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;

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

        /// <param name="networkFactory">Optional network factory (reserved for future use).</param>
        public NodeBootstrapper(INetworkFactory? networkFactory = null)
        {
            _networkFactory = networkFactory;
        }

        /// <summary>
        /// After <see cref="BuildOrchestration"/> is called with a non-null <paramref name="participant"/>
        /// and <paramref name="eventBus"/>, this property holds the <see cref="NodeOpSlaveTranslator"/>
        /// that bridges DDS <-> the slave event bus (CMC-S016 / BATCH-06).
        /// <c>null</c> when no DDS participant was supplied.
        /// </summary>
        public Hrot.Common.Orchestration.NodeOpSlaveTranslator? SlaveTranslator { get; private set; }

        // ── Orchestration construction ─────────────────────────────────────────

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
        /// </param>
        /// <param name="localTempRoot">
        /// Local staging directory root used by <c>ScenarioLoadClusterStateHandler</c> to locate pre-fetched
        /// scenario files.  Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        /// <param name="checkpointWorker">
        /// Optional <see cref="CheckpointIOWorker"/> owned by the caller.  When provided a
        /// <see cref="CheckpointClusterOpHandler"/> is registered (handles <c>TakeSnapshot</c>) and the
        /// same worker is forwarded to <see cref="LiveLoadClusterStateHandler"/> so that
        /// <c>FinalizeLive</c> awaits checkpoint drain before unloading (CGF1-S0303 A.1).
        /// </param>
        /// <param name="simGroup">
        /// Optional <see cref="Fdp.Kernel.SimulationSystemGroup"/>.  When provided together
        /// with <paramref name="lifecycleGroup"/> and <paramref name="ghostCreationSystem"/>,
        /// a <see cref="ReplayLoadClusterOpHandler"/> is registered and these objects are disabled /
        /// re-enabled during <c>PrepareReplay</c> / <c>FinalizeReplay</c> transitions
        /// (CGF1-S0304).
        /// </param>
        /// <param name="lifecycleGroup">
        /// Optional <see cref="Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup"/> gating
        /// the three network lifecycle systems during replay.
        /// </param>
        /// <param name="ghostCreationSystem">
        /// Optional <see cref="FDP.Toolkit.Replication.Systems.GhostCreationSystem"/> whose
        /// <see cref="FDP.Toolkit.Replication.Systems.GhostCreationSystem.BypassLifecycle"/>
        /// flag is toggled during replay transitions.
        /// </param>
        public ClusterSlave BuildOrchestration(
            NodeRole role,
            Fdp.ModuleHost.ModuleHostKernel kernel,
            Fdp.Kernel.EntityRepository world,
            int nodeId,
            CycloneDDS.Runtime.DdsParticipant? participant = null,
            string subsystemName = "SimHost",
            Fdp.Kernel.FdpEventBus? eventBus = null,
            FDP.Toolkit.Scenario.ScenarioSerializer? scenarioSerializer = null,
            string localTempRoot = @"C:\FDP_Temp",
            CheckpointIOWorker? checkpointWorker = null,
            Fdp.Kernel.SimulationSystemGroup? simGroup = null,
            Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup? lifecycleGroup = null,
            FDP.Toolkit.Replication.Systems.GhostCreationSystem? ghostCreationSystem = null)
        {
            if (participant == null && role.HasFlag(NodeRole.Brain))
                throw new ArgumentNullException(nameof(participant),
                    $"[SimHost] A DDS participant is required for orchestration role '{role}'. " +
                    "ClusterSlave cannot run without DDS in production.");

            var clusterSlave = new ClusterSlave(nodeId, subsystemName, eventBus);
            SlaveTranslator = null;
            if (participant != null && eventBus != null)
            {
                SlaveTranslator = new Hrot.Common.Orchestration.NodeOpSlaveTranslator(
                    commandReader:   new CycloneDDS.Runtime.DdsReader<Hrot.NED.Descriptors.Orchestration.NodeOpCommand>(participant),
                    statusWriter:    new CycloneDDS.Runtime.DdsWriter<Hrot.NED.Descriptors.Orchestration.NodeOpStatus>(participant),
                    heartbeatWriter: new CycloneDDS.Runtime.DdsWriter<Hrot.NED.Descriptors.Orchestration.NodeHeartbeat>(participant),
                    bus:             eventBus,
                    nodeId:          nodeId);
            }
            var storageProvider = new LocalDiskStorageProvider(localTempRoot);

            // Create EcsRecordReplayController for Brain-tier nodes.
            EcsRecordReplayController? controller = null;
            if (role.HasFlag(NodeRole.Brain))
                controller = new EcsRecordReplayController(kernel, nodeId, world);

            // Wire ReferenceReplayLoadHandler BEFORE ReferenceLiveLoadHandler so the
            // dispatch loop considers the Live-from-Replay branch first (CGF1-S0305).
            if (controller != null && simGroup != null && lifecycleGroup != null)
            {
                Action<bool>? bypassToggle = ghostCreationSystem != null
                    ? bypass => ghostCreationSystem.BypassLifecycle = bypass
                    : (Action<bool>?)null;

                clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
                    controller, simGroup, lifecycleGroup, bypassToggle,
                    localTempRoot));
            }

            // Wire ReferenceCheckpointHandler when a checkpoint worker is provided (CGF1-S0303).
            if (checkpointWorker != null)
                clusterSlave.RegisterHandler(new ReferenceCheckpointHandler(
                    checkpointWorker, world));

            // Wire ReferencePreviewHandler for LoadingPreview / UnloadingPreview (CGF1-S0309).
            clusterSlave.RegisterHandler(new ReferencePreviewHandler(world));

            // Wire ReferencePrefetchHandler so this node can stage scenario files and ACK.
            clusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider));

            // Wire ReferenceArchiveHandler so this node can report .fdp archives to ClusterMaster (CGF1-S0505).
            clusterSlave.RegisterHandler(new ReferenceArchiveHandler(localTempRoot, nodeId));

            // Wire scenario/episode handlers when a serializer is provided.
            if (scenarioSerializer != null)
            {
                var scenarioLoader = new HrotScenarioLoader(storageProvider, scenarioSerializer.SubsystemType);
                var zoneService    = new ZoneManagerService();

                clusterSlave.RegisterHandler(
                    new HrotScenarioLoadHandler(scenarioSerializer, scenarioLoader, zoneService, world,
                        controller: controller,
                        storageDirectory: localTempRoot));

                clusterSlave.RegisterHandler(
                    new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(scenarioSerializer, scenarioLoader, zoneService, world));

                clusterSlave.RegisterHandler(
                    new ReferenceEpisodeLoadHandler(scenarioSerializer, scenarioLoader, world));
            }

            // Wire ReferenceLiveLoadHandler AFTER the scenario handler so it only claims
            // FinalizeLive and cold PrepareLive (when no scenario serializer was registered).
            clusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(
                checkpointWorker, controller, localTempRoot));

            return clusterSlave;
        }
    }
}

