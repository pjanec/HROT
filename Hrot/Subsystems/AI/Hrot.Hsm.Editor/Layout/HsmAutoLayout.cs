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
    private const float GapX       = 140f;   // ⭐ CE-1274 — room for the arrows and their labels between columns (was 60)
    private const float GapY       =  60f;   // (was 50)
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

    /// <summary>
    /// ⭐ <c>CE-1274</c> — places <paramref name="states"/> as a FLOW, left to right: column 0 holds the initial state, each next column the
    /// states first reached by a transition from the one before (breadth first over the siblings' transitions, a transition into a nested
    /// state counting for the sibling that holds it); a column longer than <see cref="MaxRows"/> wraps into a second one. Inside a column
    /// the states are ordered by the mean row of the states that lead to them, so arrows run mostly level instead of crossing. Wide gaps
    /// leave room for the arrows and their labels. Returns the extent.
    /// <para>⛔ SUPERSEDED: a near-square grid in transition order, 60 / 50 px apart — the user, 2026-10-23: <i>"nodes are close to each
    /// other in a grid and if there are many states (see platoon commander HSM) all is unreadable"</i>.</para>
    /// </summary>
    private static Vector2 PlaceGroup(IReadOnlyList<StateNode> states, Vector2 origin, Dictionary<StateNode, Vector2> sizes)
    {
        if (states.Count == 0) return Vector2.Zero;
        foreach (var st in states) Measure(st, sizes);
        var columns = Columns(states);

        float x = origin.X, height = 0f;
        foreach (var column in columns)
        {
            float width = column.Max(st => sizes[st].X), y = origin.Y;
            foreach (var st in column)
            {
                st.Position = new Vector2(x + (width - sizes[st].X) / 2f, y);
                y += sizes[st].Y + GapY;
            }
            height = MathF.Max(height, y - GapY - origin.Y);
            x += width + GapX;
        }
        return new Vector2(x - GapX - origin.X, height);
    }

    /// <summary>A column never holds more than this many states (a longer layer wraps).</summary>
    private const int MaxRows = 6;

    /// <summary>⭐ <c>CE-1274</c> — the siblings in columns: breadth-first layers from the initial state over their transitions, ordered by the
    /// mean row of their predecessors, wrapped at <see cref="MaxRows"/>. Pure over the model.</summary>
    internal static List<List<StateNode>> Columns(IReadOnlyList<StateNode> states)
    {
        var set = new HashSet<StateNode>(states);
        StateNode? SiblingOf(StateNode? n) { while (n != null && !set.Contains(n)) n = n.Parent; return n; }
        var succ = states.ToDictionary(st => st, _ => new List<StateNode>());
        var pred = states.ToDictionary(st => st, _ => new List<StateNode>());
        foreach (var st in states)
            foreach (var from in SelfAndDescendants(st))
                foreach (var t in from.OutgoingTransitions)
                    if (SiblingOf(t.Target) is { } to && to != st && !succ[st].Contains(to)) { succ[st].Add(to); pred[to].Add(st); }

        // layers: breadth first from the initial state, then from each state not yet reached (in the declared / transition order)
        var layer = new Dictionary<StateNode, int>();
        var order = Ordered(states);
        foreach (var start in order)
        {
            if (layer.ContainsKey(start)) continue;
            layer[start] = 0;
            var queue = new Queue<StateNode>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var st = queue.Dequeue();
                foreach (var to in succ[st])
                    if (!layer.ContainsKey(to)) { layer[to] = layer[st] + 1; queue.Enqueue(to); }
            }
        }

        var rank = new Dictionary<StateNode, float>();
        var columns = new List<List<StateNode>>();
        foreach (var group in order.GroupBy(st => layer[st]).OrderBy(g => g.Key))
        {
            // the mean row of the states (in earlier columns) that lead here; a state nothing earlier leads to keeps its order
            var sorted = group.Select((st, i) => (st, key: pred[st].Where(rank.ContainsKey).Select(q => rank[q]).DefaultIfEmpty(i).Average()))
                              .OrderBy(e => e.key).Select(e => e.st).ToList();
            for (int i = 0; i < sorted.Count; i += MaxRows)
            {
                var column = sorted.Skip(i).Take(MaxRows).ToList();
                for (int r = 0; r < column.Count; r++) rank[column[r]] = r;
                columns.Add(column);
            }
                    }
        return columns;
    }

    private static IEnumerable<StateNode> SelfAndDescendants(StateNode st)
    {
        yield return st;
        foreach (var c in st.Children)
            foreach (var d in SelfAndDescendants(c)) yield return d;
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
