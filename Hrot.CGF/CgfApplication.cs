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
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Core;
using ModuleHost.Core.Time;

namespace Hrot.CGF
{
    /// <summary>
    /// CGF simulation node.  Owns the DDS participant, <see cref="ClusterSlave"/> lifecycle,
    /// and a minimal time kernel so the node participates in distributed lockstep stepping.
    /// </summary>
    public sealed class CgfApplication : IDisposable
    {
        private const int DefaultNodeId = 400;
        private const string SubsystemName = "CGF";

        private readonly DdsParticipant _participant;
        private readonly FDP.Toolkit.Orchestration.ClusterSlave _clusterSlave;
        private readonly FdpEventBus _eventBus;
        private readonly IDescriptorTranslator _timeModeTranslator;
        private readonly IDescriptorTranslator _lockstepTranslator;
        // Minimal time kernel so CGF participates in distributed lockstep stepping.
        private readonly EntityRepository _timeWorld;
        private readonly ModuleHostKernel _timeKernel;
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
            // CGF1-A.2 (BATCH-09 / Phase 3): wire time event bridge and minimal time kernel.
            _eventBus = new FdpEventBus();
            _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);
            _lockstepTranslator = TimeNetworkModule.CreateSlaveLockstepTranslator(_participant, _eventBus, nodeId);

            // Minimal time kernel: provides a monotonic wall-clock reference and hosts the
            // SlaveTimeController / SteppedSlaveController that the SlaveTimeModeListener swaps.
            _timeWorld  = new EntityRepository();
            _timeKernel = new ModuleHostKernel(_timeWorld, new EventAccumulator());
            _timeKernel.SetTimeController(new SlaveSyncController(_eventBus, nodeId));
            _timeKernel.Initialize();

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
                storageDirectory: localTempRoot,
                transport:        transport,
                nodeId:           nodeId));

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
            // Bridge FrameOrder/FrameAck for distributed lockstep stepping.
            _lockstepTranslator.ScanAndPublish(null!);
            _lockstepTranslator.PollIngress(null!, null!);
            // Advance time kernel (drives SlaveSyncController and processes FrameOrder when stepped).
            _timeKernel.Update();
            _eventBus.SwapBuffers();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clusterSlave.Dispose();
            _timeKernel.Dispose();
            _participant.Dispose();
            FdpLog<CgfApplication>.Info("[CGF] Disposed.");
        }
    }
}
