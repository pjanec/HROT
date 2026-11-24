using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐ <c>CE-4679</c> (DESIGN_LLM_Commander §4 "the actions") — the commander's tools as a JSON-schema list (<c>GET /commanders/{id}/tools</c>; L-3's
/// local model gets the same list), and the ONE reading of a posted action list into <see cref="CommanderAction"/>s. Few tools, strict shapes: a small
/// model handles few tools well.
/// </summary>
public static class CommanderTools
{
    public static JsonArray Schema()
    {
        JsonObject Str(string description, JsonArray? options = null)
        {
            var o = new JsonObject { ["type"] = "string", ["description"] = description };
            if (options != null) o["enum"] = options;
            return o;
        }
        var kinds = new JsonArray();
        foreach (var k in CommanderActionSystem.Orders) kinds.Add(k);
        var plays = new JsonArray();
        foreach (var p in CommanderActionSystem.OwnOrders) plays.Add(p);
        return new JsonArray
        {
            // ⭐ CE-4680 B-1 (§5b) — an order to your WHOLE unit: it gives the squad orders (support set, then the assault …); a direct squad order ends it.
            //   ⭐ CE-4685 — by KIND (the routing table picks the behaviour for the unit's class), never a behaviour name
            Tool("run_play", "PREFER THIS: give your WHOLE unit an order: it orders your squads for you (Attack: support by fire, then assault; "
                             + "Defend: hold an area; Bound: one squad covers, the other bounds; Hold: stay and hold). A direct squad order "
                             + "ends it first.",
                 new JsonObject { ["play"] = Str("the order to your whole unit", plays),
                                  ["where"] = Str("the objective / area: a place from the briefing (not needed for Hold)"),
                                  ["support"] = Str("Bound only: where the covering squad sets (default: the objective)") },
                 "play"),
            Tool("order", "Give ONE squad a direct order — only when no order to your whole unit fits; it ENDS that order. Repeating the order in force changes nothing.",
                 new JsonObject { ["squad"] = Str("your squad's name"), ["kind"] = Str("the order", kinds),
                                  ["where"] = Str("a place: a name from the briefing, \"N m <dir> of <name>\", or \"grid E<i> N<j>\" (not needed for Hold)") },
                 "squad", "kind"),
            Tool("hold", "Tell a squad to hold where it is.", new JsonObject { ["squad"] = Str("your squad's name") }, "squad"),
            Tool("ask_sitrep", "Ask a squad for a situation report.", new JsonObject { ["squad"] = Str("your squad's name") }, "squad"),
            Tool("stop_play", "End the play you are running; your squads keep their last orders until you give new ones.", new JsonObject()),
            Tool("note", "Your running estimate: it REPLACES your last note in the briefing (kept to 800 characters) — keep it short.", new JsonObject { ["text"] = Str("the estimate") }, "text"),   // ⭐ CE-4683 ①
        };
    }

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required)
    {
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        return new JsonObject
        {
            ["name"] = name, ["description"] = description,
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = req },
        };
    }

    /// <summary>
    /// Reads a posted action list — each <c>{"tool": "order", "squad": …, "kind": …, "where": …}</c> (or the tool-call shape
    /// <c>{"name": …, "arguments": {…}}</c>). A malformed entry is a problem with its index; the well-formed ones are read.
    /// </summary>
    public static List<CommanderAction> Parse(JsonNode? body, List<string> problems)
    {
        var actions = new List<CommanderAction>();
        if (body is not JsonArray list) { problems.Add("actions must be an array"); return actions; }
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject o) { problems.Add($"actions[{i}] is not an object"); continue; }
            var args = o["arguments"] as JsonObject ?? o;
            string? tool = (string?)(o["tool"] ?? o["name"]);
            string? S(string key) => args[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            switch (tool)
            {
                case "order":
                    if (S("squad") == null || S("kind") == null) { problems.Add($"actions[{i}]: order needs squad and kind"); break; }
                    actions.Add(new CommanderAction(CommanderActionKind.Order, S("squad"), S("kind"), Where: S("where"), Target: S("target")));
                    break;
                case "hold":
                    actions.Add(new CommanderAction(CommanderActionKind.Hold, S("squad")));
                    break;
                case "ask_sitrep":
                    actions.Add(new CommanderAction(CommanderActionKind.AskSitrep, S("squad")));
                    break;
                case "note":
                    actions.Add(new CommanderAction(CommanderActionKind.Note, Text: S("text")));
                    break;
                case "run_play":   // ⭐ CE-4680 B-1
                    if (S("play") == null) { problems.Add($"actions[{i}]: run_play needs play"); break; }
                    actions.Add(new CommanderAction(CommanderActionKind.RunPlay, Order: S("play"), Where: S("where"), Target: S("support")));
                    break;
                case "stop_play":
                    actions.Add(new CommanderAction(CommanderActionKind.StopPlay));
                    break;
                default:
                    problems.Add($"actions[{i}]: '{tool}' is not a tool — order, hold, ask_sitrep, run_play, stop_play, note");
                    break;
            }
        }
        return actions;
    }

    /// <summary>An action as the model log and the tool list write it.</summary>
    public static JsonObject ToJson(CommanderAction a) => a.Kind switch
    {
        CommanderActionKind.Order => new JsonObject { ["tool"] = "order", ["squad"] = a.Squad, ["kind"] = a.Order, ["where"] = a.Where ?? a.Target },
        CommanderActionKind.Hold => new JsonObject { ["tool"] = "hold", ["squad"] = a.Squad },
        CommanderActionKind.AskSitrep => new JsonObject { ["tool"] = "ask_sitrep", ["squad"] = a.Squad },
        CommanderActionKind.RunPlay => new JsonObject { ["tool"] = "run_play", ["play"] = a.Order, ["where"] = a.Where, ["support"] = a.Target },
        CommanderActionKind.StopPlay => new JsonObject { ["tool"] = "stop_play" },
        _ => new JsonObject { ["tool"] = "note", ["text"] = a.Text },
    };
}

/// <summary>
/// ⭐ <c>CE-4679</c> (R-4298 "a file beside the recording") — one seat's model log, JSON lines: each call's new radio lines and the mind's answer (raw, as
/// posted, for an outside mind), and each action's verdict. Opened at the first line, beside the recording the host names; no recording ⇒ no log.
/// Thread-safe (the runner and the action system both write).
/// </summary>
public sealed class ModelLog : IDisposable
{
    private readonly Func<string?> _path;
    private readonly object _gate = new();
    private StreamWriter? _w;

    public ModelLog(Func<string?> path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The file, once a line was written.</summary>
    public string? Path { get; private set; }

    public void Write(JsonObject line)
    {
        lock (_gate)
        {
            if (_w == null)
            {
                if (_path() is not { Length: > 0 } p) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(p))!);
                _w = new StreamWriter(new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
                Path = p;
            }
            _w.WriteLine(line.ToJsonString());
        }
    }

    public void Dispose()
    {
        lock (_gate) { _w?.Dispose(); _w = null; }
    }
}
