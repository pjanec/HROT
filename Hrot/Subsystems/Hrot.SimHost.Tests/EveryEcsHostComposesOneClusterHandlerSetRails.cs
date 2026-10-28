using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replay;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Map.Common.Services;
using Hrot.SimHost.Modules.Orchestration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>D16</c> (<c>R-3046</c>) — <b>every ECS host composes its cluster handlers through ONE method</b>,
    /// <see cref="EcsHostClusterHandlers.Compose"/>, so every host's slave holds the same set in the same order.
    /// <para>📐 Before it (restore design §1b U3): <c>NodeBootstrapper</c>, <c>CgfSubsystem</c> and <c>EditorSubsystem</c>
    /// each wrote the set by hand and had drifted — the checkpoint handler on one, the editor's recorder built without
    /// <c>ForNode</c> and its replay handler without the world-replacement hook, its dry run on no slave.</para>
    /// <para>Two halves, as <see cref="EveryEcsHostComposesTheLoadPhaseChainRails"/>: the CONSTRUCTED set (what Compose
    /// registers, in what order), and a source scan that the composition roots call it and build none of it by hand
    /// (<c>CgfSubsystem</c> needs most of an application to stand up).</para>
    /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1b, D14–D16.
    /// </summary>
    public sealed class EveryEcsHostComposesOneClusterHandlerSetRails : IDisposable
    {
        private readonly EntityRepository _world  = new();
        private readonly string           _staging = Path.Combine(Path.GetTempPath(), "d16-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { /* best effort */ }
        }

        private (ClusterSlave Slave, EcsHostClusterHandlers Built) ComposeOnARealSlave()
        {
            Directory.CreateDirectory(_staging);
            var accumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(_world, accumulator);
            var slave  = new ClusterSlave(7, "D16", null, NodeRole.MuscleGround);
            var (providers, _) = HrotUniversalLoadParts.Build(null, _staging, new CarKinem.Road.RoadNetworkHolder(), null);

            var built = EcsHostClusterHandlers.Compose(new EcsHostClusterHandlerInputs
            {
                Slave         = slave,
                Kernel        = kernel,
                World         = _world,
                NodeId        = 7,
                Role          = NodeRole.MuscleGround,
                HostLabel     = "D16",
                StagingRoot   = _staging,
                LoadProviders = providers,
                EventAccumulator = accumulator,   // CE-3314 — required: the kernel's own step window
            });
            return (slave, built);
        }

        [Fact]
        public void Compose_RegistersTheSharedSet_InTheOneOrder()
        {
            var (slave, built) = ComposeOnARealSlave();
            var types = slave.RegisteredHandlers.Select(h => h.GetType()).ToList();

            // ⚠ ClusterSlave gives an operation to the FIRST claimant: replay load first (it claims PrepareLive while a
            //   replay is active), live load last (it claims what nothing before it did).
            Assert.Equal(typeof(ReferenceReplayLoadHandler), types[0]);
            Assert.Equal(typeof(ReferenceLiveLoadHandler),   types[^1]);

            foreach (var t in new[] { typeof(ReferencePreviewHandler), typeof(ReferencePrefetchHandler),
                                      typeof(TerrainAssetHandler), typeof(LoadPhaseChain) })
                Assert.Equal(1, types.Count(x => x == t));

            Assert.True(types.IndexOf(typeof(ReferencePreviewHandler)) < types.IndexOf(typeof(LoadPhaseChain)),
                "the dry run must precede the load chain — both answer PrepareState");

            // ⭐ D14 — the handler the host keeps (the editor's toolbar drives it) IS the one on the slave.
            Assert.Same(built.DryRun, slave.RegisteredHandlers.OfType<ReferencePreviewHandler>().Single());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4093</c> (AQ108 D20) — every ECS host answers a master's CHECK ROUND, by construction: the composition registers ONE
        /// <see cref="WorldChangeCheckHandler"/>, built on the host's OWN world and on the check instance the host keeps
        /// (<see cref="EcsHostClusterHandlers.ChangeCheck"/>) — the baseline hooks (load chain, save wrapper) hold that same instance.
        /// 🔴 Mutation: the handler not registered ⇒ the first assertion fails; a second <c>new WorldChangeCheck()</c> for the handler ⇒ the
        /// "same instance" assertions fail.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task CE4093_Compose_RegistersTheCheckUnsavedHandler_OnTheHostsOwnWorldAndCheck()
        {
            var (slave, built) = ComposeOnARealSlave();

            var handler = Assert.Single(slave.RegisteredHandlers.OfType<WorldChangeCheckHandler>());
            Assert.True(handler.CanHandle(NodeOpType.CheckUnsaved));
            Assert.Same(slave.RegisteredHandlers.First(h => h.CanHandle(NodeOpType.CheckUnsaved)), handler);   // nobody ahead of it claims the op

            var intent = new ExecuteNodeOpIntent { Operation = NodeOpType.CheckUnsaved, TransactionId = Guid.NewGuid() };
            Assert.Equal(WorldChangeCheckHandler.NoBaseline, await handler.PrepareAsync(intent, default));
            built.ChangeCheck.TakeBaseline(_world);                                                          // the instance the host keeps IS the handler's
            Assert.Equal(WorldChangeCheckHandler.Unchanged, await handler.PrepareAsync(intent, default));
            _world.CreateEntity();                                                                           // … on the host's own world
            Assert.Equal(WorldChangeCheckHandler.Changed, await handler.PrepareAsync(intent, default));

            // the load chain — baselines (a)/(c) — holds the SAME instance (a forwarding rail on the CONSTRUCTED object)
            var chain = Assert.Single(slave.RegisteredHandlers.OfType<LoadPhaseChain>());
            var held = typeof(LoadPhaseChain).GetField("_changeCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(chain);
            Assert.Same(built.ChangeCheck, held);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4093</c> hook (b) — the host's scenario-save handler is registered WRAPPED, by the composition (not by each host): a completed
        /// save (the node's slice written) re-baselines the check, and the wrapped handler is still the one that claims and runs the save.
        /// 🔴 Mutation: the save handler registered unwrapped ⇒ the first assertion fails; <c>Commit</c> not taking the baseline ⇒ the last fails.
        /// </summary>
        [Fact]
        public void CE4093_Compose_WrapsTheScenarioSaveHandler_SoACompletedSaveRebaselines()
        {
            Directory.CreateDirectory(_staging);
            var accumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(_world, accumulator);
            var slave  = new ClusterSlave(7, "D16", null, NodeRole.MuscleGround);
            var (providers, _) = HrotUniversalLoadParts.Build(null, _staging, new CarKinem.Road.RoadNetworkHolder(), null);
            var save = new SaveStub();

            var built = EcsHostClusterHandlers.Compose(new EcsHostClusterHandlerInputs
            {
                Slave = slave, Kernel = kernel, World = _world, NodeId = 7, Role = NodeRole.MuscleGround, HostLabel = "D16",
                StagingRoot = _staging, LoadProviders = providers, EventAccumulator = accumulator, ScenarioSaveHandler = save,
            });

            var wrapper = Assert.Single(slave.RegisteredHandlers.OfType<BaselineAfterSaveHandler>());
            Assert.Same(save, wrapper.Inner);
            Assert.DoesNotContain(slave.RegisteredHandlers, h => ReferenceEquals(h, save));          // the wrapper replaced it, nothing registered twice

            var intent = new ExecuteNodeOpIntent
            {
                Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid(),
                DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload("scn"),
            };
            Assert.Same(wrapper, slave.RegisteredHandlers.First(h => h.CanHandle(intent)));           // it claims the save (the archive handler does not)
            Assert.False(built.ChangeCheck.HasBaseline);

            wrapper.Commit(intent, null);

            Assert.True(save.Committed);
            Assert.True(built.ChangeCheck.HasBaseline);
            Assert.False(built.ChangeCheck.HasChanged(_world));
        }

        private sealed class SaveStub : IClusterStateHandler
        {
            public bool Committed { get; private set; }
            public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;
            public bool CanHandle(ExecuteNodeOpIntent intent) => intent.Operation == NodeOpType.SerializeLocal && intent.DomainPayload is Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload;
            public System.Threading.Tasks.Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.FromResult<object?>(null);
            public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) => Committed = true;
            public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
        }

        public static TheoryData<string> CompositionRoots => new()
        {
            "Hrot/Engine/Hrot.EcsNode/NodeBootstrapper.cs",          // SimHost, IG, traffic, Stride
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
        };

        /// <summary>The roots call the ONE composition and construct none of its handlers themselves.</summary>
        [Theory]
        [MemberData(nameof(CompositionRoots))]
        public void EveryCompositionRoot_CallsTheSharedComposition_AndBuildsNoneByHand(string relativePath)
        {
            var code = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(relativePath));

            Assert.Contains("EcsHostClusterHandlers.Compose(", code, StringComparison.Ordinal);
            foreach (var byHand in new[]
            {
                "new ReferenceReplayLoadHandler(", "new Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler(",
                "new ReferenceLiveLoadHandler(",   "new Fdp.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(",
                "new ReferencePreviewHandler(",    "new ReferencePrefetchHandler(",
                "new Fdp.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler(",
                "new ReferenceCheckpointHandler(", "LoadPhaseChain.FromRoles(",
                "EcsRecordReplayController.ForNode(", "new Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController(",
                "new EcsRecordReplayController(",  "SerializeLocalRegistrar.Register(", "TerrainAssetRegistrar.Register(",
            })
                Assert.False(code.Contains(byHand, StringComparison.Ordinal),
                    $"{relativePath} builds '{byHand.TrimEnd('(')}' by hand. ⛔ D16: the cluster handlers of an ECS host are "
                  + "composed in ONE place (EcsHostClusterHandlers.Compose) — a hand-built copy is how the hosts drifted.");
        }
    }
}
