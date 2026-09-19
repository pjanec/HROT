using System;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b>The rail <c>CE-053</c> should have been.</b>
///
/// <para>🔴🔴 <b>Why this file exists — and it is an indictment of my own previous rail.</b>
/// <c>TheCgfPickerIsNotEmptyTests</c> created a temp directory, dropped two scenarios in it, handed that
/// path to the contributor, and asserted the picker listed two entries. 📐 Every assertion was true and
/// green — and the picker was STILL EMPTY in <c>--mode all</c>, because the defect was never in the
/// chain. It was in <b>which root the host hands the chain</b>: CGF resolved
/// <c>{staging}/nodes/node-N/scenarios</c> (a directory that does not exist — the node directory holds
/// only <c>recording_ledger</c>) while the editor resolved <c>{staging}/shared/scenarios</c> (where the
/// three authored scenarios actually live).</para>
///
/// <para>⇒ ⭐⭐ <b>The lesson, stated so the next rail inherits it:</b> a rail that SUPPLIES the input it
/// is testing cannot catch a caller that supplies a different one. ⛔ *"the chain works given a populated
/// root"* is a strictly weaker claim than *"the host points at the populated root"* — the same
/// weaker/stronger split that let <c>CE-049</c>'s equality rail stay green while the picker was empty.
/// ⭐ These rails assert the ROOT, with no path of their own.</para>
/// </summary>
public sealed class TheHostsAgreeOnTheScenarioRootTests
{
    /// <summary>
    /// ⭐⭐⭐ <b>The root every host lists scenarios from is ONE path.</b> ⚠ Asserted against
    /// <c>ClusterConfiguration</c>'s value — the editor's own source via
    /// <c>EditorBootstrap.ScenariosRoot</c> — so the two cannot drift apart silently again.
    /// </summary>
    [Fact]
    public void TheSharedScenariosRootIsTheRootTheEditorLists()
    {
        var fromToolkit = OrchestrationConstants.GetSharedScenariosRoot();
        var fromEditor  = EditorBootstrap.ScenariosRoot;

        Assert.Equal(fromEditor, fromToolkit);
    }

    /// <summary>
    /// ⭐⭐ <b>The shared root is NOT a node staging root</b> — the exact confusion <c>CE-053</c> made.
    /// ⚠ Stated as an inequality rather than a literal so it survives a staging-root relocation.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void TheSharedScenariosRootIsNotANodeScenariosRoot(int nodeId)
    {
        Assert.NotEqual(
            OrchestrationConstants.GetNodeScenariosRoot(nodeId),
            OrchestrationConstants.GetSharedScenariosRoot());
    }

    /// <summary>
    /// ⭐⭐ <b>The one literal <c>"shared"</c>.</b> 📐 <c>ClusterConfiguration.NasBasePath</c> is not
    /// reachable from <c>Hrot.CGF</c>, which is why CGF invented a second answer; its default now routes
    /// through the toolkit so both hosts read the same shape.
    /// </summary>
    [Fact]
    public void TheClusterNasBaseIsTheSharedRoot()
    {
        Assert.Equal(
            OrchestrationConstants.GetSharedRoot(),
            Hrot.Orchestrator.ClusterConfiguration.Default.NasBasePath);
    }

    // ══ S1b / V1 — THE TKB STAGING ROOT, THE SAME CLAIM FOR THE ARTIFACT DIRECTORY ════════════════
    //
    // ⭐⭐⭐ This file exists because two hosts disagreed about WHICH ROOT a chain is handed. The TKB
    //    artifact directory is the identical shape of question one layer over: the ORCHESTRATOR writes
    //    it and the NODE reads it, and until 2026-09-18 nobody wrote it at all (BP-550).
    // ⚠ Added HERE rather than in a new class, deliberately (T-1 ④): the claim is "two sides agree on a
    //    root", which is exactly what this suite is for.

    /// <summary>
    /// ⭐⭐⭐ <b><c>S1b</c> — the orchestrator's TKB destination and the node's TKB read path are the SAME
    /// DIRECTORY.</b>
    ///
    /// <para>📐 <b><c>V1</c> settled by measurement:</b> the node handler is constructed with a root that
    /// is ALREADY per-node — every host passes <c>{base}/nodes/node-N</c>
    /// (<c>SimHostApp.cs:362</c>, <c>CgfSubsystem.cs:612</c>, <c>OrchestratorSubsystem.cs:137</c>) — so
    /// composing <c>GetTkbStagingRoot</c> over it lands exactly where the orchestrator's
    /// <c>GetNodeTkbStagingRoot(base, nodeId)</c> writes. ⛔ Threading a node id into the handler, the
    /// plan's lean, would have double-applied the node segment.</para>
    ///
    /// <para>⚠ Asserted as a COMPOSITION, not a literal, so a staging-root relocation cannot break it
    /// while leaving the two sides silently different — the failure mode this whole file is about.</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(300)]
    public void TheOrchestratorsTkbDestinationIsTheNodesTkbReadPath(int nodeId)
    {
        var baseRoot = OrchestrationConstants.ResolveStagingRoot();

        // What the ORCHESTRATOR computes, from the shared base root plus a node id.
        var written = OrchestrationConstants.GetNodeTkbStagingRoot(baseRoot, nodeId);

        // What the NODE computes, from the per-node root its bootstrap already handed it.
        var perNodeRoot = OrchestrationConstants.GetNodeStagingRoot(baseRoot, nodeId);
        var read        = OrchestrationConstants.GetTkbStagingRoot(perNodeRoot);

        Assert.Equal(written, read);
    }

