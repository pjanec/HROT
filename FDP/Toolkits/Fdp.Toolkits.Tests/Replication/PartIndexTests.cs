using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Utilities;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐ <c>CE-2412</c> — the shared parts index (<see cref="PartIndex"/>) answers what a scan of every part answered: a parent's live parts,
    /// lowest part id first, ties in entity order; a part ADDED within the frame at once (a destroy and a create recycling one index
    /// included); a removed or destroyed part never; a re-parent by value write from the next frame (documented); more parts than the
    /// buffer ⇒ the true count; it rebuilds per frame and per part added, not on other structural changes; a steady read allocates
    /// nothing. 🔴 Red-proofs: the added-part rebuild dropped ⇒ the same-frame part is missed; the live parent re-check dropped ⇒ the
    /// re-parented part still shows under its old parent.
    /// </summary>
    public sealed class PartIndexTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        public PartIndexTests()
        {
            _w.RegisterComponent<PartMetadata>();
            _w.RegisterComponent<SimTransform>();
        }
        public void Dispose() => _w.Dispose();

        private Entity Part(Entity parent, int id)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new PartMetadata { ParentEntity = parent, InstanceId = id });
            return e;
        }

        private List<Entity> Scan(Entity parent)
        {
            var found = new List<(int Id, Entity E)>();
            foreach (var e in _w.Query().With<PartMetadata>().Build())
                if (_w.GetComponentRO<PartMetadata>(e).ParentEntity.Equals(parent)) found.Add((_w.GetComponentRO<PartMetadata>(e).InstanceId, e));
            var sorted = new List<Entity>();
            for (int i = 0; i < found.Count; i++)
            {   // stable by id (insertion), as a scan met them
                int j = sorted.Count; sorted.Add(found[i].E);
                while (j > 0 && _w.GetComponentRO<PartMetadata>(sorted[j - 1]).InstanceId > found[i].Id) { sorted[j] = sorted[j - 1]; j--; }
                sorted[j] = found[i].E;
            }
            return sorted;
        }

        private List<Entity> Index(Entity parent)
        {
            Span<Entity> buf = stackalloc Entity[64];
            int n = PartIndex.PartsOf(_w, parent, buf);
            Assert.True(n <= buf.Length);
            var list = new List<Entity>();
            for (int i = 0; i < n; i++) list.Add(buf[i]);
            return list;
        }

        [Fact]
        public void CE2412_ThePartsIndex_AnswersAsTheScan_WhateverHappensInTheFrame()
        {
            var rng = new Random(2412);
            var parents = new List<Entity>();
            for (int i = 0; i < 60; i++) parents.Add(_w.CreateEntity());
            for (int i = 0; i < 400; i++) Part(parents[rng.Next(parents.Count)], rng.Next(0, 20));   // duplicate ids: ties by entity order
            _w.Tick();
            foreach (var p in parents) Assert.Equal(Scan(p), Index(p));
            int builds = PartIndex.Of(_w).Builds;

            var unrelated = _w.CreateEntity();
            _w.AddComponent(unrelated, new SimTransform());                // structural, but no part added: no rebuild
            foreach (var p in parents) Assert.Equal(Scan(p), Index(p));
            Assert.Equal(builds, PartIndex.Of(_w).Builds);

            var lateParent = parents[3];
            var late = Part(lateParent, -1);                               // added this frame, sorts first
            Assert.Equal(late, Index(lateParent)[0]);

            var gone = Index(parents[5]);
            if (gone.Count > 0)
            {
                _w.RemoveComponent<PartMetadata>(gone[0]);                  // removed this frame
                Assert.DoesNotContain(gone[0], Index(parents[5]));
            }
            var doomed = Index(parents[6]);
            if (doomed.Count > 0)
            {
                _w.DestroyEntity(doomed[0]);                                // destroyed, and its index recycled by a new part elsewhere
                var reborn = Part(parents[7], 3);
                Assert.Equal(doomed[0].Index, reborn.Index);
                Assert.DoesNotContain(doomed[0], Index(parents[6]));
                Assert.Contains(reborn, Index(parents[7]));
            }
            foreach (var p in parents) Assert.Equal(Scan(p), Index(p));

            var moving = Index(parents[8]);
            Assert.NotEmpty(moving);
            _w.GetComponentRW<PartMetadata>(moving[0]).ParentEntity = parents[9];   // re-parent by value write (no production writer)
            Assert.DoesNotContain(moving[0], Index(parents[8]));                    // gone from the old parent at once (live re-check)
            _w.Tick();
            Assert.Contains(moving[0], Index(parents[9]));                          // under the new one from the next frame

            var crowded = _w.CreateEntity();
            for (int i = 0; i < 70; i++) Part(crowded, 100 - i);
            Span<Entity> small = stackalloc Entity[8];
            Assert.Equal(70, PartIndex.PartsOf(_w, crowded, small));
            Assert.Equal(31, _w.GetComponentRO<PartMetadata>(small[0]).InstanceId);   // the lowest ids, in order
            Assert.Equal(38, _w.GetComponentRO<PartMetadata>(small[7]).InstanceId);

            _w.Tick();
            Index(parents[0]);
            Span<Entity> buf = stackalloc Entity[64];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) PartIndex.PartsOf(_w, parents[i % parents.Count], buf);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
