using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐ A facet of <see cref="VariableRowOrigin"/> that <c>GroupBy</c> can group on.
///
/// <para>
/// ⛔⛔ <b>Not a set of hardcoded MODES.</b> §1b: <i>ungrouped</i> is <c>[]</c>, <i>by entity</i> is
/// <c>[Entity]</c>, <i>by asset then entity</i> is <c>[Asset, Entity]</c> — ⭐ all four the user asked
/// for, plus <c>[Section]</c>, plus anything later, <b>with no new code per mode</b>. ⭐⭐ Every facet is
/// already on the row, so grouping needs <b>no new row data</b> — which is the sign the abstraction is
/// right.
/// </para>
/// </summary>
public enum VariableFacet
{
    Asset,
    Entity,
    Section,
}

/// <summary>One grouping level. <see cref="Children"/> and <see cref="Rows"/> are mutually exclusive.</summary>
public sealed record VariableRowGroup(
    VariableFacet                     Facet,
    string                            Header,
    IReadOnlyList<VariableRowGroup>   Children,
    IReadOnlyList<VariableRow>        Rows);

/// <summary>
/// ⭐⭐⭐ <b>Grouping, folding and qualification (§1b, §1a).</b>
///
/// <para>
/// ⭐⭐ <b>The rule that makes it feel automatic: a UNIFORM facet emits NO header.</b> Watching one
/// asset ⇒ no asset header appears, by itself. ⛔ No setting, no special mode, no pointless single
/// group — and the same rule is what makes <c>[Asset, Entity]</c> a sensible Watch DEFAULT rather than
/// something the user must turn off when they happen to be watching one thing.
/// </para>
/// </summary>
public static class VariableRowGrouping
{
    public static IReadOnlyList<VariableFacet> WatchDefault   { get; } = new[] { VariableFacet.Asset, VariableFacet.Entity };
    public static IReadOnlyList<VariableFacet> DetailsDefault { get; } = Array.Empty<VariableFacet>();

    /// <summary>The display text of one facet for one row.</summary>
    public static string FacetOf(VariableRow row, VariableFacet facet) => facet switch
    {
        VariableFacet.Asset   => string.IsNullOrEmpty(row.Origin.AssetName)
                                    ? row.Origin.AssetId.ToString("D") : row.Origin.AssetName,
        VariableFacet.Entity  => row.Origin.Entity.ToString(),
        VariableFacet.Section => row.Origin.Section,
        _ => string.Empty,
    };

    /// <summary>
    /// Groups <paramref name="rows"/> by <paramref name="groupBy"/>, in order.
    /// ⭐ A level whose facet is uniform across the rows reaching it emits no group and is skipped.
    /// </summary>
    public static IReadOnlyList<VariableRowGroup> Group(
        IReadOnlyList<VariableRow> rows, IReadOnlyList<VariableFacet> groupBy)
        => BuildLevel(rows, groupBy, 0, out _);

    /// <summary>Rows that ended up outside any group (everything, when every level was uniform).</summary>
    public static IReadOnlyList<VariableRow> Ungrouped(
        IReadOnlyList<VariableRow> rows, IReadOnlyList<VariableFacet> groupBy)
    {
        BuildLevel(rows, groupBy, 0, out var leftover);
        return leftover;
    }

    private static IReadOnlyList<VariableRowGroup> BuildLevel(
        IReadOnlyList<VariableRow> rows, IReadOnlyList<VariableFacet> groupBy, int depth,
        out IReadOnlyList<VariableRow> leftover)
    {
        leftover = Array.Empty<VariableRow>();
        if (rows.Count == 0) return Array.Empty<VariableRowGroup>();

        for (int d = depth; d < groupBy.Count; d++)
        {
            var facet   = groupBy[d];
            var buckets = rows.GroupBy(r => FacetOf(r, facet), StringComparer.Ordinal).ToList();

            // ⭐⭐ Uniform ⇒ no header. Skip this level entirely and try the next facet.
            if (buckets.Count <= 1) continue;

            var result = new List<VariableRowGroup>(buckets.Count);
            foreach (var bucket in buckets)
            {
                var members  = bucket.ToList();
                var children = BuildLevel(members, groupBy, d + 1, out var inner);
                result.Add(new VariableRowGroup(
                    facet, bucket.Key,
                    children,
                    children.Count == 0 ? members : inner));
            }
            return result;
        }

        // Every remaining facet was uniform ⇒ a flat list, no headers at all.
        leftover = rows;
        return Array.Empty<VariableRowGroup>();
    }

    /// <summary>
    /// ⭐⭐ <b>Qualification — the CONTROL decides, not the source (§1a, corrected).</b>
    ///
    /// <para>
    /// The <c>Name</c> cell shows the SHORT name when something else already carries the asset: either
    /// a group header hoisted it, or the whole list is one asset so there is nothing to disambiguate.
    /// ⛔ Only when the list is heterogeneous AND ungrouped by asset does the cell qualify —
    /// <c>PlatoonHillAttack2.Health</c>. ⭐ Grouping does it better than repetition: it hoists the
    /// shared part into a header instead of rendering it N times.
    /// </para>
    /// </summary>
    public static string DisplayName(
        VariableRow row, IReadOnlyList<VariableRow> allRows, IReadOnlyList<VariableFacet> groupBy)
    {
        if (groupBy.Contains(VariableFacet.Asset)) return row.ShortName;      // a header carries it

        bool uniformAsset = allRows.All(r => r.Origin.AssetId == row.Origin.AssetId);
        if (uniformAsset) return row.ShortName;                               // nothing to disambiguate

        string asset = FacetOf(row, VariableFacet.Asset);
        return $"{asset}.{row.ShortName}";
    }

    /// <summary>⭐ Full path in the tooltip, always (§1a).</summary>
    public static string FullPathTooltip(VariableRow row)
        => $"{FacetOf(row, VariableFacet.Asset)} / {row.Origin.Entity} / {row.Origin.Section} / {row.Origin.VariablePath}";

    /// <summary>
    /// ⭐⭐⭐ <b>A COLLAPSED header inherits its children's state (§1b).</b> 🔴 red if any descendant
    /// changed this tick, 🟡 yellow if any is pending.
    ///
    /// <para>
    /// ⛔ <b>Without this, folding only HIDES — it does not help a monitor.</b> With it you can fold
    /// everything down, still see WHERE the activity is, and expand only that group.
    /// </para>
    ///
    /// <para>
    /// ⚠ Both booleans are carried up separately rather than resolved to one colour here: §4a's whole
    /// point is that <i>changed</i> and <i>pending</i> stay distinguishable, and a group is exactly
    /// where collapsing them would be most tempting.
    /// </para>
    /// </summary>
    public static RowHighlight AggregateHighlight(
        VariableRowGroup group, Func<VariableRow, RowHighlight> highlightOf)
    {
        bool changed = false, pending = false;
        Walk(group);
        return new RowHighlight(changed, pending);

        void Walk(VariableRowGroup g)
        {
            foreach (var r in g.Rows)
            {
                var h = highlightOf(r);
                changed |= h.Changed;
                pending |= h.Pending;
            }
            foreach (var c in g.Children) Walk(c);
        }
    }
}
