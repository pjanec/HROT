using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Layout;

/// <summary>
/// ⭐ <c>CE-1255</c> (BV-23, R-1028) — the statechart layout, for a machine opened with no layout data (every C#-built machine, which
/// cannot be tidied by hand: it is read-only). Sized bottom-up: a composite is laid out before its parent places it, its children
/// sit in the composite's INTERIOR coordinates (the container contract — it auto-sizes to them), the initial state first and the
/// rest in transition order, in a near-square grid of cells sized to their contents; a parallel state lays each region out in its
/// own band (child positions are band-relative along the stack axis). 📄 docs/DESIGN_Entity_Brain_View.md §9 BV-23.
/// <para>⛔ The previous grid gave children ABSOLUTE canvas positions (an interior-relative field), so every composite after the
/// first was pushed out by its own offset into its neighbour, and a nested composite got a cell narrower than itself.</para>
/// </summary>
public static class HsmAutoLayout
{
    private const float LeafWidth  = 160f;
    private const float LeafHeight =  80f;
    private const float GapX       =  60f;
    private const float GapY       =  50f;
    private const float Inset      =  16f;   // interior margin around a group
    private const float Chrome     =  64f;   // header (24) + padding (8 + 12) + outline, rounded up: overestimating only adds space
    private const float TopStartX  = 100f;
    private const float TopStartY  = 100f;

    /// <summary>Lays out every state of <paramref name="asset"/>.</summary>
    public static void Layout(HsmAsset asset)
    {
        var sizes = new Dictionary<StateNode, Vector2>();
        PlaceGroup(Ordered(asset.RootState.Children), new Vector2(TopStartX, TopStartY), sizes);
    }

    /// <summary>The outer size of <paramref name="s"/> once laid out (its children placed in its interior).</summary>
    internal static Vector2 Measure(StateNode s, Dictionary<StateNode, Vector2> sizes)
    {
        if (sizes.TryGetValue(s, out var known)) return known;
        Vector2 size;
        if (!s.IsContainer)
            size = new Vector2(LeafWidth, LeafHeight);
        else
        {
            s.SizeOverride = null;   // the container sizes itself to what it holds
            if (s.IsParallel && s.Children.Count > 0)
            {
                float width = 0f, height = 0f;
                foreach (var band in s.Children.GroupBy(c => c.RegionIndex).OrderBy(g => g.Key))
                {
                    var extent = PlaceGroup(Ordered(band.ToList()), new Vector2(Inset, Inset), sizes);   // band-relative
                    width = MathF.Max(width, extent.X + 2 * Inset);
                    height += extent.Y + 2 * Inset;
                }
                size = new Vector2(MathF.Max(width, LeafWidth), height) + new Vector2(Chrome * 0.5f, Chrome);
            }
            else
            {
                var extent = PlaceGroup(Ordered(s.Children), new Vector2(Inset, Inset), sizes);
                size = new Vector2(MathF.Max(extent.X + 2 * Inset, LeafWidth), extent.Y + 2 * Inset) + new Vector2(Chrome * 0.5f, Chrome);
            }
        }
        sizes[s] = size;
        return size;
    }

    /// <summary>Places <paramref name="states"/> in a near-square grid from <paramref name="origin"/>; returns the grid's extent.</summary>
    private static Vector2 PlaceGroup(IReadOnlyList<StateNode> states, Vector2 origin, Dictionary<StateNode, Vector2> sizes)
    {
        if (states.Count == 0) return Vector2.Zero;
        var measured = states.Select(s => Measure(s, sizes)).ToArray();
        int cols = (int)MathF.Ceiling(MathF.Sqrt(states.Count));
        int rows = (states.Count + cols - 1) / cols;
        var colWidth = new float[cols];
        var rowHeight = new float[rows];
        for (int i = 0; i < states.Count; i++)
        {
            colWidth[i % cols] = MathF.Max(colWidth[i % cols], measured[i].X);
            rowHeight[i / cols] = MathF.Max(rowHeight[i / cols], measured[i].Y);
        }
        for (int i = 0; i < states.Count; i++)
        {
            float x = origin.X, y = origin.Y;
            for (int c = 0; c < i % cols; c++) x += colWidth[c] + GapX;
            for (int r = 0; r < i / cols; r++) y += rowHeight[r] + GapY;
            states[i].Position = new Vector2(x, y);
        }
        return new Vector2(colWidth.Sum() + GapX * (cols - 1), rowHeight.Sum() + GapY * (rows - 1));
    }

    /// <summary>The initial state first, then the siblings reached by transitions from it (breadth first), then the rest as declared.</summary>
    internal static IReadOnlyList<StateNode> Ordered(IReadOnlyList<StateNode> siblings)
    {
        var set = new HashSet<StateNode>(siblings);
        var order = new List<StateNode>(siblings.Count);
        var seen = new HashSet<StateNode>();
        var queue = new Queue<StateNode>();
        foreach (var start in siblings.Where(s => s.IsInitial).Concat(siblings))
        {
            if (!seen.Add(start)) continue;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                order.Add(s);
                foreach (var t in s.OutgoingTransitions)
                    if (set.Contains(t.Target) && seen.Add(t.Target)) queue.Enqueue(t.Target);
            }
        }
        return order;
    }
}
