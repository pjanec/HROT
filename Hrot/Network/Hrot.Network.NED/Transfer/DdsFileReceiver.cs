using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.NED.Transfer;

/// <summary>
/// ⭐⭐ <c>P6</c> part 2 (Q91 §4-E2, <c>R-4002</c>) — the node side of the DDS file transfer, on EVERY node (R-4000). It answers an
/// offer naming this node with the files it lacks (the one freshness rule: same length + mtime), joins that transfer's partition,
/// writes each file to a temporary name, checks length + SHA-256, restores the sender's mtime, renames it into place, and reports.
/// The chunk reader is disposed when the transfer ends, so its partition and history are gone.
/// ⭐ Ticked from the frame thread; it never blocks (a <c>Take</c> per tick).
/// </summary>
public sealed class DdsFileReceiver : IDisposable
{
    private readonly DdsParticipant _participant;
    private readonly int _nodeId;
    private readonly string _root;
    private readonly DdsReader<FileOffer> _offers;
    private readonly DdsWriter<FileNeed> _needs;
    private readonly DdsWriter<FileAck> _acks;
    private readonly Dictionary<long, Incoming> _active = new();
    private readonly HashSet<long> _seen = new();

    /// <param name="nodeStagingRoot">This node's own staging root — offered paths are relative to it.</param>
    public DdsFileReceiver(DdsParticipant participant, int nodeId, string nodeStagingRoot)
    {
        _participant = participant ?? throw new ArgumentNullException(nameof(participant));
        _nodeId = nodeId;
        _root = nodeStagingRoot;
        _offers = new DdsReader<FileOffer>(participant);
        _needs  = new DdsWriter<FileNeed>(participant);
        _acks   = new DdsWriter<FileAck>(participant);
    }

    /// <summary>Transfers in progress — for rails.</summary>
    public int ActiveTransfers => _active.Count;

