using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §3) — one cue of a scenario's checks: a take anchor (<see cref="Spec"/>, judged by
    /// <see cref="AnchorBook.HoldsFor"/> for the state cues) or one of the two the verifier adds — <c>field</c> (a component value by
    /// <see cref="Path"/>) and a <c>shot</c> / <c>blast</c> row filtered by <see cref="Where"/>.
    /// </summary>
    public sealed record CheckCue
    {
        public AnchorSpec Spec { get; init; } = new();
        /// <summary><c>shot</c> / <c>blast</c>: the row's fields that must match (<c>"penetration": [">", 400]</c> or <c>"field": value</c>).</summary>
        public IReadOnlyList<(string Field, string Op, JsonNode? Value)> Where { get; init; } = Array.Empty<(string, string, JsonNode?)>();
        /// <summary><c>shot</c>: the round STRUCK this unit.</summary>
        public string? Hit { get; init; }
        /// <summary><c>field</c>: <c>Component.Field[.Sub]</c> in the entity dump's <c>Components</c>.</summary>
        public string? Path { get; init; }
        public string Op { get; init; } = "==";
        /// <summary><c>field</c>: the value compared with; <c>"$now"</c> is the sim time of the sample.</summary>
        public JsonNode? Value { get; init; }
        /// <summary><c>field</c>: <c>any</c> (default) · <c>all</c> · <c>atLeast:n</c> of the units <see cref="AnchorSpec.Who"/> names.</summary>
        public string Quantifier { get; init; } = "any";
        /// <summary><c>field</c> (agg-behaviors, R-8320): the ELEMENT count of a path with a <c>*</c> part (<c>UnitSlotTable.Slots.*.Health</c>) — the cue holds for a unit when at least this many
        /// elements of the array satisfy the comparison (default 1). A path with no <c>*</c> ignores it.</summary>
        public int Elements { get; init; } = 1;
        /// <summary>⭐ <c>CE-4624</c> <c>knowledge</c>: the SIDE whose knowledge of the field <see cref="AnchorSpec.Who"/> names is read (a <c>ForceId</c> name).</summary>
        public string? Side { get; init; }
        /// <summary>⭐ CE-10002 (air combat RV-15, R-10012) <c>far</c> / <c>close</c>: the unit measured from (a name or <c>*part</c>; else <see cref="AnchorSpec.Point"/>)
        /// and the distance (m): <c>far</c> holds when some unit <see cref="AnchorSpec.Who"/> names is MORE than <see cref="M"/> from it, <c>close</c>
        /// when within it. From a unit: 3-D; from a point: horizontal.</summary>
        public string? From { get; init; }
        public float M { get; init; }
        /// <summary>⭐ <c>CE-9009</c> <c>between</c>: the corridor's far end (a name or <c>*part</c>); <see cref="From"/> is its near end, <see cref="M"/> its
        /// half-width (m), <see cref="Margin"/> the length at each end left to the two units' own bodies (m, default 3).</summary>
        public string? To { get; init; }
        public float Margin { get; init; } = 3f;
        /// <summary>⭐ <c>CE-5033</c> <c>window</c>: the building whose glazed windows are read (its terrain key <c>"&lt;terrain&gt;/&lt;label&gt;"</c> or its label).</summary>
        public string? Building { get; init; }
        /// <summary>⭐ <c>CE-4672</c> <c>field</c>: judge only the DEAD units the name matches (<c>Health.Current</c> ≤ 0) — "no behaviour on a dead man".</summary>
        public bool Dead { get; init; }
        /// <summary>⭐ <c>CE-8316</c> <c>cover</c>: a unit lying PRONE uses the ground — it counts as in cover, never as standing in the open ("never STANDS
        /// wholly in the open": a fire-and-movement half lies prone to suppress while the other half bounds).</summary>
        public bool Upright { get; init; }
        /// <summary>⭐ <c>CE-2443</c> (mf-clear-lane-and-cross) <c>field</c>: more conditions the SAME unit must meet too (<c>"also": [{"path", "op", "value"}, …]</c>) —
        /// e.g. "a man in the field's x-span AND outside the lane's band". All must hold, on the unit's own components.</summary>
        public IReadOnlyList<(string Path, string Op, JsonNode? Value)> Also { get; init; } = Array.Empty<(string, string, JsonNode?)>();
        /// <summary>⭐ <c>CE-2455</c> (the urban fight suite, R-4277) — a STATE cue holds only once it has held at every sample for this long (s; 0 = at once).
        /// On a cue over several units (<c>cover</c>, <c>still</c>, <c>field</c>) each unit counts only once IT has held it that long — "a man stood in the open
        /// 15 s", never "the squad was open at 15 s of samples, a different man each time". On any other state cue: the cue itself.</summary>
        public double For { get; init; }
        /// <summary>⭐ <c>CE-2455</c> — <c>cover</c> / <c>still</c> / <c>field</c> / <c>bunched</c>: judge only the units still IN THE FIGHT (alive and not incapacitated —
        /// <c>Injury.StateOf</c>'s rule). 📌 A downed man lies still in the open by definition: without it he reads as an idle man (`2026-11-20`, the stair "stand-off").</summary>
        public bool Fighting { get; init; }
        /// <summary>⭐ <c>CE-2466</c> — <c>bunched</c>: the fewest men that make a knot (default 2). 📐 U2: the last two Red holding one corner 2.8 m apart is a
        /// buddy pair sharing cover, not a squad collapsed into a knot.</summary>
        public int Men { get; init; } = 2;
        /// <summary>⭐ <c>CE-2476</c> <c>perceives</c> (Sensing §8.6e): the REAL enemies (a name or <c>*part</c>) a unit's beliefs are scored against.</summary>
        public string? Truth { get; init; }
        /// <summary>⭐ <c>CE-2476</c> <c>perceives</c>: which count is compared — <c>contacts</c> · <c>matched</c> · <c>missed</c> · <c>phantom</c> · <c>duplicate</c>
        /// (<see cref="PerceptionScore"/>).</summary>
        public string? Metric { get; init; }
        /// <summary>⭐ <c>CE-2476</c> <c>perceives</c>: <see cref="AnchorSpec.Who"/> names commanders, and their squad contact POOL is judged.</summary>
        public bool Squad { get; init; }
        /// <summary>⭐ <c>CE-2476</c> <c>perceives</c>: false skips a contact heard only through an opening (D-8 — "look there", not "he is there").</summary>
        public bool Through { get; init; } = true;
        /// <summary>⭐⭐ Simulated devices S1e-2 (DESIGN_Simulated_Devices §5a) — <c>field</c> with <c>memory</c>: the unit's <c>[UnitMemory]</c> struct of this
        /// NAME (<c>RangefinderState</c>) is read instead of its dumped components, <see cref="Path"/> naming its field (<c>"field": "LastRange"</c>); an entity
        /// field reads as the name of the unit it holds.</summary>
        public string? Memory { get; init; }
        /// <summary>⭐ S1e-2 <c>hud</c>: the source (a device, or a unit) whose HUD RUN a view bound to it draws.</summary>
        public string? Source { get; init; }
        /// <summary>⭐ S1e-2 <c>hud</c>: <c>"text": [op, value]</c> — some text of the run compares (<c>contains</c> …).</summary>
        public (string Op, JsonNode? Value)? Text { get; init; }
        /// <summary>⭐ S1e-2 <c>hud</c>: <c>"count": [op, n]</c> — the run's primitive count compares (<c>[">", 0]</c>: the view shows the source's HUD).</summary>
        public (string Op, JsonNode? Value)? Count { get; init; }

        public string On => Spec.On;

        /// <summary>The cues a check may use: the take's nine, <c>field</c> and ⭐ <c>knowledge</c> (CE-4624 — what a side knows of a minefield:
        /// <c>"is": "Marked"</c>, or a cell state — <c>Suspected</c> / <c>KnownClear</c> / <c>KnownMined</c> / <c>Unknown</c> — counted and compared by
        /// <c>op</c> / <c>value</c>, default <c>&gt;= 1</c>).</summary>
        public static readonly IReadOnlyList<string> Kinds = AnchorSpec.Kinds.Concat(new[] { "field", "knowledge", "far", "close", "between", "window", "cover", "bunched", "still", "exposed", "perceives", "report", "hud" }).ToArray();
        /// <summary>⭐ <c>CE-4672</c> <c>cover</c>: <c>in</c> — a cover point (the terrain's cover database) within <see cref="M"/> of the unit; <c>open</c> — none.</summary>
        public static readonly IReadOnlyList<string> CoverStates = new[] { "in", "open" };

        /// <summary>⭐ <c>CE-5033</c> (glass windows) — what a <c>window</c> cue's <c>is</c> may say: a pane never hit, cracked (1 or 2 hits; <c>n</c> = the count), broken.</summary>
        public static readonly IReadOnlyList<string> WindowStates = new[] { "intact", "cracked", "broken" };

        /// <summary>⭐ <c>CE-4624</c> — what a <c>knowledge</c> cue's <c>is</c> may say.</summary>
        public static readonly IReadOnlyList<string> KnowledgeStates = new[] { "Marked", "Suspected", "KnownClear", "KnownMined", "Unknown", "Lane" };   // ⭐ CE-2443 — Lane: the COUNT of cleared lanes the side holds on the field (MF-10)

        public static readonly IReadOnlyList<string> Ops = new[] { "==", "!=", "<", "<=", ">", ">=", "in", "contains" };

        public IEnumerable<string> Names() => Spec.Names().Concat(Hit != null ? new[] { Hit } : Array.Empty<string>())
            .Concat(From != null ? new[] { From } : Array.Empty<string>())
            .Concat(To != null ? new[] { To } : Array.Empty<string>())
            .Concat(Source != null && Source != StationView ? new[] { Source } : Array.Empty<string>());

        /// <summary>⭐ S1e-2 — a <c>hud</c> cue's source naming the station's VIEW (whatever it is bound to now), not a unit.</summary>
        public const string StationView = "station:1";
    }

    /// <summary>⭐ One expectation: the cue holds once in [<see cref="After"/>, <see cref="Before"/>], NEVER in it, or at the end.</summary>
    public sealed record Expectation
    {
        public string Name { get; init; } = "";
        public CheckCue Cue { get; init; } = new();
        public bool Never { get; init; }
        public bool AtEnd { get; init; }
        public AtExpr? After { get; init; }
        public AtExpr? Before { get; init; }
        /// <summary>⭐ Judged only when this anchor / action / expectation happened in the run; else the verdict is <c>skipped</c> (ok, no time) —
        /// for a line about an EVENT the run need not produce (📌 Bridgehead: "a civilian harmed fails the tree", once the fire-discipline fix
        /// CE-2379 stopped the accidental harm the row relied on).</summary>
        public string? If { get; init; }
        /// <summary>⭐ <c>CE-4693</c> — the mirror of <see cref="If"/>: judged only when this anchor / action / expectation did NOT happen; else <c>skipped</c>.
        /// For a scenario with two legitimate outcomes (📌 <c>bl-river-crossing</c>: the sapper silenced ⇒ the bridge held and is recovered; his charge
        /// fired ⇒ the fall), each outcome's rows keyed on the same event.</summary>
        public string? Unless { get; init; }
        /// <summary>⭐ S1e-2 (DESIGN_Simulated_Devices §5a) — judged ONLY at the n-th paused frame of a <c>do: pause</c> (1 = the first): the sim time stands
        /// still there, the run's frames go on — so "the menu changed while time did not" is a row.</summary>
        public int? AtPausedFrame { get; init; }
    }

    /// <summary>
    /// ⭐⭐ Simulated devices S1e-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §5a) — what a scripted TRAINEE does at a station: <c>possess</c> a
    /// unit (as the HTTP possess does), <c>control</c> the device the view shows (press buttons by their TKB NAME, set axes, aim the look at a unit or a
    /// point — through the SAME sender a keyboard or a replica fills), bind the <c>view</c> to a device (or the plain eye), or <c>pause</c>: hold the sim
    /// time for n frames while the run keeps stepping frames (the verifier samples every paused frame).
    /// </summary>
    public sealed record StationAction
    {
        /// <summary>possess · control · view · pause · ⭐ SD-S5v click</summary>
        public string Kind { get; init; } = "";
        /// <summary>The station (1 = this node's own — the only one in the editor tier).</summary>
        public int Station { get; init; } = 1;
        /// <summary><c>possess</c>: the unit; <c>view</c>: the device (null = the plain eye).</summary>
        public string? Who { get; init; }
        /// <summary><c>control</c>: elements pressed once each, by the device type's element NAME (<c>DeviceDefDto.Elements</c>, D-142 — a switch steps).</summary>
        public IReadOnlyList<string> Press { get; init; } = Array.Empty<string>();
        /// <summary><c>control</c>: axis index (0–3) ⇒ value (−1…1), held for <see cref="For"/> s (0 = until changed).</summary>
        public IReadOnlyList<(int Axis, float Value)> Axes { get; init; } = Array.Empty<(int, float)>();
        /// <summary><c>control</c>: the look aims at this unit (its centre) …</summary>
        public string? AimAt { get; init; }
        /// <summary>… or at this world point.</summary>
        public System.Numerics.Vector3? AimPoint { get; init; }
        public double For { get; init; }
        /// <summary><c>pause</c>: how many frames the sim time is held.</summary>
        public int Frames { get; init; }
        /// <summary>⭐ SD-S5v <c>click</c>: the HUD button to click, by the element's NAME (its chip's PanelButton on the bound view's HUD).</summary>
        public string? Button { get; init; }
    }

    /// <summary>⭐ A change the check makes to the world at <see cref="At"/> (the old live checks' hand edits — wound a man, set an attribute).</summary>
    public sealed record CheckAction
    {
        public string? Name { get; init; }
        public AtExpr At { get; init; }
        public string Who { get; init; } = "";
        public string Component { get; init; } = "";
        public JsonObject Patch { get; init; } = new();
        /// <summary>⭐ CE-3494 F3 — an OPERATOR ORDER instead of a component patch: a <c>POST /orders</c> item (<c>kind</c>, <c>behavior</c>, <c>params</c> …)
        /// given to every unit <see cref="Who"/> names, through the same service the route uses. The verifier applies actions between steps, so the
        /// order is given while the world is paused — it takes effect at the next step (DESIGN_Unit_Intent.md R1).</summary>
        public JsonObject? Order { get; init; }
        /// <summary>⭐ CE-3494 F3 — resume the unit's held (or halted) mission plan — the <c>POST /missions/{id}/resume</c> service.</summary>
        public bool Resume { get; init; }
        /// <summary>⭐ CE-11061 — REVIVE the unit (the Unit form's Revive, <c>POST /entities/{id}/revive</c>): back in the fight, through the edit framework.</summary>
        public bool Revive { get; init; }
        /// <summary>⭐ S1e-2 — a trainee's action at a station (possess · control · view · pause) instead of a world edit.</summary>
        public StationAction? Station { get; init; }
        /// <summary>⭐ S1e-2 — applied on the n-th paused frame of a <c>do: pause</c> instead of at <see cref="At"/> (the sim time stands still there).</summary>
        public int? AtPausedFrame { get; init; }
    }

    /// <summary>⭐ <c>CE-4042</c> — <c>scenarios/&lt;s&gt;/checks.json</c>: what a scenario must do, as data (§3).</summary>
    public sealed record ScenarioChecks
    {
        public string Scenario { get; init; } = "";
        public double RunFor { get; init; } = 60d;
        /// <summary>Seconds between golden snapshots; null = no golden (a demo, §5).</summary>
        public double? GoldenEvery { get; init; }
        /// <summary>The golden keeps every component IN FULL instead of its hash — to run a difference down (large; not for git).</summary>
        public bool GoldenFull { get; init; }
        /// <summary>How often the world is read for the cues (sim seconds; actions still land on their exact step).</summary>
        public double SampleEvery { get; init; } = 0.25d;
        public IReadOnlyDictionary<string, CheckCue> Anchors { get; init; } = new Dictionary<string, CheckCue>(StringComparer.Ordinal);
        public IReadOnlyList<CheckAction> Do { get; init; } = Array.Empty<CheckAction>();
        public IReadOnlyList<Expectation> Expect { get; init; } = Array.Empty<Expectation>();
        /// <summary>⭐ The units that appear DURING the run (a trigger's <c>InsertMacro</c>, the showcase QRF) — named here so the start-of-run cast
        /// check does not refuse them; a cue resolves them when they appear (<c>WorldUnits</c> re-indexes on a miss). ⛔ Every other name must be there at the start.</summary>
        public IReadOnlyList<string> Later { get; init; } = Array.Empty<string>();

        /// <summary>Every unit a cue or an action names (to validate against the scenario's cast).</summary>
        /// <summary>⭐ RT-3 — the component paths the expectations read (a golden keeps them as visible state).</summary>
        public IEnumerable<string> Paths() => Expect.Where(e => e.Cue.Memory == null).Select(e => e.Cue.Path).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).Distinct(StringComparer.Ordinal);

        public IEnumerable<string> Names() =>
            Anchors.Values.SelectMany(a => a.Names())
                .Concat(Expect.SelectMany(e => e.Cue.Names()))
                .Concat(Do.Where(a => a.Station == null).Select(a => a.Who))
                .Concat(Do.Where(a => a.Station != null).SelectMany(a => new[] { a.Station!.Who, a.Station.AimAt }).Where(n => n != null).Select(n => n!))   // ⭐ S1e-2
                .Distinct(StringComparer.Ordinal);
    }

    /// <summary>⭐ <c>CE-4042</c> — reads and writes <see cref="ScenarioChecks"/>; every mistake is named, never guessed.</summary>
    public static class ChecksJson
    {
        public const string FileName = "checks.json";

        public static ScenarioChecks Parse(JsonObject json, string scenario, List<string> problems)
        {
            foreach (var (k, _) in json)
                if (k is not ("$doc" or "runFor" or "golden" or "sampleEvery" or "anchors" or "do" or "expect" or "later"))
                    problems.Add($"unknown key '{k}'");

            double? every = null;
            bool full = false;
            switch (json["golden"])
            {
                case null: break;
                case JsonValue v when v.TryGetValue<bool>(out var b): if (b) every = 10d; break;
                case JsonObject g:
                    every = TakeJson.Double(g["every"]) ?? 10d;
                    full = g["full"] is JsonValue fv && fv.TryGetValue<bool>(out var f) && f;
                    foreach (var (k, _) in g) if (k is not ("every" or "full")) problems.Add($"golden: unknown key '{k}'");
                    if (!(every > 0)) problems.Add("golden.every must be > 0");
                    break;
                default: problems.Add("golden is {\"every\": seconds} or false"); break;
            }

            var anchors = new Dictionary<string, CheckCue>(StringComparer.Ordinal);
            if (json["anchors"] is JsonObject ao)
                foreach (var (k, v) in ao)
                    if (v is JsonObject o) anchors[k] = ParseCue(o, $"anchor '{k}'", problems, extra: Array.Empty<string>());
                    else problems.Add($"anchor '{k}' is not an object");

            var actions = new List<CheckAction>();
            if (json["do"] is JsonArray da)
                for (int i = 0; i < da.Count; i++)
                {
                    var where = $"do[{i}]";
                    if (da[i] is not JsonObject a) { problems.Add($"{where} is not an object"); continue; }
                    foreach (var (k, _) in a)
                        if (k is not ("name" or "at" or "atPausedFrame" or "set" or "order" or "resume" or "revive" or "possess" or "control" or "view" or "pause" or "click")) problems.Add($"{where}: unknown key '{k}'");
                    int kinds = (a["set"] is null ? 0 : 1) + (a["order"] is null ? 0 : 1) + (a["resume"] is null ? 0 : 1) + (a["revive"] is null ? 0 : 1)
                              + (a["possess"] is null ? 0 : 1) + (a["control"] is null ? 0 : 1) + (a["view"] is null ? 0 : 1) + (a["pause"] is null ? 0 : 1)
                              + (a["click"] is null ? 0 : 1);
                    if (kinds != 1) { problems.Add($"{where}: exactly one of set, order, resume, revive, possess, control, view, pause, click"); continue; }
                    // ⭐ S1e-2 — on the n-th paused frame of a pause, instead of at a time (a pause itself starts at a time)
                    int? atPaused = null;
                    if (a["atPausedFrame"] is { } apf)
                    {
                        if (apf is JsonValue pv && pv.TryGetValue<int>(out int pn) && pn >= 1) atPaused = pn;
                        else { problems.Add($"{where}: atPausedFrame is a whole frame number >= 1"); continue; }
                        if (a["pause"] is not null) { problems.Add($"{where}: a pause starts at a time, not at a paused frame"); continue; }
                        if (a["at"] is not null) { problems.Add($"{where}: at and atPausedFrame together"); continue; }
                    }
                    var at = atPaused is null ? Time(a["at"], where + " at", problems) : new AtExpr(0d);
                    if (at is null) { problems.Add($"{where}: at is required"); continue; }
                    if (a["possess"] is not null || a["control"] is not null || a["view"] is not null || a["pause"] is not null || a["click"] is not null)
                    {
                        if (StationActionOf(a, where, problems) is { } station)
                            actions.Add(new CheckAction { Name = a["name"]?.ToString(), At = at.Value, AtPausedFrame = atPaused, Who = station.Who ?? "", Station = station });
                        continue;
                    }
                    if (atPaused != null) { problems.Add($"{where}: atPausedFrame belongs to a station action (possess, control, view)"); continue; }
                    if (a["order"] is not null)
                    {
                        if (a["order"] is not JsonObject ord || ord["who"] is null || ord["kind"] is null)
                        { problems.Add($"{where}: order is {{who, kind, …}} — a POST /orders item"); continue; }
                        var item = (JsonObject)ord.DeepClone();
                        item.Remove("who");
                        actions.Add(new CheckAction { Name = a["name"]?.ToString(), At = at.Value, Who = ord["who"]!.ToString(), Order = item });
                        continue;
                    }
                    if (a["resume"] is not null)
                    {
                        if (a["resume"] is not JsonValue rv || !rv.TryGetValue<string>(out var rwho) || rwho.Length == 0)
                        { problems.Add($"{where}: resume is the unit's name"); continue; }
                        actions.Add(new CheckAction { Name = a["name"]?.ToString(), At = at.Value, Who = rwho, Resume = true });
                        continue;
                    }
                    if (a["revive"] is not null)   // ⭐ CE-11061 — "revive": "<unit name>"
                    {
                        if (a["revive"] is not JsonValue vv || !vv.TryGetValue<string>(out var vwho) || vwho.Length == 0)
                        { problems.Add($"{where}: revive is the unit's name"); continue; }
                        actions.Add(new CheckAction { Name = a["name"]?.ToString(), At = at.Value, Who = vwho, Revive = true });
                        continue;
                    }
                    if (a["set"] is not JsonObject set || set["who"] is null || set["component"] is null || set["patch"] is not JsonObject patch)
                    { problems.Add($"{where}: set is {{who, component, patch}}"); continue; }
                    actions.Add(new CheckAction
                    {
                        Name = a["name"]?.ToString(), At = at.Value, Who = set["who"]!.ToString(), Component = set["component"]!.ToString(),
                        Patch = (JsonObject)patch.DeepClone(),
                    });
                }

            var expect = new List<Expectation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (json["expect"] is JsonArray ea)
                for (int i = 0; i < ea.Count; i++)
                {
                    if (ea[i] is not JsonObject e) { problems.Add($"expect[{i}] is not an object"); continue; }
                    var name = e["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) { problems.Add($"expect[{i}]: name is required"); continue; }
                    if (!seen.Add(name)) problems.Add($"expect '{name}': the name is used twice");
                    var where = $"expect '{name}'";
                    bool never = e["never"] is JsonObject;
                    bool atEnd = e["atEnd"] is JsonValue ev && ev.TryGetValue<bool>(out var ae) && ae;
                    CheckCue cue = never
                        ? ParseCue((JsonObject)e["never"]!, where + " never", problems, Array.Empty<string>())
                        : ParseCue(e, where, problems, new[] { "name", "after", "before", "atEnd", "never", "if", "unless", "atPausedFrame" });
                    if (never)
                        foreach (var (k, _) in e)
                            if (k is not ("name" or "never" or "after" or "before" or "if" or "unless")) problems.Add($"{where}: unknown key '{k}' beside never");
                    int? atPausedFrame = null;   // ⭐ S1e-2
                    if (e["atPausedFrame"] is { } epf)
                    {
                        if (epf is JsonValue epv && epv.TryGetValue<int>(out int epn) && epn >= 1) atPausedFrame = epn;
                        else problems.Add($"{where}: atPausedFrame is a whole frame number >= 1");
                        if (atEnd) problems.Add($"{where}: atPausedFrame and atEnd together");
                    }
                    if (never && atEnd) problems.Add($"{where}: never and atEnd together");
                    expect.Add(new Expectation
                    {
                        Name = name!, Cue = cue, Never = never, AtEnd = atEnd,
                        After = Time(e["after"], where + " after", problems), Before = Time(e["before"], where + " before", problems),
                        If = e["if"]?.ToString(),
                        Unless = e["unless"]?.ToString(),
                        AtPausedFrame = atPausedFrame,
                    });
                    if (e["if"] != null && e["unless"] != null) problems.Add($"{where}: if and unless together");
                }

            if (expect.Count == 0) problems.Add("expect is empty — a check with nothing to judge");
            double runFor = TakeJson.Double(json["runFor"]) ?? 60d;
            if (!(runFor > 0)) problems.Add("runFor must be > 0");
            double sample = TakeJson.Double(json["sampleEvery"]) ?? 0.25d;
            if (!(sample > 0)) problems.Add("sampleEvery must be > 0");

            // ⭐ every name a time refers to must be an anchor, an action or another expectation
            var known = new HashSet<string>(anchors.Keys.Concat(actions.Where(a => a.Name != null).Select(a => a.Name!)).Concat(seen), StringComparer.Ordinal);
            foreach (var t in actions.Select(a => (AtExpr?)a.At).Concat(expect.SelectMany(e => new[] { e.After, e.Before })))
                if (t is { Anchor: { } n } && !known.Contains(n)) problems.Add($"a time names '{n}', which is no anchor, action or expectation");
            foreach (var e in expect.Where(x => x.If != null && !known.Contains(x.If)))
                problems.Add($"expect '{e.Name}': if names '{e.If}', which is no anchor, action or expectation");
            foreach (var e in expect.Where(x => x.Unless != null && !known.Contains(x.Unless)))
                problems.Add($"expect '{e.Name}': unless names '{e.Unless}', which is no anchor, action or expectation");

            var later = new List<string>();
            if (json["later"] is { } ln)
            {
                if (ln is JsonArray la && la.All(x => x is JsonValue v && v.TryGetValue<string>(out var t) && !string.IsNullOrWhiteSpace(t)))
                    later.AddRange(la.Select(x => x!.GetValue<string>()));
                else problems.Add("later must be a list of unit names (the units a trigger inserts during the run)");
            }

            return new ScenarioChecks
            {
                Scenario = scenario, RunFor = runFor, GoldenEvery = every, GoldenFull = full, SampleEvery = sample,
                Anchors = anchors, Do = actions, Expect = expect, Later = later,
            };
        }

        private static AtExpr? Time(JsonNode? n, string where, List<string> problems)
        {
            if (n is null) return null;
            try { return AtExpr.Parse(n); }
            catch (FormatException ex) { problems.Add($"{where}: {ex.Message}"); return null; }
        }

        /// <summary>⭐ S1e-2 (DESIGN_Simulated_Devices §5a) — <c>possess</c> · <c>control</c> · <c>view</c> · <c>pause</c>; null (with the problems named) when malformed.</summary>
        private static StationAction? StationActionOf(JsonObject a, string where, List<string> problems)
        {
            static int StationOf(JsonObject o, string where, List<string> problems)
            {
                if (o["station"] is null) return 1;
                if (o["station"] is JsonValue sv && sv.TryGetValue<int>(out int n) && n == 1) return 1;
                problems.Add($"{where}: station is 1 — this node's own station (the editor tier has one; a second station is the cluster tier's, SD-C)");
                return 1;
            }
            if (a["pause"] is { } pn)
            {
                if (pn is not JsonObject po || po["frames"] is not JsonValue fv || !fv.TryGetValue<int>(out int frames) || frames < 1)
                { problems.Add($"{where}: pause is {{\"frames\": n}} (n >= 1)"); return null; }
                foreach (var (k, _) in po) if (k != "frames") problems.Add($"{where}: pause: unknown key '{k}'");
                return new StationAction { Kind = "pause", Frames = frames };
            }
            if (a["possess"] is { } psn)
            {
                if (psn is not JsonObject po || po["who"]?.ToString() is not { Length: > 0 } who) { problems.Add($"{where}: possess is {{who, station}}"); return null; }
                foreach (var (k, _) in po) if (k is not ("who" or "station")) problems.Add($"{where}: possess: unknown key '{k}'");
                return new StationAction { Kind = "possess", Station = StationOf(po, where, problems), Who = who };
            }
            if (a["click"] is { } cn)
            {
                // ⭐ SD-S5v (DESIGN_Simulated_Devices §3.13e) — click the bound device's HUD button by its element NAME, through the station's panel (the HUD is the path)
                if (cn is not JsonObject clo || clo["button"]?.ToString() is not { Length: > 0 } button) { problems.Add($"{where}: click is {{station, button}} (a HUD button's element name)"); return null; }
                foreach (var (k, _) in clo) if (k is not ("button" or "station")) problems.Add($"{where}: click: unknown key '{k}'");
                return new StationAction { Kind = "click", Station = StationOf(clo, where, problems), Button = button };
            }
            if (a["view"] is { } vn)
            {
                if (vn is not JsonObject vo || vo["source"]?.ToString() is not { Length: > 0 } source) { problems.Add($"{where}: view is {{station, source}} (a device's name, or \"none\")"); return null; }
                foreach (var (k, _) in vo) if (k is not ("source" or "station")) problems.Add($"{where}: view: unknown key '{k}'");
                return new StationAction { Kind = "view", Station = StationOf(vo, where, problems), Who = source == "none" ? null : source };
            }
            if (a["control"] is not JsonObject co) { problems.Add($"{where}: control is {{station, press | axis | aim, for}}"); return null; }
            foreach (var (k, _) in co) if (k is not ("station" or "press" or "axis" or "aim" or "for")) problems.Add($"{where}: control: unknown key '{k}'");
            var press = new List<string>();
            switch (co["press"])
            {
                case null: break;
                case JsonValue one when one.TryGetValue<string>(out var b) && b.Length > 0: press.Add(b); break;
                case JsonArray many when many.All(x => x is JsonValue v && v.TryGetValue<string>(out var t) && t.Length > 0): press.AddRange(many.Select(x => x!.GetValue<string>())); break;
                default: problems.Add($"{where}: control.press is a button name (the device type's channel) or a list of them"); break;
            }
            var axes = new List<(int, float)>();
            if (co["axis"] is { } an)
            {
                if (an is not JsonObject ao) problems.Add($"{where}: control.axis is {{\"0\": v, …}} (axis 0–3, v in -1…1)");
                else
                    foreach (var (k, v) in ao)
                        if (int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out int ax) && ax is >= 0 and < 4
                            && v is JsonValue av && av.TryGetValue<double>(out double val) && val is >= -1d and <= 1d) axes.Add((ax, (float)val));
                        else problems.Add($"{where}: control.axis '{k}': an axis is 0–3 and its value -1…1");
            }
            string? aimAt = null; System.Numerics.Vector3? aimPoint = null;
            switch (co["aim"])
            {
                case null: break;
                case JsonValue av when av.TryGetValue<string>(out var unit) && unit.Length > 0: aimAt = unit; break;
                case JsonArray p when p.Count is 2 or 3 && p.All(x => x is JsonValue xv && xv.TryGetValue<double>(out _)):
                    aimPoint = new System.Numerics.Vector3((float)p[0]!.GetValue<double>(), (float)p[1]!.GetValue<double>(), p.Count > 2 ? (float)p[2]!.GetValue<double>() : 0f); break;
                default: problems.Add($"{where}: control.aim is a unit's name or a point [x, y, z]"); break;
            }
            double forS = co["for"] is JsonValue fv2 && fv2.TryGetValue<double>(out double fs) ? fs : 0d;
            if (co["for"] is not null && !(forS > 0d)) problems.Add($"{where}: control.for is seconds (> 0)");
            if (press.Count == 0 && axes.Count == 0 && aimAt == null && aimPoint == null) problems.Add($"{where}: control needs press, axis or aim");
            return new StationAction { Kind = "control", Station = StationOf(co, where, problems), Press = press, Axes = axes, AimAt = aimAt, AimPoint = aimPoint, For = forS };
        }

        private static CheckCue ParseCue(JsonObject o, string where, List<string> problems, IReadOnlyCollection<string> extra)
        {
            var on = o["on"]?.ToString() ?? "";
            if (!CheckCue.Kinds.Contains(on)) problems.Add($"{where}: cue '{on}' is not one of {string.Join(", ", CheckCue.Kinds)}");
            var spec = new JsonObject();
            var where_ = new List<(string, string, JsonNode?)>();
            string? hit = null, path = null, op = "==", quantifier = "any", side = null, from = null, to = null, building = null;
            bool dead = false, upright = false, fighting = false; int men = 2;
            string? truth = null, metric = null; bool squad = false, through = true;   // ⭐ CE-2476
            string? memory = null, source = null; (string, JsonNode?)? text = null, count = null;   // ⭐ S1e-2
            double forSeconds = 0d;
            var also = new List<(string, string, JsonNode?)>();
            int elements = 1;
            float m = 0f, margin = 3f;
            bool opGiven = false;
            JsonNode? value = null;
            foreach (var (k, v) in o)
            {
                if (extra.Contains(k)) continue;
                switch (k)
                {
                    case "on" or "who" or "by" or "target" or "is" or "n" or "point" or "within" or "below":
                        spec[k] = v?.DeepClone(); break;
                    case "at": spec["target"] = v?.DeepClone(); break;          // a shot's target: "at" reads as the sentence says
                    case "hit": hit = v?.ToString(); break;
                    case "path": path = v?.ToString(); break;
                    case "memory": memory = v?.ToString(); break;                 // ⭐ S1e-2 — a [UnitMemory] struct by name
                    case "field": path = v?.ToString(); break;                    // ⭐ S1e-2 — its field (the memory row's wording)
                    case "source": source = v?.ToString(); break;                 // ⭐ S1e-2 — hud
                    case "text" or "count":                                       // ⭐ S1e-2 — hud: [op, value]
                        if (v is JsonArray hp && hp.Count == 2 && hp[0] is JsonValue hop && CheckCue.Ops.Contains(hop.ToString()))
                        { if (k == "text") text = (hop.ToString(), hp[1]?.DeepClone()); else count = (hop.ToString(), hp[1]?.DeepClone()); }
                        else problems.Add($"{where}: {k} is [op, value] (op one of {string.Join(" ", CheckCue.Ops)})");
                        break;
                    case "op": op = v?.ToString() ?? "=="; opGiven = true; break;
                    case "side": side = v?.ToString(); break;
                    case "from": from = v?.ToString(); break;
                    case "to": to = v?.ToString(); break;
                    case "building": building = v?.ToString(); break;
                    case "truth": truth = v?.ToString(); break;     // ⭐ CE-2476
                    case "metric": metric = v?.ToString(); break;
                    case "squad": squad = v is JsonValue qv && qv.TryGetValue<bool>(out var qb) && qb; break;
                    case "through": through = !(v is JsonValue tv && tv.TryGetValue<bool>(out var tb) && !tb); break;
                    case "dead": dead = v is JsonValue dv && dv.TryGetValue<bool>(out var db) && db; break;
                    case "upright": upright = v is JsonValue uv && uv.TryGetValue<bool>(out var ub) && ub; break;   // ⭐ CE-8316
                    case "fighting": fighting = v is JsonValue fv && fv.TryGetValue<bool>(out var fb) && fb; break;   // ⭐ CE-2455
                    case "men":   // ⭐ CE-2466 — bunched: the smallest group that is a knot (a buddy pair sharing a corner is not one)
                        if (v is JsonValue nv && nv.TryGetValue<int>(out var nm) && nm >= 2) men = nm; else problems.Add($"{where}: men is an integer >= 2");
                        break;
                    case "for": forSeconds = v is JsonValue sv2 && sv2.TryGetValue<double>(out var sd) ? sd : -1d; break;   // ⭐ CE-2455
                    case "also":
                        if (v is not JsonArray conds) { problems.Add($"{where}: also is a list of {{path, op, value}}"); break; }
                        foreach (var c in conds)
                            if (c is JsonObject co && co["path"]?.ToString() is { Length: > 0 } ap && (co["op"]?.ToString() ?? "==") is var aop && CheckCue.Ops.Contains(aop))
                                also.Add((ap, aop, co["value"]?.DeepClone()));
                            else problems.Add($"{where}: an also condition is {{path, op (one of {string.Join(" ", CheckCue.Ops)}), value}}");
                        break;
                    case "margin": margin = v is JsonValue gv && gv.TryGetValue<double>(out var gd) ? (float)gd : -1f; break;
                    case "m": m = v is JsonValue mv && mv.TryGetValue<double>(out var md) ? (float)md : 0f; break;
                    case "value": value = v?.DeepClone(); break;
                    case "quantifier": quantifier = v?.ToString() ?? "any"; break;
                    case "elements":
                        if (v is JsonValue ev && ev.TryGetValue<int>(out var en) && en >= 1) elements = en; else problems.Add($"{where}: elements is an integer >= 1");
                        break;
                    case "where":
                        if (v is not JsonObject w) { problems.Add($"{where}: where is an object"); break; }
                        foreach (var (f, c) in w)
                            if (c is JsonArray pair && pair.Count == 2 && pair[0] is JsonValue pv && CheckCue.Ops.Contains(pv.ToString()))
                                where_.Add((f, pv.ToString(), pair[1]?.DeepClone()));
                            else where_.Add((f, "==", c?.DeepClone()));
                        break;
                    default: problems.Add($"{where}: unknown key '{k}'"); break;
                }
            }
            // a take anchor's parser checks the take keys; "field" is the verifier's own, so it is checked here
            var anchorProblems = new List<string>();
            var parsed = TakeJson.ParseAnchor(spec, where, anchorProblems);
            problems.AddRange(anchorProblems.Where(p => on is not ("field" or "knowledge" or "far" or "close" or "between" or "window" or "cover" or "bunched" or "still" or "exposed" or "perceives" or "report" or "hud") || !p.Contains("is not one of", StringComparison.Ordinal)));
            if (on == "hud")   // ⭐ S1e-2 (DESIGN_Simulated_Devices §5a) — the published HUD run a view bound to the source draws
            {
                if (string.IsNullOrWhiteSpace(source)) problems.Add($"{where}: a hud cue needs source (the device or unit a view is bound to)");
                if (text is null && count is null) problems.Add($"{where}: a hud cue needs text or count ([op, value])");
            }
            else if (source != null || text != null || count != null) problems.Add($"{where}: source, text and count belong to a hud cue");
            if (memory != null && on != "field") problems.Add($"{where}: memory belongs to a field cue");
            if (on == "window")
            {
                if (string.IsNullOrWhiteSpace(building)) problems.Add($"{where}: a window cue needs building (its terrain key or label)");
                var state = parsed.Is is JsonValue wv ? wv.ToString() : null;
                if (state == null || !CheckCue.WindowStates.Contains(state)) problems.Add($"{where}: a window cue's is is one of {string.Join(", ", CheckCue.WindowStates)}");
            }
            else if (building != null) problems.Add($"{where}: building belongs to a window cue");
            // ⭐ CE-4672 (the street fight) — in cover / in the open, and a group bunched together
            if (on == "cover")
            {
                if (parsed.Who == null) problems.Add($"{where}: a cover cue needs who");
                var state = parsed.Is is JsonValue cv ? cv.ToString() : null;
                if (state == null || !CheckCue.CoverStates.Contains(state)) problems.Add($"{where}: a cover cue's is is one of {string.Join(", ", CheckCue.CoverStates)}");
                if (m <= 0f) problems.Add($"{where}: a cover cue needs m (metres from the nearest cover point, > 0)");
            }
            if (on == "bunched")
            {
                if (parsed.Who == null) problems.Add($"{where}: a bunched cue needs who (the group, e.g. \"*Red Squad 1 \")");
                if (m <= 0f) problems.Add($"{where}: a bunched cue needs m (metres from the group's centre, > 0)");
            }
            // ⭐ CE-2455 (the urban fight suite) — "still" (a man who has not moved), "for" (held that long), "fighting" (only the men still in the fight)
            if (on == "still")
            {
                if (parsed.Who == null) problems.Add($"{where}: a still cue needs who");
                if (m <= 0f) problems.Add($"{where}: a still cue needs m (metres he may move and still be still, > 0)");
                if (forSeconds <= 0d) problems.Add($"{where}: a still cue needs for (seconds he has not moved, > 0)");
            }
            if (on == "exposed")
            {
                if (parsed.Who == null) problems.Add($"{where}: an exposed cue needs who (the men judged)");
                if (parsed.By == null) problems.Add($"{where}: an exposed cue needs by (the enemies whose eyes count)");
            }
            if (on == "perceives")   // ⭐ CE-2476 (Sensing §8.6e) — a unit's beliefs against the real enemies
            {
                if (parsed.Who == null) problems.Add($"{where}: a perceives cue needs who (the men, or with squad: true the commanders, whose beliefs are judged)");
                if (string.IsNullOrWhiteSpace(truth)) problems.Add($"{where}: a perceives cue needs truth (the real enemies, a name or *part)");
                if (metric == null || !PerceptionScore.Metrics.Contains(metric)) problems.Add($"{where}: a perceives cue's metric is one of {string.Join(", ", PerceptionScore.Metrics)}");
                if (value is not JsonValue pv2 || !pv2.TryGetValue<int>(out _)) problems.Add($"{where}: a perceives cue's value is a whole count");
                if (m < 0f) problems.Add($"{where}: m is metres (>= 0)");
            }
            else if (truth != null || metric != null || squad || !through) problems.Add($"{where}: truth, metric, squad and through belong to a perceives cue");
            if (forSeconds < 0d) problems.Add($"{where}: for is seconds (>= 0)");
            if (forSeconds > 0d && on is "shot" or "blast" or "hit" or "report") problems.Add($"{where}: for belongs to a state cue (a {on} is an event)");
            if (on == "report")   // ⭐ CE-4676 — a report the inbox saw: by (the sender), target (the addressee), is (its kind or kinds)
            {
                foreach (var k in parsed.Is is JsonArray ks ? ks.Select(x => x?.ToString()) : parsed.Is is JsonValue kv ? new[] { kv.ToString() } : Array.Empty<string?>())
                    if (!Enum.TryParse<Fdp.Toolkit.Squad.Command.ReportKind>(k, ignoreCase: true, out var rk) || rk == Fdp.Toolkit.Squad.Command.ReportKind.NoReport)
                        problems.Add($"{where}: a report cue's is is a report kind (Contact, Spot, Sitrep, Lace, Progress, Request), not '{k}'");
            }
            if (fighting && on is not ("cover" or "still" or "field" or "bunched" or "exposed" or "close" or "far")) problems.Add($"{where}: fighting belongs to a cover, still, field, bunched, exposed, close or far cue");
            if (men != 2 && on != "bunched") problems.Add($"{where}: men belongs to a bunched cue");
            if (dead && on != "field") problems.Add($"{where}: dead belongs to a field cue");
            if (upright && on is not ("cover" or "exposed")) problems.Add($"{where}: upright belongs to a cover or exposed cue");
            if (also.Count > 0 && on != "field") problems.Add($"{where}: also belongs to a field cue");
            parsed = parsed with { On = on };
            if (on == "field")
            {
                if (string.IsNullOrWhiteSpace(path)) problems.Add($"{where}: a field cue needs path (Component.Field)");
                if (parsed.Who == null) problems.Add($"{where}: a field cue needs who");
            }
            if (on == "knowledge")
            {
                if (parsed.Who == null) problems.Add($"{where}: a knowledge cue needs who (the minefield's name)");
                if (side == null || !Enum.TryParse<ForceId>(side, ignoreCase: true, out _)) problems.Add($"{where}: a knowledge cue needs side (a ForceId: Friend, Hostile, …)");
                var state = parsed.Is is JsonValue sv ? sv.ToString() : null;
                if (state == null || !CheckCue.KnowledgeStates.Contains(state))
                    problems.Add($"{where}: a knowledge cue's is is one of {string.Join(", ", CheckCue.KnowledgeStates)}");
                if (!opGiven) op = ">=";
                value ??= JsonValue.Create(1);
            }
            else if (side != null) problems.Add($"{where}: side belongs to a knowledge cue");
            if (on is "cover" or "bunched" or "still" or "exposed")
            {
                if (from != null) problems.Add($"{where}: from belongs to a far / close / between cue");
            }
            else if (on is "far" or "close")
            {
                if (parsed.Who == null) problems.Add($"{where}: a {on} cue needs who");
                if (from == null && parsed.Point == null) problems.Add($"{where}: a {on} cue needs from (a unit) or point");
                if (m <= 0f) problems.Add($"{where}: a {on} cue needs m (metres, > 0)");
            }
            else if (on == "between")
            {
                if (parsed.Who == null) problems.Add($"{where}: a between cue needs who (the units that must or must not be in the corridor)");
                if (from == null || to == null) problems.Add($"{where}: a between cue needs from and to (the two units the corridor joins)");
                if (m <= 0f) problems.Add($"{where}: a between cue needs m (the corridor's half-width, metres, > 0)");
                if (margin < 0f) problems.Add($"{where}: a between cue's margin is metres (>= 0)");
            }
            else if (from != null) problems.Add($"{where}: from belongs to a far / close / between cue");
            if (on != "between" && (to != null || margin != 3f)) problems.Add($"{where}: to and margin belong to a between cue");
            if (!CheckCue.Ops.Contains(op)) problems.Add($"{where}: op '{op}' is not one of {string.Join(" ", CheckCue.Ops)}");
            if (quantifier is not ("any" or "all") && !(quantifier.StartsWith("atLeast:", StringComparison.Ordinal)
                && int.TryParse(quantifier[8..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                problems.Add($"{where}: quantifier is any, all or atLeast:n");
            if (where_.Count > 0 && on is not ("shot" or "blast" or "field")) problems.Add($"{where}: where filters a shot or a blast row (or, on a field cue, the elements of a * path)");
            if (where_.Count > 0 && on == "field" && !(path ?? "").Contains(".*.", StringComparison.Ordinal)) problems.Add($"{where}: where on a field cue filters the elements of a path with a * part");
            if (hit != null && on != "shot") problems.Add($"{where}: hit belongs to a shot cue");
            return new CheckCue { Spec = parsed, Where = where_, Hit = hit, Path = path, Op = op, Value = value, Quantifier = quantifier, Elements = elements, Side = side, From = from, To = to, Margin = margin, M = m, Building = building, Dead = dead, Upright = upright, Also = also, For = forSeconds, Fighting = fighting, Men = men,
                                 Truth = truth, Metric = metric, Squad = squad, Through = through, Memory = memory, Source = source, Text = text, Count = count };
        }
    }
}
