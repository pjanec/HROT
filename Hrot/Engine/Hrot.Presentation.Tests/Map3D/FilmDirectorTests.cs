using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.UI.Common.Audio;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, §4) — the film director's pure parts: the take and reel formats, the seeded takes, the
/// ONE copy of the shot math (ported from film.py's <c>_shot_for</c>), the paced clock's one-step-per-frame reckoning, the timeline, the
/// anchor cues read from component state, and the film's sound track. The runner's rails are in <c>Hrot.Editor.Tests</c>.
/// </summary>
public sealed class FilmDirectorTests
{
    private sealed class Units : IShotWorld
    {
        public readonly Dictionary<string, long> Ids = new();
        public readonly Dictionary<long, Vector3> At = new();
        public long? Resolve(string who) => long.TryParse(who, out var id) ? id : Ids.TryGetValue(who, out id) ? id : null;
        public Vector3? PositionOf(long id) => At.TryGetValue(id, out var p) ? p : null;
        public IReadOnlyCollection<string> Names => Ids.Keys;
    }

    private static ShotSpec Shot(string json)
    {
        var problems = new List<string>();
        var s = TakeJson.ParseShot(JsonNode.Parse(json)!.AsObject(), 1, problems);
        Assert.Empty(problems);
        return s;
    }

    [Theory]
    [InlineData("12", null, 12d)]
    [InlineData("\"fire\"", "fire", 0d)]
    [InlineData("\"fire-0.3\"", "fire", -0.3d)]
    [InlineData("\"door+5\"", "door", 5d)]
    [InlineData("\"a-b\"", "a-b", 0d)]
    public void CE4038_AnAtIsATimeOrAnAnchorPlusAnOffset(string json, string? anchor, double offset)
    {
        var at = AtExpr.Parse(JsonNode.Parse(json));
        Assert.Equal((anchor, offset), (at.Anchor, at.Offset));
        Assert.Equal(at, AtExpr.Parse(at.ToJson()));   // round trip
    }

