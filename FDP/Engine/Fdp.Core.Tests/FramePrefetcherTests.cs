using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using MessagePack;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1478 / CE-4407 P1</c> (docs/DESIGN_Replay_Playback_Performance.md §3) — the frame prefetcher: a recording applied through the background-decoded
    /// frames must produce EXACTLY the world the inline read + decode does, frame for frame, through seeks and reverse steps.
    /// </summary>
    public class FramePrefetcherTests : IDisposable
    {
        [ComponentId(506)] public struct PfPos { public float X, Y, Z; }

        [MessagePackObject, ComponentId(507)]
        public record PfInfo
        {
            [Key(0)] public string Name { get; set; } = string.Empty;
            [Key(1)] public int Score { get; set; }
        }

        private const int Frames = 40;
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"prefetch_{Guid.NewGuid()}.fdp");
        private readonly List<Entity> _entities = new();

        public void Dispose()
        {
            try { File.Delete(_path); } catch { }
            try { File.Delete(_path + ".meta.json"); } catch { }
        }

        /// <summary>40 frames (keyframes at 0 / 15 / 30) of seeded random edits: unmanaged moves, managed replacements, spawns, destroys.</summary>
        private void Record()
        {
            var rnd = new Random(1234);
            using var repo = new EntityRepository();
            repo.RegisterComponent<PfPos>();
            repo.RegisterComponent<PfInfo>();
            for (int i = 0; i < 400; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new PfPos { X = i });
                if (i % 7 == 0) repo.AddManagedComponent(e, new PfInfo { Name = "n" + i, Score = i });
                _entities.Add(e);
            }

            using var recorder = new AsyncRecorder(_path);
            uint prev = 0;
            for (int f = 0; f < Frames; f++)
            {
                repo.Tick();
                uint now = repo.GlobalVersion;
                for (int k = 0; k < 40; k++)
                {
                    var e = _entities[rnd.Next(_entities.Count)];
                    if (!repo.IsAlive(e) || !repo.HasComponent<PfPos>(e)) continue;
                    repo.GetComponentRW<PfPos>(e) = new PfPos { X = f, Y = rnd.Next(1000), Z = k };
                }
                for (int k = 0; k < 4; k++)
                {
                    var e = _entities[rnd.Next(_entities.Count)];
                    if (repo.IsAlive(e) && repo.HasManagedComponent<PfInfo>(e))
                        repo.SetManagedComponent(e, new PfInfo { Name = "f" + f, Score = rnd.Next(100) });
                }
                if (f % 3 == 1) { var e = _entities[rnd.Next(_entities.Count)]; if (repo.IsAlive(e)) repo.DestroyEntity(e); }
                if (f % 4 == 2)
                {
                    var e = repo.CreateEntity();
                    repo.AddComponent(e, new PfPos { X = -f });
                    repo.AddManagedComponent(e, new PfInfo { Name = "new" + f });
                    _entities.Add(e);
                }

                if (f % 15 == 0) recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                else recorder.CaptureFrame(repo, prev, DateTime.UtcNow.Ticks, blocking: true);
                prev = now;
            }
        }

        private string Digest(EntityRepository repo)
        {
            var sb = new StringBuilder();
            sb.Append(repo.GetEntityIndex().ActiveCount).Append('|');
            foreach (var e in _entities)
            {
                if (!repo.IsAlive(e)) { sb.Append('x'); continue; }
                if (repo.HasComponent<PfPos>(e)) { var p = repo.GetComponentRO<PfPos>(e); sb.Append(p.X).Append(',').Append(p.Y).Append(',').Append(p.Z); }
                if (repo.HasManagedComponent<PfInfo>(e)) { var i = repo.GetComponentRO<PfInfo>(e); sb.Append(i.Name).Append('=').Append(i.Score); }
                sb.Append(';');
            }
            return sb.ToString();
        }

        private static EntityRepository NewWorld()
        {
            var r = new EntityRepository();
            r.RegisterComponent<PfPos>();
            r.RegisterComponent<PfInfo>();
            return r;
        }

        [Fact]
        public void A_RecordingPlayedThroughTheTwoPaths_IsTheSameWorldAtEveryFrame()
        {
            Record();
            using var inline = new PlaybackController(_path, prefetchDepth: 0);
            using var ahead = new PlaybackController(_path, prefetchDepth: 3);
            using var a = NewWorld();
            using var b = NewWorld();
            Assert.Equal(Frames, ahead.TotalFrames);

            for (int i = 0; i < Frames; i++)
            {
                if (i > 0) Assert.True(ahead.WaitForPrefetch(i), $"the worker never had frame {i} ready");
                Assert.True(inline.StepForward(a));
                Assert.True(ahead.StepForward(b));
                Assert.Equal(Digest(a), Digest(b));
            }
            Assert.Equal(0, inline.PrefetchedApplies);
            Assert.Equal(Frames, inline.InlineApplies);
            // every frame after the cold start came from the decoded-ahead buffers: nothing was read or decoded on the applying thread
            Assert.Equal(Frames - 1, ahead.PrefetchedApplies);
            Assert.Equal(1, ahead.InlineApplies);
        }

        [Fact]
        public void B_SeeksAndReverseSteps_FlushAndReAim_AndStayIdentical()
        {
            Record();
            using var inline = new PlaybackController(_path, prefetchDepth: 0);
            using var ahead = new PlaybackController(_path, prefetchDepth: 3);
            using var a = NewWorld();
            using var b = NewWorld();
            var rnd = new Random(77);
            for (int op = 0; op < 60; op++)
            {
                int kind = rnd.Next(4);
                if (kind == 0)
                {
                    int target = rnd.Next(Frames);
                    inline.SeekToFrame(a, target); ahead.SeekToFrame(b, target);
                }
                else if (kind == 1)
                {
                    Assert.Equal(inline.StepBackward(a), ahead.StepBackward(b));
                }
                else
                {
                    if (rnd.Next(2) == 0) ahead.WaitForPrefetch(Math.Min(ahead.CurrentFrame + 1, Frames - 1), 200);   // sometimes ready, sometimes a race
                    Assert.Equal(inline.StepForward(a), ahead.StepForward(b));
                }
                Assert.Equal(inline.CurrentFrame, ahead.CurrentFrame);
                if (inline.CurrentFrame >= 0) Assert.Equal(Digest(a), Digest(b));
            }
        }

        [Fact]
        public void C_PreparedBytes_AreByteIdenticalToTheInlineDecode()
        {
            Record();
            using var ctl = new PlaybackController(_path, prefetchDepth: 0);
            var frames = new FrameMetadata[ctl.TotalFrames];
            for (int i = 0; i < frames.Length; i++) frames[i] = ctl.GetFrameMetadata(i);
            using var pf = new FramePrefetcher(_path, frames, ctl.FormatVersion, depth: 3);
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int header = FrameOuterHeader.SizeFor(ctl.FormatVersion);

            for (int i = 0; i < frames.Length; i++)
            {
                pf.Want(i);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                PreparedFrame? got = null;
                while (got == null && sw.ElapsedMilliseconds < 10000)
                    if (!pf.TryTake(i, out got!)) { got = null; System.Threading.Thread.Sleep(1); }
                Assert.NotNull(got);

                var comp = new byte[frames[i].CompressedSize];
                file.Position = frames[i].FilePosition + header;
                file.ReadExactly(comp);
                var raw = new byte[frames[i].UncompressedSize];
                K4os.Compression.LZ4.LZ4Codec.Decode(comp, 0, comp.Length, raw, 0, raw.Length);

                Assert.Equal(i, got!.Index);
                Assert.Equal(raw.Length, got.RawSize);
                Assert.True(new ReadOnlySpan<byte>(raw).SequenceEqual(new ReadOnlySpan<byte>(got.Raw, 0, got.RawSize)), $"frame {i}: the prepared bytes differ from the inline decode");
                Assert.Equal(frames[i].Tick, got.Header.Tick);
                pf.Release(got);
            }
        }

        [Fact]
        public void D_APreparedApply_AllocatesNoFrameBuffer_OnTheCallersThread()
        {
            Record();
            using var ahead = new PlaybackController(_path, prefetchDepth: 3);
            using var w = NewWorld();
            ahead.SeekToFrame(w, 0);
            long smallest = long.MaxValue;
            for (int i = 1; i < Frames; i++)
            {
                Assert.True(ahead.WaitForPrefetch(i));
                long before = GC.GetAllocatedBytesForCurrentThread();
                ahead.StepForward(w);
                smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            // the frame's raw / compressed buffers belong to the worker; the caller only wraps the decoded bytes (a stream + a reader)
            Assert.True(smallest < 16 * 1024, $"a prepared apply allocated {smallest} B on the caller's thread");
        }

        [Fact]
        public void E_DisposeStopsTheWorker_AndReleasesTheFile()
        {
            Record();
            var ahead = new PlaybackController(_path, prefetchDepth: 3);
            using var w = NewWorld();
            ahead.StepForward(w);
            Assert.True(ahead.WaitForPrefetch(1));
            ahead.Dispose();
            File.Delete(_path);   // a second handle left open by the worker would keep the file on Windows
            Assert.False(File.Exists(_path));
        }
    }
}
