using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// The "Condition Summary" text of the Data Breakpoint Manager. ⭐ U-14 / C2 (docs/UX/DESIGN_Condition_Editor.md §2): FOLDED INTO the one sentence
/// renderer, <see cref="ConditionSentence"/> — a condition the friendly catalogue knows reads as its sentence, a debugging one keeps the technical line
/// (<c>Component: Health.Current &lt;= 20</c>, CE-1285). ⛔ SUPERSEDED: the second copy of that logic that lived here. Kept as the breakpoints' name for it.
/// </summary>
public static class BreakpointConditionSummarizer
{
    public static string Summarize(SearchPredicateDto? dto) => ConditionSentence.Of(dto);

    /// <summary>⭐ CE-1323 — a behaviour step as a sentence: "Withdrawing entered (squad-hsm)".</summary>
    public static string Step(BehaviourStepPredicateDto st) => ConditionSentence.Step(st);
}
