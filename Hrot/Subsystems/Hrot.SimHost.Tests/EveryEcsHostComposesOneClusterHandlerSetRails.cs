using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
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
