using System;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Converts a <see cref="SearchPredicateDto"/> to a short human-readable string
/// suitable for the "Condition Summary" column in the Data Breakpoint Manager window.
/// </summary>
public static class BreakpointConditionSummarizer
{
    public static string Summarize(SearchPredicateDto? dto) => dto switch
    {
        null                           => "(none)",
        PropertyMatchDto pm            => $"Component: {pm.ComponentType?.Name ?? "?"} {SummarizePredicate(pm.Predicate)}",
        TransientEventPredicateDto te  => $"Event: {te.EventType?.Name ?? "?"}",
        BehaviorParamPredicateDto bp   => $"BParam: {bp.BehaviorId}",
        StructuralPredicateDto st      => $"Structural: {st.ModificationType}",
        SpatialBoundingPredicateDto sp => $"Spatial",
        LifecyclePredicateDto lc       => $"Lifecycle: {lc.IdentifierType}",
        TraceBufferScanPredicateDto tr => $"Trace[0x{tr.OpCode:X2}]",
        CompoundPredicateDto cp        => $"Compound[{cp.Operator}]({cp.Conditions.Count})",
        BlueprintVariablePredicateDto bv => $"Blueprint: {bv.TargetBlueprintAssetId.ToString()[..8]}...",
        ExternalHitTagPredicateDto et  => $"Tag: {et.Tag}",
        _                              => dto.GetType().Name,
    };

    private static string SummarizePredicate(SearchPredicateDto? pred) => pred switch
    {
        NumericPredicateDto n  => $"[{n.MinValue}, {n.MaxValue}]",
        StringPredicateDto s   => $"\"{s.Substring}\"",
        _                      => string.Empty,
    };
}
