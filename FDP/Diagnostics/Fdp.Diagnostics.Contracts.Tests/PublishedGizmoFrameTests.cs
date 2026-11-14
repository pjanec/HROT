using System;
using System.Numerics;
using System.Threading;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests
{
    /// <summary>
    /// ⭐ POC 2 P-6 (CE-4439; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §14.2, A-4 ⑥) — the gizmo frame the DRAWING thread reads while the
    /// kernel ticks on a worker. The drawing, the input hit-test and the picker (<c>DebugGizmoLayer</c>: <c>ReaderFrame()</c>) read the LEASED published
    /// frame, never the worker's live buffer: a leased frame does not change under its reader, and a racing writer never shows it half a frame.
    /// 🔴 Red-proof: <c>ReaderFrame</c> returning <c>GetFrame()</c> even when publishing ⇒ both fail.
    /// </summary>
    public class PublishedGizmoFrameTests
    {
        private static void WriteFrame(DebugPrimitiveBuffer b, byte tag, int n)
        {
            b.Clear();
            for (int i = 0; i < n; i++) b.DrawLine(new Vector3(i, 0, 0), new Vector3(i, 1, 0), new Rgba32(tag, 0, 0));
        }

        [Fact]
        public void A_leased_frame_does_not_change_while_the_writer_publishes_two_more()
        {
            var b = new DebugPrimitiveBuffer(256) { ReaderUsesPublished = true };
            WriteFrame(b, 1, 10); b.PublishFrame();
            b.LeasePublished();
            WriteFrame(b, 2, 20); b.PublishFrame();
            WriteFrame(b, 3, 30); b.PublishFrame();

            var f = b.ReaderFrame();
            Assert.Equal(10, f.Length);
            foreach (ref readonly var p in f) Assert.Equal(1, p.Color.R);

            b.LeasePublished();
            var g = b.ReaderFrame();
            Assert.Equal(30, g.Length);
            Assert.Equal(3, g[0].Color.R);
        }

        [Fact]
        public void A_racing_writer_never_shows_the_reader_a_torn_frame()
        {
            var b = new DebugPrimitiveBuffer(512) { ReaderUsesPublished = true };
            WriteFrame(b, 0, 1); b.PublishFrame();
            int torn = 0, reads = 0;
            var stop = DateTime.UtcNow.AddMilliseconds(400);
            var writer = new Thread(() =>
            {
                byte tag = 1;
                while (DateTime.UtcNow < stop) { WriteFrame(b, tag, 64 + tag % 200); b.PublishFrame(); tag = (byte)(tag == 250 ? 1 : tag + 1); }
            });
            writer.Start();
            while (DateTime.UtcNow < stop)
            {
                b.LeasePublished();
                var f = b.ReaderFrame();
                if (f.Length == 0) continue;
                byte first = f[0].Color.R;
                for (int i = 1; i < f.Length; i++) if (f[i].Color.R != first) { torn++; break; }
                if (f.Length != (first == 0 ? 1 : 64 + first % 200)) torn++;
                reads++;
            }
            writer.Join();
            Assert.True(reads > 100, $"only {reads} reads");
            Assert.Equal(0, torn);
        }
    }
}
