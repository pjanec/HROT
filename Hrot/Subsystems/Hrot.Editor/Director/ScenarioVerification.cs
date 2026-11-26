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
        IReadOnlyList<(int Id, string Name)> Named();

        /// <summary>The name of the unit at entity INDEX <paramref name="index"/> (the dump writes a held handle as <c>"[index, vGen]"</c>);
        /// null when it has none.</summary>
        string? NameOfEntityIndex(int index);

        /// <summary>A behaviour's name by its hash (the <c>task</c> cue).</summary>
        string? BehaviourName(int hash);

        /// <summary>The unit's components as the entity dump writes them (<c>{"Health": {...}, ...}</c>); null when it is gone.</summary>
        JsonObject? Components(int networkId);

        /// <summary>The shot log's rows, newest first (the <c>/combat/shots</c> rows).</summary>
        JsonArray Shots();

        /// <summary>The detonation log's rows, newest first (the <c>/combat/detonations</c> rows).</summary>
        JsonArray Blasts();

        /// <summary>⭐ CE-4676 — the reports the brain node's inbox saw, newest first (the <c>GET /squad/reports</c> rows: serial, seenAt, from, to — network
        /// ids — and kind); empty where the host serves none.</summary>
        JsonArray Reports() => new();

        /// <summary>⭐ CE-4679 — a commander seat holds the clock for its mind (think-pause): the run steps no further until it answers. False where
        /// the host has no seats.</summary>
        bool CommanderHoldsClock() => false;

        /// <summary>Patches one component; null when written, else why not.</summary>
        string? SetComponent(int networkId, string component, JsonObject patch);

        /// <summary>⭐ CE-3494 F3 — gives the unit an operator order (a <c>POST /orders</c> item, no <c>networkId</c>); null when accepted, else why not.</summary>
        string? Order(int networkId, JsonObject order) => "this host gives no orders";

        /// <summary>⭐ CE-3494 F3 — resumes the unit's held mission plan (<c>POST /missions/{id}/resume</c>); null when sent, else why not.</summary>
        string? ResumePlan(int networkId) => "this host resumes no plans";

        /// <summary>⭐ CE-11061 — revives the unit (<c>POST /entities/{id}/revive</c>); null when revived or asked of its owner, else why not.</summary>
        string? Revive(int networkId) => "this host revives no units";
    }

    /// <summary>One expectation's verdict: <see cref="Time"/> is when it held (or, for <c>never</c>, when it was broken).</summary>
    /// <remarks><see cref="Outside"/>: a failed expectation's cue DID hold, but outside its window (at that time) — "it moved" is not
    /// "it never happened", and the report must tell them apart.</remarks>
    /// <remarks>⭐ <c>CE-2458</c> <see cref="By"/>: a broken <c>never</c> over units (<c>cover</c>, <c>still</c>, <c>exposed</c>, <c>field</c>) names the unit that
    /// broke it — a layer-1 realism row is only actionable when it says WHO (📐 uf-car-shootout: "someone stood in sight 15 s").</remarks>
    public sealed record Verdict(string Name, bool Ok, double? Time, string Kind, double? Outside = null, string? By = null)
    {
        public JsonObject ToJson() => new()
        {
            ["name"] = Name, ["ok"] = Ok, ["t"] = Time is { } t ? Math.Round(t, 4) : null, ["kind"] = Kind,
            ["heldOutsideItsWindowAt"] = Outside is { } o ? Math.Round(o, 4) : null,
            ["by"] = By,
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
        // ⭐ CE-4676 — the reports this node's inbox saw (SquadReportLog), in arrival order: {serial, t (run-relative), from, to, kind}
        private readonly List<JsonObject> _reports = new();
        private long _reportSeen;
        private long _shotFloor = -1, _blastFloor = -1, _blastSeen = -1;
        private Dictionary<int, string> _nameOf = new();
        private double _rel;
        private readonly Dictionary<int, JsonObject?> _comps = new();
        // ⭐ CE-2455 — "for": since when each (cue, unit) has held without a break (unit -1 = the cue itself); "still": where each (cue, unit) settled
        private readonly Dictionary<(CheckCue Cue, int Unit), double> _heldSince = new();
        private readonly Dictionary<(CheckCue Cue, int Unit), System.Numerics.Vector2> _settledAt = new();
        // ⭐ CE-2456 — the standing vehicles' cover points of THIS sample (the AI's own live rule), built on the first cover cue that asks
        private List<Fdp.Toolkit.Spatial.Eqs.CoverPoint>? _vehiclePoints;
        // ⭐ CE-2458 — the unit that made each per-unit cue hold THIS sample, and who broke each never row
        private readonly Dictionary<CheckCue, int> _holder = new();
        private readonly Dictionary<string, string> _brokenBy = new(StringComparer.Ordinal);

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
            _vehiclePoints = null;
            _holder.Clear();
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
                    if (When(e.Cue, lo) is { } b && (lo is null || b >= lo - 1e-9) && (hi is null || b <= hi + 1e-9))
                    {
                        _broken[e.Name] = b;
                        if (_holder.TryGetValue(e.Cue, out var who) && _nameOf.TryGetValue(who, out var name)) _brokenBy[e.Name] = name;   // ⭐ CE-2458
                    }
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
            (e.If != null && !_times.ContainsKey(e.If)) || (e.Unless != null && _times.ContainsKey(e.Unless)) ? new Verdict(e.Name, true, null, "skipped")
            : e.Never ? new Verdict(e.Name, !_broken.ContainsKey(e.Name), _broken.TryGetValue(e.Name, out var b) ? b : null, "never",
                                    By: _brokenBy.TryGetValue(e.Name, out var by) ? by : null)
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
                case "report":   // ⭐ CE-4676 — the n-th report BY a unit (its sender) TO a unit (target), of a kind (is: "Spot" or ["Spot", "Contact"])
                {
                    var rows = _reports.Where(r => (a.By == null || Is(r["from"], a.By)) && (a.Target == null || Is(r["to"], a.Target))
                                                   && (a.Is == null || KindIs(r["kind"]?.ToString(), a.Is)))
                        .Select(r => Num(r["t"])!.Value).Where(t => from is null || t >= from - 1e-9).ToList();
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
                    if (c.Dead) units = units.Where(id => HealthOf(id) is <= 0f).ToList();   // ⭐ CE-4672 — only the dead the name matches
                    if (c.Fighting) units = units.Where(InTheFight).ToList();                // ⭐ CE-2455 — only the men still in the fight
                    var want = c.Value is JsonValue v && v.ToString() == "$now" ? JsonValue.Create(_rel + _t0) : c.Value;
                    int held = units.Count(id => Lasted(c, id, FieldHolds(Components(id), c.Path!, c.Op, want, c.Elements, c.Where)
                                                 && c.Also.All(x => FieldHolds(Components(id), x.Path, x.Op, x.Value, 1))));   // ⭐ CE-2443 — the same unit meets every "also" too · CE-2455 for
                    bool ok = c.Quantifier switch
                    {
                        "any" => held > 0,
                        "all" => units.Count > 0 && held == units.Count,
                        _ => held >= int.Parse(c.Quantifier[8..], CultureInfo.InvariantCulture),
                    };
                    return ok ? _rel : null;
                }
                case "knowledge":
                    return Lasted(c, -1, _w.World is { } kw && KnowledgeHolds(kw, c)) ? _rel : null;
                case "window":
                    return Lasted(c, -1, _w.World is { } ww && WindowHolds(ww, c)) ? _rel : null;
                case "far":
                case "close":
                    return Lasted(c, -1, DistanceHolds(c)) ? _rel : null;
                case "between":
                    return Lasted(c, -1, BetweenHolds(c)) ? _rel : null;
                case "cover":
                    return CoverHolds(c) ? _rel : null;      // per unit (CE-2455 for)
                case "still":
                    return StillHolds(c) ? _rel : null;      // ⭐ CE-2455 — per unit
                case "exposed":
                    return ExposedHolds(c) ? _rel : null;    // ⭐ CE-2456 — per unit
                case "perceives":
                    return PerceivesHolds(c) ? _rel : null;  // ⭐ CE-2476 — per unit
                case "bunched":
                    return Lasted(c, -1, BunchedHolds(c)) ? _rel : null;
                default:
                {
                    // a task cue may name several behaviours: any of them
                    var specs = a.Is is JsonArray many ? many.Select(x => a with { Is = x?.DeepClone() }) : new[] { a };
                    if (_w.World is { } world) return Lasted(c, -1, specs.Any(s => AnchorBook.HoldsFor(s, world, _w.Units, _w.BehaviourName))) ? _rel : null;
                    // ⭐ RT-5 (R-3094) — a world with no repository (the cluster tier reads a node over HTTP): the SAME value comparisons
                    //   (AnchorBook's TaskHolds / SideHolds / HealthHolds / EmbarkHolds / GestureHolds, CE-4117 N6) on the unit's dumped components.
                    //   ⛔ Before, every near / task / side / health / embark / gesture cue silently read false there.
                    var who = UnitsNamed(a.Who!).ToList();
                    return Lasted(c, -1, specs.Any(s => who.Any(id => Components(id) is { } comps && StateHolds(s, comps)))) ? _rel : null;
                }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-4624</c> (Minefields MR-19, R-4277) — what <see cref="CheckCue.Side"/> knows of the minefield <see cref="AnchorSpec.Who"/> names, as the side's
        /// own readers see it (<see cref="Fdp.Toolkit.Terrain.PlaceKnowledgeTable"/>, the union of every node's records): <c>Marked</c> holds when the side knows it in full
        /// (marked, or known at start); a cell state holds when the COUNT of the field's grid cells the side knows in that state compares by <c>op</c> / <c>value</c>.
        /// For any field the name matches.
        /// </summary>
        private bool KnowledgeHolds(EntityRepository world, CheckCue c)
        {
            if (c.Spec.Who is not { } who || c.Side is null || !Enum.TryParse<ForceId>(c.Side, true, out var force)) return false;
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.Minefield>()) return false;
            var state = c.Spec.Is is JsonValue sv ? sv.ToString() : "";
            var table = Fdp.Toolkit.Terrain.PlaceKnowledgeTable.Of(world);
            var outline = new List<System.Numerics.Vector2>();
            foreach (var id in UnitsNamed(who))
            {
                var field = new Fdp.Toolkit.Replication.EntityRef(id).Resolve(world);
                if (field.IsNull || !world.HasComponent<Fdp.Toolkit.Terrain.Minefield>(field)) continue;
                if (state == "Marked") { if (table.Marked((int)force, field)) return true; continue; }
                if (state == "Lane")   // ⭐ CE-2443 (MF-10) — the lane is in the side's knowledge as a lane RECORD of its side (a marked field reads every cell known-mined)
                {
                    if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.MinefieldLanes>() || !Fdp.Toolkit.Terrain.MinefieldIds.TryGuid(world, field, out var fg)) continue;
                    var lanes = new List<Fdp.Toolkit.Terrain.LaneItem>();
                    Fdp.Toolkit.Terrain.MinefieldLanesSystem.LanesOf(world, fg, lanes);
                    int own = 0;
                    foreach (var l in lanes) if (l.SidePlus1 == (int)force + 1) own++;
                    if (Compare(JsonValue.Create(own), c.Op, c.Value)) return true;
                    continue;
                }
                if (!Enum.TryParse<Fdp.Toolkit.Terrain.MineCellKnowledge>(state, out var want)) return false;
                outline.Clear();
                if (!Fdp.Toolkit.Terrain.MinefieldKnowledgeOps.TryOutline(world, field, outline, out var anchor)) continue;
                var g = Fdp.Toolkit.Terrain.MinefieldKnowledgeOps.Grid(world.GetComponentRO<Fdp.Toolkit.Terrain.Minefield>(field), anchor, outline);
                int n = 0;
                for (int r = 0; r < g.Rows; r++)
                    for (int col = 0; col < g.Cols; col++)
                        if (table.Knows((int)force, field, col, r) == want) n++;
                if (Compare(JsonValue.Create(n), c.Op, c.Value)) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-5033</c> (glass windows, docs/DESIGN_Glass_Windows.md §7) — a <c>window</c> cue: some glazed window of <see cref="CheckCue.Building"/> (or, with a
        /// <c>point</c>, the one whose pane is nearest to it horizontally, within <c>within</c> m — default 3) is in the state <c>is</c> names, as the world's own
        /// readers see it (<c>DoorStates.Windows</c>, the union of every node's records): <c>intact</c> = never hit, <c>cracked</c> = at least <c>n</c> hits (default 1)
        /// and not broken, <c>broken</c>.
        /// </summary>
        private bool WindowHolds(EntityRepository world, CheckCue c)
        {
            if (c.Building is not { } key || !world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()) return false;
            var terrain = world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>();
            if (terrain == null) return false;
            int building = Fdp.Toolkit.Terrain.BuildingDamages.IndexOfKey(terrain, key);
            if (building < 0) return false;
            var windows = WindowsNow(world);
            string state = c.Spec.Is is JsonValue sv ? sv.ToString() : "";
            int hits = c.Spec.N;
            float within = c.Spec.Within;
            var panes = terrain.PaneLeaves;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < panes.Count; i++)
            {
                if (panes[i].Building != building) continue;
                var (panel, opening) = terrain.Panes[i];
                if (c.Spec.Point is { } p)
                {
                    float d = System.Numerics.Vector2.Distance((panes[i].Min + panes[i].Max) * 0.5f, new System.Numerics.Vector2(p.X, p.Y));
                    if (d <= within && d < bestD) { bestD = d; best = i; }
                    continue;
                }
                if (WindowIs(windows, panel, opening, state, hits)) return true;
            }
            return best >= 0 && WindowIs(windows, terrain.Panes[best].Panel, terrain.Panes[best].Opening, state, hits);
        }

        // the windows read from the components THIS sample (a judge, never per frame of the sim — the cached view table is the sim's)
        private static Fdp.Toolkit.Terrain.WindowTable? WindowsNow(EntityRepository world)
            => world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.WindowDamage>() ? Fdp.Toolkit.Terrain.WindowTable.Fresh(world) : null;

        private static bool WindowIs(Fdp.Toolkit.Terrain.WindowTable? windows, int panel, int opening, string state, int hits)
        {
            Fdp.Toolkit.Terrain.WindowEntry e = default;
            bool known = windows != null && windows.TryGet(panel, opening, out e);
            return state switch
            {
                "intact" => !known,
                "cracked" => known && !e.Broken && e.Hits >= hits,
                "broken" => known && e.Broken,
                _ => false,
            };
        }

        /// <summary>
        /// ⭐ CE-10002 (air combat RV-15, R-10012) — <c>far</c> / <c>close</c>: some unit <see cref="AnchorSpec.Who"/> names is more than (<c>far</c>) /
        /// within (<c>close</c>) <see cref="CheckCue.M"/> of the unit <see cref="CheckCue.From"/> names (3-D; the nearest of them) or of
        /// <see cref="AnchorSpec.Point"/> (horizontal). A dead or absent unit holds nothing. ⭐ CE-9009 — through <see cref="PositionOf"/>, so it holds in the
        /// cluster tier too (⛔ before, it read only a live repository and never held there).
        /// </summary>
        private bool DistanceHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who) return false;
            bool far = c.On == "far";
            foreach (var id in UnitsNamed(who))
            {
                if (c.Fighting && !InTheFight(id)) continue;   // ⭐ CE-2466 — a dead man cannot walk back in: only the men still in the fight
                if (PositionOf(id) is not { } at) continue;
                float d = float.MaxValue;
                if (c.From != null)
                {
                    foreach (var fid in UnitsNamed(c.From))
                        if (fid != id && PositionOf(fid) is { } f) d = MathF.Min(d, System.Numerics.Vector3.Distance(at, f));
                    if (d == float.MaxValue) continue;
                }
                else if (c.Spec.Point is { } p) d = System.Numerics.Vector2.Distance(new System.Numerics.Vector2(at.X, at.Y), p);
                else continue;
                if (far ? d > c.M : d <= c.M) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-9009</c> (docs/DESIGN_Scenario_Regression.md §3; the convoy's "no cutting in", R-4277) — <c>between</c>: some unit <see cref="AnchorSpec.Who"/>
        /// names lies in the CORRIDOR between the unit <see cref="CheckCue.From"/> names and the unit <see cref="CheckCue.To"/> names: strictly between them
        /// along the line that joins them (its projection inside the segment, the two ends' own bodies excluded by <see cref="CheckCue.Margin"/> metres at
        /// each end) and within <see cref="CheckCue.M"/> metres of that line (horizontal). A car in the next lane beside the column is OUTSIDE a corridor of
        /// half a lane; a car in the gap is inside. The same reading in both tiers (<see cref="PositionOf"/>).
        /// </summary>
        private bool BetweenHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who || c.From is null || c.To is null) return false;
            foreach (var aid in UnitsNamed(c.From))
            {
                if (PositionOf(aid) is not { } a3) continue;
                foreach (var bid in UnitsNamed(c.To))
                {
                    if (bid == aid || PositionOf(bid) is not { } b3) continue;
                    var a = new System.Numerics.Vector2(a3.X, a3.Y); var b = new System.Numerics.Vector2(b3.X, b3.Y);
                    var ab = b - a; float len = ab.Length();
                    if (len <= 2f * c.Margin) continue;   // the two ends touch: no gap to cut into
                    var dir = ab / len;
                    foreach (var id in UnitsNamed(who))
                    {
                        if (id == aid || id == bid || PositionOf(id) is not { } p3) continue;
                        var ap = new System.Numerics.Vector2(p3.X, p3.Y) - a;
                        float along = System.Numerics.Vector2.Dot(ap, dir);
                        if (along <= c.Margin || along >= len - c.Margin) continue;
                        float side = MathF.Abs(ap.X * dir.Y - ap.Y * dir.X);
                        if (side <= c.M) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-4672</c> (the street fight, R-4277) — <c>cover</c>: for the LIVE units <see cref="AnchorSpec.Who"/> names, <c>in</c> holds for a unit with a point of the
        /// terrain's cover database (the world's <see cref="Fdp.Toolkit.Spatial.Eqs.ICoverProvider"/> — what the EQS cover search reads) within <see cref="CheckCue.M"/>
        /// (horizontal, within 2 m in height); <c>open</c> for a unit with none; <see cref="CheckCue.Quantifier"/> counts them (<c>any</c> · <c>all</c> · <c>atLeast:n</c>).
        /// ⚠ The editor tier only: the cluster tier reads entities over HTTP and has no cover database — the cue reads false there.
        /// </summary>
        private bool CoverHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who || _w.World is not { } world || !world.HasSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>()
                || world.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>() is not { } cover) return false;
            bool wantOpen = c.Spec.Is?.ToString() == "open";
            Span<Fdp.Toolkit.Spatial.Eqs.CoverPoint> found = stackalloc Fdp.Toolkit.Spatial.Eqs.CoverPoint[32];
            int units = 0, held = 0;
            foreach (var id in UnitsNamed(who))
            {
                if (HealthOf(id) is not > 0f || PositionOf(id) is not { } at) continue;
                if (c.Fighting && !InTheFight(id)) continue;   // ⭐ CE-2455
                units++;
                bool near = NearCover(world, cover, at, c.M, found);
                // ⭐ CE-8316 — "upright": a man lying prone uses the ground (never "standing in the open")
                if (c.Upright && Components(id)?["StanceStatus"]?["CurrentStance"]?.ToString() == "Prone") near = true;
                if (Lasted(c, id, near != wantOpen)) held++;   // ⭐ CE-2455 — "for": this man, that long
            }
            return Quantified(c.Quantifier, units, held);
        }

        /// <summary>⭐ <c>CE-4672</c> / <c>CE-2456</c> — the ONE "is a cover point within <paramref name="m"/> of <paramref name="at"/>" of the judge (the <c>cover</c> and
        /// <c>exposed</c> cues): the terrain's database (within 2 m in height), and a STANDING vehicle's sides read live as the AI's cover search reads them
        /// (<c>VehicleCover.StandingPoints</c>, CE-3142 — never in the database. ⛔ Before: a man crouched behind a parked car read "in the open").</summary>
        private bool NearCover(EntityRepository world, Fdp.Toolkit.Spatial.Eqs.ICoverProvider cover, System.Numerics.Vector3 at, float m, Span<Fdp.Toolkit.Spatial.Eqs.CoverPoint> found)
        {
            int n = cover.GetCoverPointsInRadius(new System.Numerics.Vector2(at.X, at.Y), m, found);
            for (int i = 0; i < n; i++) if (MathF.Abs(found[i].PositionZ - at.Z) <= 2f) return true;
            foreach (var vp in VehiclePoints(world))
                if (System.Numerics.Vector2.Distance(new System.Numerics.Vector2(vp.PositionX, vp.PositionY), new System.Numerics.Vector2(at.X, at.Y)) <= m
                    && MathF.Abs(vp.PositionZ - at.Z) <= 2f) return true;
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-2455</c> (the urban fight suite, R-4277) — <c>still</c>: for the LIVE units <see cref="AnchorSpec.Who"/> names, a unit holds once he has stayed within
        /// <see cref="CheckCue.M"/> (horizontal) of where he settled for <see cref="CheckCue.For"/> s — he settles again wherever he ends up when he moves further.
        /// <see cref="CheckCue.Quantifier"/> counts them. 📌 With <c>fighting</c> and a window after the first contact: "a man in the fight stood still 20 s".
        /// Both tiers (<see cref="PositionOf"/>).
        /// </summary>
        private bool StillHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who) return false;
            int units = 0, held = 0;
            foreach (var id in UnitsNamed(who))
            {
                if (HealthOf(id) is not > 0f || PositionOf(id) is not { } p) continue;
                if (c.Fighting && !InTheFight(id)) continue;
                units++;
                var at = new System.Numerics.Vector2(p.X, p.Y);
                // first seen ⇒ he settles here; moved more than m ⇒ he settles where he is now and his time starts again (a break this sample)
                bool settled = !_settledAt.TryGetValue((c, id), out var anchor) || System.Numerics.Vector2.Distance(anchor, at) <= c.M;
                if (!_settledAt.ContainsKey((c, id)) || !settled) _settledAt[(c, id)] = at;
                if (Lasted(c, id, settled)) held++;
            }
            return Quantified(c.Quantifier, units, held);
        }

        /// <summary>
        /// ⭐ <c>CE-2456</c> (the urban fight suite, R-4277) — <c>exposed</c>: for the LIVE units <see cref="AnchorSpec.Who"/> names, a unit holds when some live
        /// enemy <see cref="AnchorSpec.By"/> names, still in the fight, SEES him now — by the AI's own rule (<c>SightNow.Sees</c>, the one the weapon brain fires
        /// on). <c>upright</c>: a man lying prone is not counted. <see cref="CheckCue.Quantifier"/> counts them; <c>for</c> per unit. 📐 Why not <c>cover</c>: in a town
        /// a man inside a building or behind one is safe with no cover point near him (uf-car-shootout) — exposure is the doctrine's sin, not distance
        /// from a marker. ⚠ The editor tier only (it reads the observers' live sensor tracks).
        /// </summary>
        private bool ExposedHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who || c.Spec.By is not { } by || _w.World is not { } world) return false;
            var eyes = new List<Entity>();
            foreach (var oid in UnitsNamed(by))
            {
                if (!InTheFight(oid)) continue;
                var o = new Fdp.Toolkit.Replication.EntityRef(oid).Resolve(world);
                if (!o.IsNull && world.IsAlive(o)) eyes.Add(o);
            }
            // ⭐ CE-2459 — with m: only a man with NO cover within m counts (seen while firing over a car is a firing position, not the sin)
            var cover = c.M > 0f && world.HasSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>() ? world.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>() : null;
            Span<Fdp.Toolkit.Spatial.Eqs.CoverPoint> found = stackalloc Fdp.Toolkit.Spatial.Eqs.CoverPoint[32];
            int units = 0, held = 0;
            foreach (var id in UnitsNamed(who))
            {
                if (HealthOf(id) is not > 0f) continue;
                if (c.Fighting && !InTheFight(id)) continue;
                var me = new Fdp.Toolkit.Replication.EntityRef(id).Resolve(world);
                if (me.IsNull || !world.IsAlive(me)) continue;
                units++;
                bool prone = Components(id)?["StanceStatus"]?["CurrentStance"]?.ToString() == "Prone";
                bool seen = false;
                bool sheltered = cover != null && PositionOf(id) is { } at && NearCover(world, cover, at, c.M, found);
                if (!(c.Upright && prone) && !sheltered)
                    foreach (var o in eyes) if (Fdp.Toolkit.Perception.SightNow.Sees(world, o, me)) { seen = true; break; }
                if (Lasted(c, id, seen)) held++;
            }
            return Quantified(c.Quantifier, units, held);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2476</c> (Sensing §8.6e) — <c>perceives</c>: for each unit <see cref="AnchorSpec.Who"/> names (with <c>squad</c>, each commander's pool), its
        /// beliefs scored against the live enemies <see cref="CheckCue.Truth"/> names (<see cref="PerceptionScore"/>); the unit holds when the chosen
        /// <see cref="CheckCue.Metric"/> compares by <c>op</c> / <c>value</c>. <see cref="CheckCue.Quantifier"/> counts them; <c>for</c> per unit.
        /// ⚠ The editor tier only (the live memory: the cluster dump hides heard contacts).
        /// </summary>
        private bool PerceivesHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who || c.Truth is not { } truthName || c.Metric is not { } metric || _w.World is not { } world) return false;
            var truth = new List<(Entity, System.Numerics.Vector3)>();
            foreach (var tid in UnitsNamed(truthName))
            {
                if (!InTheFight(tid)) continue;
                var te = new Fdp.Toolkit.Replication.EntityRef(tid).Resolve(world);
                if (te.IsNull || !world.IsAlive(te) || !world.HasComponent<SimTransform>(te)) continue;
                truth.Add((te, world.GetComponent<SimTransform>(te).Position));
            }
            float m = c.M > 0f ? c.M : c.Squad ? 10f : 5f;
            int want = c.Value is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
            int units = 0, held = 0;
            foreach (var id in UnitsNamed(who))
            {
                var e = new Fdp.Toolkit.Replication.EntityRef(id).Resolve(world);
                if (e.IsNull || !world.IsAlive(e)) continue;
                if (!c.Squad && HealthOf(id) is not > 0f) continue;
                units++;
                int got = PerceptionScore.Of(world, e, truth, c.Squad, m, c.Through).Of(metric);
                bool ok = c.Op switch
                {
                    "==" => got == want, "!=" => got != want, "<" => got < want, "<=" => got <= want, ">" => got > want, ">=" => got >= want,
                    _ => false,
                };
                if (Lasted(c, id, ok)) held++;
            }
            return Quantified(c.Quantifier, units, held);
        }

        /// <summary>⭐ <c>CE-2455</c> — <paramref name="holds"/> for <paramref name="unit"/> (−1 = the cue itself) at this sample, and it has held at every sample
        /// for <see cref="CheckCue.For"/> s; a break starts it again. 0 ⇒ at once.</summary>
        private bool Lasted(CheckCue c, int unit, bool holds)
        {
            var key = (c, unit);
            if (!holds) { _heldSince.Remove(key); return false; }
            if (!_heldSince.TryGetValue(key, out var since)) _heldSince[key] = since = _rel;
            bool lasted = _rel - since >= c.For - 1e-9;
            if (lasted && unit >= 0) _holder[c] = unit;   // ⭐ CE-2458 — who
            return lasted;
        }

        /// <summary>⭐ <c>CE-2455</c> — the unit is still in the fight: alive and not incapacitated — <c>Injury.IsInTheFight</c>
        /// on a live repository; on dumped components (the cluster tier) the same rule read from <c>Health</c> and <c>InjuryThresholds</c> (the RT-5 precedent).</summary>
        private bool InTheFight(int id)
        {
            if (_w.World is { } world)
            {
                var e = new Fdp.Toolkit.Replication.EntityRef(id).Resolve(world);
                if (!e.IsNull && world.IsAlive(e)) return Fdp.Toolkit.Combat.Injury.IsInTheFight(world, e);
            }
            var comps = Components(id);
            if (comps?["Health"] is not JsonObject hp || Num(hp["Current"]) is not { } cur) return true;   // no health: nothing takes it out of the fight
            if (cur <= 0) return false;
            double max = Num(hp["Max"]) ?? 0;
            double below = Num(comps["InjuryThresholds"]?["IncapacitatedBelow"]) ?? 0;
            return max <= 0 || cur / max >= below;
        }

        private List<Fdp.Toolkit.Spatial.Eqs.CoverPoint> VehiclePoints(EntityRepository world)
        {
            if (_vehiclePoints != null) return _vehiclePoints;
            _vehiclePoints = new List<Fdp.Toolkit.Spatial.Eqs.CoverPoint>();
            var terrain = world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
            Fdp.Toolkit.Spatial.Eqs.VehicleCover.StandingPoints(world, terrain, _vehiclePoints);
            return _vehiclePoints;
        }

        /// <summary>⭐ <c>CE-4672</c> — <c>bunched</c>: at least two LIVE units <see cref="AnchorSpec.Who"/> names, and EVERY one of them within <see cref="CheckCue.M"/> of
        /// their centre (horizontal) — a squad standing in a knot. Both tiers (<see cref="PositionOf"/>).</summary>
        private bool BunchedHolds(CheckCue c)
        {
            if (c.Spec.Who is not { } who) return false;
            var at = new List<System.Numerics.Vector2>();
            foreach (var id in UnitsNamed(who))
                if (HealthOf(id) is > 0f && (!c.Fighting || InTheFight(id)) && PositionOf(id) is { } p) at.Add(new System.Numerics.Vector2(p.X, p.Y));
            if (at.Count < Math.Max(2, c.Men)) return false;   // ⭐ CE-2466 — fewer than men is no knot
            var centre = System.Numerics.Vector2.Zero;
            foreach (var p in at) centre += p;
            centre /= at.Count;
            foreach (var p in at) if (System.Numerics.Vector2.Distance(p, centre) > c.M) return false;
            return true;
        }

        private static bool Quantified(string quantifier, int units, int held) => quantifier switch
        {
            "any" => held > 0,
            "all" => units > 0 && held == units,
            _ => held >= int.Parse(quantifier[8..], CultureInfo.InvariantCulture),
        };

        /// <summary>A unit's current health from its dump; null when it has none (a squad, a vehicle without one).</summary>
        private float? HealthOf(int id) => Components(id)?["Health"] is JsonObject hp && Num(hp["Current"]) is { } h ? (float)h : null;

        /// <summary>⭐ CE-9009 — a unit's position in either tier: the live repository's <c>SimTransform</c>, else its dumped components (the cluster tier).</summary>
        private System.Numerics.Vector3? PositionOf(int id)
        {
            if (_w.World is { } world)
            {
                var e = new Fdp.Toolkit.Replication.EntityRef(id).Resolve(world);
                if (!e.IsNull && world.HasComponent<SimTransform>(e)) return world.GetComponentRO<SimTransform>(e).Position;
                // ⭐ CE-4672 — a unit the repository does not hold (a rail's dumped unit beside a real cover database) is read from its dump below
            }
            return Components(id) is { } comps && At(comps, "SimTransform.Position") is JsonArray pos && pos.Count >= 3
                ? new System.Numerics.Vector3((float)(Num(pos[0]) ?? 0), (float)(Num(pos[1]) ?? 0), (float)(Num(pos[2]) ?? 0)) : null;
        }

        private double Rel(JsonNode? firedAt) => (Num(firedAt) ?? _t0 + _rel) - _t0;

        /// <summary>⭐ RT-5 (R-3094) — a STATE cue on one unit's DUMPED components (enum fields by name, a vector as [x, y, z]): the comparison is
        /// AnchorBook's own, value for value; only the reading differs.</summary>
        private bool StateHolds(AnchorSpec a, JsonObject comps)
        {
            switch (a.On)
            {
                case "near":
                    if (a.Point is not { } p || At(comps, "SimTransform.Position") is not JsonArray pos || pos.Count < 2) return false;
                    return System.Numerics.Vector2.Distance(new System.Numerics.Vector2((float)(Num(pos[0]) ?? 0), (float)(Num(pos[1]) ?? 0)), p) <= a.Within;
                case "task":
                    if (comps["BehaviorState"] is not JsonObject bs) return false;
                    int hash = (int)(Num(bs["ActiveBehaviorHash"]) ?? 0);
                    // the hash's name: the world's own map, else the start record of the behaviour now running (the dump carries no registry)
                    string? started = comps["BehaviorStartRecord"]?["BehaviorName"]?.ToString();
                    return AnchorBook.TaskHolds(a, hash, h => _w.BehaviourName(h) ?? started);
                case "side":
                    return Enum.TryParse<Fdp.Core.ForceId>(At(comps, "EntityInfo.ForceId")?.ToString(), true, out var side) && AnchorBook.SideHolds(a, side);
                case "health":
                    if (comps["Health"] is not JsonObject hp) return false;
                    return AnchorBook.HealthHolds(a, (float)(Num(hp["Current"]) ?? 0), (float)(Num(hp["Max"]) ?? 0));
                case "embark":
                    return AnchorBook.EmbarkHolds(a, comps["IsEmbarkedTag"] is JsonObject);
                case "gesture":
                    return AnchorBook.GestureHolds(a, Enum.TryParse<Fdp.Toolkit.Combat.GestureKind>(At(comps, "Demeanour.Kind")?.ToString(), true, out var g) ? g : Fdp.Toolkit.Combat.GestureKind.None);
                default:
                    return false;
            }
        }

        private JsonObject? Components(int id)
        {
            if (!_comps.TryGetValue(id, out var c)) _comps[id] = c = _w.Components(id);
            return c;
        }

        private IEnumerable<int> UnitsNamed(string who)
        {
            // ⭐ R-8335 (agg-behaviors, cross-lane) — a NETWORK ID names the unit too, as the near / task cues already read it (WorldUnits.Resolve): an
            //   UNNAMED entity (a stochastic unit's split children, which carry no name) is otherwise out of every field cue's reach
            if (who.Length > 0 && char.IsAsciiDigit(who[0]) && int.TryParse(who, NumberStyles.None, CultureInfo.InvariantCulture, out int networkId))
            {
                yield return networkId;
                yield break;
            }
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
            string? name = Num(unit) is { } id ? (_nameOf.TryGetValue((int)id, out var n) ? n : null) : unit.ToString();
            if (name == null) return false;
            return who.StartsWith('*') ? name.Contains(who[1..], StringComparison.Ordinal) : string.Equals(name, who, StringComparison.Ordinal);
        }

        private static bool KindIs(string? kind, JsonNode want)
            => kind != null && (want is JsonArray many ? many.Any(x => string.Equals(x?.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                                                       : string.Equals(want.ToString(), kind, StringComparison.OrdinalIgnoreCase));

        /// <summary>⭐ CE-4676 — the reports the inbox saw since the last sample, oldest first — through the verify world (the debug service's
        /// <c>GET /squad/reports</c>, which reads the BRAIN world; ⛔ not <see cref="IVerifyWorld.World"/>, which in the threaded editor is the UI's
        /// mirror and has no report log).</summary>
        private void ReadReports()
        {
            var fresh = new List<JsonObject>();
            foreach (var r in _w.Reports().OfType<JsonObject>())
            {
                long serial = (long)(Num(r["serial"]) ?? -1);
                if (serial <= _reportSeen) continue;
                var row = new JsonObject
                {
                    ["serial"] = serial, ["t"] = Math.Round((Num(r["seenAt"]) ?? 0d) - _t0, 4), ["kind"] = r["kind"]?.ToString(),
                    ["from"] = r["from"]?.DeepClone(), ["to"] = r["to"]?.DeepClone(),
                };
                foreach (var k in new[] { "from", "to" })
                    if (Num(row[k]) is { } id && _nameOf.TryGetValue((int)id, out var n)) row[k] = n;
                fresh.Add(row);
            }
            foreach (var row in fresh.OrderBy(r => Num(r["serial"])))
            {
                _reports.Add(row);
                _reportSeen = Math.Max(_reportSeen, (long)(Num(row["serial"]) ?? -1));
            }
        }

        private void ReadRows()
        {
            ReadReports();
            foreach (var r in _w.Shots().OfType<JsonObject>())
            {
                long seq = (long)(Num(r["seq"]) ?? -1);
                if (seq <= _shotFloor) continue;
                // ⚠ a round is logged when FIRED and ends later in place: keep its latest row
                var row = (JsonObject)r.DeepClone();
                row.Remove("crossings");
                foreach (var k in new[] { "shooter", "target", "hit" })
                    if (Num(row[k]) is { } id && _nameOf.TryGetValue((int)id, out var n)) row[k] = n;
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
                    if (Num(row[k]) is { } id && _nameOf.TryGetValue((int)id, out var n)) row[k] = n;
                if (row["effects"] is JsonArray fx)
                    foreach (var x in fx.OfType<JsonObject>())
                        if (Num(x["entity"]) is { } eid && _nameOf.TryGetValue((int)eid, out var en)) x["entity"] = en;
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
        /// <summary>⭐ R-8320 — a <c>field</c> cue's test on one unit: a plain path compares the value there; a path with a <c>*</c> part (<c>UnitSlotTable.Slots.*.Health</c>) holds when at
        /// least <paramref name="elements"/> elements of that array satisfy the comparison — among those a <c>where</c> row passes (the unused slots of a fixed array are all zeros: <c>"where": {"CompositeSlot": [">", 0]}</c>) (the ONE wildcard: the first <c>*</c> walks an array, the rest of the path is read per element).</summary>
        public static bool FieldHolds(JsonNode? comps, string path, string op, JsonNode? want, int elements, IReadOnlyList<(string Field, string Op, JsonNode? Value)>? where = null)
        {
            int star = path.IndexOf(".*.", StringComparison.Ordinal);
            if (star < 0) return Compare(At(comps, path), op, want);
            if (At(comps, path[..star]) is not JsonArray arr) return false;
            string rest = path[(star + 3)..];
            int n = 0;
            for (int i = 0; i < arr.Count; i++)
                if ((where == null || where.Count == 0 || (arr[i] is JsonObject elt && WhereOk(elt, where))) && Compare(At(arr[i], rest), op, want) && ++n >= elements) return true;
            return false;
        }

        public static JsonNode? At(JsonNode? node, string path) => Fdp.Toolkit.Scenario.ComponentJsonPath.Get(node, path);

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

        /// <summary>⭐ <c>CE-3334</c> — the version of the by-meaning rules a golden was captured with (<c>"meaning"</c> in the file): handles by
        /// meaning. (2 also wrote per-run creation request ids by meaning; that component is gone — CE-4266 — so nothing per-run is left.)</summary>
        public const int MeaningVersion = 2;

        private readonly IVerifyWorld _w;
        private readonly bool _full;
        private readonly JsonArray _snapshots = new();
        private readonly string[] _visible;

        /// <param name="full">Keep each component in full, not its hash (<c>golden.full</c> — to run a difference down).</param>
        /// <param name="checkPaths">⭐ RT-3 — the paths the scenario's checks read: kept in each snapshot's VISIBLE state beside
        /// <see cref="VisibleState.Paths"/>.</param>
        public ScenarioGolden(IVerifyWorld world, bool full = false, IEnumerable<string>? checkPaths = null)
        {
            _w = world ?? throw new ArgumentNullException(nameof(world));
            _full = full;
            _visible = VisibleState.Paths.Concat(checkPaths ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
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
                    ["visible"] = Visible(comps),
                    ["components"] = hashes,
                };
            }
            _snapshots.Add(new JsonObject { ["t"] = Math.Round(rel, 4), ["units"] = units });
            LastTime = rel;
        }

        /// <summary>⭐ RT-3 — the unit's VISIBLE state: each of <see cref="VisibleState.Paths"/> and the checks' paths it carries (handles by
        /// meaning, numbers to 0.001).</summary>
        private JsonObject Visible(JsonObject comps)
        {
            var o = new JsonObject();
            foreach (var path in _visible)
                if (ConditionBook.At(comps, path) is { } v) o[path] = RoundAll(ByMeaning(v.DeepClone()));
            return o;
        }

        private static JsonNode? RoundAll(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o: foreach (var k in o.Select(kv => kv.Key).ToList()) if (o[k] is JsonValue) o[k] = R(o[k]); else RoundAll(o[k]); return o;
                case JsonArray a: for (int i = 0; i < a.Count; i++) if (a[i] is JsonValue) a[i] = R(a[i]); else RoundAll(a[i]); return a;
                default: return R(n);
            }
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
        public static IReadOnlyList<string> Compare(JsonObject golden, JsonObject run) => CompareCore(WithoutVisible(golden), WithoutVisible(run));

        /// <summary>⭐ RT-3 — the FULL comparison leaves the visible map out: it repeats what the components already hold, and the replay harness
        /// (H1–H5, R1) compares by component, with its own carried-by-the-recording filter.</summary>
        private static JsonObject WithoutVisible(JsonObject g)
        {
            if ((g["snapshots"] as JsonArray)?.OfType<JsonObject>().Any(s => (s["units"] as JsonObject)?.Any(u => (u.Value as JsonObject)?.ContainsKey("visible") == true) == true) != true) return g;
            var c = (JsonObject)g.DeepClone();
            foreach (var s in c["snapshots"]!.AsArray().OfType<JsonObject>())
                foreach (var (_, u) in (s["units"] as JsonObject ?? new JsonObject())) (u as JsonObject)?.Remove("visible");
            return c;
        }

        private static IReadOnlyList<string> CompareCore(JsonObject golden, JsonObject run)
        {
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
        /// ⭐ RT-3 (R-4279, docs/DESIGN_Scenario_Regression.md §11) — the golden as a scenario's WARNING: what a user could SEE differ — the
        /// times of its anchors and expectations, its rounds and bursts, and per snapshot each unit's position, health and VISIBLE state
        /// (<see cref="VisibleState.Paths"/> + the fields its checks read). ⛔ Not internal blackboards, brain diagnostics or a component added
        /// since the capture: 📐 measured `2026-11-16`, every golden red on <c>farm-under-fire</c> / <c>ua-drone-shelter</c> was one of those
        /// while all 19 expectations passed. (<see cref="Compare"/> stays the FULL comparison the replay harness needs.)
        /// </summary>
        public static IReadOnlyList<string> CompareVisible(JsonObject golden, JsonObject run)
        {
            var lines = new List<string>(CompareCore(Seen(golden), Seen(run)));
            if ((golden["snapshots"] as JsonArray)?.OfType<JsonObject>().Any(s => (s["units"] as JsonObject)?.Select(u => u.Value).OfType<JsonObject>().Any(u => u["visible"] is null) == true) == true)
                lines.Insert(0, "the golden predates the visible-state comparison (RT-3) — re-capture it once: --verify <scenario> --verify-capture");
            return lines;
        }

        /// <summary>A golden reduced to what <see cref="CompareVisible"/> compares.</summary>
        private static JsonObject Seen(JsonObject g)
        {
            var o = new JsonObject();
            foreach (var key in new[] { "times", "shots", "blasts" }) o[key] = g[key]?.DeepClone();
            var snaps = new JsonArray();
            foreach (var s in (g["snapshots"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var units = new JsonObject();
                foreach (var (name, u) in (s["units"] as JsonObject ?? new JsonObject()))
                    if (u is JsonObject uo)
                        units[name] = new JsonObject { ["position"] = uo["position"]?.DeepClone(), ["health"] = uo["health"]?.DeepClone(), ["visible"] = uo["visible"]?.DeepClone() ?? new JsonObject() };
                snaps.Add(new JsonObject { ["t"] = s["t"]?.DeepClone(), ["units"] = units });
            }
            o["snapshots"] = snaps;
            return o;
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
                        u.Remove("visible");   // ⭐ RT-3 — the harness compares by component (the visible map repeats them)
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

        /// <summary>⭐ CE-4045 H2 (docs/DESIGN_Scenario_Regression.md §10, RH-6) — the replay SOUGHT again in a shuffled, seeded order (backward seeks
        /// included), each moment compared with what the in-order playback (H1b) showed there; null when not asked.</summary>
        public LookBackResult? Seek { get; set; }

        /// <summary>⭐ CE-4045 H3 (docs/DESIGN_Scenario_Regression.md §10-7, RH-7) — a LIVE run BRANCHED from the replay at <see cref="BranchAt"/>
        /// and run on, against the straight live run from there (R1's comparison, on what the recording carries); null when not asked.</summary>
        public LookBackResult? Branch { get; set; }
        /// <summary>The run time H3 branched at (a live snapshot's).</summary>
        public double? BranchAt { get; set; }
        /// <summary>📐 The branch's own clock when it came live at <see cref="BranchAt"/> — CE-4065 while it is not the same sim time.</summary>
        public double? BranchClock { get; set; }

        /// <summary>⭐ CE-4061 — the key a director report writes this under.</summary>
        public string Kind => "verify";

        /// <summary>⭐ RT-3 (R-4279) — what the run reports but does NOT fail on: a golden that differs (in VISIBLE state) or is missing.</summary>
        public IReadOnlyList<string> Warnings => Golden is "different" or "missing" ? new[] { $"golden {Golden} — re-capture with --verify-capture if the change is intended" } : Array.Empty<string>();

        public bool Passed => Problems.Count == 0 && Verdicts.All(v => v.Ok) && Golden is not "not-captured" && Replay is (null or "same")
                              && Review is (null or { Verdict: "same" }) && ReturnDiff.Count == 0 && Playback is (null or { Verdict: "same" }) && Seek is (null or { Verdict: "same" })
                              && Branch is (null or { Verdict: "same" });

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
            ["warnings"] = new JsonArray(Warnings.Select(x => (JsonNode?)x).ToArray()),
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
            ["seek"] = Seek?.ToJson(),
            ["branch"] = Branch?.ToJson(),
            ["branchAt"] = BranchAt,
            ["branchClock"] = BranchClock is { } c ? Math.Round(c, 4) : null,
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
            _run = _every is null ? null : new ScenarioGolden(world, checks.GoldenFull || snapshotEvery != null, checks.Paths());
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
        /// <para>⭐ A name the checks declare <see cref="ScenarioChecks.Later"/> (inserted during the run) — or a <c>*part</c> one of them contains — is not
        /// asked of the start's cast.</para>
        public static IReadOnlyList<string> MissingNames(ScenarioChecks checks, IShotWorld units) =>
            checks.Names().Where(n => !checks.Later.Any(l => n.StartsWith('*') ? l.Contains(n[1..], StringComparison.Ordinal) : string.Equals(l, n, StringComparison.Ordinal)))
                  .Where(n => n.StartsWith('*') ? !units.Names.Any(x => x.Contains(n[1..], StringComparison.Ordinal)) : units.Resolve(n) is null)
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
                {
                    string? why = a.Order is { } order ? _w.Order(id, order)
                                : a.Resume ? _w.ResumePlan(id)
                                : a.Revive ? _w.Revive(id)
                                : _w.SetComponent(id, a.Component, a.Patch);
                    string what = a.Order != null ? "order" : a.Resume ? "resume" : a.Revive ? "revive" : a.Component;
                    if (why != null) ActionProblems.Add($"do at {rel:0.###} s: {a.Who}.{what}: {why}");
                }
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
                var diff = ScenarioGolden.CompareVisible(_golden, json);   // ⭐ RT-3 — visible state; a difference is a WARNING
                r.Golden = diff.Count == 0 ? "same" : "different";
                r.GoldenDiff.AddRange(diff);
            }
            return r;
        }
    }

    /// <summary>
    /// ⭐ RT-3 (R-4279) — the state a scenario's golden compares beside each unit's position and health: what a user SEES of a unit (its side,
    /// whether it is aboard, its movement phase, its mode as a civilian / an animal / a flock, its weapon posture, what it shows). ⭐ A
    /// scenario's checks add the fields THEY read. ⛔ Never internal state (blackboards, brain diagnostics, start records).
    /// </summary>
    public static class VisibleState
    {
        public static readonly IReadOnlyList<string> Paths = new[]
        {
            "EntityInfo.ForceId", "IsEmbarkedTag", "NavigationStatus.Phase", "WeaponPosture", "CivilianState.Mode", "Demeanour", "Custody",
            "AnimalState", "FlockState",
        };
    }
}
