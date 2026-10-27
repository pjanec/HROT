using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-4059</c> CR-8 (docs/DESIGN_Director_Timeline_Editor.md §11n) — the <see cref="Drafter"/>: a moment of a recording in, a take that films it out.
/// The index is built by hand: frames every 0.1 sim s, <c>Alpha</c> walking east at 2 m/s, <c>Bravo</c> walking north at 4 m/s from the moment of the shot (exactly
/// <c>gap</c> metres east of Alpha then), a position sample every 0.5 s.
/// </summary>
public sealed class DrafterTests
{
    private const long Alpha = 1, Bravo = 2;
    private const string Shot = "combat.shot", Hit = "combat.hit", Blast = "combat.blast";
    private const double Dt = 0.1;

    private static string? NameOf(long id) => id switch { Alpha => "Alpha", Bravo => "Bravo", _ => null };

    private static int FrameOf(double sim) => (int)Math.Round(sim / Dt);

    private static TimelineEvent ShotAt(double sim) => new(FrameOf(sim), Shot, Alpha, Bravo, 0f, 0f, 1f);
    private static TimelineEvent HitAt(double sim) => new(FrameOf(sim), Hit, Bravo, 0, 0f, 0f, 0f);
    private static TimelineEvent BlastAt(double sim, float damage) => new(FrameOf(sim), Blast, Alpha, Bravo, 0f, 0f, damage);

    /// <summary>The recording: <paramref name="events"/>, 25 sim seconds; the units walk as the class remarks say, <paramref name="gap"/> metres apart at <paramref name="anchorSim"/>.</summary>
    private static TimelineIndex Index(double anchorSim, double gap, params TimelineEvent[] events)
    {
        int frames = FrameOf(25d) + 1;
        var lanes = events.GroupBy(e => e.Lane).ToDictionary(g => g.Key, g => new SharedList<TimelineEvent>(g.OrderBy(e => e.Frame).ToArray()));
        var ticks = Enumerable.Range(0, frames).Select(f => (long)f * 1_000_000).ToArray();
        var sim = Enumerable.Range(0, frames).Select(f => f * Dt).ToArray();
        var alpha = new List<PositionSample>();
        var bravo = new List<PositionSample>();
        for (int f = 0; f < frames; f += 5)
        {
            double s = f * Dt;
            alpha.Add(new PositionSample(f, (float)(2 * s), 0f, 0f, 0f));
            bravo.Add(new PositionSample(f, (float)(2 * anchorSim + gap), (float)(4 * (s - anchorSim)), 0f, MathF.PI / 2));
        }
        var positions = new PositionTrack(new Dictionary<long, PositionSample[]> { [Alpha] = alpha.ToArray(), [Bravo] = bravo.ToArray() });
        return new TimelineIndex(lanes, new SharedList<long>(ticks), frames, frames, null, new SharedList<double>(sim), positions);
    }

    /// <summary>Alpha shoots Bravo at 10 s, 60 m east of Alpha; Bravo is hit 0.8 s later.</summary>
    private static TimelineIndex ShotThenHit(double gap = 60d) => Index(10d, gap, ShotAt(10d), HitAt(10.8));

    private static DraftMoment TheShot(TimelineIndex index) => Drafter.Moments(index, NameOf).Single(m => m.Lane == Shot);

    private static Take Draft(TimelineIndex index, DraftOptions? options = null, DraftMoment? moment = null)
        => Drafter.Draft(index, moment ?? TheShot(index), NameOf, options ?? new DraftOptions { Scenario = "sc" });

    private static double? Resolve(Take take, AtExpr at) => at.Resolve(n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null);

    /// <summary>The way the key's camera looks, on which side of the line actor → subject at the moment: sign of cross(line, look direction); + = to the line's left.</summary>
    private static int SideOf(TimelineIndex index, double sim, CameraKey key)
    {
        int frame = FrameOf(sim);
        var a = index.PositionAt(Alpha, frame)!.Value;
        var s = index.PositionAt(Bravo, frame)!.Value;
        double yaw = key.Camera.Yaw * Math.PI / 180d;
        double cross = (s.X - a.X) * Math.Sin(yaw) - (s.Y - a.Y) * Math.Cos(yaw);
        return Math.Sign(cross);
    }