    /// <summary>B1 — a take reads film.py's keys (snake or camel), refuses an unknown key and a camera naming two kinds, and writes back
    /// what it read. 🔴 Red-proof: drop the unknown-key check ⇒ "lookat" (a typo) passes silently.</summary>
    [Fact]
    public void CE4038_ATake_ReadsFilmPysKeys_RefusesWhatItDoesNotKnow_AndRoundTrips()
    {
        var json = JsonNode.Parse("""
            {"title":"T","caption":"C","tickHz":20,"endAt":"hit+2","anchors":{"hit":{"on":"hit","target":"T-72"}},
             "shots":[{"at":1,"seconds":4,"look_at":"300,300,5","distance":320,"to":{"distance":260},"ease":"linear","why":"w"},
                      {"at":"hit-1","seconds":2,"over":"Tank","toward":"T-72","back":16,"height":5,"speed":0.5}],
             "clip":"F-99","state":"ready"}
            """)!.AsObject();
        var problems = new List<string>();
        var take = TakeJson.ParseTake(json, "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(20, take.TickHz);
        Assert.Equal(new Vector3(300, 300, 5), take.Shots[0].Camera.LookAt);
        Assert.Equal(260f, take.Shots[0].To!["distance"]);
        Assert.Equal(new AtExpr(-1, "hit"), take.Shots[1].At);
        Assert.Equal("F-99", take.Extra!["clip"]!.ToString());

        var again = TakeJson.ParseTake(TakeJson.WriteTake(take), "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(TakeJson.WriteTake(take).ToJsonString(), TakeJson.WriteTake(again).ToJsonString());

        var bad = JsonNode.Parse("""{"shots":[{"at":1,"seconds":2,"lookat":[1,2,3]},{"at":1,"follow":"A","lookAt":[1,2,3]}]}""")!.AsObject();
        TakeJson.ParseTake(bad, "s", "x", problems);
        Assert.Contains(problems, p => p.Contains("unknown key 'lookat'"));
        Assert.Contains(problems, p => p.StartsWith("shot 2: needs exactly one of"));
    }

    /// <summary>
    /// B12 — every seeded take (55, split from <c>docs/films/clips.json</c> beside its scenario) reads with no problem, names a cue the
    /// scout knows, and the features reel names only takes that exist. 🔴 Red-proof: a seeded take with a typo'd key fails here.
    /// </summary>
    [Fact]
    public void CE4038_TheSeededTakes_AllRead_AndTheFeaturesReelNamesOnlyThem()
    {
        var repo = RepoRoot();
        var roots = new[] { Path.Combine(repo, "scenarios"), Path.Combine(repo, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios") };
        var found = new HashSet<string>();
        foreach (var root in roots)
        {
            var store = new TakeStore(() => root, () => Path.Combine(repo, "films"));
            foreach (var (scenario, takes) in store.AllTakes())
                foreach (var t in takes)
                {
                    var loaded = store.Load(scenario, t);
                    Assert.True(loaded.Ok, $"{scenario}/{t}: {string.Join("; ", loaded.Problems)}");
                    found.Add(scenario + "/" + t);
                }
        }
        Assert.True(found.Count >= 55, $"{found.Count} seeded takes");
        var reels = new TakeStore(() => roots[0], () => Path.Combine(repo, "films"));
        var features = reels.LoadReel("features");
        Assert.True(features.Ok, string.Join("; ", features.Problems));
        foreach (var it in features.Value!.Items) Assert.Contains(it.Scenario + "/" + it.Take, found);
    }

    /// <summary>B1 — a take that names a unit the scenario does not have is refused WITH the name (and a near one offered); an anchor a shot
    /// uses but the take does not define is named too.</summary>
    [Fact]
    public void CE4038_AMisspeltName_IsRefusedWithTheName()
    {
        var units = new Units();
        units.Ids["Enemy Tank"] = 7;
        units.Ids["Tank"] = 8;
        var take = new Take
        {
            Scenario = "s",
            Anchors = new Dictionary<string, AnchorSpec> { ["fire"] = new() { On = "shot", By = "Tnak" } },
            Shots = new[] { Shot("""{"at":"boom","seconds":2,"follow":"Enemy tank"}"""), Shot("""{"at":1,"seconds":2,"follow":"*Tank"}""") },
        };
        var problems = TakeStore.Validate(take, units);
        Assert.Contains(problems, p => p.Contains("no unit named 'Enemy tank'") && p.Contains("'Enemy Tank'"));
        Assert.Contains(problems, p => p.Contains("anchor 'fire'") && p.Contains("'Tnak'"));
        Assert.Contains(problems, p => p.Contains("anchor 'boom' is not defined"));
        Assert.DoesNotContain(problems, p => p.Contains("shot 2"));   // "*Tank" matches a name containing it
    }

    /// <summary>
    /// B2 — the shot math is film.py's (<c>_shot_for</c>), now in one place: follow (the orbit turns the yaw by degrees per second), over the
    /// shoulder (eye <c>back</c> behind and <c>height</c> up, looking <c>ahead</c> of the way), a fixed point, an eye and a target; <c>to</c>
    /// eases a key; the lens passes through. 🔴 Red-proof: drop <c>+ Orbit * t</c> ⇒ the yaw stays 10°.
    /// </summary>
    [Fact]
    public void CE4038_TheShotMath_IsFilmPys()
    {
        var units = new Units();
        units.Ids["A"] = 1; units.Ids["B"] = 2;
        units.At[1] = Vector3.Zero; units.At[2] = new Vector3(100, 0, 0);
        const float D = MathF.PI / 180f;

        var follow = ShotEvaluator.Evaluate(Shot("""{"at":0,"seconds":5,"follow":"A","distance":12,"yaw":10,"orbit":12,"pitch":-8,"lift":2,"fov":30}"""), 2, units)!;
        Assert.Equal((1L, 12f, 2f, 30f), (follow.Follow, follow.Distance, follow.Lift, follow.FovDeg!.Value));
        Assert.Equal(34f * D, follow.Yaw, 4);
        Assert.Equal(-8f * D, follow.Pitch, 4);

        var over = ShotEvaluator.Evaluate(Shot("""{"at":0,"seconds":5,"over":"A","toward":"B","back":20,"height":6,"ahead":0.35}"""), 0, units)!;
        var cam = new MapCamera3D { Pose = over.Pose!.Value };
        Assert.True(Vector3.Distance(new Vector3(-20, 0, 6), cam.Eye) < 1e-3f, $"eye {cam.Eye}");
        Assert.True(Vector3.Distance(new Vector3(35, 0, 1.5f), cam.LookAt) < 1e-3f, $"target {cam.LookAt}");

        var look = ShotEvaluator.Evaluate(Shot("""{"at":0,"seconds":4,"lookAt":[5,6,7],"distance":30,"yaw":90,"pitch":-20,"to":{"distance":10}}"""), 1, units)!;
        Assert.Equal(new Vector3(5, 6, 7), look.Pose!.Value.LookAt);
        Assert.Equal(30f - 20f * ShotEvaluator.Eased(Ease.InOut, 0.25f), look.Pose.Value.Distance, 3);
        Assert.Equal(90f * D, look.Pose.Value.Yaw, 4);

        var eyeTarget = ShotEvaluator.Evaluate(Shot("""{"at":0,"seconds":1,"eye":[0,0,10],"target":[10,0,0]}"""), 0, units)!;
        Assert.True(Vector3.Distance(new Vector3(0, 0, 10), new MapCamera3D { Pose = eyeTarget.Pose!.Value }.Eye) < 1e-3f);

        Assert.Null(ShotEvaluator.Evaluate(Shot("""{"at":0,"seconds":1,"follow":"Nobody"}"""), 0, units));
        Assert.Equal(0.5f, ShotEvaluator.Eased(Ease.InOut, 0.5f));
        Assert.Equal(0.25f, ShotEvaluator.Eased(Ease.In, 0.5f));
    }

    /// <summary>
    /// B3 — ⭐⭐ the paced clock is the stepping rule in numbers: each frame takes ONE step, held until it is due (k / Hz / speed after the
    /// start); a late frame steps at once (no catch-up step — the run falls behind, the factor says so). 🔴 Red-proof: a "catch-up"
    /// that returned 0 hold AND asked for two steps would show here as a factor above 1.
    /// </summary>
    [Fact]
    public void CE4038_ThePacedClock_HoldsTheFrameToRealTime_OneStepAtATime()
    {
        var clock = new PacedClock(60);
        clock.Start(10d);
        Assert.Equal(1d / 60d, clock.HoldFor(10d), 9);                 // a fast frame waits for its step
        double now = 10d;
        for (int i = 0; i < 60; i++) { now += clock.HoldFor(now); clock.Stepped(now); }
        Assert.Equal(11d, now, 6);                                     // one second of steps took one second
        Assert.Equal(1d, clock.RealtimeFactor, 6);
        Assert.Equal(0, clock.LateFrames);

        var slow = new PacedClock(60);
        slow.Start(0d);
        now = 0d;
        for (int i = 0; i < 30; i++) { now += 1d / 30d; Assert.Equal(0d, slow.HoldFor(now)); slow.Stepped(now); }   // a 30 fps renderer
        Assert.Equal(0.5d, slow.RealtimeFactor, 6);                    // half real time — the case for 20 / 15 Hz (R-4063)
        Assert.True(slow.LateFrames > 0);

        var twenty = new PacedClock(20);
        twenty.Start(0d);
        now = 0d;
        for (int i = 0; i < 20; i++) { now += 1d / 30d; now += twenty.HoldFor(now); twenty.Stepped(now); }
        Assert.Equal(1d, twenty.RealtimeFactor, 6);                    // the same renderer keeps real time at 20 Hz
        Assert.Equal(0.05f, twenty.StepSeconds);
    }

    /// <summary>B3/B7 — a take laid out in sim time: a shot starts at its <c>at</c> or when the last one ends; an unknown anchor's shot is
    /// left out and named; a replay is skipped (R-4061); a reel item's trim keeps the shots inside it.</summary>
    [Fact]
    public void CE4038_TheTimeline_PlacesShotsAsFilmPyDid()
    {
        var take = new Take
        {
            Anchors = new Dictionary<string, AnchorSpec> { ["fire"] = new() { On = "shot" }, ["late"] = new() { On = "blast" } },
            Shots = new[]
            {
                Shot("""{"at":1,"seconds":4,"lookAt":[0,0,0]}"""),
                Shot("""{"at":"fire-0.5","seconds":2,"lookAt":[0,0,0]}"""),
                Shot("""{"at":"late","seconds":2,"lookAt":[0,0,0]}"""),
                Shot("""{"at":"fire+1","seconds":2,"lookAt":[0,0,0],"replay":true}"""),
                Shot("""{"at":20,"seconds":3,"lookAt":[0,0,0]}"""),
            },
        };
        var times = new Dictionary<string, double> { ["fire"] = 4d };
        var tl = TakeTimeline.Plan(take, a => times.TryGetValue(a, out var t) ? t : null);
        Assert.Equal(new[] { (1, 1d, 5d), (2, 5d, 7d), (5, 20d, 23d) }, tl.Shots.Select(s => (s.Number, s.Start, s.End)));
        Assert.Equal(new[] { "shot 3: anchor 'late'" }, tl.Unresolved);
        Assert.Equal(new[] { 4 }, tl.SkippedReplays);
        Assert.Equal(23d, tl.End);
        Assert.Null(tl.At(10d));
        Assert.Equal(5, tl.Next(10d)!.Number);
        var trimmed = TakeTimeline.Plan(take, a => times.TryGetValue(a, out var t) ? t : null, new AtExpr(-1, "fire"), new AtExpr(21));
        Assert.Equal(new[] { 2, 5 }, trimmed.Shots.Select(s => s.Number));
        Assert.Equal(21d, trimmed.End);
    }

    /// <summary>
    /// B7 — the anchor book reads the run's beats from COMPONENT STATE (⛔ never the bus — CE-3293 S3): a unit near a point, its side, its
    /// health below a share, its running task; a beat is recorded once, at the step it is first seen. A baked time is what shots are placed
    /// by, and one seen at another time is a mismatch — a determinism finding (R-3038).
    /// </summary>
    [Fact]
    public void CE4038_TheAnchorBook_SeesBeatsInComponentState_AndNamesADrift()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<EntityInfo>();
        world.RegisterComponent<Health>();
        world.RegisterComponent<BehaviorState>();
        var man = world.CreateEntity();
        world.AddComponent(man, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = Quaternion.Identity });
        world.AddComponent(man, new NetworkIdentity { Value = 5 });
        world.AddComponent(man, new EntityInfo { Name = "Rioter 1", ForceId = ForceId.Neutral });
        world.AddComponent(man, new Health { Current = 100, Max = 100 });
        world.AddComponent(man, new BehaviorState { ActiveBehaviorHash = 42 });
        var anchors = new Dictionary<string, AnchorSpec>
        {
            ["door"] = new() { On = "near", Who = "*Rioter", Point = new Vector2(10, 0), Within = 2 },
            ["turn"] = new() { On = "side", Who = "Rioter 1", Is = JsonValue.Create("Hostile") },
            ["down"] = new() { On = "health", Who = "Rioter 1", Below = 0.5f },
            ["run"] = new() { On = "task", Who = "Rioter 1", Is = JsonValue.Create("PanicFlee") },
        };
        var units = new WorldUnits(() => world);
        var book = new AnchorBook(anchors, new Dictionary<string, double> { ["door"] = 1.0, ["turn"] = 2.0 }, h => h == 42 ? "Wander" : h == 43 ? "PanicFlee" : null);

        book.Observe(world, units, 0.5);
        Assert.Empty(book.Live);
        world.GetComponentRW<SimTransform>(man).Position = new Vector3(9, 0, 0);
        book.Observe(world, units, 1.0);
        world.GetComponentRW<EntityInfo>(man).ForceId = ForceId.Hostile;
        world.GetComponentRW<BehaviorState>(man).ActiveBehaviorHash = 43;
        book.Observe(world, units, 2.5);
        world.GetComponentRW<Health>(man).Current = 30;
        book.Observe(world, units, 3.0);
        world.GetComponentRW<SimTransform>(man).Position = new Vector3(50, 0, 0);
        book.Observe(world, units, 3.5);   // a beat is recorded ONCE

        Assert.Equal(new Dictionary<string, double> { ["door"] = 1.0, ["turn"] = 2.5, ["run"] = 2.5, ["down"] = 3.0 }, book.Live);
        Assert.Equal(1.0, book.TimeOf("door"));                   // the baked time places the shot
        Assert.Equal(3.0, book.TimeOf("down"));                   // no bake: the live one
        Assert.Equal(new[] { "turn: baked 2 s, live 2.5 s" }, book.Mismatches(3.5).Select(m => m.ToString()));
    }

    /// <summary>B11 — the film's sound track: a sound is placed at the FILM time its sim time falls at (slow motion stretches the gaps, a gap
    /// between shots is skipped), and the WAV holds it. 🔴 Red-proof: map sim time 1:1 ⇒ the second sound lands at 2 s, not 1.5 s.</summary>
    [Fact]
    public void CE4038_TheSoundTrack_IsPlacedOnTheFilmsOwnTimeline()
    {
        // 30 fps: frames 0..29 show sim 0..1 s at speed 1; frames 30..59 show sim 1..1.5 s at speed 0.5; then a jump to sim 10 s
        var sim = Enumerable.Range(0, 30).Select(i => i / 30d)
            .Concat(Enumerable.Range(0, 30).Select(i => 1d + i / 60d))
            .Concat(Enumerable.Range(0, 30).Select(i => 10d + i / 30d)).ToList();
        Assert.Equal(0.5d, SoundTrack.FilmTime(sim, 0.5, 30)!.Value, 6);
        Assert.Equal(1.5d, SoundTrack.FilmTime(sim, 1.25, 30)!.Value, 6);
        Assert.Null(SoundTrack.FilmTime(sim, 5d, 30));             // in the gap the film skips
        Assert.Equal(2.0d, SoundTrack.FilmTime(sim, 10d, 30)!.Value, 6);

        double now = 0d;
        var track = new SoundTrack(() => now);
        var output = new RecordingAudioOutput(new NullAudioOutput(), track);
        output.Open();
        output.Define(9, Enumerable.Range(0, 4800).Select(i => (short)(i % 100 * 300)).ToArray(), 48000);
        now = 0.5; output.Play(9, 1f, -1f, 1f);
        now = 5d; output.Play(9, 1f, 1f, 1f);
        Assert.Equal(2, track.Cues.Count);
        var wav = Path.Combine(Path.GetTempPath(), $"ce4038-{Guid.NewGuid():N}.wav");
        try
        {
            Assert.True(track.RenderWav(wav, sim, 30));
            var bytes = File.ReadAllBytes(wav);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            int at = 44 + (int)(0.5 * SoundTrack.SampleRate) * 4 + 404;   // sample 101 of the clip (sample 100 is a zero of its saw)
            Assert.NotEqual(0, BitConverter.ToInt16(bytes, at));      // left at 0.5 s
            Assert.Equal(0, BitConverter.ToInt16(bytes, at + 2));     // nothing on the right (panned hard left)
        }
        finally { File.Delete(wav); }
    }

    /// <summary>D-2 — "Save As" / "new from" carry a scenario's takes; never over a scenario with takes of its own.</summary>
    [Fact]
    public void CE4038_TakesAreCopied_WithTheScenario_NeverOverOwnOnes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ce4038-{Guid.NewGuid():N}");
        try
        {
            var store = new TakeStore(() => root, () => null);
            store.Save(new Take { Scenario = "a", Name = "main", Shots = new[] { Shot("""{"at":1,"seconds":2,"follow":"X"}""") } });
            Assert.Equal(1, TakeStore.CopyTakes(root, "a", "b"));
            Assert.True(store.Load("b", "main").Ok);
            Assert.Equal(0, TakeStore.CopyTakes(root, "a", "b"));      // b has its own now
            Assert.Equal(new[] { "main" }, store.TakesOf("b"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("no repo root above " + AppContext.BaseDirectory);
    }
}
