using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using CycloneDDS.Runtime;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Fdp.Network.Cyclone.Tests.Translators
{
    /// <summary>A durable (TransientLocal) test topic keyed by entity id — what a late joiner reads.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [DdsTopic]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct PublishedInstancesTestTopic
    {
        [DdsKey, DdsId(0)] public int EntityId;
        [DdsId(1)] public int Value;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4007</c> / <c>CE-4000</c> residual (docs/DESIGN_Cluster_Load_Phase.md §8.3) — the one record of what an egress writer published
    /// deletes it on the wire: a reader that joins AFTER a destruction or a wipe — a late Player — never receives the dead entity alive; a
    /// hand-over only lets go. Real DDS, private domain 177. 🔴 Red-proof: make <see cref="PublishedInstances{T}.Dispose"/> only forget (the
    /// old <c>_last.Remove</c>) ⇒ ① sees 1003 Alive.
    /// </summary>
    public sealed class PublishedInstancesTests
    {
        private const uint Domain = 177;

        private static PublishedInstances<PublishedInstancesTestTopic> Instances(DdsWriter<PublishedInstancesTestTopic> w)
            => new(w, static key => new PublishedInstancesTestTopic { EntityId = (int)key });

        /// <summary>What a reader that joins NOW receives for each entity id: its last instance state.</summary>
        private static Dictionary<int, DdsInstanceState> LateJoiner()
        {
            using var p = new DdsParticipant(Domain);
            using var r = new DdsReader<PublishedInstancesTestTopic>(p);
            var seen = new Dictionary<int, DdsInstanceState>();
            var until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until)
            {
                using (var loan = r.Take())
                    foreach (var s in loan)
                    {
                        int id = s.IsValid ? s.Data.EntityId : DdsTypeSupport.FromNative<PublishedInstancesTestTopic>(s.NativePtr).EntityId;
                        seen[id] = s.Info.InstanceState;
                    }
                if (seen.Count > 0 && DateTime.UtcNow > until.AddSeconds(-2)) break;
                Thread.Sleep(50);
            }
            return seen;
        }

        private static bool AliveFor(Dictionary<int, DdsInstanceState> seen, int id) => seen.TryGetValue(id, out var s) && s == DdsInstanceState.Alive;

        [Fact]
        public void CE4007_AnEntityDestroyedInGame_IsNotHandedAliveToALateJoiner_AndAWipeDeletesTheRest()
        {
            using var p = new DdsParticipant(Domain);
            using var w = new DdsWriter<PublishedInstancesTestTopic>(p);
            var instances = Instances(w);
            instances.Write(1002, new PublishedInstancesTestTopic { EntityId = 1002, Value = 1 });
            instances.Write(1003, new PublishedInstancesTestTopic { EntityId = 1003, Value = 1 });

            instances.Dispose(1003);                                     // ① the entity is destroyed in game
            var afterDestruction = LateJoiner();
            Assert.False(AliveFor(afterDestruction, 1003), "a late joiner received the destroyed entity 1003 alive");
            Assert.True(AliveFor(afterDestruction, 1002), "the living entity 1002 must still reach a late joiner");

            var world = new EntityRepository();                          // ② the world is wiped (a reload): everything this writer published goes
            instances.OnFrame(world);
            WorldEpoch.Advance(world);
            Assert.True(instances.OnFrame(world));
            Assert.Equal(0, instances.Count);
            Assert.False(AliveFor(LateJoiner(), 1002), "after the wipe a late joiner still received 1002 alive");
        }

        [Fact]
        public void CE4007_AHandOver_LetsGoWithoutDeleting_AndAWriterDeletesOnlyWhatItWrote()
        {
            using var p = new DdsParticipant(Domain);
            using var w = new DdsWriter<PublishedInstancesTestTopic>(p);
            var instances = Instances(w);
            instances.Write(2001, new PublishedInstancesTestTopic { EntityId = 2001, Value = 5 });
            instances.Release(2001);                                     // F7: the grantee writes it now
            Assert.Equal(0, instances.Count);
            Assert.NotEqual(DdsInstanceState.NotAliveDisposed, LateJoiner().GetValueOrDefault(2001, DdsInstanceState.Alive));

            instances.Dispose(4242);                                     // never written here ⇒ nothing goes out, nothing throws
            Assert.Equal(0, instances.Count);
        }

        [Fact]
        public void CE4007_ACompositeKey_DisposesEveryInstanceOfTheEntity()
        {
            var instances = new PublishedInstances<PublishedInstancesTestTopic>(null,
                static key => new PublishedInstancesTestTopic { EntityId = (int)(key >> 32) }, static key => (int)(key >> 32));
            long K(int parent, int child) => ((long)parent << 32) | (uint)child;
            instances.Write(K(7, 0), default); instances.Write(K(7, 1), default); instances.Write(K(8, 0), default);
            instances.Dispose(7);
            Assert.Equal(new[] { K(8, 0) }, instances.Keys);
        }

        /// <summary>
        /// ⭐ A writer that is NOT the entity's owner (its lifecycle status of a replica, an EQS result for another node's unit) is never
        /// handed <c>Dispose</c> by the cleanup system — it deletes what it wrote when ITS world raises the entity's <c>DestructionOrder</c>.
        /// 📐 Measured live 2026-11-05: 1003's lifecycle status and EQS result stayed alive for late joiners. 🔴 Red-proof: drop the
        /// DestructionOrder pass in <c>OnFrame</c> ⇒ the count stays 1.
        /// </summary>
        [Fact]
        public void CE4007_AWriterThatDoesNotOwnTheEntity_DeletesWhatItWrote_WhenItsWorldDestroysIt()
        {
            var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            world.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            var replica = world.CreateEntity();
            world.AddComponent(replica, new Fdp.Toolkit.Replication.Components.NetworkIdentity(1003));
            var instances = new PublishedInstances<PublishedInstancesTestTopic>(null, static key => new PublishedInstancesTestTopic { EntityId = (int)key });
            instances.OnFrame(world);
            instances.Write(1003, new PublishedInstancesTestTopic { EntityId = 1003, Value = 1 });
            instances.Write(1004, new PublishedInstancesTestTopic { EntityId = 1004, Value = 1 });

            world.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.DestructionOrder { Entity = replica });
            world.Bus.SwapBuffers();
            instances.OnFrame(world);
            Assert.Equal(new[] { 1004L }, instances.Keys);
        }

        /// <summary>⭐ R-2084 — the send-on-change test and an unchanged write allocate nothing on the steady frame.</summary>
        [Fact]
        public void CE4007_TheSteadyFrame_AllocatesNothing()
        {
            var instances = new PublishedInstances<PublishedInstancesTestTopic>(null, static key => new PublishedInstancesTestTopic { EntityId = (int)key });
            var world = new EntityRepository();
            var sample = new PublishedInstancesTestTopic { EntityId = 1, Value = 3 };
            for (int i = 0; i < 8; i++) instances.Write(i, sample);
            instances.OnFrame(world);
            for (int i = 0; i < 8; i++) instances.Unchanged(i, sample);   // first calls JIT / initialise statics — not the steady frame
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 100; frame++)
            {
                instances.OnFrame(world);
                for (int i = 0; i < 8; i++) if (!instances.Unchanged(i, sample)) instances.Write(i, sample);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
