using System.Collections.Generic;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ <c>CE-2377</c> step 1 — the MEASURED violators of <c>BehaviorParamsValidation.Validate</c> across every checked-in scenario, grouped
/// <c>"behaviour :: problem"</c> → the number of scenarios carrying it. ⛔ It may only SHRINK (the CE-2130 pattern): a new group, or a
/// higher count, fails the sweep; a group that no longer occurs (or fewer scenarios) fails asking to lower it. Each group's reason is
/// in the comment above it; the load refusal (step 2) is switched on only when this list is empty or the user rules per group.
/// </summary>
internal static class BehaviorParamsSweepAllowList
{
    internal static readonly Dictionary<string, int> Groups = new(System.StringComparer.Ordinal)
    {
        // (empty — measured 2026-11-02: 601 slots over scenarios/, recipes/, macros/ — 0 findings)
    };
}
