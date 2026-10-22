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

    // ── a world the book reads ────────────────────────────────────────────────────────────────

    private sealed class Units : IShotWorld
    {
        public readonly Dictionary<string, long> Ids = new(StringComparer.Ordinal);
        public long? Resolve(string who) => Ids.TryGetValue(who, out var id) ? id : null;
        public Vector3? PositionOf(long networkId) => null;
        public IReadOnlyCollection<string> Names => Ids.Keys;
    }

    private sealed class FakeWorld : IVerifyWorld
    {
        public readonly Units U = new();
        public readonly Dictionary<long, JsonObject> Comps = new();
        public readonly JsonArray ShotRows = new();
        public readonly JsonArray BlastRows = new();
        public EntityRepository? World => null;
        public IShotWorld Units => U;
        public string? BehaviourName(int hash) => null;
        public JsonObject? Components(long id) => Comps.TryGetValue(id, out var c) ? c : null;
        public JsonArray Shots() => (JsonArray)ShotRows.DeepClone();
        public JsonArray Blasts() => (JsonArray)BlastRows.DeepClone();
        public string? SetComponent(long networkId, string component, JsonObject patch) => null;

        public FakeWorld Unit(string name, long id, double health, double x)
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
        public void Shot(long seq, double firedAt, long shooter, long? target, double penetration, long? hit = null)
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
}
