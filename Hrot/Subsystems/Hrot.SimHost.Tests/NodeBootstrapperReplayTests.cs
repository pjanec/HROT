using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.SimHost.Modules.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core;
using ModuleHost.Core.Scheduling;
using Hrot.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Focused bootstrap tests confirming that <see cref="NodeBootstrapper.BuildOrchestration"/>
    /// registers <see cref="ReferenceReplayLoadHandler"/> when the replay-control objects are supplied —
    /// matching the production <see cref="SimHostApp.OnLoad"/> wiring (CGF1-S0304 Part A.1).
    /// Also verifies correct <c>PrepareLive</c> (operationId=9) dispatch routing
    /// (CGF1-S0305 / BATCH-18 A.1).
    /// </summary>
    public sealed class NodeBootstrapperReplayTests : System.IDisposable
    {
        // Domain 16 is reserved for NodeBootstrapper replay-registration tests.
        private const int TestDomain = 16;

        private readonly EntityRepository    _world;
        private readonly EventAccumulator    _evtAcc;
        private readonly ModuleHostKernel    _kernel;
        private readonly DdsParticipant      _participant;
        private readonly string              _tempDir;

        public NodeBootstrapperReplayTests()
        {
            _world       = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _evtAcc      = new EventAccumulator();
            _kernel      = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();
            _participant = new DdsParticipant(TestDomain);

            _tempDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"NodeBootstrapperReplayTests_{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _participant.Dispose();
            _kernel.Dispose();
            _world.Dispose();
            try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── A.1: Production replay registration ──────────────────────────────

        /// <summary>
        /// Verifies that <see cref="NodeBootstrapper.BuildOrchestration"/> registers
        /// <see cref="ReplayLoadClusterOpHandler"/> when <paramref name="simGroup"/>,
        /// <paramref name="lifecycleGroup"/>, and <paramref name="ghostCreationSystem"/>
        /// are all non-null — the condition required by the bootstrapper.
        ///
        /// <para>This is the focused test demanded by the BATCH-16 review: the gap was
        /// that <see cref="SimHostApp.OnLoad"/> called <c>BuildOrchestration</c> before
        /// the three objects existed, so they were null and the handler was never registered.
        /// The production fix (two-phase bootstrap) constructs these objects first and passes
        /// them here, which this test validates without a full DDS stack.</para>
        /// </summary>
        [Fact]
        public void BuildOrchestration_WithReplayParams_RegistersReplayLoadClusterOpHandler()
        {
            var entityMap     = new NetworkEntityMap();
            var ghostSys      = new GhostCreationSystem(entityMap);
            var simGroup      = new SimulationSystemGroup();
            var lifecycleGrp  = new NetworkLifecycleSystemGroup(ghostSys);

            var bootstrapper = new NodeBootstrapper();
            using var slave = bootstrapper.BuildOrchestration(
                NodeRole.Brain | NodeRole.MuscleGround | NodeRole.Perception,
                _kernel,
                _world,
                nodeId:             1,
                participant:        _participant,
                simGroup:           simGroup,
                lifecycleGroup:     lifecycleGrp,
                ghostCreationSystem: ghostSys);

            Assert.True(
                slave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
                "ReferenceReplayLoadHandler must be registered by BuildOrchestration when replay params are provided.");
        }

        /// <summary>
        /// When replay params (<paramref name="simGroup"/>) are absent,
        /// <see cref="ReplayLoadClusterOpHandler"/> must <em>not</em> be registered.
        /// Uses <see cref="NodeRole.ImageGenerator"/> which does not require DDS and does
        /// not create an <see cref="EcsRecordReplayController"/> — the guard condition
        /// <c>controller != null</c> in <see cref="NodeBootstrapper.BuildOrchestration"/>
        /// ensures no partial-wired handler is constructed.
        /// </summary>
        [Fact]
        public void BuildOrchestration_WithoutReplayParams_DoesNotRegisterReplayHandler()
        {
            var bootstrapper = new NodeBootstrapper();
            var slave = bootstrapper.BuildOrchestration(
                NodeRole.ImageGenerator,
                _kernel,
                _world,
                nodeId: 1);

            Assert.False(
                slave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
                "ReferenceReplayLoadHandler must NOT be registered when replay params are absent.");
        }

        // ── BATCH-18 A.1: Dispatch routing — PrepareLive reaches ReplayLoadClusterOpHandler when replay active ──

        /// <summary>
        /// Verifies the BATCH-18 A.1 fix: when a replay session is active,
        /// <see cref="ClusterSlave"/> dispatch routes a <c>PrepareLive</c> (operationId=9) command
        /// to <see cref="ReferenceReplayLoadHandler"/> (the Live-from-Replay branch) rather than
        /// to <see cref="ReferenceLiveLoadHandler"/>.
        ///
        /// <para>
        /// Uses real <see cref="ClusterSlave"/> dispatch (<see cref="ClusterSlave.EnqueueCommandForTest"/>
        /// + <see cref="ClusterSlave.Tick"/>) with both handlers registered in the fixed order
        /// (replay-first, matching post-BATCH-18 <see cref="NodeBootstrapper.BuildOrchestration"/>).
        /// The observable side-effect is that <see cref="EcsRecordReplayController.ActiveReplayModule"/>
        /// becomes <c>null</c> (teardown completed) and
        /// <see cref="EcsRecordReplayController.ActiveRecordingModule"/> is set (recording started),
        /// which only happens on the Live-from-Replay path in <see cref="ReferenceReplayLoadHandler"/>.
        /// </para>
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task ClusterSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch()
        {
            // ── Step 1: create entities, record an exercise, open replay ──────────
            for (int i = 0; i < 3; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform
                {
                    Position = new Vector3(i, 0f, 0f),
                });
            }

            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 5_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(25); }
            await controller.FinalizeRecordingAsync();

            await controller.PrepareReplayAsync(exerciseId, _tempDir);
            Assert.NotNull(controller.ActiveReplayModule); // pre-condition

            // ── Step 2: build a ClusterSlave with replay handler first (BATCH-18 fix) ──
            var eventBus       = new FdpEventBus();
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var simGroup       = new SimulationSystemGroup();
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            using var slave = new ClusterSlave(eventBus);
            // Registration order matches the fixed NodeBootstrapper.BuildOrchestration:
            // ReferenceReplayLoadHandler first (CanHandle(PrepareLive) is true while replay is active),
            // ReferenceLiveLoadHandler second (cold PrepareLive fallback).
            slave.RegisterHandler(new ReferenceReplayLoadHandler(
                controller, simGroup, lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir));
            slave.RegisterHandler(new ReferenceLiveLoadHandler(
                checkpointWorker: null, controller, _tempDir));

            // ── Step 3: dispatch PrepareLive via ClusterSlave ───────────────────
            var branchedExerciseId = Guid.NewGuid();
            slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = FDP.Toolkit.Orchestration.NodeOpType.PrepareLive,
                DomainPayload = branchedExerciseId,
            });

            // Drive slave ticks until async prepare completes (kernel loop running in bg).
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                slave.Tick();
                if (controller.ActiveReplayModule == null && controller.ActiveRecordingModule != null)
                    break;
                await Task.Delay(50);
            }

            cts.Cancel();
            await loopTask;

            // ── Step 4: assert ReplayLoadClusterOpHandler branch ran ────────────────
            Assert.True(controller.ActiveReplayModule == null,
                "Replay module must be torn down: ReplayLoadClusterOpHandler.PrepareLive must have run.");
            Assert.True(controller.ActiveRecordingModule != null,
                "Recording module must be installed after the Live-from-Replay branch completes.");
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
