using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-162</c> — <b>a subsystem that HOLDS a capability may not hand the debug provider
    /// <c>null</c> for it.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §1 *(the capability matrix is MEASURED
    /// from wired members — <c>R-133</c>)* · <c>docs/DESIGN_Entity_Creation_Unification.md</c> §2.3c.</para>
    ///
    /// <para>🔴 <b>The defect, measured on a live cluster <c>2026-09-03</c>.</b> <c>IgSubsystem</c> passed
    /// <c>entityMap: null</c> while <c>IgApplication</c> assigns <c>_entityMap = _context.EntityMap</c> and
    /// exposes it as <c>TestHook_EntityMap</c> — the same member name and shape <c>SimHostSubsystem</c>
    /// passes. Because <c>SubsystemDebugProvider</c> computes each capability cell from the member being
    /// non-null, <c>GET /capabilities</c> reported <c>world.entityMap:false</c> for IG and
    /// <c>GET /entities</c> answered <c>NOT_SUPPORTED_HERE</c> there — so the IG side of any cross-node
    /// entity comparison was unreadable.</para>
    ///
    /// <para>⛔⛔ <b>Why a rail and not just the fix.</b> This is the <b>11th</b> instance of the pattern
    /// <c>CLAUDE.md</c> names — <i>"a production caller that HAS a dependency must PASS it"</i> — and the
    /// distinguishing feature of that family is that the <c>null</c> reads as a deliberate,
    /// documented absence. 📌 Here it literally was: the argument sat under a paragraph explaining that IG
    /// <i>"can neither drive time nor map network ids"</i>, of which only the TIME half was ever true. ⇒ ⭐
    /// prose cannot be the control; the rail compares what the app HAS against what the provider PASSES.</para>
    ///
    /// <para>⚠ <b>Deliberately narrow, and honest about it.</b> This gates ONE capability — the entity map —
    /// across every subsystem that has one. ⛔ It is not a generic "no argument may be null" sweep: those
    /// flag dozens of correctly-defaulted arguments and get switched off within a batch
    /// (<c>CLAUDE.md</c>, the silent-default section, records exactly that being tried and thrown away).
    /// ⭐ A genuine absence stays expressible — <c>ExCon</c> has no world at all and is not in the list.</para>
    /// </summary>
    public class TheDebugProvidersDoNotUnderReportTests
    {
        /// <summary>
        /// ⭐ Subsystem composition file → the app file whose member it must forward. Both are read from
        /// source: the provider is built lazily from closures, so nothing about this is observable by
        /// constructing one — the claim is about what the composition PASSES.
        /// </summary>
        public static TheoryData<string, string> SubsystemsWithAnEntityMap() => new()
        {
            { "Hrot/Subsystems/Hrot.IG/IgSubsystem.cs",           "Hrot/Subsystems/Hrot.IG/IgApplication.cs" },
            { "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs", "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs" },
        };

        [Theory]
        [MemberData(nameof(SubsystemsWithAnEntityMap))]
        public void ASubsystemHoldingAnEntityMap_PassesItToItsDebugProvider(
            string subsystemPath, string appPath)
        {
            var app = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(appPath));

            // ⛔ Anti-vacuity: if the app stops holding a map, this row is asserting nothing and must be
            //   re-examined rather than silently passing.
            Assert.True(app.Contains("NetworkEntityMap"),
                $"{appPath} no longer mentions NetworkEntityMap, so this row cannot assert anything. " +
                "Either the app genuinely lost its map (drop the row and say why) or the rail is aimed " +
                "at the wrong file.");

            var subsystem = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(subsystemPath));

            Assert.Contains("entityMap:", subsystem);

            var afterKey = subsystem[(subsystem.IndexOf("entityMap:", System.StringComparison.Ordinal)
                                      + "entityMap:".Length)..].TrimStart();

            Assert.False(afterKey.StartsWith("null", System.StringComparison.Ordinal),
                $"{subsystemPath} passes `entityMap: null` while {appPath} holds a NetworkEntityMap. " +
                "SubsystemDebugProvider computes the capability cell from the member being non-null, so " +
                "GET /capabilities will report world.entityMap:false and GET /entities will answer " +
                "NOT_SUPPORTED_HERE for this perspective — on its own port too, because the cell comes " +
                "from the provider and not from the hosting topology. Forward the map (CE-162).");
        }

        // ── CE-163 — the cluster state, and it is UNIFORM across the ECS nodes ────────────────────

        /// <summary>
        /// ⭐⭐ Every ECS node — <b>all three, deliberately</b>. 🔒 The ruling: <i>"every ECS node must use
        /// the same shared code."</i>
        ///
        /// <para>⛔ <c>ExCon</c> is NOT here and that is correct, not an omission: it has no ECS world and
        /// it is the one subsystem that builds and pumps a <c>ClusterUiCache</c>, so it contributes the
        /// <b>cluster-wide</b> view rather than a node's own committed state. ⭐ Two different facts.</para>
        /// </summary>
        public static TheoryData<string> EcsNodeSubsystems() => new()
        {
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            "Hrot/Subsystems/Hrot.IG/IgSubsystem.cs",
            "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs",
        };

        /// <summary>
        /// 🔴 <b><c>CE-163</c>.</b> 📐 Measured on a four-process cluster <c>2026-09-03</c>:
        /// <c>POST /scenario/load/live {waitForReady:true}</c> answered
        /// <c>NOT_SUPPORTED_HERE(cluster.state)</c> on <b>every</b> node, while <c>{waitForReady:false}</c>
        /// published fine and the fan-out landed — so only the readiness READ was missing.
        ///
        /// <para>⚠ <b>The defect shape differs from <c>CE-162</c>'s and the assertion follows it.</b> There
        /// the argument was present and <c>null</c>; here it was <b>absent entirely</b>, which no
        /// "not null" check can see. ⇒ this asserts the composition CALLS the shared projection.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(EcsNodeSubsystems))]
        public void AnEcsNodeProjectsItsOwnClusterState_ThroughTheSharedSeam(string subsystemPath)
        {
            var subsystem = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(subsystemPath));

            Assert.True(subsystem.Contains("ClusterStateFrom("),
                $"{subsystemPath} does not pass its cluster state through " +
                "SubsystemDebugProvider.ClusterStateFrom(...). Every ECS node holds a ClusterSlave whose " +
                "LocalClusterState is its committed cluster state; a node that does not project it makes " +
                "GET /capabilities report cluster.state absent and POST /scenario/load/live " +
                "{waitForReady:true} answer NOT_SUPPORTED_HERE — even though the same node accepts the " +
                "load with waitForReady:false. Use the shared projection, not a hand-written lambda: the " +
                "point of CE-163 is that all three nodes read it the same way.");
        }

        /// <summary>
        /// ⛔ <b>Anti-vacuity for the row above, and it is the load-bearing half.</b> The theory asserts a
        /// CALL; this asserts the thing called still reads what it claims to. ⇒ if
        /// <c>ClusterSlave.LocalClusterState</c> is renamed away or stops reading <c>_localStateId</c>,
        /// three green rows would otherwise keep asserting nothing.
        /// </summary>
        [Fact]
        public void TheSharedSeamReadsTheSlavesCommittedState()
        {
            var slave = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "FDP/Toolkits/Fdp.Toolkits/Orchestration/ClusterSlave.cs"));

            Assert.Contains("public ClusterState LocalClusterState", slave);
            Assert.Contains("(ClusterState)_localStateId", slave);

            var provider = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "Hrot/Engine/Hrot.Presentation/DebugApi/ISubsystemDebugProvider.cs"));

            Assert.Contains("ClusterStateFrom(Func<ClusterSlave?> clusterSlave)", provider);
            Assert.Contains("clusterSlave()?.LocalClusterState", provider);
        }

        // ── CE-164 — ONE orchestration bus per node, and the base CHECKS it ───────────────────────

        /// <summary>
        /// 🔴 <b><c>CE-164</c> — a networked slave node has ONE orchestration bus.</b>
        ///
        /// <para>📐 Measured <c>2026-09-03</c>: <c>HrotNodeBuilder</c> Step 8 builds this node's
        /// <c>ClusterSlave</c> AND a complete <c>ISlaveOrchestrationTranslator</c>
        /// *(<c>NodeOpSlaveTranslator</c> + <c>ClusterOpEgressTranslator</c>)* on
        /// <c>HrotNodeContext.EventBus</c>. IG called that builder and then built a <b>second</b> bus with a
        /// bare ingress-only translator and ticked that instead — so every <c>TransitionStateIntent</c> it
        /// published landed on a bus nothing drained. <c>load/live</c> on the IG port answered
        /// <c>ok / "cluster-intent"</c> and the cluster never moved.</para>
        ///
        /// <para>⚠ <b>Why a source rail AND a runtime assertion.</b> The runtime post-condition in
        /// <c>SharedApplicationBootstrapper</c> is the real control — it fires on any node, including ones
        /// that do not exist yet. ⛔ But it only fires when a node is actually bootstrapped with a bus, and
        /// the cheapest regression *(someone re-adds <c>new FdpEventBus()</c> to a bootstrapper)* is
        /// visible in source without standing a node up. ⇒ this rail is the fast half.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(EcsNodeSubsystems))]
        public void AnEcsNodeDoesNotBuildASecondOrchestrationBus(string subsystemPath)
        {
            // ⭐ The bootstrapper, not the subsystem, is where a node wires orchestration — map across.
            var bootstrapperPath = subsystemPath switch
            {
                "Hrot/Subsystems/Hrot.IG/IgSubsystem.cs"           => "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
                "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs" => "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs",
                "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs"         => "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
                _ => throw new System.InvalidOperationException($"no bootstrapper mapped for {subsystemPath}"),
            };

            var src = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(bootstrapperPath));

            Assert.False(src.Contains("new FdpEventBus()"),
                $"{bootstrapperPath} constructs its own FdpEventBus. A networked slave node has exactly " +
                "ONE orchestration bus — the one HrotNodeBuilder created on HrotNodeContext.EventBus, " +
                "which already carries this node's complete ISlaveOrchestrationTranslator (ingress AND " +
                "the ClusterOpEgressTranslator that drains TransitionStateIntent to DDS). A second bus " +
                "splits the control plane: the node publishes on one and ticks the other, and every " +
                "intent is silently read by nobody. Build the ClusterSlave on context.EventBus (CE-164).");
        }

        /// <summary>
        /// ⛔ <b>Anti-vacuity + the durable half:</b> the rail above is a text check, so it must not be the
        /// only thing standing. This asserts the RUNTIME post-condition still exists in the shared base —
        /// ⭐ that is what actually binds every node, present and future.
        /// </summary>
        [Fact]
        public void TheSharedBootstrapperAssertsTheOneBusInvariant()
        {
            var slave = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "FDP/Toolkits/Fdp.Toolkits/Orchestration/ClusterSlave.cs"));

            Assert.Contains("public bool PublishesOn(FdpEventBus? bus)", slave);
            Assert.Contains("ReferenceEquals(_eventBus, bus)", slave);

            var basePath = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs"));

            Assert.Contains("!slave.PublishesOn(context.EventBus)", basePath);
            Assert.Contains("context.Participant != null && networkFactory != null && context.SlaveTranslator == null", basePath);
        }
    }
}
