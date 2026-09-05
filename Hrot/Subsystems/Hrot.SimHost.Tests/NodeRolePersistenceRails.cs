using System.Collections.Generic;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>R-140</c> — a node role carries a PERSISTENCE convention, and IG's half of it is
    /// enforced by an ABSENCE. This turns that absence into a checked invariant.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Node_Roles_And_Policies.md</c> §7.1 (how the rule is actually enforced),
    /// §7.3 (what is measured), §8 ①b (this rail). 🔒 The ruling, from the user:
    /// <i>"IG not saving to scenario is as simple as not letting the IG subsystem handle the clusterwide
    /// scenario save operation."</i></para>
    ///
    /// <para>📐 <b>Why an absence needs a rail.</b> The cluster save fans out to EVERY active node with no
    /// role filter (<c>ClusterMaster.ProcessStorageOpIntent</c> → <c>_roster.ActiveNodes.Keys</c>), and a
    /// node with no matching handler answers <c>Success</c> with <c>IsParticipating = false</c> without
    /// stalling the 2PC (<c>ClusterSlave</c>'s no-handler path). ⇒ IG <b>is</b> asked and declines. ⛔ But
    /// <i>"IG registers no save handler"</i> is the SILENT-DEFAULT shape: nothing stops the next person
    /// adding one, and it would look like a feature rather than a regression.</para>
    ///
    /// <para>⚠⚠ <b>What this rail does NOT prove — stated so nobody over-trusts it.</b> §7.3 measures that
    /// an IG-created entity still <b>replicates into a saving node's world</b>, where
    /// <c>ScenarioSerializer.CollectSaveableEntities</c> filters on <c>ScenarioIgnoreTag</c> and nothing
    /// else. ⇒ <b>this rail proves IG does not write the file; it does NOT prove IG's sketches stay out
    /// of it.</b> That gap is §8 ①, and it is open.</para>
    /// </summary>
    public class NodeRolePersistenceRails
    {
        private const string IgRoot = "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs";

        /// <summary>
        /// ⭐ Every node-side handler type whose <c>CanHandle</c> claims <c>NodeOpType.SerializeLocal</c>.
        /// 📐 Enumerated <c>2026-09-02</c> over every non-test file implementing
        /// <c>IClusterStateHandler</c>/<c>IClusterOpHandler</c> that mentions <c>SerializeLocal</c>:
        /// <c>ReferenceArchiveHandler</c> (the node-side archive/serialize handler) and
        /// <c>GlobalContextClusterOpHandler</c> (the orchestrator's own context writer).
        /// ⛔ <see cref="TheSaveHandlerSetIsStillComplete"/> reddens when a new one appears, so this list
        /// cannot silently go stale — which is the failure mode of every hard-coded set in this repo.
        /// </summary>
        private static readonly string[] SaveCapableHandlers =
        {
            "ReferenceArchiveHandler",
            "GlobalContextClusterOpHandler",
        };

        /// <summary>
        /// 🔴🔴🔴 <b>IG MUST NOT HANDLE THE CLUSTER-WIDE SAVE.</b>
        ///
        /// <para>⭐ Asserted on the composition root's own registration set, because that — not a role
        /// flag — is where the decision actually lives.</para>
        /// </summary>
        [Fact]
        public void IgCompositionRoot_RegistersNoSaveCapableClusterHandler()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(IgRoot));

            foreach (var handler in SaveCapableHandlers)
            {
                Assert.False(code.Contains(handler),
                    $"{IgRoot} registers {handler}, which handles NodeOpType.SerializeLocal. IG is a " +
                    "PASSIVE, NON-PERSISTING node (R-140): it may create only temporary entities, and if " +
                    "it disappears they are gone and nobody cares. Letting it answer the cluster-wide " +
                    "save makes an IG's transient state part of the scenario every other node reloads. " +
                    "If this is deliberate, the ruling has to change first — see " +
                    "docs/DESIGN_Node_Roles_And_Policies.md §5 and §7.1.");
            }
        }

        /// <summary>
        /// 🔴🔴 <b>IG MUST NOT EXTRACT ENTITIES FOR A SCENARIO EITHER.</b>
        ///
        /// <para>⭐ The save is two capabilities, not one: <i>answering the operation</i> and <i>turning a
        /// world into scenario JSON</i>. A root could gain the second without the first — e.g. an
        /// editor-style local save — and §5's rule would be broken just as thoroughly.</para>
        /// </summary>
        [Fact]
        public void IgCompositionRoot_WiresNoScenarioEntityExtractor()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(IgRoot));

            foreach (var extractor in new[] { "StagingEntityExtractor", "IScenarioEntityExtractor" })
            {
                Assert.False(code.Contains(extractor),
                    $"{IgRoot} wires {extractor}. IG carries no persistent state (R-140) and must not be " +
                    "able to turn its world into scenario entities. See " +
                    "docs/DESIGN_Node_Roles_And_Policies.md §7.1.");
            }
        }

        /// <summary>
        /// ⚠⚠ <b>NON-VACUITY — the rails above are two <c>Assert.False(contains)</c> calls, which is the
        /// single easiest assertion in this codebase to make pass for the wrong reason.</b>
        ///
        /// <para>📌 Rename <c>ReferenceArchiveHandler</c>, move IG's registrations behind a factory, or
        /// mistype a path, and both go green while saying nothing. ⇒ this rail proves the tokens are real
        /// by finding them where they SHOULD be: <c>CgfApplication</c> and <c>ExConSubsystem</c> both
        /// register <c>ReferenceArchiveHandler</c> — measured <c>2026-09-02</c> at
        /// <c>CgfApplication.cs:227</c> and <c>ExConSubsystem.cs:305</c>.</para>
        ///
        /// <para>⭐ This is the <c>CE-049</c>/<c>CE-053</c>/<c>CE-064</c> rail-blindness family: a correct
        /// assertion over a set that quietly became unreachable.</para>
        /// </summary>
        [Theory]
        [InlineData("Hrot/Subsystems/Hrot.CGF/CgfApplication.cs")]
        [InlineData("Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs")]
        public void APersistingRootDoesRegisterTheArchiveHandler(string relativePath)
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(relativePath));

            Assert.Contains("ReferenceArchiveHandler", code);
        }

        /// <summary>
        /// ⚠ <b><see cref="SaveCapableHandlers"/> is a hard-coded set, and a hard-coded set rots.</b>
        ///
        /// <para>⭐ Re-derives it from source: every non-test file that implements a cluster handler
        /// interface AND names <c>SerializeLocal</c>. A NEW save handler reddens this rail, which forces
        /// a decision about whether IG may hold it — rather than letting it slip past a stale list.</para>
        /// </summary>
        [Fact]
        public void TheSaveHandlerSetIsStillComplete()
        {
            var repoRoot = FindRepoRoot();
            var found    = new SortedSet<string>();

            foreach (var file in System.IO.Directory.EnumerateFiles(repoRoot, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                var normalised = file.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                if (normalised.Contains("/obj/") || normalised.Contains("/bin/")) continue;
                if (normalised.Contains(".Tests/") || normalised.Contains(".Tests.")) continue;

                var text = System.IO.File.ReadAllText(file);
                if (!text.Contains("IClusterStateHandler") && !text.Contains("IClusterOpHandler")) continue;
                if (!text.Contains("SerializeLocal")) continue;

                found.Add(System.IO.Path.GetFileNameWithoutExtension(file));
            }

            Assert.Equal(
                new SortedSet<string>(SaveCapableHandlers),
                found);
        }

        private static string FindRepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "docs")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    
        // ── CE-197: the declared role must CARRY what the host composes ──────────

        private const string SimHostRoot    = "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs";
        private const string SimHostApp     = "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs";
        private const string SimHostSubsys  = "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs";

        /// <summary>
        /// ⭐⭐⭐ <b>CE-197 — SimHost DECLARES <c>NavigationSolver</c>, because it composes the navigation
        /// solver.</b>
        ///
        /// <para>📐 The measurement that forced this rail: <c>SimHostSubsystem</c> declared
        /// <c>MuscleGround | Perception</c> — <b>without</b> <c>NavigationSolver</c> — while
        /// <c>SimHostNodeBootstrapper</c> composed all three from a hard-coded constant. Harmless while
        /// the constant existed; the moment the capability set is resolved FROM the declared role
        /// (<c>B4b</c> step 3, which this change made), an under-declared role SILENTLY DROPS
        /// <c>EngineBackedNavigationModule</c> and <c>EqsModule</c>.</para>
        ///
        /// <para>⚠ The failure would be silent in the worst way: the node boots, the plan verifies, and
        /// pathfinding is simply absent. That is why the invariant is pinned rather than trusted to the
        /// comment beside it.</para>
        /// </summary>
        [Fact]
        public void SimHostDeclaresTheNavigationSolverRoleItComposes()
        {
            var app = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(SimHostApp));

            // The ONE declaration carries the solver flag …
            Assert.Contains("const NodeRole DefaultRole", app);
            Assert.Contains("NodeRole.NavigationSolver", app);

            // … and the other two sites REFERENCE it rather than restating it. 📐 There were three
            // copies (field, ctor default, subsystem) and the ctor default is the one a careful edit
            // misses — it is what reddened five SimHost boot tests before the constant existed.
            var subsystem = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(SimHostSubsys));

            Assert.Contains("SimHostApp.DefaultRole", subsystem);
            Assert.DoesNotContain("NodeRole.MuscleGround | NodeRole.Perception", subsystem);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>CE-197 — the capability set is resolved from the DECLARED role, never from a constant.</b>
        ///
        /// <para>⛔ A re-introduced <c>const NodeRole composed = …</c> would make the role flags decorative
        /// again and quietly re-open the gap this change closed — the host would compose correctly while
        /// its declaration said something else, which is how the divergence above survived unnoticed.</para>
        /// </summary>
        [Fact]
        public void TheComposedRoleIsNotHardCoded()
        {
            var src = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(SimHostRoot));

            Assert.DoesNotContain("const NodeRole composed", src);
            Assert.Contains("NodeRole composed = _role", src);
        }

        /// <summary>
        /// ⭐⭐ <b>CE-197 — the node FREES the resource providers it resolved.</b>
        ///
        /// <para>📐 <c>TrajectoryPool</c>'s own remarks claimed <i>"this bootstrapper disposes the
        /// provider, which nothing did before"</i> — measured FALSE: the class had no <c>Dispose</c> at
        /// all and nothing called <c>TrajectoryPoolProvider.Dispose</c> anywhere in production, so every
        /// node leaked its <c>TrajectoryPoolManager</c>. ⚠ A documented ownership claim that nothing
        /// implements is worse than an undocumented gap, because the next reader stops looking.</para>
        /// </summary>
        [Fact]
        public void TheNodeFreesTheProvidersItResolved()
        {
            var src = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(SimHostRoot));

            Assert.Contains("DisposeResources", src);
            Assert.Contains("provider.Dispose()", src);
        }
}
}
