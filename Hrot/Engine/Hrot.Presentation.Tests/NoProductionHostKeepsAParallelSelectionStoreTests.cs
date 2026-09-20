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
    /// ⭐⭐⭐ <b>NO HOST BUILDS ITS OWN SELECTION — <c>MapInteractionPack</c> BUILDS IT FOR ALL FIVE.</b>
    /// 🔒 User, <c>2026-09-20</c>: <i>"we want to unify across host also the bootstrap code as far as
    /// possible, including this entity selection stuff."</i>
    ///
    /// <para>📐 The checkable form: <c>new EcsSelectionState(</c> and
    /// <c>new SelectionInteractionSystem(</c> appear in production in exactly ONE place — the pack.
    /// ⛔ A host that constructs either gets a SECOND selection over the same world, which is the
    /// split <c>UXI-11</c> spent four slices removing.</para>
    ///
    /// <para>⚠⚠ <b>This rail REPLACED an earlier one that asserted the opposite</b> — that every host
    /// which constructs <c>SelectionInteractionSystem</c> also constructs <c>EcsSelectionState</c>.
    /// 🔴 That was right for the world where each host wired its own and became FALSE the moment the
    /// pack took over; it failed on the very commit that unified them. ⭐ The invariant it was reaching
    /// for is stronger and simpler: <b>nobody wires their own.</b></para>
    /// </summary>
    [Fact]
    public void OnlyTheSharedPackConstructsTheSelection()
    {
        var root      = RepoRoot();
        var offenders = new List<string>();
        var packHits  = 0;

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                var name = Path.GetFileName(file);

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i].TrimStart();
                    if (l.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!l.Contains("new Hrot.ScenarioEditor.Selection.EcsSelectionState(") &&
                        !l.Contains("new EcsSelectionState(") &&
                        !l.Contains("new SelectionInteractionSystem(") &&
                        !l.Contains("new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(")) continue;

                    if (name == "MapInteractionPack.cs") { packHits++; continue; }
                    // ⚠ TWO named `?? new …` fallbacks survive, both for callers that construct the
                    //   object directly in a test. ⛔ Neither is reachable in production, because
                    //   MapInteractionPack always passes the instance it built:
                    //     · SimHostVisualization — SimHostApp passes the pack's view and gesture system;
                    //     · SelectionInteractionSystem — the pack passes the view it just constructed.
                    // ⭐ Keyed on the `??`, so REPLACING either fallback with an unconditional `new`
                    //   fails this rail — which is the case that would actually make two selections.
                    if (l.Contains("??") &&
                        (name == "SimHostVisualization.cs" || name == "SelectionInteractionSystem.cs"))
                        continue;

                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        // ⚠ Anti-vacuity: the pack must actually build both, or this scan is matching nothing.
        Assert.True(packHits >= 2,
            $"expected MapInteractionPack to construct the view AND the gesture system; found {packHits}");

        Assert.True(
            offenders.Count == 0,
            "A host is constructing its own selection instead of taking MapInteractionPack's. Two " +
            "instances over one world are two selections (UXI-11, UX_Feature_Selection.md §2.7.10). " +
            "Sites:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
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
