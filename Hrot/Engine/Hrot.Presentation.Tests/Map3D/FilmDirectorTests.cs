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

    /// <summary>A reel's <c>waiting</c> list (clips parked until ready, with the reason — PLAN_Feature_Films §5) reads, round-trips and
    /// refuses a typo'd key; the parked clips are never played. 🔴 Red-proof: drop "waiting" from the reel's key list ⇒ red.</summary>
    [Fact]
    public void CE4038_AReelsWaitingList_Reads_RoundTrips_AndIsNotPlayed()
    {
        var json = JsonNode.Parse("""
            {"title":"R","items":[{"scenario":"a","take":"main"}],"waiting":[{"clip":"F-03","why":"not ready"},{"clip":"F-04"}]}
            """)!.AsObject();
        var problems = new List<string>();
        var reel = TakeJson.ParseReel(json, "r", problems);
        Assert.Empty(problems);
        Assert.Equal(new[] { new ReelWaiting("F-03", "not ready"), new ReelWaiting("F-04", null) }, reel.Waiting);
        Assert.Single(reel.Items);
        var again = TakeJson.ParseReel(TakeJson.WriteReel(reel), "r", problems);
        Assert.Empty(problems);
        Assert.Equal(reel.Waiting, again.Waiting);

        TakeJson.ParseReel(JsonNode.Parse("""{"items":[],"waiting":[{"clip":"F-05","reason":"x"}]}""")!.AsObject(), "r", problems);
        Assert.Contains(problems, p => p.Contains("waiting 1: unknown key 'reason'"));
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

    /// <summary>⭐ <c>CE-1369</c> — a cue is logged once, but an instant replay (a segment that goes back) films its source time twice: the
    /// cue sounds in BOTH passes. 🔴 Red-proof: have <c>FilmTimes</c> stop after its first pass ⇒ the second pass is silent (the WAV holds one cue).</summary>
    [Fact]
    public void CE1369_ACue_SoundsInEveryPassThatShowsItsSourceTime()
    {
        // 30 fps: film 0–2 s shows sim 0–2; film 2–4 s is the instant replay, sim 1–3
        var sim = Enumerable.Range(0, 60).Select(i => i / 30d).Concat(Enumerable.Range(0, 60).Select(i => 1d + i / 30d)).ToList();
        var both = SoundTrack.FilmTimes(sim, 1.5, 30).ToArray();
        Assert.Equal(2, both.Length);
        Assert.Equal(1.5d, both[0], 6);                                                                     // pass one
        Assert.Equal(2.5d, both[1], 6);                                                                     // the replay
        Assert.Equal(3.5d, Assert.Single(SoundTrack.FilmTimes(sim, 2.5, 30)), 6);                           // sim 2.5 is only in the replay
        Assert.Equal(1.5d, SoundTrack.FilmTime(sim, 1.5, 30)!.Value, 6);                                    // FilmTime stays the first pass

        double now = 1.5d;
        var track = new SoundTrack(() => now);
        var output = new RecordingAudioOutput(new NullAudioOutput(), track);
        output.Open();
        output.Define(9, Enumerable.Range(0, 4800).Select(i => (short)(i % 100 * 300)).ToArray(), 48000);
        output.Play(9, 1f, -1f, 1f);
        Assert.Single(track.Cues);                                                                          // logged ONCE
        var wav = Path.Combine(Path.GetTempPath(), $"ce1369-{Guid.NewGuid():N}.wav");
        try
        {
            Assert.True(track.RenderWav(wav, sim, 30));
            var bytes = File.ReadAllBytes(wav);
            int Left(double film) => BitConverter.ToInt16(bytes, 44 + ((int)(film * SoundTrack.SampleRate) + 101) * 4);
            Assert.NotEqual(0, Left(1.5));                                                                  // pass one
            Assert.NotEqual(0, Left(2.5));                                                                  // pass two (the replay)
            Assert.Equal(0, Left(2.0));                                                                     // and nothing between them
        }
        finally { File.Delete(wav); }
    }

    /// <summary>
    /// ⭐ <c>CE-1370</c> — the subtitle band is ONE computation for the cinema overlay and a captured frame: top / middle / bottom sit where the overlay put them
    /// (1/18 of the height from the edge, or centred), and the fade scales the text's alpha and the band's together (the band at 150/255 of the text's).
    /// 🔴 Mutation: <c>SubtitlePosition.Top</c> falling through to the bottom case ⇒ the top band lands at the bottom ⇒ red.
    /// </summary>
    [Fact]
    public void CE1370_TheSubtitleBand_IsOnePlace_ForPositionAndFade()
    {
        const float h = 720f, text = 40f;
        var top = SubtitleBand.Of(new SubtitleFrame("x", SubtitlePosition.Top, 1f), h, text);
        var mid = SubtitleBand.Of(new SubtitleFrame("x", SubtitlePosition.Middle, 1f), h, text);
        var bot = SubtitleBand.Of(new SubtitleFrame("x", SubtitlePosition.Bottom, 1f), h, text);
        float size = SubtitleBand.TextSizeFor(h);
        Assert.Equal(24f, size);                                                  // 720 / 30
        Assert.Equal(h / 18f, top.Top, 3);
        Assert.Equal((h - (text + size)) / 2f, mid.Top, 3);
        Assert.Equal(h - (text + size) - h / 18f, bot.Top, 3);
        Assert.Equal(text + size, bot.Height, 3);
        Assert.True(top.Top < mid.Top && mid.Top < bot.Top);
        Assert.Equal((255, 150), (top.TextAlpha, top.BandAlpha));                 // fully opaque: the band is the dark 150

        var fading = SubtitleBand.Of(new SubtitleFrame("x", SubtitlePosition.Top, 0.5f), h, text);   // mid-fade
        Assert.Equal(128, fading.TextAlpha);
        Assert.Equal(75, fading.BandAlpha);
        Assert.Equal(0, SubtitleBand.Of(new SubtitleFrame("x", SubtitlePosition.Top, 0f), h, text).TextAlpha);
        Assert.Equal(14f, SubtitleBand.TextSizeFor(100f));                        // a small picture keeps a legible minimum
    }

    /// <summary>D-2 — "Save As" / "new from" carry a scenario's takes; never over a scenario with takes of its own.</summary>
    [Fact]
    public void CE4038_TakesAreCopied_WithTheScenario_NeverOverOwnOnes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ce4038-{Guid.NewGuid():N}");
        try
        {
            var store = new TakeStore(() => root, () => null);
            var path = store.Save(new Take { Scenario = "a", Name = "main", Caption = "Fear — it's catching", Shots = new[] { Shot("""{"at":1,"seconds":2,"follow":"X"}""") } });
            Assert.Contains("Fear — it's catching", File.ReadAllText(path));   // ⭐ a hand-edited file keeps its text (no \u2014, no \u0027)
            Assert.Equal(1, TakeStore.CopyTakes(root, "a", "b"));
            Assert.True(store.Load("b", "main").Ok);
            Assert.Equal(0, TakeStore.CopyTakes(root, "a", "b"));      // b has its own now
            Assert.Equal(new[] { "main" }, store.TakesOf("b"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ── TE-2 / T1 — take format v2: keys, segments, shots read as keys, ProgramAt ─────────────────────────────

    private const string V2Take = """
        {"anchors":{"fire":{"on":"shot","who":"Tank 1","n":1}},
         "segments":[{"from":0,"to":"fire-5"},{"from":"fire-1","to":"fire+3","speed":0.25,"gap":"dip"}],
         "keys":[{"at":"fire-5","lookAt":[105,75,1],"distance":60,"pitch":-30,"in":"cut"},
                 {"at":"fire-0.5","over":"Tank 1","toward":"Enemy","back":16,"in":{"fly":2,"ease":"in"},"still":"crossing"},
                 {"at":"fire+1","follow":"Enemy","distance":12,"fov":50,"in":{"dip":[0.4,0.6]}}]}
        """;

    /// <summary>T1 (§5, TE-2, TE-8) — a v2 take (a cut, an eased fly and a dip key, two segments) reads, and parse → write → parse is equal.
    /// 🔴 Red-proof: write the dip's two durations swapped ⇒ the equality fails.</summary>
    [Fact]
    public void TE2_AV2Take_ReadsKeysAndSegments_AndRoundTrips()
    {
        var problems = new List<string>();
        var take = TakeJson.ParseTake(JsonNode.Parse(V2Take)!.AsObject(), "s", "main", problems);
        Assert.Empty(problems);
        Assert.Empty(take.Shots);
        Assert.Equal(3, take.Keys.Count);
        Assert.Equal(KeyTransition.Cut, take.Keys[0].In);
        Assert.Equal(new AtExpr(-5, "fire"), take.Keys[0].At);
        Assert.Equal(new Vector3(105, 75, 1), take.Keys[0].Camera.LookAt);
        Assert.Equal(KeyTransition.Fly(2, Ease.In), take.Keys[1].In);
        Assert.Equal("crossing", take.Keys[1].Still);
        Assert.Equal("Tank 1", take.Keys[1].Camera.Over);
        Assert.Equal(KeyTransition.Dip(0.4, 0.6), take.Keys[2].In);
        Assert.Equal(50f, take.Keys[2].Fov);
        Assert.Equal(2, take.Segments.Count);
        Assert.Equal(new TimeSegment { From = new AtExpr(0), To = new AtExpr(-5, "fire") }, take.Segments[0]);
        Assert.Equal(new TimeSegment { From = new AtExpr(-1, "fire"), To = new AtExpr(3, "fire"), Speed = 0.25, Gap = SegmentGap.Dip }, take.Segments[1]);

        var written = TakeJson.WriteTake(take);
        var again = TakeJson.ParseTake(written, "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(take.Keys, again.Keys);
        Assert.Equal(take.Segments, again.Segments);
        Assert.Equal(written.ToJsonString(), TakeJson.WriteTake(again).ToJsonString());
    }

    /// <summary>T1 — an unknown field in a key, in a segment or in a key's <c>in</c> is a problem naming it, never silently dropped.
    /// 🔴 Red-proof: drop the unknown-key check of <c>ParseSegment</c> ⇒ the segment assertion goes red.</summary>
    [Fact]
    public void TE2_AnUnknownField_InAKeyOrASegment_IsReported()
    {
        var bad = JsonNode.Parse("""
            {"keys":[{"at":1,"follow":"A"},{"at":2,"follow":"A","fly":3},{"at":3,"follow":"A","in":{"fly":1,"spin":2}}],
             "segments":[{"from":0,"to":5},{"from":1,"to":2,"rate":3}]}
            """)!.AsObject();
        var problems = new List<string>();
        TakeJson.ParseTake(bad, "s", "x", problems);
        Assert.Contains("key 2: unknown key 'fly'", problems);
        Assert.Contains("key 3: in has unknown key 'spin'", problems);
        Assert.Contains("segment 2: unknown key 'rate'", problems);
        Assert.DoesNotContain(problems, p => p.StartsWith("key 1") || p.StartsWith("segment 1"));
    }

    /// <summary>T1 (TE-2) — a take with only <c>shots[]</c> reads as keys: a plain shot a CUT key at its <c>at</c>; a shot with <c>to</c> also a
    /// FLY key at its end carrying the <c>to</c> values; a replay shot none. 🔴 Red-proof: place the fly key at <c>At</c> instead of
    /// <c>At + Seconds</c> ⇒ red.</summary>
    [Fact]
    public void TE2_ShotsRead_AsKeys_AShotACutKey_ItsToAFlyKey_AReplayNone()
    {
        var take = new Take
        {
            Shots = new[]
            {
                Shot("""{"at":2,"seconds":3,"follow":"A","distance":40,"fov":50}"""),
                Shot("""{"at":"fire-1","seconds":4,"lookAt":[1,2,3],"distance":100,"yaw":10,"to":{"distance":60,"fov":30},"ease":"linear"}"""),
                Shot("""{"at":20,"seconds":5,"follow":"A","replay":{"from":1}}"""),
                Shot("""{"at":30,"seconds":1,"follow":"B"}"""),
            },
        };
        var keys = CameraKeys.Of(take);
        Assert.Equal(4, keys.Count);

        Assert.Equal(new AtExpr(2), keys[0].At);
        Assert.Equal(KeyTransition.Cut, keys[0].In);
        Assert.Equal("A", keys[0].Camera.Follow);
        Assert.Equal(40f, keys[0].Camera.Distance);
        Assert.Equal(50f, keys[0].Fov);

        Assert.Equal(new AtExpr(-1, "fire"), keys[1].At);
        Assert.Equal(KeyTransition.Cut, keys[1].In);
        Assert.Equal(100f, keys[1].Camera.Distance);

        Assert.Equal(new AtExpr(3, "fire"), keys[2].At);                    // the shot's end: at -1 + 4 seconds, same anchor
        Assert.Equal(KeyTransition.Fly(4, Ease.Linear), keys[2].In);
        Assert.Equal(60f, keys[2].Camera.Distance);                          // `to` applied
        Assert.Equal(10f, keys[2].Camera.Yaw);                               // what `to` does not name is the shot's
        Assert.Equal(new Vector3(1, 2, 3), keys[2].Camera.LookAt);
        Assert.Equal(30f, keys[2].Fov);

        Assert.Equal("B", keys[3].Camera.Follow);                            // the replay shot (at 20) left no key
        Assert.DoesNotContain(keys, k => k.At.Equals(new AtExpr(20)));
    }

    /// <summary>T1 (TE-2) — when a take has keys of its own they are THE keys (its shots are not also read).</summary>
    [Fact]
    public void TE2_ATakesOwnKeys_WinOverItsShots()
    {
        var own = new CameraKey { At = new AtExpr(7), Camera = new CameraSpec { Follow = "K" } };
        var take = new Take { Keys = new[] { own }, Shots = new[] { Shot("""{"at":2,"seconds":3,"follow":"A"}""") } };
        Assert.Same(take.Keys, CameraKeys.Of(take));
    }

    /// <summary>
    /// T1 (R2's acceptance: "every seeded take loads and lays out as before") — all 55 seeded takes still read with no problem, give at least one
    /// key per non-replay shot (a fly key more for each <c>to</c>), and write back WITHOUT <c>keys</c> / <c>segments</c> (they stay v1 until
    /// edited, T-L4). 🔴 Red-proof: have <c>WriteTake</c> always write <c>keys</c> ⇒ red.
    /// </summary>
    [Fact]
    public void TE2_TheSeededTakes_StillRead_GiveKeys_AndWriteBackAsV1()
    {
        var repo = RepoRoot();
        var roots = new[] { Path.Combine(repo, "scenarios"), Path.Combine(repo, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios") };
        int count = 0, withTo = 0;
        foreach (var root in roots)
        {
            var store = new TakeStore(() => root, () => Path.Combine(repo, "films"));
            foreach (var (scenario, takes) in store.AllTakes())
                foreach (var t in takes)
                {
                    var loaded = store.Load(scenario, t);
                    Assert.True(loaded.Ok, $"{scenario}/{t}: {string.Join("; ", loaded.Problems)}");
                    var take = loaded.Value!;
                    var keys = CameraKeys.Of(take);
                    int plain = take.Shots.Count(s => s.Replay == null), flies = take.Shots.Count(s => s.Replay == null && s.To is { Count: > 0 });
                    withTo += flies;
                    Assert.True(keys.Count >= plain, $"{scenario}/{t}: {keys.Count} keys for {plain} shots");
                    Assert.Equal(plain + flies, keys.Count);
                    Assert.Equal(flies, keys.Count(k => k.In.Kind == KeyTransitionKind.Fly));
                    Assert.Empty(take.Keys);
                    Assert.Empty(take.Segments);
                    var back = TakeJson.WriteTake(take);
                    Assert.False(back.ContainsKey("keys"), $"{scenario}/{t} wrote keys");
                    Assert.False(back.ContainsKey("segments"), $"{scenario}/{t} wrote segments");
                    count++;
                }
        }
        Assert.True(count >= 55, $"{count} seeded takes");
        Assert.True(withTo > 0, "no seeded take has a `to` — the fly derivation went unexercised");
    }

    private static CameraKey Key(AtExpr at, string follow, KeyTransition? @in = null) =>
        new() { At = at, Camera = new CameraSpec { Follow = follow }, In = @in ?? KeyTransition.Cut };

    /// <summary>
    /// T1 (TE-8) — <c>ProgramAt</c>: null before the first key; on a cut key that key, whole; part-way through a fly the key before it and the
    /// eased blend (⭐ T4: a fly ARRIVES at its key's time, over the seconds before it); inside a dip's out-window the PREVIOUS key still, with the black rising; and a key whose anchor has no time is ignored.
    /// 🔴 Red-proof: ignore the key's own <c>OutSeconds</c> window (fade only after the key) ⇒ the out-window row goes red.
    /// </summary>
    [Fact]
    public void T1_ProgramAt_CutFlyDip_AndAnUnresolvedKeyIsSkipped()
    {
        var k0 = Key(new AtExpr(0), "A");
        var ghost = Key(new AtExpr(0, "never"), "G");                                  // its anchor has no time
        var k1 = Key(new AtExpr(10), "B", KeyTransition.Fly(4, Ease.Linear));
        var k2 = Key(new AtExpr(20), "C", KeyTransition.Dip(2, 3));
        var k3 = Key(new AtExpr(1, "fire"), "D", KeyTransition.Fly(2, Ease.In));        // fire = 30 ⇒ at 31
        var keys = new[] { k0, ghost, k1, k2, k3 };
        double? Anchors(string a) => a == "fire" ? 30d : null;

        Assert.Null(TakeTimeline.ProgramAt(keys, Anchors, -1));                         // before the first key
        Assert.Null(TakeTimeline.ProgramAt(Array.Empty<CameraKey>(), Anchors, 5));

        var onCut = TakeTimeline.ProgramAt(keys, Anchors, 0)!;                          // exactly on a cut key
        Assert.Same(k0, onCut.Key);
        Assert.Null(onCut.From);
        Assert.Equal(1f, onCut.Blend);
        Assert.Equal(0f, onCut.Fade);
        Assert.Same(k0, TakeTimeline.ProgramAt(keys, Anchors, 6)!.Key);                 // the ghost never takes the screen

        var half = TakeTimeline.ProgramAt(keys, Anchors, 8)!;                           // halfway through a linear 4 s fly that ARRIVES at 10
        Assert.Same(k1, half.Key);                                                      // the key it flies INTO
        Assert.Same(k0, half.From);                                                     // the previous RESOLVED key, not the ghost
        Assert.Equal(0.5f, half.Blend, 1e-5f);
        Assert.Equal(0f, half.Fade);
        var after = TakeTimeline.ProgramAt(keys, Anchors, 15)!;
        Assert.Same(k1, after.Key);
        Assert.Null(after.From);
        Assert.Equal(1f, after.Blend);

        var outWindow = TakeTimeline.ProgramAt(keys, Anchors, 19)!;                     // 1 s into the dip's 2 s out-window
        Assert.Same(k1, outWindow.Key);                                                 // the previous key is still on screen
        Assert.InRange(outWindow.Fade, 0.0001f, 0.9999f);
        Assert.Equal(0.5f, outWindow.Fade, 1e-5f);
        Assert.Equal(0f, TakeTimeline.ProgramAt(keys, Anchors, 17.5)!.Fade);            // before the window: no black

        var onDip = TakeTimeline.ProgramAt(keys, Anchors, 20)!;                         // black at the key, then it clears over 3 s
        Assert.Same(k2, onDip.Key);
        Assert.Equal(1f, onDip.Fade);
        Assert.Equal(0.5f, TakeTimeline.ProgramAt(keys, Anchors, 21.5)!.Fade, 1e-5f);
        Assert.Equal(0f, TakeTimeline.ProgramAt(keys, Anchors, 23)!.Fade);

        var anchored = TakeTimeline.ProgramAt(keys, Anchors, 30)!;                      // an anchored key (31), eased In, a 2 s fly: u = 0.5 ⇒ 0.25
        Assert.Same(k3, anchored.Key);
        Assert.Same(k2, anchored.From);
        Assert.Equal(0.25f, anchored.Blend, 1e-5f);
    }

    // ── CE-1364 T4 — the runner plays the take's KEYS ───────────────────────────────────────────────────

    private static FilmShot? Played(IReadOnlyList<CameraKey> keys, Func<string, double?> anchors, double t, IShotWorld world)
        => TakeTimeline.ProgramAt(keys, anchors, t) is { } f ? ShotEvaluator.Evaluate(f, world) : null;

    /// <summary>
    /// T4 — Cut@0 (camera A), Fly@4 (camera B, 2 s, ease In): A at t=1, the eased in-between at t=3 (u = 0.5 ⇒ eased 0.25), B from t=4 on.
    /// 🔴 Red-proof: make the fly start AT the key (blend after its time, as T1 first had it) ⇒ t=3 shows A and t=5 an in-between ⇒ red.
    /// </summary>
    [Fact]
    public void CE1364_T4_ACutThenAFly_PlaysAThenTheEasedInBetweenThenB()
    {
        var a = new CameraSpec { LookAt = new Vector3(0, 0, 0), Distance = 10f, Yaw = 0f };
        var b = new CameraSpec { LookAt = new Vector3(0, 0, 0), Distance = 50f, Yaw = 40f };
        var keys = new[]
        {
            new CameraKey { At = new AtExpr(0), Camera = a, In = KeyTransition.Cut },
            new CameraKey { At = new AtExpr(4), Camera = b, In = KeyTransition.Fly(2, Ease.In) },
        };
        var world = new Units();
        double? None(string _) => null;

        Assert.Equal(10f, Played(keys, None, 1, world)!.Pose!.Value.Distance);
        Assert.Equal(10f, Played(keys, None, 2, world)!.Pose!.Value.Distance);                 // the fly has not begun
        var mid = Played(keys, None, 3, world)!;
        Assert.Equal(10f + 40f * 0.25f, mid.Pose!.Value.Distance, 1e-3f);                       // eased In: 0.5² = 0.25
        Assert.Equal(40f * 0.25f * MathF.PI / 180f, mid.Pose.Value.Yaw, 1e-4f);
        Assert.Equal(50f, Played(keys, None, 4, world)!.Pose!.Value.Distance);
        Assert.Equal(50f, Played(keys, None, 9, world)!.Pose!.Value.Distance);
    }

    /// <summary>
    /// T4 — a DIP key: the black is 0 well before, rises over the out-seconds ahead of the key (the old camera still on screen), is full at the key
    /// and clears over the in-seconds after it. 🔴 Red-proof: drop the in-seconds clear ⇒ the row 1 s after the key stays 1 ⇒ red.
    /// </summary>
    [Fact]
    public void CE1364_T4_ADipKey_FadesOutBeforeItsTime_AndInAfter()
    {
        var keys = new[]
        {
            new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { LookAt = new Vector3(0, 0, 0), Distance = 10f } },
            new CameraKey { At = new AtExpr(5), Camera = new CameraSpec { LookAt = new Vector3(9, 9, 0), Distance = 20f }, In = KeyTransition.Dip(1, 2) },
        };
        double? None(string _) => null;
        Assert.Equal(0f, TakeTimeline.ProgramAt(keys, None, 3.9)!.Fade);
        var rising = TakeTimeline.ProgramAt(keys, None, 4.5)!;
        Assert.Equal(0.5f, rising.Fade, 1e-5f);
        Assert.Equal(10f, ShotEvaluator.Evaluate(rising, new Units())!.Pose!.Value.Distance);   // the old camera still on screen
        Assert.Equal(1f, TakeTimeline.ProgramAt(keys, None, 5)!.Fade);
        Assert.Equal(0.5f, TakeTimeline.ProgramAt(keys, None, 6)!.Fade, 1e-5f);
        Assert.Equal(0f, TakeTimeline.ProgramAt(keys, None, 7)!.Fade);
    }

    /// <summary>
    /// T4 — ⭐ ONE path: every seeded take (all 55) plus a hand-made one with <c>to</c>, an orbit and a lens, played through
    /// <c>ProgramAt(CameraKeys.Played(take, plan))</c> + <c>ShotEvaluator.Evaluate(frame)</c>, gives the SAME film shot as the old per-shot path
    /// (<c>Plan</c> → shot at / next → <c>Evaluate(shot, t - start)</c>), sampled every 0.25 s with a gap hold ahead of each shot's start.
    /// 🔴 Red-proof: have <c>Blend</c> take the arriving camera's <c>Yaw</c> verbatim (no lerp) ⇒ a <c>to</c> take differs ⇒ red.
    /// </summary>
    [Fact]
    public void CE1364_T4_AV1Take_PlaysTheSameShots_AsTheOldShotPath()
    {
        var repo = RepoRoot();
        var roots = new[] { Path.Combine(repo, "scenarios"), Path.Combine(repo, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios") };
        var takes = new List<(string Name, Take Take)>();
        foreach (var root in roots)
        {
            var store = new TakeStore(() => root, () => Path.Combine(repo, "films"));
            foreach (var (scenario, names) in store.AllTakes())
                foreach (var n in names) takes.Add(($"{scenario}/{n}", store.Load(scenario, n).Value!));
        }
        takes.Add(("hand-made", new Take
        {
            Shots = new[]
            {
                Shot("""{"at":1,"seconds":3,"follow":"A","distance":12,"yaw":10,"orbit":5,"fov":50,"to":{"distance":30,"yaw":90,"fov":30},"ease":"in"}"""),
                Shot("""{"at":6,"seconds":2,"lookAt":[1,2,3],"distance":20,"to":{"pitch":-60},"ease":"linear"}"""),
                Shot("""{"at":8,"seconds":2,"eye":[0,0,5],"target":[10,10,0]}"""),
            },
        }));
        Assert.True(takes.Count >= 56);

        var units = new HashUnits();
        int compared = 0;
        foreach (var (name, take) in takes)
        {
            // the take's own scouted times when it has them (the real layout); else a stable time for every anchor name
            var baked = take.Baked?.Times;
            Func<string, double?> anchors = baked != null ? a => baked.TryGetValue(a, out var bt) ? bt : null : a => 5d + 3d * (a.Sum(c => c) % 7);
            var plan = TakeTimeline.Plan(take, anchors);
            var keys = CameraKeys.Played(take, plan);
            for (double t = 0; t <= plan.End + 1; t += 0.25)
            {
                var shot = plan.At(t);
                var target = shot ?? plan.Next(t);
                if (target == null) continue;
                var expected = ShotEvaluator.Evaluate(target.Shot, Math.Max(0d, t - target.Start), units);
                double at = shot != null ? t : target.Start;
                var frame = TakeTimeline.ProgramAt(keys, anchors, at);
                var actual = frame is null ? null : ShotEvaluator.Evaluate(frame, units);
                Assert.True(Equals(expected, actual), $"{name} at t={t}: old {expected} vs keys {actual}");
                compared++;
            }
        }
        Assert.True(compared > 500, $"{compared} samples");
    }

    private sealed class HashUnits : IShotWorld
    {
        public long? Resolve(string who) => long.TryParse(who, out var id) ? id : 1 + Math.Abs(who.Sum(c => c));
        public Vector3? PositionOf(long id) => new Vector3(id * 2, id % 5, 0);
        public IReadOnlyCollection<string> Names => Array.Empty<string>();
    }

    /// <summary>
    /// T4 — a v2 take with KEYS and no shots still has a film to run: <c>Plan</c> lays one stretch per placed key (to the next key, the last to
    /// the take's end); a key with no anchor time is named, not laid out. 🔴 Red-proof: have <c>Plan</c> read only <c>take.Shots</c> ⇒ no stretches ⇒ red.
    /// </summary>
    [Fact]
    public void CE1364_T4_AKeysOnlyTake_LaysOutOneStretchPerKey()
    {
        var take = new Take
        {
            EndAt = new AtExpr(9),
            Keys = new[]
            {
                new CameraKey { At = new AtExpr(2), Camera = new CameraSpec { Follow = "A" } },
                new CameraKey { At = new AtExpr(0, "never"), Camera = new CameraSpec { Follow = "G" } },
                new CameraKey { At = new AtExpr(5), Camera = new CameraSpec { Follow = "B" }, In = KeyTransition.Fly(2) },
            },
        };
        var plan = TakeTimeline.Plan(take, _ => null);
        Assert.Equal(new[] { (2d, 5d), (5d, 9d) }, plan.Shots.Select(s => (s.Start, s.End)).ToArray());
        Assert.Equal(new[] { "key 2: anchor 'never'" }, plan.Unresolved);
        Assert.Equal(9d, plan.End);
    }

    // ── CE-1303 P2b (docs/DESIGN_Director_Timeline_Editor.md §11o) — the film map and the subtitles, as pure rules ──────────────────────

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-7 (§11o S1): <see cref="FilmTimeMap"/> lays the segments end to end in FILM time — [0–2 @1×] film 0–2, [2–4 @0.5×]
    /// film 2–6 (two source seconds at half speed are four film seconds), [1–2 @1×] film 6–7 marked BACK (a replay) — and maps both ways: film 3 is
    /// source 2.5, film 6.5 source 1.5; source 3 is film 4 (the first segment holding it), a skipped source second the next segment's start; a take
    /// with no segments is the identity. A dip gap's black rises before the dip segment and clears after it. 🔴 Mutation: <c>FilmSeconds</c> as
    /// <c>(To − From)</c> (the speed ignored) ⇒ film 3 maps to source 3 and the film ends at 5 ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_TheFilmTimeMap_PlacesSegmentsEndToEnd_SlowAndBack()
    {
        var take = new Take
        {
            Segments = new[]
            {
                new TimeSegment { From = new AtExpr(0), To = new AtExpr(2) },
                new TimeSegment { From = new AtExpr(2), To = new AtExpr(4), Speed = 0.5 },
                new TimeSegment { From = new AtExpr(1), To = new AtExpr(2), Gap = SegmentGap.Dip },
                new TimeSegment { From = new AtExpr(0, "never"), To = new AtExpr(9) },
            },
        };
        var map = FilmTimeMap.Of(take, _ => null);
        Assert.False(map.IsIdentity);
        Assert.Equal(new[] { (0d, 2d, false), (2d, 6d, false), (6d, 7d, true) }, map.Segments.Select(s => (s.FilmStart, s.FilmEnd, s.Back)).ToArray());
        Assert.Equal(new[] { "segment 4: anchor 'never'" }, map.Unresolved);
        Assert.Equal(7d, map.FilmEnd);
        Assert.Equal((0, 1.0), map.At(1.0));
        Assert.Equal((1, 2.5), map.At(3.0));
        Assert.Equal((2, 1.5), map.At(6.5));
        Assert.Null(map.At(7.0));
        Assert.Equal(4.0, map.FilmOf(3.0));
        Assert.Equal(1.0, map.FilmOf(1.0));                   // the FIRST segment holding it, not the replay
        Assert.Equal(6.5, map.FilmOf(1.5, fromSegment: 2));   // … unless asked from the replay on
        Assert.Equal(7.0, map.FilmOf(5.0));                   // past every segment: the film's end
        Assert.Equal(0f, map.GapFade(5.0));
        Assert.Equal(1f, map.GapFade(6.0));                   // the dip's black at the cut
        Assert.InRange(map.GapFade(5.8), 0.49f, 0.51f);       // half-way down, 0.2 s before
        Assert.InRange(map.GapFade(6.2), 0.49f, 0.51f);       // half-way up, 0.2 s after

        var none = FilmTimeMap.Of(new Take(), _ => null);
        Assert.True(none.IsIdentity);
        Assert.Equal((-1, 3.25), none.At(3.25));
        Assert.Equal(3.25, none.FilmOf(3.25));
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-11 (§5, §11o): a take's <c>subtitles[]</c> read (text, at / until — a time or an anchor, position, fade), write
    /// and read back equal; a bad position or a missing until is a problem naming it; a take WITHOUT subtitles writes no <c>subtitles</c> key — every
    /// seeded take reads with none and writes back without the key (old takes load unchanged). 🔴 Mutation: <c>WriteSubtitle</c> not writing
    /// <c>position</c> ⇒ the round-trip differs ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_Subtitles_RoundTrip_AndOldTakesStayWithout()
    {
        var json = JsonNode.Parse("""
            {"anchors":{"fire":{"on":"shot","who":"Tank 1","n":1}},
             "subtitles":[{"at":"fire-4","until":"fire","text":"The platoon crosses phase line BLUE"},
                          {"at":2,"until":5.5,"text":"Top","position":"top","fade":0.3}]}
            """)!.AsObject();
        var problems = new List<string>();
        var take = TakeJson.ParseTake(json, "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(new Subtitle { At = new AtExpr(-4, "fire"), Until = new AtExpr(0, "fire"), Text = "The platoon crosses phase line BLUE" }, take.Subtitles[0]);
        Assert.Equal(new Subtitle { At = new AtExpr(2), Until = new AtExpr(5.5), Text = "Top", Position = SubtitlePosition.Top, Fade = 0.3 }, take.Subtitles[1]);
        var written = TakeJson.WriteTake(take);
        var again = TakeJson.ParseTake(written, "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(take.Subtitles, again.Subtitles);
        Assert.Equal(written.ToJsonString(), TakeJson.WriteTake(again).ToJsonString());

        TakeJson.ParseTake(JsonNode.Parse("""{"subtitles":[{"at":1,"text":"x","position":"left","colour":1}]}""")!.AsObject(), "s", "bad", problems);
        Assert.Contains("subtitle 1: needs until", problems);
        Assert.Contains("subtitle 1: position 'left' (bottom, top, middle)", problems);
        Assert.Contains("subtitle 1: unknown key 'colour'", problems);

        var repo = RepoRoot();
        int count = 0;
        foreach (var root in new[] { Path.Combine(repo, "scenarios"), Path.Combine(repo, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios") })
        {
            var store = new TakeStore(() => root, () => Path.Combine(repo, "films"));
            foreach (var (scenario, names) in store.AllTakes())
                foreach (var n in names)
                {
                    var loaded = store.Load(scenario, n);
                    Assert.True(loaded.Ok, $"{scenario}/{n}: {string.Join("; ", loaded.Problems)}");
                    Assert.Empty(loaded.Value!.Subtitles);
                    Assert.False(TakeJson.WriteTake(loaded.Value).ContainsKey("subtitles"), $"{scenario}/{n} wrote subtitles");
                    count++;
                }
        }
        Assert.True(count >= 55, $"{count} seeded takes");
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> / CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11q K1, success condition 2): a key that belongs to a SEGMENT round-trips — the segment's
    /// <c>id</c> and the key's <c>segment</c> are read, written and read again unchanged — a key naming a segment the take does not have, and an id used twice, are
    /// named problems; and the 55 seeded takes name no segment and write none, and the per-segment lookup hands each its OWN key list back untouched (they play as
    /// before). 🔴 Mutation: <c>WriteKey</c> not writing <c>segment</c> ⇒ the read-back key is unscoped ⇒ red.
    /// </summary>
    [Fact]
    public void CE4059_CR7_AKeyOfASegment_RoundTrips_AndTheSeededTakesStayUnscoped()
    {
        var json = JsonNode.Parse("""
            {"segments":[{"id":"main","from":0,"to":12},{"id":"replay","from":9,"to":11.5,"speed":0.4}],
             "keys":[{"at":2,"lookAt":[1,2,3]},{"at":9,"lookAt":[4,5,6],"segment":"replay"}]}
            """)!.AsObject();
        var problems = new List<string>();
        var take = TakeJson.ParseTake(json, "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(new[] { "main", "replay" }, take.Segments.Select(s => s.Id));
        Assert.Null(take.Keys[0].Segment);
        Assert.Equal("replay", take.Keys[1].Segment);
        var written = TakeJson.WriteTake(take);
        Assert.Equal("replay", written["keys"]![1]!["segment"]!.GetValue<string>());
        Assert.False(written["keys"]![0]!.AsObject().ContainsKey("segment"));            // an unscoped key writes nothing new
        var again = TakeJson.ParseTake(written, "s", "main", problems);
        Assert.Empty(problems);
        Assert.Equal(take.Keys, again.Keys);
        Assert.Equal(take.Segments, again.Segments);
        Assert.Equal(written.ToJsonString(), TakeJson.WriteTake(again).ToJsonString());

        TakeJson.ParseTake(JsonNode.Parse("""
            {"segments":[{"id":"a","from":0,"to":1},{"id":"a","from":1,"to":2}],"keys":[{"at":0,"lookAt":[0,0,0],"segment":"b"}]}
            """)!.AsObject(), "s", "bad", problems);
        Assert.Contains("segment id 'a' is used twice", problems);
        Assert.Contains("key 1: segment 'b' is not a segment of the take", problems);

        var repo = RepoRoot();
        int count = 0;
        foreach (var root in new[] { Path.Combine(repo, "scenarios"), Path.Combine(repo, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios") })
        {
            var store = new TakeStore(() => root, () => Path.Combine(repo, "films"));
            foreach (var (scenario, names) in store.AllTakes())
                foreach (var n in names)
                {
                    var loaded = store.Load(scenario, n).Value!;
                    Assert.All(CameraKeys.Of(loaded), k => Assert.Null(k.Segment));
                    Assert.All(loaded.Segments, s => Assert.Null(s.Id));
                    var keys = CameraKeys.Of(loaded);
                    Assert.Same(keys, CameraKeys.OnScreenIn(keys, null, _ => 0d));            // ⭐ the lookup hands an unscoped take its own list
                    Assert.DoesNotContain("\"segment\"", TakeJson.WriteTake(loaded).ToJsonString());
                    count++;
                }
        }
        Assert.True(count >= 55, $"{count} seeded takes");
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-11 (§11o S8): a take with no subtitles has its CAPTIONS read as subtitles — over every seeded take (55) the subtitle at
    /// each 0.25 s is exactly the caption the runner showed before subtitles existed (the shot on screen's <c>caption ?? take.caption</c>, none in a
    /// gap) — and a take's own subtitles win, with a fade's alpha rising from 0 at its start. 🔴 Mutation: <c>TakeSubtitles.Of</c> giving ONE
    /// subtitle over the whole take (<c>0 … plan.End</c>) instead of one per laid-out shot ⇒ the text shows before the first shot / in a gap ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_TheCaptions_ReadAsSubtitles_AreWhatPlayedBefore()
    {
        var repo = RepoRoot();
        int compared = 0, withText = 0;
        foreach (var root in new[] { Path.Combine(repo, "scenarios"), Path.Combine(repo, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios") })
        {
            var store = new TakeStore(() => root, () => Path.Combine(repo, "films"));
            foreach (var (scenario, names) in store.AllTakes())
                foreach (var n in names)
                {
                    var take = store.Load(scenario, n).Value!;
                    var baked = take.Baked?.Times;
                    Func<string, double?> anchors = baked != null ? a => baked.TryGetValue(a, out var bt) ? bt : null : a => 5d + 3d * (a.Sum(c => c) % 7);
                    var plan = TakeTimeline.Plan(take, anchors);
                    var subs = TakeSubtitles.Of(take, plan);
                    for (double t = 0; t <= plan.End + 1; t += 0.25)
                    {
                        var shot = plan.At(t);
                        string? before = shot == null ? null : shot.Shot.Caption ?? take.Caption;
                        if (string.IsNullOrWhiteSpace(before)) before = null;
                        Assert.Equal(before, TakeSubtitles.At(subs, anchors, t)?.Text);
                        if (before != null) withText++;
                        compared++;
                    }
                }
        }
        Assert.True(withText > 50, $"only {withText} captioned samples — the seeded takes' captions went unexercised");
        Assert.True(compared > 500, $"{compared} samples");

        var own = new Take { Caption = "old", Subtitles = new[] { new Subtitle { At = new AtExpr(1), Until = new AtExpr(3), Text = "new", Fade = 0.5 } } };
        var sub = TakeSubtitles.At(TakeSubtitles.Of(own, TakeTimeline.Plan(own, _ => null)), _ => null, 1.25);
        Assert.Equal(("new", SubtitlePosition.Bottom), (sub!.Text, sub.Position));
        Assert.InRange(sub.Alpha, 0.49f, 0.51f);
        Assert.Null(TakeSubtitles.At(own.Subtitles, _ => null, 3.0));   // until is exclusive
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("no repo root above " + AppContext.BaseDirectory);
    }
}
