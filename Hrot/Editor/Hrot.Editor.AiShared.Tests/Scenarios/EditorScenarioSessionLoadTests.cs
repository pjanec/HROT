using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.ScenarioEditor.Services;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Scenarios;

/// <summary>
/// ⭐ <c>CE-295</c> — the session's loads go through the shared sequence: from a loaded cluster it asks for Idle first and
/// sends the load once the cluster reports Idle. <see cref="EditorScenarioSession.LoadForLive"/> used to send one request,
/// which from Live planned an EMPTY path and only copied the files. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §9.
/// </summary>
public sealed class EditorScenarioSessionLoadTests
{
    // CE-1436 - the node's ONE cluster view over the bus; the tests advance it (as ClusterSlave.Tick does) before the session / facade frame
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FdpEventBus, Fdp.Toolkit.Orchestration.ClusterView> Views = new();
    private static Fdp.Toolkit.Orchestration.ClusterView ViewOf(FdpEventBus bus) => Views.GetValue(bus, b => new Fdp.Toolkit.Orchestration.ClusterView(b));

    private static EditorScenarioSession NewSession(FdpEventBus bus)
    {
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        return new EditorScenarioSession(fileService, bus, () => ViewOf(bus), new EntityRepository(),
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
        ViewOf(bus).Update(); session.Update();
    }

    // ── ⭐⭐⭐ CE-4101 (AQ108 §2.11a) — the session's public verbs ARE the guarded door; the raw verbs are explicit members only the run choice is handed ─────────────

    private sealed class Asks : IUnsavedChangesQuery
    {
        public UnsavedAnswer Answer = UnsavedAnswer.Unchanged;
        public int Asked;
        public void Ask(Action<UnsavedAnswer> reply) { Asked++; reply(Answer); }
    }

    private static (RunModeChoice Choice, ClusterTimeTransportAdapter Adapter, Asks Q) WithChoice(FdpEventBus bus, EditorScenarioSession session, UnsavedAnswer answer = UnsavedAnswer.Unchanged)
    {
        var adapter = new ClusterTimeTransportAdapter(bus, () => ViewOf(bus));
        var q = new Asks { Answer = answer };
        return (adapter.AttachRunChoice(RunArmed.Preview, session.RunControl, q), adapter, q);   // ⭐ the RAW verbs go to the choice only
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> — the rail that FAILS when production code can reach a raw open: <see cref="IScenarioSession"/> (what every caller holds) does not extend the raw <see cref="IRunScenarioControl"/>, and the class's
    /// RAW verbs are EXPLICIT (non-public) interface members — the session's public <c>OpenForEdit</c> / <c>LoadForLive</c> / <c>OpenRecording</c> are the door's. 🔴 Mutation: <c>IScenarioSession : IRunScenarioControl</c> again, or
    /// a raw verb made public ⇒ red.
    /// </summary>
    [Fact]
    public void CE4101_NoUnguardedPublicOpen_TheRawVerbsAreExplicitMembersOfTheRawInterfaceOnly()
    {
        Assert.False(typeof(IRunScenarioControl).IsAssignableFrom(typeof(IScenarioSession)));
        var map = typeof(EditorScenarioSession).GetInterfaceMap(typeof(IRunScenarioControl));
        foreach (var verb in new[] { "OpenForEdit", "LoadForLive", "OpenRecording" })
        {
            var targets = map.TargetMethods.Where(m => m.Name.EndsWith("." + verb, StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(targets);
            Assert.All(targets, m => Assert.False(m.IsPublic, $"the raw {verb} must be an explicit interface member, not a public one"));
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> — every IN verb a caller of the REAL session can reach (the menu, the pickers, New-from-a-recipe, the director's load, the Orchestrator panel's <i>Load into Live</i>, an Open Recording pick all end in
    /// these) asks <i>Stop &amp; Load</i> over a live run in the run choice's pending slot and PUBLISHES NOTHING until the button; Cancel publishes nothing at all; the OUT verb (New Exercise) stops a run without asking.
    /// 🔴 Red-proof: the session's <c>OpenForEdit</c> calling the raw verb ⇒ the first <c>Pending</c> is null and an Idle intent is already on the bus.
    /// </summary>
    [Fact]
    public void CE4101_EveryInVerbOfTheRealSession_OverALiveRun_AsksStopAndLoad_AndPublishesNothingUntilConfirmed()
    {
        var exercise = Guid.NewGuid();
        var verbs = new (string Name, Action<EditorScenarioSession> Call, Func<TransitionStateIntent, bool> FirstIntent)[]
        {
            ("OpenForEdit",    s => s.OpenForEdit("x"),            t => t.TargetState == ClusterState.Idle),                                         // the load's own unload first (CE-295)
            ("LoadForLive",    s => s.LoadForLive("x"),            t => t.TargetState == ClusterState.Idle),
            ("OpenRecording",  s => s.OpenRecording(exercise),     t => t.TargetState == ClusterState.OperatingReplay && t.ExerciseId == exercise),
        };
        foreach (var verb in verbs)
        {
            var bus = new FdpEventBus();
            var session = NewSession(bus);
            var (choice, _, q) = WithChoice(bus, session);
            ClusterIs(bus, session, ClusterState.OperatingLive);

            verb.Call(session);
            var ask = Assert.IsType<RunModeConfirmation>(choice.Pending);
            Assert.Equal(new[] { RunModeRunGuard.StopAndLoad, "Cancel" }, ask.Buttons.Select(b => b.Label));
            Assert.Empty(Drain(bus));                                                    // asked, nothing published
            Assert.Equal(0, q.Asked);                                                    // a run holds no scenario of its own: no unsaved round
            ask.Cancel();
            Assert.Empty(Drain(bus));                                                    // Cancel: the run is untouched

            verb.Call(session);
            choice.Pending!.Resolve(choice.Pending.Buttons[0]);
            Assert.True(verb.FirstIntent(Assert.Single(Drain(bus))), verb.Name);
        }

        // the OUT door over a run: stopped without asking
        var b2 = new FdpEventBus();
        var s2 = NewSession(b2);
        var (c2, _, _) = WithChoice(b2, s2);
        ClusterIs(b2, s2, ClusterState.OperatingLive);
        s2.NewExercise();
        Assert.Null(c2.Pending);
        Assert.Equal(ClusterState.Idle, Assert.Single(Drain(b2)).TargetState);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> (the OUT door; <i>New Exercise</i> used to discard silently) — over UNSAVED edits <c>NewExercise</c> asks <i>Save / Discard / Cancel</i> FIRST: nothing is published while it asks; Cancel leaves the scenario
    /// open; Save publishes the scenario save and THEN the Idle intent; Discard only the Idle intent; a CLEAN scenario goes at once. Opens over unsaved edits (every IN verb) ask the same.
    /// 🔴 Red-proof: <c>NewExercise</c> calling <c>RawLeave</c> directly ⇒ the Idle intent is on the bus before any question.
    /// </summary>
    [Fact]
    public void CE4101_OverUnsavedEdits_NewExerciseAndEveryOpen_AskSaveDiscardCancel_BeforeAnythingIsPublished()
    {
        var exercise = Guid.NewGuid();
        var moves = new (string Name, Action<EditorScenarioSession> Call)[]
        {
            ("NewExercise",   s => s.NewExercise()),
            ("OpenForEdit",   s => s.OpenForEdit("other")),
            ("LoadForLive",   s => s.LoadForLive("other")),
            ("OpenRecording", s => s.OpenRecording(exercise)),
        };
        foreach (var move in moves)
        {
            EditorScenarioSession Editing(UnsavedAnswer answer, out FdpEventBus bus, out RunModeChoice choice)
            {
                bus = new FdpEventBus();
                var session = NewSession(bus);
                (choice, _, _) = WithChoice(bus, session, answer);
                session.OpenForEdit("hill");                                             // from Idle: out at once, and the name is remembered
                Drain(bus);
                ClusterIs(bus, session, ClusterState.OperatingEdit);
                return session;
            }

            var s = Editing(UnsavedAnswer.Changed, out var b, out var c);
            move.Call(s);
            var ask = Assert.IsType<RunModeConfirmation>(c.Pending);
            Assert.Equal(new[] { "Save", "Discard", "Cancel" }, ask.Buttons.Select(x => x.Label));
            Assert.Empty(Drain(b));                                                      // asked FIRST — nothing out yet
            ask.Cancel();
            Assert.Empty(Drain(b));

            move.Call(s);
            c.Pending!.Resolve(c.Pending.Buttons[0]);                                    // Save
            b.SwapBuffers();
            var save = Assert.Single(b.ReadManaged<ExecuteStorageOpIntent>());
            Assert.Equal(StorageOpType.SaveScenario, save.Operation);
            Assert.Equal("hill", save.ScenarioName);
            Assert.NotEmpty(b.ReadManaged<TransitionStateIntent>());                     // …and then the move itself

            var d = Editing(UnsavedAnswer.Changed, out var b3, out var c3);
            move.Call(d);
            c3.Pending!.Resolve(c3.Pending.Buttons[1]);                                  // Discard
            b3.SwapBuffers();
            Assert.NotEmpty(b3.ReadManaged<TransitionStateIntent>());
            Assert.Empty(b3.ReadManaged<ExecuteStorageOpIntent>());                      // nothing saved

            var clean = Editing(UnsavedAnswer.Unchanged, out var b4, out var c4);
            move.Call(clean);
            Assert.Null(c4.Pending);
            Assert.NotEmpty(Drain(b4));
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4101</c> (the ONE Idle intent) — New Exercise, the transport's raw ■ (a host with no run choice) and <see cref="ScenarioLoadSequence.UnloadIntent"/> publish the SAME intent apart from the TransactionId; a load's
    /// own unload is it too. 🔴 Red-proof: <c>RawLeave</c> building its own intent (any extra field) ⇒ the equality fails.
    /// </summary>
    [Fact]
    public void CE4101_NewExerciseAndTheRawStopAndTheLoadsUnload_PublishTheOneIdleIntent()
    {
        static TransitionStateIntent Normalised(TransitionStateIntent t, Guid id) { t.TransactionId = id; return t; }
        var id = Guid.NewGuid();
        var one = Normalised(ScenarioLoadSequence.UnloadIntent(), id);

        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingLive);
        session.NewExercise();
        Assert.Equal(one, Normalised(Assert.Single(Drain(bus)), id));

        var rawBus = new FdpEventBus();
        var rawSession = NewSession(rawBus);
        var rawStop = new ClusterTimeTransportAdapter(rawBus, () => ViewOf(rawBus));      // no run choice attached: ■ is the raw one
        ClusterIs(rawBus, rawSession, ClusterState.OperatingLive);
        rawStop.Stop();
        Assert.Equal(one, Normalised(Assert.Single(Drain(rawBus)), id));

        var loadBus = new FdpEventBus();
        var loadSession = NewSession(loadBus);
        ClusterIs(loadBus, loadSession, ClusterState.OperatingLive);
        loadSession.LoadForLive("b");                                                    // unbound door: a run is stopped
        Assert.Equal(one, Normalised(Assert.Single(Drain(loadBus)), id));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4101</c> — <c>then</c> of a guarded open (New-from-a-recipe saves the seed as the new name AFTER the open) runs only once the open WENT OUT: not while the question is open, never after Cancel.
    /// 🔴 Red-proof: the door running <c>then</c> before <c>Admit</c> ⇒ the first assertion fails.
    /// </summary>
    [Fact]
    public void CE4101_TheThenOfAGuardedOpen_RunsOnlyOnceTheOpenWentOut()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        var (choice, _, _) = WithChoice(bus, session);
        ClusterIs(bus, session, ClusterState.OperatingLive);
        int ran = 0;

        session.Door.Open("seed", then: () => ran++);
        Assert.Equal(0, ran);
        choice.Pending!.Cancel();
        Assert.Equal(0, ran);                                                            // cancelled: the save-as must not follow

        session.Door.Open("seed", then: () => ran++);
        choice.Pending!.Resolve(choice.Pending.Buttons[0]);
        Assert.Equal(1, ran);
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
        var session = new EditorScenarioSession(fileService, bus, () => ViewOf(bus), new EntityRepository(),
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
    /// ⭐⭐ <c>CE-4097</c> (AQ108 §2.8) → <c>CE-4101</c> — the ONE session class IS the run-mode picker's scenario control (<see cref="EditorScenarioSession.RunControl"/>, the RAW verbs) AND the guarded
    /// <see cref="IScenarioSession"/> every other caller holds; the two interfaces are disjoint (CE-4101: <c>IScenarioSession</c> no longer extends the raw one), and <c>RunControl</c> is the session itself — no second implementation.
    /// 🔴 Red-proof: <c>RunControl</c> returning a different object ⇒ the <c>Same</c> fails.
    /// </summary>
    [Fact]
    public void CE4097_TheOneSessionClass_IsBothTheRawRunControl_AndTheGuardedSession()
    {
        Assert.False(typeof(Hrot.UI.Common.RunMode.IRunScenarioControl).IsAssignableFrom(typeof(IScenarioSession)));
        Assert.True(typeof(Hrot.UI.Common.RunMode.IRunScenarioControl).IsAssignableFrom(typeof(EditorScenarioSession)));
        Assert.True(typeof(IScenarioSession).IsAssignableFrom(typeof(EditorScenarioSession)));
        var session = NewSession(new FdpEventBus());
        Assert.Same(session, session.RunControl);   // the one class, through the narrow seam
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
        var facade = new Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter(bus, () => ViewOf(bus));
        facade.AttachRunChoice(Hrot.UI.Common.RunMode.RunArmed.Live, session);
        var run = Guid.NewGuid();
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = run });
        bus.PublishManaged(new AssetInventoryUpdateEvent { LocalExercises = new[] { new ExerciseInventoryItem(run, DateTime.UtcNow, TimeSpan.Zero, "hill-attack") } });
        bus.SwapBuffers();
        ViewOf(bus).Update(); session.Update();
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
        var facade = new Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter(bus, () => ViewOf(bus));
        facade.AttachRunChoice(Hrot.UI.Common.RunMode.RunArmed.Live, session);
        Assert.NotNull(session.SaveIntercept);                                  // installed by the choice's constructor, not by a host remembering to
        session.LoadForLive("hill-attack");                                     // names the scenario (and the load itself is not what is under test)
        bus.SwapBuffers();

        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = Guid.NewGuid() });
        bus.SwapBuffers(); ViewOf(bus).Update(); session.Update(); facade.Update(); bus.SwapBuffers();
        session.SaveCurrent();
        bus.SwapBuffers();
        Assert.Single(bus.ReadManaged<TakeCheckpointIntent>());
        Assert.Empty(bus.ReadManaged<ExecuteStorageOpIntent>());                // the scenario was never written

        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });
        bus.SwapBuffers(); ViewOf(bus).Update(); session.Update(); facade.Update(); bus.SwapBuffers();
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

    /// <summary>
    /// ⭐⭐ CE-1436 (R-3066; AQ108 §2.8a) — the session's cluster state IS the node's <c>ClusterView</c> state (it folds none of its own): the view (fed by its owner, here on another bus) says
    /// OperatingLive while the session's own bus carries a broadcast saying Edit ⇒ <c>CurrentClusterState</c> is Live, and the load sequence reads the same state (a load from Live
    /// unloads to Idle first). With no view at all it reads Idle.
    /// 🔴 Red-proof: re-adding the <c>ClusterStateUpdateEvent</c> drain (and its own state field) to <c>EditorScenarioSession.Update</c> ⇒ the state follows the bus (Edit) and the first row fails.
    /// </summary>
    [Fact]
    public void CE1436_TheSessionsClusterState_IsTheViewsState_NotItsOwnBusFold()
    {
        var nodeBus = new FdpEventBus();
        var view = new Fdp.Toolkit.Orchestration.ClusterView(nodeBus);
        nodeBus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive });
        nodeBus.SwapBuffers(); view.Update();

        var ownBus = new FdpEventBus();
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        var session = new EditorScenarioSession(fileService, ownBus, () => view, new EntityRepository(),
                                                () => Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written"));
        ownBus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });
        ownBus.SwapBuffers();
        session.Update();

        Assert.Equal(ClusterState.OperatingLive, session.CurrentClusterState);
        session.LoadForLive("b");
        var first = Assert.Single(Drain(ownBus));
        Assert.Equal(ClusterState.Idle, first.TargetState);                    // from Live: the unload first

        var none = new EditorScenarioSession(fileService, new FdpEventBus(), () => null, new EntityRepository(),
                                             () => Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written"));
        Assert.Equal(ClusterState.Idle, none.CurrentClusterState);
    }
}
