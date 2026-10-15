using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Fdp.Toolkit.Orchestration.Transfer
{
    /// <summary>
    /// ⭐⭐ <c>P6</c> part 2 (Q91 §4-E2) — picks the transport PER TARGET: a node that advertised a share
    /// (<see cref="TransferTokens"/>) gets today's copy; any other node gets the DDS transport, with each item's path made
    /// relative to that node's staging root (as the master sees it), which is where the receiver joins it to its own root.
    /// ⭐ Without a DDS transport every target goes to the share — today's behaviour, exactly.
    /// </summary>
    public sealed class RoutingTransferProvider : IFileTransferProvider
    {
        private readonly IFileTransferProvider _share;
        private readonly IFileTransferProvider? _dds;
        private readonly Func<int, bool> _hasShare;
        private readonly Func<int, string> _nodeRootSeenByMaster;

        /// <param name="hasShare">Whether a node advertised a share it can be copied into.</param>
        /// <param name="nodeRootSeenByMaster">A node's staging root as the master's paths name it (the gateway's destinations are under it).</param>
        public RoutingTransferProvider(IFileTransferProvider share, IFileTransferProvider? dds,
                                       Func<int, bool> hasShare, Func<int, string> nodeRootSeenByMaster)
        {
            _share = share ?? throw new ArgumentNullException(nameof(share));
            _dds = dds;
            _hasShare = hasShare ?? throw new ArgumentNullException(nameof(hasShare));
            _nodeRootSeenByMaster = nodeRootSeenByMaster ?? throw new ArgumentNullException(nameof(nodeRootSeenByMaster));
        }

        public string Transport => _dds == null ? _share.Transport : $"{_share.Transport}+{_dds.Transport}";

        public bool CanReach(TransferTarget target) => _share.CanReach(target) || _dds != null;

        /// <summary>Whether this provider would send to <paramref name="nodeId"/> over DDS.</summary>
        public bool UsesDds(int nodeId) => _dds != null && !_hasShare(nodeId);

        public IFileTransfer Start(TransferRequest request)
        {
            var parts = new List<IFileTransfer>();
            var viaShare = request.Targets.Where(t => !UsesDds(t.NodeId)).ToList();
            if (viaShare.Count > 0) parts.Add(_share.Start(request with { Targets = viaShare }));

            // ⭐ DDS: items relative to each node's root; targets whose relative items are identical share ONE transfer (bundling)
            foreach (var group in request.Targets.Where(t => UsesDds(t.NodeId))
                         .GroupBy(t => string.Join("|", Relative(t).Select(i => i.RelativePath))))
            {
                var first = group.First();
                parts.Add(_dds!.Start(new TransferRequest(
                    group.Select(t => new TransferTarget(t.NodeId, string.Empty)).ToList(),
                    Relative(first).ToList(), request.CorrelationId)));
            }
            return new Composite(parts, request);

            IEnumerable<TransferItem> Relative(TransferTarget t)
            {
                var nodeRoot = _nodeRootSeenByMaster(t.NodeId);
                return request.Items.Select(i => i with
                {
                    RelativePath = Path.GetRelativePath(nodeRoot, Path.Combine(t.Root, i.RelativePath)),
                });
            }
        }

        /// <summary>Several transfers seen as one: progress summed, done when all are, failed nodes merged.</summary>
        private sealed class Composite : IFileTransfer
        {
            private readonly IReadOnlyList<IFileTransfer> _parts;
            private readonly TransferRequest _request;

            public Composite(IReadOnlyList<IFileTransfer> parts, TransferRequest request)
            {
                _parts = parts;
                _request = request;
                Id = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId;
                Completion = Task.WhenAll(parts.Select(p => p.Completion));
            }

            public Guid Id { get; }
            public Task Completion { get; }
            public void Cancel() { foreach (var p in _parts) p.Cancel(); }

            public TransferStatus Status
            {
                get
                {
                    var s = _parts.Select(p => p.Status).ToList();
                    if (s.Count == 1) return s[0];
                    var code = s.Any(x => !x.IsFinished) ? OrchestrationStatusCode.InProgress
                             : s.Any(x => x.Code == OrchestrationStatusCode.Cancelled) ? OrchestrationStatusCode.Cancelled
                             : s.Any(x => x.Code != OrchestrationStatusCode.Success) ? OrchestrationStatusCode.Failure
                             : OrchestrationStatusCode.Success;
                    return new TransferStatus(code, string.Join(" | ", s.Select(x => x.Message)),
                        s.Sum(x => x.BytesDone), s.Sum(x => x.BytesTotal), s.Sum(x => x.FilesDone), s.Sum(x => x.FilesSkipped),
                        s.Sum(x => x.FilesFailed), _request.Items.Count * _request.Targets.Count,
                        s.SelectMany(x => x.FailedNodeIds).Distinct().OrderBy(n => n).ToArray());
                }
            }
        }
    }
}
