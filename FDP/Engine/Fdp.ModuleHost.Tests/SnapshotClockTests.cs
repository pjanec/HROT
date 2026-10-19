using Xunit;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3267</c> — a MODULE'S VIEW carries the frame's clock: every snapshot provider copies the live world's
    /// <see cref="GlobalTime"/> into the view it hands out. 📌 Before, a snapshot carried none, so a background module read time 0 for ever
    /// (the EQS memory stage's acquisition dwell never passed beyond 100 m — CE-3260). ✅ Red-proof: each provider's
    /// <c>CopyClockFrom</c> line removed ⇒ its case reads no clock ⇒ red.
    /// </summary>
    public class SnapshotClockTests
    {
        private static EntityRepository LiveAt(double totalTime)
        {
            var live = new EntityRepository();
            live.SetSingletonUnmanaged(new GlobalTime { TotalTime = totalTime, DeltaTime = 0.1f, TimeScale = 1f, FrameNumber = 42 });
            return live;
        }

        private static void AssertClock(ISimulationView view, double totalTime)
        {
            var snap = Assert.IsType<EntityRepository>(view);
            Assert.True(snap.HasSingleton<GlobalTime>(), "the module's view carries the frame's clock");
            Assert.Equal(totalTime, snap.GetSingleton<GlobalTime>().TotalTime);
        }

        [Fact]
        public void CE3267_TheDoubleBufferReplica_CarriesTheLiveClock_AndFollowsIt()
        {
            using var live = LiveAt(12.5);
            using var provider = new DoubleBufferProvider(live, new EventAccumulator());
            provider.Update();
            AssertClock(provider.AcquireView(), 12.5);

            live.SetSingletonUnmanaged(new GlobalTime { TotalTime = 13.0, DeltaTime = 0.5f, TimeScale = 1f });
            provider.Update();
            AssertClock(provider.AcquireView(), 13.0);
        }

        [Fact]
        public void CE3267_AnOnDemandSnapshot_CarriesTheLiveClock()
        {
            using var live = LiveAt(7.25);
            var mask = new BitMask512();
            for (int i = 0; i < 256; i++) mask.SetBit(i);
            var provider = new OnDemandProvider(live, new EventAccumulator(), mask, null, 5);
            var view = provider.AcquireView();
            AssertClock(view, 7.25);
            provider.ReleaseView(view);
        }

        [Fact]
        public void CE3267_ASharedConvoySnapshot_CarriesTheLiveClock()
        {
            using var live = LiveAt(99.0);
            var mask = new BitMask512();
            for (int i = 0; i < 256; i++) mask.SetBit(i);
            var provider = new SharedSnapshotProvider(live, new EventAccumulator(), mask, new SnapshotPool(null));
            var view = provider.AcquireView();
            AssertClock(view, 99.0);
            provider.ReleaseView(view);
        }

        /// <summary>⛔ <c>SyncFrom</c> alone copies NO clock — it also restores the live world from a snapshot (a preview's rewind), which
        /// must never rewind the clock (Time Architecture §12a). The copy is the providers' explicit step.</summary>
        [Fact]
        public void CE3267_SyncFromAlone_LeavesTheTargetsClock()
        {
            using var older = LiveAt(5.0);
            using var live = LiveAt(50.0);
            live.SyncFrom(older);
            Assert.Equal(50.0, live.GetSingleton<GlobalTime>().TotalTime);
        }
    }
}
