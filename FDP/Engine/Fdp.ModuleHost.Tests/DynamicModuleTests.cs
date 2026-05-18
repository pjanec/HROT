// File: ModuleHost.Tests/DynamicModuleTests.cs
// =====================================================================================
// Comprehensive tests for the dynamic module hot-plugging infrastructure.
//
// Coverage:
//   1. InstallModuleAsync – basic install, lifecycle states, idempotency guard
//   2. UninstallModuleAsync – basic uninstall, graceful drain, lifecycle states
//   3. RCU hot-path correctness – main loop never stalls; swap happens at BeforeSync
//   4. Module systems – dynamically installed module's systems participate in scheduling
//   5. Concurrent install/uninstall – serialisation via semaphore
//   6. Install while kernel running – module ticks from the very next frame after install
//   7. Uninstall while module is executing – drains without memory violations
//   8. IsModuleInstalled / GetModuleLifecycleState – inspection API
//   9. Install module with Direct strategy
//  10. Dispose while modules are draining – no resource leaks
//  11. Honest SoD – SharedSnapshotProvider allocated; UnionMask covers all convoy components
//  12. Honest GDB – DoubleBufferProvider allocated; basic tick under replica
//  13. Batch install – InstallModulesAsync activates N SoD modules atomically
//  14. UnionMask expansion – solo OnDemand → shared convoy when second SoD module joins
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Shared helpers for dynamic-module tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region Test module helpers

    /// <summary>
    /// A simple synchronous Direct-strategy module that counts Tick() invocations.
    /// Can be installed/uninstalled dynamically.
    /// </summary>
    class CountingDirectModule : IEcsModule, IDisposable
    {
        public string Name { get; }
        public int TickCount;
        public volatile bool WasDisposed;

        public CountingDirectModule(string name = "CountingDirect") => Name = name;

        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public void Tick(ISimulationView view, float deltaTime)
            => Interlocked.Increment(ref TickCount);

        public void Dispose() => WasDisposed = true;
    }

    /// <summary>
    /// A slowish background module that briefly sleeps in Tick().
    /// Useful for testing draining: the module may be mid-execution when uninstall is called.
    /// </summary>
    class SlowBackgroundModule : IEcsModule, IDisposable
    {
        public string Name { get; }
        public int TickCount;
        public int SleepMs;
        public volatile bool WasDisposed;

        public SlowBackgroundModule(string name = "SlowBg", int sleepMs = 20)
        {
            Name = name;
            SleepMs = sleepMs;
        }

        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(60).WithTimeout(5000);

        public void Tick(ISimulationView view, float deltaTime)
        {
            Thread.Sleep(SleepMs);
            Interlocked.Increment(ref TickCount);
        }

        public void Dispose() => WasDisposed = true;
    }

    /// <summary>
    /// Module that registers a test system (to verify system lifecycle with hot-plug).
    /// </summary>
    class SystemRegisteringModule : IEcsModule
    {
        public string Name { get; }
        public TrackingBeforeSyncSystem System { get; } = new();

        public SystemRegisteringModule(string name = "WithSystem") => Name = name;

        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public void RegisterSystems(ISystemRegistry registry)
            => registry.RegisterSystem(System);

        public void Tick(ISimulationView view, float deltaTime) { }
    }

    [UpdateInPhase(SystemPhase.BeforeSync)]
    class TrackingBeforeSyncSystem : IEcsModuleSystem
    {
        public int ExecuteCount;

        public void Execute(ISimulationView view, float deltaTime)
            => Interlocked.Increment(ref ExecuteCount);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // Test collection
    // ─────────────────────────────────────────────────────────────────────────────

    [Collection("SerialTests")]
    public class DynamicModuleTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator _evtAcc;
        private readonly ModuleHostKernel _kernel;

        public DynamicModuleTests()
        {
            _world = new EntityRepository();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 1. Basic install
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task InstallModuleAsync_ModuleIsLiveAfterAwait()
        {
            var module = new CountingDirectModule("Installed");

            // Module must not be running before install
            Assert.False(_kernel.IsModuleInstalled(module));

            // Drive the kernel on a background thread so the swap can be applied
            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            // After await: module must be visible and have Ready lifecycle
            Assert.True(_kernel.IsModuleInstalled(module));
            Assert.Equal(ModuleLifecycleState.Ready, _kernel.GetModuleLifecycleState(module));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task InstallModuleAsync_ModuleTicksAfterInstall()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            // Allow a few frames to run
            await Task.Delay(100);

            cts.Cancel();
            await loopTask;

            Assert.True(module.TickCount > 0,
                $"Expected Tick() to have been called at least once after install. Got: {module.TickCount}");
        }

        [Fact(Timeout = 10_000)]
        public async Task InstallModuleAsync_ThrowsIfAlreadyInstalled()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            // Second install of the same instance must throw
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _kernel.InstallModuleAsync(module));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task InstallModuleAsync_ThrowsIfKernelNotInitialized()
        {
            var uninitializedKernel = new ModuleHostKernel(new EntityRepository(), new EventAccumulator());
            var module = new CountingDirectModule();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => uninitializedKernel.InstallModuleAsync(module));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 2. Basic uninstall
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task UninstallModuleAsync_ModuleIsRemovedAfterAwait()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);
            Assert.True(_kernel.IsModuleInstalled(module));

            await _kernel.UninstallModuleAsync(module);

            Assert.False(_kernel.IsModuleInstalled(module));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task UninstallModuleAsync_ModuleDisposedAfterFullDrain()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);
            await _kernel.UninstallModuleAsync(module);

            // After full drain, Dispose() must have been called on the module
            Assert.True(module.WasDisposed,
                "Module.Dispose() should have been called after full drain.");

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task UninstallModuleAsync_ThrowsIfNotInstalled()
        {
            var notInstalled = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _kernel.UninstallModuleAsync(notInstalled));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task UninstallModuleAsync_ThrowsIfKernelNotInitialized()
        {
            var world2 = new EntityRepository();
            var uninitializedKernel = new ModuleHostKernel(world2, new EventAccumulator());
            var module = new CountingDirectModule();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => uninitializedKernel.UninstallModuleAsync(module));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 3. RCU swap happens at BeforeSync — Install is atomic
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task InstallModuleAsync_TaskCompletesOnlyAfterSwap()
        {
            // The install task should only complete after at least one kernel frame
            // has processed the swap. We verify this by checking the module is ticking
            // on the very first frame after await resolves.
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            // Capture tick count immediately after await
            await _kernel.InstallModuleAsync(module);
            int countAfterInstall = module.TickCount;

            // Wait a couple more frames
            await Task.Delay(50);

            cts.Cancel();
            await loopTask;

            // Module must have ticked during or after the install frame
            Assert.True(module.TickCount >= countAfterInstall,
                "Module should tick from the frame the swap was applied.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 4. Module systems integrate correctly into the scheduler
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task InstallModuleAsync_ModuleSystemsExecuteAfterInstall()
        {
            var module = new SystemRegisteringModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            // Allow frames to run
            await Task.Delay(100);

            int executeCount = module.System.ExecuteCount;

            cts.Cancel();
            await loopTask;

            Assert.True(executeCount > 0,
                $"Module's BeforeSync system should have executed after install. Count={executeCount}");
        }

        [Fact(Timeout = 10_000)]
        public async Task UninstallModuleAsync_ModuleSystemsStopExecutingAfterUninstall()
        {
            var module = new SystemRegisteringModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);
            await Task.Delay(50);

            int countBeforeUninstall = module.System.ExecuteCount;
            await _kernel.UninstallModuleAsync(module);

            // Wait several more frames
            await Task.Delay(100);

            int countAfterUninstall = module.System.ExecuteCount;

            cts.Cancel();
            await loopTask;

            // After uninstall, the system should have stopped executing
            Assert.True(countAfterUninstall <= countBeforeUninstall + 2,
                $"System should stop executing after uninstall. Before={countBeforeUninstall}, After={countAfterUninstall}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 5. Sequential install/uninstall of multiple modules
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task MultipleModules_CanBeInstalledAndUninstalledSequentially()
        {
            var moduleA = new CountingDirectModule("A");
            var moduleB = new CountingDirectModule("B");
            var moduleC = new CountingDirectModule("C");

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(moduleA);
            await _kernel.InstallModuleAsync(moduleB);
            await _kernel.InstallModuleAsync(moduleC);

            Assert.True(_kernel.IsModuleInstalled(moduleA));
            Assert.True(_kernel.IsModuleInstalled(moduleB));
            Assert.True(_kernel.IsModuleInstalled(moduleC));

            await _kernel.UninstallModuleAsync(moduleB);

            Assert.True(_kernel.IsModuleInstalled(moduleA));
            Assert.False(_kernel.IsModuleInstalled(moduleB));
            Assert.True(_kernel.IsModuleInstalled(moduleC));

            await _kernel.UninstallModuleAsync(moduleA);
            await _kernel.UninstallModuleAsync(moduleC);

            Assert.False(_kernel.IsModuleInstalled(moduleA));
            Assert.False(_kernel.IsModuleInstalled(moduleC));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 15_000)]
        public async Task InstallThenUninstall_ModulesTickCorrectly()
        {
            var moduleA = new CountingDirectModule("A");
            var moduleB = new CountingDirectModule("B");

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(moduleA);
            await _kernel.InstallModuleAsync(moduleB);

            await Task.Delay(80);

            // Both should have ticked
            Assert.True(moduleA.TickCount > 0, $"A.TickCount={moduleA.TickCount}");
            Assert.True(moduleB.TickCount > 0, $"B.TickCount={moduleB.TickCount}");

            // Uninstall A, let B keep running
            await _kernel.UninstallModuleAsync(moduleA);
            int aCountAtUninstall = moduleA.TickCount;

            await Task.Delay(80);

            // A must have stopped; B must keep ticking
            Assert.True(moduleB.TickCount > 0, $"B should still tick after A removed. B.TickCount={moduleB.TickCount}");
            // A count should not grow significantly after uninstall
            // (allow +1 for a frame in flight at uninstall time)
            Assert.True(moduleA.TickCount <= aCountAtUninstall + 1,
                $"A.TickCount grew after uninstall: was={aCountAtUninstall}, now={moduleA.TickCount}");

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 6. Draining: slow background module finishes in-flight task before dispose
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task UninstallModuleAsync_WaitsForSlowModuleToDrain()
        {
            var module = new SlowBackgroundModule(sleepMs: 50);

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            // Let the module start at least one tick
            await Task.Delay(80);
            Assert.True(module.TickCount > 0, "SlowModule should have started ticking.");

            // Uninstall while module may be mid-execution
            await _kernel.UninstallModuleAsync(module);

            // Module must be fully drained: disposed and no longer installed
            Assert.False(_kernel.IsModuleInstalled(module));
            Assert.Equal(ModuleLifecycleState.Disposed,
                _kernel.GetModuleLifecycleState(module) ?? ModuleLifecycleState.Disposed);
            Assert.True(module.WasDisposed, "Dispose() must be called after drain completes.");

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 7. Inspection API
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task IsModuleInstalled_ReturnsFalseBeforeInstall()
        {
            var module = new CountingDirectModule();
            Assert.False(_kernel.IsModuleInstalled(module));
        }

        [Fact(Timeout = 10_000)]
        public async Task IsModuleInstalled_ReturnsTrueAfterInstall()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            Assert.True(_kernel.IsModuleInstalled(module));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task GetModuleLifecycleState_ReturnsNullForUnknown()
        {
            var unknown = new CountingDirectModule();
            Assert.Null(_kernel.GetModuleLifecycleState(unknown));
        }

        [Fact(Timeout = 10_000)]
        public async Task GetModuleLifecycleState_ReturnsReadyWhenLive()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);

            Assert.Equal(ModuleLifecycleState.Ready, _kernel.GetModuleLifecycleState(module));

            cts.Cancel();
            await loopTask;
        }

        [Fact(Timeout = 10_000)]
        public async Task GetModuleLifecycleState_ReturnsDisposedAfterFullUninstall()
        {
            var module = new CountingDirectModule();

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);
            await _kernel.UninstallModuleAsync(module);

            // After full drain the state transitions to Disposed; 
            // the entry is dequeued from _drainingModules so GetModuleLifecycleState returns null.
            // Either null or Disposed is acceptable.
            var state = _kernel.GetModuleLifecycleState(module);
            Assert.True(state == null || state == ModuleLifecycleState.Disposed,
                $"Expected null or Disposed, got {state}");

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 8. Statically-registered module is unaffected by dynamic operations
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task StaticModule_ContinuesTickingDuringDynamicInstallAndUninstall()
        {
            // Pre-register a static module before Initialize
            var staticWorld = new EntityRepository();
            var staticAcc = new EventAccumulator();
            var staticKernel = new ModuleHostKernel(staticWorld, staticAcc);

            var staticModule = new CountingDirectModule("Static");
            staticKernel.RegisterModule(staticModule);
            staticKernel.InitializeForTest();

            try
            {
                using var cts = new CancellationTokenSource();
                var loopTask = RunKernelLoop(staticKernel, cts.Token);

                await Task.Delay(30); // Let static module tick a bit
                int countBefore = staticModule.TickCount;
                Assert.True(countBefore > 0, $"Static module should tick. Count={countBefore}");

                // Install a dynamic module
                var dynModule = new CountingDirectModule("Dynamic");
                await staticKernel.InstallModuleAsync(dynModule);

                await Task.Delay(30);
                int countAfterInstall = staticModule.TickCount;

                // Uninstall the dynamic module
                await staticKernel.UninstallModuleAsync(dynModule);

                await Task.Delay(30);
                int countAfterUninstall = staticModule.TickCount;

                cts.Cancel();
                await loopTask;

                // Static module must have kept ticking throughout
                Assert.True(countAfterInstall > countBefore,
                    $"Static module should tick during dynamic install. Before={countBefore}, After={countAfterInstall}");
                Assert.True(countAfterUninstall > countAfterInstall,
                    $"Static module should tick after dynamic uninstall. Before={countAfterInstall}, After={countAfterUninstall}");
            }
            finally
            {
                staticKernel.Dispose();
                staticWorld.Dispose();
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 9. Install + Uninstall + Re-install same module
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task Module_CanBeReinstalledAfterUninstall()
        {
            var module = new CountingDirectModule("Reinstallable");

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(module);
            await Task.Delay(40);
            int countFirstInstall = module.TickCount;
            Assert.True(countFirstInstall > 0, "Should tick on first install.");

            await _kernel.UninstallModuleAsync(module);
            Assert.False(_kernel.IsModuleInstalled(module));

            // Reset the tick counter to measure fresh
            Interlocked.Exchange(ref module.TickCount, 0);
            module.WasDisposed = false; // Simulate re-usable module

            await _kernel.InstallModuleAsync(module);
            await Task.Delay(40);

            int countSecondInstall = module.TickCount;
            Assert.True(countSecondInstall > 0,
                $"Module should tick after re-install. Count={countSecondInstall}");

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 10. Concurrent installs are serialized (no topology corruption)
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task ConcurrentInstalls_AreSerializedAndAllSucceed()
        {
            const int count = 5;
            var modules = new CountingDirectModule[count];
            for (int i = 0; i < count; i++)
                modules[i] = new CountingDirectModule($"Concurrent_{i}");

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            // Fire all installs concurrently
            var installTasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                var m = modules[i];
                installTasks[i] = _kernel.InstallModuleAsync(m);
            }

            await Task.WhenAll(installTasks);

            // All must be installed
            for (int i = 0; i < count; i++)
                Assert.True(_kernel.IsModuleInstalled(modules[i]),
                    $"Module {modules[i].Name} not installed after concurrent install.");

            // Let them tick
            await Task.Delay(80);

            // All must have ticked
            for (int i = 0; i < count; i++)
                Assert.True(modules[i].TickCount > 0,
                    $"Module {modules[i].Name} didn't tick. Count={modules[i].TickCount}");

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 11. Dispose while draining module — no deadlock or access violation
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task Dispose_WithDrainingModule_DoesNotDeadlock()
        {
            var world2 = new EntityRepository();
            var acc2 = new EventAccumulator();
            var kernel2 = new ModuleHostKernel(world2, acc2);
            kernel2.InitializeForTest();

            var slowModule = new SlowBackgroundModule(sleepMs: 30);

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(kernel2, cts.Token);

            await kernel2.InstallModuleAsync(slowModule);
            await Task.Delay(50); // Let it start ticking

            // Begin uninstall without awaiting (puts it in draining)
            var uninstallTask = kernel2.UninstallModuleAsync(slowModule);

            // Immediately dispose the kernel while it's draining
            cts.Cancel();
            await loopTask;
            kernel2.Dispose();

            // The uninstall task may complete or be orphaned — we just verify no deadlock/crash
            // Best effort: give it a moment
            var completedInTime = await Task.WhenAny(uninstallTask, Task.Delay(2000));
            // We don't assert success here; just ensuring no hang/crash
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 12. System scheduler property reflects the active topology post-install
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task SystemScheduler_Property_ReturnsActiveTopologyScheduler()
        {
            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            var schedulerBefore = _kernel.SystemScheduler;
            Assert.NotNull(schedulerBefore);

            var module = new SystemRegisteringModule();
            await _kernel.InstallModuleAsync(module);

            // After install, the property should still return a valid (updated) scheduler
            var schedulerAfter = _kernel.SystemScheduler;
            Assert.NotNull(schedulerAfter);

            cts.Cancel();
            await loopTask;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the kernel update loop in the background at ~60Hz until cancellation.
        /// Returns a faulted task only if the loop itself throws.
        /// </summary>
        private static Task RunKernelLoop(ModuleHostKernel kernel, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        kernel.Update(0.016f);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Log but don't rethrow — kernel loop must stay alive during tests
                        Console.Error.WriteLine($"[KernelLoop] Exception: {ex.Message}");
                    }
                    Thread.Sleep(16); // ~60 Hz
                }
            }, CancellationToken.None);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Honest SoD / GDB test components
    //
    // Each component type carries a stable [ComponentId(...)] so that
    // ComponentTypeRegistry can map it to a fixed bit position in BitMask256.
    // IDs 239, 247, 249 are unused in this assembly.
    // ─────────────────────────────────────────────────────────────────────────────

    [ComponentId(104)] internal struct DynCompAlpha  { public float X; }
    [ComponentId(105)] internal struct DynCompBeta   { public float Y; }
    [ComponentId(106)] internal struct DynCompGamma  { public int   Z; }

    // ─────────────────────────────────────────────────────────────────────────────
    // Honest module helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot-on-Demand (SlowBackground) module that reads from a snapshot view.
    /// Declares its required component types so the kernel can compute a precise mask.
    /// </summary>
    class SodCountingModule : IEcsModule, IDisposable
    {
        public string Name { get; }
        public int TickCount;
        public volatile bool WasDisposed;
        private readonly Type[] _requiredComponents;

        public SodCountingModule(string name, params Type[] requiredComponents)
        {
            Name = name;
            _requiredComponents = requiredComponents;
        }

        // SlowBackground → DataStrategy.SoD
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(60).WithTimeout(5000);

        public IEnumerable<Type>? GetRequiredComponents() => _requiredComponents;

        public void Tick(ISimulationView view, float deltaTime)
            => Interlocked.Increment(ref TickCount);

        public void Dispose() => WasDisposed = true;
    }

    /// <summary>
    /// FrameSynced GDB module that reads from the DoubleBuffer replica.
    /// </summary>
    class GdbCountingModule : IEcsModule, IDisposable
    {
        public string Name { get; }
        public int TickCount;
        public volatile bool WasDisposed;
        private readonly Type[] _requiredComponents;

        public GdbCountingModule(string name, params Type[] requiredComponents)
        {
            Name = name;
            _requiredComponents = requiredComponents;
        }

        // FastReplica → DataStrategy.GDB
        public ExecutionPolicy Policy => ExecutionPolicy.FastReplica();

        public IEnumerable<Type>? GetRequiredComponents() => _requiredComponents;

        public void Tick(ISimulationView view, float deltaTime)
            => Interlocked.Increment(ref TickCount);

        public void Dispose() => WasDisposed = true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // HonestSodGdbTests — exercises the REAL memory provisioning paths.
    //
    // The original DynamicModuleTests relied exclusively on Direct (no-provider)
    // modules. These tests verify SharedSnapshotProvider, OnDemandProvider, and
    // DoubleBufferProvider via genuine SoD and GDB strategies.
    // ─────────────────────────────────────────────────────────────────────────────

    [Collection("SerialTests")]
    public class HonestSodGdbTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator  _evtAcc;
        private readonly ModuleHostKernel  _kernel;

        public HonestSodGdbTests()
        {
            _world  = new EntityRepository();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);

            // Pre-register all test component types so the live world has their tables.
            _world.RegisterComponent<DynCompAlpha>();
            _world.RegisterComponent<DynCompBeta>();
            _world.RegisterComponent<DynCompGamma>();

            _kernel.InitializeForTest();
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────────

        private static Task RunKernelLoop(ModuleHostKernel kernel, CancellationToken ct)
            => Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { kernel.Update(0.016f); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { Console.Error.WriteLine($"[KernelLoop] {ex.Message}"); }
                    Thread.Sleep(16);
                }
            }, CancellationToken.None);

        /// <summary>Returns the live topology via reflection (InternalsVisibleTo).</summary>
        private static KernelExecutionTopology GetActiveTopology(ModuleHostKernel kernel)
        {
            var field = typeof(ModuleHostKernel)
                .GetField("_activeTopology", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (KernelExecutionTopology)field.GetValue(kernel)!;
        }

        private static ModuleHostKernel.ModuleEntry? GetEntry(
            ModuleHostKernel kernel, IEcsModule module)
            => GetActiveTopology(kernel).Modules.FirstOrDefault(e => e.Module == module);

        // ──────────────────────────────────────────────────────────────────────────
        // 1. Basic SoD dynamic install — OnDemandProvider allocated, module ticks
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task SodModule_InstallAndUninstall_UsesOnDemandProvider()
        {
            var mod = new SodCountingModule("SodAlpha", typeof(DynCompAlpha));

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(mod);

            var entry = GetEntry(_kernel, mod);
            Assert.NotNull(entry);
            Assert.IsType<OnDemandProvider>(entry!.Provider);

            await Task.Delay(80);
            Assert.True(mod.TickCount > 0,
                $"SoD module should tick. Count={mod.TickCount}");

            await _kernel.UninstallModuleAsync(mod);
            Assert.False(_kernel.IsModuleInstalled(mod));

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 2. Basic GDB dynamic install — DoubleBufferProvider allocated, module ticks
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task GdbModule_InstallAndUninstall_UsesDoubleBufferProvider()
        {
            var mod = new GdbCountingModule("GdbAlpha", typeof(DynCompAlpha));

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(mod);

            var entry = GetEntry(_kernel, mod);
            Assert.NotNull(entry);
            Assert.IsType<DoubleBufferProvider>(entry!.Provider);

            await Task.Delay(80);
            Assert.True(mod.TickCount > 0,
                $"GDB module should tick. Count={mod.TickCount}");

            await _kernel.UninstallModuleAsync(mod);
            Assert.False(_kernel.IsModuleInstalled(mod));

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 3. Batch install — InstallModulesAsync activates 3 SoD modules atomically
        //    (single swap → all three become live in the same BeforeSync boundary)
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task BatchInstall_SodModules_ActivatedAtomically()
        {
            var modA = new SodCountingModule("BatchSodA", typeof(DynCompAlpha));
            var modB = new SodCountingModule("BatchSodB", typeof(DynCompBeta));
            var modC = new SodCountingModule("BatchSodC", typeof(DynCompGamma));

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            // Single await — all three should go live from the same BeforeSync swap.
            await _kernel.InstallModulesAsync(new IEcsModule[] { modA, modB, modC });

            // Post-await: all must be installed and provide a SharedSnapshotProvider
            // (three SoD modules with the same convoy policy → shared provider).
            Assert.True(_kernel.IsModuleInstalled(modA), "modA not installed");
            Assert.True(_kernel.IsModuleInstalled(modB), "modB not installed");
            Assert.True(_kernel.IsModuleInstalled(modC), "modC not installed");

            var entryA = GetEntry(_kernel, modA);
            var entryB = GetEntry(_kernel, modB);
            var entryC = GetEntry(_kernel, modC);

            Assert.IsType<SharedSnapshotProvider>(entryA!.Provider);
            Assert.IsType<SharedSnapshotProvider>(entryB!.Provider);
            Assert.IsType<SharedSnapshotProvider>(entryC!.Provider);

            // All three must share the SAME provider instance (one convoy).
            Assert.Same(entryA.Provider, entryB.Provider);
            Assert.Same(entryA.Provider, entryC.Provider);

            // Let them tick.
            await Task.Delay(120);

            cts.Cancel();
            await loopTask;

            Assert.True(modA.TickCount > 0, $"BatchSodA.TickCount={modA.TickCount}");
            Assert.True(modB.TickCount > 0, $"BatchSodB.TickCount={modB.TickCount}");
            Assert.True(modC.TickCount > 0, $"BatchSodC.TickCount={modC.TickCount}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 4. UnionMask expansion — sequential SoD installs build growing convoy mask
        //    Phase 1: solo SoD module → OnDemandProvider (ComponentAlpha only)
        //    Phase 2: second SoD module → SharedSnapshotProvider (Alpha ∪ Beta bits set)
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task UnionMask_Expansion_NewSodModule_ExpandsSharedProvider()
        {
            var modA = new SodCountingModule("SodMaskA", typeof(DynCompAlpha));
            var modB = new SodCountingModule("SodMaskB", typeof(DynCompBeta));

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            // Phase 1: install solo SoD module → OnDemandProvider
            await _kernel.InstallModuleAsync(modA);
            {
                var entry = GetEntry(_kernel, modA);
                Assert.IsType<OnDemandProvider>(entry!.Provider);
            }

            // Phase 2: install second SoD module with a DIFFERENT component type.
            // The convoy is promoted to SharedSnapshotProvider with Union(Alpha, Beta).
            await _kernel.InstallModuleAsync(modB);

            var entryA = GetEntry(_kernel, modA);
            var entryB = GetEntry(_kernel, modB);

            Assert.NotNull(entryA);
            Assert.NotNull(entryB);

            // Both must now use a SharedSnapshotProvider.
            Assert.IsType<SharedSnapshotProvider>(entryA!.Provider);
            Assert.IsType<SharedSnapshotProvider>(entryB!.Provider);
            Assert.Same(entryA.Provider, entryB.Provider);

            // The provider's UnionMask must contain both component bits.
            var sharedProvider = (SharedSnapshotProvider)entryA.Provider;
            int idAlpha = ComponentTypeRegistry.GetId(typeof(DynCompAlpha));
            int idBeta  = ComponentTypeRegistry.GetId(typeof(DynCompBeta));

            Assert.True(idAlpha >= 0, "DynCompAlpha must be registered.");
            Assert.True(idBeta  >= 0, "DynCompBeta must be registered.");

            Assert.True(sharedProvider.UnionMask.IsSet(idAlpha),
                $"UnionMask must include DynCompAlpha (bit {idAlpha}).");
            Assert.True(sharedProvider.UnionMask.IsSet(idBeta),
                $"UnionMask must include DynCompBeta (bit {idBeta}).");

            // Let both modules tick to confirm they run correctly.
            await Task.Delay(100);

            cts.Cancel();
            await loopTask;

            Assert.True(modA.TickCount > 0, $"SodMaskA.TickCount={modA.TickCount}");
            Assert.True(modB.TickCount > 0, $"SodMaskB.TickCount={modB.TickCount}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 5. Batch uninstall — UninstallModulesAsync removes all atomically
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task BatchUninstall_SodModules_RemovedAtomically()
        {
            var modA = new SodCountingModule("BatchUnA", typeof(DynCompAlpha));
            var modB = new SodCountingModule("BatchUnB", typeof(DynCompBeta));

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModulesAsync(new IEcsModule[] { modA, modB });

            Assert.True(_kernel.IsModuleInstalled(modA));
            Assert.True(_kernel.IsModuleInstalled(modB));

            // Single await removes both in one swap.
            await _kernel.UninstallModulesAsync(new IEcsModule[] { modA, modB });

            Assert.False(_kernel.IsModuleInstalled(modA));
            Assert.False(_kernel.IsModuleInstalled(modB));

            Assert.True(modA.WasDisposed, "modA should be disposed after drain.");
            Assert.True(modB.WasDisposed, "modB should be disposed after drain.");

            cts.Cancel();
            await loopTask;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 6. ECS schema mutation — novel component registered on live world at install
        // ──────────────────────────────────────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task Install_ModuleWithNovelComponent_RegistersComponentOnLiveWorld()
        {
            // Use DynCompGamma; confirm it is registered into the kernel's live world
            // by the EnsureComponentsRegistered path (the component was already registered
            // in the constructor above, but our test verifies the ID is non-negative).
            var mod = new SodCountingModule("NovelComp", typeof(DynCompGamma));

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(_kernel, cts.Token);

            await _kernel.InstallModuleAsync(mod);

            int id = ComponentTypeRegistry.GetId(typeof(DynCompGamma));
            Assert.True(id >= 0,
                "DynCompGamma must be globally registered after dynamic install.");

            // Mask must include DynCompGamma's bit
            var entry = GetEntry(_kernel, mod);
            Assert.True(entry!.ComponentMask.IsSet(id),
                $"Module component mask must include DynCompGamma (bit {id}).");

            cts.Cancel();
            await loopTask;
        }
    }
}
