using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Infrastructure;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ H1 + H2 (CE-4226 … CE-4228) — a FRAME of the possession overlay in a real window (Xvfb + Mesa) with the player's HUD data in the world: the mission list and my
/// standards (training), the tickets / points / clock bar and the capturing strip (game), the kill feed and the streak. Each region is compared with a frame of the SAME scene
/// with every player-HUD feature off: the pixels that differ are the HUD. Each feature off removes exactly its part. Set <c>HROT_RAIL_SHOTS</c> to keep the frames (never committed).
/// Skips — never fails — where there is no display. Red-proof: draw the kill feed without its <c>on(KillFeed)</c> gate (Compose) ⇒ the kill-feed-off row fails.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class PlayerHudFrameRail
{
    private static readonly string[] AllHud =
    {
        HudFeatures.Objectives, HudFeatures.OwnEvaluation, HudFeatures.Tickets, HudFeatures.CapturePoints, HudFeatures.RoundTimer, HudFeatures.KillFeed, HudFeatures.ScorePopups, HudFeatures.BountyMarker,
    };

    private readonly record struct Counts(int Bar, int Strip, int Kills, int Column, int Streak, int Right);

    private static int Diff(Image a, Image b, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var p = Raylib.GetImageColor(a, x, y); var q = Raylib.GetImageColor(b, x, y);
                if (System.Math.Abs(p.R - q.R) + System.Math.Abs(p.G - q.G) + System.Math.Abs(p.B - q.B) > 60) n++;
            }
        return n;
    }

    private static Image Frame(string[] off, string name)
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<PossessedBy>();
        world.RegisterComponent<WeaponAmmo>();
        world.RegisterComponent<Health>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<EntityInfo>();
        world.RegisterComponent<ObjectiveStatus>();
        world.RegisterComponent<ObjectiveLinks>();
        world.RegisterManagedComponent<BehaviorStartRecord>();
        var unit = world.CreateEntity();
        world.AddComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(unit, new Health { Current = 80f, Max = 100f });
        world.AddComponent(unit, new PossessedBy { StationNodeId = (Fdp.Core.NodeId)(0), Mode = PossessionMode.Fps, Epoch = 1 });
        world.AddComponent(unit, new NetworkIdentity(4242));
        world.AddComponent(unit, new EntityInfo { Name = new FixedString64("Kral"), ForceId = ForceId.Friend });
        var map = new NetworkEntityMap();
        map.Register(4242, unit);
        world.SetSingletonManaged(map);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 378.0 });

        int next = 100;
        Entity Rule(string title, string kind, ObjectiveRole role, ObjectiveState state, ForceId owner, int subject = 0, float value = 0, float target = 0, int cur = 0, int need = 0,
                    float score = 0, float pass = 0, string reason = "", bool required = false, bool capture = false, ForceId challenger = ForceId.Neutral, float areaX = 0, float areaY = 0, float areaRadius = 0)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new NetworkIdentity(next++));
            world.AddComponent(e, new ObjectiveStatus
            {
                Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, State = state, Owner = owner, Value = value, Target = target, ProgressCur = cur, ProgressNeed = need,
                Score = score, Pass = pass, Reason = new FixedString64(reason), Required = (byte)(required ? 1 : 0), Scored = 1,
                Capture = (byte)(capture ? 1 : 0), Challenger = challenger, AreaCenter = new Vector3(areaX, areaY, 0f), AreaRadius = areaRadius,
            });
            world.AddComponent(e, new ObjectiveLinks { Subject = new EntityRef(subject) });
            return e;
        }
        // training
        Rule("Reach the village", "", ObjectiveRole.Objective, ObjectiveState.Achieved, ForceId.Friend, required: true);
        Rule("Hold the bridge", "HoldArea", ObjectiveRole.Objective, ObjectiveState.Active, ForceId.Friend, cur: 370, need: 600, required: true, reason: "3/4 men, 0 enemy, held 370 of 600 s");
        Rule("Clear the village", "", ObjectiveRole.Objective, ObjectiveState.Pending, ForceId.Friend);
        Rule("Hit ratio", "HitRatio", ObjectiveRole.Measure, ObjectiveState.Active, ForceId.Friend, subject: 4242, value: 0.12f, target: 0.25f, score: 0.5f, pass: 0.7f);
        Rule("Exposure", "Sample", ObjectiveRole.Measure, ObjectiveState.Active, ForceId.Friend, subject: 4242, value: 34, target: 20, score: 0.2f, pass: 0.7f);
        // game
        Rule("Blue tickets", "Counter", ObjectiveRole.Objective, ObjectiveState.Active, ForceId.Friend, value: 87, target: 150);
        Rule("Red tickets", "Counter", ObjectiveRole.Objective, ObjectiveState.Active, ForceId.Hostile, value: 12, target: 150);
        Rule("Point A", "HoldArea", ObjectiveRole.Objective, ObjectiveState.Active, ForceId.Friend, cur: 10, need: 10, capture: true, areaX: 400, areaY: 400, areaRadius: 15);
        Rule("Point B", "HoldArea", ObjectiveRole.Objective, ObjectiveState.Active, ForceId.Neutral, cur: 6, need: 10, capture: true, challenger: ForceId.Friend, areaX: 100, areaY: 50, areaRadius: 15);   // the held unit stands in it
        Rule("Point C", "HoldArea", ObjectiveRole.Objective, ObjectiveState.Active, ForceId.Hostile, cur: 10, need: 10, capture: true, areaX: 650, areaY: 400, areaRadius: 15);
        Rule("Streak", "Count", ObjectiveRole.Measure, ObjectiveState.Active, ForceId.Friend, subject: 4242, value: 3);
        var feed = KillFeed.In(world);
        feed.Record(new KillFeed.Entry(370, 4242, "Kral", ForceId.Friend, 9, "Raven", ForceId.Hostile, true, false, null, 212f));
        feed.Record(new KillFeed.Entry(372, 8, "Fox", ForceId.Hostile, 7, "Bobr", ForceId.Friend, false, true, null, 0f));
        feed.Record(new KillFeed.Entry(375, 6, "Hawk", ForceId.Friend, 5, "Eagle", ForceId.Hostile, true, false, "Owl", 40f));

        using var frame = UiFrameHarness.Begin(1280, 800);
        var canvas = new MapCanvas(input: null);
        var view = new MapViewSwitch(canvas, new MapCamera(), new MapCamera3D { ViewportOverride = new Vector2(1280, 800) });
        var gate = new NodeFeatures(new FeatureRegistry(HudFeatures.Declarations(true)), Fdp.Core.NodeRole.Map2D);
        foreach (var id in off) gate.SetNodeOverride(id, "{\"Enabled\":false}");
        var station = new PossessionStation(view, () => world, featureGate: () => gate);
        canvas.AddLayer(station);
        station.Update(0.016f);
        view.Camera3D.Update(0.016f);
        Assert.True(station.InFps);

        frame.Step(canvas.Draw);
        var image = Raylib.LoadImageFromScreen();
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); Raylib.ExportImage(image, Path.Combine(dir, name + ".png")); }
        return image;
    }

    // the regions of a 1280 x 800 frame: the bar (top-centre), the strip under it, the kill feed (top-right), the right column below it, the streak (under the crosshair)
    private static Counts Measure(Image on, Image bare) => new(
        Bar: Diff(on, bare, 400, 62, 880, 116), Strip: Diff(on, bare, 400, 118, 880, 154), Kills: Diff(on, bare, 880, 64, 1270, 142),
        Column: Diff(on, bare, 880, 158, 1270, 578), Streak: Diff(on, bare, 480, 500, 800, 560), Right: Diff(on, bare, 880, 64, 1270, 578));

    [SkippableFact]
    public void CE4226_TheOverlay_DrawsTheTrainingAndGameHud_AndEachFeatureOffRemovesItsPart()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var bare = Frame(AllHud, "hud-all-off");
        var all = Frame(System.Array.Empty<string>(), "hud-all-on");
        try
        {
            var a = Measure(all, bare);
            var nothing = Measure(bare, bare);
            Assert.Equal(new Counts(0, 0, 0, 0, 0, 0), nothing);
            Assert.True(a.Bar > 3000, $"the game bar is drawn ({a.Bar})");
            Assert.True(a.Strip > 1000, $"the capturing strip is drawn ({a.Strip})");
            Assert.True(a.Kills > 5000, $"the kill feed is drawn ({a.Kills})");
            Assert.True(a.Column > 5000, $"the mission list and my standards are drawn ({a.Column})");
            Assert.True(a.Streak > 200, $"the streak is drawn ({a.Streak})");

            // each feature off ⇒ its part is gone (the others stay)
            foreach (var (feature, check) in new (string, System.Func<Counts, bool>)[]
            {
                (HudFeatures.KillFeed, c => c.Right < a.Right - 5000),
                (HudFeatures.ScorePopups, c => c.Streak < 20),
                (HudFeatures.CapturePoints, c => c.Strip < 20 && c.Bar < a.Bar),
                (HudFeatures.Tickets, c => c.Bar < a.Bar - 400),
                (HudFeatures.RoundTimer, c => c.Bar < a.Bar - 150),
                (HudFeatures.Objectives, c => c.Right < a.Right - 5000),
                (HudFeatures.OwnEvaluation, c => c.Right < a.Right - 3000),
            })
            {
                using var off = new FrameHolder(Frame(new[] { feature }, "hud-off-" + feature));
                var c = Measure(off.Image, bare);
                Assert.True(check(c), $"{feature} off removes its part: {c} vs all-on {a}");
            }
        }
        finally { Raylib.UnloadImage(bare); Raylib.UnloadImage(all); }
    }

    private sealed class FrameHolder : System.IDisposable
    {
        public Image Image { get; }
        public FrameHolder(Image i) => Image = i;
        public void Dispose() => Raylib.UnloadImage(Image);
    }
}
