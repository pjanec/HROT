using System;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Orchestration
{
    /// <summary>
    /// Adapts a <see cref="IClusterOpHandler"/> (Hrot interface, takes <see cref="NodeOpCommand"/>)
    /// to the <see cref="Fdp.Toolkit.Orchestration.IClusterStateHandler"/> interface expected by
    /// <see cref="Fdp.Toolkit.Orchestration.ClusterSlave"/>.
    ///
    /// <para>
    /// Used during the G0402→G0404 migration window so existing Hrot handler
    /// implementations can be registered with the toolkit ClusterSlave without change.
    /// Remove once all handlers are migrated to implement
    /// <see cref="Fdp.Toolkit.Orchestration.IClusterStateHandler"/> directly (G0404/G0405).
    /// </para>
    /// </summary>
    public sealed class HrotHandlerAdapter : Fdp.Toolkit.Orchestration.ITickableClusterStateHandler
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
        public bool CanHandle(Fdp.Toolkit.Orchestration.NodeOpType operation) =>
            _inner.CanHandle((Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)operation);

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            var cmd = ToNodeOpCommand(intent);
            return _inner.PrepareAsync(cmd, ct)
                         .ContinueWith(t => (object?)t.Result, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) =>
            _inner.Commit(ToNodeOpCommand(intent), repo ?? _repo);

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) =>
            _inner.Abort(ToNodeOpCommand(intent), repo ?? _repo);

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

        private static NodeOpCommand ToNodeOpCommand(ExecuteNodeOpIntent intent)
        {
            string payloadJson = intent.DomainPayload switch
            {
                null     => string.Empty,
                string s => s,
                _        => System.Text.Json.JsonSerializer.Serialize(intent.DomainPayload),
            };
            return new NodeOpCommand
            {
                TransactionId = intent.TransactionId,
                TargetNodeId  = intent.TargetNodeId,
                Operation     = (Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)intent.Operation,
                PayloadJson   = payloadJson,
            };
        }
    }
}
