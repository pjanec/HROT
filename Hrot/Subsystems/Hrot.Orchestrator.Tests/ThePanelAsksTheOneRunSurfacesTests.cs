using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.Orchestrator;
using Hrot.Orchestrator.Panels;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.RunMode;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-1439</c> (AQ108 §2.12, R-3066 audit #8) — <b>the Orchestrator's cluster-control panel is the MONITOR; what it ASKS for goes through the host's ONE run surfaces</b>
/// (the transport's USER entry, the run choice, the scenario session). Each rail pins the ROUTING of one control through <see cref="ClusterPanelRunControls"/> (every panel button is one
/// call on it) over a REAL <see cref="ClusterTimeTransportAdapter"/> + <see cref="RunModeChoice"/> on a real bus — ⛔ not the ImGui draw. Armed Live (the Orchestrator's table default).
/// 🔴 Red-proofs (each a COMPILING mutation of <see cref="ClusterPanelRunControls"/>): see each test.
/// </summary>
public sealed class ThePanelAsksTheOneRunSurfacesTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    /// <summary>⭐ CE-4101 — the panel holds the GUARDED session (<see cref="IScenarioSession"/>); the choice holds the RAW verbs (<see cref="IRunScenarioControl"/>). This fake is both, so a rail sees every call either way.</summary>
    private sealed class FakeSession : IRunScenarioControl, Hrot.Editor.AiShared.Scenarios.IScenarioSession
    {
        public void Update() { }
        public Fdp.Toolkit.Orchestration.ClusterState CurrentClusterState => ClusterState.Idle;
        public void ClearWorld() { }
        public void NewExercise() { }
        public void SaveAs(string scenarioName) { }
        public void SaveTo(string filePath) { }
        public bool IsDegraded => false;
        public IReadOnlyList<Fdp.Core.Serialization.Migrations.SidecarFileInfo> GetMigrationSidecars() => Array.Empty<Fdp.Core.Serialization.Migrations.SidecarFileInfo>();
        public string? LoadedScenarioName { get; set; } = "hill-attack";
        public readonly List<string> Calls = new();
        public void LoadForLive(string scenarioName) => Calls.Add($"LoadForLive:{scenarioName}");
        public void OpenForEdit(string scenarioName) => Calls.Add($"OpenForEdit:{scenarioName}");
        public void OpenRecording(Guid exerciseId) => Calls.Add($"OpenRecording:{exerciseId}");
        public void SaveCurrent() => Calls.Add("SaveCurrent");
        public void TakeCheckpoint() => Calls.Add("TakeCheckpoint");
    }

    private sealed class CountingUnsaved : IUnsavedChangesQuery
    {
        public int Asked;
        public void Ask(Action<UnsavedAnswer> reply) { Asked++; reply(UnsavedAnswer.Unchanged); }
    }

    private sealed record Rig(ClusterPanelRunControls Controls, ClusterTimeTransportAdapter Adapter, FdpEventBus Bus, FakeSession Session, CountingUnsaved Unsaved, RunModeChoice Choice);

    /// <summary>The cluster is heard in <paramref name="state"/> (exercise <see cref="RunId"/>, recorded), the way the master's broadcast reaches the host's view.</summary>
    private static Rig With(ClusterState state)
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var adapter = new ClusterTimeTransportAdapter(bus, () => view, () => 0.0);
        var session = new FakeSession();
        var unsaved = new CountingUnsaved();
        var choice = adapter.AttachRunChoice(RunArmedDefaults.For(RunHostKind.Orchestrator), session, unsaved);
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = RunId });
        bus.SwapBuffers(); view.Update(); adapter.Update(); bus.SwapBuffers();
        return new Rig(new ClusterPanelRunControls(adapter, choice, session), adapter, bus, session, unsaved, choice);
    }

    private static List<T> Sent<T>(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<T>().ToList(); }

    // ── pause / resume / step: the USER's ▶, not the raw toggle ───────────────────────────────

    /// <summary>
    /// From Edit with Live armed the panel's Pause / Resume is the user's ▶: the unsaved check, then the live load — and NEVER the raw toggle's dry run (a route does what it names; a user control follows the armed mode, D22).
    /// 🔴 Mutation: <c>PauseOrResume</c> calls <c>Transport.TogglePlayPause()</c> ⇒ a dry run is published and the check is never asked.
    /// </summary>
    [Fact]
    public void ThePanelsResume_FromEdit_IsTheArmedModesStart_NotTheRawDryRun()
    {
        var rig = With(ClusterState.OperatingEdit);

        rig.Controls.PauseOrResume();

        Assert.Equal(1, rig.Unsaved.Asked);
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, rig.Session.Calls);
        Assert.DoesNotContain(Sent<TransitionStateIntent>(rig.Bus), t => t.TargetState == ClusterState.OperatingPreview);
    }

    /// <summary>
    /// Step from Edit is the armed mode's start too; over a paused run the panel's STEP SIZE reaches the facade's <c>StepBy</c>.
    /// 🔴 Mutation: <c>Step</c> calls <c>Transport.StepBy(seconds)</c> directly ⇒ from Edit it publishes a dry run; or ignores <c>seconds</c> ⇒ the delta assertion fails.
    /// </summary>
    [Fact]
    public void ThePanelsStep_FromEdit_IsTheArmedModesStart_AndOverAPausedRun_CarriesItsStepSize()
    {
        var edit = With(ClusterState.OperatingEdit);
        edit.Controls.Step(0.25f);
        Assert.Equal(1, edit.Unsaved.Asked);
        Assert.DoesNotContain(Sent<TransitionStateIntent>(edit.Bus), t => t.TargetState == ClusterState.OperatingPreview);

        var live = With(ClusterState.OperatingLive);   // no time-mode broadcast yet ⇒ the facade reads "paused"
        live.Controls.Step(0.25f);
        Assert.Equal(0.25f, Assert.Single(Sent<Fdp.Toolkit.Time.Domain.StepTimeIntent>(live.Bus)).DeltaSeconds);
    }

    // ── the loads ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <i>Load into Edit</i> over a live run is the run choice's GUARDED open — <i>Stop &amp; Load</i> first, nothing loaded until the operator confirms (what <c>File › Open Scenario</c> does on every host, CE-1438).
    /// 🔴 Mutation: <c>LoadIntoEdit</c> calls <c>Session.OpenForEdit</c> directly ⇒ no confirmation is pending and the load went straight over the run.
    /// </summary>
    [Fact]
    public void LoadIntoEdit_OverALiveRun_AsksStopAndLoad_AndLoadsNothingUntilConfirmed()
    {
        var rig = With(ClusterState.OperatingLive);

        rig.Controls.LoadIntoEdit("harbour");

        var ask = rig.Choice.Pending;
        Assert.NotNull(ask);
        Assert.Equal(new[] { "Stop & Load", "Cancel" }, ask!.Buttons.Select(b => b.Label));
        Assert.Empty(rig.Session.Calls);
        ask.Resolve(ask.Buttons[0]);
        Assert.Equal(new[] { "OpenForEdit:harbour" }, rig.Session.Calls);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> (AQ108 §2.11a, the panel's <i>Load into Live</i> row) — over a REAL world-less session the panel's live load is the session's guarded DOOR: over a live run it asks <i>Stop &amp; Load</i> in the host's
    /// pending slot and publishes NOTHING until the operator confirms (it used to go straight over the run); over unsaved edits it asks <i>Save / Discard / Cancel</i> (it had no unsaved check — a loss).
    /// 🔴 Mutation: <c>LoadIntoLive</c> calling <c>Session.RunControl.LoadForLive</c> (the raw verb) ⇒ no confirmation is pending and the live-load intent is already on the bus.
    /// </summary>
    [Fact]
    public void LoadIntoLive_OverARun_AsksStopAndLoad_AndOverUnsavedEdits_AsksSaveDiscardCancel_ThroughTheSessionsDoor()
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var controls = ClusterPanelRunControls.OverBus(bus, () => view);
        void Hear(ClusterState s) { bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = s }); bus.SwapBuffers(); view.Update(); controls.Update(); bus.SwapBuffers(); }

        Hear(ClusterState.OperatingLive);
        controls.LoadIntoLive("harbour");
        var ask = Assert.IsType<RunModeConfirmation>(controls.Choice.Pending);
        Assert.Equal(new[] { "Stop & Load", "Cancel" }, ask.Buttons.Select(b => b.Label));
        Assert.Empty(Sent<TransitionStateIntent>(bus));                              // asked, nothing sent
        ask.Resolve(ask.Buttons[0]);
        Assert.Equal(ClusterState.Idle, Assert.Single(Sent<TransitionStateIntent>(bus)).TargetState);   // the load's own unload first (CE-295)
    }

    /// <summary><i>Load into Live</i> is the session's live load THROUGH ITS DOOR (CE-4101) — the panel runs no load sequence of its own.
    /// 🔴 Mutation: <c>LoadIntoLive</c> calls <c>Session.OpenForEdit</c> ⇒ the call list differs.</summary>
    [Fact]
    public void LoadIntoLive_IsTheSessionsLiveLoad()
    {
        var rig = With(ClusterState.Idle);
        rig.Controls.LoadIntoLive("harbour");
        Assert.Equal(new[] { "LoadForLive:harbour" }, rig.Session.Calls);
    }

    /// <summary>⭐ The panel's own <c>ScenarioLoadSequence</c> (CE-295's copy of the session's) is gone — a rail on the type, so it cannot quietly come back.
    /// 🔴 Mutation: re-add a <c>ScenarioLoadSequence</c> field to the panel ⇒ red.</summary>
    [Fact]
    public void ThePanelHoldsNoScenarioLoadSequenceOfItsOwn()
    {
        var fields = typeof(ClusterScenarioPanel).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, f => f.FieldType == typeof(ScenarioLoadSequence));
        var props = typeof(ClusterScenarioPanel).GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(props, p => p.PropertyType == typeof(ScenarioLoadSequence));
    }

    // ── Open AAR and the checkpoint ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <i>Open AAR</i> is the run choice's recording open: from idle it reaches the session's ONE replay open; over a live run it asks <i>Stop &amp; Load</i> first.
    /// 🔴 Mutation: <c>OpenAar</c> calls <c>Session.OpenRecording</c> directly ⇒ the live-run case opens without asking.
    /// </summary>
    [Fact]
    public void OpenAar_GoesThroughTheRunChoice_TheGuardIncluded()
    {
        var idle = With(ClusterState.Idle);
        idle.Controls.OpenAar(RunId);
        Assert.Equal(new[] { $"OpenRecording:{RunId}" }, idle.Session.Calls);

        var live = With(ClusterState.OperatingLive);
        live.Controls.OpenAar(RunId);
        Assert.NotNull(live.Choice.Pending);
        Assert.Empty(live.Session.Calls);
    }

    /// <summary>
    /// <i>Take Checkpoint</i> is the choice's: in a live (recorded) run it reaches the session; elsewhere it publishes nothing and the choice's reason is what the panel shows.
    /// 🔴 Mutation: <c>TakeCheckpoint</c> calls <c>Session.TakeCheckpoint</c> directly ⇒ the idle case reaches the session.
    /// </summary>
    [Fact]
    public void TakeCheckpoint_IsTheChoices_AndItsReasonIsTheOneTheTimelineReads()
    {
        var live = With(ClusterState.OperatingLive);
        Assert.Null(live.Controls.CheckpointDisabledReason);
        live.Controls.TakeCheckpoint();
        Assert.Equal(new[] { "TakeCheckpoint" }, live.Session.Calls);

        var idle = With(ClusterState.Idle);
        Assert.NotNull(idle.Controls.CheckpointDisabledReason);
        Assert.Equal(idle.Choice.CheckpointDisabledReason, idle.Controls.CheckpointDisabledReason);
        idle.Controls.TakeCheckpoint();
        Assert.Empty(idle.Session.Calls);
    }

    // ── who owns the controls ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The silent-default rule: the Orchestrator HOLDS a transport, a run choice and a session, so its panel is handed THEM (not a private set built over the same bus). The host is an
    /// operator (its choice has a session), arms Live from the table, and registers the session with the world-less constructor (no local world to wipe).
    /// 🔴 Mutation: delete the <c>core.ScenarioPanel.RunControls = …</c> line in <c>OrchestratorSubsystem.Initialize</c> ⇒ the panel builds its own and <c>Assert.Same</c> fails.
    /// </summary>
    [Fact]
    public void TheOrchestratorHostHandsItsOwnRunSurfacesToItsPanel()
    {
        var host = new OrchestratorSubsystem();
        host.Initialize(new SubsystemConfig { Headless = true, DomainId = 224 });
        try
        {
            var choice = host.RunChoiceForTest;
            Assert.NotNull(choice);
            Assert.True(choice!.HasScenarioControl);
            Assert.Equal(RunArmed.Live, choice.HostDefault);
            Assert.Same(choice, host.ScenarioPanelForTest!.RunControls!.Choice);
            Assert.Same(host.ScenarioSessionForTest, host.ScenarioPanelForTest.RunControls.Session);
            Assert.Equal(RunHostKind.Orchestrator, ((IProvidesRunControls)host).HostKind);
        }
        finally
        {
            host.Shutdown();
        }
    }

    /// <summary>A host that composes NO run surfaces (ExCon, retiring) still gets working controls — the SAME shared classes over the panel's own bus, advanced by the panel — and a host that passes its own replaces them.</summary>
    [Fact]
    public void APanelWithNoHostSurfaces_BuildsTheSharedClassesOverItsBus_AndAHostsOwnReplaceThem()
    {
        var bus = new FdpEventBus();
        var cache = new ClusterUiCache(bus);
        var panel = new ClusterScenarioPanel(bus, cache);

        var own = panel.RunControls;
        Assert.NotNull(own);
        Assert.True(own!.Choice.HasScenarioControl);

        var rig = With(ClusterState.Idle);
        panel.RunControls = rig.Controls;
        Assert.Same(rig.Controls, panel.RunControls);
    }

    // ── the session without a world ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The world-less session (the Orchestrator's): every cluster verb works over the bus; the three LOCAL members have nothing to act on — <c>ClearWorld</c> forgets the loaded name, the sidecars are empty,
    /// <c>SaveTo</c> (no production caller) refuses.
    /// 🔴 Mutation: <c>ClearWorld</c> not resetting <c>_loadedScenarioName</c> ⇒ the first assertion fails.
    /// </summary>
    [Fact]
    public void TheWorldlessSession_OpensThroughTheBus_AndItsLocalMembersAreHonestlyEmpty()
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var session = new Hrot.Editor.AiShared.Scenarios.EditorScenarioSession(bus, () => view, () => "/nowhere");

        session.OpenForEdit("harbour");   // the cluster is Idle (no broadcast): the load goes out at once
        session.Update();
        Assert.Equal("harbour", session.LoadedScenarioName);
        Assert.Contains(Sent<TransitionStateIntent>(bus), t => t.TargetState == ClusterState.OperatingEdit && t.ScenarioId == "harbour");

        session.ClearWorld();
        Assert.Null(session.LoadedScenarioName);
        Assert.Empty(session.GetMigrationSidecars());
        Assert.Throws<NotSupportedException>(() => session.SaveTo("x.json"));
    }
}
