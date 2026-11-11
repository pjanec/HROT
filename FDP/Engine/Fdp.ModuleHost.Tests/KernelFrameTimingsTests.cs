using System;
using Fdp.ModuleHost;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>⭐ CE-4433 — the kernel's frame-by-part timer (HROT_FRAME_STATS) on the per-frame path: 0 B once a part is known (R-2084); drained biggest first.</summary>
    [Collection("KernelFrameTimings")]
    public class KernelFrameTimingsTests
    {
        [Fact]
        public void ASteadyFrameOfParts_Allocates0B_AndDrainsTheirTimes()
        {
            bool was = KernelFrameTimings.Enabled;
            KernelFrameTimings.Enabled = true;
            try
            {
                KernelFrameTimings.Drain();
                const string a = "part a", b = "part b";
                void Frame()
                {
                    long t = KernelFrameTimings.Start(), x = KernelFrameTimings.StartBytes();
                    KernelFrameTimings.Stop(a, t, x);
                    t = KernelFrameTimings.Start(); x = KernelFrameTimings.StartBytes();
                    System.Threading.Thread.SpinWait(2000);
                    KernelFrameTimings.Stop(b, t, x);
                }
                Frame();   // the first sight of each part may allocate (its slot)
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10; i++) Frame();
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

                var parts = KernelFrameTimings.Drain();
                Assert.Equal(2, parts.Count);
                Assert.Equal(b, parts[0].Name);   // the spinning part is the bigger
                Assert.Equal(11, parts[0].Calls);
                Assert.Empty(KernelFrameTimings.Drain().FindAll(p => p.Calls > 0));
            }
            finally { KernelFrameTimings.Enabled = was; }
        }

        [Fact]
        public void Off_RecordsNothing()
        {
            bool was = KernelFrameTimings.Enabled;
            KernelFrameTimings.Enabled = false;
            try
            {
                KernelFrameTimings.Stop("off part", KernelFrameTimings.Start());
                Assert.DoesNotContain(KernelFrameTimings.Drain(), p => p.Name == "off part");
            }
            finally { KernelFrameTimings.Enabled = was; }
        }
    }
}
