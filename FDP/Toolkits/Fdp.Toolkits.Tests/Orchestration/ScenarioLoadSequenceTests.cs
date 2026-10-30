using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests;

/// <summary>
/// ⭐ <c>CE-295</c> — the ONE way to load a scenario: from Idle the load goes out at once; from any other state an Idle
/// request goes out first and the load waits until the cluster reports Idle. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §9.
/// </summary>
public class ScenarioLoadSequenceTests
{
    private readonly List<TransitionStateIntent> _sent = new();
    private ClusterState _state = ClusterState.Idle;

    private ScenarioLoadSequence New(Action<TransitionStateIntent>? beforeLoad = null)
        => new(_sent.Add, () => _state, beforeLoad);

    private static TransitionStateIntent Load(string scenario, ClusterState target = ClusterState.OperatingLive)
        => new() { TransactionId = Guid.NewGuid(), TargetState = target, ScenarioId = scenario, ExerciseId = Guid.NewGuid() };

    [Fact]
    public void FromIdle_TheLoadGoesOutAtOnce()
    {
        var seq = New();
        seq.Request(Load("a"));

        var sent = Assert.Single(_sent);
        Assert.Equal(ClusterState.OperatingLive, sent.TargetState);
        Assert.Equal("a", sent.ScenarioId);
        Assert.False(seq.IsWaitingForIdle);
    }

    [Fact]
    public void FromLive_IdleGoesFirst_AndTheLoadWaitsForIdle()
    {
        _state = ClusterState.OperatingLive;
        var seq = New();
        seq.Request(Load("b"));

        var idle = Assert.Single(_sent);
        Assert.Equal(ClusterState.Idle, idle.TargetState);
        Assert.Null(idle.ScenarioId);
        Assert.True(seq.IsWaitingForIdle);

        _state = ClusterState.UnloadingLive;
        Assert.False(seq.Pump());              // not Idle yet: nothing more goes out
        Assert.Single(_sent);

        _state = ClusterState.Idle;
        Assert.True(seq.Pump());
        Assert.Equal(2, _sent.Count);
        Assert.Equal(ClusterState.OperatingLive, _sent[1].TargetState);
        Assert.Equal("b", _sent[1].ScenarioId);
        Assert.False(seq.Pump());              // sent once
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public void FromEdit_ToEdit_AlsoUnloadsFirst()
    {
        _state = ClusterState.OperatingEdit;
        var seq = New();
        seq.Request(Load("c", ClusterState.OperatingEdit));
        Assert.Equal(ClusterState.Idle, Assert.Single(_sent).TargetState);
    }

    [Fact]
    public void ANewerLoadReplacesAWaitingOne_AndCancelDropsIt()
    {
        _state = ClusterState.OperatingLive;
        var seq = New();
        seq.Request(Load("old"));
        seq.Request(Load("new"));
        _state = ClusterState.Idle;
        seq.Pump();
        Assert.Equal("new", _sent[^1].ScenarioId);

        _state = ClusterState.OperatingLive;
        seq.Request(Load("dropped"));
        seq.Cancel();
        _state = ClusterState.Idle;
        Assert.False(seq.Pump());
        Assert.DoesNotContain(_sent, s => s.ScenarioId == "dropped");
    }

    [Fact]
    public void BeforeLoad_RunsWithTheLoad_JustBeforeItGoesOut()
    {
        string? seen = null;
        int sentWhenSeen = -1;
        _state = ClusterState.OperatingLive;
        var seq = New(load => { seen = load.ScenarioId; sentWhenSeen = _sent.Count; });
        seq.Request(Load("d"));
        Assert.Null(seen);                     // not on the Idle request

        _state = ClusterState.Idle;
        seq.Pump();
        Assert.Equal("d", seen);
        Assert.Equal(1, sentWhenSeen);         // after the Idle request, before the load
    }

    /// <summary>
    /// ⭐ <c>CE-3354</c> ⑥ — the ONE live / edit load intents (HTTP's routes and the command line's startup load build them here): live = a FRESH ExerciseId per load, <c>TimeMode "Deterministic"</c> when paused and null when it must
    /// run, ForceRecording as asked; edit = NO ExerciseId, no time mode; each a fresh TransactionId. 🔴 Mutations (each compiles): the live ExerciseId left empty / reused ⇒ the first cells red; <c>startPaused</c> inverted ⇒ the time-mode cells red;
    /// the edit intent given an ExerciseId ⇒ the edit cell reds.
    /// </summary>
    [Fact]
    public void CE3354_TheLiveAndEditIntents_AreBuiltOnce_WithTheRulesTheRoutesHad()
    {
        var a = ScenarioLoadSequence.LiveLoadIntent("scn");
        var b = ScenarioLoadSequence.LiveLoadIntent("scn", startPaused: false, forceRecording: true);
        Assert.Equal((ClusterState.OperatingLive, "scn", "Deterministic", false), (a.TargetState, a.ScenarioId, a.TimeMode, a.ForceRecording));   // paused is the default
        Assert.Equal((ClusterState.OperatingLive, (string?)null, true), (b.TargetState, b.TimeMode, b.ForceRecording));                          // runs once loaded
        Assert.NotEqual(Guid.Empty, a.ExerciseId);
        Assert.NotEqual(a.ExerciseId, b.ExerciseId);                                                                                              // a live load IS a new exercise run
        Assert.NotEqual(a.TransactionId, b.TransactionId);

        var e = ScenarioLoadSequence.EditLoadIntent("scn");
        Assert.Equal((ClusterState.OperatingEdit, "scn", Guid.Empty, (string?)null, false), (e.TargetState, e.ScenarioId, e.ExerciseId, e.TimeMode, e.ForceRecording));
        Assert.NotEqual(Guid.Empty, e.TransactionId);
    }

    [Fact]
    public void ALoadMustNameItsScenario()
        => Assert.Throws<ArgumentException>(() => New().Request(new TransitionStateIntent { TargetState = ClusterState.OperatingLive }));
}
