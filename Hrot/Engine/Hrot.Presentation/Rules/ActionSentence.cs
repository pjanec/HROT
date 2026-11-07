using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Presentation.Behavior;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ CE-4307 (RX5, docs/UX/DESIGN_Rule_Editor_UX.md §2.4, rule-list-visual.svg) — a do STEP as words a person reads: <i>Message Blue: "Phase 2"</i> ·
/// <i>Order Alpha 1-1 to Move</i> · <i>Bring in forces/red-qrf at @Drop</i> · <i>End the exercise — Friend wins</i>. ⭐ The ONE step renderer: the List
/// line (<see cref="RuleSentence"/>) and the Do tab's rows (<c>ActionListFieldDrawer</c>) both call it, so they say the same.
/// Grammar words are the verb and its joiners; the rest is the author's content. Read from the step's args hydrated into the action's own params type
/// (the catalogue's <see cref="ActionEntry.ParamsType"/>), by field NAME — so the presentation needs no reference to the verbs' assembly.
/// An act the table does not know reads as its humanised name. Pure; callers cache it (the list per params string, the Do tab per row).
/// </summary>
public static class ActionSentence
{
    /// <summary>The step as text (rails, the Do tab's probe).</summary>
    public static string Of(ActionStep step, LiveRefScope? refs)
    {
        var segs = Parts(step, refs);
        if (segs.Length == 1) return segs[0].Text;
        var sb = new System.Text.StringBuilder();
        foreach (var s in segs) sb.Append(s.Text);
        return sb.ToString();
    }

    /// <summary>The step as segments (grammar / content), for the List view's two-colour line.</summary>
    public static RuleSentence.Seg[] Parts(ActionStep step, LiveRefScope? refs)
    {
        var entry = ActionListEdit.EntryOf(step.Act);
        string shortName = entry?.ShortName ?? ShortOf(step.Act);
        object? p = null;
        if (entry != null)
        {
            try { p = BehaviorParamsForm.Hydrate(entry.ParamsType, step.Args?.ToJsonString() ?? "{}"); }
            catch (Exception ex) when (ex is InvalidCastException or NullReferenceException or System.Text.Json.JsonException or ArgumentException) { p = null; }
        }
        var b = new Builder(p, refs);
        switch (shortName)
        {
            case "Message":
            {
                string to = b.Str("ToKind") switch
                {
                    "Side" => FactCatalogue.SideName((ForceId)(byte)b.Int("ToValue")),
                    "Station" => "station " + b.Int("ToValue").ToString(CultureInfo.InvariantCulture),
                    "Unit" => "unit #" + b.Int("ToValue").ToString(CultureInfo.InvariantCulture),
                    "Instructor" => "the instructor",
                    _ => "everyone",
                };
                b.G("Message ").C(to).G(": ").C("\"" + b.Str("Text") + "\"");
                break;
            }
            case "Order":
            {
                string what = b.Str("Behavior");
                string kind = what.StartsWith(Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix, StringComparison.Ordinal)
                    ? Humanize(what.Substring(Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix.Length))
                    : (what.Length == 0 ? "(no order yet)" : what);   // a behaviour name (Advanced) stays as written
                b.G("Order ").C(b.Link("Target")).G(" to ").C(kind);
                break;
            }
            case "MissionCommand":
                if (b.Str("Command") == "Jump") b.G("Jump ").C(b.Link("Target")).G("'s plan to task ").C((b.Int("TaskIndex") + 1).ToString(CultureInfo.InvariantCulture));
                else b.G("Stop ").C(b.Link("Target")).G("'s plan");
                break;
            case "SetObjectiveActive":
                b.G(b.Bool("Active") ? "Activate " : "Deactivate ").C(b.Link("Target"));
                break;
            case "AddToCounter":
            {
                double amount = b.Num("Amount");
                b.G(amount < 0 ? "Take " : "Add ").C(Math.Abs(amount).ToString("0.##", CultureInfo.InvariantCulture)).G(amount < 0 ? " from " : " to ").C(b.Link("Target"));
                break;
            }
            case "InsertMacro":
                b.G("Bring in ").C(Last(b.Str("Macro"))).G(" at ").C(b.Point("At"));
                break;
            case "EndExercise":
            {
                b.G("End the exercise");
                string winner = b.Str("Winner");
                if (winner == "Draw") b.G(" — ").C("a draw");
                else if (winner.Length > 0) b.G(" — ").C(winner).G(" wins");
                break;
            }
            case "SetSkillAmplifier":
            {
                int t = b.Int("Tiers");
                b.G("Set the skill of ").C(FactCatalogue.SideName((ForceId)(byte)b.Int("Side"))).G(" to ").C((t > 0 ? "+" : "") + t.ToString(CultureInfo.InvariantCulture));
                break;
            }
            default:
                b.G(Humanize(shortName));
                break;
        }
        return b.Done();
    }

    private static string ShortOf(string act) { int dot = act.LastIndexOf('.'); return dot >= 0 ? act.Substring(dot + 1) : act; }
    private static string Last(string path) { int slash = path.LastIndexOf('/'); return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path; }
    private static string Humanize(string s) => StructEdit.Core.Attributes.DisplayNames.Humanize(s);

    /// <summary>Reads the hydrated params by field name (null params ⇒ empty values, so a step whose args do not read still says its verb).</summary>
    private sealed class Builder
    {
        private readonly object? _p;
        private readonly LiveRefScope? _refs;
        private readonly List<RuleSentence.Seg> _segs = new(6);
        public Builder(object? p, LiveRefScope? refs) { _p = p; _refs = refs; }

        public Builder G(string text) { _segs.Add(new RuleSentence.Seg(text, true)); return this; }
        public Builder C(string text) { _segs.Add(new RuleSentence.Seg(text, false)); return this; }
        public RuleSentence.Seg[] Done() => _segs.ToArray();

        private object? Field(string name)
        {
            if (_p == null) return null;
            var t = _p.GetType();
            return t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(_p)
                ?? t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(_p);
        }

        public string Str(string name) => Field(name) switch { null => string.Empty, var v => v.ToString() ?? string.Empty };
        public int Int(string name) => Field(name) switch { int i => i, byte by => by, Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture), _ => 0 };
        public bool Bool(string name) => Field(name) is true;
        public double Num(string name) => Field(name) switch { float f => f, double d => d, int i => i, _ => 0 };

        public string Link(string name)
        {
            if (Field(name) is not EntityLink link) return "(nobody)";
            if (link.Ref.IsNone && link.Spec.IsNone) return "(nobody)";
            if (link.Ref.IsNone && _refs != null && _refs.TryResolve(link.Spec, out var resolved)) return _refs.ChipLabelOf(resolved);
            return ConditionSentence.NameOf(link, _refs);
        }

        public string Point(string name)
        {
            if (Field(name) is not PointLink at) return "(no place)";
            if (at.Spec.Form == RefForm.Key) return "@" + at.Spec.KeyText;
            if (!at.Spec.IsNone && _refs != null && _refs.TryResolve(at.Spec, out var e)) return _refs.ChipLabelOf(e);
            return at.Spec.IsNone && at.Local.Equals(default(WorldPoint)) ? "(no place)" : "a point on the map";
        }
    }
}
