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

    // ── RB-1 / RB-2 (docs/DESIGN_Replay_Browser_Debug_Http.md) — the standalone Replay Browser answers the same routes ────────────────

    private sealed class RecordingBrowserControl : IReplayBrowserControl
    {
        public readonly System.Collections.Generic.List<(string[] Paths, double At)> Opened = new();
        public string? Refuse;
        public string? Open(System.Collections.Generic.IReadOnlyList<string> paths, double atSeconds)
        {
            Opened.Add((paths.ToArray(), atSeconds));
            return Refuse;
        }
        public JsonObject Status() => new() { ["frame"] = 7 };
        public string? View(string view) => null;
        public string? History(bool back) => null;
        public string? SelectionHistory(bool back) => null;
        public (string? Job, string? Error) Export(ReplayExportRequest request) => ("job1", null);
        public JsonObject? ExportStatus(string job) => new() { ["job"] = job };
    }

    private DebugApiService BrowserHost(RecordingBrowserControl browser)
        => new(new PerspectiveScopedDispatcher(
                   new ISubsystemDebugProvider[]
                   {
                       new SubsystemDebugProvider("ReplayBrowser", "ReplayBrowser",
                           master: () => _master, clusterState: () => _state, replayBrowser: () => browser),
                   },
                   currentPerspective: () => "ReplayBrowser",
                   acksPending: null));

    [Fact]
    public void RB2_A_recording_file_opens_through_the_Replay_Browsers_own_seam_and_a_cluster_host_refuses_it()
    {
        var body = new JsonObject { ["paths"] = new JsonArray("a/node_1.fdp", "a/node_2.fdp"), ["atSeconds"] = 12.5 };

        // ⛔ a cluster host replays what its master recorded — it refuses a path rather than publish a transition it cannot honour
        var refused = Host().EnterReplay(body.DeepClone());
        Assert.Contains("Only the Replay Browser", refused.Error);
        Assert.Empty(_master.Sent);

        var browser = new RecordingBrowserControl();
        var (result, error, _) = BrowserHost(browser).EnterReplay(body.DeepClone());
        Assert.Null(error);
        var opened = Assert.Single(browser.Opened);
        Assert.Equal(new[] { "a/node_1.fdp", "a/node_2.fdp" }, opened.Paths);
        Assert.Equal(12.5, opened.At);
        Assert.True(result!["published"]!.GetValue<bool>());   // ⭐ waitForReady then finds OperatingReplay at once
        Assert.Empty(_master.Sent);                             // ⛔ never a transition for a file

        browser.Refuse = "exercise mismatch";
        Assert.Equal("exercise mismatch", BrowserHost(browser).EnterReplay(new JsonObject { ["path"] = "x.fdp" }).Error);
    }

    [Fact]
    public void RB2_The_replay_status_carries_the_Replay_Browsers_own_fields_and_none_elsewhere()
    {
        Assert.Equal(7, BrowserHost(new RecordingBrowserControl()).GetReplayStatus()["browser"]!["frame"]!.GetValue<int>());
        Assert.Null(Host().GetReplayStatus()["browser"]);
    }

    [Fact]
    public void RB1_A_master_that_refuses_in_place_answers_with_its_reason_not_a_crash()
    {
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
                new ISubsystemDebugProvider[]
                {
                    new SubsystemDebugProvider("ReplayBrowser", "ReplayBrowser",
                        master: () => new RefusingMaster(), clusterState: () => ClusterState.OperatingReplay),
                },
                currentPerspective: () => "ReplayBrowser",
                acksPending: null));

        Assert.Equal("no such exercise", svc.EnterReplay(null).Error);
        Assert.Equal("no such exercise", svc.SeekReplayTo(new JsonObject { ["atSeconds"] = 1 }).Error);
    }

    private sealed class RefusingMaster : IClusterMasterControl
    {
        public void Transition(TransitionStateIntent intent) => throw new InvalidOperationException("no such exercise");
        public void SeekReplay(SeekReplayIntent intent) => throw new InvalidOperationException("no such exercise");
        public void Storage(ExecuteStorageOpIntent intent) => throw new InvalidOperationException("no such exercise");
        public void TakeCheckpoint(TakeCheckpointIntent intent) => throw new InvalidOperationException("no such exercise");
        public void RestoreCheckpoint(RestoreCheckpointIntent intent) => throw new InvalidOperationException("no such exercise");
        public void Cancel(CancelOperationIntent intent) => throw new InvalidOperationException("no such exercise");
        public void Dump(ExecuteDiagnosticDumpIntent intent) => throw new InvalidOperationException("no such exercise");
    }

    // ── RB-2 — the seek forms that are lookups over the recording, resolved here for every host ─────────────────────────────────────

    private static readonly long Zero = TimeSpan.FromSeconds(1).Ticks;

    /// <summary>A node's recording: a keyframe at 1 s, then frames 1 s apart to 5 s; unit 1042's Health drops 100 → 60 at frame 3.</summary>
    private static string RecordUnit(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "node_1.fdp");
        using var repo = new Fdp.Core.EntityRepository();
        repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var unit = repo.CreateEntity();
        repo.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1042 });
        repo.AddComponent(unit, new Fdp.Toolkit.Combat.Components.Health { Current = 100, Max = 100 });
        using var rec = new Fdp.Core.FlightRecorder.AsyncRecorder(path,
            new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata { ExerciseId = Guid.NewGuid(), NodeId = 1 });
        repo.Tick();
        rec.CaptureKeyframe(repo, Zero, blocking: true, eventBus: repo.Bus);
        uint prev = repo.GlobalVersion;
        for (int f = 1; f <= 4; f++)
        {
            repo.Tick();
            if (f == 3) repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(unit).Current = 60;
            rec.CaptureFrame(repo, prev, Zero + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
            prev = repo.GlobalVersion;
        }
        return path;
    }

    private DebugApiService ReplayingHost(string recording, Func<int> nowFrame)
        => new(new PerspectiveScopedDispatcher(
                   new ISubsystemDebugProvider[]
                   {
                       new SubsystemDebugProvider("CGF", "CGF",
                           master:          () => _master,
                           clusterState:    () => ClusterState.OperatingReplay,
                           replayRecording: () => recording,
                           replayPosition:  () => new ReplayPosition(Zero, Zero + TimeSpan.FromSeconds(nowFrame()).Ticks)),
                   },
                   currentPerspective: () => "CGF",
                   acksPending: null));

    private long LastSeekTicks => Assert.IsType<SeekReplayIntent>(_master.Sent[^1]).TargetWallTicks;

    [Fact]
    public void RB2_A_frame_a_step_back_and_a_causality_jump_resolve_to_their_moment_and_seek_through_the_master()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rb2_" + Guid.NewGuid().ToString("N"));
        try
        {
            int now = 2;
            var svc = ReplayingHost(RecordUnit(dir), () => now);

            var frame = svc.ResolveReplaySeek(new JsonObject { ["frame"] = 4 });
            Assert.Null(frame.Error);
            Assert.Null(frame.Scan);
            Assert.Equal(TimeSpan.FromSeconds(4).Ticks, LastSeekTicks);              // frame 4 is 4 s after the zero
            Assert.Equal(4, frame.Result!["frame"]!.GetValue<int>());

            Assert.Null(svc.ResolveReplaySeek(new JsonObject { ["frames"] = -1 }).Error);
            Assert.Equal(TimeSpan.FromSeconds(1).Ticks, LastSeekTicks);              // one back from frame 2

            var cause = svc.ResolveReplaySeek(new JsonObject { ["cause"] = new JsonObject { ["frame"] = 2, ["entity"] = 1042 } });
            Assert.Equal(TimeSpan.FromSeconds(3).Ticks, LastSeekTicks);              // the frame AFTER the event
            Assert.Equal("1042", cause.Result!["entity"]!.ToJsonString());

            Assert.Null(svc.ResolveReplaySeek(new JsonObject { ["frame"] = 99 }).Error);
            Assert.Equal(TimeSpan.FromSeconds(4).Ticks, LastSeekTicks);              // clamped to the last frame
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RB2_The_next_change_of_a_unit_is_a_scan_off_the_main_thread_in_a_reader_of_its_own()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rb2_" + Guid.NewGuid().ToString("N"));
        try
        {
            int now = 1;
            var svc = ReplayingHost(RecordUnit(dir), () => now);

            var r = svc.ResolveReplaySeek(new JsonObject { ["next"] = new JsonObject { ["entity"] = 1042, ["component"] = "Health" }, ["direction"] = 1 });
            Assert.Null(r.Error);
            var scan = Assert.IsType<DebugApiService.ChangeScan>(r.Scan);
            Assert.Empty(_master.Sent);                                              // ⛔ nothing seeks before the scan answers
            Assert.Equal(3, DebugApiService.RunChangeScan(scan));                    // Health changed at frame 3

            now = 4;
            var back = svc.ResolveReplaySeek(new JsonObject { ["next"] = new JsonObject { ["entity"] = 1042 }, ["direction"] = -1 });
            Assert.Equal(3, DebugApiService.RunChangeScan(back.Scan!));             // the last change before frame 4

            now = 1;
            var none = svc.ResolveReplaySeek(new JsonObject { ["next"] = new JsonObject { ["entity"] = 1042, ["component"] = "NetworkIdentity" }, ["direction"] = 1 });
            Assert.Null(DebugApiService.RunChangeScan(none.Scan!));                 // a component that never changes: no frame
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RB5_The_replay_search_has_the_Search_panels_lifecycle_mode()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rb5_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = ReplayingHost(RecordUnit(dir), () => 0);
            var (result, error) = svc.SearchReplay(new JsonObject
            {
                ["lifecycle"] = new JsonObject { ["IdentifierType"] = "NetworkId", ["TargetValue"] = "1042" },
            });
            Assert.Null(error);
            Assert.Equal("lifecycle", result!["mode"]!.GetValue<string>());
            var span = Assert.Single(result["hits"]!.AsArray())!;
            Assert.Equal(0, span["startFrame"]!.GetValue<int>());              // the unit lives the whole recording
            Assert.Equal(4, span["endFrame"]!.GetValue<int>());
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RB2_A_lookup_outside_a_replay_is_refused_before_anything_is_read()
    {
        _state = ClusterState.OperatingLive;
        var r = new DebugApiService(new PerspectiveScopedDispatcher(
                new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "CGF", master: () => _master, clusterState: () => _state) },
                currentPerspective: () => "CGF", acksPending: null))
            .ResolveReplaySeek(new JsonObject { ["frame"] = 1 });
        Assert.Contains("POST /replay", r.Error);
        Assert.Empty(_master.Sent);
    }

    [Fact]
    public void RB3_The_Replay_Browsers_own_routes_are_not_supported_on_a_cluster_host()
    {
        var svc = Host();
        Assert.Throws<NotSupportedHereException>(() => svc.SetReplayView(new JsonObject { ["view"] = "merged" }));
        Assert.Throws<NotSupportedHereException>(() => svc.StepReplayHistory(new JsonObject { ["direction"] = "back" }));
        Assert.Throws<NotSupportedHereException>(() => svc.StepSelectionHistory(new JsonObject { ["direction"] = "back" }));
        Assert.Throws<NotSupportedHereException>(() => svc.StartReplayExport(new JsonObject()));
        // ⭐ a malformed request is told so before anything else
        Assert.Contains("back", BrowserHost(new RecordingBrowserControl()).StepReplayHistory(new JsonObject { ["direction"] = "up" }).Error);
    }

    // ── RB-6 — the timeline index's reads, a user's lane, and the lane form of seek ──────────────────────────────────────────────

    private sealed class FixedTimeline : Fdp.Toolkit.ReplayBrowser.Timeline.ITimelineSource
    {
        public Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex Index { get; init; } = Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex.Empty;
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    private static Fdp.Toolkit.ReplayBrowser.Timeline.IndexedLaneDefinition Lane(string id)
        => new(new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneDescriptor(id, id.ToUpperInvariant()), new Fdp.Toolkit.ReplayBrowser.Search.PropertyMatchDto());

    [Fact]
    public void RB6_The_timeline_lanes_and_events_come_from_the_index_and_a_lane_seek_lands_on_its_next_event()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rb6_" + Guid.NewGuid().ToString("N"));
        try
        {
            string recording = RecordUnit(dir);
            var index = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex(
                new System.Collections.Generic.Dictionary<string, Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent[]>
                {
                    ["shots"] = new[] { new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(1, "shots", 1042, 7, 0, 0, 0),
                                        new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(3, "shots", 1043, 0, 0, 0, 0) },
                },
                new long[5], scannedFrames: 5, totalFrames: 5);
            var catalog = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog().Register(Lane("shots"));
            int now = 1;
            var svc = new DebugApiService(new PerspectiveScopedDispatcher(
                new ISubsystemDebugProvider[]
                {
                    new SubsystemDebugProvider("ReplayBrowser", "ReplayBrowser",
                        master: () => _master, clusterState: () => ClusterState.OperatingReplay, replayRecording: () => recording,
                        replayPosition: () => new ReplayPosition(Zero, Zero + TimeSpan.FromSeconds(now).Ticks),
                        timeline: () => new TimelineAccess(() => new FixedTimeline { Index = index }, catalog,
                                                           new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext(Recording: true))),
                },
                currentPerspective: () => "ReplayBrowser", acksPending: null));

            var lanes = svc.GetTimelineLanes();
            Assert.Equal(2, lanes["lanes"]![0]!["count"]!.GetValue<int>());

            var (events, error) = svc.GetTimelineEvents("shots", from: null, to: null, entity: 1043, limit: 10);
            Assert.Null(error);
            Assert.Equal(3, Assert.Single(events!["events"]!.AsArray())!["frame"]!.GetValue<int>());   // only 1043's
            Assert.NotNull(svc.GetTimelineEvents("nope", null, null, null, 10).Error);

            var seek = svc.ResolveReplaySeek(new JsonObject { ["next"] = new JsonObject { ["lane"] = "shots" } });
            Assert.Null(seek.Error);
            Assert.Equal(TimeSpan.FromSeconds(3).Ticks, LastSeekTicks);                          // the next shot after frame 1
            now = 3;
            Assert.False(svc.ResolveReplaySeek(new JsonObject { ["next"] = new JsonObject { ["lane"] = "shots" } }).Result!["found"]!.GetValue<bool>());

            var added = svc.AddTimelineLane(JsonNode.Parse("{\"Lane\":{\"Id\":\"dmg\",\"DisplayName\":\"Damage\"},\"When\":{\"$type\":\"PropertyMatch\"}}"));
            Assert.Null(added.Error);
            Assert.NotNull(catalog.Find("dmg"));                                                    // the catalogue's own Register
            Assert.Contains("exists", svc.AddTimelineLane(JsonNode.Parse("{\"Lane\":{\"Id\":\"dmg\",\"DisplayName\":\"X\"},\"When\":{\"$type\":\"PropertyMatch\"}}")).Error);
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RB6_A_host_with_no_timeline_index_says_so()
        => Assert.Throws<NotSupportedHereException>(() => Host().GetTimelineLanes());

    // ── RB-7 (R-4083) — POST /selection goes through the UI's own request path ──────────────────────────────────────────────────────

    [Fact]
    public void RB7_A_selection_is_a_SelectEntityCommand_on_the_shown_world_never_a_state_write()
    {
        var world = new Fdp.Core.EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var unit = world.CreateEntity();
        world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1042 });
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        map.Register(1042, unit);
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "CGF", world: () => world, entityMap: () => map) },
            currentPerspective: () => "CGF", acksPending: null));

        Assert.Contains("runs no selection", svc.SelectEntity(new JsonObject { ["entity"] = 1042 }).Error);   // ⛔ never a silent no-op
        world.RegisterEvent<Hrot.Common.Events.SelectEntityCommand>();
        Assert.Contains("not found", svc.SelectEntity(new JsonObject { ["entity"] = 9 }).Error);
        Assert.Null(svc.SelectEntity(new JsonObject { ["entity"] = 1042 }).Error);

        world.Bus.SwapBuffers();
        var sent = Assert.Single(world.ReadEvents<Hrot.Common.Events.SelectEntityCommand>().ToArray());
        Assert.Equal(1042, sent.NetworkId);
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
