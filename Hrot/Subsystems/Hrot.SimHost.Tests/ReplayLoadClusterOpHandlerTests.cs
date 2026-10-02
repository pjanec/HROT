using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="ReferenceReplayLoadHandler"/> (CGF1-S0304).
    /// </summary>
    public class ReplayLoadClusterOpHandlerTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator _evtAcc;
        private readonly ModuleHostKernel _kernel;
        private readonly string           _tempDir;

        public ReplayLoadClusterOpHandlerTests()
        {
            _world  = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"ReplayLoadClusterOpHandlerTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── CGF1-S0304 success condition 6 ───────────────────────────────────────

        /// <summary>
        /// Full replay transition:
        /// <list type="number">
        ///   <item>Creates a small recording (5 ticks) so a real <c>.fdp</c> file exists.</item>
        ///   <item>Calls <c>PrepareAsync(PrepareReplay)</c> on the handler, triggering
        ///   <see cref="EcsRecordReplayController.PrepareReplayAsync"/>.</item>
        ///   <item>Calls <c>Commit(PrepareReplay)</c> on the handler.</item>
        ///   <item>Asserts <see cref="TogglableSimulationGroup.Enabled"/> is <c>false</c>.</item>
        ///   <item>Asserts <see cref="GhostCreationSystem.BypassLifecycle"/> is <c>true</c>.</item>
        /// </list>
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task FullReplayTransition_DisablesSimGroups()
        {
            // ── Step 1: create a recording so PrepareReplayAsync can open the file. ──
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);

            // Drive a few ticks so the file has at least one frame.
            for (int i = 0; i < 5; i++)
            {
                _world.SetSingletonUnmanaged(new GlobalTime
                {
                    DeltaTime      = 0.016f,
                    TimeScale      = 1.0f,
                    TotalWallTicks = 10_000L + i * 16L,
                });
                await Task.Delay(20);
            }

            await controller.FinalizeRecordingAsync();

            // ── Step 2: build the handler. ──
            var inputGroup       = new TogglableInputGroup("test-input");
            var simGroup         = new TogglableSimulationGroup("test");
            var postSimGroup     = new TogglablePostSimulationGroup("test-postsim");
            var entityMap        = new NetworkEntityMap();
            var ghostSys         = new GhostCreationSystem(entityMap);
            var lifecycleGroup   = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            // ── Step 3: dispatch PrepareReplay → Commit. ──
            var payload = exerciseId;  // Guid directly as DomainPayload
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = payload,
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null);

            // ── Step 4: stop kernel loop. ──
            cts.Cancel();
            await loopTask;

            // ── Step 5: assertions. ──
            Assert.False(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be false during RunningReplay.");
            Assert.False(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be false during RunningReplay.");
            Assert.True(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be true during RunningReplay.");
            Assert.False(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be false during RunningReplay.");
            Assert.False(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be false during RunningReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task FinalizeReplay_ReEnablesSimGroups()
        {
            // ── Step 1: create a recording. ──
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 1_000L,
            });

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler and run PrepareReplay → Commit. ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            var prepareCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // Sim group is now disabled.
            Assert.False(simGroup.Enabled);
            Assert.False(inputGroup.Enabled);
            Assert.False(postSimGroup.Enabled);

            // ── Step 3: dispatch FinalizeReplay → Commit. ──
            var finalizeCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.FinalizeReplay,
                DomainPayload = null,
            };
            await handler.PrepareAsync(finalizeCmd, CancellationToken.None);
            handler.Commit(finalizeCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: assertions. ──
            Assert.True(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset to false after FinalizeReplay.");
            Assert.True(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task PrepareReplay_DisablesAllFourGroups()
        {
            // ── Step 1: create a recording ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler with all four groups ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            // ── Step 3: PrepareReplay → Commit ──
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: all four groups must be disabled ──
            Assert.False(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be false during RunningReplay.");
            Assert.False(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be false during RunningReplay.");
            Assert.False(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be false during RunningReplay.");
            Assert.False(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be false during RunningReplay.");
            Assert.True(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be true during RunningReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task FinalizeReplay_ReEnablesAllFourGroups()
        {
            // ── Step 1: create a recording ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 1_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler with all four groups, run PrepareReplay ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            var prepareCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // All four groups are disabled.
            Assert.False(inputGroup.Enabled);
            Assert.False(simGroup.Enabled);
            Assert.False(postSimGroup.Enabled);
            Assert.False(lifecycleGroup.Enabled);

            // ── Step 3: FinalizeReplay → Commit ──
            var finalizeCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.FinalizeReplay,
                DomainPayload = null,
            };
            await handler.PrepareAsync(finalizeCmd, CancellationToken.None);
            handler.Commit(finalizeCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: all four groups must be re-enabled ──
            Assert.True(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset to false after FinalizeReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task PrepareLive_ReEnablesAllFourGroups()
        {
            // ── Step 1: create a recording ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 5_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler with all four groups, run PrepareReplay ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            var prepareCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // All four groups are now disabled.
            Assert.False(inputGroup.Enabled);
            Assert.False(simGroup.Enabled);
            Assert.False(postSimGroup.Enabled);

            // ── Step 3: PrepareLive (Live-from-Replay branch) → Commit ──
            var branchCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive,
                DomainPayload = Guid.NewGuid(),  // new branched exercise ID
            };
            await handler.PrepareAsync(branchCmd, CancellationToken.None);
            handler.Commit(branchCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: all four groups must be re-enabled ──
            Assert.True(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.True(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.True(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset after PrepareLive branch.");
        }

        // ── PrepareReplay returns ReplayPrepareResult ─────────────────────────────

        /// <summary>
        /// Verifies that <see cref="ReferenceReplayLoadHandler.PrepareAsync"/> for
        /// <c>PrepareReplay</c> returns a <see cref="ReplayPrepareResult"/> (not a raw long)
        /// with a positive <c>DurationSeconds</c> derived from the recording, and that
        /// <c>JsonSerializer</c> round-trips both fields without loss.
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task PrepareReplay_ReturnsReplayPrepareResult_WithDurationSeconds()
        {
            // ── Step 1: create a recording with a small number of frames. ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);

            for (int i = 0; i < 5; i++)
            {
                _world.SetSingletonUnmanaged(new GlobalTime
                {
                    DeltaTime      = 0.016f,
                    TimeScale      = 1.0f,
                    TotalWallTicks = 10_000L + i * 16L,
                });
                await Task.Delay(20);
            }

            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler and dispatch PrepareReplay. ──
            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    null,
                simGroup:      null,
                postSimGroup:  null,
                lifecycleGroup: null,
                bypassLifecycleToggle: null,
                storageDirectory: _tempDir);

            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };

            var result = await handler.PrepareAsync(cmd, CancellationToken.None);

            cts.Cancel();
            await loopTask;

        // ── Step 3: assert result type and round-trip serialisation via JsonSerializer. ──
            Assert.IsType<ReplayPrepareResult>(result);
            var rpr = (ReplayPrepareResult)result!;

            Assert.True(rpr.DurationSeconds > 0f,
                "DurationSeconds must be > 0 for a recording with at least one frame.");

            // Verify JsonSerializer round-trip: Serialize -> Deserialize must recover identical values.
            var json = System.Text.Json.JsonSerializer.Serialize(rpr);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ReplayPrepareResult>(json);
            Assert.Equal(rpr.MaxNetworkId, deserialized.MaxNetworkId);
            Assert.True(deserialized.DurationSeconds > 0f,
                "DurationSeconds must survive a JsonSerializer round-trip.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        // ── CE-259ap — DOES LIVE INGRESS REACH A NODE IN RunningReplay? ──────────────────────────
        //
        // 🔒 The question the user asked on 2026-09-11, and the prerequisite CE-259ap's whole lean turns
        //    on: docs/designs/replay-and-modules/DESIGN.md §2.1 claims TogglableInputGroup being disabled
        //    is what "blocks live DDS ingress" during playback. If that were true, the two inert
        //    mechanisms (the lifecycle gate and GhostCreationSystem.BypassLifecycle) would be dead weight
        //    and the right answer would be DELETION. If it is false, the bypass is LOAD-BEARING.

        /// <summary>
        /// ⭐⭐⭐ <b>MEASURED: <c>RunningReplay</c> does NOT stop a directly-registered
        /// <c>SystemPhase.Input</c> system — so live DDS ingress DOES reach a replaying node.</b>
        ///
        /// <para>📐 The real <c>ReferenceReplayLoadHandler</c>, the real <c>Commit(PrepareReplay)</c>, a
        /// real <c>ModuleHostKernel</c>. The probe stands in for <c>CycloneNetworkIngressSystem</c>, which
        /// carries the same <c>[UpdateInPhase(SystemPhase.Input)]</c> and is registered the same way —
        /// directly, never into a togglable group. ⚠ A stand-in because this project does not reference
        /// <c>Fdp.Network.Cyclone</c>; the companion rail below asserts the real registrations have exactly
        /// this shape, so the two together carry the claim.</para>
        ///
        /// <para>⛔⛔ <b>Why this is not a nit.</b> Ingress translators call
        /// <c>GhostCreationSystem.CreateGhost(...)</c> DIRECTLY on the Input phase. That sets
        /// <c>EntityLifecycle.Ghost</c> and registers the entity in the <c>NetworkEntityMap</c> — on a node
        /// whose world is being restored from a recording. With <c>TkbIdentity</c> present,
        /// <c>GhostPromotionSystem</c> (now built by <c>EntityCreationPack</c>, and ungated) then promotes
        /// it. ⇒ the exact corruption §3.10.3 feared, with nothing in the way.</para>
        /// </summary>
        [Fact(Timeout = 20_000)]
        public void RunningReplay_DoesNotStopADirectlyRegisteredInputPhaseSystem()
        {
            using var world  = new EntityRepository();
            using var kernel = new ModuleHostKernel(world, new EventAccumulator());

            var probe = new InputPhaseIngressProbe();
            kernel.RegisterGlobalSystem(probe);   // ⭐ exactly how every CycloneNetworkIngressSystem is wired
            kernel.InitializeForTest();

            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var ghostSys       = new GhostCreationSystem(new NetworkEntityMap());
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            // ⚠ A real controller: the handler rejects null. It is never asked to open a file — only
            //   Commit(PrepareReplay) is exercised, and that path is the pure state flip.
            var controller = new EcsRecordReplayController(kernel, nodeId: 1, world);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:       inputGroup,
                simGroup:         simGroup,
                postSimGroup:     postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            // ── Enter RunningReplay. Commit is the state flip; no prepared file is needed for it. ──
            handler.Commit(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = NodeOpType.PrepareReplay,
                DomainPayload = Guid.NewGuid(),
            }, repo: null);

            // ⛔ Anti-vacuity: the handler really did enter the replay state.
            Assert.False(inputGroup.Enabled);
            Assert.False(lifecycleGroup.Enabled);
            Assert.True(ghostSys.BypassLifecycle);

            // ⭐⭐ SYNCHRONOUS ticks, deliberately — no background loop and no Task.Delay.
            //   ⚠ A first version spun RunKernelLoop for 200 ms, which measured the same thing but added
            //     real CPU contention: LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState
            //     (a timing-sensitive sibling, 3/3 green in isolation) then failed in ~1 run of 2 under
            //     the parallel suite. ⇒ the load was MINE, so it goes rather than being explained away.
            //   ⭐ Driving kernel.Update directly is what the loop did anyway, and it makes this rail
            //     DETERMINISTIC instead of a 200 ms race.
            int before = probe.Executions;
            for (int i = 0; i < 3; i++) kernel.Update(0.016f);
            int after = probe.Executions;

            Assert.True(after > before,
                "A directly-registered SystemPhase.Input system kept running while the node was in " +
                "RunningReplay — which is the ANSWER to CE-259ap's prerequisite: live DDS ingress is NOT " +
                "gated during playback. TogglableInputGroup holds the LOGIC-PACK input systems " +
                "(MissionControlExecutionSystem, FireProcessingSystem, …); every " +
                "CycloneNetworkIngressSystem is registered directly, outside it. If this assertion ever " +
                "FAILS, ingress has become gated and CE-259ap's two inert mechanisms are dead weight to " +
                "delete rather than defects to fix — so re-read that row before 'fixing' this test.");
        }

        /// <summary>
        /// ⭐⭐ <b>The companion half: the REAL ingress systems are registered exactly as the probe is —
        /// directly, never into a togglable group — and nothing wires the one mechanism that WOULD gate
        /// them from the replay path.</b>
        ///
        /// <para>📐 Measured <c>2026-09-11</c>: <c>CycloneNetworkIngressSystem</c> exposes
        /// <c>IsWorldStateFrozen</c>, a <c>Func&lt;bool&gt;</c> checked once per <c>Execute</c> that skips
        /// exactly the <c>TranslatorClass.WorldState</c> translators — ⭐⭐ <b>precisely the gate replay
        /// needs.</b> ⛔ It has ONE production writer, <c>CgfSubsystem.WireWorldStateFreezeGate</c>, driven
        /// by the <b>DEBUGGER halt</b> (<c>CgfClusterDebugTimeController.IsWorldStateFrozen =&gt;
        /// _halted</c>, <c>DQ30-C</c>) — not by <c>RunningReplay</c>, and on one host only. ⇒ an
        /// under-adopted seam, which is why <c>CE-259ap</c>'s lean is to ADOPT it rather than to honour
        /// <c>BypassLifecycle</c> or populate the lifecycle group.</para>
        /// </summary>
        [Fact]
        public void TheReplayPathWiresNoWorldStateFreeze_AndIngressIsNeverInATogglableGroup()
        {
            var handler = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs"));

            // ⭐ The replay path toggles four GROUPS and a bypass flag — and touches no ingress gate.
            Assert.Contains("_inputGroup.Enabled", handler);
            Assert.DoesNotContain("IsWorldStateFrozen", handler);

            // ⛔ Every production registration of the ingress system is direct. If one is ever wrapped in
            //   a togglable group this reddens, and that is the good outcome — it would mean replay
            //   isolation grew a real gate.
            foreach (var path in new[]
            {
                "Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs",
                "Hrot/Network/Hrot.Network.BDC/Replication/BdcReplicationModule.cs",
                "Hrot/Network/Hrot.Network.NED/Translators/Map/EntityStatesIngressPack.cs",
            })
            {
                var src = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(path));
                Assert.Contains("CycloneNetworkIngressSystem", src);
                Assert.DoesNotContain("TogglableInputGroup", src);
            }
        }

        /// <summary>⭐ Stands in for <c>CycloneNetworkIngressSystem</c>: same phase, same registration shape.</summary>
        [Fdp.ModuleHost.Abstractions.UpdateInPhase(Fdp.ModuleHost.Abstractions.SystemPhase.Input)]
        private sealed class InputPhaseIngressProbe : Fdp.ModuleHost.Abstractions.IEcsModuleSystem
        {
            public int Executions;
            public void Execute(Fdp.ModuleHost.Abstractions.ISimulationView view, float dt) => Executions++;
        }

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
