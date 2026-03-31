using System;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;

namespace Hrot.Common.Orchestration
{
    /// <summary>
    /// Adapts a <see cref="IClusterOpHandler"/> (Hrot interface, takes <see cref="NodeOpCommand"/>)
    /// to the <see cref="FDP.Toolkit.Orchestration.IClusterStateHandler"/> interface expected by
    /// <see cref="FDP.Toolkit.Orchestration.ClusterSlave"/>.
    ///
    /// <para>
    /// Used during the G0402→G0404 migration window so existing Hrot handler
    /// implementations can be registered with the toolkit ClusterSlave without change.
    /// Remove once all handlers are migrated to implement
    /// <see cref="FDP.Toolkit.Orchestration.IClusterStateHandler"/> directly (G0404/G0405).
    /// </para>
    /// </summary>
    public sealed class HrotHandlerAdapter : FDP.Toolkit.Orchestration.ITickableClusterStateHandler
    {
        private readonly IClusterOpHandler         _inner;
        private readonly EntityRepository?   _repo;

        /// <summary>
        /// Creates an adapter wrapping <paramref name="inner"/>.
        /// </summary>
        /// <param name="inner">The Hrot-layer handler to wrap.</param>
        /// <param name="repo">Optional entity repository forwarded to <c>Commit</c> and <c>Abort</c>.</param>
        public HrotHandlerAdapter(IClusterOpHandler inner, EntityRepository? repo = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _repo  = repo;
        }

        /// <summary>The wrapped Hrot-layer handler.</summary>
        public IClusterOpHandler InnerHandler => _inner;

        // ── FDP.Toolkit.Orchestration.IClusterStateHandler ────────────────────────────

        /// <inheritdoc />
        public bool CanHandle(int operationId) =>
            _inner.CanHandle((NodeOpType)operationId);

        /// <inheritdoc />
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct) =>
            _inner.PrepareAsync(ToNodeOpCommand(cmd), ct);

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo) =>
            _inner.Commit(ToNodeOpCommand(cmd), repo ?? _repo);

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) =>
            _inner.Abort(ToNodeOpCommand(cmd), repo ?? _repo);

        // ── FDP.Toolkit.Orchestration.ITickableClusterStateHandler ────────────────────

        /// <summary>
        /// Forwards to <see cref="ITickableClusterOpHandler.DrainDeferredAcks"/> when the
        /// inner handler implements that interface; otherwise a no-op.
        /// </summary>
        public void DrainDeferredAcks()
        {
            if (_inner is ITickableClusterOpHandler tickable)
                tickable.DrainDeferredAcks();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static NodeOpCommand ToNodeOpCommand(OrchestrationCommand cmd) =>
            new NodeOpCommand
            {
                TransactionId = cmd.TransactionId,
                TargetNodeId  = cmd.TargetNodeId,
                Operation     = (NodeOpType)cmd.OperationId,
                PayloadJson   = cmd.PayloadJson ?? string.Empty,
            };
    }
}
