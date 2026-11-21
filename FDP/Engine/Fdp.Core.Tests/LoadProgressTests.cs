using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-2417</c> — the node's load progress: what both heartbeat sources read so that, while a node loads, a beat means "the load advances".
    /// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §12.
    /// </summary>
    public sealed class LoadProgressTests
    {
        /// <summary>
        /// ⭐⭐ The ambient progress the slave sets reaches a loader's WORKER (<c>Task.Run</c>, as the chain runs its off-frame steps) and its awaits —
        /// so a loader reports with no signature change. Outside a load, <see cref="LoadProgress.Report"/> is free and goes nowhere.
        /// 🔴 Red-proof: <see cref="LoadProgress.Current"/> on a <c>[ThreadStatic]</c> instead of an AsyncLocal ⇒ the worker's reports are lost.
        /// </summary>
        [Fact]
        public async Task TheAmbientProgress_FlowsIntoTheWorker_AndItsAwaits()
        {
            var progress = new LoadProgress();
            LoadProgress.Current = progress;
            await Task.Run(async () =>
            {
                LoadProgress.Report(3);
                await Task.Yield();
                LoadProgress.Report(2);
            });
            Assert.Equal(5, progress.Value);

            LoadProgress.Current = null;
            LoadProgress.Report(100);   // no load: nothing, no throw
            Assert.Equal(5, progress.Value);
        }

        /// <summary>
        /// ⭐⭐ One per node BY CONSTRUCTION: keyed on the node's bus, so the slave and its translator's pump read the same instance; another node's
        /// bus gets another. 🔴 Red-proof: <see cref="LoadProgress.For"/> returning a new instance per call ⇒ not same.
        /// </summary>
        [Fact]
        public void OnePerNode_KeyedOnItsBus()
        {
            var bus = new FdpEventBus();
            Assert.Same(LoadProgress.For(bus), LoadProgress.For(bus));
            Assert.NotSame(LoadProgress.For(bus), LoadProgress.For(new FdpEventBus()));
        }

        /// <summary>
        /// ⭐⭐ Entity creation counts on its own: the watched world's structural changes are progress (the commit's and genesis's creation need no
        /// report call). 🔴 Red-proof: <see cref="LoadProgress.Value"/> without the world ⇒ unchanged.
        /// </summary>
        [Fact]
        public void TheWatchedWorlds_EntityCreation_IsProgress()
        {
            using var world = new EntityRepository();
            var progress = new LoadProgress();
            progress.Watch(world);
            long before = progress.Value;
            for (int i = 0; i < 10; i++) world.CreateEntity();
            Assert.True(progress.Value >= before + 10, $"{before} -> {progress.Value}");
        }

        /// <summary>
        /// ⭐⭐⭐ <see cref="LoadProgress.MovedWithin"/> — the question both beat sources ask: progress in the last second ⇒ yes; a stall ⇒ no; and
        /// <see cref="LoadProgress.IsActive"/> only while a step's scope is open. The longest stall of an active load is measured (the log that says
        /// which loader must report more often). 🔴 Red-proof: <c>Poll</c> not recording the move time ⇒ MovedWithin stays false after the advance.
        /// </summary>
        [Fact]
        public void MovedWithin_FollowsTheProgress_AndTheLongestStallIsMeasured()
        {
            var progress = new LoadProgress();
            Assert.False(progress.IsActive);
            using (progress.Active("Terrain"))
            {
                Assert.True(progress.IsActive);
                Assert.True(progress.MovedWithin(1.0));   // entering is progress
                Thread.Sleep(600);
                Assert.False(progress.MovedWithin(0.5));  // a stall
                progress.Advance();
                Assert.True(progress.MovedWithin(0.5));   // it moved
            }
            Assert.False(progress.IsActive);
            var (longest, step) = progress.LongestSilence;
            Assert.True(longest.TotalMilliseconds >= 550, $"the stall measured: {longest.TotalMilliseconds} ms");
            Assert.Equal("Terrain", step);
        }

        /// <summary>
        /// ⭐ <see cref="ProgressStream"/> — a large read reports while it reads (one unit per read call), and reads the bytes unchanged.
        /// 🔴 Red-proof: <c>Read</c> not advancing ⇒ 0.
        /// </summary>
        [Fact]
        public void TheProgressStream_ReportsWhileItReads()
        {
            string path = Path.GetTempFileName();
            try
            {
                var data = new byte[3 << 20];
                new Random(7).NextBytes(data);
                File.WriteAllBytes(path, data);
                var progress = new LoadProgress();
                LoadProgress.Current = progress;
                try
                {
                    Assert.Equal(data, ProgressStream.ReadAllBytes(path));
                    Assert.True(progress.Value >= 3, $"3 MB in 1 MB reads ⇒ at least 3 reports; got {progress.Value}");
                }
                finally { LoadProgress.Current = null; }
            }
            finally { File.Delete(path); }
        }
    }
}
