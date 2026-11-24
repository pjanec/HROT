using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.CGF.Commander;

/// <summary>⭐ <c>CE-4684</c> (DESIGN_LLM_Commander §5c C-2) — what a condition watches. One flat shape (<see cref="Condition"/>), no <c>oneOf</c>.</summary>
public enum ConditionOn : byte { Immediate, AtTime, UnitAt, UnitTaskComplete, Contact, StrengthBelow, EnemySeenAt }

/// <summary>⭐ <c>CE-4684</c> C-2 — one condition: <see cref="On"/> plus the fields it needs (a unit, a place id, a clock second, a percent).</summary>
public sealed record Condition(ConditionOn On, string? Unit = null, string? At = null, double TimeS = 0, int Percent = 0)
{
    /// <summary>As the briefing says it back (<c>"UNIT_AT Alpha Squad objective"</c>).</summary>
    public string Text => On switch
    {
        ConditionOn.Immediate => "IMMEDIATE",
        ConditionOn.AtTime => $"AT_TIME {CommanderFeedSystem.Clock(TimeS)}",
        ConditionOn.UnitAt => $"UNIT_AT {Unit} {At}",
        ConditionOn.UnitTaskComplete => $"UNIT_TASK_COMPLETE {Unit}",
        ConditionOn.Contact => $"CONTACT {Unit}",
        ConditionOn.StrengthBelow => $"STRENGTH_BELOW {Unit} {Percent}%",
        _ => $"ENEMY_SEEN_AT {At}",
    };
}

/// <summary>⭐ <c>CE-4684</c> C-1/C-2 — one unit's order in a decision: the task (an order kind, a play for the commander himself, or CONTINUE), where,
/// when it starts / completes / aborts, and why. ⚠ The design's <c>Via</c> / <c>Orient</c> are not here: no order on this path takes them (§5c as built).</summary>
public sealed record UnitOrder(string Unit, string Task, string? Objective, string? SupportAt, Condition? Start, Condition? CompleteWhen,
                               IReadOnlyList<Condition> AbortWhen, string Purpose);

/// <summary>⭐ <c>CE-4684</c> C-2 — "if this happens, give these orders" — run by the watcher, no model call.</summary>
public sealed record Contingency(Condition Trigger, IReadOnlyList<UnitOrder> Orders);

/// <summary>⭐⭐ <c>CE-4684</c> (DESIGN_LLM_Commander §5c) — ONE decision per wake, in the order the model must reason: the cycle it answers, the
/// assessment, the intent, the orders, the contingencies, when to decide next.</summary>
public sealed record Decision(long Cycle, string Situation, string EnemyLikelyCoa, string MainRisk, string MainEffort, string Scheme, string EndState,
                              IReadOnlyList<UnitOrder> Orders, IReadOnlyList<Contingency> Contingencies, int NextDecisionS, IReadOnlyList<Condition> WakeOn);

/// <summary>⭐ <c>CE-4684</c> (§5c) — a mind that answers with <see cref="Decision"/>s: its seat speaks ids only (C-6), gets the seat's ids on each briefing,
/// and is paced by its own next decision (C-3). The chat-model mind is one unless its format is L-3a's tool calls; a rail's mind may be one.</summary>
public interface IDecisionMind
{
    bool AnswersWithDecisions { get; }
}

/// <summary>⭐ <c>CE-4684</c> C-6 — the ids a seat's decision may use: himself (for a play), his squads, his side's places. Nothing else is a position.</summary>
public sealed record SeatVocabulary(string Self, IReadOnlyList<string> Units, IReadOnlyList<string> Places)
{
    /// <summary>⭐ CE-4685 — the order KINDS his own unit takes as a whole (unit = himself); the routing table picks the behaviour. Default: the four.</summary>
    public IReadOnlyList<string> OwnOrders { get; init; } = CommanderActionSystem.OwnOrders;

    /// <summary>The decision's tasks for this seat: the subordinates' order kinds, his own unit's kinds, CONTINUE (each once).</summary>
    public string[] Tasks()
    {
        var l = new List<string>(CommanderActionSystem.Orders);
        foreach (var k in OwnOrders) if (!l.Contains(k)) l.Add(k);
        l.Add(DecisionContract.Continue);
        return l.ToArray();
    }
}

