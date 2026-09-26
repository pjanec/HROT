using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐⭐⭐ <b>The <c>A</c> hosts <c>B</c> hosts <c>A</c> walk — over ASSETS, at validation time.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16 (<c>E5</c> item 7).
/// 🔒 <c>HsmValidator.SubtreeHostsUnder</c> named this rule as unowned for four batches:
/// <i>"that is a walk over ASSETS, needs a resolver this validator does not have, and belongs to
/// whoever builds subtree hosting for real."</i>
///
/// <para>⛔⛔ <b>FOUR cycle detectors already exist and NONE of them is this one</b> — 📐 enumerated
/// <c>2026-09-26</c> with <c>search_graph</c> + grep: <c>BTreeValidator.CheckCycles</c> walks
/// <c>ChildVisualIds</c> <b>inside one asset</b>; <c>BTreeLinkValidator.WouldCreateCycle</c> walks the
/// ancestor chain at link-creation time; <c>ContainerCycleDetector</c> is spatial container nesting;
/// and <c>SubtreeHostsUnder</c> walks the STATE TREE, whose own remark says it <i>"cannot cycle by
/// construction"</i>. ⚠ <b>The risk here was the opposite of the usual one</b> — four plausible prior
/// arts, none applicable; reusing any would have pinned the wrong graph.</para>
///
/// <para>⭐⭐ <b>Why this type depends on NEITHER editor assembly.</b> It reads edges through
/// <see cref="ISubtreeHostingAsset"/>, so it never names <c>HsmAsset</c> or <c>BehaviorTreeAsset</c>
/// and ONE algorithm serves both validators. 🔒 Ruling 9 — and an HSM-only rule would report
/// <c>A→B→A</c> when the HSM is open and stay silent when the BTree is.</para>
/// </summary>
public static class SubtreeCycleDetector
{
    /// <summary>
    /// ⭐ Returns the hosting cycle reachable from <paramref name="startAssetId"/>, as the ring in
    /// walk order with the repeated asset appearing <b>first and last</b>
    /// (<c>[A, B, A]</c>) — or an EMPTY list when the graph below this asset is acyclic.
    ///
    /// <para>⛔⛔ <b>The <c>onPath</c> set is what makes this a CYCLE test, and a plain visited set is
    /// NOT a substitute.</b> ⚠ "Seen before" is also true of a legitimate DIAMOND — two states, or two
    /// BTree nodes, hosting the same child asset — which is valid and common. ⇒ a visited-set
    /// implementation would false-positive on it. ⭐ The separate <c>done</c> set is the one that keeps
    /// the walk linear rather than exponential on a diamond-heavy graph.</para>
    ///
    /// <para>⚠ <b>A missing or non-hosting asset simply contributes no edges.</b> ⛔ A dangling
    /// <c>SubtreeAssetId</c> is a DIFFERENT defect with its own rule
    /// (<c>DanglingReferenceAfterReload</c>); this walk must not also report it, or one authoring
    /// mistake produces two unrelated diagnostics.</para>
    /// </summary>
    public static IReadOnlyList<Guid> FindCycleFrom(IAssetCatalog? catalog, Guid startAssetId)
    {
        if (catalog is null || startAssetId == Guid.Empty) return Array.Empty<Guid>();

        var onPath = new HashSet<Guid>();
        var done   = new HashSet<Guid>();
        var path   = new List<Guid>();

        return Walk(catalog, startAssetId, onPath, done, path) ? path : Array.Empty<Guid>();
    }

    /// <summary>
    /// ⭐ Depth-first, iterative in spirit but written recursively because the asset graph is shallow
    /// by nature (an authoring artefact, not data) and the recursive form is the one a reader can
    /// check against the sequence diagram.
    /// </summary>
    private static bool Walk(
        IAssetCatalog catalog, Guid id,
        HashSet<Guid> onPath, HashSet<Guid> done, List<Guid> path)
    {
        // ⭐⭐ THE CYCLE. Close the ring by repeating the offender so the message can name it:
        //    [A, B, A] reads as "A hosts B hosts A" with no further interpretation needed.
        if (onPath.Contains(id))
        {
            path.Add(id);
            return true;
        }

        // ⭐ Already fully explored on another branch and found clean — a diamond, not a cycle.
        if (!done.Add(id)) return false;

        onPath.Add(id);
        path.Add(id);

        // ⚠ An id the catalogue cannot resolve, or an asset kind that hosts nothing, contributes no
        //   edges. ⛔ Deliberately NOT reported here — see the remarks.
        if (catalog.FindByAssetId(id) is ISubtreeHostingAsset host)
        {
            foreach (var child in host.GetHostedSubtreeAssetIds())
            {
                if (child == Guid.Empty) continue;
                if (Walk(catalog, child, onPath, done, path)) return true;
            }
        }

        onPath.Remove(id);
        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The next hop AFTER <paramref name="assetId"/> inside the ring — i.e. the child whose
    /// hosting edge, if cut, breaks this cycle.</b> Returns <see langword="null"/> when
    /// <paramref name="assetId"/> is on the approach path but NOT in the ring itself.
    ///
    /// <para>🔴 <b>Why this exists, and it was found by a rail rather than by reasoning.</b> The first
    /// cut of this rule reported the cycle with NO target element, on the argument that <i>"for
    /// <c>A→B→A</c> opened at <c>B</c>, no state of <c>B</c> is at fault."</i> ⛔ **That is wrong:**
    /// a ring has no innocent edge — cutting <c>B</c>'s edge to <c>A</c> breaks it exactly as well as
    /// cutting <c>A</c>'s. ⇒ every member of the ring has an actionable site, and the validator that
    /// omitted it produced a diagnostic the canvas could not badge at all.</para>
    ///
    /// <para>⚠ <b>The approach path is the case that keeps it honest.</b> <c>A→B→C→B</c> reached from
    /// <c>A</c> gives the path <c>[A,B,C,B]</c> whose RING is <c>B→C→B</c>; ⛔ <c>A</c> is not in it,
    /// nothing in <c>A</c> is at fault, and this returns <see langword="null"/> so the diagnostic
    /// stays asset-level rather than blaming an innocent state.</para>
    /// </summary>
    public static Guid? NextHopInRing(IReadOnlyList<Guid> cycle, Guid assetId)
    {
        if (cycle is null || cycle.Count < 2) return null;

        // ⭐ The ring is the SUFFIX from the first occurrence of the repeated tail element.
        var closing = cycle[cycle.Count - 1];
        var ringStart = -1;
        for (int i = 0; i < cycle.Count - 1; i++)
        {
            if (cycle[i] == closing) { ringStart = i; break; }
        }
        if (ringStart < 0) return null;

        for (int i = ringStart; i < cycle.Count - 1; i++)
        {
            if (cycle[i] == assetId) return cycle[i + 1];
        }
        return null;
    }

    /// <summary>
    /// ⭐ Render a cycle path as <c>Name → Name → Name</c> for a diagnostic message, falling back to
    /// the raw id when the catalogue cannot name an asset. ⚠ Shared so the two validators cannot drift
    /// into two spellings of one message.
    /// </summary>
    public static string DescribeCycle(IAssetCatalog? catalog, IReadOnlyList<Guid> cycle)
    {
        if (cycle is null || cycle.Count == 0) return string.Empty;

        var parts = new List<string>(cycle.Count);
        foreach (var id in cycle)
        {
            var name = catalog?.FindByAssetId(id)?.Name;
            parts.Add(string.IsNullOrWhiteSpace(name) ? id.ToString("D") : name!);
        }

        return string.Join(" -> ", parts);
    }
}
