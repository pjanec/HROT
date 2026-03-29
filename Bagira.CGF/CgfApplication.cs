using System;
using System.Threading;
using Bagira.CGF.Modules.Orchestration;
using Bagira.CGF.Modules.Orchestration.Handlers;
using Bagira.Common.Orchestration.Handlers;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Scenario;
using FDP.Toolkit.Time;

namespace Bagira.CGF
{
    /// <summary>
    /// Minimal CGF application shell.  Owns the DDS participant and <see cref="DrillSlave"/>
    /// lifecycle.  In Phase 1 the CGF subsystem acts only as a heartbeating DrillSlave;
    /// AI and entity logic are added in Phase 4.
    ///
    /// <para><b>Time bus (CGF1-A.2 — Option B — Phase 3 note):</b>
    /// A <see cref="FDP.Toolkit.Time.TimeNetworkModule"/>.<c>CreateDescriptorTranslator</c>
    /// instance is wired to the private <c>_eventBus</c> so that
    /// <c>SwitchTimeModeWireDto</c> samples are bridged on/off DDS each frame via
    /// <see cref="Tick"/>.  This proves the wire path is functional.
    /// However <see cref="DrillSlave"/> is constructed <em>without</em> that bus and
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
        private readonly DrillSlave _drillSlave;
        private readonly FdpEventBus _eventBus;
        private readonly IDescriptorTranslator _timeModeTranslator;
        private bool _disposed;

        /// <summary>Exposes the <see cref="DrillSlave"/> for test assertions.</summary>
        public DrillSlave DrillSlave => _drillSlave;

        /// <param name="domainId">DDS domain used for all topics.</param>
        /// <param name="nodeId">
        /// Node identifier published in <see cref="NodeHeartbeat.NodeId"/>.
        /// Defaults to <c>400</c>.
        /// </param>
        /// <param name="scenarioSerializer">
        /// Optional pre-built scenario serializer (CGF1-S0307).  When provided a
        /// <see cref="ScenarioLoadDsmHandler"/> is registered on the <see cref="DrillSlave"/>
        /// so the CGF node participates in scenario load operations.
        /// </param>
        /// <param name="localTempRoot">
        /// Local staging directory root for pre-fetched scenario files.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public CgfApplication(int domainId = 0, int nodeId = DefaultNodeId,
            ScenarioSerializer? scenarioSerializer = null, string localTempRoot = @"C:\FDP_Temp")
        {
            _participant = new DdsParticipant((uint)domainId);
            _drillSlave = new DrillSlave(_participant, nodeId, SubsystemName);
            // CGF1-A.2 (BATCH-09): wire SwitchTimeModeEvent bridge so time-mode switches
            // coordinated by the orchestrator are received and forwarded on the CGF node.
            _eventBus = new FdpEventBus();
            _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);

            // CGF1-S0307: wire scenario load handler when a serializer is provided.
            if (scenarioSerializer != null)
                _drillSlave.RegisterHandler(new ScenarioLoadDsmHandler(scenarioSerializer, localTempRoot));

            // CGF1-S0309: wire dry-run snapshot/rewind handler (no ECS state on CGF skeleton).
            _drillSlave.RegisterHandler(new DryRunDsmHandler(liveRepo: null));

            // CGF1-BATCH-17 architecture note: explicit fail-loud stubs for recording/replay ops.
            // Until CGF hosts a recordable kernel, PrepareLive / FinalizeLive / PrepareReplay /
            // FinalizeReplay are unsupported — the stub logs Error so missing brain-side persistence
            // surfaces in structured logs rather than silently succeeding (no-op path).
            _drillSlave.RegisterHandler(new FailLoudRecordReplayStub(SubsystemName));

            FdpLog<CgfApplication>.Info("[CGF] Initialized on domain {0}, nodeId {1}.", domainId, nodeId);
        }

        /// <summary>
        /// Advances one application frame.  Call at the desired tick rate (e.g. 60 Hz or
        /// slower in headless scenarios).
        /// </summary>
        public void Tick()
        {
            _drillSlave.Tick();
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
            _drillSlave.Dispose();
            _participant.Dispose();
            FdpLog<CgfApplication>.Info("[CGF] Disposed.");
        }
    }
}
