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
/// ⭐⭐⭐ <b><c>CE-018</c> — the <c>.csproj</c> walk-up has ONE implementation.</b>
///
/// <para>📌 Ruling 9 *("no keeping two implementations for the same concept")*. 📐 Measured
/// <c>2026-08-25</c>: <c>EditorSubsystem.cs</c> carried <b>two</b> hand-written copies of the walk-up —
/// a local <c>ResolveAiBehaviorsDir</c> for the asset catalog and an inline loop for the full-rebuild
/// build target — both line-for-line equivalent to
/// <see cref="AssetRoots.ResolveProjectDir"/>.</para>
///
/// <para>⭐⭐ <b>Why the duplicate MATTERED, beyond tidiness.</b> Both copies predate ruling 67, so neither
/// could see <see cref="AssetRoots.ConfiguredRoot"/>. ⇒ a deployed node that had been TOLD where its tree
/// lives was still walking up from the working directory to find it — 📌 the same split brain ruling 67's
/// own fix had to close in <c>AssetRoots</c>'s absolute-path members.</para>
///
/// <para>⚠ <b>A source scan, and it is honest about being one.</b> ⛔ It cannot see a walk-up written in
/// some other shape *(a recursive helper, a <c>DirectoryInfo.Parent</c> chain)*. ⭐ It catches the copy
/// that actually gets written — the <c>while (dir != null) { if (File.Exists(candidate)) …; dir =
/// GetDirectoryName(dir); }</c> loop — which is the one that appeared twice.</para>
/// </summary>
public sealed class TheWalkUpHasOneImplementationTests
{
    /// <summary>⭐ The one file allowed to contain the loop.</summary>
    private const string TheOneImplementation = "AssetRoots.cs";

