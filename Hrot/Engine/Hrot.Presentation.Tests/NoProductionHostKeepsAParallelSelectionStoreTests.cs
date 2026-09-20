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
    /// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-2</c>'s gate: NOTHING hand-writes the <c>SelectionState</c>
    /// component except the view.</b>
    ///
    /// <para>📄 §2.7.3 rule 1 — one writer. 🔴 Measured <c>2026-09-20</c>, BEFORE <c>S-2</c>: four
    /// production sites each hand-rolled the same clear-loop-then-set —
    /// <c>EditorSubsystem</c>'s <c>GlobalActionIds.Select</c> handler, <c>EditorSubsystem.SetSelection2D</c>,
    /// <c>IgApplication.SelectEntityOnMap</c>, and (until <c>S-1</c>) <c>SelectionInteractionSystem</c>.
    /// ⚠ §2.7.4's delete-list named THREE; <c>SetSelection2D</c> was the fourth and it had been
    /// missed — that is why this is a scan and not a checklist.</para>
    ///
    /// <para>⚠ <b>The exemption is a WHITELIST, not a pattern.</b> ⛔ Any new file that writes the
    /// component fails this, including a "small" one — that is the point. ⭐ If a host genuinely needs
    /// to write it, the answer is to publish a <c>SelectionChangeRequest</c>.</para>
    /// </summary>
    [Fact]
    public void OnlyTheViewWritesTheSelectionStateComponent()
    {
        // ⭐ The ONE implementation. It uses an alias (SelectionStateComponent) precisely because the
        //   view's own type name would collide -- so the literal below cannot match it by accident.
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "EcsSelectionState.cs" };

        var root      = RepoRoot();
        var offenders = new List<string>();

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                if (allowed.Contains(Path.GetFileName(file))) continue;
                // ⚠ Scoped to Hrot.IG.Components.SelectionState. The Blueprint/BTree/HSM editors have
                //   their own unrelated SelectionState -- a NODE selection, a different concept
                //   (the ".*Selection.*" trap: ~60 of 67 classes are not this one).
                var p = file.Replace('\\', '/');
                if (p.Contains("/Blueprints/") || p.Contains("/NodeEdit") || p.Contains("/AI/")) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i];
                    if (l.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!l.Contains("new SelectionState {") && !l.Contains("new SelectionState{")) continue;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The SelectionState component is being written outside EcsSelectionState. Publish a " +
            "SelectionChangeRequest instead (UXI-11 S-2, UX_Feature_Selection.md §2.7.3 rule 1). Sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>EVERY HOST WITH A MAP SELECTION HOLDS THE SHARED VIEW.</b>
    /// 🔒 User ruling, <c>2026-09-20</c>: <i>"simhost is not special in how it should handle the UI;
    /// lets make the nodes use same (best shared) stuff in the same way."</i>
    ///
    /// <para>📐 The checkable form: a file that constructs <c>SelectionInteractionSystem</c> — i.e. a
    /// host that lets the operator select on a map — must also construct an
    /// <c>EcsSelectionState</c>. ⛔ Without it the host is writing the <c>SelectionState</c> component
    /// through one path and showing its panels something else, which is exactly the split
    /// <c>UXI-11</c> exists to end.</para>
    ///
    /// <para>🔴 Measured <c>2026-09-20</c>, before this rail: <b>five</b> hosts constructed the
    /// interaction system and only three held the view. SimHost kept a <c>SimHostSelectionManager</c>
    /// behind a <c>SimHostInspectorAdapter</c> — <b>both now deleted</b> — and ReplayBrowser held
    /// nothing at all while its own hand-written callback tried to bridge the gap. ⚠ I had recorded
    /// ReplayBrowser as <i>"a host with no global selection"</i>; that was wrong, and this rail is what
    /// makes the claim checkable instead of remembered.</para>
    /// </summary>
    [Fact]
    public void EveryHostThatRunsTheInteractionSystemAlsoHoldsTheSharedView()
    {
        var root    = RepoRoot();
        var missing = new List<string>();
        var seen    = new List<string>();

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                var text = File.ReadAllText(file);
                if (!text.Contains("new SelectionInteractionSystem(") &&
                    !text.Contains("Systems.SelectionInteractionSystem(")) continue;

                seen.Add(Path.GetFileName(file));
                if (!text.Contains("EcsSelectionState("))
                    missing.Add(Path.GetRelativePath(root, file));
            }
        }

        // ⚠ Anti-vacuity: a scan that matched nothing would pass silently.
        // 📐 FOUR hosts run it: Editor · IG · SimHost · ReplayBrowser.
        // 🔴 CGF is the fifth MAP host and deliberately absent from this list — it runs NO
        //    SelectionInteractionSystem at all, which is UXI-11's remaining open defect
        //    ("the input path is missing", measured 2026-09-19). ⇒ when CGF gains one, this count
        //    becomes 5 and the host must bring the shared view with it, which the assertion below
        //    then enforces. ⛔ Do not raise the threshold to 5 to "fix" a failure — check whether a
        //    host lost its interaction system instead.
        Assert.True(
            seen.Count >= 4,
            $"expected >=4 interaction-system hosts, found {seen.Count}: {string.Join(", ", seen)}");
        foreach (var expected in new[]
                 { "EditorSubsystem.cs", "IgApplication.cs", "SimHostVisualization.cs", "ReplayBrowserSubsystem.cs" })
            Assert.Contains(expected, seen);

        Assert.True(
            missing.Count == 0,
            "A host runs SelectionInteractionSystem (so the operator can select on its map) but holds " +
            "no EcsSelectionState, so its panels and its map cannot agree. Give it the shared view " +
            "(UXI-11, UX_Feature_Selection.md §2.7.9). Hosts:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
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
