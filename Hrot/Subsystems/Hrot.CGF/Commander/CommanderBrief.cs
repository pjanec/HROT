using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐ <c>CE-4680</c> B-2 (DESIGN_LLM_Commander §5b) — the commander's mission and intent, as his scenario gives it: Task · Purpose · Constraints · Time
/// (each up to 127 UTF-8 bytes). Read once into the seat's briefing prefix. ⭐ Authoring-only and <c>NoReplay</c>: it never changes in a run.
/// ⭐ UNMANAGED (CLAUDE.md "NEW COMPONENTS") so the scenario's auto-serializer loads and saves it as it does <see cref="CommanderSeat"/>
/// (<c>"CommanderBrief": {"Task": …}</c>). ⛔ SUPERSEDED: a managed record — 📌 measured, the Haiku run: a managed component needs a scenario
/// translator, and without one the brief never loaded (the rail set it directly and hid it).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.CommanderBrief)]
[DataPolicy(DataPolicy.NoReplay)]
public struct CommanderBrief
{
    /// <summary>What to do ("seize the sheds at house").</summary>
    public FixedString128 Task;
    /// <summary>Why ("so the company can cross the road unobserved").</summary>
    public FixedString128 Purpose;
    /// <summary>What not to do ("keep a squad in reserve").</summary>
    public FixedString128 Constraints;
    /// <summary>By when ("by 00:06").</summary>
    public FixedString128 Time;

    /// <summary>The prefix line; empty when nothing is set (a seat's open — not per frame).</summary>
    public readonly string Line()
    {
        var parts = new System.Collections.Generic.List<string>(4);
        void Add(string label, in FixedString128 v) { string t = v.ToString().Trim(); if (t.Length > 0) parts.Add(label + t); }
        Add("Task: ", Task); Add("Purpose: ", Purpose); Add("Constraints: ", Constraints); Add("Time: ", Time);
        return parts.Count == 0 ? "" : "Mission — " + string.Join(" · ", parts);
    }
}

/// <summary>
/// ⭐ <c>CE-4680</c> B-6 (§5b) — a one-line terrain note on a named place (a Point graphic or a tactical area): <i>"house: sheds, one storey, open
/// ground to the east"</i>. Listed with the places in the briefing prefix. Authoring-only, unmanaged and <c>NoReplay</c>, as <see cref="CommanderBrief"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.PlaceNote)]
[DataPolicy(DataPolicy.NoReplay)]
public struct PlaceNote
{
    public FixedString128 Text;
}
