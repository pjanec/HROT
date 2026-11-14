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

        /// <summary>
        /// ⭐ <c>CE-4395</c> — parts ADDED within the frame are taken in from the repository's add log, WITHOUT a build (📐 27 full builds a
        /// frame at 3 000 men), and every read still answers exactly as the scan: ties with built parts by entity order, a part added, removed
        /// and added again, a parent created this frame, more parts than the buffer. A steady read with late parts allocates nothing.
        /// 🔴 Red-proof: <c>TakeLateAdds</c> ignoring the log (every add rebuilds) ⇒ the build count moves.
        /// </summary>
        [Fact]
        public void CE4395_PartsAddedWithinTheFrame_AreTakenInWithoutABuild_AndAnswerAsTheScan()
        {
            var rng = new Random(4395);
            var parents = new List<Entity>();
            for (int i = 0; i < 40; i++) parents.Add(_w.CreateEntity());
            for (int i = 0; i < 200; i++) Part(parents[rng.Next(parents.Count)], rng.Next(0, 10));
            _w.Tick();
            foreach (var p in parents) Assert.Equal(Scan(p), Index(p));
            var index = PartIndex.Of(_w);
            int builds = index.Builds;

            for (int round = 0; round < 30; round++)                     // the brain's shape: a read, a part added, a read …
            {
                Part(parents[rng.Next(parents.Count)], rng.Next(0, 10));   // ids collide with built parts: ties by entity order
                var p = parents[rng.Next(parents.Count)];
                Assert.Equal(Scan(p), Index(p));
            }
            // a late part on a RECYCLED low index, its id equal to a built part's: the build's walk would meet it FIRST (ties by entity index)
            var tieParent = parents[10];
            var built = Index(tieParent);
            Assert.NotEmpty(built);
            int tieId = _w.GetComponentRO<PartMetadata>(built[^1]).InstanceId;
            Entity lowest = default;
            foreach (var e in _w.Query().With<PartMetadata>().Build()) { lowest = e; break; }
            Assert.True(lowest.Index < built[^1].Index);
            _w.DestroyEntity(lowest);
            var recycled = Part(tieParent, tieId);
            Assert.Equal(lowest.Index, recycled.Index);
            Assert.Equal(Scan(tieParent), Index(tieParent));
            var again = Part(parents[2], 4);                               // added, removed, added again — once
            Index(parents[2]);
            _w.RemoveComponent<PartMetadata>(again);
            Assert.DoesNotContain(again, Index(parents[2]));
            _w.AddComponent(again, new PartMetadata { ParentEntity = parents[2], InstanceId = 4 });
            var newParent = _w.CreateEntity();                             // a parent born this frame, past the build's arrays
            var child = Part(newParent, 1);
            Assert.Equal(new List<Entity> { child }, Index(newParent));
            for (int i = 0; i < 20; i++) Part(newParent, 50 - i);
            Span<Entity> small = stackalloc Entity[4];
            Assert.Equal(21, PartIndex.PartsOf(_w, newParent, small));
            Assert.Equal(1, _w.GetComponentRO<PartMetadata>(small[0]).InstanceId);
            Assert.Equal(31, _w.GetComponentRO<PartMetadata>(small[1]).InstanceId);
            foreach (var p in parents) Assert.Equal(Scan(p), Index(p));
            Assert.Equal(Scan(newParent), Index(newParent));
            Assert.Equal(builds, index.Builds);                            // ⭐ not one build within the frame
            Assert.True(index.LateAdds >= 50);

            Span<Entity> buf = stackalloc Entity[64];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) PartIndex.PartsOf(_w, parents[i % parents.Count], buf);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);

            _w.Tick();                                                     // the next frame builds once and forgets the late parts
            foreach (var p in parents) Assert.Equal(Scan(p), Index(p));
            Assert.Equal(builds + 1, index.Builds);
        }

        /// <summary>⭐ <c>CE-4395</c> — a version set back (playback, a test reset) must not make the add log speak for this version: the index
        /// falls back to a build and still answers as the scan.</summary>
        [Fact]
        public void CE4395_AVersionSetBack_FallsBackToABuild()
        {
            var parent = _w.CreateEntity();
            Part(parent, 1);
            _w.Tick();
            Assert.Single(Index(parent));
            uint v = _w.GlobalVersion;
            Part(parent, 2);
            _w.ResetGlobalVersion(v);                                      // same version number, the log forgotten
            var third = Part(parent, 3);
            Assert.Equal(Scan(parent), Index(parent));
            Assert.Contains(third, Index(parent));
        }

        /// <summary>
        /// ⭐ CE-4517 (X-2) — once the frame's index is prepared (<see cref="PartIndex.PrepareFrame"/>), reads take no lock: eight threads reading
        /// every parent at once answer exactly as the scan, with no build, and a steady read allocates nothing. A part added after that still
        /// shows (the locked path takes it in).
        /// 🔴 Red-proof: the freshness stamps published BEFORE the build (as before) is a race only a stress run shows; this rail pins the answers,
        /// the build count and the late part.
        /// </summary>
        [Fact]
        public void CE4517_APreparedIndex_IsReadFromManyThreadsAtOnce_AsTheScan_WithoutABuild()
        {
            var parents = new List<Entity>();
            for (int p = 0; p < 120; p++)
            {
                var parent = _w.CreateEntity();
                parents.Add(parent);
                for (int k = 0; k < 1 + p % 7; k++) Part(parent, 7 - k);
            }
            _w.Tick();
            PartIndex.PrepareFrame(_w);
            int builds = PartIndex.Of(_w).Builds;
            var expect = new List<Entity>[parents.Count];
            for (int p = 0; p < parents.Count; p++) expect[p] = Scan(parents[p]);

            int wrong = 0;
            System.Threading.Tasks.Parallel.For(0, 8, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
            {
                var buf = new Entity[16];
                for (int round = 0; round < 50; round++)
                    for (int p = 0; p < parents.Count; p++)
                    {
                        int n = PartIndex.PartsOf(_w, parents[p], buf);
                        bool same = n == expect[p].Count;
                        for (int k = 0; same && k < n; k++) same = buf[k].Equals(expect[p][k]);
                        if (!same) System.Threading.Interlocked.Increment(ref wrong);
                    }
            });
            Assert.Equal(0, wrong);
            Assert.Equal(builds, PartIndex.Of(_w).Builds);                 // fresh: nobody built

            Span<Entity> one = stackalloc Entity[16];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int p = 0; p < parents.Count; p++) PartIndex.PartsOf(_w, parents[p], one);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);

            var late = Part(parents[5], 0);                                // added within the frame: the locked path takes it in
            Assert.Equal(Scan(parents[5]), Index(parents[5]));
            Assert.Contains(late, Index(parents[5]));
        }
    }
}
