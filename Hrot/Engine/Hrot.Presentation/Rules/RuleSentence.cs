using System;
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
    /// <summary>The line for a rule of kind <paramref name="kind"/> (its behaviour name) with params <paramref name="json"/>.</summary>
    public static (string Text, bool Incomplete) Of(string kind, string json, string title, bool required, LiveRefScope? refs)
    {
        if (kind == "Trigger")
        {
            TriggerParamsJsonDto t;
            try { t = (TriggerParamsJsonDto)Hrot.Presentation.Behavior.BehaviorParamsForm.Hydrate(typeof(TriggerParamsJsonDto), json); }
            catch (Exception ex) when (ex is InvalidCastException or NullReferenceException or System.Text.Json.JsonException) { return ($"{title}: (its params do not read)", true); }
            var sb = new StringBuilder("When ");
            bool incomplete = t.When == null;
            sb.Append(t.When == null ? "(no condition yet)" : Lower(ConditionSentence.Of(t.When, refs)));
            if (t.HoldFor > 0) sb.Append(" for ").Append(FactCatalogue.Num(t.HoldFor)).Append(" s");
            sb.Append(" → ");
            if (t.Do is not { Count: > 0 }) { sb.Append("(nothing to do yet)"); incomplete = true; }
            else
                for (int i = 0; i < t.Do.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(StructEdit.Core.Attributes.DisplayNames.Humanize(t.Do[i].Act));
                }
            return (sb.ToString(), incomplete);
        }
        string what = StructEdit.Core.Attributes.DisplayNames.Humanize(kind);
        return ($"{what}: {title}{(required ? " — required" : "")}", false);
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
