using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Threading;
using Xunit;
using Pos = Fdp.Tests.RecorderDeltaLogicTests.Position;
using Vel = Fdp.Tests.RecorderDeltaLogicTests.Velocity;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ N-3 (CE-4465; R-4216 / R-4217) — the two staging buffers between the sim world S and the presentation world A, no lock between threads:
    /// ① the UI never sees a TORN tick (every entity of A carries the same tick stamp after every pull, the UI frame ~5–16 ms); ② the sim publishes
    /// FASTER than the UI pulls ⇒ every event and every job reply still reaches A once, in order; ③ the sim never blocks — while the UI holds a
    /// buffer, a publish SKIPS (returns at once) and the next publish carries what it skipped; ④ 0 B per steady publish + pull.
    /// 🔴 Red-proofs (run, each reverted): ① <c>Publish</c> writing the LATEST buffer (the one the UI pulls) instead of the other one, with the claim
    /// removed ⇒ torn ticks; ② <c>Pull</c> draining only the packet of the pulled tick ⇒ events of skipped / never-pulled publishes lost;
    /// ③ <c>Publish</c> spinning until the buffer is free instead of skipping ⇒ the max publish time exceeds the UI's hold.
    /// </summary>
    public class StagingWorldsTests
    {
        [EventId(9761)] public struct StagingEvent { public long Seq; }

        private static (EntityRepository S, Entity[] Es) NewSim(int n)
        {
            var s = new EntityRepository();
            s.RegisterComponent<Pos>(); s.RegisterComponent<Vel>();
            var es = new Entity[n];
            for (int i = 0; i < n; i++) { es[i] = s.CreateEntity(); s.AddComponent(es[i], new Pos { X = -1 }); s.AddComponent(es[i], new Vel { VX = -1 }); }
            return (s, es);
        }

        private static StagingWorlds NewStaging(EntityRepository s)
        {
            var st = new StagingWorlds();
            st.TableMask.SetBit(ComponentType<Pos>.ID);
            st.RegisterStreamsFrom(s.Bus);
            return st;
        }

        [Fact]
        public void TheUiNeverSeesATornTick()
        {
            var (s, es) = NewSim(600);
            using var _s = s;
            using var st = NewStaging(s);
            using var a = new EntityRepository();
            var stop = new ManualResetEventSlim(false);
            var sim = new Thread(() =>
            {
                long tick = 0;
                while (!stop.IsSet)
                {
                    tick++;
                    s.Tick();
                    for (int i = 0; i < es.Length; i++) s.GetComponentRW<Pos>(es[i]).X = tick;   // the tick's stamp, written entity by entity
                    st.Publish(s, tick);
                }
            });
            sim.Start();
            var rnd = new Random(7);
            int pulls = 0, checkedEntities = 0;
            var until = Stopwatch.StartNew();
            try
            {
                while (until.ElapsedMilliseconds < 1500)
                {
                    if (st.Pull(a))
                    {
                        pulls++;
                        float stamp = a.GetComponentRO<Pos>(es[0]).X;
                        Assert.Equal(st.PulledTick, (long)stamp);
                        for (int i = 0; i < es.Length; i++) { Assert.Equal(stamp, a.GetComponentRO<Pos>(es[i]).X); checkedEntities++; }
                    }
                    Thread.Sleep(5 + rnd.Next(12));   // a UI frame of ~5–16 ms
                }
            }
            finally { stop.Set(); sim.Join(); }
            Assert.True(pulls >= 20, $"only {pulls} pulls");
            Assert.True(st.Published > pulls, "the sim publishes faster than the UI pulls in this rail");
            Assert.True(checkedEntities > 0);
        }

        [Fact]
        public void PublishingFasterThanTheUiPulls_EveryEventAndReplyReachesA_Once_InOrder_AndTheSimNeverBlocks()
        {
            var (s, es) = NewSim(200);
            using var _s = s;
            using var st = NewStaging(s);
            using var a = new EntityRepository();
            st.WhileHoldingForTest = () => Thread.Sleep(20);   // the UI holds each pulled buffer 20 ms — the sim must skip, never wait
            const long Ticks = 3000;
            var stop = new ManualResetEventSlim(false);
            double maxPublishMs = 0;
            var sim = new Thread(() =>
            {
                for (long tick = 1; tick <= Ticks; tick++)
                {
                    s.Tick();
                    s.Bus.Publish(new StagingEvent { Seq = tick });
                    s.Bus.SwapBuffers();                                   // the tick's event is readable now
                    long t = tick;
                    st.Post(p => Replies.Add(t));                          // a job reply / log line bound for A
                    var sw = Stopwatch.StartNew();
                    st.Publish(s, tick);
                    maxPublishMs = Math.Max(maxPublishMs, sw.Elapsed.TotalMilliseconds);
                }
                s.Bus.SwapBuffers();
                st.Publish(s, Ticks + 1);                                  // a last tick with no event: it carries anything skipped
                while (st.Skipped > 0 && st.LatestTick != Ticks + 1) st.Publish(s, Ticks + 1);
                stop.Set();
            });
            var seen = new List<long>();
            Replies.Clear();
            sim.Start();
            while (!stop.IsSet || st.PulledTick < st.LatestTick)
            {
                if (st.Pull(a)) foreach (var e in a.Bus.Read<StagingEvent>()) seen.Add(e.Seq);
                Thread.Sleep(1);
            }
            sim.Join();
            Assert.True(st.Skipped > 0, "the rail must make the sim skip");
            Assert.Equal(Ticks, seen.Count);
            for (int i = 0; i < seen.Count; i++) Assert.Equal(i + 1, seen[i]);
            Assert.Equal(Ticks, Replies.Count);
            for (int i = 0; i < Replies.Count; i++) Assert.Equal(i + 1, Replies[i]);
            Assert.True(maxPublishMs < 15, $"a publish took {maxPublishMs:F1} ms while the UI held a buffer 20 ms — it waited");
        }

        private static readonly List<long> Replies = new();

        [Fact]
        public void RowsCopyOnlyTypesOutsideTheMask_AndUiOwnedSingletonsSurviveAPull()
        {
            var (s, es) = NewSim(10);
            using var _s = s;
            using var st = NewStaging(s);
            using var a = new EntityRepository();
            s.Tick();
            s.GetComponentRW<Vel>(es[3]).VX = 33;
            st.SetRows(new[] { es[3] });
            Assert.True(st.Publish(s, 1));
            Assert.True(st.Pull(a));
            Assert.True(a.HasComponent<Vel>(es[3]));                          // the row brought the out-of-mask type
            Assert.Equal(33, a.GetComponentRO<Vel>(es[3]).VX);
            Assert.False(a.HasComponent<Vel>(es[4]));                         // not a row: the out-of-mask type stays out
            Assert.False(st.Pull(a));                                          // nothing newer
        }

        public sealed class ClusterNote { public long Seq; }
        public sealed class UiIntent { public int V; }

        /// <summary>⭐ D-9 — a second BUS PAIR (the orchestration bus and the UI's mirror): every managed event of the sim's bus reaches the mirror
        /// exactly once, in order — including a type first registered AFTER the pair was added (never dropped silently); what the UI publishes on
        /// the mirror reaches the sim's bus at <c>TakeUplinks</c>, not before.
        /// 🔴 Red-proof (run): the pull without <c>addMissingManagedStreams</c> ⇒ the late type never reaches the mirror.</summary>
        [Fact]
        public void ASecondBusPair_CarriesManagedEventsDownOnce_AndTheUisPublishesUp()
        {
            var (s, es) = NewSim(4);
            using var _s = s;
            using var st = NewStaging(s);
            using var a = new EntityRepository();
            var simOrch = new FdpEventBus();
            var uiOrch = new FdpEventBus();
            st.AddBusPair(simOrch, uiOrch);
            var seen = new List<long>();
            for (long t = 1; t <= 50; t++)
            {
                simOrch.PublishManaged(new ClusterNote { Seq = t });   // first published (registered) after AddBusPair
                simOrch.SwapBuffers();
                st.Publish(s, t);
                if (t % 7 == 0 && st.Pull(a)) foreach (var n in uiOrch.ReadManaged<ClusterNote>()) seen.Add(n.Seq);
            }
            simOrch.SwapBuffers();
            st.Publish(s, 51);
            Assert.True(st.Pull(a));
            foreach (var n in uiOrch.ReadManaged<ClusterNote>()) seen.Add(n.Seq);
            Assert.Equal(50, seen.Count);
            for (int i = 0; i < seen.Count; i++) Assert.Equal(i + 1, seen[i]);

            uiOrch.PublishManaged(new UiIntent { V = 7 });                // the UI publishes on its mirror …
            simOrch.PublishManaged(new ClusterNote { Seq = 52 }); simOrch.SwapBuffers(); st.Publish(s, 52);
            Assert.True(st.Pull(a));                                       // … the pull sends it up …
            simOrch.SwapBuffers();
            Assert.Empty(simOrch.ReadManaged<UiIntent>());                 // … but nothing reaches the sim's bus before the sim takes it
            st.TakeUplinks(s);
            simOrch.SwapBuffers();
            Assert.Single(simOrch.ReadManaged<UiIntent>());
            Assert.Equal(7, simOrch.ReadManaged<UiIntent>()[0].V);
        }

        [Fact]
        public void SteadyPublishAndPull_AllocateNothing()
        {
            var (s, es) = NewSim(300);
            using var _s = s;
            using var st = NewStaging(s);
            using var a = new EntityRepository();
            st.SetRows(new[] { es[1], es[2] });
            for (long t = 1; t <= 20; t++) { s.Tick(); s.GetComponentRW<Pos>(es[(int)t]).X = t; s.Bus.Publish(new StagingEvent { Seq = t }); s.Bus.SwapBuffers(); st.Publish(s, t); st.Pull(a); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (long t = 21; t <= 120; t++) { s.Tick(); s.GetComponentRW<Pos>(es[(int)(t % 300)]).X = t; s.Bus.Publish(new StagingEvent { Seq = t }); s.Bus.SwapBuffers(); st.Publish(s, t); st.Pull(a); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
