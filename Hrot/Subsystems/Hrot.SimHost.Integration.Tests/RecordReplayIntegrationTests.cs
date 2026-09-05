using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Map.Common;
using Hrot.SimHost.Modules.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replay;
using Fdp.ModuleHost;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// Integration tests for the recording / replay module lifecycle (MOD1-P8T5).
    /// These tests ensure that <see cref="EcsRecordReplayController"/> correctly
    /// orchestrates <see cref="RecordingModule"/>, <see cref="EpisodeRecorderModule"/>,
    /// and <see cref="ReplayModule"/> inside a real <see cref="ModuleHostKernel"/>.
    /// </summary>
    public class RecordReplayIntegrationTests : IDisposable
    {
        // Domain 18 is reserved for RecordReplay integration tests (CGF1-S0104 / A.4).
        private const int TestDomain = 18;

        private readonly EntityRepository  _world;
        private readonly EventAccumulator  _evtAcc;
        private readonly ModuleHostKernel  _kernel;
        private readonly string            _tempDir;
        private readonly DdsParticipant    _ddsParticipant;

        public RecordReplayIntegrationTests()
        {
            _world  = new EntityRepository();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();
            _ddsParticipant = HrotEnvironment.CreateParticipant(TestDomain);

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"RecordReplayIntegrationTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            _ddsParticipant.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── MOD1-P8T5 success condition 2 ────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task NodeBootstrapper_BrainRole_RegistersLiveLoadClusterStateHandler()
        {
            // Arrange — Brain role requires a real DDS participant (CGF-1-BATCH-03 A.4).
            // Provide an event bus so LiveLoadClusterStateHandler is registered; it wraps the
            // EcsRecordReplayController for the Brain-role recording/replay lifecycle
            // (CGF1-S0304 / BATCH-20 A.3: EcsRecordReplayController.CanHandle is always
            // false — factory-only; direct registration as IClusterOpHandler is intentionally absent).
            var eventBus     = new FdpEventBus();
            var bootstrapper = new NodeBootstrapper();
            using var clusterSlave = bootstrapper.BuildOrchestration(
                NodeRole.Brain, _kernel, _world, nodeId: 1, participant: _ddsParticipant,
                eventBus: eventBus);

            // Assert: ReferenceLiveLoadHandler is registered.
            Assert.True(clusterSlave.IsHandlerRegistered<ReferenceLiveLoadHandler>(),
                "Brain role must register a ReferenceLiveLoadHandler (owns record/replay lifecycle).");
        }

        [Fact(Timeout = 10_000)]
        public void NodeBootstrapper_ImageGeneratorRole_NoControllerRegistered()
        {
            // ImageGenerator role does not participate in orchestration — null participant allowed.
            var bootstrapper = new NodeBootstrapper();
            var clusterSlave   = bootstrapper.BuildOrchestration(
                NodeRole.ImageGenerator, _kernel, _world, nodeId: 2);

            Assert.False(clusterSlave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
                "ImageGenerator role must NOT register a ReferenceReplayLoadHandler.");
        }

        // ── MOD1-P8T5 success condition 3 ────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task RecordingLifecycle_InstallUninstall_ModuleInstalledThenGone()
        {
            // Arrange
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: ControllerNodeId, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            // Act: install recording
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            var module = controller.ActiveRecordingModule;

            Assert.NotNull(module);
            Assert.True(_kernel.IsModuleInstalled(module!),
                "RecordingModule must be installed after PrepareRecordingAsync.");

            // Allow a few frames to tick so the RecorderTickSystem runs.
            await Task.Delay(100);

            // Act: finalize recording
            await controller.FinalizeRecordingAsync();

            Assert.Null(controller.ActiveRecordingModule);
            Assert.False(_kernel.IsModuleInstalled(module!),
                "RecordingModule must be uninstalled after FinalizeRecordingAsync.");

            // The .fdp file must exist (proves the recorder actually ran).
            // ⛔ NEVER hand-compose this path — see the note on ExerciseRecordingPath below.
            var expectedFile = ExerciseRecordingPath(exerciseId);
            Assert.True(File.Exists(expectedFile),
                $"Expected recording file at {expectedFile}.");

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T5 success condition 4 ────────────────────────────────────────

        [Fact(Timeout = 20_000)]
        public async Task EpisodeRecording_WithConcurrentGlobalRecorder_BothFilesProduced()
        {
            // Arrange: global recording + episode recording start concurrently.
            var exerciseId    = Guid.NewGuid();
            var episodeId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: ControllerNodeId, _world);

            Directory.CreateDirectory(
                Fdp.Toolkit.Orchestration.OrchestrationConstants.GetEpisodesRoot(_tempDir));

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            await controller.StartEpisodeRecordingAsync(episodeId, _tempDir);

            // Allow a few frames so RecorderTickSystems run and create non-empty files.
            await Task.Delay(150);

            await controller.StopEpisodeRecordingAsync(episodeId);
            await controller.FinalizeRecordingAsync();

            cts.Cancel();
            await loopTask;

            // Both .fdp files must exist. ⛔ Both paths come from the SAME helpers production writes
            //   through — see the note on ExerciseRecordingPath below.
            var globalFile = ExerciseRecordingPath(exerciseId);
            var episodeFile  = Fdp.Toolkit.Orchestration.OrchestrationConstants
                .GetEpisodeRecordingFilePath(_tempDir, episodeId, ControllerNodeId);

            Assert.True(File.Exists(globalFile),
                $"Global recording file not found at {globalFile}.");
            Assert.True(File.Exists(episodeFile),
                $"Episode recording file not found at {episodeFile}.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>The node id every <see cref="EcsRecordReplayController"/> in this class is built with.</summary>
        private const int ControllerNodeId = 1;

        /// <summary>
        /// 🔴 <b>The exercise recording's path, resolved the way PRODUCTION resolves it.</b>
        ///
        /// <para>📐 <b>Measured <c>2026-09-01</c>:</b> both recording rails hand-composed
        /// <c>{tempDir}/{exerciseId}/node_1.fdp</c> and asserted the file existed there. The recorder
        /// writes to <c>{tempDir}/<b>exercises</b>/{exerciseId}/node_1.fdp</c>
        /// (<c>EcsRecordReplayController.GetRecordingFilePath</c> →
        /// <c>OrchestrationConstants.ExercisesDirectoryName</c>). ⇒ the rails failed as
        /// <i>"recording file not found"</i> — which reads as <b>a recorder that never ran</b> and is
        /// nothing of the sort. The recorder was writing correctly the whole time.</para>
        ///
        /// <para>⭐⭐ <b>Asserting on a path means calling the function that BUILT it.</b> The exercise
        /// side now has <c>OrchestrationConstants.GetExerciseRecordingFilePath</c>, mirroring the episode
        /// side's long-standing <c>GetEpisodeRecordingFilePath</c>; production and this rail call the same
        /// one, so they cannot disagree about the layout again.</para>
        /// </summary>
        private string ExerciseRecordingPath(Guid exerciseId) =>
            Fdp.Toolkit.Orchestration.OrchestrationConstants
                .GetExerciseRecordingFilePath(_tempDir, exerciseId, ControllerNodeId);

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
