using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.SimHost.Modules.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Scheduling;
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
    [Xunit.Collection(RecordReplayKernelLoopCollection.Name)]
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
            var simGroup      = new TogglableSimulationGroup("test");
            var inputGroup    = new TogglableInputGroup("test-input");
            var postSimGroup  = new TogglablePostSimulationGroup("test-postsim");
            var lifecycleGrp  = new NetworkLifecycleSystemGroup(ghostSys);

            var bootstrapper = new NodeBootstrapper();
            // ⚠ CHANGED `2026-09-18`: the role set no longer includes Brain. ⭐ A Brain host REQUIRES the
            //   scenario part of the load phase, and this call composes no authoring dependencies — the
            //   chain now says so at composition instead of the node silently loading nothing.
            //   📐 Production SimHost is MuscleGround | Perception | NavigationSolver and never Brain
            //   (SimHostApp.cs:183, :255), so nothing production-shaped is weakened by dropping it here;
            //   the case this test is actually about is the REPLAY handler.
            //   📄 docs/DESIGN_Cluster_Load_Phase.md §4.1b.
            using var slave = bootstrapper.BuildOrchestration(
                NodeRole.MuscleGround | NodeRole.Perception,
                _kernel,
                _world,
                nodeId:             1,
                participant:        _participant,
                simGroup:           simGroup,
                inputGroup:         inputGroup,
                postSimGroup:       postSimGroup,
                lifecycleGroup:     lifecycleGrp,
                ghostCreationSystem: ghostSys);

            Assert.True(
                slave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
                "ReferenceReplayLoadHandler must be registered by BuildOrchestration when replay params are provided.");
        }

        /// <summary>
        /// ⭐ <c>D16</c> (<c>R-3046</c>) — every ECS host's slave holds the SAME cluster handlers, so the replay handler is
        /// registered even with no replay groups. ⛔ SUPERSEDED: <i>"must NOT be registered when replay params are
        /// absent"</i> — a conditional that only a test ever exercised (every production host passes its groups), and that
        /// R-4000 forbids: a behaviour that must differ is decided by role, never by a missing handler.
        /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1b U3.
        /// </summary>
        [Fact]
        public void BuildOrchestration_WithoutReplayParams_StillRegistersTheReplayHandler()
        {
            var bootstrapper = new NodeBootstrapper();
            var slave = bootstrapper.BuildOrchestration(
                NodeRole.Map2D,
                _kernel,
                _world,
                nodeId: 1);

            Assert.True(
                slave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
                "D16: the replay handler is part of the one set every ECS host composes.");
        }

        // ── CE-1446 B0: the orchestration inputs CGF and the editor need ──────────────

        private sealed class MarkerEpisodeHandler : IClusterStateHandler
        {
            public bool CanHandle(Fdp.Toolkit.Orchestration.NodeOpType operation) => false;
            public Task<object?> PrepareAsync(Fdp.Toolkit.Orchestration.ExecuteNodeOpIntent intent, CancellationToken ct) => Task.FromResult<object?>(null);
            public void Commit(Fdp.Toolkit.Orchestration.ExecuteNodeOpIntent intent, EntityRepository? repo) { }
            public void Abort(Fdp.Toolkit.Orchestration.ExecuteNodeOpIntent intent, EntityRepository? repo) { }
        }

        private ClusterSlave BuildBrain(NodeBootstrapper b, bool offline, DdsParticipant? participant,
            System.Collections.Generic.IReadOnlyList<IClusterStateHandler>? scenarioHandlers = null, Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper? remapper = null)
            => b.BuildOrchestration(
                NodeRole.Brain, _kernel, _world, nodeId: 1, participant: participant,
                scenarioSerializer: Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(new Fdp.Toolkit.Behavior.BehaviorRegistry()),
                scenarioExtractor: new Hrot.CGF.Orchestration.StagingEntityExtractor(),
                scenarioSource: new FdpEventBus(),
                scenarioIdAllocator: new Hrot.Core.Network.SequentialIdAllocator(),
                localTempRoot: _tempDir,
                scenarioHandlers: scenarioHandlers, behaviorRemapper: remapper, offline: offline);

        /// <summary>⭐ CE-1446 B0 — an OFFLINE Brain (the editor) builds with no participant; a networked one still refuses (the guard keeps its meaning).
        /// 🔴 Red-proof: `!offline &&` removed from the guard ⇒ the first half throws.</summary>
        [Fact]
        public void CE1446_AnOfflineBrain_BuildsWithNoParticipant_ANetworkedOneStillRefuses()
        {
            using var slave = BuildBrain(new NodeBootstrapper(), offline: true, participant: null);
            Assert.True(slave.IsHandlerRegistered<ReferenceReplayLoadHandler>());

            Assert.Throws<ArgumentNullException>(() => BuildBrain(new NodeBootstrapper(), offline: false, participant: null));
        }

        /// <summary>⭐ CE-1446 B0 — the host's own scenario handlers replace the default episode handler (CGF's `CgfEpisodeLoadHandler`); with none, the default stays.
        /// 🔴 Red-proof: `scenarioHandlers` not threaded into the composition ⇒ the marker is absent and the default is present.</summary>
        [Fact]
        public void CE1446_HostScenarioHandlers_ReachTheSlave_InPlaceOfTheDefaultEpisodeHandler()
        {
            var marker = new MarkerEpisodeHandler();
            using var custom = BuildBrain(new NodeBootstrapper(), offline: true, participant: null, scenarioHandlers: new IClusterStateHandler[] { marker });
            Assert.Contains(marker, custom.RegisteredHandlers);
            Assert.False(custom.IsHandlerRegistered<ReferenceEpisodeLoadHandler>());

            using var def = BuildBrain(new NodeBootstrapper(), offline: true, participant: null);
            Assert.True(def.IsHandlerRegistered<ReferenceEpisodeLoadHandler>());
        }

        /// <summary>⭐ CE-1446 B0 — the behaviour remapper reaches the composed scenario load step (so a load that renumbers ids rewrites mission EntityRefs, as CGF and the editor need).
        /// 🔴 Red-proof: `behaviorRemapper: behaviorRemapper` dropped from the `ScenarioLoadStep` construction ⇒ the step holds null.
        /// ⚠ Reads the step's private field by reflection: the step is composed inside the chain and exposes nothing.</summary>
        [Fact]
        public void CE1446_TheBehaviorRemapper_ReachesTheScenarioLoadStep()
        {
            var remapper = Hrot.CGF.Configuration.CgfBehaviorSetup.CreateBehaviorRemapper();
            using var slave = BuildBrain(new NodeBootstrapper(), offline: true, participant: null, remapper: remapper);

            var chain = slave.RegisteredHandlers.OfType<Hrot.Map.Common.ClusterLoad.LoadPhaseChain>().Single();
            var steps = (System.Collections.IEnumerable)typeof(Hrot.Map.Common.ClusterLoad.LoadPhaseChain)
                .GetField("_steps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(chain)!;
            var step = steps.Cast<object>().Single(x => x is Hrot.Map.Common.ClusterLoad.ScenarioLoadStep);
            var held = step.GetType().GetField("_remapper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(step);

            Assert.Same(remapper, held);
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
            var simGroup       = new TogglableSimulationGroup("test");
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            using var slave = new ClusterSlave(eventBus);
            // Registration order matches the fixed NodeBootstrapper.BuildOrchestration:
            // ReferenceReplayLoadHandler first (CanHandle(PrepareLive) is true while replay is active),
            // ReferenceLiveLoadHandler second (cold PrepareLive fallback).
            slave.RegisterHandler(new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    null,
                simGroup:      simGroup,
                postSimGroup:  null,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir));
            slave.RegisterHandler(new ReferenceLiveLoadHandler(controller, _tempDir));

            // ── Step 3: dispatch PrepareLive via ClusterSlave ───────────────────
            var branchedExerciseId = Guid.NewGuid();
            slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive,
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
