using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using CycloneDDS.Runtime;
using DdsBlob;

// usage: send <domain> <totalMB> <chunkKB> | recv <domain> <expectedBytes> <timeoutSec> <chunkKB>
var mode = args[0]; uint domain = uint.Parse(args[1]);
using var p = new DdsParticipant(domain);
if (mode == "pingsend") { PingRun.Send(p, int.Parse(args[2])); return; }
if (mode == "pingrecv") { PingRun.Recv(p, int.Parse(args[2])); return; }
if (mode == "send")
{
    int totalMb = int.Parse(args[2]); int chunkKb = int.Parse(args[3]);
    var data = new byte[totalMb * 1024 * 1024]; new Random(42).NextBytes(data);
    Console.WriteLine($"SEND sha={Convert.ToHexString(SHA256.HashData(data))[..16]}");
    using var w = new DdsWriter<FileBlob>(p, "FileBlob");
    Thread.Sleep(3000); // discovery
    int chunk = chunkKb * 1024; int n = (data.Length + chunk - 1) / chunk;
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < n; i++)
    {
        int len = Math.Min(chunk, data.Length - i * chunk);
        var buf = new byte[len]; Buffer.BlockCopy(data, i * chunk, buf, 0, len);
        w.Write(new FileBlob { TransferId = 1, Seq = i, SeqCount = n, RelativePath = "terrain/test.bin", Data = buf });
    }
    Console.WriteLine($"SEND wrote {n} samples of {chunkKb} KB in {sw.ElapsedMilliseconds} ms");
    Thread.Sleep(8000); // keep writer alive for reliable repair
}
else
{
    long expected = long.Parse(args[2]); int timeout = int.Parse(args[3]); int chunk = int.Parse(args[4]) * 1024;
    using var r = new DdsReader<FileBlob>(p, "FileBlob");
    var got = new byte[expected]; long recvBytes = 0; int samples = 0; int seqCount = -1;
    var seen = new System.Collections.Generic.HashSet<int>();
    Stopwatch? sw = null; var deadline = DateTime.UtcNow.AddSeconds(timeout);
    while (DateTime.UtcNow < deadline && (seqCount < 0 || seen.Count < seqCount))
    {
        var loan = r.Take();
        try
        {
            for (int i = 0; i < loan.Count; i++)
            {
                var s = loan[i]; if (s.Data == null) continue;
                sw ??= Stopwatch.StartNew();
                seqCount = s.SeqCount;
                if (seen.Add(s.Seq)) { Buffer.BlockCopy(s.Data, 0, got, s.Seq * chunk, s.Data.Length); recvBytes += s.Data.Length; samples++; }
            }
        }
        finally { loan.Dispose(); }
        Thread.Sleep(1);
    }
    Console.WriteLine($"RECV samples={samples}/{seqCount} bytes={recvBytes}/{expected} in {sw?.ElapsedMilliseconds ?? -1} ms (first sample to last) sha={Convert.ToHexString(SHA256.HashData(got))[..16]}");
}
