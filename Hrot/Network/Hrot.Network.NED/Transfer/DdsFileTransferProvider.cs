using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Transfer;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.NED.Transfer;

/// <summary>
/// ⭐⭐ <c>P6</c> part 2 (Q91 §4-E2, <c>R-4002</c>) — the DDS transport: offer → each target's need → 64 KB chunks in the transfer's
/// OWN partition, at most <see cref="TransferWire.Window"/> beyond the slowest target → each target's verdict. Done only when every
/// target checked length + SHA-256. A <c>Write</c> that times out is back-pressure: wait, resend the same chunk. The chunk writer is
/// disposed at the end. ⛔ No timer decides anything (R-4001): a transfer ends on its targets' answers or on <see cref="IFileTransfer.Cancel"/>.
/// ⭐ Item paths are RELATIVE to each target's staging root; <see cref="TransferTarget.Root"/> is ignored.
/// </summary>
public sealed class DdsFileTransferProvider : IFileTransferProvider, IDisposable
{
    private readonly DdsParticipant _participant;
    private readonly DdsWriter<FileOffer> _offers;
    private readonly DdsReader<FileNeed> _needs;
    private readonly DdsReader<FileAck> _acks;
    private readonly object _pumpGate = new();
    private readonly ConcurrentDictionary<long, Inbox> _inboxes = new();

    public DdsFileTransferProvider(DdsParticipant participant)
    {
        _participant = participant ?? throw new ArgumentNullException(nameof(participant));
        _offers = new DdsWriter<FileOffer>(participant);
        _needs  = new DdsReader<FileNeed>(participant);
        _acks   = new DdsReader<FileAck>(participant);
    }

    public string Transport => "dds";

    public bool CanReach(TransferTarget target) => true;

    public IFileTransfer Start(TransferRequest request)
    {
        var t = new DdsTransfer(this, request);
        t.Run();
        return t;
    }

    public void Dispose()
    {
        _offers.Dispose();
        _needs.Dispose();
        _acks.Dispose();
    }

    // ── the shared control readers, drained into one inbox per transfer (several transfers may run at once) ──

    private sealed class Inbox
    {
        public readonly ConcurrentQueue<FileNeed> Needs = new();
        public readonly ConcurrentQueue<FileAck> Acks = new();
    }

    private void Pump()
    {
        lock (_pumpGate)
        {
            using (var loan = _needs.Take())
                foreach (var s in loan)
                    if (s.IsValid && _inboxes.TryGetValue(s.Data.TransferId, out var box)) box.Needs.Enqueue(s.Data);
            using (var loan = _acks.Take())
                foreach (var s in loan)
                    if (s.IsValid && _inboxes.TryGetValue(s.Data.TransferId, out var box)) box.Acks.Enqueue(s.Data);
        }
    }

    private sealed class DdsTransfer : IFileTransfer
    {
        private readonly DdsFileTransferProvider _owner;
        private readonly TransferRequest _request;
        private readonly CancellationTokenSource _cts = new();
        private readonly long _transferId;
        private volatile TransferStatus _status;
        private long _bytesDone, _bytesTotal;
        private int _done, _skipped, _failed;
        private readonly List<int> _failedNodes = new();
        private readonly List<string> _errors = new();

        public DdsTransfer(DdsFileTransferProvider owner, TransferRequest request)
        {
            _owner = owner;
            _request = request;
            Id = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId;
            _transferId = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) & long.MaxValue;
            _status = Snapshot(OrchestrationStatusCode.InProgress, "offering");
        }

        public Guid Id { get; }
        public TransferStatus Status => _status;
        public Task Completion { get; private set; } = Task.CompletedTask;
        public void Cancel() => _cts.Cancel();

        public void Run()
        {
            var box = new Inbox();
            _owner._inboxes[_transferId] = box;
            Completion = Task.Run(() =>
            {
                try { Send(box); }
                catch (OperationCanceledException)
                {
                    // ⭐ tell the targets to drop the partial transfer (an offer with no partition = cancelled)
                    _owner._offers.Write(new FileOffer { TransferId = _transferId, Partition = string.Empty, TargetsJson = "[]", ItemsJson = "[]" });
                    _status = Snapshot(OrchestrationStatusCode.Cancelled, "cancelled");
                }
                catch (Exception ex)
                {
                    // ⭐ the whole transfer failed: every (target, file) counts as failed, every target is named — so a caller
                    //   counting FilesFailed / FailedNodeIds never reads it as a success.
                    lock (_errors)
                    {
                        _errors.Add(ex.Message);
                        _failedNodes.Clear();
                        _failedNodes.AddRange(_request.Targets.Select(t => t.NodeId).Distinct());
                    }
                    _failed = _request.Items.Count * _request.Targets.Count;
                    _done = 0;
                    _status = Snapshot(OrchestrationStatusCode.Failure, ex.Message);
                }
                finally { _owner._inboxes.TryRemove(_transferId, out _); }
            });
        }

