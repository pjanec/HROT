using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.Presentation.Windows;
using Hrot.UI.Common.Rules;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ CE-4217 (rules view R3; RV-8) — <b>Window ▸ Mission summary in a REAL ImGui frame</b>: the cards of both sides, the scores-by-level table with its groupings, a click on a row shows <i>why</i> and <i>Show the moments on the timeline →</i>
/// reaches the host, and <i>Export report</i> is disabled with "later". ⛔ Screenshots are evidence (<c>HROT_RULES_SHOTS</c>), never compared.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class MissionSummaryWindowFrameRail : IDisposable
{
    private readonly EntityRepository _world = new();

    public MissionSummaryWindowFrameRail()
    {
        _world.RegisterComponent<NetworkIdentity>(); _world.RegisterComponent<ObjectiveStatus>(); _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<EntityInfo>(); _world.RegisterComponent<Health>(); _world.RegisterComponent<InjuryThresholds>();
    }

    public void Dispose() => _world.Dispose();

    private void Man(int id, ForceId side, string name, float health = 100f)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        _world.AddComponent(e, new Health { Current = health, Max = 100f });
        _world.AddComponent(e, new InjuryThresholds { WoundedBelow = 0.7f, IncapacitatedBelow = 0.3f });
    }

    private delegate void Edit(ref ObjectiveStatus s);
    private void Rule(int id, string title, string kind, ObjectiveRole role, int parent, int subject, float score, float pass, ObjectiveState state = ObjectiveState.Active, string group = "", Edit? edit = null)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(id));
        var s = new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, Owner = ForceId.Friend, State = state, Score = score, Pass = pass, Group = new FixedString32(group), Scored = 1 };
        edit?.Invoke(ref s);
        _world.AddComponent(e, s);
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
    }

    private static string ShotPath(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RULES_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-rules-rail");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static void Pos(ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    [SkippableFact]
    public void TheMissionSummary_DrawsTheCards_AClickOnARowShowsWhy_ShowMomentsReachesTheHost_ExportIsDisabled()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Man(900, ForceId.Friend, "1st Platoon"); Man(901, ForceId.Friend, "Rifleman Kral"); Man(902, ForceId.Friend, "Rifleman Down", 20f);
        Man(950, ForceId.Hostile, "Red 1");
        Rule(5, "Overall", "WeightedMean", ObjectiveRole.Objective, 0, 900, 0.42f, 0.6f);
        Rule(10, "Reach the village", "ReachArea", ObjectiveRole.Objective, 5, 0, 1f, 0f, ObjectiveState.Achieved, edit: (ref ObjectiveStatus s) => s.Required = 1);
        Rule(20, "Members", "MemberEvaluation", ObjectiveRole.Objective, 5, 900, 0.42f, 0.6f);
        Rule(21, "Member standards", "WeightedMean", ObjectiveRole.Objective, 20, 901, 0.61f, 0f);
        Rule(22, "Hit ratio", "HitRatio", ObjectiveRole.Measure, 21, 901, 0.56f, 0.6f, group: "fire", edit: (ref ObjectiveStatus s) => { s.Value = 0.14f; s.Target = 0.25f; s.Weight = 30f; });

        var moments = new List<int>();
        var window = new MissionSummaryWindow(() => _world, () => null, null, "rail_summary", "Scenario", Vector4.One, moments.Add, now: () => 600d);
        using var frame = UiFrameHarness.Begin(1280, 760);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 760));
                    ImGui.Begin("Mission summary", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }
        Step(null, 5);
        var p = window.Probe;
        Assert.Contains(p.Cards, l => l.StartsWith("Blue: Overall 0.42") && l.Contains("pass mark 0.60") && l.Contains("not yet decided"));
        Assert.Contains("Blue: required 1 / 1", p.Cards);
        Assert.Contains(p.Cards, l => l.StartsWith("Blue: forces 2 / 3") && l.Contains("1 down"));
        Assert.Contains(p.Cards, l => l.StartsWith("Blue: attention Hit ratio 0.14 (target 0.25)"));
        Assert.Contains(p.Cards, l => l.StartsWith("Red: forces 1 / 1"));
        Assert.Equal(ExportLaterText, p.Export);
        frame.Screenshot(ShotPath("summary-0-cards.png"));

        // select a row (the table is below the cards): the probe names it; its children are listed with pass / fail
        window.SelectedNetworkId = 21;
        Step(null, 3);
        Assert.Contains(window.Probe.Why, l => l.StartsWith("Hit ratio | 0.14 vs 0.25 | weight 30 | fail"));
        Assert.Equal("Command chain", window.Probe.Level);
        Assert.Contains(window.Probe.Levels, l => l.StartsWith("1st Platoon | 0.42"));
        frame.Screenshot(ShotPath("summary-1-why.png"));
        window.Side = SummarySide.Red;
        Step(null, 3);
        Assert.All(window.Probe.Cards, l => Assert.StartsWith("Red:", l));   // the side switch
    }

    /// <summary>⭐ U-5 (CE-4335, AQ108 §0; the user's report: "Mission summary's End exercise works with no live exercise running") — the REAL Mission summary window in a real frame, once per run mode: <i>End exercise</i> is enabled ONLY in a running Live exercise; in
    /// Edit, a Preview, the Live briefing and a review (DAR) it is greyed with its reason (the ONE rule, <c>InstructorEnd.RunRefusal</c>, that the rules bundle wires to the host's transport); a Live exercise that has ENDED keeps "already ended"; on a recording (AAR) it is not
    /// drawn (R-4156). Read off the frame: the button's own text is dim when greyed. 🔴 Red-proof (compiles): <c>RunRefusal</c> returning null for Edit ⇒ the Edit row is enabled ⇒ red.</summary>
    [SkippableFact]
    public void U5_EndExercise_IsGreyedWithItsReason_InEveryModeButARunningLive()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        _world.RegisterComponent<ExerciseResult>();
        Man(900, ForceId.Friend, "1st Platoon");
        var rows = new (string Name, Hrot.UI.Common.Facades.TransportMode Mode, bool Paused, double Total, bool Ended, bool Recording, string? Reason)[]
        {
            ("Edit", Hrot.UI.Common.Facades.TransportMode.Edit, true, 0, false, false, InstructorEnd.NotRunning),
            ("Preview", Hrot.UI.Common.Facades.TransportMode.Preview, false, 12, false, false, InstructorEnd.PreviewIsNotAnExercise),
            ("Live briefing", Hrot.UI.Common.Facades.TransportMode.Live, true, 0, false, false, InstructorEnd.NotStarted),
            ("Live running", Hrot.UI.Common.Facades.TransportMode.Live, false, 42, false, false, null),
            ("Live ended", Hrot.UI.Common.Facades.TransportMode.Live, false, 90, true, false, InstructorEnd.Ended),
            ("DAR", Hrot.UI.Common.Facades.TransportMode.Review, true, 30, false, false, InstructorEnd.ARecording),
            ("AAR", Hrot.UI.Common.Facades.TransportMode.Replay, false, 30, false, true, "not drawn"),
        };
        using var frame = UiFrameHarness.Begin(900, 300);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        foreach (var row in rows)
        {
            Entity ended = default;
            if (row.Ended) { ended = _world.CreateEntity(); _world.AddComponent(ended, new ExerciseResult { Phase = ExercisePhase.Ended }); }
            if (row.Recording) Fdp.Toolkit.Replay.ReplayState.Enter(_world);
            try
            {
                var window = new MissionSummaryWindow(() => _world, () => null, null, "rail_end_" + row.Name.Replace(' ', '_'), "Scenario", Vector4.One, _ => { }, now: () => 600d);
                window.End.CreateOneShot = _ => true;          // the host can create the one-shot (the rules bundle wires it)
                window.End.SeatRefusal = () => null;           // an Instructor / Author seat
                window.End.RunRefusal = () => InstructorEnd.RunRefusal(row.Mode, row.Paused, row.Total);   // what the rules bundle wires to the host's transport
                for (int i = 0; i < 5; i++)
                    frame.Step(() =>
                    {
                        ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(900, 300));
                        ImGui.Begin("Mission summary", ImGuiWindowFlags.NoCollapse);
                        window.DrawContent();
                        ImGui.End();
                    });
                string path = ShotPath("end-" + row.Name.Replace(' ', '-') + ".png");
                frame.Screenshot(path);
                var r = window.End.DrawnButton;
                if (row.Reason == "not drawn")
                {
                    Assert.Equal(default, r);                                  // a recording: End does not exist here
                    continue;
                }
                var offer = window.End.Offer;
                Assert.Equal(row.Reason, offer.DisabledReason);
                Assert.Equal(row.Reason == null, offer.Available);
                var image = Raylib.LoadImage(path);
                try
                {
                    int bright = 0;
                    for (int x = (int)r.Min.X; x < (int)r.Max.X; x++)
                        for (int y = (int)r.Min.Y; y < (int)r.Max.Y; y++)
                        {
                            var c = Raylib.GetImageColor(image, x, y);
                            if (c.R > 215 && c.G > 215 && c.B > 215) bright++;
                        }
                    if (row.Reason == null) Assert.True(bright >= 20, $"{row.Name}: End exercise is enabled - its text is full strength ({bright} px)");
                    else Assert.True(bright == 0, $"{row.Name}: End exercise is greyed - its text is dim ({bright} bright px)");
                }
                finally { Raylib.UnloadImage(image); }
            }
            finally
            {
                if (row.Recording) Fdp.Toolkit.Replay.ReplayState.Leave(_world);
                if (row.Ended) _world.DestroyEntity(ended);
            }
        }
    }

    private const string ExportLaterText = "later";
}