/// <summary>
/// ⭐⭐ <c>CE-4684</c> (DESIGN_LLM_Commander §5c C-1, C-6) — the decision contract: the JSON schema a chat model's output is decoded against (Ollama's
/// <c>format</c>), the reader that turns its answer into a <see cref="Decision"/>, and the validation (ids exist, the task fits the unit, the counts) whose
/// errors go back to the model ONCE. One place: the chat mind, the stand-ins and the rails use it.
/// </summary>
public static class DecisionContract
{
    public const string Continue = "CONTINUE";
    public const int MaxOrders = 6, MaxContingencies = 3, MaxContingencyOrders = 3, MaxAborts = 2, MaxWakeOn = 3;
    public const int MinNextS = 10, MaxNextS = 300;

    /// <summary>The tasks: the order kinds (a subordinate's, his own unit's — CE-4685), CONTINUE.</summary>
    public static readonly string[] Tasks = new SeatVocabulary("", Array.Empty<string>(), Array.Empty<string>()).Tasks();

    public static readonly string[] ConditionNames = { "IMMEDIATE", "AT_TIME", "UNIT_AT", "UNIT_TASK_COMPLETE", "CONTACT", "STRENGTH_BELOW", "ENEMY_SEEN_AT" };

    private static string[] Concat(params string[][] parts)
    {
        var l = new List<string>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }

    private static JsonArray Enum(IEnumerable<string> values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(v);
        return a;
    }

    private static JsonObject Str() => new() { ["type"] = "string" };

