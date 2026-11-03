using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.Presentation.Windows;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ D1 + G1 (CE-4231) — <b>Window ▸ Trainee debrief</b> and <b>Window ▸ Game summary in a REAL ImGui frame</b> (Xvfb): the debrief's header, standards cards with the group mean, went well / work on and the
/// <i>later</i> lines; the summary's banner, subline, both teams' tables, your round and the awards. The probe (the text the window drew) is what is asserted; screenshots are evidence
/// (<c>HROT_RULES_SHOTS</c>), never committed and never compared.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DebriefWindowsFrameRail : IDisposable
{
    private readonly EntityRepository _world = new();
    private int _next = 100;

    public DebriefWindowsFrameRail()
    {
        _world.RegisterComponent<NetworkIdentity>(); _world.RegisterComponent<ObjectiveStatus>(); _world.RegisterComponent<ObjectiveLinks>(); _world.RegisterComponent<EntityInfo>(); _world.RegisterComponent<ExerciseResult>();
    }

    public void Dispose() => _world.Dispose();

    private int Man(int net, string name, ForceId side)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(net));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        return net;
    }

    private int Rule(string title, string kind, ObjectiveRole role, int parent, int subject, float value = 0, float target = 0, float score = 0, float pass = 0, float points = 0, string group = "", ObjectiveState state = ObjectiveState.Active, bool scored = true)
    {
        var e = _world.CreateEntity(); int id = _next++;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, State = state, Value = value, Target = target, Score = score, Pass = pass, Points = points, Group = new FixedString32(group), Scored = (byte)(scored ? 1 : 0) });
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
        return id;
    }

    private static string ShotPath(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RULES_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-rules-rail");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    [SkippableFact]
    public void TheTraineeDebriefAndTheGameSummary_DrawTheirCardsInARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var res = _world.CreateEntity();
        _world.AddComponent(res, new ExerciseResult { Phase = ExercisePhase.Ended, EndedAt = 1122, Friend = new SideResult { Outcome = SideOutcome.Win }, Hostile = new SideResult { Outcome = SideOutcome.Lose } });
        int plat = Man(900, "1st Platoon", ForceId.Friend), kral = Man(902, "Rifleman Kral", ForceId.Friend), hawk = Man(10, "Hawk", ForceId.Friend); int raven = Man(20, "Raven", ForceId.Hostile);
        int blue = Man(1, "Blue", ForceId.Friend), red = Man(2, "Red", ForceId.Hostile);
        // training
        int top = Rule("Platoon attack", "WeightedMean", ObjectiveRole.Objective, 0, plat, score: 0.42f, pass: 0.6f);
        int members = Rule("Members", "MemberEvaluation", ObjectiveRole.Objective, top, plat, score: 0.42f, pass: 0.6f);
        int root = Rule("Member standards", "WeightedMean", ObjectiveRole.Objective, members, kral, score: 0.22f, pass: 0.6f);
        Rule("Hit ratio", "HitRatio", ObjectiveRole.Measure, root, kral, 0.12f, 0.25f, 0.48f, 0.7f, group: "fire");
        Rule("Contact report", "ReportLatency", ObjectiveRole.Measure, root, kral, 38f, 60f, 1f, 0.7f, group: "reports");
        Rule("Hit ratio (mean)", "SelectionMean", ObjectiveRole.Objective, members, 0, 0.14f, 0.25f, 0.54f, 0.6f, group: "fire", scored: false);
        // game
        int bt = Rule("Blue score", "MemberEvaluation", ObjectiveRole.Objective, 0, blue, 3150), rt = Rule("Red score", "MemberEvaluation", ObjectiveRole.Objective, 0, red, 1420);
        void Player(int man, int parent, float score, float kills)
        {
            int r = Rule("Score", "Points", ObjectiveRole.Measure, parent, man, score);
            Rule("Kills", "Count", ObjectiveRole.Measure, r, man, kills, points: 100); Rule("Assists", "Count", ObjectiveRole.Measure, r, man, 2, points: 50); Rule("Deaths", "Count", ObjectiveRole.Measure, r, man, 3, scored: false);
        }
        Player(hawk, bt, 1840, 11); Player(kral, bt, 1310, 8); Player(raven, rt, 1420, 9);
        Rule("Top scorer", "Best", ObjectiveRole.Objective, 0, hawk, 1840, state: ObjectiveState.Achieved, scored: false);

        var debrief = new TraineeDebriefWindow(() => _world, () => null, null, "rail_debrief", "Scenario", Vector4.One, now: () => 1122d) { Chosen = kral };
        var summary = new GameSummaryWindow(() => _world, () => null, null, "rail_gamesum", "Scenario", Vector4.One, now: () => 1122d);
        using var frame = UiFrameHarness.Begin(1280, 760);
        void Draw(string title, Action content, int n = 4)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() => { ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 760)); ImGui.Begin(title, ImGuiWindowFlags.NoCollapse); content(); ImGui.End(); });
        }
        Draw("Trainee debrief", debrief.DrawContent);
        var d = debrief.Probe;
        Assert.Equal("available", d.Availability);
        Assert.Equal("Rifleman Kral", d.Header);
        Assert.Contains(d.Summary, l => l.StartsWith("score 0.22") && l.Contains("NO-GO"));
        Assert.Contains("1 of 2 standards met", d.Summary);
        Assert.Contains(d.Cards, l => l.StartsWith("fire | Hit ratio | NO-GO | 0.12 · standard 0.25 | 1st Platoon mean 0.14"));
        Assert.Contains(d.Cards, l => l.StartsWith("reports | Contact report | GO | 38"));
        Assert.Equal(new[] { "Contact report: 38 · standard 60" }, d.WentWell); Assert.Equal(new[] { "Hit ratio: 0.12 · standard 0.25" }, d.WorkOn);
        Assert.Equal(2, d.Later.Count);
        frame.Screenshot(ShotPath("debrief.png"));

        Draw("Game summary", summary.DrawContent);
        var g = summary.Probe;
        Assert.Equal("BLUE WINS", g.Banner);
        Assert.Equal(2, g.Teams.Count);
        Assert.StartsWith("Blue 3150: Hawk K11", g.Teams[0]); Assert.Contains("Rifleman Kral K8", g.Teams[0]);
        Assert.StartsWith("Red 1420: Raven K9", g.Teams[1]);
        Assert.Contains(g.Awards, a => a.StartsWith("Top scorer: Hawk"));
        Assert.Equal("available", g.Availability);
        frame.Screenshot(ShotPath("game-summary.png"));
    }
}
