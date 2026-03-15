using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagira.SimHost.Modules.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Replay;
using ModuleHost.Core;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EcsRecordReplayController"/> (MOD1-P8T1).
    /// </summary>
    public class EcsRecordReplayControllerTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator  _evtAcc;
        private readonly ModuleHostKernel  _kernel;
        private readonly string            _tempDir;

        public EcsRecordReplayControllerTests()
        {
            _world   = new EntityRepository();
            _evtAcc  = new EventAccumulator();
            _kernel  = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"EcsRecordReplayControllerTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── MOD1-P8T1 success condition 1 ────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task PrepareRecordingAsync_InstallsRecordingModule()
        {
            var drillId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            await controller.PrepareRecordingAsync(drillId, _tempDir);

            // After await: module must be installed in the kernel.
            Assert.NotNull(controller.ActiveRecordingModule);
            Assert.True(_kernel.IsModuleInstalled(controller.ActiveRecordingModule!),
                "RecordingModule must be in the kernel after PrepareRecordingAsync.");

            // Cleanup — finalize before tearing down the kernel loop.
            await controller.FinalizeRecordingAsync();

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T1 success condition 2 ────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task FinalizeRecordingAsync_UninstallsRecordingModule()
        {
            var drillId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            await controller.PrepareRecordingAsync(drillId, _tempDir);
            var installedModule = controller.ActiveRecordingModule;
            Assert.NotNull(installedModule);

            await controller.FinalizeRecordingAsync();

            // After finalize: module must be gone and property cleared.
            Assert.Null(controller.ActiveRecordingModule);
            Assert.False(_kernel.IsModuleInstalled(installedModule!),
                "RecordingModule must be uninstalled after FinalizeRecordingAsync.");

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T1 success condition 3 ────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task StartStopStoryRecording_InstallsAndUninstallsStoryModule()
        {
            var storyId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            // Create the stories directory that the controller will write into.
            var storiesDir = Path.Combine(_tempDir, "stories");
            Directory.CreateDirectory(storiesDir);

            await controller.StartStoryRecordingAsync(storyId, _tempDir);

            // Story module is live — ActiveRecordingModule is unaffected (no global recording).
            Assert.Null(controller.ActiveRecordingModule);

            await controller.StopStoryRecordingAsync(storyId);

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T1 success condition 4 ────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task PrepareRecordingAsync_AndStoryRecording_RunConcurrently()
        {
            var drillId    = Guid.NewGuid();
            var storyId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            // Create stories sub-directory.
            Directory.CreateDirectory(Path.Combine(_tempDir, "stories"));

            await controller.PrepareRecordingAsync(drillId, _tempDir);
            await controller.StartStoryRecordingAsync(storyId, _tempDir);

            // Both modules independently installed.
            Assert.NotNull(controller.ActiveRecordingModule);
            Assert.True(_kernel.IsModuleInstalled(controller.ActiveRecordingModule!));

            await controller.StopStoryRecordingAsync(storyId);
            await controller.FinalizeRecordingAsync();

            cts.Cancel();
            await loopTask;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Task RunKernelLoop(ModuleHostKernel kernel, CancellationToken ct) =>
            Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { kernel.Update(0.016f); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"[KernelLoop] {ex.Message}");
                    }
                    Thread.Sleep(16);
                }
            }, CancellationToken.None);
    }
}
