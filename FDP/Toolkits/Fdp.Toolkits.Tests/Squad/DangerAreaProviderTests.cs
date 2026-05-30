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

        [Fact]
        public void FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup()
        {
            var provider = new FakeDangerAreaProvider()
                .Add("a").Add("b").Add("c");

            Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[4];

            // Warm-up call to ensure any one-time JIT allocations are accounted for.
            provider.Refresh(default, default, buf, out _);

            // Force a gen-0 collect so the baseline is clean.
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            long before = GC.GetTotalMemory(forceFullCollection: false);

            for (int i = 0; i < 1000; i++)
                provider.Refresh(default, default, buf, out _);

            long after = GC.GetTotalMemory(forceFullCollection: false);

            // Allow a tiny tolerance for any infrastructure overhead (GC bookkeeping, etc.).
            Assert.True(after - before < 4096,
                $"Refresh allocated heap memory: before={before}, after={after}");
        }
    }
}
