using System;
using System.Threading;
using Hrot.CGF.Modules.Orchestration;
using Hrot.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using FDP.Toolkit.Time;

namespace Hrot.CGF
{
    /// <summary>
    /// Minimal CGF application shell.  Owns the DDS participant and <see cref="ClusterSlave"/>
    /// lifecycle.  In Phase 1 the CGF subsystem acts only as a heartbeating ClusterSlave;
    /// AI and entity logic are added in Phase 4.
    ///
    /// <para><b>Time bus (CGF1-A.2 — Option B — Phase 3 note):</b>
    /// A <see cref="FDP.Toolkit.Time.TimeNetworkModule"/>.<c>CreateDescriptorTranslator</c>
    /// instance is wired to the private <c>_eventBus</c> so that
    /// <c>SwitchTimeModeWireDto</c> samples are bridged on/off DDS each frame via
    /// <see cref="Tick"/>.  This proves the wire path is functional.
    /// However <see cref="ClusterSlave"/> is constructed <em>without</em> that bus and
    /// no <c>SlaveTimeModeListener</c> is registered, so ingressed
    /// <c>SwitchTimeModeEvent</c> messages are not acted on by this shell.
    /// Full time-mode switching (CGF1-S0205 end-to-end: <c>SteppedSlaveController</c>
    /// via Future Barrier) requires wiring a <c>ModuleHostKernel</c> and
    /// <c>SlaveTimeModeListener</c>, which land in Phase 3+ when the CGF subsystem
    /// acquires simulation entity management.</para>
    /// </summary>
    public sealed class CgfApplication : IDisposable
    {
        private const int DefaultNodeId = 400;
        private const string SubsystemName = "CGF";

        private readonly DdsParticipant _participant;
        private readonly FDP.Toolkit.Orchestration.ClusterSlave _clusterSlave;
        private readonly FdpEventBus _eventBus;
        private readonly IDescriptorTranslator _timeModeTranslator;
        private bool _disposed;

        /// <summary>Exposes the <see cref="FDP.Toolkit.Orchestration.ClusterSlave"/> for test assertions.</summary>
        public FDP.Toolkit.Orchestration.ClusterSlave ClusterSlave => _clusterSlave;

        /// <param name="domainId">DDS domain used for all topics.</param>
        /// <param name="nodeId">
        /// Node identifier published in <see cref="NodeHeartbeat.NodeId"/>.
        /// Defaults to <c>400</c>.
        /// </param>
        /// <param name="scenarioSerializer">
        /// Optional pre-built scenario serializer (CGF1-S0307).  When provided,
        /// <see cref="ReferenceScenarioLoadHandler"/> and <see cref="ReferenceEpisodeLoadHandler"/>
        /// are registered so the CGF node participates in scenario/episode cluster operations
        /// (header-peek / ACK only until record/replay wiring lands on CGF).
        /// </param>
        /// <param name="localTempRoot">
        /// Local staging directory root for pre-fetched scenario files.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public CgfApplication(int domainId = 0, int nodeId = DefaultNodeId,
            ScenarioSerializer? scenarioSerializer = null, string localTempRoot = @"C:\FDP_Temp")
        {
            _participant = new DdsParticipant((uint)domainId);
            // CGF1-A.2 (BATCH-09): wire SwitchTimeModeEvent bridge.
            _eventBus = new FdpEventBus();
            _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);

            var transport = new DdsOrchestrationTransport(_participant, nodeId);
            _clusterSlave   = new FDP.Toolkit.Orchestration.ClusterSlave(transport, nodeId, SubsystemName);

            var storageProvider = new LocalDiskStorageProvider(localTempRoot);

            // CGF1-BATCH-23 A.1: brain-appropriate record/replay controller seam.
            // Phase 3 skeleton: participates in the cluster handshake (ACK) without writing
            // ECS frames.  Shared between ReferenceReplayLoadHandler and
            // ReferenceLiveLoadHandler so IsReplayActive state is consistent.
            var rrController = new CgfRecordReplayController();

            // Wire ReferenceReplayLoadHandler FIRST so it claims PrepareReplay /
            // FinalizeReplay unconditionally and claims PrepareLive only when a replay
            // session is currently active (Live-from-Replay branch, CGF1-S0305).
            _clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
                rrController,
                simGroup:              null,
                lifecycleGroup:        null,
                bypassLifecycleToggle: null,
                transport,
                nodeId,
                storageDirectory:      localTempRoot));

            // CGF1-S0307: wire scenario load handler when a serializer is provided.
            // Registered AFTER ReferenceReplayLoadHandler so the replay branch is
            // checked first; ReferenceScenarioLoadHandler claims cold PrepareLive.
            if (scenarioSerializer != null)
            {
                // CGF header-peek-only path: world=null because CGF has no ECS repo.
                _clusterSlave.RegisterHandler(
                    new ReferenceScenarioLoadHandler(scenarioSerializer, storageProvider, world: null));

                // CGF1-S0308: wire episode handler; CGF is header-peek only (world=null).
                _clusterSlave.RegisterHandler(
                    new ReferenceEpisodeLoadHandler(scenarioSerializer, storageProvider,
                        world: null, transport, nodeId));
            }

            // Wire ReferenceLiveLoadHandler AFTER the scenario handler so it only
            // claims FinalizeLive (and cold PrepareLive when no scenario handler
            // was registered).  controller is shared with ReferenceReplayLoadHandler.
            _clusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(
                checkpointWorker: null,
                controller:       rrController,
                storageDirectory: localTempRoot));

            // Wire ReferencePrefetchHandler so this node can stage scenario files and ACK.
            _clusterSlave.RegisterHandler(new ReferencePrefetchHandler(transport, nodeId, storageProvider));

            // CGF1-S0309: wire dry-run handler (no ECS state on CGF skeleton).
            _clusterSlave.RegisterHandler(new ReferencePreviewHandler(liveRepo: null));

            FdpLog<CgfApplication>.Info("[CGF] Initialized on domain {0}, nodeId {1}.", domainId, nodeId);
        }

        /// <summary>
        /// Advances one application frame.  Call at the desired tick rate (e.g. 60 Hz or
        /// slower in headless scenarios).
        /// </summary>
        public void Tick()
        {
            _clusterSlave.Tick();
            // Bridge SwitchTimeModeEvent: egress coordinator events to DDS, ingress DDS events to bus.
            _timeModeTranslator.ScanAndPublish(null!);
            _timeModeTranslator.PollIngress(null!, null!);
            _eventBus.SwapBuffers();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clusterSlave.Dispose();
            _participant.Dispose();
            FdpLog<CgfApplication>.Info("[CGF] Disposed.");
        }
    }
}
