using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// <b><c>UXI-11</c> slice <c>S-1</c>'s gate, as a rail: no host with an ECS world keeps a second
/// selection store.</b>
///
/// <para>📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.4 — <em>"<c>DefaultSelectionState</c>'s own
/// <c>HashSet</c> becomes <c>EcsSelectionState</c>, a read-through"</em>.</para>
///
/// <para>⭐⭐⭐ <b>Why a source scan and not a unit test.</b> The defect is not a wrong value — it is a
/// second <em>place</em> a value can live. 🔴 Measured <c>2026-09-20</c>: <c>CgfSubsystem</c> and
/// <c>EditorSubsystem</c> each constructed a <c>DefaultSelectionState</c> while
/// <c>SelectionInteractionSystem</c> wrote the <c>SelectionState</c> component, and the ~8 000-test
/// suite was green throughout — because every test used one store or the other, never both. ⛔ No
/// behavioural assertion catches a construction site; only looking for it does.</para>
///
/// <para>⚠ <b><c>DefaultSelectionState</c> is NOT banned.</b> It is the legitimate implementation for a
/// host with no world — §2.7.1's <c>DdsBackedSelectionState</c> (ExCon) is the same shape — and the
/// cheap fake for tests. ⭐ What is banned is a host that HAS a world holding one anyway: that is the
/// silent-default shape, <em>"a production caller that HAS a dependency must pass it"</em>.</para>
/// </summary>
public sealed class NoProductionHostKeepsAParallelSelectionStoreTests
{
    /// <summary>
    /// Trees searched. ⚠ <c>FDP/Examples</c> is excluded deliberately — <c>CarKinem</c> is a
    /// world-less sample and its adapter is a correct non-ECS implementation.
    /// </summary>
    private static readonly string[] ProductionTrees = { "Hrot", "Stride" };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "docs")) &&
                Directory.Exists(Path.Combine(dir, "FDP")) &&
                Directory.Exists(Path.Combine(dir, "Hrot")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("repo root not found from " + AppContext.BaseDirectory);
    }

    private static bool IsProductionFile(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.Contains("/obj/") || p.Contains("/bin/")) return false;
        // ⭐ Tests may construct one freely -- it is the in-memory fake.
        if (p.Contains(".Tests/") || p.Contains(".Tests.")) return false;
        if (p.Contains("/Examples/")) return false;
        return true;
    }

    [Fact]
    public void NoProductionHostConstructsTheInMemorySelectionStore()
    {
        var root      = RepoRoot();
        var offenders = new List<string>();

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains("new DefaultSelectionState")) continue;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A host with an ECS world is holding a second selection store. Use " +
            "Hrot.ScenarioEditor.Selection.EcsSelectionState instead (UXI-11 S-1, " +
            "UX_Feature_Selection.md §2.7.4). Sites:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⚠ <b>The negative control.</b> ⛔ A scan that finds nothing because it is looking in the wrong
    /// place passes exactly like a scan that finds nothing because the tree is clean. 📌 This is the
    /// <c>T-1</c> lesson — <em>"if it stays GREEN while the feature is BROKEN, THAT is the finding"</em>
    /// — applied before the fact.
    /// </summary>
    [Fact]
    public void TheScanActuallyReachesTheHostFilesItIsSupposedToGuard()
    {
        var root  = RepoRoot();
        var seen  = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;
            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
                if (IsProductionFile(file)) seen.Add(Path.GetFileName(file));
        }

        Assert.Contains("CgfSubsystem.cs", seen);
        Assert.Contains("EditorSubsystem.cs", seen);
        Assert.Contains("SelectionInteractionSystem.cs", seen);
    }
}