        private void Send(Inbox box)
        {
            var ct = _cts.Token;
            var targets = _request.Targets.Select(t => t.NodeId).Distinct().ToArray();

            // ① describe the files (a missing or oversized source fails the whole transfer for every target, loudly)
            var items = _request.Items.Select(i =>
            {
                var f = new FileInfo(i.SourcePath);
                if (!f.Exists) throw new FileNotFoundException($"source '{i.SourcePath}' does not exist");
                if (f.Length > TransferWire.MaxFileBytes)
                    throw new InvalidOperationException($"'{i.RelativePath}' is {f.Length} bytes — above the DDS cap; use the share");
                return new OfferedFile(i.RelativePath.Replace('\\', '/'), f.Length, f.LastWriteTimeUtc.Ticks, TransferWire.Sha256Of(i.SourcePath));
            }).ToArray();

            var partition = TransferWire.PartitionFor(_transferId);
            _owner._offers.Write(new FileOffer
            {
                TransferId  = _transferId,
                Partition   = partition,
                TargetsJson = JsonSerializer.Serialize(targets, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed),
                ItemsJson   = JsonSerializer.Serialize(items, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed),
            });

            // ② every target says what it lacks
            var needs = new Dictionary<int, int[]>();
            while (needs.Count < targets.Length)
            {
                ct.ThrowIfCancellationRequested();
                _owner.Pump();
                while (box.Needs.TryDequeue(out var n))
                    if (Array.IndexOf(targets, n.NodeId) >= 0)
                        needs[n.NodeId] = JsonSerializer.Deserialize<int[]>(n.NeededJson ?? "[]", Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<int>();
                Thread.Sleep(2);
            }
            foreach (var (node, need) in needs) _skipped += items.Length - need.Length;
            foreach (var (node, need) in needs) foreach (var i in need) _bytesTotal += items[i].Length;
            _status = Snapshot(OrchestrationStatusCode.InProgress, "sending");

            var receivers = needs.Where(kv => kv.Value.Length > 0).Select(kv => kv.Key).ToArray();
            var verdicts = new Dictionary<int, FileAck>();
            if (receivers.Length > 0)
            {
                // ③ the chunks, in the transfer's own partition — wait until every receiving target's reader is matched
                using var writer = new DdsWriter<FileChunk>(_owner._participant, partition: partition);
                while (writer.CurrentStatus.CurrentCount < receivers.Length) { ct.ThrowIfCancellationRequested(); Thread.Sleep(5); }

                var acked = new Dictionary<(int Node, int File), int>();
                for (int fileIndex = 0; fileIndex < items.Length; fileIndex++)
                {
                    var wanting = receivers.Where(n => Array.IndexOf(needs[n], fileIndex) >= 0).ToArray();
                    if (wanting.Length == 0) continue;
                    int seqCount = TransferWire.SeqCountFor(items[fileIndex].Length);
                    using var source = File.OpenRead(_request.Items[fileIndex].SourcePath);
                    var buffer = new byte[TransferWire.ChunkBytes];
                    for (int seq = 0; seq < seqCount; seq++)
                    {
                        // the window: never more than Window chunks beyond the slowest target that wants this file
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            Drain(box, acked, verdicts);
                            int slowest = wanting.Min(n => acked.TryGetValue((n, fileIndex), out var a) ? a : -1);
                            if (seq - slowest <= TransferWire.Window) break;
                            Thread.Sleep(1);
                        }
                        int read = source.Read(buffer, 0, buffer.Length);
                        var data = new byte[read];
                        Buffer.BlockCopy(buffer, 0, data, 0, read);
                        var chunk = new FileChunk { TransferId = _transferId, FileIndex = fileIndex, Seq = seq, SeqCount = seqCount, Data = data };
                        while (true)
                        {
                            try { writer.Write(chunk); break; }
                            catch (DdsException) { ct.ThrowIfCancellationRequested(); Thread.Sleep(2); }   // ⭐ back-pressure, never failure
                        }
                        Interlocked.Add(ref _bytesDone, read * wanting.Length);
                        _status = Snapshot(OrchestrationStatusCode.InProgress, "sending");
                    }
                }

                // ④ every receiving target's verdict on the whole transfer
                while (receivers.Any(n => !verdicts.ContainsKey(n)))
                {
                    ct.ThrowIfCancellationRequested();
                    Drain(box, acked, verdicts);
                    Thread.Sleep(2);
                }
            }

            foreach (var n in receivers)
            {
                var v = verdicts[n];
                if (v.Code == (int)OrchestrationStatusCode.Success) _done += needs[n].Length;
                else
                {
                    _failed += needs[n].Length;
                    lock (_errors) { _failedNodes.Add(n); _errors.Add($"node {n}: {v.Message}"); }
                }
            }
            _status = _failed > 0
                ? Snapshot(OrchestrationStatusCode.Failure, $"{_failed} failed: " + string.Join("; ", _errors.Take(5)))
                : Snapshot(OrchestrationStatusCode.Success, $"{_done} sent, {_skipped} already held");
            Fdp.Core.Logging.FdpLog<DdsFileTransferProvider>.Info(
                "[Transfer] DDS transfer {0:x} to node(s) {1}: {2}", _transferId, string.Join(",", targets), _status.Message);
        }

        private void Drain(Inbox box, Dictionary<(int, int), int> acked, Dictionary<int, FileAck> verdicts)
        {
            _owner.Pump();
            while (box.Acks.TryDequeue(out var a))
            {
                if (a.FileIndex < 0 && a.Done) verdicts[a.NodeId] = a;
                else if (!acked.TryGetValue((a.NodeId, a.FileIndex), out var prev) || a.ContiguousSeq > prev)
                    acked[(a.NodeId, a.FileIndex)] = a.ContiguousSeq;
            }
        }

        private TransferStatus Snapshot(OrchestrationStatusCode code, string message)
        {
            int[] failedNodes;
            lock (_errors) failedNodes = _failedNodes.OrderBy(n => n).ToArray();
            return new TransferStatus(code, message, Interlocked.Read(ref _bytesDone), _bytesTotal,
                _done, _skipped, _failed, _request.Items.Count * _request.Targets.Count, failedNodes);
        }
    }
}
