using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Editor.Tests.Director;

/// <summary>
/// ⭐ <c>CE-4060</c> (docs/DESIGN_Film_Production.md §4a) — a scenario's checks and verdicts as ONE documentation take: a still per check at the check's
/// moment, each framed by the drafter's ③ camera. The checks are a <c>ua-tank-ammo</c>-shaped set parsed by <see cref="ChecksJson"/>; the recording is a
/// synthetic index (frames every 0.1 sim s for 61 s; Tank walks east at 2 m/s, the others stand; a position sample every 0.5 s).
/// </summary>
public sealed class DocumentationTakesTests
{
    private const long Tank = 1, EnemyTank = 2, RiflemanA = 3, RiflemanB = 4;
    private const double Dt = 0.1;

    private static readonly Dictionary<string, long> Ids = new(StringComparer.Ordinal)
    {
        ["Tank"] = Tank, ["Enemy Tank"] = EnemyTank, ["Rifleman A"] = RiflemanA, ["Rifleman B"] = RiflemanB,
    };

    private static long? IdOf(string name) => Ids.TryGetValue(name, out var id) ? id : null;

    private static string? NameOf(long id) => Ids.FirstOrDefault(p => p.Value == id).Key;

    private static int FrameOf(double sim) => (int)Math.Round(sim / Dt);