    public void Tick()
    {
        using (var loan = _offers.Take())
            foreach (var s in loan)
            {
                if (!s.IsValid) continue;
                var offer = s.Data;
                if (string.IsNullOrEmpty(offer.Partition))   // ⭐ the sender cancelled
                {
                    if (_active.Remove(offer.TransferId, out var cancelled)) cancelled.Dispose(deletePartials: true);
                    continue;
                }
                if (!_seen.Add(offer.TransferId)) continue;
                var targets = JsonSerializer.Deserialize<int[]>(offer.TargetsJson ?? "[]", Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<int>();
                if (Array.IndexOf(targets, _nodeId) < 0) continue;   // a bystander takes nothing (and joins no partition)
                Accept(offer);
            }

        foreach (var t in _active.Values.ToList()) Pump(t);
    }

    private void Accept(FileOffer offer)
    {
        var items = JsonSerializer.Deserialize<OfferedFile[]>(offer.ItemsJson ?? "[]", Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<OfferedFile>();
        var needed = new List<int>();
        for (int i = 0; i < items.Length; i++)
            if (!TransferWire.AlreadyHeld(PathOf(items[i]), items[i].Length, items[i].LastWriteUtcTicks))
                needed.Add(i);

        // ⭐ The reader exists BEFORE the need is sent, so the sender — which waits for every target's reader to match — never
        //   sends into an empty partition.
        Incoming? t = null;
        if (needed.Count > 0)
        {
            t = new Incoming(offer.TransferId, items, needed,
                new DdsReader<FileChunk>(_participant, partition: offer.Partition), PathOf);
            _active[offer.TransferId] = t;
        }
        _needs.Write(new FileNeed { TransferId = offer.TransferId, NodeId = _nodeId, NeededJson = JsonSerializer.Serialize(needed, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) });
        if (t == null) Done(offer.TransferId, OrchestrationStatusCode.Success, "already held");
    }

    private void Pump(Incoming t)
    {
        using (var loan = t.Reader.Take())
            foreach (var s in loan)
            {
                if (!s.IsValid) continue;
                var c = s.Data;
                if (!t.Files.TryGetValue(c.FileIndex, out var f) || f.Finished) continue;
                f.Write(c.Seq, c.Data ?? Array.Empty<byte>());
                bool complete = f.Contiguous == f.SeqCount - 1;
                if (complete || f.Contiguous - f.LastAcked >= TransferWire.AckEvery)
                {
                    _acks.Write(new FileAck { TransferId = t.Id, NodeId = _nodeId, FileIndex = c.FileIndex, ContiguousSeq = f.Contiguous });
                    f.LastAcked = f.Contiguous;
                }
                if (complete) f.Finish();
            }

        if (!t.Files.Values.All(f => f.Finished)) return;

        _active.Remove(t.Id);
        var failed = t.Files.Where(kv => kv.Value.Error != null).ToList();
        t.Dispose(deletePartials: false);
        if (failed.Count == 0) Done(t.Id, OrchestrationStatusCode.Success, $"{t.Files.Count} received");
        else Done(t.Id, OrchestrationStatusCode.Failure,
            string.Join("; ", failed.Select(kv => $"'{t.Items[kv.Key].RelativePath}': {kv.Value.Error}")));
    }

    private void Done(long transferId, OrchestrationStatusCode code, string message)
        => _acks.Write(new FileAck
        {
            TransferId = transferId, NodeId = _nodeId, FileIndex = -1, ContiguousSeq = -1,
            Done = true, Code = (int)code, Message = message,
        });

    private string PathOf(OfferedFile f) => Path.Combine(_root, f.RelativePath);

    public void Dispose()
    {
        foreach (var t in _active.Values) t.Dispose(deletePartials: true);
        _active.Clear();
        _offers.Dispose();
        _needs.Dispose();
        _acks.Dispose();
    }

    private sealed class Incoming
    {
        public readonly long Id;
        public readonly OfferedFile[] Items;
        public readonly Dictionary<int, IncomingFile> Files = new();
        public readonly DdsReader<FileChunk> Reader;

        public Incoming(long id, OfferedFile[] items, List<int> needed, DdsReader<FileChunk> reader, Func<OfferedFile, string> pathOf)
        {
            Id = id; Items = items; Reader = reader;
            foreach (var i in needed) Files[i] = new IncomingFile(items[i], pathOf(items[i]));
        }

        public void Dispose(bool deletePartials)
        {
            foreach (var f in Files.Values) f.Close(deletePartials);
            Reader.Dispose();
        }
    }

    private sealed class IncomingFile
    {
        private readonly OfferedFile _item;
        private readonly string _destination;
        private readonly bool[] _have;
        private FileStream? _stream;
        private string? _partial;
        public readonly int SeqCount;
        public int Contiguous = -1;
        public int LastAcked = -1;
        public bool Finished;
        public string? Error;

        public IncomingFile(OfferedFile item, string destination)
        {
            _item = item;
            _destination = destination;
            SeqCount = TransferWire.SeqCountFor(item.Length);
            _have = new bool[SeqCount];
        }

        /// <summary>Writes one chunk at its offset into the temporary file beside the destination (opened on the first chunk).</summary>
        public void Write(int seq, byte[] data)
        {
            if (seq < 0 || seq >= SeqCount || _have[seq] || Error != null) return;
            try
            {
                if (_stream == null)
                {
                    var dir = Path.GetDirectoryName(_destination);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    _partial = _destination + Fdp.Toolkit.Orchestration.Transfer.NodeShareProvider.PartialSuffix;
                    _stream = new FileStream(_partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    _stream.SetLength(_item.Length);
                }
                _stream.Position = (long)seq * TransferWire.ChunkBytes;
                _stream.Write(data, 0, data.Length);
            }
            catch (Exception ex) { Error = ex.Message; }
            _have[seq] = true;
            while (Contiguous + 1 < SeqCount && _have[Contiguous + 1]) Contiguous++;
        }

        /// <summary>Every chunk is in: check length + SHA-256, restore the sender's mtime, rename into place.</summary>
        public void Finish()
        {
            Finished = true;
            if (Error != null) { Close(deletePartials: true); return; }
            try
            {
                _stream!.Flush();
                _stream.Dispose();
                _stream = null;
                var length = new FileInfo(_partial!).Length;
                if (length != _item.Length) throw new IOException($"length {length} != {_item.Length}");
                if (!string.Equals(TransferWire.Sha256Of(_partial!), _item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("SHA-256 mismatch");
                File.SetLastWriteTimeUtc(_partial!, new DateTime(_item.LastWriteUtcTicks, DateTimeKind.Utc));
                File.Move(_partial!, _destination, overwrite: true);
                _partial = null;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Close(deletePartials: true);
            }
        }

        public void Close(bool deletePartials)
        {
            try { _stream?.Dispose(); } catch { /* best effort */ }
            _stream = null;
            if (deletePartials && _partial != null) try { File.Delete(_partial); } catch { /* best effort */ }
        }
    }
}
