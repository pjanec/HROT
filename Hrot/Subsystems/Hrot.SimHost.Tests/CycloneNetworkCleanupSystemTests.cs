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
            public long DescriptorOrdinal { get; init; } = 0;
            public string TopicName => "mock";
            public long ReceivedSampleCount { get; private set; }
            public long SentSampleCount { get; private set; }
            public TranslatorDirection Direction => TranslatorDirection.Bidirectional;
            public List<long> DisposedIds { get; } = new();
            public bool ThrowOnDispose { get; set; }
            // ⭐ CE-4079 ②
            public List<(long Id, long Instance)> Released { get; } = new();
            public bool ThrowOnRelease { get; set; }

            public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
            public void ScanAndPublish(ISimulationView view) { }
            public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

            public void Dispose(long networkEntityId)
            {
                if (ThrowOnDispose) throw new InvalidOperationException("Simulated disposal failure");
                DisposedIds.Add(networkEntityId);
            }

            public void ReleaseInstance(long networkEntityId, long instanceId)
            {
                if (ThrowOnRelease) throw new InvalidOperationException("Simulated release failure");
                Released.Add((networkEntityId, instanceId));
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

            // the keyframe of the target: the world cleared in place (as PlaybackSystem.ApplyFrame does for a keyframe), the early
            //   entity re-created, the late one absent
            repo.SoftClear();
            CreateAuthoritativeEntity(repo, netId: 2001L);
            system.Execute(repo, 0f);

            Assert.Equal(new[] { 2002L }, t1.DisposedIds);
            Assert.Equal(new[] { 2002L }, t2.DisposedIds);
            Assert.Equal(1, system.SweptDisposes);

            system.Execute(repo, 0f);                                    // once only
            Assert.Single(t1.DisposedIds);
        }

        /// <summary>
        /// ⭐⭐ §12b — the sweep is CHANGE-DRIVEN: it runs once per in-place world replacement (<c>EntityRepository.ClearGeneration</c>),
        /// never on an ordinary frame; an ordinary frame looks only at the entities whose networking components changed — a new owned
        /// entity spawned mid-run is still tracked and retracted by its DestructionOrder. 🔴 First build: a full sweep over every
        /// networked entity, every frame.
        /// </summary>
        [Fact]
        public void TheSweep_RunsOncePerWorldReplacement_NeverPerFrame()
        {
            var repo = CreateWorld();
            var t1 = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            for (int i = 0; i < 50; i++) CreateAuthoritativeEntity(repo, netId: 5000L + i);
            system.Execute(repo, 0f);                                    // the first run: one sweep learns the world
            int afterFirst = system.Sweeps;
            for (int frame = 0; frame < 20; frame++) { repo.Tick(); system.Execute(repo, 0f); }
            Assert.Equal(afterFirst, system.Sweeps);                     // ordinary frames: no sweep

            repo.Tick();
            var spawned = CreateAuthoritativeEntity(repo, netId: 5999L);  // spawned mid-run: found by the delta
            system.Execute(repo, 0f);
            repo.Tick();
            PublishDestructionOrder(repo, spawned);
            system.Execute(repo, 0f);
            Assert.Equal(new[] { 5999L }, t1.DisposedIds);
            Assert.Equal(afterFirst, system.Sweeps);

            repo.SoftClear();                                            // a world replaced in place: exactly one sweep
            system.Execute(repo, 0f);
            system.Execute(repo, 0f);
            Assert.Equal(afterFirst + 1, system.Sweeps);
            Assert.Equal(51, t1.DisposedIds.Count);                      // the 50 the replacement lacks, retracted
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

        /// <summary>
        /// ⭐⭐ <c>CE-4079</c> ② — the creator's grant was confirmed (<c>DescriptorHandedOver</c>): the translators of THAT descriptor let go of
        /// the instance — once, with the entity's id and the descriptor's instance — and nothing is disposed (a dispose would delete the
        /// descriptor the grantee now owns). A translator that fails to release does not stop the others.
        /// </summary>
        [Fact]
        public void CE4079_AHandedOverDescriptor_IsReleasedOnThatDescriptorsTranslatorsOnly_AndNeverDisposed()
        {
            var repo = CreateWorld();
            repo.RegisterEvent<Fdp.Toolkit.Replication.Messages.DescriptorHandedOver>();
            var egress = new MockTranslator { DescriptorOrdinal = 2 };
            var ingressOfTheSame = new MockTranslator { DescriptorOrdinal = 2 };
            var broken = new MockTranslator { DescriptorOrdinal = 2, ThrowOnRelease = true };
            var other = new MockTranslator { DescriptorOrdinal = 3 };
            var system = new CycloneNetworkCleanupSystem(new[] { broken, egress, ingressOfTheSame, other });

            repo.Bus.Publish(new Fdp.Toolkit.Replication.Messages.DescriptorHandedOver
            {
                NetworkId = new NetworkIdentity(77L),
                PackedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(2, 0),
            });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0f);

            Assert.Equal(new[] { (77L, 0L) }, egress.Released);
            Assert.Equal(new[] { (77L, 0L) }, ingressOfTheSame.Released);   // the translator filter is the ordinal; each translator decides if it ever wrote
            Assert.Empty(other.Released);                                   // another descriptor's translator is left alone
            Assert.Empty(egress.DisposedIds);                               // ⭐ a release is not a delete
            Assert.Equal(2, system.ReleasedHandedOver);                     // the broken one is not counted and did not stop the rest

            repo.Bus.SwapBuffers();                                         // the next frame: the event is gone, nothing is released again
            system.Execute(repo, 0f);
            Assert.Single(egress.Released);
        }

        // ── CE-4079 ④ — the cleanup is where a node's retraction disposes are written, so it is where their acknowledgements are awaited ──

        /// <summary>A translator that holds a REAL writer in a private field, as every production translator does.</summary>
        private sealed class WriterHoldingTranslator : Fdp.Interfaces.IDescriptorTranslator
        {
            private readonly CycloneDDS.Runtime.DdsWriter<Hrot.NED.Descriptors.EntityLean> _writer;
            public WriterHoldingTranslator(CycloneDDS.Runtime.DdsParticipant p) => _writer = new(p);
            public void Retract(long id) => _writer.DisposeInstance(new Hrot.NED.Descriptors.EntityLean { EntityId = id });
            public void Send(long id) => _writer.Write(new Hrot.NED.Descriptors.EntityLean { EntityId = id, Side = 1 });
            public long DescriptorOrdinal => 0;
            public string TopicName => "writer-holder";
            public long ReceivedSampleCount => 0;
            public long SentSampleCount => 0;
            public TranslatorDirection Direction => TranslatorDirection.Egress;
            public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
            public void ScanAndPublish(ISimulationView view) { }
            public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
            public void Dispose(long networkEntityId) { }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4079</c> ④ (DESIGN_Replay_Time_Axis_And_Review §12c W3) — over REAL DDS: the writer a translator holds is found without anyone
        /// listing it, and once a matched reliable reader has what the writer sent — a dispose included — <c>WaitForAcks</c> reports everything
        /// acknowledged. 📐 It is the library's <c>dds_wait_for_acks</c> that answers; this proves the system reaches the right writers.
        /// </summary>
        [Fact(Timeout = 30_000)]
        [Trait("Category", "Integration")]
        public void CE4079_TheWritersATranslatorHolds_AreFound_AndTheirDisposesAreAcknowledged()
        {
            const uint domain = 232u;
            using var node = new CycloneDDS.Runtime.DdsParticipant(domain);
            using var reader = new CycloneDDS.Runtime.DdsReader<Hrot.NED.Descriptors.EntityLean>(node);
            var translator = new WriterHoldingTranslator(node);
            var system = new CycloneNetworkCleanupSystem(new Fdp.Interfaces.IDescriptorTranslator[] { translator });
            Assert.Equal(1, system.AckWriterCount);        // found on the translator, not registered by hand

            // The reader matches (a sample sent before discovery is lost), then the node retracts what it sent.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            bool got = false;
            while (!got && DateTime.UtcNow < deadline)
            {
                translator.Send(900);
                System.Threading.Thread.Sleep(50);
                using var loan = reader.Take();
                foreach (var _ in loan) got = true;
            }
            Assert.True(got, "the reader never matched");
            translator.Retract(900);

            var report = system.WaitForAcks(TimeSpan.FromSeconds(5));

            Assert.Equal(1, report.Writers);
            Assert.True(report.Complete, $"the dispose was not acknowledged: {report}");
        }

        /// <summary>⭐ <c>CE-4079</c> ④ — a writer whose reader never acknowledges: the wait ends within its budget, the shortfall is COUNTED (not thrown), and the writers after it are still asked.</summary>
        [Fact]
        public void CE4079_AWriterThatIsNeverAcknowledged_TimesOutWithinTheBudget_AndTheOthersAreStillAsked()
        {
            var system = new CycloneNetworkCleanupSystem(Array.Empty<Fdp.Interfaces.IDescriptorTranslator>());
            int askedAfter = 0;
            system.RegisterAckWaiter("never", t => { System.Threading.Thread.Sleep(t); return false; });   // a reader that never acknowledges
            system.RegisterAckWaiter("after", t => { askedAfter++; return true; });

            var report = system.WaitForAcks(TimeSpan.FromMilliseconds(200));

            Assert.Equal(2, report.Writers);
            Assert.Equal(1, report.TimedOut);
            Assert.False(report.Complete);
            Assert.Equal(1, askedAfter);
            Assert.InRange(report.WaitedMilliseconds, 150, 3000);
        }

        /// <summary>
        /// ⭐ <c>CE-4079</c> ④ — the sweep marker the controller waits on: <c>SweptClearGeneration</c> follows the world's <c>ClearGeneration</c> only once
        /// the cleanup's sweep of the swap has RUN, and the system publishes itself for the world on its first frame (no host wiring).
        /// </summary>
        [Fact]
        public void CE4079_TheSweepMarker_FollowsAWorldSwap_OnlyAfterTheSweepRan()
        {
            var repo = CreateWorld();
            var system = new CycloneNetworkCleanupSystem(new[] { new MockTranslator() });
            Assert.Null(Fdp.Core.Orchestration.RetractionAckSources.Of(repo));

            system.Execute(repo, 0f);
            Assert.Same(system, Fdp.Core.Orchestration.RetractionAckSources.Of(repo));      // published by itself
            Assert.Equal(repo.ClearGeneration, system.SweptClearGeneration);

            repo.SoftClear();                                                                // a world swap (a seek's keyframe)
            Assert.NotEqual(repo.ClearGeneration, system.SweptClearGeneration);              // its sweep has not run yet

            system.Execute(repo, 0f);                                                        // the next frame
            Assert.Equal(repo.ClearGeneration, system.SweptClearGeneration);
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

            repo.SoftClear();                                           // a backward seek re-creates it
            CreateAuthoritativeEntity(repo, netId: 4001L);
            system.Execute(repo, 0f);
            repo.SoftClear();                                           // a forward seek past its death
            system.Execute(repo, 0f);
            Assert.Equal(new[] { 4001L, 4001L }, t1.DisposedIds);
        }
    }
}
