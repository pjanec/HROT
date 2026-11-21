using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-4093</c> (AQ108 D20, master side) — <b>the CHECK ROUND</b>: the master asks every participant whether its whole world differs from its baseline
/// and folds the answers (<see cref="UnsavedCheckAggregator"/>) — plus its OWN fact that a save round failed — into one <see cref="UnsavedCheckResult"/>.
/// 🔴 Mutations (each compiles): the aggregator reading "no-baseline" as saved ⇒ the no-baseline cells red; not skipping an empty answer ⇒ the
/// not-participating cells red; the master not folding <c>SaveFailedSinceLoad</c> in ⇒ the failed-save cells red; no editing refusal ⇒ the refusal cell red;
/// no <c>NoteLoadForUnsavedCheck</c> ⇒ the "a new load forgets the failure" cell red.
/// </summary>
public sealed class UnsavedCheckAggregatorTests
{
    private static IReadOnlyDictionary<int, Dictionary<FdpNodeOpType, string>> Answers(params (int Node, string Answer)[] answers)
        => answers.ToDictionary(a => a.Node, a => new Dictionary<FdpNodeOpType, string> { [FdpNodeOpType.CheckUnsaved] = a.Answer });

    private static UnsavedCheckResult Run(params (int Node, string Answer)[] answers)
        => Assert.IsType<UnsavedCheckResult>(new UnsavedCheckAggregator().Aggregate(Answers(answers)));

    /// <summary>Only an explicit "unchanged" means saved — the plain word and the JSON string the wire carries read alike.</summary>
    [Fact]
    public void EveryNodeUnchanged_IsNotChanged()
    {
        var r = Run((1, "unchanged"), (2, "\"unchanged\""));
        Assert.False(r.Changed);
        Assert.Empty(r.ChangedNodes);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("\"changed\"")]
    [InlineData("no-baseline")]          // nothing says its world is saved ⇒ changed
    [InlineData("gibberish")]            // an answer nobody can read is not "unchanged"
    public void OneNodeNotUnchanged_IsChanged_AndNamesThatNode(string answer)
    {
        var r = Run((1, "unchanged"), (2, answer), (3, "unchanged"));
        Assert.True(r.Changed);
        Assert.Equal(new[] { 2 }, r.ChangedNodes);
    }

    /// <summary>A host with no ECS world ACKs the round as not participating — the master stores "" — it has nothing to lose, so it never makes the scenario changed.</summary>
    [Fact]
    public void EmptyAnswers_AreSkipped_NotCountedAsChanged()
    {
        var r = Run((1, "unchanged"), (2, ""), (3, "  "));
        Assert.False(r.Changed);
        Assert.Empty(r.ChangedNodes);
        Assert.False(Run((7, "")).Changed);   // a round whose every node was silent: nothing unsaved anywhere
    }

    [Fact]
    public void ChangedNodes_AreSorted_AndEveryOneIsListed()
    {
        var r = Run((9, "changed"), (4, "no-baseline"), (6, "unchanged"));
        Assert.Equal(new[] { 4, 9 }, r.ChangedNodes);
    }

    /// <summary>The registry runs every aggregator on every round: one that finds none of its op's answers must stay out of the way (null, not "unchanged").</summary>
    [Fact]
    public void ARoundWithoutCheckAnswers_IsNullForThisAggregator()
    {
        var other = new Dictionary<int, Dictionary<FdpNodeOpType, string>> { [1] = new() { [FdpNodeOpType.SerializeLocal] = "[]" } };
        Assert.Null(new UnsavedCheckAggregator().Aggregate(other));
    }
}

[Collection("OrchestratorTests")]
public sealed class ClusterMasterUnsavedCheckTests
{
    private const int A = 11, B = 22;

