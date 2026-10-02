using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>L7</c> — <b>every ECS host runs ONE load-phase chain, and the parts it runs come from its
    /// ROLES.</b>
    ///
    /// <para>⛔⛔ <b>REWRITTEN <c>2026-09-18</c>, and the old version is why.</b> 🔒 <c>C8</c> ruled
    /// "IG/CGF get real terrain loaders", and this file asserted the remedy it chose: <i>register the
    /// terrain loader BEFORE every other <c>PrepareLive</c> claimant</i>. 🔴 The mechanism was right —
    /// <c>ClusterSlave</c> dispatches to the FIRST claimant and returns — but the remedy was not:
    /// registering first does not mean "before the others", it means <b>INSTEAD OF the others</b>.
    /// Measured: on CGF the terrain loader shadowed the scenario loader and the cluster loaded ZERO
    /// entities; on SimHost the knowledge-base loader shadowed terrain, so terrain had NEVER loaded.
    /// ⚠ <b>This suite was GREEN throughout</b>, because it asserted the ordering it had been written to
    /// defend rather than what a node ends up running.</para>
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
    public sealed class EveryEcsHostComposesTheLoadPhaseChainRails
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
                code.Contains("LoadPhaseChain.FromRoles", StringComparison.Ordinal),
                $"{relativePath} must compose its load phase through LoadPhaseChain.FromRoles. ⛔ A host "
              + "that hand-registers prerequisite loaders as separate handlers makes them COMPETE: "
              + "ClusterSlave gives the step to the first claimant and returns, and the losers vanish "
              + "silently. 📄 docs/DESIGN_Cluster_Load_Phase.md §4.1b.");

            Assert.True(
                CompositionRootSource.ConstructsType(code, "KnowledgeBaseLoadStep"),
                $"{relativePath} must offer a KnowledgeBaseLoadStep. ⭐ The knowledge base is required by "
              + "EVERY ECS node — not by any role — because every ECS node composes the full genesis "
              + "pipeline and must be able to resolve the templates of entities it may be asked to "
              + "create (Q65-A′). 🔴 CGF and IG had none at all and would have ignored a scenario's "
              + "TkbName entirely.");
        }

        /// <summary>
        /// ⭐ And it must be handed a <c>RoadNetworkHolder</c>, which is why the ctor rejects a null one:
        /// publishing a blob with no owner is a leak plus a generation that is never retired.
        /// </summary>
        [Fact]
        public void OnlyAHostWhoseROLEReadsTheRoadGraph_OwnsARoadNetworkHolder()
        {
            // ⭐ SimHost carries MuscleGround and NavigationSolver — the two roles with a measured
            //   road-graph consumer — so it owns a holder and offers a terrain step.
            var simHost = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource("Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs"));

            Assert.True(CompositionRootSource.ConstructsType(simHost, "RoadNetworkHolder"),
                "SimHost carries the roles that read the road graph, so it owns the holder it publishes through.");
            Assert.True(CompositionRootSource.ConstructsType(simHost, "TerrainLoadStep"),
                "SimHost must offer the terrain part its roles require.");

            // ⛔ IG (Map2D) and CGF (Brain) have NO road-graph consumer, so they load no terrain at all.
            //   🔒 "load nothing where nothing reads it" (user, 2026-09-18). ⚠ IG previously held a
            //   RoadNetworkHolder that nothing on that host ever read.
            foreach (var path in new[]
            {
                "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
                "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            })
            {
                var code = CompositionRootSource.StripComments(
                    CompositionRootSource.ReadRepoSource(path));

                Assert.False(CompositionRootSource.ConstructsType(code, "TerrainLoadStep"),
                    $"{path} has no role that reads the road graph, so it must make no terrain resident.");
            }
        }

        // ── ② ⭐⭐⭐ it is not SHADOWED ─────────────────────────────────────────────────────────

        // ── ② ⭐⭐⭐ nothing can be SHADOWED, because nothing else claims the step ───────────────

        /// <summary>
        /// ⛔⛔ <b>THE RAIL THAT MATTERS, restated against the real defect.</b> The old version asked
        /// "is the terrain loader registered EARLY enough?" — a question that has no safe answer when the
        /// dispatcher picks exactly one claimant. ⭐ This one asks the question that does: <b>does any host
        /// still hand-register a prerequisite loader as its own handler?</b>
        ///
        /// <para>⚠ If a new prerequisite is ever added as a handler instead of a step, this reddens — and
        /// that is precisely the mistake <c>C8</c> made and this suite failed to catch.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(EcsCompositionRoots))]
        public void NoHost_HandRegistersAPrerequisiteLoaderAsItsOwnHandler(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            foreach (var retired in new[]
            {
                "TerrainLoadClusterStateHandler",
                "TkbLoadClusterStateHandler",
                "CgfScenarioLoadHandler",
                "HrotScenarioLoadHandler",
                "HrotEditLoadHandler",
            })
            {
                Assert.False(CompositionRootSource.ConstructsType(code, retired),
                    $"{relativePath} constructs {retired}, which was RETIRED into the load-phase chain. "
                  + "Registering a prerequisite as its own handler makes it compete for the one load step "
                  + "and silently cancel the others. 📄 docs/DESIGN_Cluster_Load_Phase.md §2.2.");
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
