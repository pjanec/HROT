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
        var repoRoot  = RepoRoot();

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
    /// ⭐⭐⭐ <b>And the two composition roots must still resolve their THREE roots</b> — ⛔ otherwise the
    /// scan above is satisfied by a host that resolves nothing at all, which is the vacuous pass a
    /// forbid-only rail invites. 📌 <c>CE-064</c>'s shape: an assertion that is correct, universal and
    /// unreachable.
    /// </summary>
    [Theory]
    [InlineData("EditorSubsystem.cs")]
    [InlineData("CgfSubsystem.cs")]
    public void EachCompositionRootResolvesAllThreeKindsThroughTheResolver(string fileName)
    {
        var text = File.ReadAllText(TheFile(fileName));

        // ⚠ CGF routes all three through one `RootFor(kind)` local, the editor names each kind — so the
        //   assertion is on the CALL, not on how many times it appears.
        Assert.Contains("ResolveAssetsRoot(", text);

        foreach (var kind in new[] { "Blueprint", "BTree", "Hsm" })
            Assert.True(
                text.Contains($"AssetKind.{kind}", StringComparison.Ordinal),
                $"{fileName} no longer mentions AssetKind.{kind}; this rail can no longer see its roots.");
    }

    /// <summary>
    /// ⚠ <b>The rail's own red-proof.</b> ⛔ A scan over an empty or wrong directory passes vacuously. ⭐ So
    /// the tree must be reachable, the one implementation must be in it, and it must itself match the
    /// forbidden pattern — otherwise the regex has rotted and this rail means nothing.
    /// </summary>
    [Fact]
    public void TheScanActuallyReachesTheSourceTreeAndTheRegexStillMatches()
    {
        var repoRoot = RepoRoot();

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
                                    StringComparison.OrdinalIgnoreCase));

    private static string TheFile(string fileName)
    {
        var hit = ProductionSources(RepoRoot()).FirstOrDefault(f => Path.GetFileName(f) == fileName);
        Assert.True(hit != null, $"{fileName} not found in the source tree — this rail cannot run.");
        return hit!;
    }

    /// <summary>
    /// ⭐ Found through <see cref="AssetRoots.ResolveProjectDir"/> — ⛔ fails loudly rather than skipping: a
    /// rail that opts out when it cannot find the tree reports green forever.
    /// </summary>
    private static string RepoRoot()
    {
        var projectDir = AssetRoots.ResolveProjectDir(
            "Hrot", "Editor", "Hrot.Editor.AiShared", "Hrot.Editor.AiShared.csproj");

        Assert.True(projectDir != null,
            "Could not locate the source tree from either the working directory or the output " +
            "directory. This rail scans sources and cannot run without them.");

        // …/Hrot/Editor/Hrot.Editor.AiShared → up three.
        return Path.GetFullPath(Path.Combine(projectDir!, "..", "..", ".."));
    }
}
