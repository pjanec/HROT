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
        var repoRoot = RepoRoot();

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
        var repoRoot = RepoRoot();

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

    /// <summary>
    /// ⭐ Found through the very seam under test — <see cref="AssetRoots.ResolveProjectDir"/> — which is
    /// also a small end-to-end use of it. ⛔ Fails loudly rather than skipping: a rail that quietly opts
    /// out when it cannot find the tree is a rail that reports green forever.
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