    /// <summary>The drafted take has FOUR keys in time order at t−8, t−4, t−0.7 and t+0.8−0.3 (the hit pulls ④ in), each an offset on the SHOT's anchor name, and the
    /// shot's sim time is baked. 🔴 Mutation: <c>MomentBefore</c> 0.7 → 0.5 ⇒ the third offset assertion fails.</summary>
    [Fact]
    public void CE4059_CR8_TheDraft_HasFourKeysInTimeOrder_AnchoredOnTheShot_AndBakesItsTime()
    {
        var index = ShotThenHit();
        var take = Draft(index);

        Assert.Equal(4, take.Keys.Count);
        Assert.Equal(new[] { -8d, -4d, -0.7, 0.8 - 0.3 }, take.Keys.Select(k => k.At.Offset).ToArray(), new OffsetComparer());
        Assert.All(take.Keys, k => Assert.Equal("shot:Alpha#1", k.At.Anchor));
        Assert.Equal(new[] { "shot:Alpha#1" }, take.Anchors.Keys);
        Assert.Equal(("shot", "Alpha", 1), (take.Anchors["shot:Alpha#1"].On, take.Anchors["shot:Alpha#1"].By, take.Anchors["shot:Alpha#1"].N));
        Assert.Equal(10d, take.Baked!.Times["shot:Alpha#1"], 6);
        Assert.Equal(60, take.Baked.TickHz);
        var times = take.Keys.Select(k => Resolve(take, k.At)!.Value).ToArray();
        Assert.Equal(times.OrderBy(x => x), times);
        Assert.Equal(2d, times[0], 6);
        Assert.Equal(10.5, times[3], 6);
        Assert.Equal(10.5 + 4d, Resolve(take, take.EndAt!.Value)!.Value, 6);
        Assert.Empty(take.Segments);
    }

    /// <summary>With no hit on the subject after the moment, ④ is at t+2 (and lasts 4 s). 🔴 Mutation: <c>ConsequenceAfter</c> 2 → 3 ⇒ the fourth offset fails.</summary>
    [Fact]
    public void CE4059_CR8_WithNoHit_TheConsequence_StartsTwoSecondsAfterTheShot()
    {
        var take = Draft(Index(10d, 60d, ShotAt(10d)));

        Assert.Equal(2d, take.Keys[3].At.Offset, 6);
        Assert.Equal(6d, take.EndAt!.Value.Offset, 6);
    }

