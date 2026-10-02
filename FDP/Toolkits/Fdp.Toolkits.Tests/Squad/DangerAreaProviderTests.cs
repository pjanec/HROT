using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.DangerArea.Fake;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// P0-04: Verifies <see cref="DangerAreaDescriptor"/> layout and
    /// <see cref="FakeDangerAreaProvider"/> behaviour.
    /// </summary>
    public class DangerAreaProviderTests
    {
        [Fact]
        public void DangerAreaDescriptor_PinnedSize_MatchesActual()
        {
            Assert.Equal(DangerAreaDescriptor.PinnedSize, Unsafe.SizeOf<DangerAreaDescriptor>());
        }

        [Fact]
        public void FakeDangerAreaProvider_Builder_ThreeFeatures()
        {
            var provider = new FakeDangerAreaProvider()
                .Add("street-alpha",  kind: DangerAreaKind.StreetCrossing)
                .Add("junction-beta", kind: DangerAreaKind.Intersection)
                .Add("ridge-gamma",   kind: DangerAreaKind.CrestLine);

            Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[4];
            provider.Refresh(default, default, buf, out int count);

            Assert.Equal(3, count);
            Assert.Equal(DangerAreaKind.StreetCrossing, buf[0].Kind);
            Assert.Equal(DangerAreaKind.Intersection,   buf[1].Kind);
            Assert.Equal(DangerAreaKind.CrestLine,      buf[2].Kind);
        }

        [Fact]
        public void FakeDangerAreaProvider_FeatureId_PinsForStreetEast01()
        {
            // Compute expected FNV-1a-32 of "street-east-01" in-test so the test
            // documents the exact hash rather than hard-coding a magic number.
            uint expected = FakeDangerAreaProvider.Fnv1a32("street-east-01");

            var provider = new FakeDangerAreaProvider().Add("street-east-01");
            Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[1];
            provider.Refresh(default, default, buf, out int count);

            Assert.Equal(1, count);
            Assert.Equal(expected, buf[0].FeatureId);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>AX-018</c> — the INSTRUMENT was wrong, not the claim.</b>
        ///
        /// <para>🔴 This used to measure <c>GC.GetTotalMemory</c> — the <b>whole process heap</b> — around
        /// the loop, with a 4096-byte fudge and a <c>[Trait("Stability","Flaky")]</c> tag whose comment said
        /// it *"passes in isolation"*. 📐 Measured <c>2026-08-26</c>: it fails in isolation too *(8224 bytes,
        /// 2× the tolerance)*. ⛔ Of course it does — xunit's own machinery allocates on other threads
        /// concurrently, so a process-wide counter can never attribute bytes to THIS code. ⇒ the tolerance
        /// was not a tolerance, it was a coin flip, and no value of it would have fixed that.</para>
        ///
        /// <para>⭐⭐ <b><c>GC.GetAllocatedBytesForCurrentThread()</c> is the right instrument</b> — it counts
        /// only this thread's allocations and is exactly what a zero-alloc claim needs. ⇒ ⭐ the assert can
        /// now be EXACT *(zero bytes across 1000 calls)*, which is <b>stricter</b> than the fudge it
        /// replaces, and the <c>Flaky</c> trait is gone because the measurement is no longer shared state.</para>
        ///
        /// <para>⚠ It also no longer needs <c>GC.Collect</c>: a thread-local allocation counter is unaffected
        /// by collection, so forcing one was only ever masking the noise it could not remove.</para>
        /// </summary>
        [Fact]
        public void FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup()
        {
            var provider = new FakeDangerAreaProvider()
                .Add("a").Add("b").Add("c");

            Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[4];

            // Warm-up call so one-time JIT allocations land outside the measured window.
            provider.Refresh(default, default, buf, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
                provider.Refresh(default, default, buf, out _);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
        }
    }
}
