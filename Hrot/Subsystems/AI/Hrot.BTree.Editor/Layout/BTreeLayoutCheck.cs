using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.BTree.Editor.Model;

namespace Hrot.BTree.Editor.Layout;

/// <summary>
/// ⭐ <c>CE-1368</c> (BTree host design §3.4) — the ONE overlap measure: which DRAWN rectangles of a tree (each node's body AND each decorator
/// pill, sized by <c>NodeSizing</c> through <see cref="BTreeAutoLayout"/>'s sizer) cover each other. The CE-1367 rail, the open path
/// (<see cref="RelayIfOverlapping"/>) and "Reset layout" all use it — no second copy.
/// </summary>
public static class BTreeLayoutCheck
{
    /// <summary>One drawn rectangle; <paramref name="Owner"/> is the index of the node it belongs to (a node's pills never "overlap" their own host).</summary>
    public readonly record struct DrawnRect(string Who, int Owner, Vector2 Min, Vector2 Max);

    /// <summary>Every node's drawn body and pills at their CURRENT positions.</summary>
    public static List<DrawnRect> DrawnRects(BehaviorTreeAsset asset, Func<string, float>? measure, float headerHeight)
    {
        var sizer = BTreeAutoLayout.SizerFor(asset, measure, headerHeight);
        var rects = new List<DrawnRect>();
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            var n = asset.Nodes[i];
            var d = sizer.Of(n);
            string who = string.IsNullOrEmpty(n.DisplayLabel) ? n.KernelType.ToString() : n.DisplayLabel;
            rects.Add(new DrawnRect(who, i, n.Position, n.Position + new Vector2(d.Width, d.Height)));
            foreach (var pl in d.Pills.Placements.Values)
                rects.Add(new DrawnRect("pill of " + who, i, n.Position + pl.TopLeft, n.Position + pl.TopLeft + pl.Size));
        }
        return rects;
    }

    /// <summary>How many pairs of drawn rectangles (of different nodes) overlap; <paramref name="first"/> names the first pair.</summary>
    public static int OverlappingPairs(BehaviorTreeAsset asset, Func<string, float>? measure, float headerHeight, out string first)
        => OverlappingPairs(DrawnRects(asset, measure, headerHeight), out first);

    public static int OverlappingPairs(IReadOnlyList<DrawnRect> r, out string first)
    {
        int pairs = 0; first = "";
        for (int a = 0; a < r.Count; a++)
            for (int b = a + 1; b < r.Count; b++)
            {
                if (r[a].Owner == r[b].Owner) continue;
                if (r[a].Min.X < r[b].Max.X && r[b].Min.X < r[a].Max.X && r[a].Min.Y < r[b].Max.Y && r[b].Min.Y < r[a].Max.Y)
                { pairs++; if (first == "") first = $"'{r[a].Who}' x '{r[b].Who}'"; }
            }
        return pairs;
    }

    /// <summary>The open path for a STORED layout (a JSON asset): when any two drawn rectangles overlap, tidy the whole tree in memory —
    /// positions only, ⛔ the asset is NOT marked dirty (it is saved only if the author saves). A layout with no overlap is left exactly as
    /// authored. Returns true when it re-laid.</summary>
    public static bool RelayIfOverlapping(BehaviorTreeAsset asset, Func<string, float>? measure, float headerHeight)
    {
        if (OverlappingPairs(asset, measure, headerHeight, out _) == 0) return false;
        BTreeAutoLayout.Layout(asset, measure, headerHeight);
        return true;
    }
}