    /// <summary>①, ③ and ④ look from ONE side of the line actor → subject; the replay (asked for) is the one crossing — its key on the other side — and a segment goes back
    /// over [t−1, t+1.5] at speed 0.4, after the main pass. 🔴 Mutations: ④'s yaw +180 ⇒ the one-side assertion fails; the replay's +180 removed ⇒ the other-side assertion fails.</summary>
    [Fact]
    public void CE4059_CR8_TheLine_IsCrossedOnlyByTheReplay()
    {
        var index = ShotThenHit();
        var plain = Draft(index);
        var sides = new[] { 0, 2, 3 }.Select(i => SideOf(index, 10d, plain.Keys[i])).ToArray();
        Assert.NotEqual(0, sides[0]);
        Assert.All(sides, s => Assert.Equal(sides[0], s));
        Assert.Empty(plain.Segments);

        var replayed = Draft(index, new DraftOptions { Scenario = "sc", Replay = true });
        Assert.Equal(5, replayed.Keys.Count);   // ⭐ CE-4059 §11q: the fifth is the replay's own key (CE4059_CR7_TheReplaysReverseAngle_* — scope and time)
        Assert.Equal(sides, new[] { 0, 2, 3 }.Select(i => SideOf(index, 10d, replayed.Keys[i])).ToArray());
        Assert.Equal(-sides[0], SideOf(index, 10d, replayed.Keys[4]));
        Assert.Equal(2, replayed.Segments.Count);
        var back = replayed.Segments[1];
        Assert.Equal(0.4, back.Speed, 6);
        Assert.Equal(9d, Resolve(replayed, back.From)!.Value, 6);
        Assert.Equal(11.5, Resolve(replayed, back.To)!.Value, 6);
        Assert.Equal(Resolve(replayed, replayed.Segments[0].To)!.Value, Resolve(replayed, replayed.EndAt!.Value)!.Value, 6);
        Assert.Equal(Resolve(replayed, replayed.Keys[0].At)!.Value, Resolve(replayed, replayed.Segments[0].From)!.Value, 6);
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> / CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11q K5, success condition 3): the replay's reverse-angle key BELONGS TO the replay segment and
    /// sits at its REAL source time — the replay's first frame, t−1 = 9 s (⛔ SUPERSEDED: parked at ④'s end, 14.5 s, §11n as built) — on the moment's anchor; the two
    /// segments are named <c>main</c> and <c>replay</c>. Played through the per-segment lookup: at source 9.2 the main pass shows ② (the set-up — the reverse key is
    /// not its), the replay shows the reverse key; at 9.6 / 10.6 the main pass shows ③ / ④, the replay still the reverse key (③ and ④ do not cut it). 🔴 Mutation:
    /// the reverse key without <c>Segment = ReplaySegmentId</c> ⇒ the main pass at 9.2 shows it ⇒ red.
    /// </summary>
    [Fact]
    public void CE4059_CR7_TheReplaysReverseAngle_IsAKeyOfTheReplay_AtItsRealSourceTime()
    {
        var index = ShotThenHit();
        var take = Draft(index, new DraftOptions { Scenario = "sc", Replay = true });
        var reverse = take.Keys[4];
        Assert.Equal(Drafter.ReplaySegmentId, reverse.Segment);
        Assert.Equal(new AtExpr(-1d, "shot:Alpha#1"), reverse.At);
        Assert.Equal(9d, Resolve(take, reverse.At)!.Value, 6);
        Assert.Equal(new[] { Drafter.MainSegmentId, Drafter.ReplaySegmentId }, take.Segments.Select(s => s.Id));
        Assert.All(take.Keys.Take(4), k => Assert.Null(k.Segment));

        Func<string, double?> anchors = n => take.Baked!.Times.TryGetValue(n, out var t) ? t : null;
        CameraKey? On(string segment, double sim) => TakeTimeline.ProgramAt(CameraKeys.OnScreenIn(take.Keys, segment, anchors), anchors, sim)?.Key;
        Assert.Same(take.Keys[1], On(Drafter.MainSegmentId, 9.2));     // ② the set-up (6 s) — the reverse key (9 s) is not the main pass's
        Assert.Same(reverse, On(Drafter.ReplaySegmentId, 9.2));
        Assert.Same(take.Keys[2], On(Drafter.MainSegmentId, 9.6));     // ③ the moment (9.3 s)
        Assert.Same(reverse, On(Drafter.ReplaySegmentId, 9.6));         // … does not cut the replay
        Assert.Same(take.Keys[3], On(Drafter.MainSegmentId, 10.6));    // ④ the consequence (10.5 s)
        Assert.Same(reverse, On(Drafter.ReplaySegmentId, 10.6));
        Assert.Same(reverse, On(Drafter.ReplaySegmentId, 11.4));
    }

    /// <summary>③ frames BOTH units at every position sample of its window [t−0.7, t+2]: seen from the camera pose its LookAt / Distance / Yaw / Pitch imply, each stays
    /// inside the horizontal half-angle of its lens. 🔴 Mutation: the fitted distance halved ⇒ a unit leaves the frame.</summary>
    [Fact]
    public void CE4059_CR8_TheMoment_FramesBothUnitsAtEverySampleOfItsWindow()
    {
        var index = ShotThenHit();
        var options = new DraftOptions { Scenario = "sc" };
        var key = Draft(index, options).Keys[2];

        Assert.NotNull(key.Camera.LookAt);
        double yaw = key.Camera.Yaw * Math.PI / 180d, pitch = key.Camera.Pitch * Math.PI / 180d;
        var forward = new Vector3((float)(Math.Cos(pitch) * Math.Cos(yaw)), (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch));
        var right = new Vector3((float)Math.Sin(yaw), (float)-Math.Cos(yaw), 0f);
        var eye = key.Camera.LookAt!.Value - forward * key.Camera.Distance;
        double tanHalf = Math.Tan(key.Fov!.Value * Math.PI / 360d) * options.AspectRatio;

        int checkedSamples = 0;
        for (int f = FrameOf(9.3); f <= FrameOf(12d); f++)
            foreach (var unit in new[] { Alpha, Bravo })
            {
                var p = index.PositionAt(unit, f)!.Value;
                var v = new Vector3(p.X, p.Y, p.Z) - eye;
                double depth = Vector3.Dot(v, forward);
                Assert.True(depth > 0, $"unit {unit} at frame {f} is behind the camera");
                Assert.True(Math.Abs(Vector3.Dot(v, right)) / depth <= tanHalf, $"unit {unit} at frame {f} leaves the frame");
                checkedSamples++;
            }
        Assert.True(checkedSamples >= 50);
    }

