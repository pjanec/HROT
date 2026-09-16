using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// Unit tests for the canonical <see cref="NetworkGatewaySystem"/> (creator waiter) and its
    /// base <see cref="DeferredConstructionParticipant"/> — the reliable-init cross-node barrier
    /// (docs/DESIGN_Cross_Node_Construction_Barrier.md §1.1/§3a.4/§3a.5).
    ///
    /// <para>Since CE-2xx the gateway reads its peer set from the stamped
    /// <see cref="NetworkAckPeerSet"/> managed component, not <c>INetworkTopology</c> (retired).</para>
    /// </summary>
    public class NetworkGatewaySystemTests
    {
        private const int GatewayModuleId = 101;
        private const int LocalNodeId     = 1;

        // ── Setup helpers ─────────────────────────────────────────────────────

        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterComponent<NetworkIdentity>();   // CE-289/290: gateway reads NetId for the poll-store.
            repo.RegisterManagedComponent<NetworkAckPeerSet>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<ConstructionAck>();
            repo.RegisterEvent<DestructionOrder>();
            repo.RegisterEvent<DestructionAck>();
            return repo;
        }

        private static NetworkGatewaySystem NewGateway(EntityLifecycleModule elm, int timeoutFrames = -1)
            => new NetworkGatewaySystem(GatewayModuleId, LocalNodeId, elm, timeoutFrames);

        private static EntityLifecycleModule NewElm() => new EntityLifecycleModule(new GatewayTestTkbDb(), Array.Empty<int>());

        /// <summary>Runs one Execute tick: plays back the buffer and swaps buses so output events are visible.</summary>
        private static void RunTick(EntityRepository repo, IEcsModuleSystem sys, float dt = 0f)
        {
            sys.Execute(repo, dt);
            var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            cb.Playback(repo);
            repo.Bus.SwapBuffers();
        }

        private static void PublishOrder(EntityRepository repo, Entity e, long blueprintId = 0)
        {
            repo.Bus.Publish(new ConstructionOrder { Entity = e, BlueprintId = blueprintId, FrameNumber = 0, InitiatorModuleId = 0 });
            repo.Bus.SwapBuffers();
        }

        private static bool SawAck(EntityRepository repo, Entity e)
        {
            foreach (var ack in ((ISimulationView)repo).ReadEvents<ConstructionAck>())
                if (ack.Entity == e && ack.Success) return true;
            return false;
        }

        private static void StampPeers(EntityRepository repo, Entity e, params int[] peers)
            => repo.AddComponent(e, new NetworkAckPeerSet { ExpectedAckPeers = peers });

        // ── Fast path: no PendingNetworkAck → immediate ACK ───────────────────

        [Fact]
        public void Execute_AcksImmediately_WhenNoPendingNetworkAck()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm());
            var entity = repo.CreateEntity();

            PublishOrder(repo, entity);
            RunTick(repo, gateway);

            Assert.True(SawAck(repo, entity), "fast-mode entity should ack immediately");
        }

        // ── Reliable but empty peer set → immediate ACK ───────────────────────

        [Fact]
        public void Execute_AcksImmediately_WhenPendingAckButNoPeers()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm());
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            // no NetworkAckPeerSet stamped → zero peers

            PublishOrder(repo, entity);
            RunTick(repo, gateway);

            Assert.True(SawAck(repo, entity), "reliable entity with no peers should ack immediately");
        }

        // ── Reliable with a peer → deferred until that peer reports Active ─────

        [Fact]
        public void ReceiveLifecycleStatus_AcksConstruction_WhenPeerReportsActive()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm());
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            StampPeers(repo, entity, 2);

            PublishOrder(repo, entity);
            RunTick(repo, gateway);
            // RED-PROOF: still Constructing — no ack before the peer reports.
            Assert.False(SawAck(repo, entity), "must NOT ack before the peer reports Active");

            var cmd = ((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Active, cmd, 1);
            ((EntityCommandBuffer)cmd).Playback(repo);
            repo.Bus.SwapBuffers();

            Assert.True(SawAck(repo, entity), "should ack once the sole peer reports Active");
        }

        // ── Multi-peer: ACK only after EVERY peer reports Active ──────────────

        [Fact]
        public void MultiPeer_AcksOnlyAfterAllPeersReportActive()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm());
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            StampPeers(repo, entity, 2, 3);

            PublishOrder(repo, entity);
            RunTick(repo, gateway);

            // Peer 2 reports — still waiting for peer 3.
            var cmd1 = ((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Active, cmd1, 1);
            ((EntityCommandBuffer)cmd1).Playback(repo);
            repo.Bus.SwapBuffers();
            Assert.False(SawAck(repo, entity), "must NOT ack while peer 3 is still outstanding");

            // Peer 3 reports — now complete.
            var cmd2 = ((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 3, EntityLifecycle.Active, cmd2, 2);
            ((EntityCommandBuffer)cmd2).Playback(repo);
            repo.Bus.SwapBuffers();
            Assert.True(SawAck(repo, entity), "should ack once BOTH peers report Active");
        }

        // ── CE-288 (C2): the short phase-1 probe prunes a non-delivering peer + self-heals ─
        [Fact]
        public void Phase1Probe_PrunesSilentPeer_FiresSelfHeal_KeepsResponder()
        {
            using var repo = CreateRepo();
            var dropped = new System.Collections.Generic.List<int>();
            var gateway = new NetworkGatewaySystem(GatewayModuleId, LocalNodeId, NewElm(),
                                                   reliableInitTimeoutFrames: -1, onPeerUnsupported: dropped.Add);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            repo.AddComponent(entity, new NetworkIdentity { Value = 7000 });
            StampPeers(repo, entity, 2, 3);   // wait for peers 2 and 3

            PublishOrder(repo, entity);
            RunTick(repo, gateway);           // deferred; phase-1 clock starts

            // peer 2 reports Constructing (phase-1 = "participating"); peer 3 stays silent.
            var cmd = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Constructing, cmd, 1);
            cmd.Playback(repo); repo.Bus.SwapBuffers();

            repo.ResetGlobalVersion((uint)(NetworkGatewaySystem.PHASE1_TIMEOUT_FRAMES + 50));
            RunTick(repo, gateway);           // phase-1 window elapsed → prune the silent peer

            Assert.Equal(new[] { 3 }, dropped.ToArray());   // only peer 3 (never sent phase-1) is self-healed
            Assert.False(SawAck(repo, entity), "peer 2 sent phase-1 and hasn't reached Active — still waiting on it");
        }

        [Fact]
        public void Phase1Probe_AllSilent_PrunesAll_CompletesWithSuccess()
        {
            using var repo = CreateRepo();
            var dropped = new System.Collections.Generic.List<int>();
            var gateway = new NetworkGatewaySystem(GatewayModuleId, LocalNodeId, NewElm(),
                                                   reliableInitTimeoutFrames: -1, onPeerUnsupported: dropped.Add);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            repo.AddComponent(entity, new NetworkIdentity { Value = 7001 });
            StampPeers(repo, entity, 2, 3);

            PublishOrder(repo, entity);
            RunTick(repo, gateway);
            repo.ResetGlobalVersion((uint)(NetworkGatewaySystem.PHASE1_TIMEOUT_FRAMES + 50));
            RunTick(repo, gateway);           // both silent → both pruned → wait-set empties → complete

            Assert.Equal(new[] { 2, 3 }, dropped.OrderBy(x => x).ToArray());
            Assert.True(SawAck(repo, entity), "with no supporting peer left, the entity acks (does not hang)");
            Assert.Equal(ConstructionOutcome.Success, ConstructionResults.Get(repo, 7001).Outcome);
        }

        // ── CE-289 (C3): timeout ABORTS a reliable entity, never force-acks it ─
        [Fact]
        public void OnTimeout_AbortsReliableEntity_WritesFailedTimeout_AndDoesNotAck()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm(), timeoutFrames: 2);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            repo.AddComponent(entity, new NetworkIdentity { Value = 5555 });
            StampPeers(repo, entity, 2);   // one peer that never reports Active

            PublishOrder(repo, entity);
            RunTick(repo, gateway);        // frame 0: deferred; store = Pending
            Assert.False(SawAck(repo, entity), "must not ack while still waiting for the peer");
            Assert.Equal(ConstructionOutcome.Pending, ConstructionResults.Get(repo, 5555).Outcome);

            // The peer sends phase-1 (Constructing) — so it is a SUPPORTING host that is merely stuck before
            // Active, NOT an unsupported host (which the C2 short probe would prune instead). This is the case
            // the long abort timeout governs.
            var cmd0 = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Constructing, cmd0, 1);
            cmd0.Playback(repo); repo.Bus.SwapBuffers();

            repo.ResetGlobalVersion(100);  // advance well past the 2-frame timeout
            RunTick(repo, gateway);        // timeout → abort (peer kept by phase-1, then stuck → aborted)

            // A reliable entity is torn down, NOT force-activated (§3b.3).
            Assert.False(SawAck(repo, entity), "reliable entity must be aborted, not force-acked, on timeout");
            var r = ConstructionResults.Get(repo, 5555);
            Assert.Equal(ConstructionOutcome.Failed, r.Outcome);
            Assert.Equal(ConstructionFailReason.Timeout, r.Reason);
        }

        // ── A non-Active status never completes the handshake ─────────────────

        [Fact]
        public void ReceiveLifecycleStatus_Ignores_NonActiveStates()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm());
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            StampPeers(repo, entity, 2);

            PublishOrder(repo, entity);
            RunTick(repo, gateway);

            var cmd = ((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Constructing, cmd, 1);
            ((EntityCommandBuffer)cmd).Playback(repo);
            repo.Bus.SwapBuffers();

            Assert.False(SawAck(repo, entity), "a Constructing status must not complete the barrier");
        }

        // ── §2b node-id: the LOCAL node id in the peer set is not waited on ───

        [Fact]
        public void LocalNodeId_InPeerSet_IsExcluded()
        {
            using var repo = CreateRepo();
            var gateway = NewGateway(NewElm());
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            // Peer set is {local, 2}; the gateway must wait only for 2, never itself.
            StampPeers(repo, entity, LocalNodeId, 2);

            PublishOrder(repo, entity);
            RunTick(repo, gateway);
            Assert.False(SawAck(repo, entity), "still waiting for the one real peer");

            var cmd = ((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Active, cmd, 1);
            ((EntityCommandBuffer)cmd).Playback(repo);
            repo.Bus.SwapBuffers();
            Assert.True(SawAck(repo, entity), "the local node id was correctly excluded → the one peer completes it");
        }

        // ── Base class poll mode (item B): defer until a local condition clears ─

        private sealed class FlagReadinessParticipant : DeferredConstructionParticipant
        {
            public bool Ready;
            public FlagReadinessParticipant(int moduleId, EntityLifecycleModule elm) : base(moduleId, elm) { }
            protected override bool Participates(ISimulationView view, Entity entity, long blueprintId) => true;
            protected override bool TryImmediateComplete(ISimulationView view, Entity entity) => false; // always defer
            protected override bool TryComplete(ISimulationView view, Entity entity) => Ready;
        }

        [Fact]
        public void PollParticipant_DefersUntilLocalConditionReady()
        {
            using var repo = CreateRepo();
            var participant = new FlagReadinessParticipant(202, NewElm()) { Ready = false };
            var entity = repo.CreateEntity();

            PublishOrder(repo, entity);
            RunTick(repo, participant);
            Assert.False(SawAck(repo, entity), "poll participant must defer while its condition is not ready");

            participant.Ready = true;
            RunTick(repo, participant);
            Assert.True(SawAck(repo, entity), "poll participant acks once its condition becomes ready");
        }
    }

    // ── Stub TKB database used by NetworkGatewaySystemTests ──────────────────
    internal sealed class GatewayTestTkbDb : ITkbDatabase
    {
        public IEnumerable<TkbTemplate> GetAll()                  => Array.Empty<TkbTemplate>();
        public TkbTemplate GetByName(string name)                 => null!;
        public TkbTemplate GetByType(long tkbType)                => null!;
        public void        Register(TkbTemplate t)                { }
        public bool TryGetByName(string name, out TkbTemplate t)  { t = null!; return false; }
        public bool TryGetByType(long type, out TkbTemplate t)    { t = null!; return false; }
        public string? ActiveTkbName { get; set; }
        public void Clear() { }
        public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath)
            => throw new NotImplementedException();
    }
}
