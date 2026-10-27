using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §2) — what the verifier reads and changes in the world, through what the engine
    /// already has: the entity dump (<c>GET /entities/{id}</c>), the shot and burst rows (<c>/combat/shots</c>, <c>/combat/detonations</c>) and
    /// the component edit (<c>POST /entities/{id}/component</c>). ⭐ An interface so the rails verify with no editor. Main thread only.
    /// </summary>
    public interface IVerifyWorld
    {
        EntityRepository? World { get; }

        /// <summary>The units by name (the director's <see cref="WorldUnits"/>) — ⚠ ONE id per name.</summary>
        IShotWorld Units { get; }

        /// <summary>⭐ EVERY named unit with its network id, two that share a name included (📌 <c>hill-attack</c>'s two "M1 Abrams",
        /// <c>test-fire</c>'s two Bradleys) — a name in a check matches every unit that carries it.</summary>
        IReadOnlyList<(long Id, string Name)> Named();

        /// <summary>The name of the unit at entity INDEX <paramref name="index"/> (the dump writes a held handle as <c>"[index, vGen]"</c>);
        /// null when it has none.</summary>
        string? NameOfEntityIndex(int index);

        /// <summary>A behaviour's name by its hash (the <c>task</c> cue).</summary>
        string? BehaviourName(int hash);

        /// <summary>The unit's components as the entity dump writes them (<c>{"Health": {...}, ...}</c>); null when it is gone.</summary>
        JsonObject? Components(long networkId);

        /// <summary>The shot log's rows, newest first (the <c>/combat/shots</c> rows).</summary>
        JsonArray Shots();

        /// <summary>The detonation log's rows, newest first (the <c>/combat/detonations</c> rows).</summary>
        JsonArray Blasts();

        /// <summary>Patches one component; null when written, else why not.</summary>
        string? SetComponent(long networkId, string component, JsonObject patch);
    }

    /// <summary>One expectation's verdict: <see cref="Time"/> is when it held (or, for <c>never</c>, when it was broken).</summary>
    /// <remarks><see cref="Outside"/>: a failed expectation's cue DID hold, but outside its window (at that time) — "it moved" is not
    /// "it never happened", and the report must tell them apart.</remarks>
    public sealed record Verdict(string Name, bool Ok, double? Time, string Kind, double? Outside = null)
    {
        public JsonObject ToJson() => new()
        {
            ["name"] = Name, ["ok"] = Ok, ["t"] = Time is { } t ? Math.Round(t, 4) : null, ["kind"] = Kind,
            ["heldOutsideItsWindowAt"] = Outside is { } o ? Math.Round(o, 4) : null,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-4042</c> (§3) — the scenario's conditions in this run: each anchor's, action's and expectation's first time, each <c>never</c>
    /// broken. Read at the checks' sample cadence. A shot holds at its own firing time; a state cue at the sample that sees it.
    /// </summary>
    public sealed class ConditionBook
    {
        private readonly ScenarioChecks _checks;
        private readonly IVerifyWorld _w;
        private readonly double _t0;
        private readonly Dictionary<string, double> _times = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _broken = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _outside = new(StringComparer.Ordinal);
        private readonly SortedDictionary<long, JsonObject> _shots = new();
        private readonly List<JsonObject> _blasts = new();
        private long _shotFloor = -1, _blastFloor = -1, _blastSeen = -1;
        private Dictionary<long, string> _nameOf = new();
        private double _rel;
        private readonly Dictionary<long, JsonObject?> _comps = new();

        /// <param name="t0">The sim time the run started at (rows carry absolute sim times).</param>
        public ConditionBook(ScenarioChecks checks, IVerifyWorld world, double t0)
        {
            _checks = checks ?? throw new ArgumentNullException(nameof(checks));
            _w = world ?? throw new ArgumentNullException(nameof(world));
            _t0 = t0;
            // ⚠ the logs live beside the world and outlast a load: only what happens from now on counts
            _shotFloor = MaxSeq(world.Shots());
            _blastFloor = MaxSeq(world.Blasts());
        }

        /// <summary>Every name's first time in this run (anchors, actions, expectations).</summary>
        public IReadOnlyDictionary<string, double> Times => _times;

        /// <summary>The rounds of this run (seq ascending), names resolved.</summary>
        public IEnumerable<JsonObject> ShotRows => _shots.Values;

        public IReadOnlyList<JsonObject> BlastRows => _blasts;

        public void Record(string name, double rel) => _times.TryAdd(name, rel);

        public double? TimeOf(AtExpr? at) => at?.Resolve(n => _times.TryGetValue(n, out var t) ? t : null);

        /// <summary>One sample: the rows since the last, then every anchor and expectation still open.</summary>
        public void Observe(double rel)
        {
            _rel = rel;
            _comps.Clear();
            _nameOf = _w.Named().GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name);
            ReadRows();
            foreach (var (name, cue) in _checks.Anchors)
                if (!_times.ContainsKey(name) && When(cue) is { } t) _times[name] = t;
            foreach (var e in _checks.Expect)
            {
                if (e.AtEnd) continue;
                double? lo = TimeOf(e.After), hi = TimeOf(e.Before);
                if (e.After is { Anchor: not null } && lo is null) continue;   // its window opens on a beat not seen yet
                if (e.Never)
                {
                    if (_broken.ContainsKey(e.Name) || (hi is { } h0 && rel > h0 + 1e-9)) continue;
                    if (When(e.Cue, lo) is { } b && (lo is null || b >= lo - 1e-9) && (hi is null || b <= hi + 1e-9)) _broken[e.Name] = b;
                }
                else if (!_times.ContainsKey(e.Name) && When(e.Cue, lo) is { } t && (lo is null || t >= lo - 1e-9) && (hi is null || t <= hi + 1e-9))
                    _times[e.Name] = t;
                else if (!_times.ContainsKey(e.Name) && !_outside.ContainsKey(e.Name) && When(e.Cue) is { } any)
                    _outside[e.Name] = any;   // held, but not in its window — kept to tell "moved" from "never"
            }
        }

        /// <summary>The run is over: a last sample, then the <c>atEnd</c> expectations.</summary>
        public void Finish(double rel)
        {
            Observe(rel);
            foreach (var e in _checks.Expect.Where(x => x.AtEnd))
                if (When(e.Cue) is not null) _times[e.Name] = rel;
        }

        public IReadOnlyList<Verdict> Verdicts() => _checks.Expect.Select(e =>
            e.Never ? new Verdict(e.Name, !_broken.ContainsKey(e.Name), _broken.TryGetValue(e.Name, out var b) ? b : null, "never")
                    : new Verdict(e.Name, _times.ContainsKey(e.Name), _times.TryGetValue(e.Name, out var t) ? t : null, e.AtEnd ? "atEnd" : "once",
                                  _times.ContainsKey(e.Name) ? null : _outside.TryGetValue(e.Name, out var o) ? o : null))
            .ToList();

        // ── the cues ──────────────────────────────────────────────────────────────────────────

        /// <summary>When the cue holds in this run (for an event, the n-th at or after <paramref name="from"/>); null when not (yet).</summary>
        private double? When(CheckCue c, double? from = null)
        {
            var a = c.Spec;
            switch (c.On)
            {
                case "shot":
                {
                    var rows = _shots.Values.Where(r => (a.By == null || Is(r["shooter"], a.By)) && (a.Target == null || Is(r["target"], a.Target))
                                                        && (c.Hit == null || Is(r["hit"], c.Hit)) && WhereOk(r, c.Where))
                        .Select(r => Rel(r["firedAt"])).Where(t => from is null || t >= from - 1e-9).ToList();
                    return rows.Count >= a.N ? rows[a.N - 1] : null;
                }
                case "blast":
                {
                    var rows = _blasts.Where(r => (a.By == null || Is(r["shooter"], a.By)) && WhereOk(r, c.Where))
                        .Select(r => Num(r["seenAt"])!.Value).Where(t => from is null || t >= from - 1e-9).ToList();
                    return rows.Count >= a.N ? rows[a.N - 1] : null;
                }
                case "hit":
                {
                    if (a.Target == null) return null;
                    var hits = _shots.Values.Where(r => r["outcome"]?.ToString() == "Hit" && Is(r["hit"], a.Target)).Select(r => Rel(r["firedAt"]))
                        // ⭐ a burst that STRUCK the unit (a drone's dive, a round's warhead on it) — as AnchorBook.Hit reads it — or hurt it
                        .Concat(_blasts.Where(r => Is(r["struck"], a.Target)
                                                   || r["effects"] is JsonArray fx && fx.Any(x => Is(x?["entity"], a.Target) && Num(x?["totalDamage"]) > 0))
                                       .Select(r => Num(r["seenAt"])!.Value))
                        .Where(t => from is null || t >= from - 1e-9).OrderBy(t => t).ToList();
                    return hits.Count >= a.N ? hits[a.N - 1] : null;
                }
                case "field":
                {
                    var units = UnitsNamed(a.Who!).ToList();
                    var want = c.Value is JsonValue v && v.ToString() == "$now" ? JsonValue.Create(_rel + _t0) : c.Value;
                    int held = units.Count(id => Compare(At(Components(id), c.Path!), c.Op, want));
                    bool ok = c.Quantifier switch
                    {
                        "any" => held > 0,
                        "all" => units.Count > 0 && held == units.Count,
                        _ => held >= int.Parse(c.Quantifier[8..], CultureInfo.InvariantCulture),
                    };
                    return ok ? _rel : null;
                }
                default:
                {
                    if (_w.World is not { } world) return null;
                    // a task cue may name several behaviours: any of them
                    var specs = a.Is is JsonArray many ? many.Select(x => a with { Is = x?.DeepClone() }) : new[] { a };
                    return specs.Any(s => AnchorBook.HoldsFor(s, world, _w.Units, _w.BehaviourName)) ? _rel : null;
                }
            }
        }

        private double Rel(JsonNode? firedAt) => (Num(firedAt) ?? _t0 + _rel) - _t0;

        private JsonObject? Components(long id)
        {
            if (!_comps.TryGetValue(id, out var c)) _comps[id] = c = _w.Components(id);
            return c;
        }

        private IEnumerable<long> UnitsNamed(string who)
        {
            if (who.StartsWith('*'))
            {
                var part = who[1..];
                foreach (var (id, n) in _nameOf)
                    if (n.Contains(part, StringComparison.Ordinal)) yield return id;
            }
            else
                foreach (var (id, n) in _nameOf)
                    if (string.Equals(n, who, StringComparison.Ordinal)) yield return id;   // every unit that carries the name
        }

        /// <summary>A row's unit (a network id, or the name already resolved) against a name or <c>*part</c>.</summary>
        private bool Is(JsonNode? unit, string who)
        {
            if (unit is null) return false;
            string? name = Num(unit) is { } id ? (_nameOf.TryGetValue((long)id, out var n) ? n : null) : unit.ToString();
            if (name == null) return false;
            return who.StartsWith('*') ? name.Contains(who[1..], StringComparison.Ordinal) : string.Equals(name, who, StringComparison.Ordinal);
        }

        private void ReadRows()
        {
            foreach (var r in _w.Shots().OfType<JsonObject>())
            {
                long seq = (long)(Num(r["seq"]) ?? -1);
                if (seq <= _shotFloor) continue;
                // ⚠ a round is logged when FIRED and ends later in place: keep its latest row
                var row = (JsonObject)r.DeepClone();
                row.Remove("crossings");
                foreach (var k in new[] { "shooter", "target", "hit" })
                    if (Num(row[k]) is { } id && _nameOf.TryGetValue((long)id, out var n)) row[k] = n;
                _shots[seq] = row;
            }
            var fresh = new List<JsonObject>();
            foreach (var r in _w.Blasts().OfType<JsonObject>())
            {
                long seq = (long)(Num(r["seq"]) ?? -1);
                if (seq <= _blastFloor || seq <= _blastSeen) continue;
                var row = (JsonObject)r.DeepClone();
                row.Remove("rays");
                foreach (var k in new[] { "shooter", "struck" })
                    if (Num(row[k]) is { } id && _nameOf.TryGetValue((long)id, out var n)) row[k] = n;
                if (row["effects"] is JsonArray fx)
                    foreach (var x in fx.OfType<JsonObject>())
                        if (Num(x["entity"]) is { } eid && _nameOf.TryGetValue((long)eid, out var en)) x["entity"] = en;
                row["seenAt"] = Math.Round(_rel, 4);
                fresh.Add(row);
            }
            foreach (var row in fresh.OrderBy(r => Num(r["seq"])))
            {
                _blasts.Add(row);
                _blastSeen = Math.Max(_blastSeen, (long)(Num(row["seq"]) ?? -1));
            }
        }

        private static long MaxSeq(JsonArray rows) => rows.OfType<JsonObject>().Select(r => (long)(Num(r["seq"]) ?? -1)).DefaultIfEmpty(-1).Max();

        // ── values ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A number, whatever CLR type the row or dump built it from. ⚠ <c>TryGetValue&lt;double&gt;</c> alone answers false for a value
        /// created from a <c>long</c> or a <c>float</c> (a shot's <c>seq</c>, a position) — measured: every row was skipped as "no number".
        /// </summary>
        public static double? Num(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<float>(out var f)) return f;
            if (v.TryGetValue<ulong>(out var ul)) return ul;
            if (v.TryGetValue<uint>(out var ui)) return ui;
            if (v.TryGetValue<short>(out var sh)) return sh;
            if (v.TryGetValue<ushort>(out var us)) return us;
            if (v.TryGetValue<byte>(out var b)) return b;
            if (v.TryGetValue<sbyte>(out var sb)) return sb;
            if (v.TryGetValue<decimal>(out var m)) return (double)m;
            return null;
        }

        /// <summary>The value at <c>A.B.C</c> (an array index is a number part).</summary>
        public static JsonNode? At(JsonNode? node, string path)
        {
            foreach (var part in path.Split('.'))
            {
                node = node switch
                {
                    JsonObject o => o[part],
                    JsonArray a when int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < a.Count => a[i],
                    _ => null,
                };
                if (node is null) return null;
            }
            return node;
        }

        public static bool Compare(JsonNode? value, string op, JsonNode? want)
        {
            if (value is null) return op == "==" && want is null;
            double? a = Num(value), b = Num(want);
            switch (op)
            {
                case "==": return a is { } x && b is { } y ? Math.Abs(x - y) <= 1e-3 : Text(value) == Text(want);
                case "!=": return !Compare(value, "==", want);
                case "<": return a < b;
                case "<=": return a <= b;
                case ">": return a > b;
                case ">=": return a >= b;
                case "in": return want is JsonArray set && set.Any(w => Compare(value, "==", w));
                case "contains": return Text(value).Contains(Text(want), StringComparison.Ordinal);
                default: return false;
            }
        }

        private static string Text(JsonNode? n) => n is JsonValue v ? v.ToString() : n?.ToJsonString() ?? "";

        private static bool WhereOk(JsonObject row, IReadOnlyList<(string Field, string Op, JsonNode? Value)> where)
            => where.All(w => Compare(At(row, w.Field), w.Op, w.Value));
    }

    /// <summary>
    /// ⭐ <c>CE-4042</c> (§4) — the run as a golden: the times, the rows, and every named unit at every <c>golden.every</c> seconds — its
    /// position and health in full, every component as a short hash of its dump (a full dump of a 7-unit scenario was 1.7 MB a minute;
    /// a hash still names the FIRST component to differ, which is where a bisect starts).
    /// </summary>
    public sealed class ScenarioGolden
    {
        public const string FileName = "golden.json";

        /// <summary>⭐ <c>CE-3334</c> — the version of the by-meaning rules a golden was captured with (<c>"meaning"</c> in the file): 2 = handles
        /// AND per-run request ids by meaning. A golden without it (captured before) compares with <see cref="PerRunIds"/> left out.</summary>
        public const int MeaningVersion = 2;

        /// <summary>⭐ <c>CE-3334</c> — components whose value is an id MINTED PER RUN (a fresh <c>Guid</c> each time): its value means "the
        /// request that made this unit", so it is written as <c>"@request:&lt;the first unit it made&gt;"</c> wherever it appears.</summary>
        public static readonly IReadOnlyList<string> PerRunIds = new[] { "CreationRequestId" };

        private readonly IVerifyWorld _w;
        private readonly Dictionary<Guid, string> _requests = new();
        private readonly bool _full;
        private readonly JsonArray _snapshots = new();

        /// <param name="full">Keep each component in full, not its hash (<c>golden.full</c> — to run a difference down).</param>
        public ScenarioGolden(IVerifyWorld world, bool full = false)
        {
            _w = world ?? throw new ArgumentNullException(nameof(world));
            _full = full;
        }

        public int Snapshots => _snapshots.Count;

        /// <summary>The time of the last snapshot (−∞ when none).</summary>
        public double LastTime { get; private set; } = double.NegativeInfinity;

        public void Snapshot(double rel)
        {
            var units = new JsonObject();
            // ⭐ two units that share a name are both kept: "M1 Abrams", "M1 Abrams #2" (in network-id order — ids repeat per world)
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var all = new List<(string Name, JsonObject Comps)>();
            foreach (var (id, unitName) in _w.Named().OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id))
            {
                int k = seen[unitName] = seen.TryGetValue(unitName, out var c) ? c + 1 : 1;
                if (_w.Components(id) is { } found) all.Add((k == 1 ? unitName : $"{unitName} #{k}", found));
            }
            // ⭐ CE-3334 — a per-run id means the unit it made: the first unit (in name order) that carries it
            _requests.Clear();
            foreach (var (name, comps) in all)
                foreach (var idType in PerRunIds)
                    if (comps[idType]?["Value"] is JsonValue v && v.TryGetValue<string>(out var text) && Guid.TryParse(text, out var g) && g != Guid.Empty)
                        _requests.TryAdd(g, name);
            foreach (var (name, comps) in all)
            {
                var hashes = new JsonObject();
                foreach (var (type, value) in comps.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    var meant = ByMeaning(value?.DeepClone());
                    hashes[type] = _full ? meant : Hash(meant);
                }
                var p = ConditionBook.At(comps, "SimTransform.Position");
                units[name] = new JsonObject
                {
                    ["position"] = p switch
                    {
                        JsonObject o => new JsonArray(R(o["X"]), R(o["Y"]), R(o["Z"])),
                        JsonArray a => new JsonArray(a.Select(R).ToArray()),   // the dump writes a vector as [x, y, z]
                        _ => p?.DeepClone(),
                    },
                    ["health"] = ConditionBook.At(comps, "Health.Current")?.DeepClone(),
                    ["components"] = hashes,
                };
            }
            _snapshots.Add(new JsonObject { ["t"] = Math.Round(rel, 4), ["units"] = units });
            LastTime = rel;
        }

        private static readonly System.Text.RegularExpressions.Regex HandleShape = new(@"^\[(\d+), v\d+\]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// ⭐ A held entity handle (<c>"[12, v1]"</c> — its INDEX, i.e. allocation order) becomes the unit it means (<c>"@Technical"</c>, or
        /// <c>"@?"</c> unnamed): ⛔ otherwise one entity more or less renumbers every later handle and every holder's hash changes with no
        /// change in meaning. 📌 Measured by backend on <c>ua-tank-ammo</c> (CE-2349 created one item fewer ⇒ <c>IsEmbarkedTag</c> and
        /// <c>PassengerBuffer</c> differed). ⚠ A network id held as a NUMBER is not rewritten — it cannot be told from any other number.
        /// </summary>
        private JsonNode? ByMeaning(JsonNode? n) => Meant(n) ?? n;

        /// <summary>The name a held handle means, or null when <paramref name="n"/> is not one — containers are rewritten in place, and only
        /// a REPLACED child is assigned back (⛔ assigning a node to its own slot throws "The node already has a parent").</summary>
        private JsonNode? Meant(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var k in o.Select(kv => kv.Key).ToList()) if (Meant(o[k]) is { } r) o[k] = r;
                    return null;
                case JsonArray a:
                    for (int i = 0; i < a.Count; i++) if (Meant(a[i]) is { } r) a[i] = r;
                    return null;
                case JsonValue v when v.TryGetValue<string>(out var text) && HandleShape.Match(text) is { Success: true } m:
                    return JsonValue.Create("@" + (_w.NameOfEntityIndex(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) ?? "?"));
                case JsonValue v when v.TryGetValue<string>(out var text) && Guid.TryParse(text, out var g) && _requests.TryGetValue(g, out var maker):
                    return JsonValue.Create("@request:" + maker);   // ⭐ CE-3334 — a per-run request id, by meaning
                case JsonValue v when ConditionBook.Num(v) is { } d && !double.IsFinite(d):
                    return Unwritable(d);
                default:
                    return null;
            }
        }

        private static JsonNode? R(JsonNode? n) => ConditionBook.Num(n) is { } d
            ? (double.IsFinite(d) ? JsonValue.Create(Math.Round(d, 3)) : Unwritable(d))
            : n?.DeepClone();

        /// <summary>⛔ JSON cannot write a non-finite number (it throws) — 📐 measured: a component held one and the whole verify failed at
        /// step 256. Written as its name instead: deterministic, and still says what the value was.</summary>
        private static JsonNode Unwritable(double d) => JsonValue.Create(double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity");

        /// <summary>FNV-1a 64 of the value's JSON — the dump writes a component's fields in one fixed order, so equal values hash alike.</summary>
        public static string Hash(JsonNode? value)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(value?.ToJsonString() ?? "null")) { h ^= b; h *= 1099511628211UL; }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>⭐ CE-4045 H1a — only the snapshots (what <see cref="CompareFrom"/> reads): a recorded run has no condition book.</summary>
        public JsonObject SnapshotsJson() => new() { ["snapshots"] = _snapshots.DeepClone() };

        /// <summary>
        /// ⭐ The component names a recording carries — its OWN schema manifest (<c>&lt;file&gt;.meta.json</c>), as a snapshot names them (the
        /// short type name). One reader for R1's checkpoint (CE-3299 B5) and H1a's live recording (CE-4045): a checkpoint IS a one-frame
        /// recording. Null when the file has no manifest beside it.
        /// </summary>
        public static Func<string, bool>? CarriedBy(string recording)
        {
            var metaPath = recording + ".meta.json";
            if (!System.IO.File.Exists(metaPath)) return null;
            var meta = Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Deserialize(System.IO.File.ReadAllText(metaPath));
            var names = new HashSet<string>((meta.SchemaManifest ?? new()).Values.Select(v => v.Name.Replace('+', '.').Split('.')[^1]), StringComparer.Ordinal);
            return names.Contains;
        }

        public JsonObject ToJson(string scenario, long steps, ConditionBook book) => new()
        {
            ["$doc"] = "scenario golden — docs/DESIGN_Scenario_Regression.md §4; re-record: Hrot.ClusterRunner --mode editor --headless --verify "
                       + scenario + " --verify-capture (or POST /director/verify {scenario, capture: true})",
            ["scenario"] = scenario,
            ["meaning"] = MeaningVersion,
            ["steps"] = steps,
            ["times"] = new JsonObject(book.Times.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                                           .Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)Math.Round(kv.Value, 4)))),
            ["shots"] = new JsonArray(book.ShotRows.Select(r => (JsonNode?)Pick(r, "seq", "firedAt", "shooter", "target", "hit", "outcome", "penetration", "weaponIndex")).ToArray()),
            ["blasts"] = new JsonArray(book.BlastRows.Select(r => (JsonNode?)Pick(r, "seq", "seenAt", "shooter", "warheadSource", "burst")).ToArray()),
            ["snapshots"] = _snapshots.DeepClone(),
        };

        private static JsonObject Pick(JsonObject row, params string[] keys)
        {
            var o = new JsonObject();
            foreach (var k in keys) o[k] = row[k] is JsonValue v && ConditionBook.Num(v) is { } d ? JsonValue.Create(Math.Round(d, 4)) : row[k]?.DeepClone();
            return o;
        }

        /// <summary>
        /// Every difference of <paramref name="run"/> from <paramref name="golden"/> (numbers within 0.001 — <see cref="ComponentDiffService"/>,
        /// the <c>/diff/compare</c> tolerance), ordered so the FIRST line answers "when did it start to differ, and in whom".
        /// </summary>
        public static IReadOnlyList<string> Compare(JsonObject golden, JsonObject run)
        {
            // ⭐ CE-3334 — a golden captured before per-run ids were written by meaning holds their raw hash, which no later run can repeat:
            //   compare it without them (a re-capture brings them back, by meaning)
            if (golden.ContainsKey("scenario") && golden["meaning"] is null)
            {
                golden = WithoutPerRunIds(golden);
                run = WithoutPerRunIds(run);
            }
            var diff = new ComponentDiffService();
            var lines = new List<string>();
            foreach (var key in new[] { "steps", "times", "shots", "blasts" })
            {
                var paths = Modified(diff.ComputeDiff(key, golden[key], run[key], 0.001), key).ToList();
                if (paths.Count == 0) continue;
                lines.Add($"{key}: {paths.Count} difference(s), first {paths[0]}");
            }
            var gs = golden["snapshots"] as JsonArray ?? new JsonArray();
            var rs = run["snapshots"] as JsonArray ?? new JsonArray();
            if (gs.Count != rs.Count) lines.Add($"snapshots: golden {gs.Count}, run {rs.Count}");
            for (int i = 0; i < Math.Min(gs.Count, rs.Count); i++)
            {
                var paths = Modified(diff.ComputeDiff("t", gs[i], rs[i], 0.001), "").ToList();
                if (paths.Count == 0) continue;
                var t = gs[i]?["t"];
                var units = paths.Select(p => p.Split('.', 3) is { Length: >= 2 } s && s[0] == "units" ? s[1] : "?").Distinct().ToList();
                lines.Insert(0, $"FIRST differing snapshot at t={t} s — {paths.Count} value(s) in {string.Join(", ", units.Take(6))}{(units.Count > 6 ? " …" : "")}");
                lines.InsertRange(1, paths.Take(8).Select(p => "    " + p));
                break;
            }
            return lines;
        }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B5 (R1, restore design §1k) — two runs' snapshots from <paramref name="t1"/> on, compared as
        /// <see cref="Compare"/> does: <paramref name="straight"/> ran from 0, <paramref name="restored"/> began from a checkpoint at
        /// <paramref name="t1"/>. ⛔ The rows before t1 (times, shots, blasts) are not the restored run's to have, so only state is compared.
        /// <para>⭐ <paramref name="carried"/> (a component's name ⇒ the checkpoint carries it; null = every one): with it, only the CARRIED
        /// components (and the position and health) are compared here — a component a checkpoint does not carry by design (<c>NoReplay</c>)
        /// is listed by <see cref="CompareNotCarried"/> instead, since what it changes shows up in the carried state.</para>
        /// </summary>
        public static IReadOnlyList<string> CompareFrom(JsonObject straight, JsonObject restored, double t1, Func<string, bool>? carried = null)
            => Compare(From(straight, t1, carried, true), From(restored, t1, carried, true));

        /// <summary>⭐ B5 — the differences from <paramref name="t1"/> on in the components the checkpoint does NOT carry (for information).</summary>
        public static IReadOnlyList<string> CompareNotCarried(JsonObject straight, JsonObject restored, double t1, Func<string, bool> carried)
            => Compare(From(straight, t1, carried, false), From(restored, t1, carried, false));

        private static JsonObject WithoutPerRunIds(JsonObject run)
        {
            var copy = run.DeepClone().AsObject();
            foreach (var snap in (copy["snapshots"] as JsonArray) ?? new JsonArray())
                foreach (var (_, unit) in (snap?["units"] as JsonObject) ?? new JsonObject())
                    if (unit?["components"] is JsonObject comps)
                        foreach (var idType in PerRunIds) comps.Remove(idType);
            return copy;
        }

        private static JsonObject From(JsonObject run, double t1, Func<string, bool>? carried, bool keepCarried)
        {
            var snapshots = new JsonArray();
            foreach (var n in (run["snapshots"] as JsonArray) ?? new JsonArray())
            {
                if (ConditionBook.Num(n?["t"]) is not { } t || t < t1 - 1e-6) continue;
                var snap = n!.DeepClone().AsObject();
                if (carried != null && snap["units"] is JsonObject units)
                    foreach (var (_, unit) in units)
                    {
                        if (unit is not JsonObject u) continue;
                        if (!keepCarried) { u.Remove("position"); u.Remove("health"); }
                        if (u["components"] is JsonObject comps)
                            foreach (var name in comps.Select(kv => kv.Key).ToList())
                                if (carried(name) != keepCarried) comps.Remove(name);
                    }
                snapshots.Add(snap);
            }
            return new JsonObject { ["snapshots"] = snapshots };
        }

        private static IEnumerable<string> Modified(DiffNode? node, string path)
        {
            if (node is null || !node.IsModified) yield break;
            switch (node)
            {
                case DiffValue v:
                    yield return $"{path}: golden {Short(v.OldValue)} run {Short(v.NewValue)}";
                    break;
                case DiffObject o:
                    foreach (var c in o.Children)
                        foreach (var p in Modified(c, path.Length == 0 ? c.Name : c.Name.Length == 0 ? path : path + "." + c.Name))
                            yield return p;
                    break;
            }
        }

        private static string Short(string s) => s.Length > 60 ? s[..57] + "…" : s;
    }

    /// <summary>What one verification found.</summary>
    public sealed class VerifyResult : IRunResult
    {
        public List<Verdict> Verdicts { get; } = new();
        /// <summary>none (a demo) · same · different · missing · captured · not-captured</summary>
        public string Golden { get; set; } = "none";
        public List<string> GoldenDiff { get; } = new();
        public List<string> Problems { get; } = new();
        public List<string> Written { get; } = new();
        public JsonObject? GoldenJson { get; set; }

        /// <summary>The run was a RELOAD in a process that had loaded a scenario before (CE-3294: state that outlives a load).</summary>
        public bool Reload { get; private set; }

        /// <summary>⭐ <c>CE-4045</c> H1a — the live run against its own recording (Scenario Regression §10-1): null (not asked) · same ·
        /// different · failed (it could not be compared — <see cref="ReplayDiff"/> says why).</summary>
        public string? Replay { get; set; }
        /// <summary>The first difference in what the recording carries (as <see cref="GoldenDiff"/>), or why the comparison failed.</summary>
        public List<string> ReplayDiff { get; } = new();
        /// <summary>⭐ For information, never a failure (RH-4): differences in components the recording does NOT carry by its manifest.</summary>
        public List<string> ReplayNotCarried { get; } = new();
        /// <summary>The live snapshots matched to a recorded frame.</summary>
        public int ReplaySnapshots { get; set; }

        /// <summary>⭐ <c>CE-4045</c> H5 — the review of the live run against the live run: null (not asked) · same · different.</summary>
        public LookBackResult? Review { get; set; }
        /// <summary>⭐ The world after the return against the world the run left — EVERY component (the full checkpoint, R-3063).</summary>
        public List<string> ReturnDiff { get; } = new();

        /// <summary>⭐ <c>CE-4045</c> H1b — the product's cluster REPLAY of the finished run (entered, sought by recorded sim time) against the
        /// live run: null (not asked) · same · different.</summary>
        public LookBackResult? Playback { get; set; }

        /// <summary>⭐ CE-4061 — the key a director report writes this under.</summary>
        public string Kind => "verify";

        public bool Passed => Problems.Count == 0 && Verdicts.All(v => v.Ok) && Golden is ("none" or "same" or "captured") && Replay is (null or "same")
                              && Review is (null or { Verdict: "same" }) && ReturnDiff.Count == 0 && Playback is (null or { Verdict: "same" });

        /// <summary>
        /// ⭐ This run was a reload: a capture is REFUSED (a reload's golden bakes CE-3294 in — measured: <c>SopState</c> and the animation
        /// definition handle differ from a first load), and a difference is labelled as possibly the reload's, not a regression.
        /// </summary>
        public void MarkReload()
        {
            Reload = true;
            const string Fresh = "verify in a FRESH process: --verify <s> [--verify-capture], or POST /director/verify {scenario, fresh: true}";
            if (Golden == "captured") { Golden = "not-captured"; GoldenDiff.Insert(0, "this run was a RELOAD (CE-3294) — a golden is recorded only from a first load: " + Fresh); }
            else if (Golden == "different") GoldenDiff.Insert(0, "⚠ this run was a RELOAD (CE-3294): a difference may be the reload's, not a regression — " + Fresh);
        }

        public JsonObject ToJson() => new()
        {
            ["passed"] = Passed,
            ["reload"] = Reload,
            ["verdicts"] = new JsonArray(Verdicts.Select(v => (JsonNode?)v.ToJson()).ToArray()),
            ["golden"] = Golden,
            ["goldenDiff"] = new JsonArray(GoldenDiff.Select(x => (JsonNode?)x).ToArray()),
            ["written"] = new JsonArray(Written.Select(x => (JsonNode?)x).ToArray()),
            ["problems"] = new JsonArray(Problems.Select(x => (JsonNode?)x).ToArray()),
            ["replay"] = Replay,
            ["replaySnapshots"] = Replay is null ? null : ReplaySnapshots,
            ["replayDiff"] = Replay is null ? null : new JsonArray(ReplayDiff.Select(x => (JsonNode?)x).ToArray()),
            ["replayNotCarried"] = Replay is null ? null : new JsonArray(ReplayNotCarried.Select(x => (JsonNode?)x).ToArray()),
            ["review"] = Review?.ToJson(),
            ["returnDiff"] = Review is null ? null : new JsonArray(ReturnDiff.Select(x => (JsonNode?)x).ToArray()),
            ["playback"] = Playback?.ToJson(),
        };
    }

    /// <summary>
    /// ⭐ <c>CE-4045</c> — what ONE look-back found: the run's live snapshots against the world the product shows when it looks back at
    /// the same moments — a review of the live run (H5) or a replay of the finished one (H1b). One shape, one loop
    /// (<c>DirectorRunner</c>'s look-back), one comparator (<see cref="ScenarioGolden.CompareFrom"/>).
    /// </summary>
    public sealed class LookBackResult
    {
        /// <summary>same · different (also when a seek never landed, or the way back never came — <see cref="Problems"/> says which).</summary>
        public string Verdict { get; set; } = "different";
        /// <summary>The live snapshots looked back at (the times the product actually STOOD at).</summary>
        public int Snapshots { get; set; }
        /// <summary>The first difference from the live run, on what the recording carries.</summary>
        public List<string> Diff { get; } = new();
        /// <summary>For information (RH-4): what differs in components the recording does NOT carry by its manifest.</summary>
        public List<string> NotCarried { get; } = new();
        /// <summary>Seeks that did not land, a way back that did not come.</summary>
        public List<string> Problems { get; } = new();

        public JsonObject ToJson() => new()
        {
            ["verdict"] = Verdict,
            ["snapshots"] = Snapshots,
            ["diff"] = new JsonArray(Diff.Select(x => (JsonNode?)x).ToArray()),
            ["notCarried"] = new JsonArray(NotCarried.Select(x => (JsonNode?)x).ToArray()),
            ["problems"] = new JsonArray(Problems.Select(x => (JsonNode?)x).ToArray()),
        };
    }

    /// <summary>
    /// ⭐ <c>CE-4042</c> (§2a) — one scenario verified: the runner calls <see cref="Observe"/> and <see cref="ApplyDue"/> on EVERY frame (one
    /// step each — so an action lands on its exact step), and <see cref="Finish"/> when <c>runFor</c> is reached.
    /// </summary>
    public sealed class ScenarioVerification
    {
        private readonly ScenarioChecks _checks;
        private readonly JsonObject? _golden;
        private readonly bool _capture;
        private readonly IVerifyWorld _w;
        private readonly ConditionBook _book;
        private readonly ScenarioGolden? _run;
        private readonly HashSet<CheckAction> _done = new();
        private readonly double? _every;
        private double _nextSample, _nextSnapshot;
        public List<string> ActionProblems { get; } = new();

        /// <param name="startAt">⭐ <c>CE-3299</c> B5 (R1) — the run begins at this relative time (a checkpoint restored at T1): samples and
        /// snapshots fall on the same grid as a run from 0, so the two can be compared snapshot for snapshot.</param>
        /// <param name="snapshotEvery">⭐ B5 — snapshot every this many seconds, every component in full, whatever the checks say (R1
        /// compares full component state, and a red must name the component).</param>
        public ScenarioVerification(ScenarioChecks checks, JsonObject? golden, bool capture, IVerifyWorld world, double t0,
                                    double startAt = 0, double? snapshotEvery = null)
        {
            _checks = checks ?? throw new ArgumentNullException(nameof(checks));
            _golden = golden;
            _capture = capture;
            _w = world ?? throw new ArgumentNullException(nameof(world));
            _book = new ConditionBook(checks, world, t0);
            _every = snapshotEvery ?? checks.GoldenEvery;
            _run = _every is null ? null : new ScenarioGolden(world, checks.GoldenFull || snapshotEvery != null);
            _nextSample = FirstAtOrAfter(startAt, checks.SampleEvery);
            _nextSnapshot = _every is { } e ? FirstAtOrAfter(startAt, e) : 0;
        }

        private static double FirstAtOrAfter(double t, double step) => t <= 0 || !(step > 0) ? 0 : Math.Ceiling(t / step - 1e-4) * step;   // a run's time carries float dust

        /// <summary>⭐ B5 — the indexes (in <see cref="ScenarioChecks.Do"/>) of the actions applied so far: a checkpoint carries them to the
        /// process that restores it, which must not apply them again.</summary>
        public IReadOnlyList<int> DoneActions => _checks.Do.Select((a, i) => (a, i)).Where(x => _done.Contains(x.a)).Select(x => x.i).ToList();

        /// <summary>⭐ B5 — the actions the checkpoint's own run had already applied count as done here.</summary>
        public void MarkDone(IEnumerable<int> indexes)
        {
            foreach (var i in indexes) if (i >= 0 && i < _checks.Do.Count) _done.Add(_checks.Do[i]);
        }

        public ScenarioChecks Checks => _checks;
        public ConditionBook Book => _book;

        /// <summary>⭐ CE-4045 H5 — the run's own snapshots so far (null when the checks snapshot nothing).</summary>
        public ScenarioGolden? Run => _run;

        /// <summary>The names the checks use that this scenario's cast does not carry (a <c>*part</c> needs one).</summary>
        public static IReadOnlyList<string> MissingNames(ScenarioChecks checks, IShotWorld units) =>
            checks.Names().Where(n => n.StartsWith('*') ? !units.Names.Any(x => x.Contains(n[1..], StringComparison.Ordinal)) : units.Resolve(n) is null)
                  .Select(n => $"no unit named '{n}' in {checks.Scenario}").ToList();

        public void Observe(double rel)
        {
            if (rel + 1e-9 >= _nextSample) { _book.Observe(rel); _nextSample += _checks.SampleEvery; }
            if (_run != null && rel + 1e-9 >= _nextSnapshot) { _run.Snapshot(rel); _nextSnapshot += _every!.Value; }
        }

        /// <summary>Each action due by <paramref name="rel"/> is applied now (before this frame's step).</summary>
        public void ApplyDue(double rel)
        {
            foreach (var a in _checks.Do)
            {
                if (_done.Contains(a) || _book.TimeOf(a.At) is not { } at || rel + 1e-9 < at) continue;
                _done.Add(a);
                var ids = _w.Named().Where(x => a.Who.StartsWith('*') ? x.Name.Contains(a.Who[1..], StringComparison.Ordinal)
                                                                       : string.Equals(x.Name, a.Who, StringComparison.Ordinal))
                                    .Select(x => x.Id).Distinct().ToList();
                if (ids.Count == 0) ActionProblems.Add($"do at {rel:0.###} s: no unit '{a.Who}'");
                foreach (var id in ids)
                    if (_w.SetComponent(id, a.Component, a.Patch) is { } why) ActionProblems.Add($"do at {rel:0.###} s: {a.Who}.{a.Component}: {why}");
                if (a.Name != null) _book.Record(a.Name, rel);
            }
        }

        public VerifyResult Finish(double rel, long steps)
        {
            _book.Finish(rel);
            var r = new VerifyResult();
            r.Verdicts.AddRange(_book.Verdicts());
            r.Problems.AddRange(ActionProblems);
            foreach (var a in _checks.Do.Where(a => !_done.Contains(a))) r.Problems.Add($"an action never ran (its time '{a.At}' was not reached)");
            if (_run == null) return r;
            if (_run.LastTime < rel - 1e-6) _run.Snapshot(rel);   // the end state is always in the golden
            var json = _run.ToJson(_checks.Scenario, steps, _book);
            json["verdicts"] = new JsonObject(r.Verdicts.Select(v => KeyValuePair.Create(v.Name, (JsonNode?)(v.Time is { } t ? Math.Round(t, 4) : null))));
            r.GoldenJson = json;
            if (_capture)
            {
                bool ok = r.Problems.Count == 0 && r.Verdicts.All(v => v.Ok);
                r.Golden = ok ? "captured" : "not-captured";
                if (!ok) r.GoldenDiff.Add("a golden of a failing run would bless the failure — fix the run, then capture");
            }
            else if (_golden == null) r.Golden = "missing";
            else
            {
                var diff = ScenarioGolden.Compare(_golden, json);
                r.Golden = diff.Count == 0 ? "same" : "different";
                r.GoldenDiff.AddRange(diff);
            }
            return r;
        }
    }
}
