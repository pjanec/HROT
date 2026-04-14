using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;

namespace Hrot.IG.Modules.Orchestration
{
    /// <summary>
    /// Dummy Cluster handler for zone-load operations on the IG node
    /// (CGF-1-BATCH-23 A.2).
    ///
    /// <para>
    /// IG is a pure network renderer and has no zone / terrain-DB state of its
    /// own.  The orchestrator may fan out <c>PrepareZone</c> and
    /// <c>CommitZone</c> commands to all roster members including IG when the
    /// cluster transitions through <c>LoadingEdit</c>.  This handler ensures those
    /// commands are acknowledged promptly so the 2PC round is not stalled.
    /// </para>
    ///
    /// <para>
    /// <b>Future work:</b> Full terrain-DB preload from scenario entities (receiving
    /// map-tile / height-map references embedded in the zone command payload)
    /// is not yet implemented.  When IG acquires a terrain-streaming subsystem, this
    /// handler should be replaced with a real implementation that kicks off the tile
    /// prefetch in <see cref="PrepareAsync"/> and signals completion in
    /// <see cref="Commit"/>.
    /// </para>
    /// </summary>
    public sealed class IgZoneDummyHandler : IClusterStateHandler
    {
        private readonly long _localNodeId;

        public IgZoneDummyHandler(long localNodeId = 0)
        {
            _localNodeId = localNodeId;
        }
        /// <inheritdoc />
        public bool CanHandle(Fdp.Toolkit.Orchestration.NodeOpType operation)
            => operation == Fdp.Toolkit.Orchestration.NodeOpType.PrepareZone
            || operation == Fdp.Toolkit.Orchestration.NodeOpType.CommitZone;

        /// <inheritdoc />
        /// <remarks>
        /// No-op.  Returns <see langword="null"/> immediately (success) so that
        /// the ClusterSlave can ACK the orchestrator without delay.
        /// Terrain-DB preload from scenario entities is future work.
        /// </remarks>
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            FdpLog<IgZoneDummyHandler>.Info(
                "[Node-{0}] Zone op {1} — dummy ACK (terrain preload is future work).",
                _localNodeId, intent.Operation);
            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
    }
}
