using System;
using System.Linq;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>⭐ <c>CE-1285</c> (Behaviour Debugging DBG-6, Q44-A/C, R-97) — what a breakpoint IS, for the one list's Kind column and filter.</summary>
public enum BreakpointKindOf
{
    /// <summary>A component / event / structural condition on data.</summary>
    Data,
    /// <summary>A graph node (BTree, HSM, Blueprint) breaks when it runs.</summary>
    Node,
    /// <summary>"Break when it changes its mind" — the unit's brain journal.</summary>
    Journal,
}

public static class BreakpointKind
{
    /// <summary>The kind of <paramref name="bp"/>: a node when it names a graph element or carries a node tag, a journal break when it
    /// watches the brain journal, else data.</summary>
    public static BreakpointKindOf Of(Breakpoint bp)
    {
        if (bp.SourceElementId.HasValue || bp.Condition is ExternalHitTagPredicateDto) return BreakpointKindOf.Node;
        return Mentions(bp.Condition, "BrainJournal") ? BreakpointKindOf.Journal : BreakpointKindOf.Data;
    }

    private static bool Mentions(SearchPredicateDto? p, string typeName) => p switch
    {
        PropertyMatchDto pm   => pm.ComponentType?.Name == typeName,
        CompoundPredicateDto c => c.Conditions.Any(x => Mentions(x, typeName)),
        _                     => false,
    };
}