    /// <summary>④ FOLLOWS the subject (the target, not the shooter) from above, pitch −35. 🔴 Mutation: ④ following the actor ⇒ the follow assertion fails.</summary>
    [Fact]
    public void CE4059_CR8_TheConsequence_FollowsTheSubject_FromAbove()
    {
        var key = Draft(ShotThenHit()).Keys[3];

        Assert.Equal("Bravo", key.Camera.Follow);
        Assert.Equal(-35f, key.Camera.Pitch);
    }

    /// <summary>With the units 150 m apart ③ is a telephoto: a FOLLOW of the subject with an 18° lens (on the key as well); at 60 m it is not. 🔴 Mutation: the 100 m limit raised
    /// to 200 ⇒ the follow assertion fails.</summary>
    [Fact]
    public void CE4059_CR8_PastOneHundredMetres_TheMoment_IsATelephotoFollowOfTheSubject()
    {
        var far = Draft(ShotThenHit(gap: 150d)).Keys[2];
        Assert.Equal("Bravo", far.Camera.Follow);
        Assert.Equal(18f, far.Camera.Fov);
        Assert.Equal(18f, far.Fov);
        Assert.InRange(far.Camera.Distance, 8f, 600f);

        var near = Draft(ShotThenHit(gap: 60d)).Keys[2];
        Assert.Null(near.Camera.Follow);
        Assert.NotNull(near.Camera.LookAt);
        Assert.Equal(45f, near.Fov);
    }

    /// <summary>A moment at t = 3 s cannot have its establish shot at t−8: ① is a FIXED time 0 (not anchored — nothing before the run's start to hang on) and runs ≥ 2 s
    /// before the next key. 🔴 Mutation: the set-up always kept ⇒ it also lands at 0 and ① lasts 0 s.</summary>
    [Fact]
    public void CE4059_CR8_AMomentAtThreeSeconds_StartsTheEstablishShotAtZero_AndHoldsItTwoSeconds()
    {
        var take = Draft(Index(3d, 60d, ShotAt(3d)));

        Assert.True(take.Keys[0].At.IsFixed);
        Assert.Equal(0d, take.Keys[0].At.Offset);
        var times = take.Keys.Select(k => Resolve(take, k.At)!.Value).ToArray();
        Assert.Equal(times.OrderBy(x => x), times);
        Assert.True(times[1] - times[0] >= 2d - 1e-9, $"the establish shot lasts {times[1] - times[0]} s");
        Assert.Equal(3d - 0.7, times[^2], 6);   // ③ is still cut in ahead of the shot
    }

    /// <summary>A blast scores above a first shot — even a harmless one (damage 0) — and a heavier blast above a lighter. 🔴 Mutations: the blast's base score 3 → 0.5 ⇒ the first comparison
    /// fails; the damage term dropped ⇒ the second fails.</summary>
    [Fact]
    public void CE4059_CR8_Moments_ScoreABlastAboveAFirstShot_AndAHeavierBlastAboveALighter()
    {
        var moments = Drafter.Moments(Index(3d, 60d, ShotAt(3d), BlastAt(15d, 0f), BlastAt(20d, 300f)), NameOf);

        Assert.Equal(3, moments.Count);
        var shot = moments.Single(m => m.Lane == Shot);
        var light = moments.Single(m => m.Lane == Blast && m.Frame == FrameOf(15d));
        var heavy = moments.Single(m => m.Lane == Blast && m.Frame == FrameOf(20d));
        Assert.True(light.Score > shot.Score);
        Assert.True(heavy.Score > light.Score);
        Assert.Equal((Alpha, (long?)Bravo), (light.Actor, light.Subject));
        Assert.Equal((Alpha, (long?)Bravo), (shot.Actor, shot.Subject));
        Assert.Equal((15d, 3d), (Math.Round(light.Sim, 6), Math.Round(shot.Sim, 6)));
    }

    /// <summary>A hit 0.8 s after a shot is part of the SHOT's moment, not a candidate of its own; a hit 4 s later is its own, and carries who hit it. Only the FIRST round
    /// of a shooter at a target is a candidate. 🔴 Mutation: the merge window 2 s → 0.5 s ⇒ two moments and the count fails.</summary>
    [Fact]
    public void CE4059_CR8_Moments_MergeAHitJustAfterAShot_IntoTheShotsMoment()
    {
        var merged = Drafter.Moments(Index(10d, 60d, ShotAt(10d), ShotAt(10.3), HitAt(10.8)), NameOf);
        var only = Assert.Single(merged);
        Assert.Equal((Shot, 10d), (only.Lane, Math.Round(only.Sim, 6)));
        Assert.True(only.Score >= 2d, "the merged moment keeps the better score");

        var apart = Drafter.Moments(Index(10d, 60d, ShotAt(10d), HitAt(14d)), NameOf);
        Assert.Equal(new[] { Shot, Hit }, apart.Select(m => m.Lane).ToArray());
        Assert.Equal((Alpha, (long?)Bravo), (apart[1].Actor, apart[1].Subject));
    }

