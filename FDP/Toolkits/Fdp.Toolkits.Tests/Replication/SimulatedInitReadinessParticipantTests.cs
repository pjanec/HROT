using System;
using Xunit;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// CE-291 (piece C, C5) — rails for <see cref="SimulatedInitReadinessParticipant"/>, the peer-side
    /// FAKE local-init participant that holds a reliable ghost in <c>Constructing</c> for a simulated
    /// navmesh/model-load window (docs/DESIGN_Cross_Node_Construction_Barrier.md §3b/§3c).
    /// </summary>
    public class SimulatedInitReadinessParticipantTests
    {
        private const int ModuleId = 303;

        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<ReportLifecycleOnActive>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<ConstructionAck>();
            repo.RegisterEvent<DestructionOrder>();
            repo.RegisterEvent<DestructionAck>();
            return repo;
        }

        private static EntityLifecycleModule NewElm() => new EntityLifecycleModule(new GatewayTestTkbDb(), Array.Empty<int>());

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

        // ── A non-reliable ghost (no ReportLifecycleOnActive tag) is acked immediately ──

        [Fact]
        public void AcksImmediately_WhenNotReliableGhost()
        {
            using var repo = CreateRepo();
            var participant = new SimulatedInitReadinessParticipant(ModuleId, NewElm(), simulatedReadyFrames: 10);
            var entity = repo.CreateEntity();
            // no ReportLifecycleOnActive → fast mode / creator-local → no fake wait

            PublishOrder(repo, entity);
            RunTick(repo, participant);

            Assert.True(SawAck(repo, entity), "a non-reliable ghost must not be delayed by the fake init");
        }

        // ── A reliable ghost is HELD for the simulated window, then acked ──

        [Fact]
        public void Defers_ThenAcks_AfterSimulatedWindowElapses()
        {
            using var repo = CreateRepo();
            var participant = new SimulatedInitReadinessParticipant(ModuleId, NewElm(), simulatedReadyFrames: 5);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new ReportLifecycleOnActive());

            PublishOrder(repo, entity);
            RunTick(repo, participant);
            // RED-PROOF: still Constructing — the window has not elapsed.
            Assert.False(SawAck(repo, entity), "must hold the reliable ghost while the simulated init window is open");

            repo.ResetGlobalVersion(500);   // well past the 5-frame window (defer frame is a few ticks in)
            RunTick(repo, participant);
            Assert.True(SawAck(repo, entity), "acks once the simulated init window has elapsed");
        }

        // ── §3b.3: a reliable ghost is NEVER self-force-activated on the base timeout ──

        [Fact]
        public void OnTimeout_KeepsWaiting_NeverSelfActivates()
        {
            using var repo = CreateRepo();
            // Window LONGER than the base DEFAULT_TIMEOUT_FRAMES (300), so the base timeout fires first.
            var participant = new SimulatedInitReadinessParticipant(ModuleId, NewElm(), simulatedReadyFrames: 1000);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new ReportLifecycleOnActive());

            PublishOrder(repo, entity);
            RunTick(repo, participant);

            // Advance PAST the base 300-frame timeout but BEFORE the 1000-frame window.
            repo.ResetGlobalVersion(400);
            RunTick(repo, participant);

            Assert.False(SawAck(repo, entity),
                "a reliable ghost must NOT self-force-activate on the base timeout — it waits for the creator's disposal (§3b.3)");
        }

        // ── A ghost torn down before completing (creator abort) clears state and acks its destruction ──

        [Fact]
        public void Destruction_ClearsPendingState_AndAcksDestruction()
        {
            using var repo = CreateRepo();
            var participant = new SimulatedInitReadinessParticipant(ModuleId, NewElm(), simulatedReadyFrames: 1000);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new ReportLifecycleOnActive());

            PublishOrder(repo, entity);
            RunTick(repo, participant);
            Assert.False(SawAck(repo, entity), "deferred, still holding");

            // Creator abort → DestructionOrder reaches the peer's ELM path.
            repo.Bus.Publish(new DestructionOrder { Entity = entity, FrameNumber = 1, Reason = new FixedString64("reliable-init-timeout") });
            repo.Bus.SwapBuffers();
            RunTick(repo, participant);

            bool sawDestructionAck = false;
            foreach (var ack in ((ISimulationView)repo).ReadEvents<DestructionAck>())
                if (ack.Entity == entity && ack.Success) sawDestructionAck = true;
            Assert.True(sawDestructionAck, "the base must ack destruction so teardown completes");
        }
    }
}
