using Fdp.Toolkit.Orchestration;
using Hrot.ClusterRunner;
using Hrot.ClusterRunner.Configuration;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3354</c> ⑥ — the command line is a second SENDER of the same two messages (<c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.10b): once the master is up it publishes the map-in-load setting FIRST (when asked) and then the load
/// intent ScenarioLoadSequence builds for HTTP too; once only; from a state other than Idle the unload goes first and the load waits for Idle. No timer: the master holds the load itself.
/// </summary>
public sealed class StartupScenarioLoadTests
{
    private sealed class FakeMaster : IClusterMasterControl
    {
        public readonly List<object> Sent = new();
        public void Transition(TransitionStateIntent intent) => Sent.Add(intent);
        public void SetRunSettings(SetRunSettingsIntent intent) => Sent.Add(intent);
        public void SeekReplay(SeekReplayIntent intent) => Sent.Add(intent);
        public void SetReplayAxis(SetReplayAxisIntent intent) => Sent.Add(intent);
        public void SetReplayLoop(SetReplayLoopIntent intent) => Sent.Add(intent);
        public void Storage(ExecuteStorageOpIntent intent) => Sent.Add(intent);
        public void TakeCheckpoint(TakeCheckpointIntent intent) => Sent.Add(intent);
        public void RestoreCheckpoint(RestoreCheckpointIntent intent) => Sent.Add(intent);
        public void CheckUnsaved(CheckUnsavedIntent intent) => Sent.Add(intent);
        public void Cancel(CancelOperationIntent intent) => Sent.Add(intent);
        public void Dump(ExecuteDiagnosticDumpIntent intent) => Sent.Add(intent);
    }

    private readonly FakeMaster _master = new();
    private ClusterState? _state = ClusterState.Idle;
    private IClusterMasterControl? _control;

    private StartupScenarioLoad New(StartupScenarioLoad.Plan plan) { _control = _master; return new(plan, () => _control, () => _state); }

    /// <summary>
    /// The setting goes out BEFORE the load (the load's per-node stamp reads it); the live-paused load is the HTTP one (TimeMode "Deterministic", a fresh exercise); nothing goes out before the host is up; and it is sent ONCE.
    /// 🔴 Mutations (each compiles): the setting sent after the load ⇒ the order cell reds; <c>_issued</c> never set ⇒ the "once" cell reds; the master/state null-test removed ⇒ the "not yet" cell throws.
    /// </summary>
    [Fact]
    public void CE3354_TheSetting_GoesBeforeTheLoad_TheLoadIsTheHttpOne_AndItIsSentOnce()
    {
        var load = New(new("scn", Live: true, Run: false, new[] { "film-cam" }));
        _control = null; load.Tick();                                         // the host is not up
        Assert.Empty(_master.Sent);
        _control = _master; _state = null; load.Tick();                       // its state is not known yet
        Assert.Empty(_master.Sent);
        _state = ClusterState.Idle;
        load.Tick();

        Assert.Equal(2, _master.Sent.Count);
        Assert.Equal(new[] { "film-cam" }, Assert.IsType<SetRunSettingsIntent>(_master.Sent[0]).MapInLoadNodes);   // first
        var intent = Assert.IsType<TransitionStateIntent>(_master.Sent[1]);
        Assert.Equal((ClusterState.OperatingLive, "scn", "Deterministic"), (intent.TargetState, intent.ScenarioId, intent.TimeMode));
        Assert.NotEqual(Guid.Empty, intent.ExerciseId);
        Assert.Equal(intent.TransactionId, load.LoadRequestId);
        Assert.True(load.Done);
        load.Tick(); load.Tick();
        Assert.Equal(2, _master.Sent.Count);                                  // once only
    }

    /// <summary>--play: the live load carries no time mode (it runs when every node loaded); --load edit: an edit intent with no exercise; no map-in-load: no run-settings intent at all.
    /// 🔴 Mutations (each compiles): <c>startPaused</c> not inverted by Run ⇒ the play cell reds; the edit branch building the live intent ⇒ the edit cell reds.</summary>
    [Fact]
    public void CE3354_PlayRuns_EditOpensForEditing_AndNoSettingMeansNoSettingIntent()
    {
        var play = New(new("scn", Live: true, Run: true, null));
        play.Tick();
        var live = Assert.IsType<TransitionStateIntent>(Assert.Single(_master.Sent));
        Assert.Equal((ClusterState.OperatingLive, (string?)null), (live.TargetState, live.TimeMode));

        _master.Sent.Clear();
        New(new("scn", Live: false, Run: false, null)).Tick();
        var edit = Assert.IsType<TransitionStateIntent>(Assert.Single(_master.Sent));
        Assert.Equal((ClusterState.OperatingEdit, Guid.Empty), (edit.TargetState, edit.ExerciseId));

        _master.Sent.Clear();
        var only = New(new(null, Live: false, Run: false, new[] { "a" }));
        only.Tick();
        Assert.IsType<SetRunSettingsIntent>(Assert.Single(_master.Sent));     // the setting alone
        Assert.True(only.Done);
    }

    /// <summary>
    /// Started from a state other than Idle, the sequence's unload goes FIRST and the load waits (not Done) until the cluster reports Idle — then it goes out, once.
    /// 🔴 Mutation: <c>Pump</c> not called ⇒ the load never goes out.
    /// </summary>
    [Fact]
    public void CE3354_FromANonIdleCluster_TheUnloadGoesFirst_AndTheLoadWaitsForIdle()
    {
        _state = ClusterState.OperatingLive;
        var load = New(new("scn", Live: true, Run: false, null));
        load.Tick();
        Assert.Equal(ClusterState.Idle, Assert.IsType<TransitionStateIntent>(Assert.Single(_master.Sent)).TargetState);   // the unload
        Assert.False(load.Done);
        load.Tick();
        Assert.Single(_master.Sent);                                          // still waiting

        _state = ClusterState.Idle;
        load.Tick();
        Assert.Equal(2, _master.Sent.Count);
        Assert.Equal((ClusterState.OperatingLive, "scn"), (((TransitionStateIntent)_master.Sent[1]).TargetState, ((TransitionStateIntent)_master.Sent[1]).ScenarioId));
        Assert.True(load.Done);
    }

    /// <summary>The plan from a validated configuration: an empty <c>--map-in-load</c> means this process' own nodes; names are kept; nothing asked ⇒ no plan.</summary>
    [Fact]
    public void CE3354_ThePlan_ComesFromTheConfiguration()
    {
        HrotRunnerConfiguration Cfg(string[]? map, string scenario = "scn", bool play = false)
        { var c = new HrotRunnerConfiguration { ModeString = "all", NoWait = true, ScenarioName = scenario, Play = play, MapInLoad = map }; c.Validate(); return c; }
        var own = new[] { "IG-100", "SimHost-1" };

        Assert.Null(StartupScenarioLoad.Plan.From(Cfg(null, scenario: ""), own));
        var p = StartupScenarioLoad.Plan.From(Cfg(Array.Empty<string>(), play: true), own)!;
        Assert.Equal((true, true, "scn"), (p.Live, p.Run, p.Scenario));
        Assert.Equal(own, p.MapInLoadNodes);
        Assert.Equal(new[] { "film-cam" }, StartupScenarioLoad.Plan.From(Cfg(new[] { " film-cam " }), own)!.MapInLoadNodes);
        Assert.Null(StartupScenarioLoad.Plan.From(Cfg(null), own)!.MapInLoadNodes);
    }
}
