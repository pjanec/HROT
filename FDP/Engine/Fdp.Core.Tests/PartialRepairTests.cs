using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Xunit;
using static Fdp.Tests.FramePrefetcherTests;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1478 P2</c> (docs/DESIGN_Replay_Playback_Performance.md §3, DES-006 §5.1) — a delta repairs only what it wrote; the result must be the world a
    /// whole-world repair leaves, over random structural deltas, and a frame that wrote nothing must walk nothing.
    /// </summary>
    public class PartialRepairTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"partial_{Guid.NewGuid()}.fdp");
        private readonly List<Entity> _entities = new();
        private const int Count = 3000;   // > one hot (mask) chunk of 1 024 entities

        public void Dispose()
        {
            try { File.Delete(_path); } catch { }
            try { File.Delete(_path + ".meta.json"); } catch { }
        }

        private static EntityRepository NewWorld()
        {
            var r = new EntityRepository();
            r.RegisterComponent<PfPos>();
            r.RegisterComponent<PfInfo>();
            return r;
        }

        /// <summary>frame 0 keyframe, then <paramref name="deltas"/> deltas; frame 1 is idle (writes nothing), the others are seeded random edits incl. managed add / remove, spawn, destroy.</summary>
        private void Record(int deltas, bool quietFrame1 = true, bool managedOnlyEdits = false)
        {
            var rnd = new Random(99);
            using var repo = NewWorld();
            for (int i = 0; i < Count; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new PfPos { X = i });
                if (i % 5 == 0) repo.AddManagedComponent(e, new PfInfo { Name = "n" + i, Score = i });
                _entities.Add(e);
            }
            using var recorder = new AsyncRecorder(_path);
            repo.Tick();
            recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
            uint prev = repo.GlobalVersion;
            for (int f = 1; f <= deltas; f++)
            {
                repo.Tick();
                uint now = repo.GlobalVersion;
                if (!(quietFrame1 && f == 1))
                {
                    for (int k = 0; k < 30; k++)
                    {
                        var e = _entities[rnd.Next(_entities.Count)];
                        if (!repo.IsAlive(e)) continue;
                        if (!managedOnlyEdits && repo.HasComponent<PfPos>(e)) repo.GetComponentRW<PfPos>(e) = new PfPos { X = f, Y = k };
                        if (repo.HasManagedComponent<PfInfo>(e)) repo.SetManagedComponent(e, new PfInfo { Name = "f" + f, Score = k });
                    }
                    if (!managedOnlyEdits)
                    {
                        for (int k = 0; k < 6; k++)   // managed add / remove (structural: the masks move)
                        {
                            var e = _entities[rnd.Next(_entities.Count)];
                            if (!repo.IsAlive(e)) continue;
                            if (repo.HasManagedComponent<PfInfo>(e)) repo.RemoveManagedComponent<PfInfo>(e);
                            else repo.AddManagedComponent(e, new PfInfo { Name = "add" + f, Score = k });
                        }
                        var d = _entities[rnd.Next(_entities.Count)];
                        if (repo.IsAlive(d)) repo.DestroyEntity(d);
                        for (int k = 0; k < 3; k++)
                        {
                            var e = repo.CreateEntity();
                            repo.AddComponent(e, new PfPos { X = -f });
                            if (k == 0) repo.AddManagedComponent(e, new PfInfo { Name = "sp" + f });
                            _entities.Add(e);
                        }
                    }
                }
                recorder.CaptureFrame(repo, prev, DateTime.UtcNow.Ticks, blocking: true);
                prev = now;
            }
        }

        private string Digest(EntityRepository repo)
        {
            var sb = new StringBuilder();
            var idx = repo.GetEntityIndex();
            sb.Append(idx.ActiveCount).Append('/').Append(idx.MaxIssuedIndex).Append('|');
            for (int i = 0; i <= idx.MaxIssuedIndex; i++)
            {
                ref readonly var m = ref idx.GetMetadataUnsafe(i);
                if (!m.IsActive) { sb.Append('x'); continue; }
                ref var mask = ref idx.GetComponentMaskUnsafe(i);
                sb.Append(mask.IsSet(506) ? 'P' : 'p').Append(mask.IsSet(507) ? 'I' : 'i');
            }
            foreach (var e in _entities)
            {
                if (!repo.IsAlive(e)) continue;
                if (repo.HasManagedComponent<PfInfo>(e)) { var n = repo.GetComponentRO<PfInfo>(e); sb.Append(n.Name).Append(n.Score).Append(';'); }
            }
            return sb.ToString();
        }

        [Fact]
        public void A_RandomStructuralDeltas_PartialRepairLeavesTheWorldAWholeRepairDoes()
        {
            Record(deltas: 25);
            using var full = new PlaybackController(_path);
            using var part = new PlaybackController(_path);
            full.Playback.ForceFullRepair = true;
            using var a = NewWorld();
            using var b = NewWorld();
            int partialFrames = 0;
            for (int i = 0; i < full.TotalFrames; i++)
            {
                full.StepForward(a);
                part.StepForward(b);
                Assert.Equal(Digest(a), Digest(b));
                if (!part.Playback.LastRepairWasFull) partialFrames++;
            }
            Assert.Equal(full.TotalFrames - 1, partialFrames);   // the keyframe repairs everything, every delta after it only what it wrote
        }

        [Fact]
        public void B_AMaskThatDriftedUnderAManagedChunkTheFrameWrote_IsRepairedLikeTheWholeRepairDoes()
        {
            Record(deltas: 3, quietFrame1: false, managedOnlyEdits: true);
            using var full = new PlaybackController(_path);
            using var part = new PlaybackController(_path);
            full.Playback.ForceFullRepair = true;
            using var a = NewWorld();
            using var b = NewWorld();
            full.StepForward(a); part.StepForward(b);   // the keyframe

            // drift: the mask bit of a managed component that has data is cleared — in BOTH worlds, identically
            Entity victim = default;
            foreach (var e in _entities) if (a.IsAlive(e) && a.HasManagedComponent<PfInfo>(e)) { victim = e; break; }
            a.GetEntityIndex().GetComponentMaskUnsafe(victim.Index).ClearBit(507);
            b.GetEntityIndex().GetComponentMaskUnsafe(victim.Index).ClearBit(507);

            full.StepForward(a); part.StepForward(b);   // a delta that rewrites the managed chunk
            Assert.False(part.Playback.LastRepairWasFull);
            Assert.True(b.GetEntityIndex().GetComponentMaskUnsafe(victim.Index).IsSet(507), "the partial repair left the drifted managed mask bit cleared");
            Assert.Equal(Digest(a), Digest(b));
        }

        [Fact]
        public void C_AFrameThatWroteNothing_WalksNoEntity_AndAKeyframeWalksTheWholeIndex()
        {
            Record(deltas: 2);   // frame 1 is idle
            using var part = new PlaybackController(_path);
            using var w = NewWorld();
            part.StepForward(w);
            Assert.True(part.Playback.LastRepairWasFull);
            Assert.True(part.Playback.LastRepairEntitiesVisited >= Count, "a keyframe repairs the whole index");
            part.StepForward(w);
            Assert.False(part.Playback.LastRepairWasFull);
            Assert.Equal(0, part.Playback.LastRepairEntitiesVisited);
        }

        [Fact]
        public void D_ADeltaOnAWorldTheLastRepairDidNotLeaveConsistent_RepairsEverything()
        {
            Record(deltas: 2);
            using var part = new PlaybackController(_path);
            using var first = NewWorld();
            using var second = NewWorld();
            part.StepForward(first);
            part.Playback.ForceFullRepair = false;
            part.StepForward(second);   // a delta onto a world no full repair ever saw
            Assert.True(part.Playback.LastRepairWasFull);
        }
    }
}