    /// <summary>A master on a bus with registered nodes, driven frame by frame; every node op is answered by <see cref="Respond"/>.</summary>
    private sealed class Rig
    {
        public readonly FdpEventBus Bus = new();
        public readonly ClusterMaster Master;
        public readonly List<ExecuteNodeOpIntent> Intents = new();
        public readonly List<ClusterOpCompletedEvent> Completed = new();
        /// <summary>⭐ CE-4099 — the prefetch intents the master published (the recovery flag rides them).</summary>
        public readonly List<ExecutePrefetchIntent> SeenPrefetches = new();
        /// <summary>What each node answers to <c>CheckUnsaved</c> (a missing node answers "unchanged").</summary>
        public readonly Dictionary<int, string> CheckAnswer = new();
        /// <summary>⭐ CE-3340 ⑤ — nodes that never answer a <c>CheckUnsaved</c> (dead, but still on the roster until their heartbeat times out).</summary>
        public readonly HashSet<int> SilentOnCheck = new();
        /// <summary>The status a node answers to a <c>SerializeLocal</c> (the save's node round); default Success.</summary>
        public OrchestrationStatusCode SaveRoundStatus = OrchestrationStatusCode.Success;
        /// <summary>When set, a successful <c>SerializeLocal</c> answer carries this manifest (the slices to pull) — the merge then waits for the NAS pull.</summary>
        public bool SaveAnswerCarriesSlices;

        public Rig(params int[] nodes)
        {
            Master = new ClusterMaster(Bus, TestClusterConfig.NothingMandatory());
            Master.RegisterAggregator(new UnsavedCheckAggregator());
            Master.RegisterAggregator(new StorageConsensusAggregator());
            foreach (var n in nodes)
                Inject(() => Bus.PublishManaged(new NodeHeartbeatEvent
                {
                    NodeId = (Fdp.Core.NodeId)(n), LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
                    WallTicksUtc = DateTimeOffset.UtcNow.Ticks, SubsystemName = "SimHost",
                }));
            Frame();
        }

        private NodeOpCompletedEvent? Respond(ExecuteNodeOpIntent i)
        {
            if (i.Operation == FdpNodeOpType.CommitState) return null;   // the slave applies it without an ACK
            if (i.Operation == FdpNodeOpType.CheckUnsaved && SilentOnCheck.Contains(i.TargetNodeId)) return null;
            object? payload = null;
            var status = OrchestrationStatusCode.Success;
            if (i.Operation == FdpNodeOpType.CheckUnsaved)
                payload = CheckAnswer.TryGetValue(i.TargetNodeId, out var a) ? a : "unchanged";
            else if (i.Operation == FdpNodeOpType.SerializeLocal)
            {
                status = SaveRoundStatus;
                if (SaveAnswerCarriesSlices && status == OrchestrationStatusCode.Success)
                    payload = new List<FileManifestEntry> { new() { SourceUnc = "n" + i.TargetNodeId, RelativeDest = $"s/.slices/{i.TargetNodeId}.json", DocType = "scenario" } };
            }
            return new NodeOpCompletedEvent
            {
                TransactionId = i.TransactionId, Operation = i.Operation, NodeId = i.TargetNodeId,
                StatusCode = status, IsParticipating = !(i.Operation == FdpNodeOpType.CheckUnsaved && string.IsNullOrEmpty(payload as string)),
                ResultPayload = payload,
            };
        }

        private readonly List<Action> _pending = new();

        /// <summary>Publishes an event so the master READS it on its next tick — ⚠ the bus is double-buffered: an event published before a frame's first swap is gone by its second.</summary>
        public void Inject(Action publish) => _pending.Add(publish);

        /// <summary>One frame: the nodes read what the master published and answer; the master ticks.</summary>
        public void Frame()
        {
            Bus.SwapBuffers();
            foreach (var p in _pending) p();
            _pending.Clear();
            foreach (var i in Bus.ReadManaged<ExecuteNodeOpIntent>().ToList())
            {
                Intents.Add(i);
                if (Respond(i) is { } ack) Bus.PublishManaged(ack);
            }
            Completed.AddRange(Bus.ReadManaged<ClusterOpCompletedEvent>());
            SeenPrefetches.AddRange(Bus.ReadManaged<ExecutePrefetchIntent>());
            Bus.SwapBuffers();
            Master.Tick();
        }

        public void Settle(int frames = 12) { for (int f = 0; f < frames; f++) Frame(); }

