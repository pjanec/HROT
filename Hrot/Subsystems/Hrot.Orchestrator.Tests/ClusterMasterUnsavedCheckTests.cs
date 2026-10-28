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
        /// <summary>What each node answers to <c>CheckUnsaved</c> (a missing node answers "unchanged").</summary>
        public readonly Dictionary<int, string> CheckAnswer = new();
        /// <summary>The status a node answers to a <c>SerializeLocal</c> (the save's node round); default Success.</summary>
        public OrchestrationStatusCode SaveRoundStatus = OrchestrationStatusCode.Success;
        /// <summary>When set, a successful <c>SerializeLocal</c> answer carries this manifest (the slices to pull) — the merge then waits for the NAS pull.</summary>
        public bool SaveAnswerCarriesSlices;

        public Rig(params int[] nodes)
        {
            Master = new ClusterMaster(Bus);
            Master.RegisterAggregator(new UnsavedCheckAggregator());
            Master.RegisterAggregator(new StorageConsensusAggregator());
            foreach (var n in nodes)
                Inject(() => Bus.PublishManaged(new NodeHeartbeatEvent
                {
                    NodeId = n, LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
                    WallTicksUtc = DateTimeOffset.UtcNow.Ticks, SubsystemName = "SimHost",
                }));
            Frame();
        }

        private NodeOpCompletedEvent? Respond(ExecuteNodeOpIntent i)
        {
            if (i.Operation == FdpNodeOpType.CommitState) return null;   // the slave applies it without an ACK
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

        var asked = rig.Intents.Where(i => i.Operation == FdpNodeOpType.CheckUnsaved).Select(i => i.TargetNodeId).OrderBy(x => x).ToArray();
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
}
