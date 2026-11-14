using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.Rules;
using Hrot.UI.Common.RunMode;
using ImGuiNET;
using Xunit;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests.RunMode;

/// <summary>
/// ⭐⭐ R-7 / CE-3416 (AQ108 §0 D32, story panel ⑨) — <b>the Player's briefing screen</b>: shown while Live waits at 0:00 (<c>RunModeChoice.LiveBriefing</c>), gone on ▶, built from the ONE objectives reader
/// (<c>TrainingHud</c>), the scenario's resident description and the held unit; the picture-book area is a reserved frame only. 0 B steady (cached strings; rebuilt only on change).
/// 🔴 Mutations: <c>Shows</c> always true ⇒ the state rows red · <c>Refresh</c> without its early return ⇒ <c>Rebuilds</c> grows and the 0 B rail goes red · the objective list built with every side ⇒ the "mine only" row red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class PlayerBriefingTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private int _next = 100;
    private Entity _man;

    public PlayerBriefingTests()
    {
        _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<EntityInfo>(); _w.RegisterComponent<ObjectiveStatus>(); _w.RegisterComponent<ObjectiveLinks>();
        _w.RegisterComponent<PossessedBy>(); _w.RegisterComponent<UnitSubordinate>(); _w.RegisterManagedComponent<ScenarioDescriptionResident>();
    }

    public void Dispose() => _w.Dispose();

    private Entity Man(int net, string name, ForceId side, TacticalDesignation role = TacticalDesignation.Undefined)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new NetworkIdentity(net));
        _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        _w.AddComponent(e, new UnitSubordinate { Designation = role });
        return e;
    }

    private void Objective(string title, ForceId side, bool required = false)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new NetworkIdentity(_next++));
        _w.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32("HoldArea"), Role = ObjectiveRole.Objective, State = ObjectiveState.Active, Owner = side, Required = (byte)(required ? 1 : 0) });
        _w.AddComponent(e, new ObjectiveLinks());
    }

    private void Scene()
    {
        _man = Man(7, "Alpha 1-1", ForceId.Friend, TacticalDesignation.Marksman);
        _w.AddComponent(_man, new PossessedBy());
        Objective("Hold the bridge", ForceId.Friend, required: true); Objective("No civilian harm", ForceId.Friend); Objective("Report contact within 30 s", ForceId.Friend);
        Objective("Destroy the bridge", ForceId.Hostile);   // the other side's — never on my screen
        ScenarioDescription.Stamp(_w, "Seize the bridge before dawn and hold it until relieved.", "night-attack", "h", null, "");
    }

    // ── when it shows ─────────────────────────────────────────────────────────────────────

    /// <summary>Shown only while Live waits at 0:00; gone on ▶ (running), gone once the clock has run (paused later), gone in Edit and with no run controls at all.</summary>
    [Fact]
    public void TheBriefingShows_OnlyWhileLiveWaitsAtZero_AndHidesOnPlay()
    {
        Assert.False(PlayerBriefing.Shows(null));
        Assert.False(PlayerBriefing.Shows(With(ClusterState.OperatingEdit).Choice));
        Assert.True(PlayerBriefing.Shows(With(ClusterState.OperatingLive).Choice));                       // paused at 0:00
        Assert.False(PlayerBriefing.Shows(With(ClusterState.OperatingLive, running: true).Choice));       // ▶ pressed
        Assert.False(PlayerBriefing.Shows(With(ClusterState.OperatingLive, clock: 42).Choice));           // paused later
    }

    // ── what it says ──────────────────────────────────────────────────────────────────────

    /// <summary>The content of panel ⑨, from the real world: the title, "You are: side · unit · role", the description, MY side's objectives required first, with the enemy's left out.</summary>
    [Fact]
    public void TheBriefing_SaysTheScenario_WhoIAm_AndMySidesObjectivesOnly()
    {
        Scene();
        var b = new PlayerBriefing();
        b.Refresh(_w);
        Assert.Equal("night-attack — briefing", b.Content.Title);
        Assert.Equal("You are: Blue · Alpha 1-1 · marksman", b.Content.YouAre);
        Assert.Equal("Seize the bridge before dawn and hold it until relieved.", b.Content.Mission);
        Assert.Equal(new[] { "Hold the bridge (required)", "No civilian harm", "Report contact within 30 s" }, b.Content.Objectives);   // ⛔ not "Destroy the bridge"
    }

    [Fact]
    public void TheBriefing_WithNobodyHeldYet_SaysSoAndListsNoObjectives_AndAnEmptyDescriptionIsHonest()
    {
        var c = PlayerBriefing.Build("x", null, default, "", TacticalDesignation.Undefined, Array.Empty<ObjectiveLine>());
        Assert.Equal("You are: not assigned to a unit yet", c.YouAre);
        Assert.Equal("", c.Mission); Assert.Empty(c.Objectives);
        Assert.Equal("automatic rifleman", PlayerBriefing.RoleWords(TacticalDesignation.AutomaticRifleman));
        var longText = PlayerBriefing.Build("x", new string('a', 5000), default, "", TacticalDesignation.Undefined, Array.Empty<ObjectiveLine>());
        Assert.Equal(PlayerBriefing.MissionCap, longText.Mission.Length);   // capped so one ImGui call stays on its stack buffer
    }

    /// <summary>The model is rebuilt only when its inputs change: steady frames rebuild nothing; a new objective status rebuilds once. 🔴 Mutation: drop the early return ⇒ Rebuilds grows.</summary>
    [Fact]
    public void TheModel_IsRebuiltOnlyWhenItsInputsChange()
    {
        Scene();
        var b = new PlayerBriefing();
        for (int i = 0; i < 20; i++) b.Refresh(_w);
        Assert.Equal(1, b.Rebuilds);
        Objective("Secure the road", ForceId.Friend);
        for (int i = 0; i < 20; i++) b.Refresh(_w);
        Assert.Equal(2, b.Rebuilds);
        Assert.Contains("Secure the road", b.Content.Objectives);
    }

    /// <summary>
    /// ⭐ A Player node's REPLICATED world holds no description singleton (the load step stamps it on the Brain only): the title and the Mission come from the run's scenario and its file's header,
    /// read once per change, not per frame. 🔴 Mutation: <c>Rebuild</c> ignoring <c>RunScenario</c> ⇒ the title is "Briefing" and the Mission empty ⇒ red.
    /// </summary>
    [Fact]
    public void OnAPlayersReplicatedWorld_TheTitleAndMissionComeFromTheRunsScenarioFile()
    {
        _man = Man(7, "Alpha 1-1", ForceId.Friend);
        _w.AddComponent(_man, new PossessedBy());
        int reads = 0;
        var b = new PlayerBriefing { DescriptionOf = name => { reads++; return name == "night-attack" ? "Seize the bridge." : null; }, RunScenario = "night-attack" };
        for (int i = 0; i < 10; i++) b.Refresh(_w);
        Assert.Equal("night-attack — briefing", b.Content.Title);
        Assert.Equal("Seize the bridge.", b.Content.Mission);
        Assert.Equal(1, reads);                               // the file is read on a change only
    }


    // ── in a real frame ───────────────────────────────────────────────────────────────────

    private static string ShotPath(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RULES_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-rules-rail");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>The screen drawn in a REAL ImGui frame (Xvfb): shown in the briefing, absent after ▶, and 0 B per steady frame. The screenshot is evidence (never committed).</summary>
    [SkippableFact]
    public void TheBriefing_DrawsInARealFrame_AndCostsZeroBytesWhenSteady()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Scene();
        var briefing = With(ClusterState.OperatingLive);
        var b = new PlayerBriefing();
        using var frame = UiFrameHarness.Begin(1280, 760);
        for (int i = 0; i < 6; i++) frame.Step(() => b.Draw(briefing.Choice, _w));
        Assert.True(b.WasShown);
        Assert.Equal(1, b.Rebuilds);
        frame.Screenshot(ShotPath("player-briefing.png"));

        long before = GC.GetAllocatedBytesForCurrentThread(), after = 0;
        for (int i = 0; i < 10; i++) frame.Step(() => { long s = GC.GetAllocatedBytesForCurrentThread(); b.Draw(briefing.Choice, _w); after += GC.GetAllocatedBytesForCurrentThread() - s; });
        Assert.Equal(0, after);                                                    // 0 B steady: cached strings, no rebuild
        Assert.Equal(1, b.Rebuilds);

        var running = With(ClusterState.OperatingLive, running: true);
        frame.Step(() => b.Draw(running.Choice, _w));
        Assert.False(b.WasShown);                                                   // ▶ hides it
    }
}
