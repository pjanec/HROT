using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fdp.Toolkit.Orchestration.Transfer
{
    /// <summary>
    /// ⭐⭐ <c>P6</c> <c>N3</c> — the SHARE transport: today's <c>File.Copy</c>, aimed at the staging root the node advertised
    /// (<see cref="TransferTokens"/>), or — co-located — the root the orchestrator already writes into. ⭐ MOVED, not new:
    /// these are the copies <c>StorageGatewayModule</c>'s push sites made inline.
    ///
    /// <para>⭐ Each file lands under a temporary name and is renamed into place, so a reader never sees half a file.
    /// <c>File.Copy</c> keeps the source's last-write time, which is what makes the skip rule a cluster-wide identity
    /// (<c>StorageGatewayModule.IsAlreadyCurrent</c>, user ruling <c>2026-09-18</c>).</para>
    /// </summary>
    public sealed class NodeShareProvider : IFileTransferProvider
    {
        /// <summary>Concurrent copies per transfer — the gateway's former cap (disk/NIC saturation).</summary>
        public const int MaxParallelCopies = 8;

        /// <summary>The suffix a file carries while it is being written.</summary>
        public const string PartialSuffix = ".xfer-part";

        public string Transport => "share";

        public bool CanReach(TransferTarget target) => !string.IsNullOrEmpty(target.Root);

        public IFileTransfer Start(TransferRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var unreachable = request.Targets.Where(t => !CanReach(t)).Select(t => t.NodeId).ToArray();
            if (unreachable.Length > 0)
                throw new ArgumentException(
                    $"The share transport cannot reach node(s) {string.Join(", ", unreachable)} — they advertised no share.",
                    nameof(request));

            var transfer = new ShareTransfer(request);
            transfer.Run();
            return transfer;
        }

        /// <summary>⭐ The ONE freshness rule — same length and same last-write time ⇒ already there.</summary>
        public static bool IsAlreadyCurrent(FileInfo source, string destination)
        {
            var dst = new FileInfo(destination);
            return source.Exists && dst.Exists
                && source.Length == dst.Length
                && source.LastWriteTimeUtc == dst.LastWriteTimeUtc;
        }

        private sealed class ShareTransfer : IFileTransfer
        {
            private readonly TransferRequest _request;
            private readonly CancellationTokenSource _cts = new();
            private readonly object _gate = new();
            private readonly HashSet<int> _failedNodes = new();
            private readonly List<string> _errors = new();
            private long _bytesDone, _bytesTotal;
            private int _done, _skipped, _failed;
            private volatile TransferStatus _status;

            public ShareTransfer(TransferRequest request)
            {
                _request = request;
                Id = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId;
                _status = Snapshot(OrchestrationStatusCode.InProgress, "starting");
            }

            public Guid Id { get; }
            public TransferStatus Status => _status;
            public Task Completion { get; private set; } = Task.CompletedTask;
            public void Cancel() => _cts.Cancel();

            public void Run()
            {
                var work = new List<(TransferTarget Target, TransferItem Item, FileInfo Source)>();
                foreach (var item in _request.Items)
                {
                    var src = new FileInfo(item.SourcePath);
                    foreach (var target in _request.Targets)
                    {
                        work.Add((target, item, src));
                        if (src.Exists) _bytesTotal += src.Length;
                    }
                }
                _status = Snapshot(OrchestrationStatusCode.InProgress, "copying");

                Completion = Task.Run(() =>
                {
                    try
                    {
                        Parallel.ForEach(work,
                            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies, CancellationToken = _cts.Token },
                            w => CopyOne(w.Target, w.Item, w.Source));
                        Finish(_failed > 0 ? OrchestrationStatusCode.Failure : OrchestrationStatusCode.Success);
                    }
                    catch (OperationCanceledException)
                    {
                        Finish(OrchestrationStatusCode.Cancelled);
                    }
                });
            }

            private void CopyOne(TransferTarget target, TransferItem item, FileInfo source)
            {
                var dest = Path.Combine(target.Root, item.RelativePath);
                string? partial = null;
                try
                {
                    if (!source.Exists)
                        throw new FileNotFoundException($"source '{source.FullName}' does not exist");

                    if (IsAlreadyCurrent(source, dest))
                    {
                        Interlocked.Increment(ref _skipped);
                        Publish();
                        return;
                    }

                    var dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    partial = dest + PartialSuffix;
                    File.Copy(source.FullName, partial, overwrite: true);   // ⭐ keeps the source's last-write time
                    File.Move(partial, dest, overwrite: true);
                    partial = null;

                    Interlocked.Add(ref _bytesDone, source.Length);
                    Interlocked.Increment(ref _done);
                    Publish();
                }
                catch (Exception ex)
                {
                    if (partial != null) try { File.Delete(partial); } catch { /* best effort */ }
                    Interlocked.Increment(ref _failed);
                    lock (_gate)
                    {
                        _failedNodes.Add(target.NodeId);
                        _errors.Add($"node {target.NodeId}: '{item.RelativePath}': {ex.Message}");
                    }
                    Publish();
                }
            }

            private void Publish() => _status = Snapshot(OrchestrationStatusCode.InProgress, "copying");

            private void Finish(OrchestrationStatusCode code)
            {
                string message;
                lock (_gate)
                    message = code switch
                    {
                        OrchestrationStatusCode.Success   => $"{_done} copied, {_skipped} already current",
                        OrchestrationStatusCode.Cancelled => "cancelled",
                        _ => $"{_failed} failed: " + string.Join("; ", _errors.Take(5)),
                    };
                _status = Snapshot(code, message);
            }

            private TransferStatus Snapshot(OrchestrationStatusCode code, string message)
            {
                int[] failedNodes;
                lock (_gate) failedNodes = _failedNodes.OrderBy(n => n).ToArray();
                return new TransferStatus(code, message,
                    Interlocked.Read(ref _bytesDone), _bytesTotal,
                    Volatile.Read(ref _done), Volatile.Read(ref _skipped), Volatile.Read(ref _failed),
                    _request.Items.Count * _request.Targets.Count, failedNodes);
            }
        }
    }
}
