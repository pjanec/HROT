using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using Fdp.Toolkit.Runner;
using Xunit;

namespace Fdp.Toolkits.Tests.Runner
{
    /// <summary>
    /// ⭐⭐ N-13 (CE-4475; U-2 / F-21) — a threaded process runs EVERY kernel on ONE sim worker, in sequence: a staged subsystem's own sim step
    /// and a plain subsystem's Update both run on that one thread (never the caller's), the boundary work too; the caller keeps only the staged
    /// subsystem's presentation; a plain subsystem is not drawn while its kernel ticks on the worker.
    /// 🔴 Red-proof: the plain subsystems' Update left on the calling thread ⇒ two threads tick kernels.
    /// </summary>
    public sealed class OneSimWorkerTests
    {
        private sealed class Plain : ISubsystem
        {
            public readonly ConcurrentDictionary<int, int> UpdateThreads = new();
            public int Draws;
            public string Name { get; init; } = "plain";
            public Vector4 TitleBarColor => Vector4.Zero;
            public void Initialize(SubsystemConfig config) { }
            public void Update(float deltaTime) => UpdateThreads[Environment.CurrentManagedThreadId] = 1;
            public void DrawWorld() => Draws++;
            public void DrawUI() => Draws++;
            public void Shutdown() { }
        }

        private sealed class Staged : ISubsystem, ISimSteppedSubsystem
        {
            public readonly ConcurrentDictionary<int, int> StepThreads = new(), UpdateThreads = new();
            public bool Hosted;
            public string Name => "staged";
            public Vector4 TitleBarColor => Vector4.Zero;
            public void Initialize(SubsystemConfig config) { }
            public void Update(float deltaTime) => UpdateThreads[Environment.CurrentManagedThreadId] = 1;
            public void DrawWorld() { }
            public void DrawUI() { }
            public void Shutdown() { }
            public void UseHostSimWorker() => Hosted = true;
            public bool SimStepOnHostWorker() { StepThreads[Environment.CurrentManagedThreadId] = 1; return true; }
        }

        [Fact]
        public void N13_EveryKernel_RunsOnTheOneSimWorker_TheCallerKeepsOnlyThePresentation()
        {
            var a = new Plain { Name = "cgf" };
            var b = new Plain { Name = "simhost" };
            var s = new Staged();
            var orch = new SubsystemOrchestrator(new ISubsystem[] { a, s, b });
            orch.Initialize();
            var boundary = new ConcurrentDictionary<int, int>();
            Assert.True(orch.Draws(a));                         // no worker yet: every subsystem draws
            orch.StartSimWorker(() => boundary[Environment.CurrentManagedThreadId] = 1);
            Assert.True(s.Hosted);
            try
            {
                int caller = Environment.CurrentManagedThreadId;
                var until = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < until && (a.UpdateThreads.IsEmpty || b.UpdateThreads.IsEmpty || s.StepThreads.IsEmpty))
                {
                    orch.Update(0.016f);
                    orch.DrawWorldAll();
                    orch.DrawUIAll();
                    Thread.Sleep(2);
                }
                var threads = new System.Collections.Generic.HashSet<int>(a.UpdateThreads.Keys);
                threads.UnionWith(b.UpdateThreads.Keys);
                threads.UnionWith(s.StepThreads.Keys);
                threads.UnionWith(boundary.Keys);
                Assert.Single(threads);                         // ONE thread ticks every kernel (and runs the boundary work)
                Assert.DoesNotContain(caller, threads);         // … and it is not the caller's
                Assert.Equal(new[] { caller }, s.UpdateThreads.Keys);   // the staged presentation stays on the caller
                Assert.Equal(0, a.Draws + b.Draws);             // a plain subsystem is not drawn beside its ticking world
                Assert.False(orch.Draws(a));                    // ⭐ the ONE rule the runner also gates each subsystem's windows by
                Assert.True(orch.Draws(s));
                Assert.Equal(0, orch.SimWorkerFaults);
            }
            finally { orch.Stop(); }
        }

        private sealed class Disposable : ISubsystem, ISimSteppedSubsystem
        {
            private int _inStep;
            public volatile bool Disposed;
            public int StepsAfterShutdown, ShutdownDuringStep, Steps;
            public string Name => "disposable";
            public Vector4 TitleBarColor => Vector4.Zero;
            public void Initialize(SubsystemConfig config) { }
            public void Update(float deltaTime) { }
            public void DrawWorld() { }
            public void DrawUI() { }
            public void UseHostSimWorker() { }
            public bool SimStepOnHostWorker()
            {
                Interlocked.Exchange(ref _inStep, 1);
                if (Disposed) Interlocked.Increment(ref StepsAfterShutdown);
                Thread.Sleep(2);                                // a step takes time: the window a shutdown used to land in
                Interlocked.Increment(ref Steps);
                Interlocked.Exchange(ref _inStep, 0);
                return true;
            }
            public void Shutdown()
            {
                if (Volatile.Read(ref _inStep) == 1) Interlocked.Increment(ref ShutdownDuringStep);
                Disposed = true;                                // the worlds are gone from here on
                Thread.Sleep(5);                                // … and a step that still ran would touch them
            }
        }

        /// <summary>
        /// ⭐ CE-4574 (X-5; docs/DESIGN_Threaded_Presentation.md §15) — <see cref="SubsystemOrchestrator.Shutdown"/> STOPS and JOINS the sim worker
        /// before any subsystem disposes its worlds: no step is in flight when a Shutdown starts, and none runs after it. 📐 The editor crashed on
        /// exit (<c>AccessViolationException</c> in <c>SyncPresentationCore</c> during <c>StagingWorlds.Publish</c>): the worker was still
        /// publishing into a staging world the shutdown had freed. Start → play → shutdown, twenty times.
        /// 🔴 Red-proof: <c>Shutdown</c> without <c>StopSimWorker</c> ⇒ steps during / after the shutdown.
        /// </summary>
        [Fact]
        public void X5_Shutdown_StopsAndJoinsTheSimWorker_BeforeAnySubsystemDisposes()
        {
            for (int round = 0; round < 20; round++)
            {
                var s = new Disposable();
                var orch = new SubsystemOrchestrator(new ISubsystem[] { s });
                orch.Initialize();
                orch.StartSimWorker(null);
                var until = DateTime.UtcNow.AddSeconds(5);
                while (Volatile.Read(ref s.Steps) < 3 && DateTime.UtcNow < until) { orch.Update(0.016f); Thread.Sleep(1); }
                Assert.True(s.Steps >= 3, "the worker never stepped");
                orch.Shutdown();
                Thread.Sleep(10);                               // a worker that outlived the shutdown would step again in here
                Assert.Equal(0, s.ShutdownDuringStep);
                Assert.Equal(0, s.StepsAfterShutdown);
                Assert.False(orch.SimWorkerAlive);
            }
        }
    }
}
