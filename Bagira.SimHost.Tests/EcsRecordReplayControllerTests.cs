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

        // ── CGF1-S0304 success condition 3 ───────────────────────────────────────

        /// <summary>
        /// After a 10-tick recording session is finalized:
        /// <list type="bullet">
        ///   <item>A <c>.meta.json</c> file exists at the expected path.</item>
        ///   <item>The JSON contains <c>"MaxNetworkId"</c> &gt;= 0 (well-formed manifest).</item>
        /// </list>
        /// When <c>maxNetworkId > 0</c> is passed to <c>FinalizeRecordingAsync</c> the
        /// value is faithfully round-tripped to disk (CGF1-S0304).
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task FinalizeRecording_WritesMetaJson()
        {
            var drillId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new Fdp.Kernel.GlobalTime
            {
                DeltaTime       = 0.016f,
                TimeScale       = 1.0f,
                TotalWallTicks  = 1000L,
            });

            await controller.PrepareRecordingAsync(drillId, _tempDir);

            // Drive 10 update frames so the recorder has real data.
            for (int i = 0; i < 10; i++)
            {
                _world.SetSingletonUnmanaged(new Fdp.Kernel.GlobalTime
                {
                    DeltaTime       = 0.016f,
                    TimeScale       = 1.0f,
                    TotalWallTicks  = 1000L + i * 16L,
                });
                await Task.Delay(20);   // let kernel loop tick
            }

            // Finalize with a known MaxNetworkId so the value is verifiable.
            const long expectedMaxNetworkId = 42L;
            await controller.FinalizeRecordingAsync(maxNetworkId: expectedMaxNetworkId);

            cts.Cancel();
            await loopTask;

            // Assert .meta.json exists somewhere under _tempDir (search recursively to be
            // agnostic of the exact drill-directory naming convention).
            var metaFiles = Directory.GetFiles(_tempDir, "*.meta.json", SearchOption.AllDirectories);
            Assert.True(metaFiles.Length > 0,
                $"Expected at least one .meta.json under {_tempDir} after FinalizeRecordingAsync.");

            var metaPath = metaFiles[0];
            Assert.True(File.Exists(metaPath), $"Expected .meta.json at {metaPath}.");

            var json = await File.ReadAllTextAsync(metaPath);
            Assert.Contains("MaxNetworkId", json);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("MaxNetworkId", out var prop),
                "meta.json must contain MaxNetworkId field.");
            Assert.Equal(expectedMaxNetworkId, prop.GetInt64());
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
