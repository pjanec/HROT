using Fdp.Core;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐ <c>CE-4680</c> B-2 (DESIGN_LLM_Commander §5b) — the commander's mission and intent, as his scenario gives it: Task · Purpose · Constraints · Time.
/// Read once into the seat's briefing prefix. ⭐ Authoring-only: a managed, immutable record (CLAUDE.md "NEW COMPONENTS"), <c>NoReplay</c> — it never
/// changes in a run, so it is never recorded and is loaded with the scenario (<c>"CommanderBrief": {"Task": …}</c>).
/// </summary>
[ComponentId(GlobalComponentIds.CommanderBrief)]
[DataPolicy(DataPolicy.NoReplay)]
public sealed record CommanderBrief
{
    /// <summary>What to do ("seize the sheds at house").</summary>
    public string Task { get; init; } = "";
    /// <summary>Why ("so the company can cross the road unobserved").</summary>
    public string Purpose { get; init; } = "";
    /// <summary>What not to do ("do not enter Block A; keep a squad in reserve").</summary>
    public string Constraints { get; init; } = "";
    /// <summary>By when ("the house by 00:06").</summary>
    public string Time { get; init; } = "";

    /// <summary>The prefix line; empty when nothing is set.</summary>
    public string Line()
    {
        var parts = new System.Collections.Generic.List<string>(4);
        if (Task.Length > 0) parts.Add("Task: " + Task);
        if (Purpose.Length > 0) parts.Add("Purpose: " + Purpose);
        if (Constraints.Length > 0) parts.Add("Constraints: " + Constraints);
        if (Time.Length > 0) parts.Add("Time: " + Time);
        return parts.Count == 0 ? "" : "Mission — " + string.Join(" · ", parts);
    }
}

/// <summary>
/// ⭐ <c>CE-4680</c> B-6 (§5b) — a one-line terrain note on a named place (a Point graphic or a tactical area): <i>"house: sheds, one storey, open
/// ground to the east"</i>. Listed with the places in the briefing prefix. Authoring-only, as <see cref="CommanderBrief"/>.
/// </summary>
[ComponentId(GlobalComponentIds.PlaceNote)]
[DataPolicy(DataPolicy.NoReplay)]
public sealed record PlaceNote
{
    public string Text { get; init; } = "";
}
