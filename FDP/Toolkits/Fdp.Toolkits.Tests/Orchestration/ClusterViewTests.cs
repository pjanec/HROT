using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-1436</c> (<c>R-3066</c>) — <see cref="ClusterView"/>, the ONE cluster read model of a node: the facts the master last said, folded from the
/// fact events. 📄 docs/DESIGN_Cluster_View.md §3 V3. The node's <see cref="ClusterSlave"/> owns one and its follow-fields read it.
/// </summary>
public sealed class ClusterViewTests
{
    private static (FdpEventBus Bus, ClusterView View) Rig()
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        return (bus, new ClusterView(bus));
    }


    private static void HearStruct<T>(FdpEventBus bus, ClusterView view, T ev) where T : struct
    {
        bus.PublishManaged(ev);
        bus.SwapBuffers();
        view.Update();
    }

    /// <summary>
    /// The state broadcast is folded once: state, exercise, axis and the two recording flags; "no master heard" (<c>HasFollowedState</c> false) is told
    /// from a heard <c>Idle</c>, <c>MasterGone</c> is set by its broadcast and cleared by the next real one, and the flags survive a MasterGone.
    /// 🔴 Mutation: <c>State</c> not folded on a real broadcast ⇒ the first fold assertion fails.
    /// </summary>
    [Fact]
    public void TheStateBroadcast_IsFoldedOnce_WithMasterGoneAndTheFollowedFlag()
    {
        var (bus, view) = Rig();
        Assert.False(view.HasFollowedState);
        Assert.Equal(ClusterState.Idle, view.State);
        Assert.Equal(ReplayAxis.Sim, view.ReplayAxis);

        var exercise = Guid.NewGuid();
        HearStruct(bus, view, new ClusterStateUpdateEvent
        {
            CurrentState = ClusterState.OperatingLive, ExerciseId = exercise, ReplayAxis = ReplayAxis.Wall, RunNotRecorded = true, RecordLiveRunsOff = true,
        });
        Assert.True(view.HasFollowedState);
        Assert.Equal((ClusterState.OperatingLive, exercise, ReplayAxis.Wall, true, true),
            (view.State, view.ExerciseId, view.ReplayAxis, view.RunNotRecorded, view.RecordLiveRunsOff));

        // CE-3341 - the armed mode is one more field of the same fold: Live before any broadcast, the master's word after, replaced by the next real one
        Assert.Equal(ClusterArmedMode.Live, view.ArmedMode);
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit, ArmedMode = ClusterArmedMode.Preview });
        Assert.Equal(ClusterArmedMode.Preview, view.ArmedMode);
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = exercise, ReplayAxis = ReplayAxis.Wall, RunNotRecorded = true, RecordLiveRunsOff = true });   // a sender that does not set it
        Assert.Equal(ClusterArmedMode.Live, view.ArmedMode);

        // R-1043 - LastLoadMode is the same kind of fold
        Assert.Equal(ClusterArmedMode.Live, view.LastLoadMode);
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit, LastLoadMode = ClusterArmedMode.Preview, RunNotRecorded = true, RecordLiveRunsOff = true, ReplayAxis = ReplayAxis.Wall });
        Assert.Equal(ClusterArmedMode.Preview, view.LastLoadMode);

        HearStruct(bus, view, new ClusterStateUpdateEvent { MasterGone = true });
        Assert.True(view.MasterGone);
        Assert.True(view.HasFollowedState);                       // a master WAS heard
        Assert.Equal(ClusterState.Idle, view.State);
        Assert.True(view.RunNotRecorded);                         // the flags are the last real broadcast's, not reset by the master leaving

        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });
        Assert.False(view.MasterGone);
        Assert.False(view.RunNotRecorded);                        // the next real broadcast replaces them
    }

    /// <summary>
    /// A MasterGone with no real broadcast before it is not a "followed" state (the start gate would read the <c>Idle</c> default as the cluster's).
    /// 🔴 Mutation: <c>HasFollowedState = true</c> added to the MasterGone branch ⇒ fails.
    /// </summary>
    [Fact]
    public void AMasterGoneBeforeAnyBroadcast_IsNotAFollowedState()
    {
        var (bus, view) = Rig();
        HearStruct(bus, view, new ClusterStateUpdateEvent { MasterGone = true });
        Assert.True(view.MasterGone);
        Assert.False(view.HasFollowedState);
    }

    /// <summary>
    /// The inventory: an empty view says "none arrived" (<c>HasInventory</c> false) rather than an empty list the master sent; one arrival replaces all
    /// four arrays. 🔴 Mutation: <c>HasInventory</c> never set ⇒ fails.
    /// </summary>
    [Fact]
    public void TheInventory_IsTheMastersLastWord_AndAbsentUntilOneArrives()
    {
        var (bus, view) = Rig();
        Assert.False(view.HasInventory);

        var ex = new ExerciseInventoryItem(Guid.NewGuid(), DateTime.UtcNow, TimeSpan.FromSeconds(5), "scn");
        HearStruct(bus, view, new AssetInventoryUpdateEvent { LocalScenarios = new[] { "a", "b" }, LocalExercises = new[] { ex } });

        Assert.True(view.HasInventory);
        Assert.Equal(new[] { "a", "b" }, view.AvailableScenarios);
        Assert.Equal(ex, Assert.Single(view.AvailableExercises));
        Assert.Empty(view.ArchivedExercises);                     // null on the event ⇒ empty, never null
    }

    /// <summary>
    /// ⭐ <c>CE-4093</c> — the check round: an outcome is found by request id; a refusal carries the master's reason; the answer becomes
    /// <c>LastUnsavedCheck</c> only while the cluster is editing and is dropped when it leaves; an InProgress status is not an outcome.
    /// 🔴 Mutation: <c>LastUnsavedCheck</c> not cleared on leaving editing ⇒ the last assertion fails.
    /// </summary>
    [Fact]
    public void TheCheckRound_IsFoundByRequestId_AndItsAnswerLivesOnlyWhileEditing()
    {
        var (bus, view) = Rig();
        var round = Guid.NewGuid();
        var refused = Guid.NewGuid();
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });

        HearStruct(bus, view, new ClusterOpCompletedEvent { RequestId = round, StatusCode = OrchestrationStatusCode.InProgress });
        Assert.Null(view.UnsavedCheckOutcomeFor(round));

        HearStruct(bus, view, new ClusterOpCompletedEvent
        {
            RequestId = round, StatusCode = OrchestrationStatusCode.Success, ResultPayload = new UnsavedCheckResult(true, new[] { 4 }),
        });
        Assert.True(view.UnsavedCheckOutcomeFor(round)!.Result!.Changed);
        Assert.True(view.LastUnsavedCheck!.Changed);

        HearStruct(bus, view, new ClusterOpCompletedEvent
        {
            RequestId = refused, StatusCode = OrchestrationStatusCode.Failure, ResultPayload = UnsavedCheckOutcome.NotEditingReason,
        });
        var o = view.UnsavedCheckOutcomeFor(refused)!;
        Assert.Null(o.Result);
        Assert.Equal(UnsavedCheckOutcome.NotEditingReason, o.Refusal);

        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle });
        Assert.Null(view.LastUnsavedCheck);                       // the answer described a scenario that is no longer open
        Assert.NotNull(view.UnsavedCheckOutcomeFor(round));       // …while the round's own outcome is still found by id
    }

    /// <summary>An answer that arrives while NOT editing is never the last verdict (it would describe nothing open).</summary>
    [Fact]
    public void ACheckAnswer_OutsideEditing_IsNotTheLastVerdict()
    {
        var (bus, view) = Rig();
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive });
        var id = Guid.NewGuid();
        HearStruct(bus, view, new ClusterOpCompletedEvent { RequestId = id, StatusCode = OrchestrationStatusCode.Success, ResultPayload = new UnsavedCheckResult(true, Array.Empty<int>()) });
        Assert.Null(view.LastUnsavedCheck);
        Assert.NotNull(view.UnsavedCheckOutcomeFor(id));
    }

    /// <summary>
    /// The outcome ring keeps the last <see cref="ClusterView.OutcomeCapacity"/> ops by request id, oldest forgotten first. 🔴 Mutation: capacity not
    /// enforced (no eviction) ⇒ the first op is still found ⇒ fails.
    /// </summary>
    [Fact]
    public void TheOutcomeRing_KeepsTheLast32_ByRequestId()
    {
        var (bus, view) = Rig();
        var ids = new Guid[ClusterView.OutcomeCapacity + 1];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = ids[i], StatusCode = OrchestrationStatusCode.Success, ResultPayload = UnsavedCheckResult.Unchanged });
        }
        bus.SwapBuffers();
        view.Update();

        Assert.Equal(32, ClusterView.OutcomeCapacity);
        Assert.Null(view.UnsavedCheckOutcomeFor(ids[0]));         // evicted
        Assert.NotNull(view.UnsavedCheckOutcomeFor(ids[1]));
        Assert.NotNull(view.UnsavedCheckOutcomeFor(ids[^1]));
    }

    /// <summary>
    /// Folding the same frame twice changes nothing — the node's slave and a composing cache (the editor's) both drain the one view each frame.
    /// 🔴 Mutation: the last verdict re-created on every fold (<c>check with { }</c>) ⇒ <c>Assert.Same</c> fails.
    /// </summary>
    [Fact]
    public void UpdatingTwiceInOneFrame_ChangesNothing()
    {
        var (bus, view) = Rig();
        var id = Guid.NewGuid();
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });
        bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = id, StatusCode = OrchestrationStatusCode.Success, ResultPayload = new UnsavedCheckResult(true, Array.Empty<int>()) });
        bus.SwapBuffers();
        view.Update();
        var once = (view.State, view.LastUnsavedCheck, view.UnsavedCheckOutcomeFor(id));

        view.Update();

        Assert.Equal(once.State, view.State);
        Assert.Same(once.LastUnsavedCheck, view.LastUnsavedCheck);
        Assert.Equal(once.Item3, view.UnsavedCheckOutcomeFor(id));
    }

    /// <summary>A view over no bus (the test / offline shape) folds nothing and never throws.</summary>
    [Fact]
    public void AViewOverNoBus_IsInert()
    {
        var view = new ClusterView(null);
        view.Update();
        Assert.Equal(ClusterState.Idle, view.State);
        Assert.False(view.HasFollowedState);
        Assert.Null(view.UnsavedCheckOutcomeFor(Guid.NewGuid()));
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-3344</c> (R-3069) — <see cref="ClusterView.CanOperate"/> is cluster-ready FROM THIS NODE: the master's state has reached it, is not <c>Degraded</c>, names nothing
    /// still missing (<c>WaitingFor</c>), the master is present, and (for a view that has a node id) THIS node is in the master's in-sync set. Each clause alone closes it; the reason names it.
    /// 🔴 Mutations (each compiles): <c>NotOperableReason</c> without the <c>WaitingFor</c> arm ⇒ the waiting cell reds; without the <c>Degraded</c> arm ⇒ the Degraded cell; <c>IsInSync</c> returning
    /// true ⇒ the outsider cell; <c>MasterGone</c> arm removed ⇒ the master-gone cell.
    /// </summary>
    [Fact]
    public void CE3344_CanOperate_IsClusterReadyFromThisNode_EachClauseAloneClosesIt()
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        var view = new ClusterView(bus, nodeId: 5);

        Assert.False(view.CanOperate);                                               // nothing heard yet
        Assert.Contains("no cluster state", view.NotOperableReason);

        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, WaitingFor = "Brain (CGF)" });
        HearStruct(bus, view, new InSyncSetChangedEvent { NodeIds = new[] { 5 } });
        Assert.False(view.CanOperate);                                               // the cluster is waiting
        Assert.Equal("Brain (CGF)", view.WaitingFor);
        Assert.Contains("waiting for: Brain (CGF)", view.NotOperableReason);

        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.Degraded, WaitingFor = "" });
        Assert.False(view.CanOperate);                                               // Degraded
        Assert.Contains("Degraded", view.NotOperableReason);

        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, WaitingFor = "" });
        Assert.True(view.CanOperate, view.NotOperableReason);                        // ready, and this node is in sync
        Assert.Equal(string.Empty, view.WaitingFor);

        HearStruct(bus, view, new InSyncSetChangedEvent { NodeIds = new[] { 6 } });  // the master dropped this node from the set
        Assert.False(view.CanOperate);
        Assert.Contains("in-sync", view.NotOperableReason);
        HearStruct(bus, view, new InSyncSetChangedEvent { NodeIds = new[] { 5, 6 } });
        Assert.True(view.CanOperate);

        HearStruct(bus, view, new ClusterStateUpdateEvent { MasterGone = true });
        Assert.False(view.CanOperate);
        Assert.Contains("master is gone", view.NotOperableReason);

        // a view with no node id (an observer) does not ask to be in sync — and still follows the rest
        var observerBus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(observerBus);
        var observer = new ClusterView(observerBus);
        HearStruct(observerBus, observer, new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle });
        Assert.True(observer.CanOperate);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> (R-3073, success 4) — the master's ERROR (a required node / role lost with a world loaded) folds into the view: <c>IsInError</c>, <c>CanOperate</c> false, <c>NotOperableReason</c> = the error
    /// BEFORE the Degraded / waiting texts (it says why and what to do); a state without it clears it (the cluster reached Idle).
    /// 🔴 Mutations (each compiles): <c>NotOperableReason</c> without the error arm ⇒ the reason reads "Degraded — waiting for…"; <c>DrainClusterState</c> not folding <c>ErrorReason</c> ⇒ <c>IsInError</c> false.
    /// </summary>
    [Fact]
    public void CE3345_TheClusterError_IsFolded_ClosesCanOperate_AndIsTheFirstReason()
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        var view = new ClusterView(bus, nodeId: 5);
        HearStruct(bus, view, new InSyncSetChangedEvent { NodeIds = new[] { 5 } });
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive });
        Assert.True(view.CanOperate, view.NotOperableReason);
        Assert.False(view.IsInError);

        HearStruct(bus, view, new ClusterStateUpdateEvent
            { CurrentState = ClusterState.Degraded, WaitingFor = "Brain (CGF)", ErrorReason = "Brain (CGF, node 3) lost at sim 00:12:31" });
        Assert.True(view.IsInError);
        Assert.False(view.CanOperate);
        Assert.Equal("Brain (CGF, node 3) lost at sim 00:12:31", view.ErrorReason);
        Assert.StartsWith("the cluster is in error — Brain (CGF, node 3) lost", view.NotOperableReason);   // before "Degraded" / "waiting for"
        Assert.DoesNotContain("waiting for", view.NotOperableReason);

        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, WaitingFor = "Brain (CGF)" });   // Close reached Idle: the error is gone, the latch waits as CE-3344
        Assert.False(view.IsInError);
        Assert.Equal(string.Empty, view.ErrorReason);
        Assert.Contains("waiting for: Brain (CGF)", view.NotOperableReason);
    }

    // ── ⭐⭐ CE-3345 part B (R-3070, R-3072, AQ110 B/C/D) — the two levels and the gate policies ─────────────────────────────────────────────

    private static (FdpEventBus Bus, ClusterView View) OperableRig(int nodeId = 1)
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        var view = new ClusterView(bus, nodeId);
        HearStruct(bus, view, new InSyncSetChangedEvent { NodeIds = new[] { nodeId } });
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive });
        Assert.True(view.CanOperate);
        return (bus, view);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success conditions 6 and 7 (the view half) — the CLUSTER level (<c>ClusterPlayReady</c>, <c>LoadProgress</c>) is folded from the master's broadcast, the LOCAL level from the node's own report; and the two host
    /// policies read them differently: a <b>Player</b>'s gate opens on its OWN PlayReady while the cluster is still loading for others (irrelevant to it); an <b>Operator</b>'s stays closed until the cluster is play-ready — and while
    /// the node itself loads. Neither opens while the cluster cannot be operated at all.
    /// 🔴 Mutations (each compiles): <c>GateOpen</c> Player arm = the Operator expression ⇒ the "player open while the cluster loads" cell reds; the Operator arm without <c>!IsLocalLoading</c> ⇒ the "local loading" cell reds; without the <c>ClusterPlayReady</c> clause ⇒ the "cluster loading" cell reds;
    /// <c>ClusterPlayReady</c> not folded ⇒ the final Operator cell reds; <c>CanOperate</c> dropped from either arm ⇒ the not-operable cell reds.
    /// </summary>
    [Fact]
    public void CE3345_TheClusterLevelIsFoldedFromTheBroadcast_AndThePlayerAndOperatorGatesReadItDifferently()
    {
        var (bus, view) = OperableRig();
        Assert.False(view.ClusterPlayReady);
        Assert.False(view.IsClusterLoading);
        view.NoteLocalPhase(NodePhase.PlayReady, null, null);

        // another participant is still loading: the cluster is not play-ready
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ClusterPlayReady = false, LoadProgress = "CGF (node 2): Terrain" });
        Assert.True(view.IsClusterLoading);
        Assert.Equal("CGF (node 2): Terrain", view.LoadProgress);
        Assert.True(view.GateOpen(HostGatePolicy.Player));      // ⭐ the player plays on its OWN play-ready: what the others load is irrelevant to it
        Assert.False(view.GateOpen(HostGatePolicy.Operator));   // ⭐ the operator waits for the cluster

        // this node loads itself: neither opens
        view.NoteLocalPhase(NodePhase.Loading, "Terrain", null);
        Assert.False(view.GateOpen(HostGatePolicy.Player));
        Assert.False(view.GateOpen(HostGatePolicy.Operator));
        Assert.Equal("Terrain", view.LocalStep);

        // the cluster is done but THIS node is still loading (an observer is excluded from the cluster level): the operator's own load keeps its gate shut
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ClusterPlayReady = true, LoadProgress = "" });
        Assert.False(view.GateOpen(HostGatePolicy.Operator));
        Assert.False(view.GateOpen(HostGatePolicy.Player));

        // the whole cluster is play-ready and so is this node
        view.NoteLocalPhase(NodePhase.PlayReady, null, null);
        Assert.True(view.ClusterPlayReady);
        Assert.False(view.IsClusterLoading);
        Assert.True(view.GateOpen(HostGatePolicy.Operator));
        Assert.True(view.GateOpen(HostGatePolicy.Player));

        // not operable at all (a mandatory node is missing): both closed
        HearStruct(bus, view, new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ClusterPlayReady = true, WaitingFor = "Brain (CGF)" });
        Assert.False(view.GateOpen(HostGatePolicy.Operator));
        Assert.False(view.GateOpen(HostGatePolicy.Player));

        // no master ⇒ no cluster level
        HearStruct(bus, view, new ClusterStateUpdateEvent { MasterGone = true });
        Assert.False(view.ClusterPlayReady);
        Assert.False(view.IsClusterLoading);
    }

    /// <summary>
    /// ⭐ <c>CE-3345</c> success condition 4 (the view half) — a failed op's REASON (the failing node's, through the master's op status) is readable per request id; a success has none; an unknown id is unknown.
    /// 🔴 Mutation: <c>OutcomeOf</c> returning a null reason ⇒ the reason cell reds.
    /// </summary>
    [Fact]
    public void CE3345_TheOpStatusOfAFailedLoad_CarriesTheNodesReason()
    {
        var (bus, view) = Rig();
        var failed = Guid.NewGuid();
        var ok = Guid.NewGuid();
        HearStruct(bus, view, new ClusterOpCompletedEvent { RequestId = failed, StatusCode = OrchestrationStatusCode.Failure, ResultPayload = "cancelled by the user on node 2" });
        HearStruct(bus, view, new ClusterOpCompletedEvent { RequestId = ok, StatusCode = OrchestrationStatusCode.Success });

        Assert.Equal((OrchestrationStatusCode.Failure, "cancelled by the user on node 2"), view.OutcomeOf(failed)!.Value);
        Assert.Equal((OrchestrationStatusCode.Success, (string?)null), view.OutcomeOf(ok)!.Value);
        Assert.Null(view.OutcomeOf(Guid.NewGuid()));
    }
}
