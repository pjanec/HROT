using System.Linq;
using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CycloneNetworkCleanupSystem"/> verifying the
    /// fan-out descriptor disposal behaviour (BUG1-N002).
    /// </summary>
    public class CycloneNetworkCleanupSystemTests
    {
        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Minimal world with the components the cleanup system queries.</summary>
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        /// <summary>
        /// Publishes a DestructionOrder and swaps the bus buffer so the event is
        /// visible to systems that run next — mirrors the ELM "publish then swap" step.
        /// The entity is NOT destroyed here; the caller should destroy it AFTER the
        /// cleanup system has run (matching the real ELM 1-frame delay guarantee).
        /// </summary>
        private static void PublishDestructionOrder(EntityRepository repo, Entity entity)
        {
            repo.Bus.Publish(new DestructionOrder { Entity = entity });
            repo.Bus.SwapBuffers();
        }

        /// <summary>
        /// Creates an entity that is locally authoritative and adds it to the
        /// cleanup system's tracking set via a first Execute call.
        /// </summary>
        private static Entity CreateAuthoritativeEntity(EntityRepository repo, long netId,
            int primaryOwner = 1, int localNode = 1)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(netId));
            repo.AddComponent(entity, new NetworkAuthority(
                primaryOwnerId: primaryOwner,
                localNodeId:    localNode));
            return entity;
        }

        // â”€â”€ Mock translator â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private sealed class MockTranslator : Fdp.Interfaces.IDescriptorTranslator
        {
            public long DescriptorOrdinal => 0;
            public string TopicName => "mock";
            public long ReceivedSampleCount { get; private set; }
            public long SentSampleCount { get; private set; }
            public TranslatorDirection Direction => TranslatorDirection.Bidirectional;
            public List<long> DisposedIds { get; } = new();
            public bool ThrowOnDispose { get; set; }

            public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
            public void ScanAndPublish(ISimulationView view) { }
            public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

            public void Dispose(long networkEntityId)
            {
                if (ThrowOnDispose) throw new InvalidOperationException("Simulated disposal failure");
                DisposedIds.Add(networkEntityId);
            }
        }

        // â”€â”€ BUG1-N002 Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>⭐ LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — a LIGHTWEIGHT round has no lifecycle: no DestructionOrder ever
        /// announces it. When it is simply destroyed (it burst), the next tick disposes it on every translator — so every viewer drops
        /// its copy — exactly once; a remote (non-authoritative) round is never disposed from here.</summary>
        [Fact]
        public void LR2_ALightweightRound_IsDisposedWhenItIsDestroyed_WithoutAnyDestructionOrder()
        {
            var repo = CreateWorld();
            repo.RegisterComponent<LightweightRound>();
            var t1 = new MockTranslator();
            var t2 = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1, t2 });

            var round = CreateAuthoritativeEntity(repo, netId: 900L);
            repo.AddComponent(round, default(LightweightRound));
            var remote = CreateAuthoritativeEntity(repo, netId: 901L, primaryOwner: 2, localNode: 1);
            repo.AddComponent(remote, default(LightweightRound));
            system.Execute(repo, 0f);
            Assert.Empty(t1.DisposedIds);                      // alive: nothing yet

            repo.DestroyEntity(round);                         // it burst — no ELM, no DestructionOrder
            repo.DestroyEntity(remote);
            system.Execute(repo, 0f);
            Assert.Equal(new[] { 900L }, t1.DisposedIds);
            Assert.Equal(new[] { 900L }, t2.DisposedIds);

            system.Execute(repo, 0f);                          // once only
            Assert.Single(t1.DisposedIds);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-4000</c> F2 / <c>R-4019</c> — a WIPED world (entering Idle; <c>WorldBoundaryReset.Clear</c> destroys with no
        /// DestructionOrder and moves the <c>WorldEpoch</c>) deletes on the wire every entity this node OWNED — once, on every
        /// translator — and never a replica. 🔴 Before: the ids were forgotten and nothing was written, so a durable
        /// <c>EntityMaster</c> kept the dead run alive for every late joiner.
        /// </summary>
        [Fact]
        public void CE4000_AWipedWorld_DeletesEveryOwnedEntity_OnTheWire_OnceAndNeverAReplica()
        {
            var repo = CreateWorld();
            var t1 = new MockTranslator();
            var t2 = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1, t2 });

            CreateAuthoritativeEntity(repo, netId: 1001L);
            CreateAuthoritativeEntity(repo, netId: 1002L);
            CreateAuthoritativeEntity(repo, netId: 1300L, primaryOwner: 2, localNode: 1);   // a replica of node 2's
            system.Execute(repo, 0f);
            Assert.Empty(t1.DisposedIds);

            Hrot.Map.Common.ClusterLoad.WorldBoundaryReset.Clear(repo);   // the wipe: entities gone, epoch moved
            system.Execute(repo, 0f);
            Assert.Equal(new[] { 1001L, 1002L }, t1.DisposedIds.OrderBy(x => x));
            Assert.Equal(new[] { 1001L, 1002L }, t2.DisposedIds.OrderBy(x => x));

            system.Execute(repo, 0f);                                     // once only
            Assert.Equal(2, t1.DisposedIds.Count);
        }

        [Fact]
        public void AllTranslatorsReceiveDispose_WhenEntityDies()
        {
            var repo    = CreateWorld();
            var t1      = new MockTranslator();
            var t2      = new MockTranslator();
            var t3      = new MockTranslator();
            var system  = new CycloneNetworkCleanupSystem(new[] { t1, t2, t3 });

            var entity = CreateAuthoritativeEntity(repo, netId: 100L);

            // First tick: system tracks the entity.
            system.Execute(repo, 0f);

            // Simulate ELM: publish DestructionOrder and swap so it's readable next tick.
            PublishDestructionOrder(repo, entity);

            // Second tick: system reads DestructionOrder (entity still alive) and calls Dispose.
            system.Execute(repo, 0f);

            // ELM hard-deletes the entity after all systems have run.
            repo.DestroyEntity(entity);

            Assert.Contains(100L, t1.DisposedIds);
            Assert.Contains(100L, t2.DisposedIds);
            Assert.Contains(100L, t3.DisposedIds);
        }

        [Fact]
        public void OneFaultyTranslator_DoesNotBlockOthers()
        {
            var repo    = CreateWorld();
            var t1      = new MockTranslator { ThrowOnDispose = true };
            var t2      = new MockTranslator();
            var t3      = new MockTranslator();
            var system  = new CycloneNetworkCleanupSystem(new[] { t1, t2, t3 });

            var entity = CreateAuthoritativeEntity(repo, netId: 200L);

            system.Execute(repo, 0f);
            PublishDestructionOrder(repo, entity);
            system.Execute(repo, 0f);
            repo.DestroyEntity(entity);

            // t1 threw, but t2 and t3 must still have been called
            Assert.Contains(200L, t2.DisposedIds);
            Assert.Contains(200L, t3.DisposedIds);
        }

        [Fact]
        public void NonAuthoritativeEntity_IsNotTracked_AndNeverDisposed()
        {
            var repo   = CreateWorld();
            var t1     = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            // PrimaryOwner=2, Local=1 â†’ not authoritative
            var entity = CreateAuthoritativeEntity(repo, netId: 300L, primaryOwner: 2, localNode: 1);

            system.Execute(repo, 0f);
            PublishDestructionOrder(repo, entity);
            system.Execute(repo, 0f);
            repo.DestroyEntity(entity);

            Assert.Empty(t1.DisposedIds);
        }

        [Fact]
        public void LiveAuthoritativeEntity_IsNotDisposed()
        {
            var repo   = CreateWorld();
            var t1     = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            CreateAuthoritativeEntity(repo, netId: 400L);

            system.Execute(repo, 0f);
            system.Execute(repo, 0f);

            // Entity is still alive â†’ no dispose
            Assert.Empty(t1.DisposedIds);
        }

        [Fact]
        public void MultipleEntities_EachTranslatorDisposedOnce()
        {
            var repo   = CreateWorld();
            var t1     = new MockTranslator();
            var t2     = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1, t2 });

            var e1 = CreateAuthoritativeEntity(repo, netId: 500L);
            var e2 = CreateAuthoritativeEntity(repo, netId: 501L);

            system.Execute(repo, 0f);

            // Publish both destruction orders in the same frame before swapping.
            repo.Bus.Publish(new DestructionOrder { Entity = e1 });
            repo.Bus.Publish(new DestructionOrder { Entity = e2 });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0f);
            repo.DestroyEntity(e1);
            repo.DestroyEntity(e2);

            Assert.Contains(500L, t1.DisposedIds);
            Assert.Contains(501L, t1.DisposedIds);
            Assert.Contains(500L, t2.DisposedIds);
            Assert.Contains(501L, t2.DisposedIds);
            Assert.Equal(2, t1.DisposedIds.Count);
            Assert.Equal(2, t2.DisposedIds.Count);
        }

        // ── CE-3329 PW — §12b W1: the mark-and-sweep by network id (CE-3331) ─────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <c>CE-3331</c> — a world replaced IN PLACE (a replay's backward seek, a checkpoint restore: a keyframe applied, no
        /// DestructionOrder, the epoch NOT ended) retracts on the wire every id it published that the new world lacks — once, on every
        /// translator — and keeps the survivors, even re-created under new handles. 🔴 Before: nothing was disposed, so the entity
        /// created after the target stayed on the wire and TransientLocal handed it to every late joiner.
        /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12b, §12e.
        /// </summary>
        [Fact]
        public void CE3331_AWorldReplacedInPlace_DisposesWhatItLacks_AndKeepsTheSurvivors()
        {
            var repo = CreateWorld();
            var t1 = new MockTranslator();
            var t2 = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1, t2 });

            var early = CreateAuthoritativeEntity(repo, netId: 2001L);   // alive at the seek target
            var late  = CreateAuthoritativeEntity(repo, netId: 2002L);   // created AFTER the target
            system.Execute(repo, 0f);

            // the keyframe of the target: everything replaced, the early entity re-created under a new handle, the late one absent
            repo.DestroyEntity(early);
            repo.DestroyEntity(late);
            CreateAuthoritativeEntity(repo, netId: 2001L);
            system.Execute(repo, 0f);

            Assert.Equal(new[] { 2002L }, t1.DisposedIds);
            Assert.Equal(new[] { 2002L }, t2.DisposedIds);
            Assert.Equal(1, system.SweptDisposes);

            system.Execute(repo, 0f);                                    // once only
            Assert.Single(t1.DisposedIds);
        }

        /// <summary>⭐ §12b — an id handed over to another node (authority flipped in place, no destroy) is FORGOTTEN, never disposed: its
        /// new owner publishes it. Nor is it disposed when it later leaves this node's world.</summary>
        [Fact]
        public void AHandOver_IsForgotten_NeverDisposed()
        {
            var repo = CreateWorld();
            var t1 = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            var unit = CreateAuthoritativeEntity(repo, netId: 3001L);
            system.Execute(repo, 0f);

            repo.SetComponent(unit, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));   // node 2 owns it now
            system.Execute(repo, 0f);
            Assert.Empty(t1.DisposedIds);
            Assert.Equal(1, system.ForgottenHandedOver);

            repo.DestroyEntity(unit);                                   // its owner deleted it; our ghost goes
            system.Execute(repo, 0f);
            Assert.Empty(t1.DisposedIds);
        }

        /// <summary>⭐ §12b — an entity retracted by its DestructionOrder lingers while it tears down (awaiting acks); a tearing-down
        /// entity counts as absent, so it is disposed ONCE, never again when it finally leaves. A seek that brings the id back alive
        /// tracks it again.</summary>
        [Fact]
        public void ATornDownEntity_IsDisposedOnce_AndASeekThatBringsItBack_TracksItAgain()
        {
            var repo = CreateWorld();
            var t1 = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            var unit = CreateAuthoritativeEntity(repo, netId: 4001L);
            system.Execute(repo, 0f);
            repo.SetLifecycleState(unit, EntityLifecycle.TearDown);       // as NetworkSpawningSystem does, before the order
            PublishDestructionOrder(repo, unit);
            system.Execute(repo, 0f);                                   // the DestructionOrder: disposed
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0f);                                   // still tearing down
            repo.DestroyEntity(unit);
            system.Execute(repo, 0f);                                   // gone: no second dispose
            Assert.Equal(new[] { 4001L }, t1.DisposedIds);

            var back = CreateAuthoritativeEntity(repo, netId: 4001L);   // a backward seek re-creates it
            system.Execute(repo, 0f);
            repo.DestroyEntity(back);                                   // a forward seek past its death
            system.Execute(repo, 0f);
            Assert.Equal(new[] { 4001L, 4001L }, t1.DisposedIds);
        }
    }
}
