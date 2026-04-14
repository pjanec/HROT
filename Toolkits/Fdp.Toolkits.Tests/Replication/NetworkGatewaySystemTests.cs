using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.ModuleHost_Core.Abstractions;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.ModuleHost_Core.Network;
using Fdp.ModuleHost_Core.Network.Interfaces;
using INetworkTopology = Fdp.Interfaces.INetworkTopology;

namespace FDP.Toolkit.Replication.Tests
{
    /// <summary>
    /// Unit tests for the canonical <see cref="NetworkGatewaySystem"/> (PACK3-N001).
    ///
    /// <para>Tests use a minimal <see cref="EntityRepository"/> cast to
    /// <see cref="ISimulationView"/> to drive the system and verify
    /// <see cref="ConstructionAck"/> event publication.</para>
    /// </summary>
    public class NetworkGatewaySystemTests
    {
        // ── Mock network topologies ───────────────────────────────────────────

        /// <summary>Reports zero expected peers → immediate ACK.</summary>
        private sealed class EmptyTopology : INetworkTopology
        {
            public int              LocalNodeId     => 1;
            public IEnumerable<int> GetExpectedPeers(long tkbType) => Array.Empty<int>();
            public IEnumerable<int> GetAllNodes()                  => Array.Empty<int>();
        }

        /// <summary>Reports one expected peer (nodeId=2) for every TKB type.</summary>
        private sealed class SinglePeerTopology : INetworkTopology
        {
            public int              LocalNodeId     => 1;
            public IEnumerable<int> GetExpectedPeers(long tkbType) => new[] { 2 };
            public IEnumerable<int> GetAllNodes()                  => new[] { 1, 2 };
        }

        // ── Setup helper ──────────────────────────────────────────────────────

        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<ConstructionAck>();
            repo.RegisterEvent<DestructionOrder>();
            repo.RegisterEvent<DestructionAck>();
            return repo;
        }

        /// <summary>
        /// Runs a single Execute tick: plays back the command buffer and swaps bus buffers
        /// so that output events are visible to <see cref="ISimulationView.ConsumeEvents{T}"/>.
        /// </summary>
        private static void RunTick(EntityRepository repo, NetworkGatewaySystem gateway, float dt = 0f)
        {
            gateway.Execute(repo, dt);
            var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            cb.Playback(repo);
            repo.Bus.SwapBuffers();
        }

        // ── Test 1: immediate ACK when entity has no PendingNetworkAck ────────

        [Fact]
        public void Execute_AcksImmediately_WhenNoPendingNetworkAck()
        {
            using var repo  = CreateRepo();
            var elm     = new EntityLifecycleModule(new GatewayTestTkbDb(), Array.Empty<int>());
            var gateway = new NetworkGatewaySystem(101, 1, new EmptyTopology(), elm);

            var entity = repo.CreateEntity();

            // Publish ConstructionOrder so the system can consume it this tick.
            repo.Bus.Publish(new ConstructionOrder
            {
                Entity = entity, BlueprintId = 0, FrameNumber = 0, InitiatorModuleId = 0,
            });
            repo.Bus.SwapBuffers();

            RunTick(repo, gateway);

            bool found = false;
            foreach (var ack in ((ISimulationView)repo).ConsumeEvents<ConstructionAck>())
                if (ack.Entity == entity && ack.Success) { found = true; break; }
            Assert.True(found, "Expected immediate ConstructionAck for entity with no PendingNetworkAck");
        }

        // ── Test 2: immediate ACK when PendingNetworkAck exists but no peers ──

        [Fact]
        public void Execute_AcksImmediately_WhenPendingAckButNoPeers()
        {
            using var repo  = CreateRepo();
            var elm     = new EntityLifecycleModule(new GatewayTestTkbDb(), Array.Empty<int>());
            var gateway = new NetworkGatewaySystem(101, 1, new EmptyTopology(), elm);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });

            repo.Bus.Publish(new ConstructionOrder
            {
                Entity = entity, BlueprintId = 0, FrameNumber = 0, InitiatorModuleId = 0,
            });
            repo.Bus.SwapBuffers();

            RunTick(repo, gateway);

            bool found = false;
            foreach (var ack in ((ISimulationView)repo).ConsumeEvents<ConstructionAck>())
                if (ack.Entity == entity && ack.Success) { found = true; break; }
            Assert.True(found, "Expected immediate ConstructionAck when topology has no peers");
        }

        // ── Test 3: ACK deferred until all peers acknowledge ──────────────────

        [Fact]
        public void ReceiveLifecycleStatus_AcksConstruction_WhenAllPeersRespond()
        {
            using var repo  = CreateRepo();
            var elm     = new EntityLifecycleModule(new GatewayTestTkbDb(), Array.Empty<int>());
            var gateway = new NetworkGatewaySystem(101, 1, new SinglePeerTopology(), elm);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });

            repo.Bus.Publish(new ConstructionOrder
            {
                Entity = entity, BlueprintId = 0, FrameNumber = 0, InitiatorModuleId = 0,
            });
            repo.Bus.SwapBuffers();

            // First Execute: entity enters pending state — no ACK yet.
            RunTick(repo, gateway);

            var acksAfterFirstExec = ((ISimulationView)repo).ConsumeEvents<ConstructionAck>();
            Assert.Equal(0, acksAfterFirstExec.Length);

            // Peer nodeId=2 reports Active — gateway must now publish ACK.
            var cmd2 = ((ISimulationView)repo).GetCommandBuffer();
            gateway.ReceiveLifecycleStatus(entity, 2, EntityLifecycle.Active, cmd2, 1);
            var cb2 = (EntityCommandBuffer)cmd2;
            cb2.Playback(repo);
            repo.Bus.SwapBuffers();

            bool found = false;
            foreach (var ack in ((ISimulationView)repo).ConsumeEvents<ConstructionAck>())
                if (ack.Entity == entity && ack.Success) { found = true; break; }
            Assert.True(found, "Expected ConstructionAck after all peers respond");
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
    }
}