    /// <summary>
    /// ⭐ The shape: a loop that reassigns its cursor to <c>Path.GetDirectoryName(dir)</c>. ⛔ Plain
    /// <c>GetDirectoryName</c> calls are everywhere and are not walk-ups — the ASSIGNMENT BACK is what
    /// makes it one.
    /// </summary>
    private static readonly Regex WalkUpStep = new(
        @"\b(\w+)\s*=\s*(System\.IO\.)?Path\.GetDirectoryName\(\s*\1\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void OnlyAssetRootsWalksUpLookingForACsproj()
    {
        var repoRoot = TestRepo.Root;

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (Path.GetFileName(file) == TheOneImplementation) continue;
            if (!IsProductionSource(file)) continue;

            var text = File.ReadAllText(file);
            if (!text.Contains(".csproj")) continue;
            if (!WalkUpStep.IsMatch(text)) continue;

            offenders.Add(Path.GetRelativePath(repoRoot, file));
        }

        Assert.Equal(new List<string>(), offenders.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// ⚠ <b>The rail's own red-proof.</b> ⛔ A scan that walked an empty or wrong directory would pass
    /// vacuously. ⭐ So: the tree must be there, and the ONE implementation must be found in it and must
    /// itself match the forbidden pattern — otherwise the regex has rotted and the rail means nothing.
    /// </summary>
    [Fact]
    public void TheScanActuallyReachesTheSourceTree()
    {
        var repoRoot = TestRepo.Root;

        var assetRoots = Directory
            .EnumerateFiles(repoRoot, TheOneImplementation, SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        Assert.NotNull(assetRoots);
        Assert.Matches(WalkUpStep, File.ReadAllText(assetRoots!));
    }

    /// <summary>
    /// ⭐⭐ <b>PRODUCTION only, and the exclusion is a scope statement rather than a convenience.</b>
    ///
    /// <para>📐 Measured <c>2026-08-25</c>: the remaining walk-ups live in <c>*.Tests</c> golden-corpus
    /// fixtures *(<c>GoldenCorpus</c>, <c>AiAssetCorpus</c>, <c>FolderLayoutTests</c>, …)* and one FDP
    /// example. ⭐ Those answer a DIFFERENT question — *"where is the repo, so I can read my test data?"* —
    /// and they are not asset-root resolution: they must never consult
    /// <see cref="AssetRoots.ConfiguredRoot"/>, because a configured deployment root is exactly the wrong
    /// answer for a golden corpus. ⛔ Folding them in would be ruling 9 applied to two concepts that merely
    /// look alike.</para>
    ///
    /// <para>⚠ Stated so the exclusion is not read as *"the rail was weakened until it passed"* — it is
    /// the boundary of what <c>CE-018</c> claims.</para>
    /// </summary>
    private static bool IsProductionSource(string file)
        => !file.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !file.Contains(Path.DirectorySeparatorChar + "examples" + Path.DirectorySeparatorChar,
                          StringComparison.OrdinalIgnoreCase);

    // ── CE-3343 — the TEST-side repository-root walk-up has ONE implementation too ─────────────────────────────────

    /// <summary>⭐ The files that may spell the repository marker: the one production walk-up and the one test-side helper.</summary>
    private static readonly string[] TheRepoRootImplementations = { "ContentLayout.cs", "TestRepo.cs" };

    /// <summary>The repository marker as a string literal — built from parts so this rail does not flag itself.</summary>
    private static readonly string MarkerLiteral = "\"" + "HROT" + ".sln" + "\"";

    /// <summary>
    /// ⭐ A method that exists to answer "where is the repo root?" — by any of the names the ~50 private copies used. ⛔ Deliberately
    /// NOT <c>FindRoot</c> (a tree-root finder in the BTree/HSM editors) and NOT a bare <c>Root</c>.
    /// </summary>
    private static readonly Regex RepoRootMethod = new(
        @"\bstatic\s+[\w?<>.\[\]]+\s+\w*(?:RepoRoot|RepositoryRoot|SolutionRoot|WorkspaceRoot|FindRepo\w*|ResolveRepo\w*|LocateRepo\w*)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-3343</c> — no source file keeps a PRIVATE walk-up to the repository root.</b>
    /// Route through <c>Fdp.Testing.TestRepo.Root</c> (<c>FDP/TestShared/TestRepo.cs</c>, built on
    /// <c>ContentLayout.TryFindSourceTree</c>). 📐 Measured <c>2026-10-29</c>: ~60 files each carried their own — different markers
    /// (<c>HROT.sln</c>, <c>docs/</c>, <c>layout/default</c>, <c>Hrot/Subsystems</c>), different failure modes (null, assert, throw).
    /// ⚠ A source scan: it catches a copy that spells the marker or is named like a repo-root finder; ⛔ not a walk-up that locates a
    /// FILE by relative path (<c>FindUp("docs/x.md")</c>) — that is another question.
    /// </summary>
    [Fact]
    public void NoFileKeepsAPrivateRepoRootWalkUp()
    {
        var repoRoot = TestRepo.Root;
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var top in new[] { "Hrot", "FDP", "Stride" })
            foreach (var file in EnumerateSources(Path.Combine(repoRoot, top)))
            {
                scanned++;
                var name = Path.GetFileName(file);
                if (Array.IndexOf(TheRepoRootImplementations, name) >= 0) continue;
                if (name == nameof(TheWalkUpHasOneImplementationTests) + ".cs") continue;

                var text = File.ReadAllText(file);
                if (text.Contains(MarkerLiteral, StringComparison.Ordinal) || RepoRootMethod.IsMatch(text))
                    offenders.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
            }

        Assert.True(scanned > 1000, $"the scan reached only {scanned} files — it walked the wrong tree");
        Assert.Equal(new List<string>(), offenders.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    /// <summary>⭐ <c>*.cs</c> under <paramref name="dir"/>, skipping hidden folders (<c>.claude/worktrees</c> holds whole checkouts), <c>bin</c>, <c>obj</c>, <c>node_modules</c> and <c>ExtDeps</c>.</summary>
    private static IEnumerable<string> EnumerateSources(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "*.cs")) yield return f;
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || name is "bin" or "obj" or "node_modules" or "ExtDeps") continue;
            foreach (var f in EnumerateSources(sub)) yield return f;
        }
    }
}
