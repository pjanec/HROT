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
            // ⭐ CE-259am — ReplayBrowser gained a map in DESIGN_Gizmo_Anchor_Identity.md §6.7 and must
            //   forward it like everyone else. ⚠ Its composition and its "app" are ONE file: the subsystem
            //   holds _activeRepo and rebuilds the singleton itself (EnsureNetworkEntityMap).
            {
                "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs",
                "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs"
            },
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

        // ── CE-259am — a subsystem that OWNS A PERSPECTIVE contributes a provider AT ALL ──────────

        /// <summary>
        /// ⭐⭐⭐ The subsystems that OWN a perspective, from
        /// <c>docs/DESIGN_Perspective_Unification.md</c> §1b's <i>subsystem → perspectives</i> table
        /// *(user-confirmed <c>2026-08-23</c>)* — ⛔ not a list this rail invented.
        ///
        /// <para>⚠ <b>Two deliberate absences, both from that same table:</b> the <b>editor</b> owns the
        /// debug API itself with the full surface *(it needs no provider — <c>DESIGN_Mcp_Diagnostics_Federation.md</c>
        /// §1)*, and the <b>orchestrator</b> owns NO perspective *(both its windows are
        /// <c>WindowScope.Global</c> with an empty <c>OwningPerspective</c>)</b>.</para>
        /// </summary>
        public static TheoryData<string, string> PerspectiveOwningSubsystems() => new()
        {
            { "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",                         "Scenario" },
            { "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs",                 "SimHost" },
            { "Hrot/Subsystems/Hrot.IG/IgSubsystem.cs",                           "IG" },
            { "Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs",                     "ExCon" },
            { "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs",     "ReplayBrowser" },
        };

        /// <summary>
        /// 🔴 <b><c>CE-259am</c> — measured <c>2026-09-11</c> driving <c>--mode replaybrowser</c> under
        /// <c>xvfb</c> over HTTP.</b> <c>ReplayBrowserSubsystem</c> implemented no
        /// <c>IProvidesDebugSurface</c> at all, so <c>PerspectiveScopedDispatcher</c> was constructed with
        /// an EMPTY provider list ⇒ <c>GET /capabilities</c> reported <c>providers=[]</c> and
        /// <c>matrix={}</c>, and <c>/panels/_gizmo</c>, <c>/annotations</c>, <c>/entities/{id}</c> and
        /// <c>/entities/{id}/focus</c> all refused — ⛔ while that host held a world, a map AND a gizmo
        /// buffer it was filling every frame.
        ///
        /// <para>⚠⚠ <b>A THIRD defect shape, which is why neither existing theory could see it.</b>
        /// <c>CE-162</c> was <i>argument present and <c>null</c></i>; <c>CE-163</c> was <i>argument absent
        /// from an existing provider</i>; this is <b>no provider whatsoever</b> — 📌 and both of those
        /// rails read the provider's argument list, which does not exist in this case. ⇒ the assertion has
        /// to be about the subsystem DECLARING the seam.</para>
        ///
        /// <para>⭐ It also pins the <c>perspective:</c> STRING, and that half is not decoration:
        /// <c>PerspectiveScopedDispatcher.Resolve</c> matches on it, so a provider naming a perspective the
        /// window manager never reports is <b>indistinguishable at runtime from contributing nothing</b> —
        /// the same silent outcome, one line further in.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(PerspectiveOwningSubsystems))]
        public void ASubsystemOwningAPerspective_ContributesADebugProvider(
            string subsystemPath, string perspective)
        {
            var src = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(subsystemPath));

            Assert.True(src.Contains("IProvidesDebugSurface"),
                $"{subsystemPath} owns the '{perspective}' perspective but does not implement " +
                "Hrot.Presentation.DebugApi.IProvidesDebugSurface. ClusterRunner/Program.cs builds the " +
                "dispatcher from subsystems.OfType<IProvidesDebugSurface>(), so this subsystem " +
                "contributes NOTHING: GET /capabilities reports providers=[] and matrix={} for its mode, " +
                "and every world/gizmo/entity route answers as though the host held nothing — even when " +
                "it holds a world, an entity map and a gizmo buffer. That is not a missing capability, it " +
                "is an instrument reporting ABSENT where the truth is PRESENT (CE-259am).");

            Assert.True(src.Contains("CreateDebugProvider"),
                $"{subsystemPath} declares IProvidesDebugSurface but has no CreateDebugProvider body.");

            // ⛔⛔ READ THE ARGUMENT, NOT THE FILE. 📌 The first cut of this assertion was
            //    `src.Contains($"\"{perspective}\"")` and its inverse-edit red-proof STAYED GREEN: every
            //    one of these subsystems also writes `public string Name => "<Perspective>"`, so the
            //    literal was satisfied by the NAME property while `perspective:` named something else
            //    entirely. ⇒ exactly the blindness CLAUDE.md records twice (a fully-qualified
            //    `new …EntityRotatorGizmo(` keeping a rail green since CE-051; `new FdpEventBus()` in
            //    CE-260). ⭐ Fixed IN PLACE, and it reddens now.
            // ⭐ EVERY occurrence, not just the provider's: these files also pass `perspective:` to window
            //   registration helpers, and all of them must agree — measured 2026-09-11, CGF and SimHost
            //   each have two and both match.
            int at = 0, seen = 0;
            while ((at = src.IndexOf("perspective:", at, System.StringComparison.Ordinal)) >= 0)
            {
                var after = src[(at + "perspective:".Length)..].TrimStart();
                Assert.True(after.StartsWith($"\"{perspective}\"", System.StringComparison.Ordinal),
                    $"{subsystemPath} passes a `perspective:` argument that is not \"{perspective}\" — " +
                    "the perspective DESIGN_Perspective_Unification.md §1b says it owns (found: " +
                    $"{after[..System.Math.Min(40, after.Length)]}). PerspectiveScopedDispatcher.Resolve " +
                    "matches providers on that exact string against what the window manager reports, so a " +
                    "mismatch is indistinguishable at runtime from contributing no provider at all: every " +
                    "route answers NOT_SUPPORTED_HERE while the host holds everything it needs.");
                seen++;
                at += "perspective:".Length;
            }

            Assert.True(seen > 0, $"{subsystemPath} passes no `perspective:` argument at all.");
        }

        /// <summary>
        /// ⭐⭐ <b>The <c>CE-162</c> theory's twin for the MAP FEED</b> — <c>gizmoBuffer</c> is the member
        /// whose absence <c>CE-259am</c> was actually measured through, and the <c>entityMap</c> theory
        /// above could not cover it.
        ///
        /// <para>⛔ Deliberately narrow, exactly as the <c>entityMap</c> row is: it lists the subsystems
        /// measured to HOLD a buffer. ⭐ <c>ExCon</c> is absent and that is correct — it builds none, so
        /// <c>panels.gizmo</c> is honestly false for its perspective *(ruling 49:
        /// absent-and-explained beats present-and-broken)*.</para>
        /// </summary>
        public static TheoryData<string> SubsystemsWithAGizmoBuffer() => new()
        {
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            "Hrot/Subsystems/Hrot.IG/IgSubsystem.cs",
            "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs",
            "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs",
        };

        [Theory]
        [MemberData(nameof(SubsystemsWithAGizmoBuffer))]
        public void ASubsystemHoldingAGizmoBuffer_PassesItToItsDebugProvider(string subsystemPath)
        {
            var src = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(subsystemPath));

            // ⛔ Anti-vacuity: if the subsystem stops driving a buffer, this row asserts nothing and must
            //   be re-examined rather than passing quietly.
            Assert.True(src.Contains("DebugPrimitiveBuffer") || src.Contains("GizmoBuffer")
                        || src.Contains("gizmoBuffer"),
                $"{subsystemPath} no longer mentions a debug primitive buffer, so this row cannot assert " +
                "anything. Either it genuinely stopped drawing gizmos (drop the row and say why) or the " +
                "rail is aimed at the wrong file.");

            Assert.Contains("gizmoBuffer:", src);

            var afterKey = src[(src.IndexOf("gizmoBuffer:", System.StringComparison.Ordinal)
                                + "gizmoBuffer:".Length)..].TrimStart();

            Assert.False(afterKey.StartsWith("null", System.StringComparison.Ordinal),
                $"{subsystemPath} passes `gizmoBuffer: null` while it drives a DebugPrimitiveBuffer. " +
                "SubsystemDebugProvider computes panels.gizmo from the member being non-null, so " +
                "GET /panels/_gizmo and POST /annotations will refuse for this perspective — and the " +
                "refusal reads as 'this host draws no gizmos', which would be false. Forward the buffer " +
                "(BP-487, CE-259am).");
        }

        /// <summary>
        /// ⛔⛔ <b>Anti-vacuity for both theories above, and it is the load-bearing half:</b> they are TEXT
        /// checks on composition files, so the seam they assert against must still be the seam the runner
        /// reads. ⇒ if <c>Program.cs</c> stops selecting providers by <c>IProvidesDebugSurface</c>, or
        /// <c>Resolve</c> stops matching on <c>Perspective</c>, five green rows would assert nothing.
        /// </summary>
        [Fact]
        public void TheRunnerSelectsProvidersByTheSeamTheseRailsAssert()
        {
            var program = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "Hrot/Runner/Hrot.ClusterRunner/Program.cs"));

            Assert.Contains("OfType<Hrot.Presentation.DebugApi.IProvidesDebugSurface>()", program);
            Assert.Contains("CreateDebugProvider()", program);

            var dispatcher = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "Hrot/Engine/Hrot.Presentation/DebugApi/PerspectiveScopedDispatcher.cs"));

            // ⭐ The match is on the provider's Perspective string — what the third assertion above pins.
            Assert.Contains("p.Perspective, perspective", dispatcher);

            var provider = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "Hrot/Engine/Hrot.Presentation/DebugApi/ISubsystemDebugProvider.cs"));

            // ⭐ And panels.gizmo is measured from the member, which is why forwarding it matters.
            Assert.Contains("DebugCapabilities.GizmoFrame] = GizmoBuffer is not null", provider);
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

            // ⭐⭐ CE-260 — QUALIFICATION-TOLERANT. This was `src.Contains("new FdpEventBus()")`, which a
            //    file writing `new Fdp.Core.FdpEventBus()` walks straight past — and because the
            //    assertion is NEGATIVE it would have gone SILENTLY GREEN over the very defect it forbids.
            //    📌 Exactly the blindness the "H - ui" session found on their own rail (CE-259), where a
            //    fully-qualified `new Hrot.ScenarioEditor.Gizmos.EntityRotatorGizmo(` had kept a rail
            //    green over a duplicate since CE-051.
            Assert.False(CompositionRootSource.ConstructsType(src, "FdpEventBus"),
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
