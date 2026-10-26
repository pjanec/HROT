using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ R-3052…R-3055 — every route that drives the cluster is ONE master intent, published through the host's
/// <see cref="IClusterMasterControl"/>: save, preview, checkpoint, replay, seek. 📄 docs/DESIGN_Http_Control_Through_The_Cluster_Master.md §3.
/// </summary>
public sealed class TheClusterControlRoutesTests
{
    private readonly RecordingMasterControl _master = new();
    private ClusterState _state = ClusterState.Idle;

    private static readonly ExerciseInventoryItem Older = new(Guid.NewGuid(), new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(5), "a");
    private static readonly ExerciseInventoryItem Newer = new(Guid.NewGuid(), new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(3), "b");

    private DebugApiService Host(bool withMaster = true)
        => new(new PerspectiveScopedDispatcher(
                   new ISubsystemDebugProvider[]
                   {
                       new SubsystemDebugProvider("CGF", "CGF",
                           master:    withMaster ? () => _master : null,
                           exercises: () => new[] { Older, Newer }),
                   },
                   currentPerspective: () => "CGF",
                   acksPending: null),
               clusterState: () => _state);

    [Fact]
    public void A_save_is_one_storage_intent_to_the_master()
    {
        Host().SaveScenario("folder/my-scenario");

        var save = Assert.IsType<ExecuteStorageOpIntent>(Assert.Single(_master.Sent));
        Assert.Equal(StorageOpType.SaveScenario, save.Operation);
        Assert.Equal("folder/my-scenario", save.ScenarioName);
    }

    [Fact]
    public void Preview_is_the_clusters_dry_run_entered_and_left_through_the_master()
    {
        _state = ClusterState.OperatingEdit;
        var svc = Host();

        Assert.Null(svc.EnterPreview(startPaused: true).Error);
        var enter = Assert.Single(_master.Transitions);
        Assert.Equal(ClusterState.OperatingPreview, enter.TargetState);
        Assert.Equal("Deterministic", enter.TimeMode);   // a start-paused dry run (ClusterMaster pauses it)

        _state = ClusterState.OperatingPreview;
        Assert.Null(svc.ExitPreview().Error);
        Assert.Equal(ClusterState.OperatingEdit, _master.Transitions[1].TargetState);
    }

    [Fact]
    public void A_checkpoint_is_a_named_keyframe_of_the_live_recording_and_a_restore_names_it()
    {
        var svc = Host();

        var (taken, error, _) = svc.TakeCheckpoint(label: null);
        Assert.Null(error);
        var take = Assert.IsType<TakeCheckpointIntent>(Assert.Single(_master.Sent));
        Assert.False(string.IsNullOrWhiteSpace(take.Label));                        // named here: a restore finds it by its label
        Assert.Equal(take.Label, taken!["label"]!.GetValue<string>());

        Assert.NotNull(svc.RestoreCheckpoint(label: null).Error);                    // the master refuses an unnamed restore
        Assert.Null(svc.RestoreCheckpoint(take.Label).Error);
        Assert.Equal(take.Label, Assert.IsType<RestoreCheckpointIntent>(_master.Sent[^1]).Label);
    }

    [Fact]
    public void Entering_a_replay_is_one_transition_from_any_state_and_the_MASTER_picks_the_exercise()
    {
        _state = ClusterState.OperatingLive;   // ⭐ R-3055: no /cluster/transition walk — the planner plans the way

        var (result, error, _) = Host().EnterReplay(new JsonObject { ["atSeconds"] = 30 });

        Assert.Null(error);
        var t = Assert.Single(_master.Transitions);
        Assert.Equal(ClusterState.OperatingReplay, t.TargetState);
        // ⭐ none named ⇒ Guid.Empty on EVERY host: the master resolves it (ClusterMaster.ResolveReplayExercise) — ⛔ the asker never
        //   picks from a cache it may not have (measured: the same request worked on the editor and was refused on CGF)
        Assert.Equal(Guid.Empty, t.ExerciseId);
        Assert.Equal(TimeSpan.FromSeconds(30).Ticks, t.TargetWallTicks);   // C9: ticks after the replay's zero
        Assert.Null(result!["exerciseId"]);

        var named = Guid.NewGuid();
        Assert.Null(Host().EnterReplay(new JsonObject { ["exerciseId"] = named.ToString() }).Error);
        Assert.Equal(named, _master.Transitions[^1].ExerciseId);   // a named exercise is kept
    }

    [Fact]
    public void A_seek_is_the_masters_seek_round_and_only_in_a_replay()
    {
        var svc = Host();

        _state = ClusterState.OperatingLive;
        Assert.Contains("POST /replay", svc.SeekReplayTo(new JsonObject { ["atSeconds"] = 45 }).Error);
        Assert.Empty(_master.Sent);

        _state = ClusterState.OperatingReplay;
        Assert.Null(svc.SeekReplayTo(new JsonObject { ["atSeconds"] = 45 }).Error);
        Assert.Equal(TimeSpan.FromSeconds(45).Ticks, Assert.IsType<SeekReplayIntent>(Assert.Single(_master.Sent)).TargetWallTicks);

        Assert.Null(svc.StopReplay().Error);
        Assert.Equal(ClusterState.Idle, _master.Transitions.Last().TargetState);
    }

    [Fact]
    public void The_exercises_are_listed_newest_first()
    {
        var (result, error, _) = Host().ListExercises();

        Assert.Null(error);
        var ids = result!["exercises"]!.AsArray().Select(x => x!["exerciseId"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { Newer.ExerciseId.ToString(), Older.ExerciseId.ToString() }, ids);
    }

    [Fact]
    public void A_host_that_reaches_no_master_says_so()
    {
        var svc = Host(withMaster: false);

        Assert.StartsWith("No subsystem", svc.TakeCheckpoint("x").Error);   // ⭐ the host maps this prefix to 503
        Assert.StartsWith("No subsystem", svc.EnterReplay(null).Error);
        Assert.StartsWith("No subsystem", svc.StopReplay().Error);
    }
}
