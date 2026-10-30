using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-4090</c> (AQ108 D14, D15, §5a) — <b>LIVE DOES NOT IMPLY RECORDING</b>, at the MASTER: the cluster-wide <i>Record live runs</i> setting
/// (default ON) is held by the master, put on the live load's payload for every node and on every cluster-state broadcast, and every gate that
/// needs a recording (checkpoint, restore, DAR, export, replay of the last run) REFUSES at the master with <c>"this run was not recorded"</c> —
/// ⛔ never a throw deep in a node. The review / ledger / save gates live in their own feature suites (<c>ClusterMasterReviewTests</c>,
/// <c>AssetInventoryProcessManagerTests</c>); the branch in <c>ClusterMasterBranchTests</c>.
/// 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md §4 D14, §5a.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterRunRecordingTests
{
    private const int NodeId = 42;

    /// <summary>One node, Idle; then a live load whose request is <paramref name="livePayloadJson"/>, every prepare ACKed until live. Returns what
    /// the load put on the wire (the PrepareLive / OperatingLive payloads) and the last cluster-state broadcast.</summary>
    private sealed record LiveRun(ClusterMaster Master, FdpEventBus Bus, List<ExecuteNodeOpIntent> Prepares, ClusterStateUpdateEvent? LastState);

    private static LiveRun GoLive(bool? recordLiveRuns = null, string livePayloadJson = "{\"TargetState\":\"OperatingLive\"}", Guid? exercise = null)
    {
        var bus = new FdpEventBus();
        var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = NodeId, LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks, SubsystemName = "SimHost",
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        if (recordLiveRuns is { } record) SetSetting(master, bus, record);

        if (exercise is { } named)
            livePayloadJson = livePayloadJson.TrimEnd('}') + $",\"ExerciseId\":\"{named}\"}}";
        return Load(master, bus, livePayloadJson);
    }

    private static void SetSetting(ClusterMaster master, FdpEventBus bus, bool record)
    {
        bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), RecordLiveRuns = record });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    private static LiveRun Load(ClusterMaster master, FdpEventBus bus, string payloadJson)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState, PayloadJson = payloadJson,
        });
        var prepares = new List<ExecuteNodeOpIntent>();
        ClusterStateUpdateEvent? last = null;
        for (int round = 0; round < 12; round++)
        {
            bus.SwapBuffers();
            foreach (var ev in bus.ReadManaged<ClusterStateUpdateEvent>()) last = ev;
            foreach (var intent in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.TargetNodeId == NodeId && i.Operation != FdpNodeOpType.CommitState))
            {
                prepares.Add(intent);
                bus.PublishManaged(new NodeOpCompletedEvent
                {
                    TransactionId = intent.TransactionId, Operation = intent.Operation, NodeId = NodeId,
                    StatusCode = OrchestrationStatusCode.Success, IsParticipating = true,
                });
            }
            bus.SwapBuffers();
            master.Tick();
            if (master.CurrentClusterState == ClusterState.OperatingLive && round > 2) break;
        }
        bus.SwapBuffers();
        foreach (var ev in bus.ReadManaged<ClusterStateUpdateEvent>()) last = ev;
        return new LiveRun(master, bus, prepares, last);
    }

    private static EditLoadHandlerPayload Payload(ExecuteNodeOpIntent i) => Assert.IsType<EditLoadHandlerPayload>(i.DomainPayload);

    /// <summary>Sends one ClusterOpRequest and returns the status the master answers it (plus every node op it fanned out).</summary>
    private static (ClusterOpCompletedEvent? Status, List<ExecuteNodeOpIntent> Ops) Ask(LiveRun run, ClusterOpType op, string payload = "")
    {
        var id = Guid.NewGuid();
        run.Master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = id, OperationType = op, PayloadJson = payload });
        run.Bus.SwapBuffers(); run.Master.Tick(); run.Bus.SwapBuffers();
        var status = run.Bus.ReadManaged<ClusterOpCompletedEvent>().Where(e => e.RequestId == id).Cast<ClusterOpCompletedEvent?>().FirstOrDefault();
        return (status, run.Bus.ReadManaged<ExecuteNodeOpIntent>().ToList());
    }

    private static void AssertRefusedForNoRecording((ClusterOpCompletedEvent? Status, List<ExecuteNodeOpIntent> Ops) answer)
    {
        Assert.NotNull(answer.Status);
        Assert.Equal(OrchestrationStatusCode.Rejected, answer.Status!.Value.StatusCode);
        Assert.Equal(RunRecordingGates.NotRecordedReason, answer.Status.Value.ResultPayload);   // the one reason, at the top
        Assert.Empty(answer.Ops);                                                                // ⛔ nothing reached a node — no throw to be had there
    }

    // ── 1 · 6 — the setting, and how it reaches every node ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐ D14 — the default is ON: the live load's payload says record and nothing is announced as unrecorded (today's behaviour, byte for byte).
    /// Turning the setting OFF (the intent every host sends) changes the NEXT live load: EVERY live step's payload carries <c>RecordingOff</c>, and the
    /// master announces <c>RunNotRecorded</c> on the cluster-state broadcast every host reads. Turning it back ON makes the next live load record again.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void The_setting_defaults_on_and_off_rides_the_live_payload_and_the_broadcast_to_every_node()
    {
        var run = GoLive();   // the default setting
        Assert.True(run.Master.RecordLiveRuns);
        Assert.False(run.Master.RunNotRecorded);
        Assert.All(run.Prepares.Where(p => p.DomainPayload is EditLoadHandlerPayload), p => Assert.False(Payload(p).RecordingOff));
        Assert.False(run.LastState!.Value.RunNotRecorded);

        var off = GoLive(recordLiveRuns: false);
        Assert.False(off.Master.RecordLiveRuns);
        Assert.True(off.Master.RunNotRecorded);
        var live = off.Prepares.Where(p => p.Operation == FdpNodeOpType.PrepareLive).ToList();
        Assert.NotEmpty(live);
        Assert.All(live, p => Assert.True(Payload(p).RecordingOff, "PrepareLive must tell the node not to record"));
        Assert.All(off.Prepares.Where(p => p.DomainPayload is EditLoadHandlerPayload { TargetState: Fdp.Toolkit.Orchestration.ClusterState.OperatingLive }),
                   p => Assert.True(Payload(p).RecordingOff, "the OperatingLive step (where the recorder would start) too"));
        Assert.NotEqual(Guid.Empty, Payload(live[0]).ExerciseId);                      // the id still exists — only the recording does not
        Assert.True(off.LastState!.Value.RunNotRecorded, "every host's cluster-state broadcast says it");

        // ON again: from Live back through Idle to a new live load
        SetSetting(off.Master, off.Bus, true);
        Assert.True(off.Master.RecordLiveRuns);
        Assert.True(off.Master.RunNotRecorded, "the run on screen keeps its mode — a setting applies from the NEXT live load");
        off.Master.Dispose(); run.Master.Dispose();
    }

    /// <summary>⭐ D14 — a host that is not the master's process sends the setting as a ClusterOp (wire value 24): the master takes it the same way,
    /// and an unreadable payload changes nothing (CE-3341; it used to reset to ON).</summary>
    [Fact(Timeout = 10_000)]
    public void The_setting_arrives_as_a_cluster_op_too_and_an_unreadable_one_changes_nothing()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.SetRunSettings, PayloadJson = "{\"RecordLiveRuns\":false}",
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.False(master.RecordLiveRuns);

        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.SetRunSettings, PayloadJson = "not json" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.False(master.RecordLiveRuns);   // CE-3341: an unreadable payload changes NOTHING (it used to reset to ON)
    }

    /// <summary>⭐ CE-4090 — every host reads the SETTING off the cluster-state broadcast: ON by default (nothing inverted announced), and a
    /// <c>SetRunSettings</c> makes the master republish the state at once, both ways.</summary>
    [Fact(Timeout = 10_000)]
    public void The_setting_itself_rides_the_cluster_state_broadcast_and_is_republished_when_it_changes()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        ClusterStateUpdateEvent? Last()
        {
            ClusterStateUpdateEvent? last = null;
            foreach (var e in bus.ReadManaged<ClusterStateUpdateEvent>()) last = e;
            return last;
        }
        SetSetting(master, bus, true);
        Assert.False(Last()!.Value.RecordLiveRunsOff);
        SetSetting(master, bus, false);
        Assert.True(Last()!.Value.RecordLiveRunsOff);          // told at once, no load needed
        SetSetting(master, bus, true);
        Assert.False(Last()!.Value.RecordLiveRunsOff);
    }

    /// <summary>⭐ CE-4100 (AQ108 D15) — the cluster's <i>Ask to keep recordings</i> setting: OFF by default (keep silently), set by the op, broadcast at once on every state
    /// publication, and left alone by an op that does not carry it (null = no change) — and it is independent of <c>RecordLiveRuns</c>.
    /// 🔴 Mutation: the master not copying <c>AskToKeepRecordings</c> into the broadcast ⇒ the second assertion fails.</summary>
    [Fact(Timeout = 10_000)]
    public void CE4100_AskToKeepRecordings_DefaultsOff_IsSetByTheOp_RidesTheBroadcast_AndAnOpThatDoesNotCarryItLeavesItAlone()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        ClusterStateUpdateEvent? Last()
        {
            ClusterStateUpdateEvent? last = null;
            foreach (var e in bus.ReadManaged<ClusterStateUpdateEvent>()) last = e;
            return last;
        }
        Assert.False(master.AskToKeepRecordings);

        bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), AskToKeepRecordings = true });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.True(master.AskToKeepRecordings);
        Assert.True(Last()!.Value.AskToKeepRecordings);          // told at once, no load needed
        Assert.True(master.RecordLiveRuns);                      // the other setting did not move

        SetSetting(master, bus, false);                          // an op carrying only RecordLiveRuns
        Assert.True(master.AskToKeepRecordings);
        Assert.True(Last()!.Value.AskToKeepRecordings);

        bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), AskToKeepRecordings = false });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.False(Last()!.Value.AskToKeepRecordings);
        Assert.False(master.RecordLiveRuns);
    }

    // ── 4 — the verifier ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ §5a — the scenario verifier compares a RECORDING against a golden: its live load (<c>ForceRecording</c>) records even with the
    /// setting OFF; the same load without the force does not.</summary>
    [Fact(Timeout = 10_000)]
    public void A_verify_run_forces_recording_on_even_when_the_setting_is_off()
    {
        var forced = GoLive(recordLiveRuns: false, livePayloadJson: "{\"TargetState\":\"OperatingLive\",\"ForceRecording\":true}");
        Assert.False(forced.Master.RunNotRecorded);
        Assert.All(forced.Prepares.Where(p => p.DomainPayload is EditLoadHandlerPayload), p => Assert.False(Payload(p).RecordingOff));
        Assert.False(forced.LastState!.Value.RunNotRecorded);
        forced.Master.Dispose();

        var plain = GoLive(recordLiveRuns: false);   // the control: no force ⇒ the setting wins
        Assert.True(plain.Master.RunNotRecorded);
        plain.Master.Dispose();
    }

    // ── 2 — the gates, one per consumer ───────────────────────────────────────────────────────────────────────

    /// <summary>⭐ §5a — checkpoint: with the run not recorded the master refuses (no TakeSnapshot reaches any node); with it recorded the same
    /// request fans out as before (the control — "ON behaves exactly as today").</summary>
    [Fact(Timeout = 10_000)]
    public void A_checkpoint_of_a_run_that_was_not_recorded_is_refused_at_the_master_and_a_recorded_one_is_not()
    {
        var off = GoLive(recordLiveRuns: false);
        AssertRefusedForNoRecording(Ask(off, ClusterOpType.TakeCheckpoint, "{\"label\":\"cp\"}"));
        off.Master.Dispose();

        var on = GoLive();
        var answer = Ask(on, ClusterOpType.TakeCheckpoint, "{\"label\":\"cp\"}");
        Assert.Contains(answer.Ops, i => i.Operation == FdpNodeOpType.TakeSnapshot);
        on.Master.Dispose();
    }

    /// <summary>⭐ §5a — restore: refused at the master, and — the point of the gate — the live clock is NOT paused first (no PauseTime, no
    /// PrepareRestore on any node); a recorded run restores as before.</summary>
    [Fact(Timeout = 10_000)]
    public void A_restore_of_a_run_that_was_not_recorded_is_refused_before_anything_pauses_and_a_recorded_one_is_not()
    {
        var off = GoLive(recordLiveRuns: false);
        var answer = Ask(off, ClusterOpType.RestoreCheckpoint, "{\"label\":\"cp\"}");
        AssertRefusedForNoRecording(answer);
        Assert.DoesNotContain(off.Bus.ReadManaged<PauseTimeIntent>(), _ => true);   // ⛔ the old order paused first
        off.Master.Dispose();

        var on = GoLive();
        Assert.Contains(Ask(on, ClusterOpType.RestoreCheckpoint, "{\"label\":\"cp\"}").Ops, i => i.Operation == FdpNodeOpType.PrepareRestore);
        on.Master.Dispose();
    }

    /// <summary>⭐ §5a — keep / export: archiving the run that was not recorded is refused; another exercise's export is not touched by the gate.</summary>
    [Fact(Timeout = 10_000)]
    public void Exporting_the_run_that_was_not_recorded_is_refused_but_an_older_exercise_is_not()
    {
        var off = GoLive(recordLiveRuns: false);
        var activeId = off.Master.ActiveExerciseId;
        Assert.NotEqual(Guid.Empty, activeId);
        AssertRefusedForNoRecording(Ask(off, ClusterOpType.ExportArchive, $"{{\"ExerciseId\":\"{activeId}\"}}"));

        var older = Ask(off, ClusterOpType.ExportArchive, $"{{\"ExerciseId\":\"{Guid.NewGuid()}\"}}");
        Assert.NotEqual(RunRecordingGates.NotRecordedReason, older.Status?.ResultPayload);   // not this gate's business
        off.Master.Dispose();
    }

    /// <summary>⭐ §5a — "replay the last run" (no exercise named) after a run that was not recorded is refused — ⛔ never silently the newest OLDER
    /// exercise; naming an exercise still replays it.</summary>
    [Fact(Timeout = 10_000)]
    public void A_replay_of_the_last_run_is_refused_when_it_was_not_recorded_but_a_named_exercise_is_not()
    {
        var off = GoLive(recordLiveRuns: false);
        off.Master.NewestExercise = () => Guid.NewGuid();   // an OLDER recorded exercise exists
        var refused = Ask(off, ClusterOpType.TransitionState, "{\"TargetState\":\"OperatingReplay\"}");
        AssertRefusedForNoRecording(refused);

        var named = Ask(off, ClusterOpType.TransitionState, $"{{\"TargetState\":\"OperatingReplay\",\"ExerciseId\":\"{Guid.NewGuid()}\"}}");
        Assert.NotEqual(OrchestrationStatusCode.Rejected, named.Status?.StatusCode ?? OrchestrationStatusCode.Success);
        off.Master.Dispose();
    }

    // ── 5 — discard a recording (D15) ─────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ D15 — <c>DELETE /exercises/{id}</c> at the master: the host's deleter (the inventory's) is asked once for an id it may discard;
    /// an unknown id is refused; the exercise still recording is refused WITHOUT asking.</summary>
    [Fact(Timeout = 10_000)]
    public void Discarding_a_recording_asks_the_inventory_refuses_unknown_ids_and_never_the_run_still_recording()
    {
        var run = GoLive();                                   // recorded, live: its exercise is still recording
        var asked = new List<Guid>();
        var known = Guid.NewGuid();
        run.Master.ExerciseDeleter = id => { asked.Add(id); return id == known; };

        AssertRejected(Ask(run, ClusterOpType.DeleteExercise, $"{{\"ExerciseId\":\"{run.Master.ActiveExerciseId}\"}}"), "still recording");
        Assert.Empty(asked);

        Assert.Equal(OrchestrationStatusCode.Success, Ask(run, ClusterOpType.DeleteExercise, $"{{\"ExerciseId\":\"{known}\"}}").Status!.Value.StatusCode);
        AssertRejected(Ask(run, ClusterOpType.DeleteExercise, $"{{\"ExerciseId\":\"{Guid.NewGuid()}\"}}"), "no such");
        AssertRejected(Ask(run, ClusterOpType.DeleteExercise, "{}"), "required");
        Assert.Single(asked.Where(a => a == known));
        run.Master.Dispose();

        static void AssertRejected((ClusterOpCompletedEvent? Status, List<ExecuteNodeOpIntent> Ops) a, string why)
        {
            Assert.Equal(OrchestrationStatusCode.Rejected, a.Status!.Value.StatusCode);
            Assert.Contains(why, (string)a.Status.Value.ResultPayload!);
        }
    }

    // ── CE-3341 (AQ108 D12a, R-3068) — the ARMED mode is one cluster-wide fact, set by the same op ──────────────

    private static ClusterStateUpdateEvent? LastBroadcast(FdpEventBus bus)
    {
        ClusterStateUpdateEvent? last = null;
        foreach (var e in bus.ReadManaged<ClusterStateUpdateEvent>()) last = e;
        return last;
    }

    private static void Op(ClusterMaster master, FdpEventBus bus, string payloadJson)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.SetRunSettings, PayloadJson = payloadJson });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    /// <summary>⭐ SC1 — a <c>SetRunSettings{Armed}</c> (the op a remote host sends, and the intent a local one publishes) changes the master's armed mode, and
    /// the cluster-state broadcast carries it AT ONCE (no load needed): the default is Live.</summary>
    [Fact(Timeout = 10_000)]
    public void The_armed_mode_is_set_by_the_run_settings_op_and_rides_the_broadcast_at_once()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        Assert.Equal(ClusterArmedMode.Live, master.ArmedMode);                        // the default

        Op(master, bus, "{\"Armed\":\"Preview\"}");                                   // the wire op
        Assert.Equal(ClusterArmedMode.Preview, master.ArmedMode);
        Assert.Equal(ClusterArmedMode.Preview, LastBroadcast(bus)!.Value.ArmedMode);

        bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), Armed = ClusterArmedMode.AAR });   // the in-process intent
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Equal(ClusterArmedMode.AAR, master.ArmedMode);
        Assert.Equal(ClusterArmedMode.AAR, LastBroadcast(bus)!.Value.ArmedMode);
    }

    /// <summary>⭐ SC2 — an old-shape op (only <c>RecordLiveRuns</c>) or an unreadable one leaves the armed mode as it is; an armed-only op leaves the recording setting.
    /// An older PEER's broadcast (no armed field) reads as the default — see <c>CE3341_…</c> in the SimHost translator tests.</summary>
    [Fact(Timeout = 10_000)]
    public void An_old_shape_run_settings_op_changes_no_armed_mode_and_an_armed_only_op_changes_no_recording_setting()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        Op(master, bus, "{\"Armed\":\"AAR\"}");
        Assert.True(master.RecordLiveRuns);                                          // armed-only: the recording setting untouched

        Op(master, bus, "{\"RecordLiveRuns\":false}");                                // old shape
        Assert.False(master.RecordLiveRuns);
        Assert.Equal(ClusterArmedMode.AAR, master.ArmedMode);                        // unchanged
        Assert.Equal(ClusterArmedMode.AAR, LastBroadcast(bus)!.Value.ArmedMode);

        Op(master, bus, "not json");
        Assert.Equal(ClusterArmedMode.AAR, master.ArmedMode);
        Assert.False(master.RecordLiveRuns);
    }

    /// <summary>⭐ SC3 — a scenario LOAD (edit, then live) never changes the armed mode: only <c>SetRunSettings{Armed}</c> does (D12a: the load does not carry it).</summary>
    [Fact(Timeout = 10_000)]
    public void A_scenario_load_edit_or_live_does_not_change_the_armed_mode()
    {
        var edit = GoLive(livePayloadJson: "{\"TargetState\":\"OperatingEdit\"}");
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit, edit.LastState!.Value.CurrentState);   // the load really ran
        Assert.Equal(ClusterArmedMode.Live, edit.Master.ArmedMode);
        Op(edit.Master, edit.Bus, "{\"Armed\":\"Preview\"}");
        Assert.Equal(ClusterArmedMode.Preview, edit.Master.ArmedMode);

        var live = Load(edit.Master, edit.Bus, "{\"TargetState\":\"OperatingLive\"}");   // edit -> live, same master
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, live.LastState!.Value.CurrentState);
        Assert.Equal(ClusterArmedMode.Preview, live.Master.ArmedMode);                // not reset by the load
        Assert.Equal(ClusterArmedMode.Preview, live.LastState!.Value.ArmedMode);      // and the broadcast still says it
        edit.Master.Dispose();
    }

    // ── R-1043 — the initial armed mode is a bootstrap parameter; LastLoadMode is the mode the last load went into ──────────────

    /// <summary>⭐ R-1043 a — default config ⇒ initial armed mode Live; the editor's one-node config ⇒ Preview (its only bootstrap difference); never read from the config file.</summary>
    [Fact(Timeout = 10_000)]
    public void The_initial_armed_mode_is_a_bootstrap_parameter_Live_by_default_Preview_for_the_editors_one_node_config()
    {
        var bus = new FdpEventBus();
        using var plain = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        Assert.Equal(ClusterArmedMode.Live, plain.ArmedMode);
        using var editor = new ClusterMaster(new FdpEventBus(), ClusterConfiguration.Default.ForOneNodeCluster());
        Assert.Equal(ClusterArmedMode.Preview, editor.ArmedMode);
        Assert.Equal(ClusterArmedMode.Preview, editor.LastLoadMode);   // before any load it equals the initial mode

        var fromFile = System.Text.Json.JsonSerializer.Deserialize<ClusterConfiguration>("{\"InitialArmedMode\":\"Preview\"}", Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
        Assert.Equal(ClusterArmedMode.Live, fromFile.InitialArmedMode);   // ⛔ no file config
    }

    /// <summary>⭐ R-1043 b — LastLoadMode: the initial mode before any load; a live load ⇒ Live; an edit load ⇒ Preview; it rides the broadcast; arming does not move it.</summary>
    [Fact(Timeout = 10_000)]
    public void LastLoadMode_is_the_initial_mode_before_a_load_then_the_mode_each_load_went_into_and_rides_the_broadcast()
    {
        var bus = new FdpEventBus();
        var master = new ClusterMaster(bus, new ClusterConfiguration { MandatoryRoles = Fdp.Core.NodeRole.None, InitialArmedMode = ClusterArmedMode.AAR });
        Assert.Equal(ClusterArmedMode.AAR, master.LastLoadMode);   // = the initial mode
        bus.PublishManaged(new NodeHeartbeatEvent { NodeId = NodeId, LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle, WallTicksUtc = DateTimeOffset.UtcNow.Ticks, SubsystemName = "SimHost" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        var edit = Load(master, bus, "{\"TargetState\":\"OperatingEdit\"}");
        Assert.Equal(ClusterArmedMode.Preview, master.LastLoadMode);
        Assert.Equal(ClusterArmedMode.Preview, edit.LastState!.Value.LastLoadMode);

        Op(master, bus, "{\"Armed\":\"Live\"}");
        Assert.Equal(ClusterArmedMode.Preview, master.LastLoadMode);   // arming is not a load

        var live = Load(master, bus, "{\"TargetState\":\"OperatingLive\"}");
        Assert.Equal(ClusterArmedMode.Live, master.LastLoadMode);
        Assert.Equal(ClusterArmedMode.Live, live.LastState!.Value.LastLoadMode);
        master.Dispose();
    }
}