    /// <summary>The schema for one seat — the id fields are ENUMS of that seat's ids, so a decoder that follows it cannot name anything else (C-6).</summary>
    public static JsonObject Schema(SeatVocabulary v)
    {
        var units = new List<string> { v.Self };
        units.AddRange(v.Units);
        JsonObject Cond() => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["on"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(ConditionNames) },
                ["unit"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(v.Units) },
                ["at"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(v.Places) },
                ["time_s"] = new JsonObject { ["type"] = "integer" },
                ["percent"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = new JsonArray("on"),
        };
        JsonObject Order(bool full)
        {
            var props = new JsonObject
            {
                ["unit"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(units) },
                ["task"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(v.Tasks()) },
                ["objective"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(v.Places) },
                ["support_at"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(v.Places) },
            };
            if (full)
            {
                props["start"] = Cond();
                props["complete_when"] = Cond();
                props["abort_when"] = new JsonObject { ["type"] = "array", ["maxItems"] = MaxAborts, ["items"] = Cond() };
            }
            props["purpose"] = Str();
            return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray("unit", "task", "purpose") };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["cycle"] = new JsonObject { ["type"] = "integer" },
                ["assessment"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["situation"] = Str(), ["enemy_likely_coa"] = Str(), ["main_risk"] = Str() },
                    ["required"] = new JsonArray("situation", "enemy_likely_coa", "main_risk"),
                },
                ["intent"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["main_effort"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(units) }, ["scheme"] = Str(), ["end_state"] = Str(),
                    },
                    ["required"] = new JsonArray("main_effort", "scheme", "end_state"),
                },
                ["orders"] = new JsonObject { ["type"] = "array", ["maxItems"] = MaxOrders, ["items"] = Order(true) },
                ["contingencies"] = new JsonObject
                {
                    ["type"] = "array", ["maxItems"] = MaxContingencies,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["trigger"] = Cond(), ["orders"] = new JsonObject { ["type"] = "array", ["maxItems"] = MaxContingencyOrders, ["items"] = Order(false) },
                        },
                        ["required"] = new JsonArray("trigger", "orders"),
                    },
                },
                ["next_decision"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["not_later_than_s"] = new JsonObject { ["type"] = "integer", ["minimum"] = MinNextS, ["maximum"] = MaxNextS },
                        ["wake_on"] = new JsonObject { ["type"] = "array", ["maxItems"] = MaxWakeOn, ["items"] = Cond() },
                    },
                    ["required"] = new JsonArray("not_later_than_s"),
                },
            },
            ["required"] = new JsonArray("cycle", "assessment", "intent", "orders", "contingencies", "next_decision"),
        };
    }

    /// <summary>
    /// The answer's text → a <see cref="Decision"/>, validated against the seat's ids and <paramref name="cycle"/>. Every problem is added to
    /// <paramref name="problems"/> in words the model can act on; a decision with ANY problem is not returned (all or nothing — the model repairs the
    /// whole of it, else the seat CONTINUEs).
    /// </summary>
    public static Decision? Read(string? content, SeatVocabulary v, long cycle, List<string> problems)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(Unfence(content ?? ""))?.AsObject(); }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { problems.Add($"the answer is not one JSON object ({e.Message})"); return null; }
        if (o == null) { problems.Add("the answer is empty — send the decision object"); return null; }

        long echoed = Long(o["cycle"]) ?? -1;
        if (echoed != cycle) problems.Add($"cycle is {(echoed < 0 ? "missing" : echoed.ToString(CultureInfo.InvariantCulture))} — it must be {cycle} (the CYCLE of this briefing)");
        var a = o["assessment"] as JsonObject;
        var i = o["intent"] as JsonObject;
        if (a == null) problems.Add("assessment is missing");
        if (i == null) problems.Add("intent is missing");
        string mainEffort = S(i?["main_effort"]);
        if (i != null && !IsUnit(v, mainEffort, self: true)) problems.Add($"intent.main_effort '{mainEffort}' is not one of {List(v.Self, v.Units)}");

        var orders = ReadOrders(o["orders"] as JsonArray, v, "orders", MaxOrders, full: true, problems);
        // his own unit's order coordinates the squads; a squad order in the same decision would END it (§5b) — say so instead of letting it happen
        bool play = false, squadOrder = false;
        foreach (var uo in orders)
        {
            if (uo.Unit == v.Self && uo.Task != Continue && (uo.Start == null || uo.Start.On == ConditionOn.Immediate)) play = true;
            else if (uo.Unit != v.Self && uo.Task != Continue && (uo.Start == null || uo.Start.On == ConditionOn.Immediate)) squadOrder = true;
        }
        if (play && squadOrder) problems.Add($"orders give {v.Self} (the whole unit) an order AND a subordinate an order now — the subordinate's order ENDS the whole unit's: give the subordinates CONTINUE (the whole unit's order orders them), or no order to {v.Self}");
        var contingencies = new List<Contingency>();
        if (o["contingencies"] is JsonArray cs)
        {
            if (cs.Count > MaxContingencies) problems.Add($"contingencies has {cs.Count} — at most {MaxContingencies}");
            for (int k = 0; k < cs.Count && k < MaxContingencies; k++)
            {
                var c = cs[k] as JsonObject;
                var trigger = ReadCondition(c?["trigger"], v, $"contingencies[{k}].trigger", problems);
                if (trigger?.On == ConditionOn.Immediate) problems.Add($"contingencies[{k}].trigger is IMMEDIATE — that is an order, not a contingency");
                var cos = ReadOrders(c?["orders"] as JsonArray, v, $"contingencies[{k}].orders", MaxContingencyOrders, full: false, problems);
                if (cos.Count == 0) problems.Add($"contingencies[{k}] has no orders");
                if (trigger != null) contingencies.Add(new Contingency(trigger, cos));
            }
        }
        else if (o["contingencies"] != null) problems.Add("contingencies must be a list");

        var next = o["next_decision"] as JsonObject;
        int nextS = (int)(Long(next?["not_later_than_s"]) ?? -1);
        if (next == null || nextS < 0) problems.Add($"next_decision.not_later_than_s is missing ({MinNextS}…{MaxNextS} s)");
        var wake = new List<Condition>();
        if (next?["wake_on"] is JsonArray ws)
        {
            if (ws.Count > MaxWakeOn) problems.Add($"next_decision.wake_on has {ws.Count} — at most {MaxWakeOn}");
            for (int k = 0; k < ws.Count && k < MaxWakeOn; k++)
                if (ReadCondition(ws[k], v, $"next_decision.wake_on[{k}]", problems) is { } w) wake.Add(w);
        }
        if (problems.Count > 0) return null;
        return new Decision(echoed, S(a!["situation"]), S(a["enemy_likely_coa"]), S(a["main_risk"]), mainEffort, S(i!["scheme"]), S(i["end_state"]),
                            orders, contingencies, Math.Clamp(nextS, MinNextS, MaxNextS), wake);
    }

    private static List<UnitOrder> ReadOrders(JsonArray? arr, SeatVocabulary v, string where, int max, bool full, List<string> problems)
    {
        var list = new List<UnitOrder>();
        if (arr == null) { problems.Add($"{where} must be a list (empty when every unit CONTINUEs)"); return list; }
        if (arr.Count > max) problems.Add($"{where} has {arr.Count} — at most {max}");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int k = 0; k < arr.Count && k < max; k++)
        {
            string at = $"{where}[{k}]";
            if (arr[k] is not JsonObject o) { problems.Add($"{at} is not an object"); continue; }
            string unit = S(o["unit"]), task = S(o["task"]);
            string? objective = Opt(o["objective"]), supportAt = Opt(o["support_at"]);
            // ⭐ CE-4685 — the same KIND for a subordinate or for his whole unit (unit = himself); the routing table picks the behaviour
            bool own = Contains(v.OwnOrders, task);
            bool isOrder = Array.IndexOf(CommanderActionSystem.Orders, task) >= 0;
            if (!IsUnit(v, unit, self: true)) { problems.Add($"{at}.unit '{unit}' is not one of {List(v.Self, v.Units)}"); continue; }
            if (!seen.Add(unit)) problems.Add($"{at}: {unit} has two orders in one list — one each");
            if (!own && !isOrder && task != Continue) { problems.Add($"{at}.task '{task}' is not one of {string.Join(", ", v.Tasks())}"); continue; }
            if (unit == v.Self && !own && task != Continue) problems.Add($"{at}: {task} is not an order {v.Self}'s whole unit takes — {string.Join(", ", v.OwnOrders)}");
            if (unit != v.Self && !isOrder && task != Continue) problems.Add($"{at}: {task} is the whole unit's order — unit must be '{v.Self}', not {unit}");
            bool needsPlace = task is not (Continue or "Hold") && !task.EndsWith("Hold", StringComparison.Ordinal);
            if (needsPlace && objective == null) problems.Add($"{at}.objective is missing — {task} needs a place id");
            if (objective != null && !Has(v.Places, objective)) problems.Add($"{at}.objective '{objective}' is not a place id: {string.Join(", ", v.Places)}");
            if (supportAt != null && !Has(v.Places, supportAt)) problems.Add($"{at}.support_at '{supportAt}' is not a place id: {string.Join(", ", v.Places)}");
            Condition? start = null, complete = null;
            var aborts = new List<Condition>();
            if (full)
            {
                if (o["start"] != null) start = ReadCondition(o["start"], v, $"{at}.start", problems);
                if (o["complete_when"] != null) complete = ReadCondition(o["complete_when"], v, $"{at}.complete_when", problems);
                if (o["abort_when"] is JsonArray ab)
                {
                    if (ab.Count > MaxAborts) problems.Add($"{at}.abort_when has {ab.Count} — at most {MaxAborts}");
                    for (int j = 0; j < ab.Count && j < MaxAborts; j++)
                        if (ReadCondition(ab[j], v, $"{at}.abort_when[{j}]", problems) is { } c) aborts.Add(c);
                }
            }
            list.Add(new UnitOrder(unit, task, objective, supportAt, start, complete, aborts, S(o["purpose"])));
        }
        return list;
    }

    private static Condition? ReadCondition(JsonNode? n, SeatVocabulary v, string at, List<string> problems)
    {
        if (n is not JsonObject o) { problems.Add($"{at} is not a condition object"); return null; }
        string on = S(o["on"]);
        int idx = Array.IndexOf(ConditionNames, on);
        if (idx < 0) { problems.Add($"{at}.on '{on}' is not one of {string.Join(", ", ConditionNames)}"); return null; }
        var kind = (ConditionOn)idx;
        string? unit = Opt(o["unit"]), place = Opt(o["at"]);
        double time = Long(o["time_s"]) ?? -1;
        int percent = (int)(Long(o["percent"]) ?? -1);
        bool needUnit = kind is ConditionOn.UnitAt or ConditionOn.UnitTaskComplete or ConditionOn.Contact or ConditionOn.StrengthBelow;
        bool needPlace = kind is ConditionOn.UnitAt or ConditionOn.EnemySeenAt;
        int before = problems.Count;
        if (needUnit && !IsUnit(v, unit, self: false)) problems.Add($"{at}: {on} needs unit — one of {string.Join(", ", v.Units)} (got '{unit}')");
        if (needPlace && (place == null || !Has(v.Places, place))) problems.Add($"{at}: {on} needs at — a place id: {string.Join(", ", v.Places)} (got '{place}')");
        if (kind == ConditionOn.AtTime && time < 0) problems.Add($"{at}: AT_TIME needs time_s (the clock in seconds)");
        if (kind == ConditionOn.StrengthBelow && percent is < 1 or > 100) problems.Add($"{at}: STRENGTH_BELOW needs percent 1…100");
        if (problems.Count > before) return null;
        return new Condition(kind, needUnit ? unit : null, needPlace ? place : null, Math.Max(0, time), Math.Max(0, percent));
    }

    /// <summary>A model that fenced its JSON (```json … ```) is read without the fence — the object is what counts.</summary>
    private static string Unfence(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        int nl = s.IndexOf('\n');
        int end = s.LastIndexOf("```", StringComparison.Ordinal);
        return nl > 0 && end > nl ? s.Substring(nl + 1, end - nl - 1) : s;
    }

    private static bool Contains(IReadOnlyList<string> list, string s)
    {
        for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], s, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsUnit(SeatVocabulary v, string? unit, bool self) => unit != null && ((self && unit == v.Self) || Has(v.Units, unit));
    private static bool Has(IReadOnlyList<string> list, string s)
    {
        for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], s, StringComparison.Ordinal)) return true;
        return false;
    }
    private static string List(string self, IReadOnlyList<string> units) => self + ", " + string.Join(", ", units);
    private static string S(JsonNode? n) => n is JsonValue jv && jv.TryGetValue(out string? s) ? s ?? "" : "";
    private static string? Opt(JsonNode? n) => S(n) is { Length: > 0 } s ? s : null;
    private static long? Long(JsonNode? n)
    {
        if (n is not JsonValue jv) return null;
        if (jv.TryGetValue(out long l)) return l;
        if (jv.TryGetValue(out double d)) return (long)Math.Round(d);
        if (jv.TryGetValue(out string? s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
        return null;
    }

    /// <summary>The decision's estimate — the assessment and intent, said once (it replaces the last one, as a note does).</summary>
    public static string Estimate(Decision d)
        => $"ASSESSMENT {d.Situation} Enemy likely: {d.EnemyLikelyCoa} Main risk: {d.MainRisk} INTENT main effort {d.MainEffort}: {d.Scheme} End state: {d.EndState}";

    /// <summary>One order as the action system's action: a play (the commander's own), Hold, or a squad order. CONTINUE has none.</summary>
    public static CommanderAction? ToAction(UnitOrder o, SeatVocabulary v)
    {
        if (o.Task == Continue) return null;
        if (o.Unit == v.Self) return new CommanderAction(CommanderActionKind.RunPlay, Order: o.Task, Where: o.Objective, Target: o.SupportAt);
        if (o.Task == "Hold") return new CommanderAction(CommanderActionKind.Hold, o.Unit);
        // ⭐ CE-4686 ③ — a base of fire STANDS at support_at and fires at the objective (without support_at it stands at the objective)
        if (o.Task == "SupportByFire" && o.SupportAt != null)
            return new CommanderAction(CommanderActionKind.Order, o.Unit, o.Task, Where: o.SupportAt, Target: o.Objective);
        return new CommanderAction(CommanderActionKind.Order, o.Unit, o.Task, Where: o.Objective);
    }

    /// <summary>The order as a radio line (<c>"Alpha Squad Attack → house (start: UNIT_AT Bravo Squad support)"</c>).</summary>
    public static string Text(UnitOrder o)
    {
        var sb = new StringBuilder(o.Unit).Append(' ').Append(o.Task);
        if (o.Objective != null) sb.Append(" → ").Append(o.Objective);
        if (o.Start is { On: not ConditionOn.Immediate } s) sb.Append(" (start: ").Append(s.Text).Append(')');
        return sb.ToString();
    }
}
