using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CE-1304</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §5, §11t V1) — one item of the View track: from <see cref="At"/> (SOURCE sim seconds, a time
/// or an anchor + offset, like a key) the film shows the <see cref="TwoD"/> map or the 3-D one, and the layer set <see cref="Layers"/> — the SAME
/// <see cref="LayerChange"/> a take and a shot already carry, applied through the one <see cref="ILayerOverride"/> (R-4060). A null field keeps what the
/// item before it set (an item may switch only the map, or only the layers).
/// </summary>
public sealed record ViewChange
{
    public AtExpr At { get; init; }
    /// <summary>True = the 2-D map, false = the 3-D map, null = as before.</summary>
    public bool? TwoD { get; init; }
    /// <summary>The layer set from here (the override's "start clean, these on, these off" — an absolute set, not a delta); null = as before.</summary>
    public LayerChange? Layers { get; init; }
}

/// <summary>⭐ CE-1304 P6 — what the View track says at one source second: the map (2-D or 3-D) and the layer set in force.</summary>
public sealed record ViewFrame(bool TwoD, LayerChange Layers)
{
    /// <summary>The take's view before any item: the 3-D map (the film camera's own) and a clean layer set.</summary>
    public static readonly ViewFrame Start = new(false, LayerChange.None);

    /// <summary>The same map and the same layer names in the same order (a record's list fields compare by reference).</summary>
    public bool Same(ViewFrame? other) => other != null && other.TwoD == TwoD && TakeViews.Same(other.Layers, Layers);
}

/// <summary>
/// ⭐⭐ <c>CE-1304</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §11t V2) — a take's view items, however it was written: its own <see cref="Take.Views"/> when it
/// has any, else WHAT IT ALREADY PLAYS read as view items — the take's <c>layers</c> from its start, and at each laid-out shot's start the take's set with
/// that shot's <c>layers</c> laid over it (exactly the set <c>DirectorRunner.EnterShot</c> applied before the View track existed), equal neighbours merged.
/// ⛔ No second layer mechanism: the runner applies every item through <see cref="ILayerOverride"/>, and the first View edit writes these items as they play
/// (the <see cref="TakeSubtitles.Of"/> pattern, CR-11 S8).
/// </summary>
public static class TakeViews
{
    public static IReadOnlyList<ViewChange> Of(Take take, TakeTimeline laidOut)
    {
        if (take.Views.Count > 0) return take.Views;
        var list = new List<ViewChange> { new() { At = new AtExpr(0d), Layers = take.Layers ?? LayerChange.None } };
        foreach (var shot in laidOut.Shots)
        {
            var layers = OfShot(take.Layers, shot.Shot.Layers);
            if (Same(list[^1].Layers!, layers)) continue;
            list.Add(new ViewChange { At = new AtExpr(shot.Start), Layers = layers });
        }
        return list;
    }

    /// <summary>The set a v1 shot plays: the take's on / off, the shot's added, and the shot's off taken out of on (the runner's rule since B10).</summary>
    public static LayerChange OfShot(LayerChange? take, LayerChange? shot)
    {
        var on = new List<string>(take?.On ?? Array.Empty<string>());
        var off = new List<string>(take?.Off ?? Array.Empty<string>());
        if (shot != null) { on.AddRange(shot.On); off.AddRange(shot.Off); on.RemoveAll(shot.Off.Contains); }
        return new LayerChange(on, off);
    }

    /// <summary>
    /// The view at SOURCE second <paramref name="sourceSim"/>: every item whose time is known and at or before it, in time order (list order on a tie),
    /// each setting what it names. Before the first: <see cref="ViewFrame.Start"/>.
    /// </summary>
    public static ViewFrame At(IReadOnlyList<ViewChange> views, Func<string, double?> anchorTime, double sourceSim)
    {
        bool twoD = ViewFrame.Start.TwoD;
        var layers = ViewFrame.Start.Layers;
        foreach (var (v, _) in Ordered(views, anchorTime))
        {
            if (v.At.Resolve(anchorTime) is not { } at || at > sourceSim + 1e-9) break;
            if (v.TwoD is { } t) twoD = t;
            if (v.Layers != null) layers = v.Layers;
        }
        return new ViewFrame(twoD, layers);
    }

    /// <summary>The items whose time is known, by time (stable), each with its index in <paramref name="views"/>.</summary>
    public static IEnumerable<(ViewChange View, int Index)> Ordered(IReadOnlyList<ViewChange> views, Func<string, double?> anchorTime)
        => views.Select((v, i) => (View: v, Index: i, At: v.At.Resolve(anchorTime)))
                .Where(x => x.At != null)
                .OrderBy(x => x.At!.Value).ThenBy(x => x.Index)
                .Select(x => (x.View, x.Index));

    /// <summary>The same names in the same order.</summary>
    public static bool Same(LayerChange a, LayerChange b) => a.On.SequenceEqual(b.On) && a.Off.SequenceEqual(b.Off);

    /// <summary>The track's label of an item: <c>2-D</c> / <c>3-D</c> and the layers it turns on.</summary>
    public static string Describe(ViewChange v)
    {
        var parts = new List<string>();
        if (v.TwoD is { } t) parts.Add(t ? "2-D" : "3-D");
        if (v.Layers is { } l)
            parts.Add(l.On.Count == 0 && l.Off.Count == 0 ? "clean layers"
                      : string.Join(" ", l.On.Select(n => "+" + n).Concat(l.Off.Select(n => "-" + n))));
        return parts.Count == 0 ? "view" : string.Join(" | ", parts);
    }
}
