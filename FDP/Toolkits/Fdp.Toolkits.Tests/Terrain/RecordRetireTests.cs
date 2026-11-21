using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-2468 UF-1 (docs/designs/minefields/DESIGN_Minefields.md §9b.1 K2 — "nothing lasts forever") — <c>RecordWriterCore.Retire(key)</c>: it ends this
    /// node's OWNED records of a key (a networked one through <see cref="DestroyEntityCommand"/> + TearDown, an offline one at once), never a ghost; a creation
    /// still in flight is ended as it arrives and its queued items are forgotten; a keyframe before and after the retire seeks back and forth with the record
    /// there and gone (the recorder sees an ordinary destroy).
    /// </summary>
    public sealed class RecordRetireTests
    {
        [ComponentId(510)]
        private struct Note : IKeyedRecord<int, NoteItem>
        {
            public int Key, Value;
            int IKeyedRecord<int, NoteItem>.RecordKey { readonly get => Key; set => Key = value; }
            public readonly bool IsFull => false;
            public readonly bool Holds(in NoteItem item) => Value >= item.Value;
            public bool TryAdd(in NoteItem item) { if (item.Value > Value) Value = item.Value; return true; }
        }

        private readonly record struct NoteItem(int Key, int Value);

        /// <summary>A writer whose items come from <see cref="Items"/> and whose retires come from <see cref="Retiring"/> (both drained each tick).</summary>
        private sealed class Writer : RecordWriterCore<Note, int, NoteItem>
        {
            public readonly List<NoteItem> Items = new();
            public readonly List<int> Retiring = new();
            public int LastEnded;
            protected override long DescriptorKey => 0;
            protected override long TkbType => 9999;
            protected override int KeyOf(in NoteItem item) => item.Key;
            protected override Note NewRecord(in int key, int existing) => new() { Key = key };
            protected override void ReadItems(EntityRepository repo, List<NoteItem> into) { into.AddRange(Items); Items.Clear(); }
            protected override void BeforeItems(EntityRepository repo, List<NoteItem> into)
            {
                foreach (var k in Retiring) LastEnded = Retire(repo, k);
                Retiring.Clear();
            }
        }

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Writer Writer = new();
            private int _nextId = 100;
            public readonly bool Networked;
            public Rig(bool networked)
            {
                Networked = networked;
                W.RegisterComponent<Note>(); W.RegisterComponent<SimTransform>(); W.RegisterComponent<NetworkAuthority>(); W.RegisterComponent<NetworkIdentity>();
                if (networked) W.RegisterManagedEvent<DestroyEntityCommand>();
            }
            /// <summary>A tick of the writer; its creation requests become entities (with a network id when networked) unless <paramref name="hold"/>.</summary>
            public void Tick(bool hold = false)
            {
                W.Bus.SwapBuffers();
                Writer.Execute(W, 0.016f);
                W.Bus.SwapBuffers();
                if (hold) return;
                Materialise();
            }
            public List<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest> Requests() => W.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>().ToList();
            public void Materialise() => Materialise(Requests());
            public void Materialise(List<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest> requests)
            {
                foreach (var r in requests)
                {
                    var e = W.CreateEntity();
                    foreach (var c in r.InitialComponents!) if (c is Note n) W.AddComponent(e, n);
                    if (Networked) W.AddComponent(e, new NetworkIdentity(_nextId++));
                }
            }
            public List<Entity> Live() { var l = new List<Entity>(); foreach (var e in W.Query().With<Note>().WithLifecycle(EntityLifecycle.AllButTearDown).Build()) l.Add(e); return l; }
            public void Dispose() => W.Dispose();
        }

        /// <summary>🔴 Red-proof: <c>Retire</c> returning without ending anything ⇒ the record stays live and no destroy is asked.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UF1_ARetire_EndsTheOwnedRecord_ThroughTheNetworkWhenItHasAnId(bool networked)
        {
            using var rig = new Rig(networked);
            rig.Writer.Items.Add(new NoteItem(7, 3)); rig.Tick();
            rig.Writer.Items.Add(new NoteItem(8, 1)); rig.Tick();
            Assert.Equal(2, rig.Live().Count);

            rig.Writer.Retiring.Add(7); rig.Tick();
            Assert.Equal(1, rig.Writer.LastEnded);
            var left = Assert.Single(rig.Live());
            Assert.Equal(8, rig.W.GetComponentRO<Note>(left).Key);   // the other key is untouched
            if (networked)
            {
                var asked = Assert.Single(rig.W.Bus.ReadManaged<DestroyEntityCommand>());
                Assert.Equal(100, asked.NetworkId);   // the record of key 7 (the first made)
            }
        }

        /// <summary>⭐ a ghost (another node's record) is never ended here — its owner retires it. 🔴 Red-proof: drop the <c>Owns</c> test ⇒ the ghost is torn down.</summary>
        [Fact]
        public void UF1_AGhost_IsNeverRetiredByANodeThatDoesNotOwnIt()
        {
            using var rig = new Rig(networked: true);
            var ghost = rig.W.CreateEntity();
            rig.W.AddComponent(ghost, new Note { Key = 7, Value = 5 });
            rig.W.AddComponent(ghost, new NetworkIdentity(55));
            rig.W.SetLifecycleState(ghost, EntityLifecycle.Ghost);

            rig.Writer.Retiring.Add(7); rig.Tick();
            Assert.Equal(0, rig.Writer.LastEnded);
            Assert.Equal(EntityLifecycle.Ghost, rig.W.GetLifecycleState(ghost));
            Assert.Empty(rig.W.Bus.ReadManaged<DestroyEntityCommand>());
        }

        /// <summary>⭐ retired while its creation was in flight: the queued item is forgotten and the entity is ended as it arrives.
        /// 🔴 Red-proof: drain a retired pending key like any other ⇒ the late entity stays live with the queued item applied.</summary>
        [Fact]
        public void UF1_APendingCreation_IsEndedAsItArrives_AndItsQueuedItemsAreForgotten()
        {
            using var rig = new Rig(networked: false);
            rig.Writer.Items.Add(new NoteItem(7, 3)); rig.Tick(hold: true);    // the creation is asked, not yet answered
            var asked = rig.Requests();
            Assert.Single(asked);
            rig.Writer.Items.Add(new NoteItem(7, 9)); rig.Tick(hold: true);    // queued behind it
            rig.Writer.Retiring.Add(7); rig.Tick(hold: true);
            rig.Materialise(asked);                                            // the entity arrives
            Assert.Single(rig.Live());
            rig.Tick();                                                        // the next drain sees it — and ends it
            Assert.Empty(rig.Live());
        }

        /// <summary>⭐ the recorder sees an ordinary destroy: a keyframe with the record and one after its retire seek back and forth (an AAR seek).
        /// 🔴 Red-proof: retire leaving the entity alive ⇒ the second keyframe still holds the record.</summary>
        [Fact]
        public void UF1_KeyframesBeforeAndAfterARetire_SeekBackAndForth()
        {
            byte[] Keyframe(EntityRepository src, long tick)
            {
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) new Fdp.Core.FlightRecorder.RecorderSystem().RecordKeyframe(src, bw, tick);
                return ms.ToArray();
            }
            using var rig = new Rig(networked: false);
            rig.Writer.Items.Add(new NoteItem(7, 3)); rig.Tick();
            var before = Keyframe(rig.W, 10);
            rig.Writer.Retiring.Add(7); rig.Tick();
            var after = Keyframe(rig.W, 20);

            using var live = new EntityRepository();
            live.RegisterComponent<Note>(); live.RegisterComponent<SimTransform>(); live.RegisterComponent<NetworkAuthority>(); live.RegisterComponent<NetworkIdentity>();
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            int Notes(byte[] keyframe)
            {
                using var br = new BinaryReader(new MemoryStream(keyframe)); playback.ApplyFrame(live, br, processEvents: false);
                int n = 0; foreach (var _ in live.Query().With<Note>().Build()) n++;
                return n;
            }
            Assert.Equal(1, Notes(before));
            Assert.Equal(0, Notes(after));
            Assert.Equal(1, Notes(before));
        }
    }
}
