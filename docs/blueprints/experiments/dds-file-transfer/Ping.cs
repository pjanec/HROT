using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
namespace DdsBlob
{
    public static class PingRun
    {
        // pingsend: writes a ping every 10 ms for <seconds>
        public static void Send(DdsParticipant p, int seconds)
        {
            using var w = new DdsWriter<BlobPing>(p);
            Thread.Sleep(3000);
            var end = DateTime.UtcNow.AddSeconds(seconds); int seq = 0;
            while (DateTime.UtcNow < end) { w.Write(new BlobPing { Id = 1, Seq = seq++, SentTicks = Stopwatch.GetTimestamp() }); Thread.Sleep(10); }
            Thread.Sleep(1000);
        }
        public static void Recv(DdsParticipant p, int seconds)
        {
            using var r = new DdsReader<BlobPing>(p);
            var lat = new List<double>(); var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                var loan = r.Take();
                try { for (int i = 0; i < loan.Count; i++) { var s = loan[i]; if (s.SentTicks == 0) continue; lat.Add((Stopwatch.GetTimestamp() - s.SentTicks) * 1000.0 / Stopwatch.Frequency); } }
                finally { loan.Dispose(); }
                Thread.Sleep(1);
            }
            if (lat.Count == 0) { Console.WriteLine("PING none"); return; }
            lat.Sort();
            Console.WriteLine($"PING n={lat.Count} p50={lat[lat.Count/2]:F2}ms p99={lat[(int)(lat.Count*0.99)]:F2}ms max={lat[^1]:F2}ms");
        }
    }
}