        public void Request(Guid id, ClusterOpType op, string? payloadJson = null)
            => Master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = id, OperationType = op, PayloadJson = payloadJson ?? string.Empty });

        public void GoTo(ClusterState target)
        {
            Request(Guid.NewGuid(), ClusterOpType.TransitionState, System.Text.Json.JsonSerializer.Serialize(new { TargetState = target.ToString() }));
            Settle();
            Assert.Equal(target, Master.CurrentClusterState);
        }

        /// <summary>Asks one check round and returns how it ended.</summary>
        public ClusterOpCompletedEvent Check()
        {
            var id = Guid.NewGuid();
            Request(id, ClusterOpType.CheckUnsavedChanges);
            Settle(4);
            return Completed.Last(c => c.RequestId == id);
        }

        public void Save(string name = "scn")
        {
            Request(Guid.NewGuid(), ClusterOpType.SaveScenario, $"{{\"ScenarioName\":\"{name}\"}}");
            Settle(4);
        }
    }

    private static UnsavedCheckResult ResultOf(ClusterOpCompletedEvent e)
    {
        Assert.Equal(OrchestrationStatusCode.Success, e.StatusCode);
        return Assert.IsType<UnsavedCheckResult>(e.ResultPayload);
    }

    /// <summary>The master fans <c>CheckUnsaved</c> to EVERY participant and reports the aggregate (and remembers it).</summary>
    [Fact(Timeout = 10_000)]
    public void TheRound_AsksEveryParticipant_AndReportsTheAggregate()
    {
        var rig = new Rig(A, B);
        rig.GoTo(ClusterState.OperatingEdit);
        rig.CheckAnswer[A] = "unchanged";
        rig.CheckAnswer[B] = "changed";

        var done = rig.Check();

        var asked = rig.Intents.Where(i => i.Operation == FdpNodeOpType.CheckUnsaved).Select(i => i.TargetNodeId.Value).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { A, B }, asked);
        var r = ResultOf(done);
        Assert.True(r.Changed);
        Assert.Equal(new[] { B }, r.ChangedNodes);
        Assert.Equal(r, rig.Master.LastUnsavedCheck);
    }

    [Fact(Timeout = 10_000)]
    public void EveryNodeUnchanged_OrSilent_ReportsUnchanged()
    {
        var rig = new Rig(A, B);
        rig.GoTo(ClusterState.OperatingEdit);
        rig.CheckAnswer[B] = "";   // a host with no ECS world: not participating

        var r = ResultOf(rig.Check());

        Assert.False(r.Changed);
        Assert.Empty(r.ChangedNodes);
        Assert.False(rig.Master.LastUnsavedCheck!.Changed);
    }

    /// <summary>
    /// ⭐⭐ CE-3340 ⑤ (R-4001) — <b>the round always ends</b>, which is why the HTTP gate needs no deadline of its own: a node that never answers holds the round
    /// (no timer fails it), and the moment its heartbeat timeout ejects it the master completes the round on the nodes that remain
    /// (<c>DropFromInFlightRounds</c>) — the answer is the survivors' verdict. 🔴 Mutation: <c>EjectNode</c> not calling <c>DropFromInFlightRounds</c> ⇒ the round
    /// never completes ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ARoundWhoseNodeIsEjected_StillCompletes_OnTheNodesThatRemain()
    {
        var rig = new Rig(A, B);
        rig.GoTo(ClusterState.OperatingEdit);
        rig.CheckAnswer[A] = "changed";
        rig.SilentOnCheck.Add(B);                                   // B died: it will never answer

        var id = Guid.NewGuid();
        rig.Request(id, ClusterOpType.CheckUnsavedChanges);
        rig.Settle(20);
        Assert.DoesNotContain(rig.Completed, c => c.RequestId == id);   // held — no timer ends it

        rig.Master.EjectNode(B);                                    // its heartbeat timed out
        rig.Settle(4);

        var r = ResultOf(rig.Completed.Last(c => c.RequestId == id));
        Assert.True(r.Changed);
        Assert.Equal(new[] { A }, r.ChangedNodes);                  // the verdict of the nodes that remain
    }

    /// <summary>Nobody to ask ⇒ "unchanged" at once (only the master's own fact could say otherwise).</summary>
    [Fact(Timeout = 10_000)]
    public void ZeroParticipants_ReportsUnchanged_WithoutAnyFanOut()
    {
        var rig = new Rig();   // no node
        rig.GoTo(ClusterState.OperatingEdit);

        var r = ResultOf(rig.Check());

        Assert.False(r.Changed);
        Assert.DoesNotContain(rig.Intents, i => i.Operation == FdpNodeOpType.CheckUnsaved);
    }

    /// <summary>Meaningful only while a scenario is open for EDITING: refused (Rejected + the reason) in Idle and in a live run — never answered "unchanged" as if it had looked.</summary>
    [Fact(Timeout = 10_000)]
    public void OutsideEditing_IsRefusedWithTheReason_AndAsksNoNode()
    {
        var rig = new Rig(A);
        var idle = rig.Check();                                                     // Idle
        Assert.Equal(OrchestrationStatusCode.Rejected, idle.StatusCode);
        Assert.Equal(UnsavedCheckOutcome.NotEditingReason, idle.ResultPayload);

        rig.GoTo(ClusterState.OperatingLive);
        var live = rig.Check();
        Assert.Equal(OrchestrationStatusCode.Rejected, live.StatusCode);
        Assert.Equal(UnsavedCheckOutcome.NotEditingReason, live.ResultPayload);

        Assert.DoesNotContain(rig.Intents, i => i.Operation == FdpNodeOpType.CheckUnsaved);
        Assert.Null(rig.Master.LastUnsavedCheck);
    }

    /// <summary>
    /// A node cannot know a save failed AFTER it wrote its slice (it re-baselined at Commit): a failed save round makes the MASTER report changed — until a save
    /// completes. 🔴 Without the fold-in the check says "unchanged" for a scenario whose file on disk is stale.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void AFailedSaveRound_MakesTheNextCheckChanged_UntilASaveCompletes()
    {
        var rig = new Rig(A, B);
        rig.GoTo(ClusterState.OperatingEdit);
        Assert.False(ResultOf(rig.Check()).Changed);                // both nodes unchanged

        rig.SaveRoundStatus = OrchestrationStatusCode.Failure;
        rig.Save();
        Assert.True(rig.Master.SaveFailedSinceLoad);
        var afterFailure = ResultOf(rig.Check());
        Assert.True(afterFailure.Changed, "every node says unchanged, but the save failed — the file on disk is stale");
        Assert.Empty(afterFailure.ChangedNodes);                    // only the master's own fact made it changed

        rig.SaveRoundStatus = OrchestrationStatusCode.Success;
        rig.Save();                                                 // a save that completes (nothing to pull)
        Assert.False(rig.Master.SaveFailedSinceLoad);
        Assert.False(ResultOf(rig.Check()).Changed);
    }

    /// <summary>The node round can succeed and the NAS pull / merge still fail (<c>StorageProcessManager</c> reports it): the file on disk is stale, the nodes do not know.</summary>
    [Fact(Timeout = 10_000)]
    public void ASaveWhoseMergeFails_IsUnsaved_AndOneWhoseMergeSucceeds_IsSaved()
    {
        var rig = new Rig(A);
        rig.GoTo(ClusterState.OperatingEdit);
        rig.SaveAnswerCarriesSlices = true;

        rig.Save();
        Guid tx = rig.Intents.Last(i => i.Operation == FdpNodeOpType.SerializeLocal).TransactionId;
        Assert.False(ResultOf(rig.Check()).Changed, "the slices are written; the pull + merge has not reported yet — not (yet) a failure");

        var failedTx = tx;
        rig.Inject(() => rig.Bus.PublishManaged(new ScenarioSaveFinishedEvent { TransactionId = failedTx, Succeeded = false }));
        rig.Settle(2);
        Assert.True(ResultOf(rig.Check()).Changed, "the merge failed: scenario.json on disk is stale");

        rig.Save();
        tx = rig.Intents.Last(i => i.Operation == FdpNodeOpType.SerializeLocal).TransactionId;
        var savedTx = tx;
        rig.Inject(() => rig.Bus.PublishManaged(new ScenarioSaveFinishedEvent { TransactionId = savedTx, Succeeded = true }));
        rig.Settle(2);
        Assert.False(ResultOf(rig.Check()).Changed);
    }

    /// <summary>A different scenario (or none) is open after the next load: the old save failure and the old answer no longer describe anything.</summary>
    [Fact(Timeout = 10_000)]
    public void ANewLoad_ForgetsTheOldSaveFailure_AndTheOldAnswer()
    {
        var rig = new Rig(A);
        rig.GoTo(ClusterState.OperatingEdit);
        rig.SaveRoundStatus = OrchestrationStatusCode.Failure;
        rig.Save();
        Assert.True(ResultOf(rig.Check()).Changed);
        Assert.NotNull(rig.Master.LastUnsavedCheck);

        rig.GoTo(ClusterState.Idle);                                // the unload before a load
        Assert.False(rig.Master.SaveFailedSinceLoad);
        Assert.Null(rig.Master.LastUnsavedCheck);

        rig.GoTo(ClusterState.OperatingEdit);
        Assert.False(ResultOf(rig.Check()).Changed);
    }

    // ── ⭐⭐ CE-4099 AUTOSAVE (AQ108 §4c A1–A3, §4c.1) — the master's trigger ──────────────────────────────────────────────────────────────────

    /// <summary>Into editing WITH a scenario open (autosave runs only for one), the autosave clock driven by the rail.</summary>
    private static (Rig Rig, Func<double> Advance) EditingWithAutosave(double everySeconds = 60)
    {
        var rig = new Rig(A, B);
        double now = 1000;
        rig.Master.AutosaveClock = () => now;
        rig.Master.AutosaveEvery = TimeSpan.FromSeconds(everySeconds);
        var tx = Guid.NewGuid();   // a scenario load parks until its prefetch reports done (as the bootstrap rails drive it)
        rig.Inject(() => rig.Bus.PublishManaged(new TransitionStateIntent { TransactionId = tx, TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit, ScenarioId = "scn" }));
        rig.Frame();
        rig.Inject(() => rig.Bus.PublishManaged(new PrefetchDistributionCompletedEvent { RequestId = tx, ScenarioId = "scn", IsSuccess = true }));
        rig.Settle();
        Assert.Equal(ClusterState.OperatingEdit, rig.Master.CurrentClusterState);
        return (rig, () => now += everySeconds + 1);
    }

    private static Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload[] AutosaveFanOuts(Rig rig)
        => rig.Intents.Where(i => i.Operation == FdpNodeOpType.SerializeLocal)
                      .Select(i => i.DomainPayload).OfType<Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload>().ToArray();

    /// <summary>
    /// ⭐⭐ CE-4099 A1/A2 — editing, the interval passed, a node CHANGED ⇒ the check round, then the SAVE ROUND with the autosave flag to every participant
    /// (the same fan-out as a save). 🔴 Red-proof (compiles): <c>OnAutosaveCheck</c> returning before the fan-out ⇒ no autosave payload ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4099_TheIntervalPassed_AndANodeChanged_StartsTheSaveRoundAsAnAutosave()
    {
        var (rig, advance) = EditingWithAutosave();
        rig.CheckAnswer[A] = "changed";
        rig.Settle(4);
        Assert.Empty(AutosaveFanOuts(rig));                                   // not yet: the interval starts when editing does

        advance();
        rig.Settle(6);

        Assert.Contains(rig.Intents, i => i.Operation == FdpNodeOpType.CheckUnsaved);
        var fanned = AutosaveFanOuts(rig);
        Assert.Equal(2, fanned.Length);                                       // A and B
        Assert.All(fanned, p => Assert.True(p.Autosave && p.ScenarioName == "scn" && p.OnlyGuids == null));
        Assert.Equal(1, rig.Master.AutosavesStarted);
    }

    /// <summary>
    /// ⭐ CE-4099 phase 2 (A1's <c>autosaveMinutes</c>) — the setting arrives as a <c>SetRunSettingsIntent</c>: 3 ⇒ <c>AutosaveEvery</c> = 3 min and the broadcast says 3; 0 ⇒ OFF (no check round in editing, however long it passes) and the broadcast says 0.
    /// 🔴 Red-proof (compiles): <c>ProcessSetRunSettingsIntent</c> not applying <c>AutosaveMinutes</c> ⇒ the first assertion fails.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4099_AutosaveMinutes_IsSetByTheOp_RidesTheBroadcast_AndZeroTurnsItOff()
    {
        var (rig, advance) = EditingWithAutosave();
        Assert.Equal(1, rig.Master.AutosaveMinutes);                          // the rail's 60 s, as whole minutes (rounded up)
        rig.Inject(() => rig.Bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), AutosaveMinutes = 3 }));
        rig.Settle(3);
        Assert.Equal(TimeSpan.FromMinutes(3), rig.Master.AutosaveEvery);
        Assert.Equal(3, rig.Master.AutosaveMinutes);
        rig.Inject(() => rig.Bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), RecordLiveRuns = true }));   // an op that does not carry it
        rig.Settle(3);
        Assert.Equal(3, rig.Master.AutosaveMinutes);

        rig.Inject(() => rig.Bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), AutosaveMinutes = 0 }));
        rig.Settle(3);
        Assert.Equal(TimeSpan.Zero, rig.Master.AutosaveEvery);
        Assert.Equal(0, rig.Master.AutosaveMinutes);
        rig.Intents.Clear();
        for (int i = 0; i < 4; i++) { advance(); rig.Settle(6); }
        Assert.DoesNotContain(rig.Intents, x => x.Operation == FdpNodeOpType.CheckUnsaved);   // OFF: no check round while editing
        Assert.Equal(0, rig.Master.AutosavesStarted);
    }

    /// <summary>⭐ CE-4099 A1 — nothing changed ⇒ the check round runs, no autosave is written.</summary>
    [Fact(Timeout = 10_000)]
    public void CE4099_NothingChanged_WritesNothing()
    {
        var (rig, advance) = EditingWithAutosave();
        rig.Settle(2);
        advance();
        rig.Settle(6);
        Assert.Contains(rig.Intents, i => i.Operation == FdpNodeOpType.CheckUnsaved);
        Assert.Empty(AutosaveFanOuts(rig));
        Assert.Equal(0, rig.Master.AutosavesStarted);
    }

    /// <summary>
    /// ⭐⭐ CE-4099 A3 — an autosave is NOT a save: its round failing does not make the scenario "save failed" (and a success does not clear one), and
    /// it is never started outside editing. 🔴 Red-proof (compiles): the autosave registering as a save round (<c>NoteSaveRoundStarted(txId)</c>) ⇒
    /// <c>SaveFailedSinceLoad</c> true ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4099_AFailedAutosave_IsNotAFailedSave_AndIdleNeverAutosaves()
    {
        var (rig, advance) = EditingWithAutosave();
        rig.CheckAnswer[A] = "changed";
        rig.SaveRoundStatus = OrchestrationStatusCode.Rejected;              // every node fails its part
        rig.Settle(2);
        advance();
        rig.Settle(8);
        Assert.Equal(1, rig.Master.AutosavesStarted);
        Assert.False(rig.Master.SaveFailedSinceLoad);

        var idle = new Rig(A, B);
        double t = 0;
        idle.Master.AutosaveClock = () => t;
        idle.Master.AutosaveEvery = TimeSpan.FromSeconds(1);
        for (int i = 0; i < 5; i++) { t += 10; idle.Frame(); }
        Assert.DoesNotContain(idle.Intents, x => x.Operation == FdpNodeOpType.CheckUnsaved);
        Assert.Equal(0, idle.Master.AutosavesStarted);
    }

    /// <summary>
    /// ⭐⭐ CE-4099 (AQ108 §4c A7) — a load that RECOVERS the autosave: the prefetch is asked for the recovery, and the scenario then reads CHANGED (what is
    /// open is not the saved file) although every node's world equals its baseline — until a real save completes. 🔴 Red-proof (compiles): the master not
    /// folding <c>RecoveredAutosaveSinceLoad</c> into the check ⇒ "unchanged" right after the recovery ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4099_ARecoveredAutosave_ReadsChanged_UntilASave()
    {
        var rig = new Rig(A, B);
        var tx = Guid.NewGuid();
        rig.Inject(() => rig.Bus.PublishManaged(new TransitionStateIntent { TransactionId = tx, TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit, ScenarioId = "scn", RecoverAutosave = true }));
        rig.Frame();
        rig.Frame();
        Assert.Contains(rig.SeenPrefetches, p => p.RequestId == tx && p.RecoverAutosave);   // the prefetch is asked for the recovery
        rig.Inject(() => rig.Bus.PublishManaged(new PrefetchDistributionCompletedEvent { RequestId = tx, ScenarioId = "scn", IsSuccess = true }));
        rig.Settle();
        Assert.Equal(ClusterState.OperatingEdit, rig.Master.CurrentClusterState);

        Assert.True(rig.Master.RecoveredAutosaveSinceLoad);
        Assert.True(ResultOf(rig.Check()).Changed);        // every node says "unchanged" — the master's fact says otherwise

        rig.Save("scn");                                   // a real save that completes
        Assert.False(rig.Master.RecoveredAutosaveSinceLoad);
        Assert.False(ResultOf(rig.Check()).Changed);
    }
}
