using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Editor.Tests.Director;

/// <summary>
/// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §3, §4) — a scenario's checks as data: the grammar names every mistake; an expectation
/// holds once in its window, a <c>never</c> is broken by the first sample that sees it, an <c>atEnd</c> is judged on the last; a shot holds at its
/// own firing time and filters on its row; the golden names the FIRST snapshot and unit that differ.
/// </summary>
public sealed class ScenarioChecksTests
{
    private static ScenarioChecks Parse(string json, out List<string> problems)
    {
        problems = new List<string>();
        return ChecksJson.Parse((JsonObject)JsonNode.Parse(json)!, "s", problems);
    }

    /// <summary>⭐ Every mistake is named (none guessed): an unknown key, an unknown cue, a field cue with no path, a time naming nothing.</summary>
    [Fact]
    public void CE4042_TheGrammar_NamesEveryMistake()
    {
        Parse("""
            { "runFor": 30, "golden": {"every": 5}, "anchors": {"go": {"on": "task", "who": "A", "is": "Move"}},
              "do": [{"name": "hurt", "at": "go+2", "set": {"who": "A", "component": "Health", "patch": {"Current": 40}}}],
              "expect": [{"name": "fires", "on": "shot", "by": "A", "at": "B", "where": {"penetration": [">", 400]}, "after": "hurt", "before": 20},
                         {"name": "no friendly fire", "never": {"on": "shot", "by": "*Blue", "hit": "*Blue"}},
                         {"name": "alive", "on": "field", "who": "A", "path": "Health.Current", "op": ">", "value": 0, "atEnd": true}] }
            """, out var ok);
        Assert.Empty(ok);

        var bad = Parse("""
            { "runFor": 30, "colour": 1,
              "expect": [{"name": "x", "on": "teleport"},
                         {"name": "y", "on": "field", "who": "A"},
                         {"name": "z", "on": "task", "who": "A", "is": "Move", "after": "nowhere"},
                         {"name": "w", "on": "health", "who": "A", "below": 0.5, "where": {"a": 1}}] }
            """, out var problems);
        Assert.Contains(problems, p => p.Contains("unknown key 'colour'"));
        Assert.Contains(problems, p => p.Contains("cue 'teleport'"));
        Assert.Contains(problems, p => p.Contains("needs path"));
        Assert.Contains(problems, p => p.Contains("'nowhere'"));
        Assert.Contains(problems, p => p.Contains("where filters a shot or a blast"));
    }

    /// <summary>⭐ showcase scenarios — <c>"later"</c> names the units a trigger inserts DURING the run (a QRF macro): the start-of-run cast check does not
    /// refuse them (nor a <c>*part</c> one of them carries), while any other missing name is still refused; a malformed list is named.
    /// 🔴 Red-proof: drop the Later filter in <c>MissingNames</c> ⇒ "Red QRF" is reported missing.</summary>
    [Fact]
    public void Later_UnitsInsertedDuringTheRun_PassTheStartCastCheck()
    {
        var checks = Parse("""
            { "runFor": 10, "later": ["Red QRF", "Red QRF Squad 1 Leader"],
              "expect": [{"name": "the QRF attacks", "on": "task", "who": "Red QRF", "is": "PlatoonAttack"},
                         {"name": "its men", "on": "health", "who": "*QRF Squad", "below": 0.5},
                         {"name": "a typo", "on": "task", "who": "Blue Platon", "is": "SquadMarch"}] }
            """, out var problems);
        Assert.Empty(problems);
        Assert.Equal(new[] { "Red QRF", "Red QRF Squad 1 Leader" }, checks.Later);
        var units = new Units(); units.Ids["Blue Platoon"] = 1;
        var missing = ScenarioVerification.MissingNames(checks, units);
        Assert.Equal(new[] { "no unit named 'Blue Platon' in s" }, missing);

        Parse("""{ "runFor": 10, "later": "Red QRF", "expect": [{"name": "x", "on": "task", "who": "A", "is": "B"}] }""", out var bad);
        Assert.Contains(bad, p => p.Contains("later must be a list"));
    }

    /// <summary>
    /// ⭐ <c>if</c>: a row about an event the run need not produce is judged only when that event happened — else <c>skipped</c> (ok, no time);
    /// when it happened the row is judged as usual. 🔴 Red-proof: drop the <c>If</c> term in <c>Verdicts</c> ⇒ the skipped row FAILS.
    /// </summary>
    [Fact]
    public void If_ARowAboutAnEventThatDidNotHappen_IsSkipped_AndJudgedWhenItDid()
    {
        const string json = """
            { "runFor": 10, "anchors": {"harm": {"on": "field", "who": "Villager", "path": "Health.Current", "op": "<", "value": 100}},
              "expect": [{"name": "the tree fails", "on": "field", "who": "Tree", "path": "State", "op": "==", "value": "Failed", "after": "harm", "if": "harm"}] }
            """;
        var checks = Parse(json, out var problems);
        Assert.Empty(problems);
        Assert.Equal("harm", checks.Expect[0].If);

        var calm = new FakeWorld().Unit("Villager", 1, 100, 0).Unit("Tree", 2, 100, 0);
        var book = new ConditionBook(checks, calm, 0);
        book.Observe(0); book.Finish(10);
        var skipped = book.Verdicts().Single();
        Assert.True(skipped.Ok); Assert.Equal("skipped", skipped.Kind); Assert.Null(skipped.Time);

        var hurt = new FakeWorld().Unit("Villager", 1, 100, 0).Unit("Tree", 2, 100, 0);
        var book2 = new ConditionBook(checks, hurt, 0);
        book2.Observe(0);
        hurt.Comps[1] = new JsonObject { ["Health"] = new JsonObject { ["Current"] = 40 } };
        book2.Observe(3);
        book2.Finish(10);
        Assert.False(book2.Verdicts().Single().Ok);   // the harm happened and the tree never read Failed

        Parse(json.Replace("\"if\": \"harm\"", "\"if\": \"nope\""), out var bad);
        Assert.Contains(bad, p => p.Contains("if names 'nope'"));
    }

