using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Scenarios;

/// <summary>
/// ⭐ <c>CE-295</c> — the session's loads go through the shared sequence: from a loaded cluster it asks for Idle first and
/// sends the load once the cluster reports Idle. <see cref="EditorScenarioSession.LoadForLive"/> used to send one request,
/// which from Live planned an EMPTY path and only copied the files. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §9.
/// </summary>
public sealed class EditorScenarioSessionLoadTests
{
    private static EditorScenarioSession NewSession(FdpEventBus bus)
    {
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        return new EditorScenarioSession(fileService, bus, new EntityRepository(),
                                         () => Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written"));
    }

    private static TransitionStateIntent[] Drain(FdpEventBus bus)
    {
        bus.SwapBuffers();
        return bus.ReadManaged<TransitionStateIntent>().ToArray();
    }

    private static void ClusterIs(FdpEventBus bus, EditorScenarioSession session, ClusterState state)
    {
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state });
        bus.SwapBuffers();
        session.Update();
    }

    [Fact]
    public void LoadForLive_FromLive_UnloadsFirst_ThenLoads()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingLive);

        session.LoadForLive("b");
        var first = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.Idle, first.TargetState);

        ClusterIs(bus, session, ClusterState.Idle);
        var load = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingLive, load.TargetState);
        Assert.Equal("b", load.ScenarioId);
        Assert.Equal("b", session.LoadedScenarioName);
    }

    [Fact]
    public void LoadForLive_FromIdle_LoadsAtOnce()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);

        session.LoadForLive("a");
        var load = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingLive, load.TargetState);
        Assert.Equal("a", load.ScenarioId);
    }

    [Fact]
    public void OpenForEdit_FromEdit_UnloadsFirst_ThenLoadsForEdit()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingEdit);

        session.OpenForEdit("c");
        Assert.Equal(ClusterState.Idle, Assert.Single(Drain(bus)).TargetState);

        ClusterIs(bus, session, ClusterState.Idle);
        var load = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingEdit, load.TargetState);
        Assert.Equal("c", load.ScenarioId);
    }

    /// <summary>
    /// ⭐ <c>CE-4098</c> (AQ108 D16) — File › Open Recording ends in the ONE replay intent <c>ReplaySessionIntents.EnterReplay(exerciseId)</c> (what
    /// <c>POST /replay {exerciseId}</c> publishes), in ONE request from ANY state — no Idle hop (the master plans the unload) — and it drops a
    /// scenario load still waiting for Idle, which would otherwise fire after it and undo it.
    /// 🔴 Red-proof: swap the intent for <c>EnterReview()</c> ⇒ the target-state assertion fails.
    /// </summary>
    [Fact]
    public void OpenRecording_PublishesTheOneReplayIntent_FromAnyState_AndDropsAWaitingLoad()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingLive);
        session.OpenForEdit("waiting");   // asks for Idle first and waits
        Assert.Equal(ClusterState.Idle, Assert.Single(Drain(bus)).TargetState);

        var exercise = Guid.NewGuid();
        session.OpenRecording(exercise);

        var replay = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingReplay, replay.TargetState);
        Assert.Equal(exercise, replay.ExerciseId);

        ClusterIs(bus, session, ClusterState.Idle);
        Assert.Empty(Drain(bus));   // the waiting edit-open was cancelled — it must not follow the replay request
    }

    [Fact]
    public void OpenRecording_WithNoExercise_PublishesNothing()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);

        session.OpenRecording(Guid.Empty);   // Empty means "the master picks" on the wire; a pick always names one

        Assert.Empty(Drain(bus));
    }

    [Fact]
    public void NewExercise_DropsAWaitingLoad()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingLive);

        session.LoadForLive("b");
        session.NewExercise();
        Drain(bus);

        ClusterIs(bus, session, ClusterState.Idle);
        Assert.Empty(Drain(bus));
    }

    /// <summary>⭐ <c>CE-1025</c> (R-1031) — every load request (and New Exercise) leaves the host's dry run FIRST, before anything is asked
    /// of the cluster. 🔴 Red-proof: drop the <c>_leaveRun</c> call in <c>LoadForLive</c> ⇒ red.</summary>
    [Fact]
    public void CE1025_EveryLoad_LeavesTheDryRunFirst()
    {
        var bus = new FdpEventBus();
        int left = 0;
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        var session = new EditorScenarioSession(fileService, bus, new EntityRepository(),
            () => Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written"), leaveRun: () => left++);

        session.LoadForLive("b");
        Assert.Equal(1, left);
        session.OpenForEdit("c");
        Assert.Equal(2, left);
        session.NewExercise();
        Assert.Equal(3, left);
        session.LoadForLive(" ");                                              // no scenario, no load: the run is not touched
        Assert.Equal(3, left);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4097</c> (AQ108 §2.8) — the session IS the run-mode picker's scenario control: <c>IScenarioSession : IRunScenarioControl</c>, and the
    /// six actions are DECLARED ONCE (on the narrow interface), never copied onto the session interface — "no second implementation of the actions".
    /// 🔴 Red-proof: re-declare <c>LoadForLive</c> on <c>IScenarioSession</c> (a second declaration) ⇒ red.
    /// </summary>
    [Fact]
    public void CE4097_TheSessionExtendsTheRunScenarioControl_AndDeclaresNoSecondCopyOfItsActions()
    {
        Assert.True(typeof(Hrot.UI.Common.RunMode.IRunScenarioControl).IsAssignableFrom(typeof(IScenarioSession)));
        Hrot.UI.Common.RunMode.IRunScenarioControl control = NewSession(new FdpEventBus());   // the one class, through the narrow seam
        Assert.NotNull(control);

        var own = typeof(IScenarioSession).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                                                       | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).ToHashSet();
        foreach (var name in new[] { "LoadForLive", "OpenForEdit", "OpenRecording", "SaveCurrent", "TakeCheckpoint", "get_LoadedScenarioName", "LoadedScenarioName" })
            Assert.DoesNotContain(name, own);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4097</c> — ■ in a live run, through the REAL session: the run's scenario (the inventory's) is opened for editing by the session's ordinary
    /// two-leg load — the unload the sequence always sends, then <c>OperatingEdit</c> with that scenario once the cluster reports Idle — and nothing stops at Idle.
    /// 🔴 Red-proof: <c>RunModeChoice.StopLive</c> not calling <c>OpenForEdit</c> ⇒ no <c>OperatingEdit</c> ever follows.
    /// </summary>
    [Fact]
    public void CE4097_StopInALiveRun_ThroughTheRealSession_OpensTheRunsScenarioForEditing()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        var facade = new Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter(bus);
        facade.AttachRunChoice(Hrot.UI.Common.RunMode.RunArmed.Live, session);
        var run = Guid.NewGuid();
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = run });
        bus.PublishManaged(new AssetInventoryUpdateEvent { LocalExercises = new[] { new ExerciseInventoryItem(run, DateTime.UtcNow, TimeSpan.Zero, "hill-attack") } });
        bus.SwapBuffers();
        session.Update();
        facade.Update();
        bus.SwapBuffers();

        facade.Stop();
        var unload = Drain(bus);
        Assert.Single(unload);
        Assert.Equal(ClusterState.Idle, unload[0].TargetState);                // the sequence's first leg …
        ClusterIs(bus, session, ClusterState.Idle);
        var load = Drain(bus);
        var edit = Assert.Single(load);                                        // … and the second: the run's scenario, for editing
        Assert.Equal(ClusterState.OperatingEdit, edit.TargetState);
        Assert.Equal("hill-attack", edit.ScenarioId);
        Assert.Equal(Hrot.UI.Common.RunMode.RunArmed.Live, facade.RunChoice!.Armed);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4097</c> part two (AQ108 D8, D22) — Save in a run, through the REAL session: in a Live run <c>SaveCurrent</c> (what File › Save, Ctrl+S and the toolbar all end in) publishes a
    /// <c>TakeCheckpointIntent</c> and NO <c>ExecuteStorageOpIntent</c> — the scenario is never written; in editing the same call is the ordinary scenario save. The choice installs the intercept on
    /// the session by construction. 🔴 Red-proof: <c>EditorScenarioSession.SaveCurrent</c> without its <c>SaveIntercept</c> line ⇒ the Live row publishes a scenario save.
    /// </summary>
    [Fact]
    public void CE4097_SaveInALiveRun_ThroughTheRealSession_IsACheckpoint_NeverAScenarioWrite()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        var facade = new Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter(bus);
        facade.AttachRunChoice(Hrot.UI.Common.RunMode.RunArmed.Live, session);
        Assert.NotNull(session.SaveIntercept);                                  // installed by the choice's constructor, not by a host remembering to
        session.LoadForLive("hill-attack");                                     // names the scenario (and the load itself is not what is under test)
        bus.SwapBuffers();

        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = Guid.NewGuid() });
        bus.SwapBuffers(); session.Update(); facade.Update(); bus.SwapBuffers();
        session.SaveCurrent();
        bus.SwapBuffers();
        Assert.Single(bus.ReadManaged<TakeCheckpointIntent>());
        Assert.Empty(bus.ReadManaged<ExecuteStorageOpIntent>());                // the scenario was never written

        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });
        bus.SwapBuffers(); session.Update(); facade.Update(); bus.SwapBuffers();
        session.SaveCurrent();
        bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<TakeCheckpointIntent>());
        var save = Assert.Single(bus.ReadManaged<ExecuteStorageOpIntent>());    // editing: the ordinary save
        Assert.Equal(StorageOpType.SaveScenario, save.Operation);
        Assert.Equal("hill-attack", save.ScenarioName);
    }

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (D21) — the session opens a recording AT a moment: <c>OpenRecording(id, atSimSeconds)</c> publishes the ONE replay intent with the sim target (the sim target wins on
    /// either axis), and the one-argument form still opens at the start. 🔴 Red-proof: the two-argument overload dropping <c>simSeconds:</c> ⇒ the target is null.
    /// </summary>
    [Fact]
    public void CE4097_OpenRecording_AtAMoment_PublishesTheReplayIntentWithTheSimTarget()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        var id = Guid.NewGuid();

        session.OpenRecording(id, 42.5);
        var at = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingReplay, at.TargetState);
        Assert.Equal(id, at.ExerciseId);
        Assert.Equal(42.5, at.TargetSimSeconds);

        session.OpenRecording(id);
        var start = Assert.Single(Drain(bus));
        Assert.Null(start.TargetSimSeconds);                                    // its start
    }
}
