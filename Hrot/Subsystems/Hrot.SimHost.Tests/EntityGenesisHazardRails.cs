using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>ACCEPTANCE ⑪ — the two DOUBLE-CONSUMPTION hazards, gated BEFORE the host that can trip
    /// them adopts the pack.</b>
    ///
    /// <para>📄 <c>DESIGN_Entity_Creation_Unification.md</c> §6 ⑪ · <c>Architect_Question_65</c> §5.6
    /// (<c>CE-144</c>) and its "THE ORDERING HAZARD" section.</para>
    ///
    /// <para>⭐⭐ <b>Why these are written now, while they are GREEN.</b> IG is the only host that can trip
    /// either, and it has not adopted the pack yet. A rail added <i>after</i> the adoption proves the
    /// adoption was right; a rail added <i>before</i> it makes the hazard impossible to introduce
    /// silently. 📌 This is the one place in the programme where the hazard is cluster-breaking and
    /// unverifiable locally — <c>hrot-ai-debug</c> has been down, so no claim about IG has been checked
    /// against a running cluster.</para>
    ///
    /// <para>🔴🔴 <b><c>CE-160</c> CORRECTS ACCEPTANCE ⑪'s OWN WORDING.</b> §6 ⑪ says the spawn hazard
    /// applies <i>"while any tool still publishes bus-level <c>SpawnEntityCommand</c>"</i> — ⛔ that is
    /// too weak, and retargeting IG's tools would NOT be sufficient. 📐 Measured:
    /// <c>CreateEntityRequestSystem</c> — which the pack itself constructs — publishes
    /// <c>SpawnEntityCommand</c> onto <c>repo.Bus</c> unconditionally at <b>two</b> sites (the root entity
    /// and each auto-spawned TKB child), and <c>SpawnEntityCommandEgressTranslator</c> reads that same
    /// bus. ⇒ ⭐⭐⭐ <b>a host holding both would forward every LOCALLY-created entity to the arbiter,
    /// which materialises it again — a double spawn caused by the PACK, not by any tool.</b> ⇒ the
    /// condition is unconditional: <b>spawn system and spawn-egress translator are mutually
    /// exclusive.</b></para>
    /// </summary>
    public class EntityGenesisHazardRails
    {
        /// <summary>
        /// ⭐ Every production composition root that materialises entities. ⛔ IG is deliberately IN this
        /// list even though it has not adopted the pack: that is the point — the rail must redden the
        /// moment it gains a spawn system without dropping its egress translator.
        /// </summary>
        public static TheoryData<string> CompositionRoots() => new()
        {
            "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
            "Hrot/Subsystems/Hrot.NodeComposition/StrideNodeBootstrapper.cs",
        };

        /// <summary>
        /// 🔴🔴🔴 <b>THE SPAWN HAZARD — no root may materialise locally AND forward the same bus event.</b>
        ///
        /// <para>📐 The two consumers of one event:
        /// <c>NetworkSpawningSystem.cs:92</c> <c>view.ReadManagedEvents&lt;SpawnEntityCommand&gt;()</c> and
        /// <c>SpawnEntityCommandEgressTranslator.cs:80</c> <c>_eventBus.ReadManaged&lt;SpawnEntityCommand&gt;()</c>.
        /// A host with both materialises the entity locally AND asks the arbiter to materialise it again,
        /// which replicates a ghost back. ⚠ <b>Loud and visible</b> — unlike its destroy twin below.</para>
        ///
        /// <para>⭐ The obtaining of a spawn system is matched through EITHER spelling — a direct
        /// <c>new NetworkSpawningSystem</c> or an <c>EntityCreationPack.Build</c>, which always yields one
        /// (that is acceptance ⑨). ⛔ Matching only the direct construction would go green the day a host
        /// adopts the pack, which is exactly when the hazard arrives.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(CompositionRoots))]
        public void NoRoot_HoldsBothTheSpawnSystemAndTheSpawnEgressTranslator(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            bool materialisesLocally =
                code.Contains("new NetworkSpawningSystem") || code.Contains("EntityCreationPack.Build");

            bool forwardsSpawnsToTheArbiter =
                code.Contains("SpawnEntityCommandEgressTranslator")
                || SeamYieldsTheSpawnEgressTranslator(code);

            Assert.False(materialisesLocally && forwardsSpawnsToTheArbiter,
                $"{relativePath} both MATERIALISES entities locally and FORWARDS SpawnEntityCommand to the " +
                "arbiter. Every locally-created entity would be spawned twice — once here, once by the " +
                "arbiter, which then replicates a ghost back. Note this is NOT only about tools: " +
                "CreateEntityRequestSystem (built by the pack) publishes SpawnEntityCommand on the same " +
                "bus the egress translator reads, so retargeting the tools does not make it safe. " +
                "Drop the spawn egress translator when this host adopts the pack.");
        }

        /// <summary>
        /// ⭐⭐ <b>Follows the factory SEAM to what it actually RETURNS.</b>
        ///
        /// <para>⚠⚠ <b>STRENGTHENED <c>2026-09-03</c>, and the previous version was a genuine false
        /// positive.</b> It matched the <i>call</i> <c>CreateIgEgressTranslators</c> in the root, on the
        /// reasoning that <i>"IG obtains its egress translators through the factory seam, never by naming
        /// the type — so the rail matches the SEAM."</i> ⛔ That conflates <b>asking a factory for
        /// translators</b> with <b>getting the spawn one</b>. 📌 Measured: after <c>F5</c> removed
        /// <c>SpawnEntityCommandEgressTranslator</c> from that factory method, the call site remained — so
        /// the moment IG scheduled the spawn system (<c>CE-144</c> option A) the rail reddened on a host
        /// that is now <b>correct</b>.</para>
        ///
        /// <para>⭐ Resolving the seam is strictly stronger in BOTH directions: it still reddens if the
        /// translator is ever put back into the factory's IG list, and it no longer punishes a root merely
        /// for using the seam. ⛔ Deliberately narrow — it resolves the ONE production factory this seam
        /// has; a second implementation would need naming here, and <see cref="TheEgressSeamIsResolvable"/>
        /// makes that a loud red rather than a silent pass.</para>
        /// </summary>
        private const string IgEgressFactoryPath =
            "Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs";

        private static bool SeamYieldsTheSpawnEgressTranslator(string rootCode)
        {
            if (!rootCode.Contains("CreateIgEgressTranslators")) return false;

            var factory = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(IgEgressFactoryPath));

            // The body of CreateIgEgressTranslators, up to the next method or the end of the file.
            int start = factory.IndexOf("CreateIgEgressTranslators", System.StringComparison.Ordinal);
            if (start < 0) return false;                       // TheEgressSeamIsResolvable catches this
            int next = factory.IndexOf("public ", start + 1, System.StringComparison.Ordinal);
            var body = next > start ? factory[start..next] : factory[start..];

            return body.Contains("SpawnEntityCommandEgressTranslator");
        }

        /// <summary>
        /// ⛔ The seam-resolution above is only as good as its ability to FIND the factory. If the file
        /// moves or the method is renamed, <see cref="SeamYieldsTheSpawnEgressTranslator"/> would answer
        /// <c>false</c> and the hazard rail would go vacuously green — the exact rail-blindness family
        /// (<c>CE-049</c>/<c>CE-053</c>/<c>CE-064</c>) the root-list rail below exists to prevent.
        /// </summary>
        [Fact]
        public void TheEgressSeamIsResolvable()
        {
            var factory = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(IgEgressFactoryPath));   // throws if it moved

            Assert.Contains("CreateIgEgressTranslators", factory);
        }

        /// <summary>
        /// 🔴🔴 <b>THE DESTROY HAZARD (<c>CE-144</c>) — and it fails SILENTLY, which is why it needs a rail
        /// more than its twin does.</b>
        ///
        /// <para>📐 <c>GhostDestructionSystem</c> does <c>Unregister</c> + an <b>immediate hard
        /// <c>DestroyEntity</c></b>; <c>NetworkSpawningSystem.ProcessDestroy</c> does
        /// <c>SetLifecycleState(TearDown)</c> + <c>_elm.BeginDestruction(...)</c>. Both consume the same
        /// bus event, and <b>either order is wrong</b>: ghost-first ⇒ <c>ProcessDestroy</c> finds nothing,
        /// logs, returns, and ELM teardown NEVER runs, so <c>EntityMaster</c> is never disposed on the
        /// wire and <b>other IGs keep the drawing as a zombie forever</b>; spawn-first ⇒ the hard delete
        /// rips the entity out mid-teardown and the handshake cannot complete.</para>
        ///
        /// <para>⚠⚠ <b>The symptom is the OPPOSITE of the spawn hazard's</b> — a double spawn is visible
        /// on your own screen; an undeleted entity is visible only on a PEER. ⇒ nobody would find this by
        /// running the node they changed.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(CompositionRoots))]
        public void NoRoot_HoldsBothTheSpawnSystemAndASecondDestroyConsumer(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            bool materialisesLocally =
                code.Contains("new NetworkSpawningSystem") || code.Contains("EntityCreationPack.Build");

            bool hasSecondDestroyConsumer = code.Contains("new GhostDestructionSystem");

            Assert.False(materialisesLocally && hasSecondDestroyConsumer,
                $"{relativePath} holds NetworkSpawningSystem AND GhostDestructionSystem. They consume the " +
                "same DestroyEntityCommand and either order is wrong: the ghost system hard-deletes so ELM " +
                "teardown never runs (EntityMaster is never disposed and PEERS keep a zombie), or teardown " +
                "starts and the hard delete rips the entity out mid-handshake. Once this host materialises " +
                "entities, GhostDestructionSystem must be DROPPED, not kept beside it (CE-144).");
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-141</c> — <b>NO COMPOSITION ROOT BUILDS A TKB TRANSLATOR LIST.</b>
        ///
        /// <para>🔒 <b>User ruling, <c>2026-09-03</c>:</b> <i>"entity creation needs to be unified. There
        /// should be nothing we give just to IG. every ECS nodes must use same TKB in same way using the
        /// same shared code."</i></para>
        ///
        /// <para>📐 <b>Why a rail rather than deleting the seam.</b> <c>.WithTranslators(...)</c> now has
        /// ZERO production callers — SimHost dropped it at <c>CE-140</c> step 3, IG at <c>CE-141</c> — but
        /// the builder extension itself is reachable and <c>GhostPromotionSystem</c>'s explicit-list arm is
        /// a legitimate API. ⛔ Deleting a public seam whose removal ripples into <c>Hrot.Network.NED</c> is
        /// a bigger change than the invariant needs, and <i>"no rush removals"</i> applies. ⇒ ⭐ gate the
        /// USE, keep the seam.</para>
        ///
        /// <para>⛔ <b>What breaking this costs, so the message can say it:</b> a host that passes its own
        /// list hands <c>GhostPromotionSystem</c> a SECOND, equal-but-distinct instance, which is exactly
        /// what <c>tkb-1/DESIGN.md</c> §6.3 (<i>"identical for all three systems within the same node"</i>)
        /// asks not to happen — and if that list is NARROWER it silently under-projects every entity the
        /// host spawns. 📄 <c>TkbTranslatorSet</c>'s own contract: <i>"Do NOT subtract from this list."</i></para>
        /// </summary>
        [Theory]
        [MemberData(nameof(CompositionRoots))]
        public void NoRoot_BuildsItsOwnTkbTranslatorList(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            Assert.False(code.Contains(".WithTranslators("),
                $"{relativePath} calls .WithTranslators(...). No ECS node may build its own TKB " +
                "projection list: EntityCreationPack composes ONE list from TkbTranslatorSet.Base() " +
                "(plus BasePlus/BaseWith for genuine ADDITIONS), hands that instance to the ELM and to " +
                "NetworkSpawningSystem, and GhostPromotionSystem reads it back off the ELM. Passing a " +
                "list here creates a second, distinct instance — and a narrower one under-projects every " +
                "entity this host spawns, silently. Express 'this host does not want X' by not " +
                "REGISTERING X in its component registry (CE-141).");
        }

        /// <summary>
        /// ⚠ <b>The rails above are only as good as their target list.</b> A composition root that is
        /// renamed or moved would make both Theories silently cover less, so the list is asserted to be
        /// non-empty and every entry must resolve — <c>ReadRepoSource</c> throws on a missing file rather
        /// than returning empty. 📌 The <c>CE-049</c>/<c>CE-053</c>/<c>CE-064</c> rail-blindness family is
        /// exactly this shape: a correct assertion over a set that became unreachable.
        /// </summary>
        [Fact]
        public void TheRootListIsNonEmptyAndEveryEntryResolves()
        {
            var roots = CompositionRoots();
            Assert.NotEmpty(roots);

            foreach (var row in roots)
            {
                var path = (string)row[0]!;
                var src  = CompositionRootSource.ReadRepoSource(path);   // throws if it moved
                Assert.False(string.IsNullOrWhiteSpace(src), $"{path} is empty.");
            }
        }
    }
}