    /// <summary>
    /// ⭐⭐ The TKB root is BENEATH the node's staging root, not beside it — i.e. the node segment is
    /// applied exactly ONCE. ⛔ The specific way the plan's lean would have gone wrong.
    /// </summary>
    [Fact]
    public void TheTkbRootCarriesTheNodeSegmentExactlyOnce()
    {
        var baseRoot = OrchestrationConstants.ResolveStagingRoot();
        var tkb      = OrchestrationConstants.GetNodeTkbStagingRoot(baseRoot, 7);

        Assert.StartsWith(OrchestrationConstants.GetNodeStagingRoot(baseRoot, 7), tkb, StringComparison.Ordinal);
        Assert.EndsWith(OrchestrationConstants.TkbDirectoryName, tkb, StringComparison.Ordinal);

        // ⭐ "node-7" appears once. A double-applied segment is the failure this pins.
        var segment = OrchestrationConstants.GetNodeDirectoryName(7);
        var first   = tkb.IndexOf(segment, StringComparison.Ordinal);
        Assert.True(first >= 0, "the node segment is missing entirely");
        Assert.Equal(-1, tkb.IndexOf(segment, first + segment.Length, StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐ <c>S1a</c> — the directory name has ONE definition. ⚠ A source scan, because the defect being
    /// prevented is a hand-built literal somewhere else, which no runtime assertion can see.
    /// ⛔ Test fixtures are exempt: they legitimately build the layout they are asserting against.
    /// </summary>
    [Theory]
    // ⚠ RE-POINTED `2026-09-18`: the knowledge-base loader moved out of Hrot.SimHost and became a STEP
    //   in the shared load-phase chain (L3/L4a). The claim is unchanged — the directory name has ONE
    //   definition — only its home moved. 📄 docs/DESIGN_Cluster_Load_Phase.md §4.1c.
    [InlineData("Hrot.Core", "Services/LoadPhase/KnowledgeBaseLoadStep.cs")]
    public void TheTkbDirectoryNameIsNotBuiltByHand(string project, string file)
    {
        // ⚠ Hrot.Core lives under Hrot/Engine/, not Hrot/Subsystems/ — the resolver takes segments.
        var text = HostSource.ReadRelative("Hrot", "Engine", project, file);

        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // prose may cite it
            if (line.Contains("///", StringComparison.Ordinal)) continue;                // nor doc comments
            Assert.DoesNotContain("\"TKB\"", line);
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The composition guard: whichever root a host resolves, the ENUMERATOR run over it is the
    /// shared one.</b> ⚠ A source scan is necessary here — a host picking the wrong root is composition,
    /// invisible to reflection — and it is narrow on purpose: it asserts only that no host feeds a NODE
    /// scenarios root into the scenario contributor, which is the precise mistake made.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void NoHostFeedsANodeStagingRootToTheScenarioContributor(string project, string file)
    {
        var text = HostSource.Read(project, file);

        // ⭐ The contributor's argument is an enumeration over a root; a node root there is the bug.
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // ⭐ prose may cite it
            Assert.DoesNotContain("GetNodeScenariosRoot", line);
        }
    }

    /// <summary>
    /// ⭐ Sanity: the enumerator answers empty (⛔ never throws) for a root that does not exist — which is
    /// what the node root WAS, and is why the failure presented as an empty list rather than a crash.
    /// ⚠ This is the rail that explains why nothing logged: an absent root is a legitimate state.
    /// </summary>
    [Fact]
    public void AnAbsentRootEnumeratesEmptyRatherThanThrowing()
    {
        var absent = Path.Combine(Path.GetTempPath(), "cgf-absent-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(absent));
        Assert.Empty(ScenarioEnumeration.EnumerateRelPaths(absent));
    }
}

/// <summary>⭐ Shared helper — locates a host composition root's source from the test bin directory.</summary>
internal static class HostSource
{
    internal static string Read(string project, string file)
        => ReadRelative("Hrot", "Subsystems", project, file);

    /// <summary>⭐ For sources that are not directly under <c>Hrot/Subsystems/&lt;project&gt;/</c>.</summary>
    internal static string ReadRelative(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(new[] { dir!.FullName }.Concat(segments).ToArray());
        Assert.True(File.Exists(path), $"expected {path} to exist — the rail's target moved.");
        return File.ReadAllText(path);
    }
}