    /// <summary>The camera's height follows the actor's kind: Person −8 · Vehicle −6 · Group −55 · Aircraft +10 · unknown −20 (③; ① stays −40). 🔴 Mutation: Person −8 → −18 ⇒ the
    /// first assertion fails.</summary>
    [Fact]
    public void CE4059_CR8_TheHeight_FollowsTheKindOfTheActor()
    {
        var index = ShotThenHit();
        float Pitch(SubjectKind? kind) => Draft(index, new DraftOptions { Scenario = "sc", Kind = kind is { } k ? _ => k : null }).Keys[2].Camera.Pitch;

        Assert.Equal(-8f, Pitch(SubjectKind.Person));
        Assert.Equal(-6f, Pitch(SubjectKind.Vehicle));
        Assert.Equal(-55f, Pitch(SubjectKind.Group));
        Assert.Equal(10f, Pitch(SubjectKind.Aircraft));
        Assert.Equal(-20f, Pitch(SubjectKind.Unknown));
        Assert.Equal(-20f, Pitch(null));
        Assert.Equal(-40f, Draft(index, new DraftOptions { Scenario = "sc", Kind = _ => SubjectKind.Person }).Keys[0].Camera.Pitch);
    }

    /// <summary>A moment the take's anchors cannot name (the shooter has no name here) still drafts: FIXED times, no anchor, no bake — four keys at the same instants.
    /// 🔴 Mutation: <c>Key</c> dereferencing the anchor without the null check ⇒ the draft throws.</summary>
    [Fact]
    public void CE4059_CR8_AMomentWithNoExpressibleAnchor_FallsBackToFixedTimes()
    {
        var index = ShotThenHit();
        var moment = new DraftMoment(Shot, 0, FrameOf(10d), 10d, Alpha, Bravo, 1d);
        var take = Drafter.Draft(index, moment, _ => null, new DraftOptions { Scenario = "sc" });

        Assert.Empty(take.Anchors);
        Assert.Null(take.Baked);
        Assert.All(take.Keys, k => Assert.True(k.At.IsFixed));
        Assert.Equal(new[] { 2d, 6d, 9.3 }, take.Keys.Take(3).Select(k => k.At.Offset).ToArray(), new OffsetComparer());
        Assert.Equal(4, take.Keys.Count);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1305</c> (docs/DESIGN_Director_Timeline_Editor.md §11v T1, success condition 2): each shot template IS the drafter's own camera for that beat — a
    /// template key at a drafted key's time equals that key's camera (establish, over the shoulder, the moment, the consequence, the replay's reverse angle),
    /// computed by the drafter's rule (<see cref="Drafter.TemplateCamera"/> vs <see cref="Drafter.Draft"/>, not a copy here); follow and top-down are framed on the
    /// same line (the moment's yaw), follow on the subject at the moment's height, top-down from −85°. 🔴 Mutation: <c>Drafter.HoldOf</c> answering 4 s for every
    /// template ⇒ the moment's and the reverse angle's windows differ from the draft's ⇒ their framing distance differs ⇒ red.
    /// </summary>
    [Fact]
    public void CE1305_EachShotTemplate_IsTheDraftersOwnCamera_ForThatBeat()
    {
        var index = ShotThenHit();
        var moment = TheShot(index);
        var options = new DraftOptions { Scenario = "sc", Replay = true };
        var take = Draft(index, options);
        var beats = new[] { ShotTemplate.Establish, ShotTemplate.OverTheShoulder, ShotTemplate.Moment, ShotTemplate.Consequence, ShotTemplate.Reverse };
        Assert.Equal(beats.Length, take.Keys.Count);
        for (int i = 0; i < beats.Length; i++)
        {
            var key = take.Keys[i];
            var template = Drafter.TemplateCamera(index, moment, NameOf, options, beats[i], Resolve(take, key.At)!.Value);
            Assert.True(key.Camera == template, $"{beats[i]}: the draft's key {key.Camera} vs the template {template}");
        }
        Assert.Equal(new CameraSpec { Over = "Alpha", Toward = "Bravo", Fov = 45f }, take.Keys[1].Camera);   // the over-the-shoulder beat, as K would see it

        var theMoment = take.Keys[2].Camera;
        var follow = Drafter.TemplateCamera(index, moment, NameOf, options, ShotTemplate.Follow, 9.3);
        Assert.Equal(("Bravo", theMoment.Yaw, theMoment.Pitch), (follow.Follow, follow.Yaw, follow.Pitch));
        var top = Drafter.TemplateCamera(index, moment, NameOf, options, ShotTemplate.TopDown, 9.3);
        Assert.Equal((theMoment.Yaw, -85f), (top.Yaw, top.Pitch));
        Assert.NotNull(top.LookAt);
        Assert.Equal(Enum.GetValues<ShotTemplate>().Length, Drafter.Templates.Select(Drafter.LabelOf).Distinct().Count());
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1305</c> (§11v T2, success condition 2): on the Camera row's EMPTY space the right-click menu offers every template (<i>"Add key: …"</i>) — disabled
    /// before the first event — and picking one adds ONE cut key at the clicked second, hung on the event K would hang it on (<c>shot:Alpha#1</c> + the offset), whose
    /// camera is the drafter's for that event (<see cref="DirectorDrafts.TemplateCamera"/> → <see cref="Drafter.TemplateCamera"/>): ONE undo step takes it away again.
    /// The Director window's button is the same call. 🔴 Mutation: <c>AddTemplateKey</c> adding the key with a fixed time (no anchor) ⇒ the anchor assertion is red.
    /// </summary>
    [Fact]
    public void CE1305_AddKeyFromATemplate_HangsTheDraftersCameraOnTheEvent_InOneUndoStep()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ce1305-tpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TakeStore(() => root, () => null);
            var index = ShotThenHit();
            var session = new DirectorTakeSession(() => store, () => null, () => null, sim => EventAnchors.NearestBefore(index, sim, NameOf));
            var drafts = new DirectorDrafts(() => index, NameOf, _ => SubjectKind.Unknown, () => "sc", session);
            session.Templates = drafts.TemplateCamera;
            Assert.Empty(session.OpenDraft(new Take { Scenario = "sc", Name = "t", Keys = new[] { new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { LookAt = Vector3.Zero, Distance = 10 } } } }));
            var tracks = session.Director!;
            var undo = session.UndoStack;
            undo.Clear();

            var early = tracks.Camera.MenuItems(null, 5d).ToList();                       // nothing before 5 s to frame
            Assert.Equal(Drafter.Templates.Count, early.Count);
            Assert.All(early, m => Assert.False(m.Enabled));
            Assert.False(tracks.AddTemplateKey(ShotTemplate.OverTheShoulder, 5d));

            var menu = tracks.Camera.MenuItems(null, 10.5).ToList();                       // after the shot (10 s), before the hit (10.8 s)
            var item = menu.Single(m => m.Label == DirectorTracks.TemplateMenuPrefix + Drafter.LabelOf(ShotTemplate.Consequence));
            Assert.True(item.Enabled);
            item.Run();

            var keys = session.Editor!.Take.Keys;
            Assert.Equal(2, keys.Count);
            Assert.Equal(new AtExpr(0.5, "shot:Alpha#1"), keys[1].At);                    // K's anchor: the shot at 10 s, + 0.5 s
            Assert.Contains("shot:Alpha#1", session.Editor.Take.Anchors.Keys);
            var expected = Drafter.TemplateCamera(index, TheShot(index), NameOf, new DraftOptions { Scenario = "sc", Kind = _ => SubjectKind.Unknown }, ShotTemplate.Consequence, 10.5);
            Assert.Equal(expected, keys[1].Camera);
            Assert.Equal(expected.Fov, keys[1].Fov);

            Assert.Equal("Add camera key", undo.UndoDescription);                          // ONE step …
            session.UndoLast();
            Assert.Single(session.Editor.Take.Keys);
            Assert.False(undo.CanUndo);                                                     // … and nothing else to undo

            Assert.Null(DirectorWindow.AddTemplateKey(tracks, ShotTemplate.Moment, 10.5));   // the window's button: the same add
            Assert.Equal(2, session.Editor.Take.Keys.Count);
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    private sealed class OffsetComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-6;
        public int GetHashCode(double obj) => 0;
    }
}
