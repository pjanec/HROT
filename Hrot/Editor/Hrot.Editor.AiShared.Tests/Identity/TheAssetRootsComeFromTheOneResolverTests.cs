using Fdp.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-093</c> / <c>CE-094</c> (<c>J1</c>) — every asset root comes from
/// <see cref="AssetRoots.ResolveAssetsRoot"/> / <see cref="AssetRoots.RecipesFor"/>, and NOT from a
/// hand-combined base.</b>
///
/// <para>📐 <b>Measured <c>2026-08-27</c>, three production sites.</b> Each combined its own idea of a base
/// with <see cref="AssetRoots.AssetsRelative"/> or <see cref="AssetRoots.RecipesRelative"/>:</para>
/// <list type="bullet">
///   <item><c>EditorSubsystem</c> — twice, over <c>ResolveProjectDir</c>: ⛔ <b>the walk-up ONLY</b>, which
///   answers <see langword="null"/> when there is no source tree ⇒ on a deployed node the editor's two JSON
///   roots went null and it loaded none of its own BTree/HSM JSON assets. ⚠ Its Blueprint root, three
///   lines above, already used the shared resolver — ⇒ <b>a split brain inside ONE host</b>.</item>
///   <item><c>CgfSubsystem</c> — a <c>RootFor</c> local function that was <c>ResolveAssetsRoot</c> spelled
///   out. ⭐ Behaviourally right, ⛔ ruling 9 wrong: it is the copy that let the editor drift.</item>
///   <item><c>BlueprintEditorBootstrap.DiscoverRecipes</c> — over the AI.Behaviors <b>assembly
///   directory</b> ⇒ a configured node listed blueprint ASSETS from its configured tree and blueprint
///   RECIPES from the bin directory. 📌 A member ruling 67's own sweep missed.</item>
/// </list>
///
/// <para>⭐⭐ <b>Why a SOURCE SCAN, stated plainly.</b> ⛔ The claim is a WIRING claim — <i>"the hosts use
/// the shared resolver"</i> — and both composition roots resolve their roots into private fields inside
/// <c>Initialize</c>, with no seam to inject or read. ⇒ ⚠ the behavioural half is already covered
/// (<c>TheDeployedNodeFindsItsAssetsTests.EveryRootMemberAgreesWithTheConfiguredRoot</c> proves the
/// resolver honours the config); what NO behavioural rail can see is a host that quietly stops calling it.
/// ⭐ Precedent and shape: <see cref="TheWalkUpHasOneImplementationTests"/>, including its anti-vacuity
/// fact — a scan that reaches nothing passes forever.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.13.</para>
/// </summary>
public sealed class TheAssetRootsComeFromTheOneResolverTests
{
    /// <summary>⭐ The one file allowed to combine a base with a relative segment — it IS the resolver.</summary>
    private const string TheOneImplementation = "AssetRoots.cs";

