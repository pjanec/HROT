using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using CycloneDDS.Runtime;
namespace DdsBlob
{
    public static class Xfer
    {
        // Sender: partitioned chunk writer; waits until all receivers are matched; ack-gated window; Timeout = back-pressure.
        public static void Send(DdsParticipant p, int mb, int chunkKb, int window, int[] receivers, string partition)
        {
            var data = new byte[mb * 1024 * 1024]; new Random(7).NextBytes(data);
            string sha = Convert.ToHexString(SHA256.HashData(data))[..16];
            int chunk = chunkKb * 1024, n = (data.Length + chunk - 1) / chunk;
            using var ackR = new DdsReader<XferAck>(p, "XferAck");
            var total = Stopwatch.StartNew();
            var w = new DdsWriter<XferChunk>(p, "XferChunk", IntPtr.Zero, partition);
            while (w.CurrentStatus.CurrentCount < receivers.Length) Thread.Sleep(5);   // no timeout: wait for every receiver's reader
            long matchMs = total.ElapsedMilliseconds;
            var acked = receivers.ToDictionary(r => r, r => -1); var done = new HashSet<int>(); var shaOf = new Dictionary<int, string>();
            int next = 0, retries = 0;
            void Drain()
            {
                var loan = ackR.Take();
                try { for (int i = 0; i < loan.Count; i++) { var a = loan[i]; if (!acked.ContainsKey(a.NodeId)) continue;
                    acked[a.NodeId] = Math.Max(acked[a.NodeId], a.ContiguousSeq); if (a.Done == 1) { done.Add(a.NodeId); shaOf[a.NodeId] = a.Sha ?? ""; } } }
                finally { loan.Dispose(); }
            }
            while (done.Count < receivers.Length)
            {
                Drain();
                int minAck = acked.Values.Min();
                while (next < n && next - minAck <= window)
                {
                    int len = Math.Min(chunk, data.Length - next * chunk);
                    var buf = new byte[len]; Buffer.BlockCopy(data, next * chunk, buf, 0, len);
                    var s = new XferChunk { TransferId = 1, Seq = next, SeqCount = n, TotalBytes = data.Length, ChunkSize = chunk, Data = buf };
                    try { w.Write(ref s); next++; }
                    catch (DdsException) { retries++; Thread.Sleep(2); break; }   // back-pressure: wait, resend the same chunk
                }
                Thread.Sleep(1);
            }
            long ms = total.ElapsedMilliseconds;
            w.Dispose();   // close the writer: the transfer partition is gone
            Console.WriteLine($"XSEND {mb}MB chunk={chunkKb}KB window={window} receivers={receivers.Length} matched-after={matchMs}ms total={ms}ms ({mb*1000.0/ms:F0} MB/s) writeTimeoutsRetried={retries} sha={sha} receiverSha=[{string.Join(",", shaOf.Values)}]");
        }

        // Receiver: joins the transfer partition, acks contiguous progress, verifies, acks Done, closes its reader.
        public static void Recv(DdsParticipant p, int nodeId, string partition, int seconds)
        {
            using var ackW = new DdsWriter<XferAck>(p, "XferAck");
            var r = new DdsReader<XferChunk>(p, "XferChunk", IntPtr.Zero, partition);
            byte[]? buf = null; bool[]? have = null; int contiguous = -1, count = 0, lastAcked = -1; long bytes = 0;
            var deadline = DateTime.UtcNow.AddSeconds(seconds); var sw = new Stopwatch();
            while (DateTime.UtcNow < deadline)
            {
                var loan = r.Take();
                try { for (int i = 0; i < loan.Count; i++) { var c = loan[i]; if (c.Data == null) continue;
                    if (buf == null) { buf = new byte[c.TotalBytes]; have = new bool[c.SeqCount]; sw.Start(); }
                    if (have![c.Seq]) continue; have[c.Seq] = true; count++; bytes += c.Data.Length;
                    Buffer.BlockCopy(c.Data, 0, buf, (int)((long)c.Seq * c.ChunkSize), c.Data.Length); } }
                finally { loan.Dispose(); }
                if (have != null) { while (contiguous + 1 < have.Length && have[contiguous + 1]) contiguous++;
                    bool all = contiguous == have.Length - 1;
                    if (contiguous - lastAcked >= 8 || all)
                    { var a = new XferAck { TransferId = 1, NodeId = nodeId, ContiguousSeq = contiguous, Done = all ? 1 : 0, Sha = all ? Convert.ToHexString(SHA256.HashData(buf!))[..16] : "" };
                      ackW.Write(ref a); lastAcked = contiguous; if (all) break; } }
                Thread.Sleep(1);
            }
            r.Dispose();   // close the reader
            Console.WriteLine($"XRECV node={nodeId} partition={partition} chunks={count}/{have?.Length ?? 0} bytes={bytes} in {sw.ElapsedMilliseconds}ms");
            Thread.Sleep(1500); // let the Done ack reach the sender before this writer goes away
        }
    }
}
