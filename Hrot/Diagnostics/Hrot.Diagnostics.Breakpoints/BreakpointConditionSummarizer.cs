using System;
using System.Linq;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Converts a <see cref="SearchPredicateDto"/> to a short human-readable string
/// suitable for the "Condition Summary" column in the Data Breakpoint Manager window.
/// <para>⭐ <c>CE-1285</c> (Behaviour Debugging DBG-6) — READABLE: a field and what it does (<c>BrainJournal.Seq changed</c>,
/// <c>Health.Current &lt;= 20</c>), a compound's parts joined. 🔴 It printed a "changed" condition's unbounded range raw —
/// <c>[-1.7976931348623157E+308, 1.7976931348623157E+308]</c> — the user's <i>"weird large numbers"</i>.</para>
/// </summary>
public static class BreakpointConditionSummarizer
{
    public static string Summarize(SearchPredicateDto? dto) => dto switch
    {
        null                           => "(none)",
        PropertyMatchDto pm            => Property(pm),
        TransientEventPredicateDto te  => $"Event: {te.EventType?.Name ?? "?"}",
        BehaviorParamPredicateDto bp   => $"BParam: {bp.BehaviorId}",
        StructuralPredicateDto st      => $"Structural: {st.ModificationType}",
        SpatialBoundingPredicateDto sp => $"Spatial",
        LifecyclePredicateDto lc       => $"Lifecycle: {lc.IdentifierType}",
        TraceBufferScanPredicateDto tr => $"Trace[0x{tr.OpCode:X2}]",
        CompoundPredicateDto cp        => $"Compound[{cp.Operator}]({cp.Conditions.Count}): "
                                          + string.Join(cp.Operator == LogicalOperator.And ? " AND " : " OR ", cp.Conditions.Select(Summarize)),
        BlueprintVariablePredicateDto bv => $"Blueprint: {bv.TargetBlueprintAssetId.ToString()[..8]}...",
        ExternalHitTagPredicateDto et  => et.Tag.StartsWith("btree:", StringComparison.Ordinal) ? "the node runs" : $"Tag: {et.Tag}",
        _                              => dto.GetType().Name,
    };

    /// <summary>"Component: Type.Path op value" — the field, never a raw infinite range.</summary>
    private static string Property(PropertyMatchDto pm)
    {
        string field = (pm.ComponentType?.Name ?? "?") + (string.IsNullOrEmpty(pm.PropertyPath) ? "" : "." + pm.PropertyPath);
        string what = pm.Operator switch
        {
            SearchOperator.Changed     => "changed",
            SearchOperator.Contains    => $"contains {Value(pm.Predicate)}",
            SearchOperator.StartsWith  => $"starts with {Value(pm.Predicate)}",
            SearchOperator.GreaterThan => $"> {Value(pm.Predicate)}",
            SearchOperator.LessThan    => $"< {Value(pm.Predicate)}",
            _                          => Value(pm.Predicate) is { Length: > 0 } v ? v : "",
        };
        return $"Component: {field}{(what.Length > 0 ? " " + what : "")}";
    }

    private static string Value(SearchPredicateDto? pred) => pred switch
    {
        NumericPredicateDto n  => Range(n.MinValue, n.MaxValue),
        StringPredicateDto s   => $"\"{s.Substring}\"",
        _                      => string.Empty,
    };

    /// <summary>A numeric range as people write it: <c>== 5</c>, <c>&gt;= 5</c>, <c>&lt;= 9</c>, <c>in [5, 9]</c>, or nothing when unbounded.</summary>
    private static string Range(double min, double max)
    {
        bool lo = min > double.MinValue / 2, hi = max < double.MaxValue / 2;
        if (lo && hi) return min == max ? $"== {min}" : $"in [{min}, {max}]";
        if (lo) return $">= {min}";
        if (hi) return $"<= {max}";
        return string.Empty;
    }
}