    private static TimelineIndex Index()
    {
        int frames = FrameOf(61d) + 1;
        var ticks = Enumerable.Range(0, frames).Select(f => (long)f * 1_000_000).ToArray();
        var sim = Enumerable.Range(0, frames).Select(f => f * Dt).ToArray();
        var samples = new Dictionary<long, List<PositionSample>> { [Tank] = new(), [EnemyTank] = new(), [RiflemanA] = new(), [RiflemanB] = new() };
        for (int f = 0; f < frames; f += 5)
        {
            samples[Tank].Add(new PositionSample(f, (float)(2 * f * Dt), 0f, 0f, 0f));
            samples[EnemyTank].Add(new PositionSample(f, 80f, 0f, 0f, MathF.PI));
            samples[RiflemanA].Add(new PositionSample(f, 10f, 30f, 0f, 0f));
            samples[RiflemanB].Add(new PositionSample(f, 150f, -20f, 0f, 0f));
        }
        var positions = new PositionTrack(samples.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        return new TimelineIndex(new Dictionary<string, SharedList<TimelineEvent>>(), new SharedList<long>(ticks), frames, frames, null, new SharedList<double>(sim), positions);
    }

    private static ScenarioChecks Checks(string expect)
    {
        var problems = new List<string>();
        var checks = ChecksJson.Parse((JsonObject)JsonNode.Parse("{ \"runFor\": 60, \"expect\": [" + expect + "] }")!, "ua-tank-ammo", problems);
        Assert.Empty(problems);
        return checks;
    }

    private const string TankShot = """{"name": "Tank fires on the enemy", "on": "shot", "by": "Tank", "at": "Enemy Tank", "before": 20}""";
    private const string EnemyWounded = """{"name": "Enemy tank is wounded", "on": "field", "who": "Enemy Tank", "path": "Health.Current", "op": "<", "value": 50, "before": 40}""";
    private const string NoFriendlyFire = """{"name": "No friendly fire", "never": {"on": "shot", "by": "Tank", "hit": "Tank"}}""";
    private const string TankAlive = """{"name": "Tank alive at end", "on": "field", "who": "Tank", "path": "Health.Current", "op": ">", "value": 0, "atEnd": true}""";

    private static readonly string[] Names = { "Tank", "Enemy Tank", "Rifleman A", "Rifleman B" };

    private static Take Build(ScenarioChecks checks, IReadOnlyList<Verdict> verdicts, IReadOnlyCollection<string>? names = null, DraftOptions? options = null)
        => DocumentationTakes.From(checks, verdicts, Index(), IdOf, NameOf, names ?? Names, options ?? new DraftOptions { Scenario = "ua-tank-ammo" });

    /// <summary>The four-check set of the design's acceptance, judged: the shot passed at 8.9, the wound failed (cue never held), the never passed, the atEnd passed.</summary>
    private static (ScenarioChecks, Verdict[]) TheSet() => (
        Checks(string.Join(",", TankShot, EnemyWounded, NoFriendlyFire, TankAlive)),
        new[]
        {
            new Verdict("Tank fires on the enemy", true, 8.9, "once"),
            new Verdict("Enemy tank is wounded", false, null, "once"),
            new Verdict("No friendly fire", true, null, "never"),
            new Verdict("Tank alive at end", true, 60d, "atEnd"),
        });

    private static IReadOnlyList<(string Still, string Caption)> Captions(Take take)
        => ((JsonArray)take.Extra![DocumentationTakes.CaptionsKey]!).Select(n => (n!["still"]!.ToString(), n["caption"]!.ToString())).ToList();

    /// <summary>The §4a set ⇒ exactly three keys, at 8.9 (passed), 40 (failed, never held: the window's end) and RunFor (atEnd), in time order and at FIXED times; the passing
    /// never makes none; the take is <c>_doc</c> of the checks' scenario. 🔴 Mutation: the failed check's window end <c>e.Before?.Resolve(namedTime) ?? runFor</c> → <c>runFor</c> ⇒ the 40 s key is 60.</summary>
    [Fact]
    public void CE4060_TheAcceptanceSet_GivesThreeKeys_AtThePassTime_TheWindowEnd_AndRunFor_InOrder()
    {
        var (checks, verdicts) = TheSet();
        var take = Build(checks, verdicts);

        Assert.Equal("_doc", take.Name);
        Assert.Equal("ua-tank-ammo", take.Scenario);
        Assert.Equal(3, take.Keys.Count);
        Assert.Equal(new[] { 8.9, 40d, 60d }, take.Keys.Select(k => k.At.Offset).ToArray());
        Assert.All(take.Keys, k => Assert.True(k.At.IsFixed));
    }

    /// <summary>Each key's still is <c>nn-slug</c> of its check's name in time order, and its caption is the name ending ✓ (passed) or ✗ (failed).
    /// 🔴 Mutation: the caption's <c>verdict.Ok ? " ✓" : " ✗"</c> inverted ⇒ the first caption ends ✗.</summary>
    [Fact]
    public void CE4060_EachKeyIsAStillNamedFromItsCheck_WithACaptionEndingInItsVerdictMark()
    {
        var (checks, verdicts) = TheSet();
        var take = Build(checks, verdicts);

        Assert.Equal(new[] { "00-tank-fires-on-the-enemy", "01-enemy-tank-is-wounded", "02-tank-alive-at-end" }, take.Keys.Select(k => k.Still).ToArray());
        var captions = Captions(take);
        Assert.Equal(take.Keys.Select(k => k.Still!).ToArray(), captions.Select(c => c.Still).ToArray());
        Assert.Equal(new[] { "Tank fires on the enemy ✓", "Enemy tank is wounded ✗", "Tank alive at end ✓" }, captions.Select(c => c.Caption).ToArray());
    }

    /// <summary>The first key's camera IS the drafter's ③ for the same moment (Tank → Enemy Tank at 8.9): the third key of <see cref="Drafter.Draft"/> — not a copy of its math.
    /// 🔴 Mutation: the moment's subject dropped (<c>subject</c> → null) ⇒ the camera is the actor-only framing and differs.</summary>
    [Fact]
    public void CE4060_TheFirstKeysCamera_IsTheDraftersThirdKey_ForThatMoment()
    {
        var (checks, verdicts) = TheSet();
        var take = Build(checks, verdicts);

        var index = Index();
        var moment = new DraftMoment("check", -1, FrameOf(8.9), 8.9, Tank, EnemyTank, 1d);
        var drafted = Drafter.Draft(index, moment, NameOf, new DraftOptions { Scenario = "ua-tank-ammo" });
        Assert.Equal(4, drafted.Keys.Count);   // ① establish · ② set-up · ③ the moment · ④ consequence
        Assert.Equal(drafted.Keys[2].Camera, take.Keys[0].Camera);
        Assert.Equal(drafted.Keys[2].Camera.Fov, take.Keys[0].Fov);
        Assert.NotNull(take.Keys[0].Camera.LookAt);
    }

    /// <summary>A <c>never</c> that held (passed) has no moment ⇒ no key; one that was broken (Time 12) is a key at 12 — when it happened.
    /// 🔴 Mutation: <c>case "never": return v.Ok ? null : v.Time</c> → <c>v.Ok ? runFor : v.Time</c> ⇒ the passing never makes a key at 60.</summary>
    [Fact]
    public void CE4060_ANeverThatPassed_MakesNoKey_ANeverThatFailed_MakesOneWhenItHappened()
    {
        var checks = Checks(NoFriendlyFire);

        Assert.Empty(Build(checks, new[] { new Verdict("No friendly fire", true, null, "never") }).Keys);

        var failed = Build(checks, new[] { new Verdict("No friendly fire", false, 12d, "never") });
        var key = Assert.Single(failed.Keys);
        Assert.Equal(12d, key.At.Offset);
        Assert.Equal("00-no-friendly-fire", key.Still);
        Assert.Equal("No friendly fire ✗", Captions(failed).Single().Caption);
    }

    /// <summary>A wildcard <c>"*Rifleman"</c> is the first unit by ORDINAL name order that contains the part — "Rifleman A", although "Rifleman B" is listed first — so the key's
    /// camera is the drafter's ③ for Rifleman A, not B. 🔴 Mutation: <c>unitNames.OrderBy(…)</c> → <c>unitNames.ToList()</c> ⇒ B is picked and the camera differs.</summary>
    [Fact]
    public void CE4060_AWildcard_ResolvesToTheFirstMatchByNameOrder()
    {
        var checks = Checks("""{"name": "A rifleman fires", "on": "shot", "by": "*Rifleman", "at": "Enemy Tank"}""");
        var verdicts = new[] { new Verdict("A rifleman fires", true, 5d, "once") };
        var take = Build(checks, verdicts, names: new[] { "Rifleman B", "Tank", "Rifleman A", "Enemy Tank" });

        var index = Index();
        CameraSpec ThirdFor(long actor) =>
            Drafter.Draft(index, new DraftMoment("check", -1, FrameOf(5d), 5d, actor, EnemyTank, 1d), NameOf, new DraftOptions { Scenario = "ua-tank-ammo" }).Keys.Single(k => k.At.Offset == 5d - 0.7).Camera;
        Assert.NotEqual(ThirdFor(RiflemanA), ThirdFor(RiflemanB));
        Assert.Equal(ThirdFor(RiflemanA), Assert.Single(take.Keys).Camera);
    }

    /// <summary>A failed check whose cue DID hold, but outside its window (Outside 25), is filmed where it happened — 25, not the window's end (40) — and still named ✗.
    /// 🔴 Mutation: <c>v.Outside ?? e.Before?.Resolve(namedTime) ?? runFor</c> reordered to put <c>Before</c> first ⇒ the key is at 40.</summary>
    [Fact]
    public void CE4060_ACheckThatHeldOutsideItsWindow_IsFilmedWhereItHappened()
    {
        var checks = Checks(EnemyWounded);
        var take = Build(checks, new[] { new Verdict("Enemy tank is wounded", false, null, "once", Outside: 25d) });

        var key = Assert.Single(take.Keys);
        Assert.Equal(25d, key.At.Offset);
        Assert.EndsWith(" ✗", Captions(take).Single().Caption);
    }

    /// <summary>A check whose units resolve to nothing is framed over the whole scenario (the drafter's group camera) and its caption SAYS so, the verdict mark still last.
    /// 🔴 Mutation: the <c>(wholeScene ? " (no unit … )" : "")</c> note removed ⇒ the caption lacks it.</summary>
    [Fact]
    public void CE4060_ACheckWithNoResolvableUnit_IsFramedOverTheWholeScenario_AndSaysSo()
    {
        var checks = Checks("""{"name": "Ghost fires", "on": "shot", "by": "Ghost", "at": "Phantom"}""");
        var take = Build(checks, new[] { new Verdict("Ghost fires", true, 10d, "once") });

        var key = Assert.Single(take.Keys);
        var look = key.Camera.LookAt!.Value;
        Assert.InRange(look.X, 0f, 150f);   // the middle of the cast (Tank 20 … Rifleman B 150), not the origin fallback of a unit-less framing
        Assert.True(look.X > 20f);
        var caption = Captions(take).Single().Caption;
        Assert.Contains("whole scenario", caption);
        Assert.EndsWith(" ✓", caption);
    }
}