    /// <summary>
    /// ⭐ The shape: <c>Path.Combine(&lt;anything&gt;, …AssetsRelative(…))</c> — a caller supplying its own
    /// base. ⛔ A bare <c>AssetsRelative</c> call is fine and is not matched: relative segments are used
    /// legitimately (a golden's expected path, a log line).
    /// </summary>
    private static readonly Regex HandCombinedRoot = new(
        @"Path\.Combine\([^;]*?(AssetsRelative|RecipesRelative|ScenariosRecipesRelative)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// ⚠⚠ <b>COMMENT LINES ARE STRIPPED, and finding that out was the rail's first useful act.</b>
    ///
    /// <para>📐 On its first run this scan flagged all three files <c>J1</c> had <b>just fixed</b> — because
    /// each fix carries a comment QUOTING the code it replaced (<i>"what was here:
    /// <c>Path.Combine(assemblyLocation, RecipesRelative(Blueprint))</c>"</i>). ⇒ ⛔ a text scan cannot tell
    /// a violation from a description of one, and this codebase documents its removals on purpose.</para>
    ///
    /// <para>⚠ Whole comment lines only — a trailing <c>// …</c> after real code keeps its code. ⭐ Stated
    /// so nobody reads a green as <i>"no comment mentions the old shape"</i>: it means no <b>executable</b>
    /// line has it.</para>
    /// </summary>
    private static string StrippedOfCommentLines(string text)
        => string.Join('\n', text.Split('\n').Where(line =>
        {
            var t = line.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("*",  StringComparison.Ordinal)
                && !t.StartsWith("/*", StringComparison.Ordinal);
        }));

    [Fact]
    public void NoProductionFileCombinesItsOwnBaseWithAnAssetSegment()
    {
        var offenders = new List<string>();
        var repoRoot  = TestRepo.Root;

        foreach (var file in ProductionSources(repoRoot))
        {
            if (Path.GetFileName(file) == TheOneImplementation) continue;

            var text = StrippedOfCommentLines(File.ReadAllText(file));
            if (!HandCombinedRoot.IsMatch(text)) continue;

            offenders.Add(Path.GetRelativePath(repoRoot, file));
        }

        Assert.Equal(new List<string>(), offenders.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// ⭐ <b>CE-4089 — no content path is spelled outside <c>ContentLayout</c> or read from a project/exe folder.</b> The old shapes: a path into the Behaviors
    /// project (<c>"Hrot.AI.Behaviors", "Assets"|"Recipes"</c>), an exe-folder content root (<c>AppContext.BaseDirectory, "Recipes"|"Assets"|"assets"</c>) and the
    /// shipped-recipes tree (<c>"Recipes", "Terrain"|"Buildings"|…</c>). ⛔ Comment lines are not scanned (see <see cref="StrippedOfCommentLines"/>).
    /// </summary>
    private static readonly Regex OldContentPath = new(
        @"""Hrot\.AI\.Behaviors""\s*,\s*""(Assets|Recipes)""|AppContext\.BaseDirectory\s*,\s*""(Recipes|Assets|assets)""|""Recipes""\s*,\s*""(Terrain|Buildings|Scenarios|Blueprints)""",
        RegexOptions.Compiled);

    [Fact]
    public void NoProductionFileSpellsAnOldContentPath()
    {
        var offenders = new List<string>();
        var repoRoot  = TestRepo.Root;

        foreach (var file in ProductionSources(repoRoot))
            if (OldContentPath.IsMatch(StrippedOfCommentLines(File.ReadAllText(file))))
                offenders.Add(Path.GetRelativePath(repoRoot, file));

        Assert.Equal(new List<string>(), offenders.OrderBy(x => x, StringComparer.Ordinal).ToList());

        // ⚠ anti-vacuity: the regex still matches each old shape (a rotted pattern would pass forever)
        Assert.Matches(OldContentPath, @"Path.Combine(root, ""Hrot"", ""Subsystems"", ""Hrot.AI.Behaviors"", ""Recipes"", ""Terrain"")");
        Assert.Matches(OldContentPath, @"Path.Combine(AppContext.BaseDirectory, ""Recipes"", ""Terrain"")");
        Assert.Matches(OldContentPath, @"Path.Combine(AppContext.BaseDirectory, ""assets"", ""audio"")");
    }

    /// <summary>
    /// ⭐ <b>CE-3340 (R-3066) — no production file spells a content root of the NEW tree by hand</b>: <c>Path.Combine(…, "assets"|"recipes"|"scenarios"|"opened")</c>
    /// or a <c>"assets/x"</c>-style literal. ⛔ The rail above only bans the OLD shapes; this one keeps the new tree from growing a second spelling beside
    /// <c>ContentLayout</c> / <c>ContentTreeNames</c> (the one authority) and <c>OrchestrationConstants</c> (whose names ContentLayout reuses).
    /// ⭐ <c>FDP/ExtDeps/</c> is excluded: a vendored project (SumoSharp) with its OWN <c>scenarios/</c> folder, not this content tree.
    /// </summary>
    private static readonly Regex HandSpelledNewContentRoot = new(
        @"Path\.Combine\([^)]*""(assets|recipes|scenarios|opened)""|""(assets|recipes|opened)/[A-Za-z]",
        RegexOptions.Compiled);

    private static readonly string[] TheContentAuthorities = { "ContentLayout.cs", "ContentTreeNames.cs", "OrchestrationConstants.cs" };

    [Fact]
    public void NoProductionFileSpellsANewContentRootOutsideContentLayout()
    {
        var offenders = new List<string>();
        var repoRoot  = TestRepo.Root;
        var extDeps   = Path.Combine("FDP", "ExtDeps") + Path.DirectorySeparatorChar;

        foreach (var file in ProductionSources(repoRoot))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            if (rel.StartsWith(extDeps, StringComparison.Ordinal)) continue;
            if (TheContentAuthorities.Contains(Path.GetFileName(file))) continue;
            if (HandSpelledNewContentRoot.IsMatch(StrippedOfCommentLines(File.ReadAllText(file))))
                offenders.Add(rel);
        }

        Assert.Equal(new List<string>(), offenders.OrderBy(x => x, StringComparer.Ordinal).ToList());

        // ⚠ anti-vacuity: the pattern still matches each new-tree shape, and not the runner's help text ("scenarios/, assets/ and recipes/").
        Assert.Matches(HandSpelledNewContentRoot, @"Path.Combine(nas, ""assets"", ""terrain"")");
        Assert.Matches(HandSpelledNewContentRoot, @"Path.Combine(root, ""opened"")");
        Assert.Matches(HandSpelledNewContentRoot, @"var p = ""recipes/scenarios"";");
        Assert.DoesNotMatch(HandSpelledNewContentRoot, @"""scenarios/, assets/ and recipes/).""");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The ANTI-VACUITY half: something must still resolve all THREE roots</b> — ⛔ otherwise the
    /// forbid-scan above is satisfied by a tree that resolves nothing at all. 📌 <c>CE-064</c>'s shape:
    /// an assertion that is correct, universal and unreachable.
    ///
    /// <para>⚠⚠ <b>RE-POINTED <c>2026-09-26</c> (<c>CE-342</c>), and the distinction matters:</b> this is
    /// a rail whose SETUP no longer produces its situation, <b>not</b> an assertion that weakened.
    /// 🔴 It used to read each composition root's source, because each host resolved its own three roots.
    /// ⭐ <c>CE-342</c> moved that into ONE shared <c>AiAssetCatalogComposer</c>, so CGF legitimately
    /// stopped calling <c>ResolveAssetsRoot</c> — the very drift the rail exists to prevent is now
    /// structurally impossible rather than merely checked. ⇒ the guard moves to the composer, and a
    /// SECOND rail proves both hosts reach it, which together are STRICTLY STRONGER than the original.
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.19.</para>
    /// </summary>
    [Fact]
    public void TheSharedComposerResolvesAllThreeKindsThroughTheResolver()
    {
        var text = File.ReadAllText(TheFile("AiAssetCatalogComposer.cs"));

        Assert.Contains("ResolveAssetsRoot(", text);

        foreach (var kind in new[] { "Blueprint", "BTree", "Hsm" })
            Assert.True(
                text.Contains($"AssetKind.{kind}", StringComparison.Ordinal),
                $"AiAssetCatalogComposer no longer mentions AssetKind.{kind}; this rail cannot see its roots.");
    }

    /// <summary>
    /// ⭐⭐ <b>…and BOTH hosts must actually reach it.</b> ⛔ Without this, the composer could resolve all
    /// three roots impeccably while a host quietly hand-rolled its own catalogue again — which is the
    /// same vacuous pass in a new place.
    /// </summary>
    [Theory]
    [InlineData("EditorSubsystem.cs")]
    [InlineData("CgfSubsystem.cs")]
    public void EachCompositionRootComposesItsCatalogThroughTheSharedComposer(string fileName)
    {
        var text = StrippedOfCommentLines(File.ReadAllText(TheFile(fileName)));

        Assert.True(
            text.Contains("AiAssetCatalogComposer.Compose(", StringComparison.Ordinal),
            $"{fileName} no longer composes its asset catalogue through the shared composer.");
    }

    /// <summary>
    /// ⚠ <b>The rail's own red-proof.</b> ⛔ A scan over an empty or wrong directory passes vacuously. ⭐ So
    /// the tree must be reachable, the one implementation must be in it, and it must itself match the
    /// forbidden pattern — otherwise the regex has rotted and this rail means nothing.
    /// </summary>
    [Fact]
    public void TheScanActuallyReachesTheSourceTreeAndTheRegexStillMatches()
    {
        var repoRoot = TestRepo.Root;

        var files = ProductionSources(repoRoot).ToList();
        Assert.True(files.Count > 500, $"only {files.Count} production sources found — the scan is not reaching the tree");

        var assetRoots = files.FirstOrDefault(f => Path.GetFileName(f) == TheOneImplementation);
        Assert.NotNull(assetRoots);
        Assert.Matches(HandCombinedRoot, StrippedOfCommentLines(File.ReadAllText(assetRoots!)));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static IEnumerable<string> ProductionSources(string repoRoot)
        => Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                     && !f.Contains(Path.DirectorySeparatorChar + "examples" + Path.DirectorySeparatorChar,
                                    StringComparison.OrdinalIgnoreCase)
                     // ⭐ CE-3340 — never a hidden directory: `.claude/worktrees/*` holds other checkouts (agents' worktrees, old commits) whose
                     //   sources are not this tree's production code; 📌 measured 2026-10-29, both content rails read a paused worktree's files.
                     && !Path.GetRelativePath(repoRoot, f).Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')));

    private static string TheFile(string fileName)
    {
        var hit = ProductionSources(TestRepo.Root).FirstOrDefault(f => Path.GetFileName(f) == fileName);
        Assert.True(hit != null, $"{fileName} not found in the source tree — this rail cannot run.");
        return hit!;
    }

}
