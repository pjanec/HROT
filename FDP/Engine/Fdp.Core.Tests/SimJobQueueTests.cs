using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Threading;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ N-1 (CE-4463) — the one sim job queue (docs/DESIGN_Threaded_Work_Inventory.md §8, D-5): posted from several threads ⇒ applied on the
    /// sim thread, in post order, exactly once; a job's reply reaches the presentation outbox in order; with no <see cref="SimLink"/> a write runs at
    /// once on the given world (the switch-off path, unchanged).
    /// 🔴 Red-proof (run): <c>Drain</c> dequeuing into a stack and running it LIFO ⇒ the per-producer order assertion fails.
    /// </summary>
    public class SimJobQueueTests
    {
        private sealed class ListOutbox : IPresentationOutbox
        {
            public readonly List<Action<EntityRepository>> Items = new();
            public void Post(Action<EntityRepository> a) { lock (Items) Items.Add(a); }
        }

        [Fact]
        public void PostedFromSeveralThreads_RunOnTheSimThread_InPostOrder_ExactlyOnce()
        {
            var q = new SimJobQueue();
            using var sim = new EntityRepository();
            const int Producers = 4, PerProducer = 2000;
            var ran = new List<(int Producer, int I, int Thread)>();
            int simThread = 0;
            var stop = new ManualResetEventSlim(false);
            var simLoop = new Thread(() =>
            {
                simThread = Environment.CurrentManagedThreadId;
                while (!stop.IsSet) { q.Drain(sim); Thread.Yield(); }
                q.Drain(sim);
            });
            simLoop.Start();
            var producers = new Thread[Producers];
            for (int p = 0; p < Producers; p++)
            {
                int pp = p;
                producers[p] = new Thread(() =>
                {
                    for (int i = 0; i < PerProducer; i++)
                    {
                        int ii = i;
                        q.Post(_ => { ran.Add((pp, ii, Environment.CurrentManagedThreadId)); }, "rail");   // only the sim thread runs jobs: no lock needed
                    }
                });
                producers[p].Start();
            }
            foreach (var t in producers) t.Join();
            SpinWait.SpinUntil(() => q.Pending == 0, 10_000);
            stop.Set(); simLoop.Join();

            Assert.Equal(Producers * PerProducer, ran.Count);
            Assert.Equal(Producers * PerProducer, q.Executed);
            Assert.Equal(0, q.Faults);
            var next = new int[Producers];
            foreach (var (p, i, th) in ran)
            {
                Assert.Equal(simThread, th);
                Assert.Equal(next[p], i);   // per producer: post order; each exactly once
                next[p]++;
            }
            Assert.Equal(simThread, q.SimThreadId);
        }

        [Fact]
        public void AJobPostedByAJob_RunsAtTheNextDrain_AndAThrowingJobDoesNotStopTheDrain()
        {
            var q = new SimJobQueue();
            using var sim = new EntityRepository();
            var order = new List<string>();
            q.Post(_ => { order.Add("a"); q.Post(__ => order.Add("nested"), "nested"); }, "a");
            q.Post(_ => throw new InvalidOperationException("boom"), "throws");
            q.Post(_ => order.Add("b"), "b");
            Assert.Equal(3, q.Drain(sim));
            Assert.Equal(new[] { "a", "b" }, order);
            Assert.Equal(1, q.Faults);
            Assert.Equal("throws", q.LastFault);
            Assert.Equal(1, q.Drain(sim));
            Assert.Equal(new[] { "a", "b", "nested" }, order);
        }

        [Fact]
        public void AReply_GoesToTheOutbox_InOrder()
        {
            var q = new SimJobQueue();
            var outbox = new ListOutbox();
            q.Outbox = outbox;
            using var sim = new EntityRepository();
            var seen = new List<int>();
            for (int i = 0; i < 5; i++) { int ii = i; q.Post(_ => p => seen.Add(ii), "reply"); }
            q.Drain(sim);
            Assert.Empty(seen);                       // nothing runs on the UI's world until the UI drains the outbox
            Assert.Equal(5, outbox.Items.Count);
            using var a = new EntityRepository();
            foreach (var r in outbox.Items) r(a);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, seen);
        }

        [Fact]
        public void WithNoLink_AWriteRunsAtOnce_OnTheGivenWorld_AndItsReplyToo()
        {
            using var world = new EntityRepository();
            EntityRepository? ranOn = null, repliedOn = null;
            SimJobs.Run(world, s => { ranOn = s; return p => repliedOn = p; }, "inline");
            Assert.Same(world, ranOn);
            Assert.Same(world, repliedOn);
            Assert.False(SimJobs.IsThreaded(world));
        }

        [Fact]
        public void WithALink_AWriteIsPosted_AndRunsOnTheSimWorldAtTheDrain()
        {
            using var a = new EntityRepository();
            using var s = new EntityRepository();
            var q = new SimJobQueue();
            a.SetSingletonManaged(new SimLink(q, s));
            EntityRepository? ranOn = null;
            SimJobs.Run(a, w => ranOn = w, "posted");
            Assert.True(SimJobs.IsThreaded(a));
            Assert.Null(ranOn);
            Assert.Equal(1, q.Pending);
            q.Drain(s);
            Assert.Same(s, ranOn);
        }

        [Fact]
        public void PostAndWait_CompletesWithTheJobsResult_OnTheDrain()
        {
            var q = new SimJobQueue();
            using var sim = new EntityRepository();
            var t = q.PostAndWait(s => 42, "answer");
            Assert.False(t.IsCompleted);
            q.Drain(sim);
            Assert.True(t.Wait(5000));
            Assert.Equal(42, t.Result);
        }
    }
}