    // ── a world the book reads ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ RT-5 (R-3094) — a world with NO repository (the cluster tier reads a node over HTTP) judges the state cues on the unit's DUMPED components,
    /// through AnchorBook's own value comparisons: near (a position as [x, y, z]), embark, task (the hash, named by the start record), side,
    /// health. 🔴 Red-proof: the old <c>World is null ⇒ null</c> ⇒ every row fails.
    /// </summary>
    [Fact]
    public void RT5_StateCues_HoldOnDumpedComponents_WhenTheWorldHasNoRepository()
    {
        var checks = Parse("""
            {"runFor":5,"expect":[
              {"name":"near","on":"near","who":"Bike","point":[200,200],"within":20},
              {"name":"aboard","on":"embark","who":"Rider","is":true},
              {"name":"off","on":"embark","who":"Bike","is":false},
              {"name":"task","on":"task","who":"Bike","is":"DriveBy"},
              {"name":"side","on":"side","who":"Bike","is":"Hostile"},
              {"name":"health","on":"health","who":"Rider","below":0.5}]}
            """, out var problems);
        Assert.Empty(problems);
        var w = new FakeWorld();
        w.U.Ids["Bike"] = 1; w.U.Ids["Rider"] = 2;
        w.Comps[1] = new JsonObject
        {
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(195, 205, 0) },
            ["BehaviorState"] = new JsonObject { ["ActiveBehaviorHash"] = 420206978 },
            ["BehaviorStartRecord"] = new JsonObject { ["BehaviorName"] = "DriveBy" },
            ["EntityInfo"] = new JsonObject { ["ForceId"] = "Hostile" },
        };
        w.Comps[2] = new JsonObject
        {
            ["IsEmbarkedTag"] = new JsonObject { ["Vehicle"] = "[0, v1]", ["Seat"] = 0 },
            ["Health"] = new JsonObject { ["Current"] = 30, ["Max"] = 100 },
        };
        var book = new ConditionBook(checks, w, 0);
        book.Observe(1);
        book.Finish(5);
        foreach (var v in book.Verdicts()) Assert.True(v.Ok, v.Name + " should hold on the dumped components");
    }

    /// <summary>
    /// ⭐ <c>CE-9009</c> (the convoy's "no cutting in", R-4277) — <c>between</c>: a unit IN the corridor between two others holds (a car in the convoy's
    /// gap); a unit beside it — the next lane — does not, nor one past either end; it needs who, from, to and m. And <c>far</c> / <c>close</c> now hold on
    /// DUMPED positions too (the cluster tier) — before, they read only a live repository and never held there. 🔴 Red-proofs: <c>BetweenHolds</c> ignoring
    /// the lateral limit ⇒ the next-lane row holds ⇒ red; <c>DistanceHolds</c> back on a live repository only ⇒ the close row fails ⇒ red.
    /// </summary>
    [Fact]
    public void CE9009_Between_HoldsForAUnitInTheGap_NotBesideIt_AndCloseHoldsOnDumpedPositions()
    {
        var checks = Parse("""
            {"runFor":5,"expect":[
              {"name":"a car cut in","on":"between","who":"*SUMO ","from":"Escort 1","to":"Escort 2","m":1.6},
              {"name":"a car in the next lane is not in the gap","never":{"on":"between","who":"SUMO beside","from":"Escort 1","to":"Escort 2","m":1.6}},
              {"name":"a car past the column is not in the gap","never":{"on":"between","who":"SUMO ahead","from":"Escort 1","to":"Escort 2","m":1.6}},
              {"name":"close on dumped positions","on":"close","who":"Escort 2","from":"Escort 1","m":16}]}
            """, out var problems);
        Assert.Empty(problems);
        var w = new FakeWorld();
        void At(string name, int id, double x, double y)
        {
            w.U.Ids[name] = id;
            w.Comps[id] = new JsonObject { ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, y, 0) } };
        }
        At("Escort 1", 1, 100, 0); At("Escort 2", 2, 85, 0);   // a 15 m gap on y 0
        At("SUMO in gap", 3, 92.5, 0.4);                      // in the gap
        At("SUMO beside", 4, 92.5, 3.2);                      // the next lane
        At("SUMO ahead", 5, 120, 0);                          // past the lead
        var book = new ConditionBook(checks, w, 0);
        book.Observe(1);
        book.Finish(5);
        foreach (var v in book.Verdicts()) Assert.True(v.Ok, v.Name + " should hold");

        Parse("""{"runFor":5,"expect":[{"name":"x","on":"between","who":"*SUMO ","from":"Escort 1","m":1.6}]}""", out var noTo);
        Assert.Contains(noTo, p => p.Contains("needs from and to"));
        Parse("""{"runFor":5,"expect":[{"name":"x","on":"near","who":"Escort 1","point":[0,0],"to":"Escort 2"}]}""", out var stray);
        Assert.Contains(stray, p => p.Contains("belong to a between cue"));
    }

    private sealed class Units : IShotWorld
    {
        public readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);
        public int? Resolve(string who) => Ids.TryGetValue(who, out var id) ? id : null;
        public Vector3? PositionOf(int networkId) => null;
        public IReadOnlyCollection<string> Names => Ids.Keys;
    }

    private sealed class FakeWorld : IVerifyWorld
    {
        public readonly Units U = new();
        public readonly Dictionary<long, JsonObject> Comps = new();
        public readonly JsonArray ShotRows = new();
        public readonly JsonArray BlastRows = new();
        public EntityRepository? World { get; set; }
        public IShotWorld Units => U;
        public string? BehaviourName(int hash) => null;
        public JsonObject? Components(int id) => Comps.TryGetValue(id, out var c) ? c : null;
        public IReadOnlyList<(int Id, string Name)> Named() => U.Ids.Select(kv => (kv.Value, kv.Key)).Concat(Extra).ToList();
        /// <summary>Units that share a name with another (the name index keeps only one).</summary>
        public readonly List<(int Id, string Name)> Extra = new();
        public string? NameOfEntityIndex(int index) => U.Ids.FirstOrDefault(kv => kv.Value == index).Key;
        public JsonArray Shots() => (JsonArray)ShotRows.DeepClone();
        public JsonArray Blasts() => (JsonArray)BlastRows.DeepClone();
        public string? SetComponent(int networkId, string component, JsonObject patch) => null;

        public FakeWorld Unit(string name, int id, double health, double x)
        {
            U.Ids[name] = id;
            Comps[id] = new JsonObject
            {
                ["Health"] = new JsonObject { ["Current"] = health, ["Max"] = 100 },
                ["SimTransform"] = new JsonObject { ["Position"] = new JsonObject { ["X"] = x, ["Y"] = 0, ["Z"] = 0 } },
            };
            return this;
        }

        /// <summary>A shot row as <c>/combat/shots</c> writes it (newest first).</summary>
        public void Shot(long seq, double firedAt, int shooter, int? target, double penetration, long? hit = null)
            => ShotRows.Insert(0, new JsonObject
            {
                ["seq"] = seq, ["firedAt"] = firedAt, ["shooter"] = shooter, ["target"] = target, ["penetration"] = penetration,
                ["outcome"] = hit is null ? "Miss" : "Hit", ["hit"] = hit,
            });
    }

    /// <summary>
    /// ⭐⭐ The windows: a shot holds at its FIRING time (the t0 offset taken off), filtered by its row and counted (n); an expectation whose
    /// window opens on a beat waits for it; a never is broken by the first matching row; a field cue counts its units (atLeast); atEnd reads
    /// the last sample. 🔴 Red-proof: drop the <c>after</c> check in Observe ⇒ "the second round after the first" holds at the first.
    /// </summary>
    [Fact]
    public void CE4042_TheBook_HoldsAtTheEventsTime_InItsWindow()
    {
        var checks = Parse("""
            { "runFor": 10, "anchors": {"first": {"on": "shot", "by": "Tank"}},
              "expect": [{"name": "an APFSDS round at the T-72", "on": "shot", "by": "Tank", "at": "T-72", "where": {"penetration": [">", 400]}},
                         {"name": "the second round after the first", "on": "shot", "by": "Tank", "after": "first+0.5"},
                         {"name": "never hits a friend", "never": {"on": "shot", "by": "Tank", "hit": "*Friend"}},
                         {"name": "two riflemen hurt", "on": "field", "who": "*Rifleman", "path": "Health.Current", "op": "<", "value": 50, "quantifier": "atLeast:2"},
                         {"name": "the tank ends alive", "on": "field", "who": "Tank", "path": "Health.Current", "op": ">", "value": 0, "atEnd": true}] }
            """, out var problems);
        Assert.Empty(problems);
        var w = new FakeWorld().Unit("Tank", 1, 100, 0).Unit("T-72", 2, 100, 0).Unit("Rifleman 1", 3, 100, 0).Unit("Rifleman 2", 4, 100, 0).Unit("Friend 1", 5, 100, 0);
        w.Shot(0, 99.0, 9, null, 5);                         // before the run: below the floor, never counted
        var book = new ConditionBook(checks, w, t0: 100.0);
        book.Observe(0.0);
        w.Shot(1, 101.25, 1, 3, 30);                         // HE at a rifleman
        w.Shot(2, 101.50, 1, 2, 600);                        // APFSDS at the T-72 — the event's own time, not the sample's
        book.Observe(1.75);
        Assert.Equal(1.25, book.Times["first"], 6);
        Assert.Equal(1.50, book.Times["an APFSDS round at the T-72"], 6);
        Assert.False(book.Times.ContainsKey("the second round after the first"), "1.50 is before first+0.5 = 1.75");
        w.Shot(3, 101.80, 1, 3, 30);
        w.Comps[3]["Health"]!["Current"] = 20;
        book.Observe(2.0);
        Assert.Equal(1.80, book.Times["the second round after the first"], 6);
        Assert.False(book.Times.ContainsKey("two riflemen hurt"), "one of two");
        w.Comps[4]["Health"]!["Current"] = 10;
        w.Shot(4, 102.10, 1, 3, 30, hit: 5);                 // a round strikes Friend 1
        book.Finish(2.5);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.True(v["two riflemen hurt"].Ok);
        Assert.Equal(2.5, v["two riflemen hurt"].Time!.Value, 6);
        Assert.False(v["never hits a friend"].Ok);
        Assert.Equal(2.10, v["never hits a friend"].Time!.Value, 6);
        Assert.True(v["the tank ends alive"].Ok);
    }

    /// <summary>
    /// ⭐ A name matches EVERY unit that carries it — 📌 <c>test-fire</c>'s two "M2 Bradley IFV": the round strikes the SECOND one, which a
    /// one-id-per-name index does not know. 🔒 Red-proof: resolve exact names through <c>Units.Resolve</c> ⇒ the hit is never seen.
    /// </summary>
    [Fact]
    public void CE4042_AName_MatchesEveryUnitThatCarriesIt()
    {
        var checks = Parse("""{ "runFor": 10, "expect": [{"name": "a Bradley is struck", "on": "shot", "hit": "M2 Bradley IFV"}, {"name": "both Bradleys alive", "on": "field", "who": "M2 Bradley IFV", "path": "Health.Current", "op": ">", "value": 0, "quantifier": "all", "atEnd": true}] }""", out var problems);
        Assert.Empty(problems);
        var w = new FakeWorld().Unit("M2 Bradley IFV", 1, 100, 0);
        w.Extra.Add((2, "M2 Bradley IFV"));
        w.Comps[2] = new JsonObject { ["Health"] = new JsonObject { ["Current"] = 70 } };
        var book = new ConditionBook(checks, w, 0);
        book.Observe(0);
        w.Shot(0, 3.0, 1, 2, 30, hit: 2);
        book.Finish(10);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.Equal(3.0, v["a Bradley is struck"].Time!.Value, 6);
        Assert.True(v["both Bradleys alive"].Ok);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> (Minefields MR-19, R-4277) — the <c>knowledge</c> cue reads what a SIDE knows of a named minefield, as the side's own readers do
    /// (<c>PlaceKnowledgeTable</c>): <c>Marked</c> holds for the side that knows it (here: known at start) and not for another; a cell state counts the
    /// field's grid cells the side knows in it (default <c>&gt;= 1</c>). The grammar names a knowledge cue with no side or an unknown state.
    /// 🔴 Red-proof: read the knowledge of side 0 for every cue ⇒ Friend's rows fail.
    /// </summary>
    [Fact]
    public void CE4624_TheKnowledgeCue_ReadsWhatASideKnowsOfAField()
    {
        var checks = Parse("""
            { "runFor": 10, "expect": [
                {"name": "Friend knows the field", "on": "knowledge", "who": "Field A", "side": "Friend", "is": "Marked"},
                {"name": "Hostile knows the field", "on": "knowledge", "who": "Field A", "side": "Hostile", "is": "Marked"},
                {"name": "Friend knows every cell mined", "on": "knowledge", "who": "Field A", "side": "Friend", "is": "KnownMined", "op": "==", "value": 16},
                {"name": "Friend suspects a cell", "on": "knowledge", "who": "Field A", "side": "Friend", "is": "Suspected"},
                {"name": "Hostile knows nothing", "on": "knowledge", "who": "Field A", "side": "Hostile", "is": "Unknown", "op": "==", "value": 16}] }
            """, out var problems);
        Assert.Empty(problems);
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Terrain.Minefield>(); world.RegisterComponent<Fdp.Toolkit.Terrain.MinefieldKnowledge>();
        world.RegisterManagedComponent<Hrot.Map.Common.Components.EditablePolyline>();
        var field = world.CreateEntity();
        world.AddComponent(field, new SimTransform { Position = new Vector3(500f, 200f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(field, new Fdp.Toolkit.Terrain.Minefield { Type = Fdp.Toolkit.Terrain.MineType.AntiPersonnel, SpacingM = 4f, KnownAtStartSides = 1 << (int)ForceId.Friend });
        world.AddComponent(field, new NetworkIdentity { Value = 77 });
        world.SetManagedComponent(field, new Hrot.Map.Common.Components.EditablePolyline   // 40 × 40 m ⇒ a 4 × 4 grid of 10 m cells
        {
            Points = new[] { new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 40f), new Vector2(0f, 40f) },
        });
        var w = new FakeWorld { World = world };
        w.U.Ids["Field A"] = 77;
        var book = new ConditionBook(checks, w, 0);
        book.Finish(1);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.True(v["Friend knows the field"].Ok);
        Assert.False(v["Hostile knows the field"].Ok);
        Assert.True(v["Friend knows every cell mined"].Ok);
        Assert.False(v["Friend suspects a cell"].Ok);
        Assert.True(v["Hostile knows nothing"].Ok);

        Parse("""{ "runFor": 1, "expect": [{"name": "x", "on": "knowledge", "who": "Field A", "is": "Mined"}] }""", out var bad);
        Assert.Contains(bad, p => p.Contains("needs side", StringComparison.Ordinal));
        Assert.Contains(bad, p => p.Contains("is is one of", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐ <c>CE-2443</c> (Minefields MF-10) — <c>is: "Lane"</c>: the COUNT of cleared lanes the side holds on the field (its lane records; default <c>&gt;= 1</c>) —
    /// a marked field reads every cell known-mined, so the lane is in the side's knowledge as a lane record, never as cell levels.
    /// 🔴 Red-proof: count every side's lanes ⇒ "Hostile holds no lane" fails.
    /// </summary>
    [Fact]
    public void CE2443_TheKnowledgeCue_Lane_CountsTheSidesClearedLanes()
    {
        var checks = Parse("""
            { "runFor": 10, "expect": [
                {"name": "Friend holds a lane", "on": "knowledge", "who": "Field A", "side": "Friend", "is": "Lane"},
                {"name": "Friend holds one lane", "on": "knowledge", "who": "Field A", "side": "Friend", "is": "Lane", "op": "==", "value": 1},
                {"name": "Hostile holds no lane", "on": "knowledge", "who": "Field A", "side": "Hostile", "is": "Lane", "op": "==", "value": 0}] }
            """, out var problems);
        Assert.Empty(problems);
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<Fdp.Toolkit.Scenario.EntityGuid>();
        world.RegisterComponent<Fdp.Toolkit.Terrain.Minefield>(); world.RegisterComponent<Fdp.Toolkit.Terrain.MinefieldKnowledge>(); world.RegisterComponent<Fdp.Toolkit.Terrain.MinefieldLanes>();
        world.RegisterManagedComponent<Hrot.Map.Common.Components.EditablePolyline>();
        var guid = Guid.NewGuid();
        var field = world.CreateEntity();
        world.AddComponent(field, new SimTransform { Position = new Vector3(500f, 200f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(field, new Fdp.Toolkit.Terrain.Minefield { Type = Fdp.Toolkit.Terrain.MineType.AntiPersonnel, SpacingM = 4f });
        world.AddComponent(field, new NetworkIdentity { Value = 77 });
        world.AddComponent(field, new Fdp.Toolkit.Scenario.EntityGuid(guid));
        world.SetManagedComponent(field, new Hrot.Map.Common.Components.EditablePolyline
        {
            Points = new[] { new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 40f), new Vector2(0f, 40f) },
        });
        var lanes = new Fdp.Toolkit.Terrain.MinefieldLanes { Field = guid };
        Assert.True(lanes.TryAdd(new Fdp.Toolkit.Terrain.LaneItem(guid, new Vector2(490f, 220f), new Vector2(550f, 220f), 4.5f, (byte)((int)ForceId.Friend + 1))));
        world.AddComponent(world.CreateEntity(), lanes);
        var w = new FakeWorld { World = world };
        w.U.Ids["Field A"] = 77;
        var book = new ConditionBook(checks, w, 0);
        book.Finish(1);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.True(v["Friend holds a lane"].Ok);
        Assert.True(v["Friend holds one lane"].Ok);
        Assert.True(v["Hostile holds no lane"].Ok);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-5033</c> (glass windows, docs/DESIGN_Glass_Windows.md §7) — the <c>window</c> cue reads a building's glazed window as every reader of the world does
    /// (<c>DoorStates.Windows</c>): the one nearest a point (or any of the building's) is <c>intact</c>, <c>cracked</c> (at least <c>n</c> hits) or <c>broken</c>.
    /// The grammar names a window cue with no building or an unknown state. 🔴 Red-proof: judge "broken" by <c>Hits &gt;= 1</c> ⇒ the cracked window reads broken.
    /// </summary>
    [Fact]
    public void CE5033_TheWindowCue_ReadsAPanesState()
    {
        var checks = Parse("""
            { "runFor": 10, "expect": [
                {"name": "the west window is cracked twice", "on": "window", "building": "range/H", "point": [22.6, 20], "within": 1, "is": "cracked", "n": 2},
                {"name": "the west window is broken", "on": "window", "building": "range/H", "point": [22.6, 20], "within": 1, "is": "broken"},
                {"name": "the east window is broken", "on": "window", "building": "range/H", "point": [27.6, 20], "within": 1, "is": "broken"},
                {"name": "some window of H is broken", "on": "window", "building": "range/H", "is": "broken"},
                {"name": "the east window is intact", "on": "window", "building": "range/H", "point": [27.6, 20], "within": 1, "is": "intact"}] }
            """, out var problems);
        Assert.Empty(problems);
        var terrain = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [ { "type": "Feature", "properties": { "kind": "building", "label": "H",
                "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]], "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "openings": [ { "kind": "window", "at": 2, "width": 1.2 }, { "kind": "window", "at": 7, "width": 1.2 } ] },
                  { "from": [10,0], "to": [10,8] }, { "from": [10,8], "to": [0,8] }, { "from": [0,8], "to": [0,0] } ] } ] } },
              "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """, "range");
        var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Terrain.WindowDamage>();
        world.SetSingletonManaged(terrain);
        var (panel, west) = terrain.Panes[0];
        var d = new Fdp.Toolkit.Terrain.WindowDamage { Building = 0 };
        Fdp.Toolkit.Terrain.WindowDamages.Hit(ref d, panel, west, 0.5f, 0.5f);
        Fdp.Toolkit.Terrain.WindowDamages.Hit(ref d, panel, west, 0.2f, 0.2f);
        Fdp.Toolkit.Terrain.WindowDamages.Break(ref d, terrain.Panes[1].Panel, terrain.Panes[1].Opening);
        world.AddComponent(world.CreateEntity(), d);
        var book = new ConditionBook(checks, new FakeWorld { World = world }, 0);
        book.Finish(1);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.True(v["the west window is cracked twice"].Ok);
        Assert.False(v["the west window is broken"].Ok);
        Assert.True(v["the east window is broken"].Ok);
        Assert.True(v["some window of H is broken"].Ok);
        Assert.False(v["the east window is intact"].Ok);

        Parse("""{ "runFor": 1, "expect": [{"name": "x", "on": "window", "is": "shattered"}] }""", out var bad);
        Assert.Contains(bad, p => p.Contains("needs building", StringComparison.Ordinal));
        Assert.Contains(bad, p => p.Contains("is is one of", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4672</c> (the street fight, R-4277) — <c>cover</c> reads the terrain's cover database (the world's <c>ICoverProvider</c>): a live man within <c>m</c> of a cover point
    /// is <c>in</c>, one with none is <c>open</c>, counted by the quantifier; a dead one counts for neither. <c>bunched</c> holds when every live man of the group stands within
    /// <c>m</c> of their centre. <c>dead</c> on a field cue judges only the dead the name matches. The grammar names a cover cue with no state or no m.
    /// 🔴 Red-proof: <c>BunchedHolds</c> ignoring the dead ⇒ the dead man far away still counts ⇒ the bunched squad reads spread ⇒ red.
    /// </summary>
    [Fact]
    public void CE4672_CoverBunchedAndDead()
    {
        var checks = Parse("""
            { "runFor": 10, "expect": [
                {"name": "someone of the squad is in cover", "on": "cover", "who": "*Sq ", "is": "in", "m": 2},
                {"name": "the whole squad is in cover", "on": "cover", "who": "*Sq ", "is": "in", "m": 2, "quantifier": "all"},
                {"name": "two of the squad stand in the open", "on": "cover", "who": "*Sq ", "is": "open", "m": 2, "quantifier": "atLeast:2"},
                {"name": "the squad is bunched", "on": "bunched", "who": "*Sq ", "m": 2},
                {"name": "a dead man still runs a behaviour", "on": "field", "who": "*Sq ", "dead": true, "path": "BehaviorState.ActiveBehaviorHash", "op": "!=", "value": 0},
                {"name": "a dead man runs nothing", "never": {"on": "field", "who": "*Sq ", "dead": true, "path": "BehaviorState.ActiveBehaviorHash", "op": "==", "value": 0}}] }
            """, out var problems);
        Assert.Empty(problems);
        var world = new EntityRepository();
        world.SetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>(new Fdp.Toolkit.Spatial.Eqs.ManualCoverProvider(new[]
            { new Fdp.Toolkit.Spatial.Eqs.CoverPoint { PositionX = 10, PositionY = 0, PositionZ = 0, DirectionX = 1, Quality = 1 } }));
        var w = new FakeWorld { World = world };
        void Man(string name, int id, double hp, double x, int behaviour)
        {
            w.U.Ids[name] = id;
            w.Comps[id] = new JsonObject
            {
                ["Health"] = new JsonObject { ["Current"] = hp, ["Max"] = 100 },
                ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, 0, 0) },
                ["BehaviorState"] = new JsonObject { ["ActiveBehaviorHash"] = behaviour },
            };
        }
        Man("Sq Leader", 1, 100, 10.5, 7);     // by the cover point
        Man("Sq Rifleman 1", 2, 100, 12.5, 7); // 2.5 m off it: in the open
        Man("Sq Rifleman 2", 3, 100, 13.0, 7); // in the open
        Man("Sq Rifleman 3", 4, 0, 60, 9);     // dead, far away, still "running" a behaviour
        var book = new ConditionBook(checks, w, 0);
        book.Observe(1);
        book.Finish(10);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.True(v["someone of the squad is in cover"].Ok);
        Assert.False(v["the whole squad is in cover"].Ok);
        Assert.True(v["two of the squad stand in the open"].Ok);
        Assert.True(v["the squad is bunched"].Ok);                 // the three live men within 2 m of their centre (11.3): the dead one is not counted
        Assert.True(v["a dead man still runs a behaviour"].Ok);
        Assert.True(v["a dead man runs nothing"].Ok);               // no dead man has hash 0 ⇒ the never row holds

        Parse("""{ "runFor": 1, "expect": [{"name": "x", "on": "cover", "who": "A"}, {"name": "y", "on": "bunched", "who": "*Sq "}, {"name": "z", "on": "near", "who": "A", "point": [0,0], "dead": true}] }""", out var bad);
        Assert.Contains(bad, p => p.Contains("cover cue's is is one of", StringComparison.Ordinal));
        Assert.Contains(bad, p => p.Contains("a cover cue needs m", StringComparison.Ordinal));
        Assert.Contains(bad, p => p.Contains("a bunched cue needs m", StringComparison.Ordinal));
        Assert.Contains(bad, p => p.Contains("dead belongs to a field cue", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐ <c>CE-2443</c> — <c>also</c> on a field cue: every extra condition holds on the SAME unit ("a man inside the field's x-span AND above the lane's band");
    /// a man who meets one condition and another man the other does not make it hold. 🔴 Red-proof: judge <c>also</c> on ANY unit ⇒ the split pair holds ⇒ red.
    /// </summary>
    [Fact]
    public void CE2443_Also_AllConditionsOnTheSameUnit()
    {
        var checks = Parse("""
            { "runFor": 5, "expect": [
                {"name": "a man in the field above the lane", "on": "field", "who": "*Man", "path": "SimTransform.Position.1", "op": ">", "value": 83,
                 "also": [{"path": "SimTransform.Position.0", "op": ">", "value": 120}, {"path": "SimTransform.Position.0", "op": "<", "value": 140}]}] }
            """, out var problems);
        Assert.Empty(problems);
        JsonObject At(double x, double y) => new() { ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, y, 0) } };
        var split = new FakeWorld();
        split.U.Ids["Man A"] = 1; split.Comps[1] = At(100, 90);   // above the band, but outside the field
        split.U.Ids["Man B"] = 2; split.Comps[2] = At(130, 80);   // in the field, but inside the lane
        var b1 = new ConditionBook(checks, split, 0); b1.Observe(1); b1.Finish(5);
        Assert.False(b1.Verdicts().Single().Ok);
        var one = new FakeWorld();
        one.U.Ids["Man C"] = 3; one.Comps[3] = At(130, 90);       // in the field AND above the band
        var b2 = new ConditionBook(checks, one, 0); b2.Observe(1); b2.Finish(5);
        Assert.True(b2.Verdicts().Single().Ok);
        Parse("""{ "runFor": 1, "expect": [{"name": "x", "on": "near", "who": "A", "point": [0,0], "also": [{"path": "a", "op": "==", "value": 1}]}] }""", out var bad);
        Assert.Contains(bad, p => p.Contains("also belongs to a field cue", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐ A <c>hit</c> cue sees a burst that STRUCK the unit (a drone diving onto a tank), as the take's <c>AnchorBook.Hit</c> does — 📌
    /// measured on <c>ua-drone-shelter</c>: without it, "the deck tank is never hit" was vacuous. 🔴 Red-proof: drop the <c>struck</c> term.
    /// </summary>
    [Fact]
    public void CE4042_AHitCue_SeesABurstThatStruckTheUnit()
    {
        var checks = Parse("""{ "runFor": 10, "expect": [{"name": "the tank is hit", "on": "hit", "target": "Tank"}] }""", out _);
        var w = new FakeWorld().Unit("Tank", 1, 100, 0).Unit("Drone", 2, 10, 0);
        var book = new ConditionBook(checks, w, 0);
        book.Observe(0);
        w.BlastRows.Insert(0, new JsonObject { ["seq"] = 0, ["shooter"] = 2, ["struck"] = 1, ["effects"] = new JsonArray() });
        book.Observe(2.5);
        Assert.Equal(2.5, book.Verdicts().Single().Time!.Value, 6);
    }

    /// <summary>
    /// ⭐ A held handle hashes as the unit it MEANS: the passenger points at "Technical" under index 2 in one run and index 3 in another
    /// (one entity created more before it — allocation order) and the golden is the SAME. 🔴 Red-proof: hash the raw value ⇒ different.
    /// </summary>
    [Fact]
    public void CE4042_AHeldHandle_HashesAsTheUnitItMeans()
    {
        var checks = Parse("""{ "runFor": 1, "golden": {"every": 1}, "expect": [{"name": "x", "on": "field", "who": "Rider", "path": "Health.Current", "op": ">", "value": 0}] }""", out _);
        JsonObject Run(int technicalId)
        {
            var w = new FakeWorld().Unit("Rider", 1, 100, 0).Unit("Technical", technicalId, 100, 0);
            // ⭐ a plain field beside the handle, and one inside an array: the real dump's shape (a handle-only component hid a throw)
            w.Comps[1]["IsEmbarkedTag"] = new JsonObject { ["VehicleEntity"] = $"[{technicalId}, v1]", ["Seat"] = 2, ["Slots"] = new JsonArray(1, $"[{technicalId}, v1]") };
            var g = new ScenarioGolden(w);
            g.Snapshot(0);
            var book = new ConditionBook(checks, w, 0);
            book.Finish(0);
            return g.ToJson("s", 1, book);
        }
        Assert.Empty(ScenarioGolden.Compare(Run(2), Run(3)));
    }

    /// <summary>
    /// ⭐ A non-finite number in a component is written by NAME — JSON cannot hold one, and 📐 a component carrying +∞ made the whole verify
    /// of ua-casualty-care-heavy fail at step 256. 🔴 Red-proof: drop the non-finite case ⇒ ArgumentException from the hash.
    /// </summary>
    [Fact]
    public void CE4042_ANonFiniteNumber_IsWrittenByName()
    {
        var checks = Parse("""{ "runFor": 1, "golden": {"every": 1, "full": true}, "expect": [{"name": "x", "on": "field", "who": "Rider", "path": "Health.Current", "op": ">", "value": 0}] }""", out _);
        var w = new FakeWorld().Unit("Rider", 1, 100, 0);
        w.Comps[1]["SquadMemberSense"] = new JsonObject { ["Since"] = JsonValue.Create(float.PositiveInfinity), ["Gap"] = JsonValue.Create(double.NaN), ["Ok"] = 1.5 };
        var g = new ScenarioGolden(w, full: true);
        g.Snapshot(0);
        var book = new ConditionBook(checks, w, 0);
        book.Finish(0);
        var json = g.ToJson("s", 1, book).ToJsonString();
        Assert.Contains("\"Infinity\"", json);
        Assert.Contains("\"NaN\"", json);
        var hashed = new ScenarioGolden(w);
        hashed.Snapshot(0);
        _ = hashed.ToJson("s", 1, book).ToJsonString();   // the hash path does not throw either
    }

    /// <summary>
    /// ⭐ A failed expectation whose cue held LATE says when ("it moved" is not "it never happened" — measured on pc-defend-house, whose
    /// Red line opened fire after its window). 🔴 Red-proof: drop the outside record ⇒ heldOutsideItsWindowAt is null.
    /// </summary>
    [Fact]
    public void CE4042_AFailedExpectation_SaysWhenItHeldOutsideItsWindow()
    {
        var checks = Parse("""{ "runFor": 60, "expect": [{"name": "fires early", "on": "shot", "by": "Tank", "before": 10}, {"name": "never fires", "on": "shot", "by": "Ghost"}] }""", out _);
        var w = new FakeWorld().Unit("Tank", 1, 100, 0).Unit("Ghost", 2, 100, 0);
        var book = new ConditionBook(checks, w, 0);
        book.Observe(0);
        w.Shot(0, 42.0, 1, null, 30);
        book.Finish(60);
        var v = book.Verdicts().ToDictionary(x => x.Name);
        Assert.False(v["fires early"].Ok);
        Assert.Equal(42.0, v["fires early"].Outside!.Value, 6);
        Assert.Null(v["never fires"].Outside);
    }

    /// <summary>
    /// ⭐⭐ The golden: two equal runs compare SAME; a unit moved 0.2 m at the second snapshot is named — the FIRST differing snapshot, the
    /// unit, the value — and a component that differs only in a field the golden hashes is named by its component. Positions and health are
    /// compared within 0.001 (the /diff/compare tolerance); a component's hash is exact. 🔴 Red-proof: compare snapshots in reverse ⇒ the first line names t=2, not t=1.
    /// </summary>
    [Fact]
    public void CE4042_TheGolden_NamesTheFirstSnapshotAndUnitThatDiffer()
    {
        var checks = Parse("""{ "runFor": 2, "golden": {"every": 1}, "expect": [{"name": "x", "on": "field", "who": "A", "path": "Health.Current", "op": ">", "value": 0}] }""", out _);
        JsonObject Run(double xAt1, double xAt2, string mood)
        {
            var w = new FakeWorld().Unit("A", 1, 100, 0).Unit("B", 2, 100, 5);
            var book = new ConditionBook(checks, w, 0);
            var g = new ScenarioGolden(w);
            g.Snapshot(0);
            w.Comps[1]["SimTransform"]!["Position"]!["X"] = xAt1;
            w.Comps[2]["Mood"] = new JsonObject { ["Value"] = mood };
            g.Snapshot(1);
            w.Comps[1]["SimTransform"]!["Position"]!["X"] = xAt2;
            g.Snapshot(2);
            book.Finish(2);
            return g.ToJson("s", 120, book);
        }
        var golden = Run(1.0, 2.0, "calm");
        Assert.Empty(ScenarioGolden.Compare(golden, Run(1.0, 2.0, "calm")));

        // ⚠ a position is compared within 0.001, but a component's HASH is exact (a stepped run repeats bit for bit — Regression_Net N1)
        var tiny = ScenarioGolden.Compare(golden, Run(1.0004, 2.0, "calm"));
        Assert.Contains(tiny, l => l.Contains("units.A.components.SimTransform"));
        Assert.DoesNotContain(tiny, l => l.Contains("units.A.position"));

        var moved = ScenarioGolden.Compare(golden, Run(1.2, 2.0, "calm"));
        Assert.StartsWith("FIRST differing snapshot at t=1 s", moved[0]);
        Assert.Contains("A", moved[0]);
        Assert.Contains(moved, l => l.Contains("units.A.position") && l.Contains("1.2"));

        var mood = ScenarioGolden.Compare(golden, Run(1.0, 2.0, "angry"));
        Assert.Contains(mood, l => l.Contains("units.B.components.Mood"));
    }

    /// <summary>⭐ R-8320 — a field path with a <c>*</c> part counts the ARRAY's elements that satisfy the comparison (the stochastic unit's slot table): <c>elements</c> is the least count.</summary>
    [Fact]
    public void R8320_AFieldPathWithAStar_CountsTheElementsThatHold()
    {
        var comps = JsonNode.Parse("""{"UnitSlotTable": {"Count": 3, "Slots": [{"Health": 25, "CompositeSlot": 1}, {"Health": 0, "CompositeSlot": 2}, {"Health": 0, "CompositeSlot": 3}, {"Health": 0, "CompositeSlot": 0}]}}""")!;
        JsonNode Zero = JsonValue.Create(0)!;
        Assert.True(ConditionBook.FieldHolds(comps, "UnitSlotTable.Slots.*.Health", "<=", Zero, 3));          // the unused fourth slot is all zeros too
        Assert.False(ConditionBook.FieldHolds(comps, "UnitSlotTable.Slots.*.Health", "<=", Zero, 4));
        var used = new List<(string, string, JsonNode?)> { ("CompositeSlot", ">", JsonValue.Create(0)) };   // where: only the slots a composite filled
        Assert.True(ConditionBook.FieldHolds(comps, "UnitSlotTable.Slots.*.Health", "<=", Zero, 2, used));
        Assert.False(ConditionBook.FieldHolds(comps, "UnitSlotTable.Slots.*.Health", "<=", Zero, 3, used));
        Assert.False(ConditionBook.FieldHolds(comps, "UnitSlotTable.Nope.*.Health", "<=", Zero, 1));
        Assert.True(ConditionBook.FieldHolds(comps, "UnitSlotTable.Count", "==", JsonValue.Create(3), 1));   // no star: the plain read
        var checks = Parse("""{"runFor": 5, "expect": [{"name": "two down", "on": "field", "who": "U", "path": "UnitSlotTable.Slots.*.Health", "op": "<=", "value": 0, "elements": 2}]}""", out var problems);
        Assert.Empty(problems);
        Assert.Equal(2, checks.Expect[0].Cue.Elements);
        Parse("""{"runFor": 5, "expect": [{"name": "x", "on": "field", "who": "U", "path": "A.*.B", "elements": 0}]}""", out var bad);
        Assert.Contains(bad, l => l.Contains("elements"));
        Parse("""{"runFor": 5, "expect": [{"name": "x", "on": "field", "who": "U", "path": "A.B", "where": {"C": 1}}]}""", out var plain);
        Assert.Contains(plain, l => l.Contains("where"));
        Parse("""{"runFor": 5, "expect": [{"name": "x", "on": "field", "who": "U", "path": "A.*.B", "where": {"C": [">", 0]}}]}""", out var starred);
        Assert.Empty(starred);
    }

    /// <summary>
    /// ⭐ R-8335 (agg-behaviors) — a <c>field</c> cue's <c>who</c> may be a NETWORK ID (as the near / task cues read one): an UNNAMED entity — a
    /// stochastic unit's split child — is judged by it. 🔴 Red-proof: drop the id branch of <c>UnitsNamed</c> ⇒ the unnamed child never holds.
    /// </summary>
    [Fact]
    public void R8335_AFieldCueNamesAnUnnamedEntityByItsNetworkId()
    {
        var checks = Parse("""{"runFor": 1, "expect": [{"name": "the child's key", "on": "field", "who": "1007", "path": "EntityGuid.Value", "op": "==", "value": "0b02c05f-cb97-5c94-b354-0ba5ec6c6ac9"}, {"name": "a gone id", "on": "field", "who": "1099", "path": "EntityGuid.Value", "op": "==", "value": "x"}]}""", out var problems);
        Assert.Empty(problems);
        var w = new FakeWorld().Unit("A", 1, 100, 0);
        w.Comps[1007] = new JsonObject { ["EntityGuid"] = new JsonObject { ["Value"] = "0b02c05f-cb97-5c94-b354-0ba5ec6c6ac9" } };   // no name: not in Named()
        var book = new ConditionBook(checks, w, 0);
        book.Observe(0.5);
        book.Finish(1);
        var v = book.Verdicts();
        Assert.True(v.Single(x => x.Name == "the child's key").Ok);
        Assert.False(v.Single(x => x.Name == "a gone id").Ok);
    }

    /// <summary>
    /// ⭐⭐ RT-3 (R-4279) — the golden compares VISIBLE state: an internal blackboard / diagnostics component that differs (or a component
    /// added since the capture) is NOT a difference; a visible mode (<c>AnimalState</c>), a field the checks read (<c>Fuel.Litres</c>) and a
    /// position ARE; a golden from before the visible mode asks to be re-captured; and a golden difference is a WARNING — the run still
    /// PASSES. 🔴 Red-proofs: compare with <c>Compare</c> (the full one) ⇒ the blackboard line appears; keep the golden in <c>Passed</c> ⇒ red.
    /// </summary>
    [Fact]
    public void RT3_TheGolden_ComparesVisibleState_AndADifferenceIsAWarning()
    {
        var checks = Parse("""{ "runFor": 1, "golden": {"every": 1}, "expect": [{"name": "fuel", "on": "field", "who": "A", "path": "Fuel.Litres", "op": ">", "value": 0}] }""", out _);
        JsonObject Run(string blackboard, string mode, double litres, double x, bool addedComponent = false)
        {
            var w = new FakeWorld().Unit("A", 1, 100, x);
            w.Comps[1]["BlueprintBlackboard1024"] = new JsonObject { ["Bytes"] = blackboard };
            w.Comps[1]["AnimalState"] = new JsonObject { ["Mode"] = mode };
            w.Comps[1]["Fuel"] = new JsonObject { ["Litres"] = litres, ["Internal"] = blackboard };
            if (addedComponent) w.Comps[1]["RadarSignature"] = new JsonObject { ["Rcs"] = 3 };
            var g = new ScenarioGolden(w, checkPaths: checks.Paths());
            g.Snapshot(0);
            var book = new ConditionBook(checks, w, 0);
            book.Finish(0);
            return g.ToJson("s", 60, book);
        }
        var golden = Run("aa", "Calm", 10, 0);
        Assert.Empty(ScenarioGolden.CompareVisible(golden, Run("bb", "Calm", 10, 0, addedComponent: true)));   // internals and a new component: same
        Assert.Contains(ScenarioGolden.CompareVisible(golden, Run("aa", "Flee", 10, 0)), l => l.Contains("visible.AnimalState"));
        Assert.Contains(ScenarioGolden.CompareVisible(golden, Run("aa", "Calm", 4, 0)), l => l.Contains("visible.Fuel.Litres"));
        Assert.Contains(ScenarioGolden.CompareVisible(golden, Run("aa", "Calm", 10, 2)), l => l.Contains("units.A.position"));

        // a golden captured before the visible mode: compared on what it has, and asked to be re-captured
        var old = (JsonObject)golden.DeepClone();
        foreach (var snap in old["snapshots"]!.AsArray()) foreach (var u in snap!["units"]!.AsObject()) u.Value!.AsObject().Remove("visible");
        Assert.StartsWith("the golden predates", ScenarioGolden.CompareVisible(old, golden)[0]);

        // a difference is a WARNING: the run passes and says so
        var result = new VerifyResult { Golden = "different" };
        result.Verdicts.Add(new Verdict("fuel", true, 0, "field"));
        Assert.True(result.Passed);
        Assert.Single(result.Warnings);
        Assert.Single(result.ToJson()["warnings"]!.AsArray());
    }
}
