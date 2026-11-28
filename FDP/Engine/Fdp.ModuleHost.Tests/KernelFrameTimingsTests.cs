using System;
using Fdp.ModuleHost;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>⭐ CE-11076 — the timer is PROCESS-GLOBAL (<c>KernelFrameTimings.Enabled</c>): while a test switches it on, a kernel ticking in a
    /// parallel test records its own parts into it (extra parts in the drain, a contended lock allocating on this thread). 📌 Measured: red 2 of 3
    /// suite runs once a kernel rail that sleeps between frames (CE11076) joined the suite, green alone. ⇒ this collection runs alone.</summary>
    [CollectionDefinition("KernelFrameTimings", DisableParallelization = true)]
    public sealed class KernelFrameTimingsCollection { }

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
