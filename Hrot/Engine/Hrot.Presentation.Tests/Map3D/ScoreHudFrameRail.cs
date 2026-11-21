using System.Numerics;
using Fdp.Core;
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
/// ⭐⭐ G1 (CE-4231) — a FRAME of the possession overlay in a real window (Xvfb + Mesa) with a game score tree in the world: while the scoreboard key is HELD the scoreboard is drawn (both teams, the man's
/// row), and a child Count that ROSE between two frames floats up as a "+100 Kills" pop-up. Each feature off ⇒ its part is not drawn. Compared with a frame of the same scene with both features off.
/// Skips — never fails — where there is no display.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ScoreHudFrameRail
{
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

    private static Image Frame(bool held, bool rise, string[] off, string name)
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<PossessedBy>(); world.RegisterComponent<WeaponAmmo>(); world.RegisterComponent<Health>();
        world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<EntityInfo>(); world.RegisterComponent<ObjectiveStatus>(); world.RegisterComponent<ObjectiveLinks>();
        Entity Man(int net, string nm, ForceId side)
        {
            var m = world.CreateEntity();
            world.AddComponent(m, new NetworkIdentity(net));
            world.AddComponent(m, new EntityInfo { Name = new FixedString64(nm), ForceId = side });
            return m;
        }
        var unit = Man(4242, "Kral", ForceId.Friend);
        world.AddComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(unit, new Health { Current = 80f, Max = 100f });
        world.AddComponent(unit, new PossessedBy { StationNodeId = (Fdp.Core.NodeId)(0), Mode = PossessionMode.Fps, Epoch = 1 });
        Man(4300, "Hawk", ForceId.Friend); Man(4400, "Raven", ForceId.Hostile); Man(1, "Blue", ForceId.Friend); Man(2, "Red", ForceId.Hostile);
        var map = new NetworkEntityMap(); map.Register(4242, unit); world.SetSingletonManaged(map);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 378.0 });

        int next = 100; Entity killsOfMe = default;
        Entity Rule(string title, string kind, ObjectiveRole role, int parent, int subject, float value = 0, float points = 0, bool scored = true)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new NetworkIdentity(next++));
            world.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, State = ObjectiveState.Active, Value = value, Points = points, Scored = (byte)(scored ? 1 : 0) });
            world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
            return e;
        }
        int blue = NetworkIdResolver.RuntimeNetworkIdOf(world, Rule("Blue score", "MemberEvaluation", ObjectiveRole.Objective, 0, 1, 3150));
        int red = NetworkIdResolver.RuntimeNetworkIdOf(world, Rule("Red score", "MemberEvaluation", ObjectiveRole.Objective, 0, 2, 1420));
        void Player(int man, int parent, float score, float kills, bool mine)
        {
            int root = NetworkIdResolver.RuntimeNetworkIdOf(world, Rule("Score", "Points", ObjectiveRole.Measure, parent, man, score));
            var k = Rule("Kills", "Count", ObjectiveRole.Measure, root, man, kills, 100);
            if (mine) killsOfMe = k;
            Rule("Assists", "Count", ObjectiveRole.Measure, root, man, 2, 50); Rule("Deaths", "Count", ObjectiveRole.Measure, root, man, 4, scored: false);
            Rule("Captures", "Count", ObjectiveRole.Measure, root, man, 1, 150); Rule("Team kills", "Count", ObjectiveRole.Measure, root, man, 0, -200);
            Rule("Streak", "Count", ObjectiveRole.Measure, root, man, 0, scored: false); Rule("Best streak", "Count", ObjectiveRole.Measure, root, man, 2, scored: false);
        }
        Player(4300, blue, 1840, 11, false); Player(4242, blue, 1310, 8, true); Player(4400, red, 1420, 9, false);

        using var frame = UiFrameHarness.Begin(1280, 800);
        var canvas = new MapCanvas(input: null);
        var view = new MapViewSwitch(canvas, new MapCamera(), new MapCamera3D { ViewportOverride = new Vector2(1280, 800) });
        var gate = new NodeFeatures(new FeatureRegistry(HudFeatures.Declarations(true)), Fdp.Core.NodeRole.Map2D);
        foreach (var id in off) gate.SetNodeOverride(id, "{\"Enabled\":false}");
        var station = new PossessionStation(view, () => world, featureGate: () => gate);
        canvas.AddLayer(station);
        station.Update(0.016f); view.Camera3D.Update(0.016f);
        Assert.True(station.InFps);
        frame.Step(canvas.Draw);                                   // the first look: the baseline of the pop-ups
        if (rise) { world.GetComponentRW<ObjectiveStatus>(killsOfMe).Value = 9; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 379.0 }); }
        station.ScoreboardHeld = held;
        frame.Step(canvas.Draw);
        var image = Raylib.LoadImageFromScreen();
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); Raylib.ExportImage(image, Path.Combine(dir, name + ".png")); }
        return image;
    }

    [SkippableFact]
    public void CE4231_TheScoreboardIsDrawnWhileHeld_AndAScoreRiseFloatsUp_EachFeatureOffRemovesItsPart()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string[] none = System.Array.Empty<string>(), both = { HudFeatures.Scoreboard, HudFeatures.ScorePopups };
        var bare = Frame(true, true, both, "score-all-off");
        var board = Frame(true, false, new[] { HudFeatures.ScorePopups }, "score-board");
        var boardOff = Frame(true, false, both, "score-board-off");
        var popup = Frame(false, true, new[] { HudFeatures.Scoreboard }, "score-popup");
        var released = Frame(false, false, none, "score-released");
        var baseline = Frame(false, false, both, "score-baseline");
        try
        {
            // the board sits in the middle of the screen; the pop-up just under the crosshair
            int Board(Image i, Image b) => Diff(i, b, 300, 200, 980, 640);
            int Popup(Image i, Image b) => Diff(i, b, 480, 420, 800, 500);
            Assert.True(Board(board, boardOff) > 6000, $"the scoreboard is drawn while K is held ({Board(board, boardOff)})");
            Assert.True(Board(released, baseline) < 50, "released, no scoreboard");
            Assert.True(Board(boardOff, baseline) < 50, "game.scoreboard off: nothing even while held");
            Assert.True(Popup(popup, baseline) > 150, $"the +100 pop-up is drawn after a rise ({Popup(popup, baseline)})");
            Assert.True(Popup(bare, baseline) < 50, "game.score-popups off: no pop-up");
        }
        finally { foreach (var i in new[] { bare, board, boardOff, popup, released, baseline }) Raylib.UnloadImage(i); }
    }
}
