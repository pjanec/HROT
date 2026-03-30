using System;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;

namespace Bagira.Common.Orchestration
{
    /// <summary>
    /// Adapts a <see cref="IDsmHandler"/> (Bagira interface, takes <see cref="NodeOpCommand"/>)
    /// to the <see cref="FDP.Toolkit.Orchestration.IDsmHandler"/> interface expected by
    /// <see cref="FDP.Toolkit.Orchestration.DrillSlave"/>.
    ///
    /// <para>
    /// Used during the G0402→G0404 migration window so existing Bagira handler
    /// implementations can be registered with the toolkit DrillSlave without change.
    /// Remove once all handlers are migrated to implement
    /// <see cref="FDP.Toolkit.Orchestration.IDsmHandler"/> directly (G0404/G0405).
    /// </para>
    /// </summary>
    public sealed class BagiraHandlerAdapter : FDP.Toolkit.Orchestration.ITickableDsmHandler
    {
        private readonly IDsmHandler         _inner;
        private readonly EntityRepository?   _repo;

        /// <summary>
        /// Creates an adapter wrapping <paramref name="inner"/>.
        /// </summary>
        /// <param name="inner">The Bagira-layer handler to wrap.</param>
        /// <param name="repo">Optional entity repository forwarded to <c>Commit</c> and <c>Abort</c>.</param>
        public BagiraHandlerAdapter(IDsmHandler inner, EntityRepository? repo = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _repo  = repo;
        }

        /// <summary>The wrapped Bagira-layer handler.</summary>
        public IDsmHandler InnerHandler => _inner;

        // ── FDP.Toolkit.Orchestration.IDsmHandler ────────────────────────────

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

        // ── FDP.Toolkit.Orchestration.ITickableDsmHandler ────────────────────

        /// <summary>
        /// Forwards to <see cref="ITickableDsmHandler.DrainDeferredAcks"/> when the
        /// inner handler implements that interface; otherwise a no-op.
        /// </summary>
        public void DrainDeferredAcks()
        {
            if (_inner is ITickableDsmHandler tickable)
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
