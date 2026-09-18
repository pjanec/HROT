using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>C8</c> — 🔒 <b>user ruling <c>2026-09-17</c>: "IG/CGF get loaders in batch 3".</b> Every ECS
    /// host composes a REAL <see cref="TerrainLoadClusterStateHandler"/>, not a scoped-down identity check.
    ///
    /// <para>⛔⛔ <b>The second rail here is the one that matters, and it is not "is it registered".</b>
    /// 📐 Measured while building <c>C8</c>: <c>ClusterSlave</c> dispatches to the FIRST
    /// <c>CanHandle</c>-true handler and then RETURNS. The terrain loader claims
    /// <c>PrepareLive</c>/<c>PrepareEdit</c> — and so does <c>ReferenceLiveLoadHandler</c>,
    /// UNCONDITIONALLY. ⇒ registering terrain anywhere after it makes the loader <b>dead code that never
    /// runs, silently</b>. IG and CGF both registered their live handler early, so the naive placement
    /// (down beside the <c>TerrainAssetRegistrar</c> call) would have shipped a loader that could not
    /// fire. ⭐ That is the same shadowing defect <c>SerializeLocalRegistrar</c>'s header records: "the
    /// archive handler SHADOWED the scenario save on some hosts — no scenario slice was ever written".</para>
    ///
    /// <para>⚠ <b>Why a SOURCE scan and not a constructed host.</b> <c>IgNodeBootstrapper</c> is
    /// <c>internal</c> and <c>CgfSubsystem</c> needs most of an application to stand up; this is the
    /// established pattern for "does host X compose Y" in this repo (<c>NodeRolePersistenceRails</c> uses
    /// the same helper for the same reason). ⭐ The ORDERING SEMANTICS it relies on are asserted
    /// separately, against a real <see cref="ClusterSlave"/>, in
    /// <see cref="RegistrationOrderDecidesTheDispatchWinner"/> — so neither rail rests on an assumption
    /// the other one makes.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ②a ③ ④, §8.3, §10.5; defect <c>BP-537</c>.
    /// </summary>
    public sealed class TerrainLoaderIsComposedOnEveryEcsHostRails
    {
        /// <summary>The three ECS composition roots that must each hold a terrain loader.</summary>
        public static TheoryData<string> EcsCompositionRoots => new()
        {
            "Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
        };

        // ── ① it is composed at all ───────────────────────────────────────────────────────────

        [Theory]
        [MemberData(nameof(EcsCompositionRoots))]
        public void EveryEcsHost_ComposesARealTerrainLoader(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            Assert.True(
                CompositionRootSource.ConstructsType(code, "TerrainLoadClusterStateHandler"),
                $"{relativePath} must COMPOSE a TerrainLoadClusterStateHandler. A host that cannot load "
              + "the terrain its scenario names fails D5's identity check on every zone op (BP-537), and "
              + "the user ruled that IG and CGF get real loaders — not a scoped-down check.");
        }

        /// <summary>
        /// ⭐ And it must be handed a <c>RoadNetworkHolder</c>, which is why the ctor rejects a null one:
        /// publishing a blob with no owner is a leak plus a generation that is never retired.
        /// </summary>
        [Theory]
        [MemberData(nameof(EcsCompositionRoots))]
        public void EveryEcsHost_OwnsARoadNetworkHolder(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            Assert.True(
                CompositionRootSource.ConstructsType(code, "RoadNetworkHolder"),
                $"{relativePath} must own a RoadNetworkHolder — the terrain loader publishes through it.");
        }

        // ── ② ⭐⭐⭐ it is not SHADOWED ─────────────────────────────────────────────────────────

        /// <summary>
        /// ⛔⛔ <b>THE RAIL THAT MATTERS.</b> The terrain loader must be registered BEFORE every other
        /// handler on that host which claims <c>PrepareLive</c> — otherwise the first such handler wins
        /// the dispatch and the loader never runs.
        ///
        /// <para>⚠ The shadowing set is named explicitly rather than inferred: these are the handlers
        /// measured to claim <c>PrepareLive</c> (<c>ReferenceLiveLoadHandler</c> unconditionally;
        /// <c>CgfScenarioLoadHandler</c> / <c>HrotScenarioLoadHandler</c> / <c>HrotEditLoadHandler</c> for
        /// a scenario load). ⭐ If a NEW <c>PrepareLive</c> claimant is added above the loader on any
        /// host, this rail reddens — which is the whole point.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(EcsCompositionRoots))]
        public void TheTerrainLoader_IsRegisteredBeforeEveryPrepareLiveClaimant(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            int terrainAt = code.IndexOf("TerrainLoadClusterStateHandler", StringComparison.Ordinal);
            Assert.True(terrainAt >= 0, $"{relativePath} composes no terrain loader at all.");

            foreach (var claimant in new[]
            {
                "ReferenceLiveLoadHandler",
                "CgfScenarioLoadHandler",
                "HrotScenarioLoadHandler",
                "HrotEditLoadHandler",
            })
            {
                int claimantAt = code.IndexOf(claimant, StringComparison.Ordinal);
                if (claimantAt < 0) continue;   // this host does not compose that one

                Assert.True(terrainAt < claimantAt,
                    $"{relativePath}: the terrain loader is registered AFTER {claimant}, which also claims "
                  + "PrepareLive. ClusterSlave dispatches to the FIRST matching handler and returns, so the "
                  + "terrain loader would be dead code that never runs — silently. Move it above.");
            }
        }

        /// <summary>
        /// ⭐ The ORDERING SEMANTICS the source rail above depends on, asserted against a real
        /// <see cref="ClusterSlave"/> so that rail is not resting on my reading of the dispatch loop.
        /// ⛔ If <c>ClusterSlave</c> ever dispatched to ALL matching handlers instead of the first, this
        /// test would fail and the source rail's premise would be void — which is exactly when someone
        /// needs to know.
        /// </summary>
        [Fact]
        public void RegistrationOrderDecidesTheDispatchWinner()
        {
            using var bus  = new FdpEventBus();
            using var repo = new EntityRepository();
            using var slave = new ClusterSlave(bus, nodeId: 1);

            var first  = new SpyHandler();
            var second = new SpyHandler();
            slave.RegisterHandler(first);
            slave.RegisterHandler(second);

            bus.PublishManaged(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 1,
                Operation     = NodeOpType.PrepareLive,
            });
            bus.SwapBuffers();
            slave.Tick();

            Assert.Equal(1, first.PrepareCalls);
            Assert.Equal(0, second.PrepareCalls);   // ⛔ shadowed — the premise of the rail above
        }

        private sealed class SpyHandler : IClusterStateHandler
        {
            public int PrepareCalls { get; private set; }

            public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareLive;

            public System.Threading.Tasks.Task<object?> PrepareAsync(
                ExecuteNodeOpIntent intent, System.Threading.CancellationToken ct)
            {
                PrepareCalls++;
                return System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
            public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
        }
    }
}
