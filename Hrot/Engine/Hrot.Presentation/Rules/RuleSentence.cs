using System;
using System.Collections.Generic;
using System.Text;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ U-18 / RX3 (docs/UX/DESIGN_Rule_Editor_UX.md §2.1, rule-list-visual.svg) — a rule as ONE line a person reads in the List view:
/// a trigger <i>"When Alpha 1-1 is inside the area for 30 s → Message, Begin phase"</i>, an objective <i>"Take and hold: OBJ BLUE — required"</i>.
/// ⭐ The condition's words are <see cref="ConditionSentence"/>'s — the ONE renderer the condition editor uses, so the list and the editor say the same.
/// An incomplete rule says so (<see cref="Incomplete"/>). Pure; a caller caches it per params string (the list draws every frame).
/// </summary>
public static class RuleSentence
{
    /// <summary>One run of the line: <see cref="Grammar"/> words (<i>When · for · s · →</i>, an act name, an objective's kind words) draw in the grammar blue; the rest is the author's content (the condition, the title) in the default colour.</summary>
    public readonly record struct Seg(string Text, bool Grammar);

    /// <summary>The arrow's text in <see cref="Of"/>'s <c>Text</c> and in the segments — ⚠ the UI font has no U+2192, so the List view draws the Font Awesome arrow where a segment is exactly this.</summary>
    public const string Arrow = " → ";

    /// <summary>The line for a rule of kind <paramref name="kind"/> (its behaviour name) with params <paramref name="json"/> — text only (rails, <c>ListRows</c>).</summary>
    public static (string Text, bool Incomplete) Of(string kind, string json, string title, bool required, LiveRefScope? refs)
    {
        var (text, incomplete, _) = Parts(kind, json, title, required, refs);
        return (text, incomplete);
    }

    /// <summary>⭐ The same line as SEGMENTS (text, isGrammar), for the List view's two-colour drawing. The condition's own words stay ONE content segment (<see cref="ConditionSentence"/> has no grammar split). ⚠ An objective's <i>required</i> is NOT a segment: the list draws it right-aligned and muted.</summary>
    public static (string Text, bool Incomplete, Seg[] Segments) Parts(string kind, string json, string title, bool required, LiveRefScope? refs)
    {
        var segs = new List<Seg>(8);
        if (kind == "Trigger")
        {
            TriggerParamsJsonDto t;
            try { t = (TriggerParamsJsonDto)Hrot.Presentation.Behavior.BehaviorParamsForm.Hydrate(typeof(TriggerParamsJsonDto), json); }
            catch (Exception ex) when (ex is InvalidCastException or NullReferenceException or System.Text.Json.JsonException)
            { return ($"{title}: (its params do not read)", true, new[] { new Seg(title, false), new Seg(": (its params do not read)", false) }); }
            var sb = new StringBuilder();
            void Add(string text, bool grammar) { sb.Append(text); segs.Add(new Seg(text, grammar)); }
            Add("When ", true);
            bool incomplete = t.When == null;
            Add(t.When == null ? "(no condition yet)" : Lower(ConditionSentence.Of(t.When, refs)), false);
            if (t.HoldFor > 0) { Add(" for ", true); Add(FactCatalogue.Num(t.HoldFor), false); Add(" s", true); }
            Add(Arrow, true);
            if (t.Do is not { Count: > 0 }) { Add("(nothing to do yet)", false); incomplete = true; }
            else
                for (int i = 0; i < t.Do.Count; i++)
                {
                    if (i > 0) Add(", ", true);
                    foreach (var seg in ActionSentence.Parts(t.Do[i], refs)) Add(seg.Text, seg.Grammar);   // ⭐ CE-4307 (RX5) — the ONE step renderer
                }
            return (sb.ToString(), incomplete, segs.ToArray());
        }
        string what = StructEdit.Core.Attributes.DisplayNames.Humanize(kind);
        return ($"{what}: {title}{(required ? " — required" : "")}", false, new[] { new Seg(what, true), new Seg(": ", true), new Seg(title, false) });
    }

    /// <summary>
    /// ⭐ W-1 (P-8, mission-plan-detail-visual.svg B) — how a PHASE begins, in the Phases group: <i>"begins when …"</i> (the trigger's <c>when</c>,
    /// the ONE condition sentence), <i>"begins at the start"</i> for a scenario time of 0, plus <i>" · latest m:ss"</i> when it has a no-later-than.
    /// </summary>
    public static string PhaseBegins(string json, float deadlineSeconds, LiveRefScope? refs)
    {
        string when;
        try
        {
            var t = (TriggerParamsJsonDto)Hrot.Presentation.Behavior.BehaviorParamsForm.Hydrate(typeof(TriggerParamsJsonDto), json);
            when = t.When switch
            {
                null => "begins when (no condition yet)",
                Fdp.Toolkit.ReplayBrowser.Search.ScenarioTimePredicateDto { FromSeconds: <= 0 } => "begins at the start",
                var w => "begins when " + Lower(ConditionSentence.Of(w, refs)),
            };
        }
        catch (Exception ex) when (ex is InvalidCastException or NullReferenceException or System.Text.Json.JsonException) { when = "begins — its params do not read"; }
        return deadlineSeconds > 0 ? $"{when} · latest {Hrot.UI.Common.Rules.RulesModel.Clock(deadlineSeconds)}" : when;
    }

    /// <summary>A sentence inside a sentence starts lower-case (<i>When at least 3 of Red side…</i>) — ⚠ but a name keeps its case (it starts the WHO).</summary>
    private static string Lower(string s)
        => s.Length > 1 && char.IsUpper(s[0]) && char.IsLower(s[1]) && (s.StartsWith("It ", StringComparison.Ordinal) || s.StartsWith("Any ", StringComparison.Ordinal)
               || s.StartsWith("All ", StringComparison.Ordinal) || s.StartsWith("At ", StringComparison.Ordinal) || s.StartsWith("None ", StringComparison.Ordinal))
            ? char.ToLowerInvariant(s[0]) + s.Substring(1) : s;

    /// <summary>The rule entity's authored behaviour name and params JSON (its <c>BehaviorStartRecord</c>), or null — the ONE reader (the editor's and the list's).</summary>
    public static (string Name, string Json)? ParamsOf(Fdp.Core.EntityRepository? w, Fdp.Core.Entity e)
    {
        if (w == null || !w.IsAlive(e) || !w.TryGetTable(typeof(Fdp.Toolkit.Behavior.Components.BehaviorStartRecord), out _) || !w.HasManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>(e)) return null;
        var rec = ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>(e);
        return rec == null ? null : (rec.BehaviorName, rec.JsonParams);
    }
}
