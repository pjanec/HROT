using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using CarKinem.Road;
using Xunit;

namespace CarKinem.Tests.Road
{
    /// <summary>
    /// C6 — BLOB LIFETIME. Publishing a new road graph must not free the one a background reader is
    /// still walking.
    ///
    /// <para>⛔⛔ <b>The hazard is real and it is in production today.</b> 📐 Measured:
    /// <c>ZoneManagerService.LoadZones</c> calls <c>existingRoad.Dispose()</c> <b>synchronously,
    /// immediately before</b> publishing the replacement. <see cref="RoadNetworkBlob"/> is a struct of
    /// <c>NativeArray</c>s, so that frees memory a 10 Hz <c>SlowBackground</c> solver may be mid-traversal
    /// inside — cross-thread use-after-free, no exception, no log. It does not bite yet only because
    /// nothing constructs <c>NavigationSolverModule</c> in production.</para>
    ///
    /// <para>⚠ <b>Why the assertions are on generations and not on <c>IsCreated</c>:</b> the blob is a
    /// STRUCT, so the holder disposing its own copy cannot be observed through the test's copy. The
    /// checkable facts are (a) the retired generation is PINNED while a lease is open, (b) it is RECLAIMED
    /// when the last lease closes — i.e. pinned, not leaked — and (c) the leased data stays readable
    /// across the swap.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.4 (as-built) — the hazard batch ① deferred here.
    /// </summary>
    public sealed class RoadNetworkHolderTests
    {
        private static RoadNetworkBlob TwoNodeNetwork(float endX)
        {
            var b = new RoadNetworkBuilder();
            b.AddNode(new Vector2(0f, 0f));
            b.AddNode(new Vector2(endX, 0f));
            b.AddSegment(new Vector2(0f, 0f), new Vector2(endX / 2f, 0f),
                         new Vector2(endX, 0f), new Vector2(endX / 2f, 0f),
                         startNodeIdx: 0, endNodeIdx: 1);
            return b.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
        }

        // ── The core claim ────────────────────────────────────────────────────────────────────

        [Fact]
        public void PublishingWhileALeaseIsOpen_PinsTheRetiredGraph_AndReclaimsItOnRelease()
        {
            using var holder = new RoadNetworkHolder(TwoNodeNetwork(100f), takeOwnership: true);

            var lease = holder.Borrow();
            Assert.Equal(1, holder.LiveGenerations);

            holder.Publish(TwoNodeNetwork(200f));

            // ⭐ The retired generation is still alive because WE are inside it.
            Assert.Equal(2, holder.LiveGenerations);

            // ⭐ And it is still READABLE — this is the read that would have been a use-after-free.
            Assert.True(lease.Value.Nodes.IsCreated);
            Assert.Equal(2, lease.Value.Nodes.Length);
            Assert.Equal(100f, lease.Value.Nodes[1].Position.X, 3);

            lease.Dispose();

            // ⭐ Reclaimed, not leaked: the pin is released and the retired generation goes away.
            Assert.Equal(1, holder.LiveGenerations);
        }

        [Fact]
        public void PublishingWithNoReaders_RetiresImmediately()
        {
            using var holder = new RoadNetworkHolder(TwoNodeNetwork(100f), takeOwnership: true);

            holder.Publish(TwoNodeNetwork(200f));

            // Nothing was pinning the old generation, so it was freed on the spot — no queue growth.
            Assert.Equal(1, holder.LiveGenerations);
        }

        [Fact]
        public void ANewLeaseAlwaysSeesTheLATESTGraph()
        {
            using var holder = new RoadNetworkHolder(TwoNodeNetwork(100f), takeOwnership: true);

            using (var old = holder.Borrow())
                Assert.Equal(100f, old.Value.Nodes[1].Position.X, 3);

            holder.Publish(TwoNodeNetwork(777f));

            using var fresh = holder.Borrow();
            Assert.Equal(777f, fresh.Value.Nodes[1].Position.X, 3);
        }

        [Fact]
        public void NestedLeasesOnOneRetiredGeneration_AreAllPinnedUntilTheLastRelease()
        {
            using var holder = new RoadNetworkHolder(TwoNodeNetwork(100f), takeOwnership: true);

            var a = holder.Borrow();
            var b = holder.Borrow();
            holder.Publish(TwoNodeNetwork(200f));
            Assert.Equal(2, holder.LiveGenerations);

            a.Dispose();
            Assert.Equal(2, holder.LiveGenerations);   // b still inside it

            b.Dispose();
            Assert.Equal(1, holder.LiveGenerations);
        }

        [Fact]
        public void DisposingTheHolderWhileALeaseIsOpen_IsSafe_AndTheReaderFinishes()
        {
            var holder = new RoadNetworkHolder(TwoNodeNetwork(100f), takeOwnership: true);
            var lease = holder.Borrow();

            holder.Dispose();

            // The reader is still inside the graph and must be able to finish its traversal.
            Assert.True(lease.Value.Nodes.IsCreated);
            Assert.Equal(2, lease.Value.Nodes.Length);

            lease.Dispose();
        }

        // ── The shape it actually has to survive ──────────────────────────────────────────────

        [Fact]
        public void AReaderLoopingWhileThePublisherSwaps_NeverFaults()
        {
            // ⭐ The production shape: a 10 Hz background solver borrowing/reading/releasing while the
            //   commit path republishes. Before C6 this is precisely where the free landed.
            using var holder = new RoadNetworkHolder(TwoNodeNetwork(10f), takeOwnership: true);

            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Exception? readerFault = null;
            long reads = 0;

            var reader = Task.Run(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using var lease = holder.Borrow();
                        var blob = lease.Value;

                        // Walk it the way the solver does — length then elements.
                        if (blob.Nodes.IsCreated)
                        {
                            int n = blob.Nodes.Length;
                            for (int i = 0; i < n; i++)
                            {
                                var p = blob.Nodes[i].Position;
                                if (float.IsNaN(p.X)) throw new InvalidOperationException("torn read");
                            }
                        }
                        Interlocked.Increment(ref reads);
                    }
                }
                catch (Exception ex) { readerFault = ex; }
            });

            for (int i = 0; i < 200 && !stop.IsCancellationRequested; i++)
            {
                holder.Publish(TwoNodeNetwork(10f + i));
                Thread.Sleep(1);
            }

            stop.Cancel();
            reader.Wait(TimeSpan.FromSeconds(5));

            Assert.Null(readerFault);
            Assert.True(Interlocked.Read(ref reads) > 0, "the reader never ran — the test proved nothing");
        }

        [Fact]
        public void TakeOwnershipFalse_LeavesTheBlobToTheCaller()
        {
            // ⚠ The double-free guard: a caller that keeps its own blob must be able to say so, because
            //   the struct's Dispose() cannot be observed across copies.
            var mine = TwoNodeNetwork(50f);
            using (var holder = new RoadNetworkHolder())
            {
                holder.Publish(mine, takeOwnership: false);
                holder.Publish(TwoNodeNetwork(60f));
            }

            Assert.True(mine.Nodes.IsCreated);
            mine.Dispose();
        }
    }
}
