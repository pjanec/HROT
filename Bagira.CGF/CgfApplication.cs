using System;
using System.Threading;
using Bagira.CGF.Modules.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

namespace Bagira.CGF
{
    /// <summary>
    /// Minimal CGF application shell.  Owns the DDS participant and <see cref="DrillSlave"/>
    /// lifecycle.  In Phase 1 the CGF subsystem acts only as a heartbeating DrillSlave;
    /// AI and entity logic are added in Phase 4.
    /// </summary>
    public sealed class CgfApplication : IDisposable
    {
        private const int DefaultNodeId = 400;
        private const string SubsystemName = "CGF";

        private readonly DdsParticipant _participant;
        private readonly DrillSlave _drillSlave;
        private bool _disposed;

        /// <summary>Exposes the <see cref="DrillSlave"/> for test assertions.</summary>
        public DrillSlave DrillSlave => _drillSlave;

        /// <param name="domainId">DDS domain used for all topics.</param>
        /// <param name="nodeId">
        /// Node identifier published in <see cref="NodeHeartbeat.NodeId"/>.
        /// Defaults to <c>400</c>.
        /// </param>
        public CgfApplication(int domainId = 0, int nodeId = DefaultNodeId)
        {
            _participant = new DdsParticipant((uint)domainId);
            _drillSlave = new DrillSlave(_participant, nodeId, SubsystemName);
            FdpLog<CgfApplication>.Info("[CGF] Initialized on domain {0}, nodeId {1}.", domainId, nodeId);
        }

        /// <summary>
        /// Advances one application frame.  Call at the desired tick rate (e.g. 60 Hz or
        /// slower in headless scenarios).
        /// </summary>
        public void Tick()
        {
            _drillSlave.Tick();
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
