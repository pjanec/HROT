using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Xunit;
using static Fdp.Tests.FramePrefetcherTests;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1478 P6</c> (format v8, docs/DESIGN_Replay_Playback_Performance.md §3) — a delta carries the 64-byte blocks that moved, not the chunk's used prefix.
    /// The recording must replay to EXACTLY the world the whole-prefix recording of the same session does, frame for frame, through seeks.
    /// </summary>
    public class BlockDeltaTests : IDisposable
    {
        private readonly string _blocks = Path.Combine(Path.GetTempPath(), $"blk_{Guid.NewGuid()}.fdp");
        private readonly string _whole = Path.Combine(Path.GetTempPath(), $"whl_{Guid.NewGuid()}.fdp");

        public void Dispose()
        {
            foreach (var f in new[] { _blocks, _whole }) { try { File.Delete(f); File.Delete(f + ".meta.json"); } catch { } }
        }

        private static EntityRepository NewWorld()
        {
            var r = new EntityRepository();
            r.RegisterComponent<PfPos>();
            r.RegisterComponent<PfInfo>();
            return r;
        }

        /// <summary>The same seeded session recorded into both files: sparse moves of a big table (few blocks change per chunk), spawns that grow the used prefix, destroys, managed edits.</summary>
        private void Record(int frames = 30)
        {
            var rnd = new Random(4242);
            using var repo = NewWorld();
            var entities = new System.Collections.Generic.List<Entity>();
            for (int i = 0; i < 2500; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new PfPos { X = i });
                if (i % 9 == 0) repo.AddManagedComponent(e, new PfInfo { Name = "n" + i });
                entities.Add(e);
            }
            using var rb = new AsyncRecorder(_blocks) { BlockDeltas = true };
            using var rw = new AsyncRecorder(_whole) { BlockDeltas = false };
            uint prev = 0;
            for (int f = 0; f < frames; f++)
            {
                repo.Tick();
                uint now = repo.GlobalVersion;
                for (int k = 0; k < 12; k++)   // sparse: a dozen of 2 500 rows move
                {
                    var e = entities[rnd.Next(entities.Count)];
                    if (repo.IsAlive(e) && repo.HasComponent<PfPos>(e)) repo.GetComponentRW<PfPos>(e) = new PfPos { X = f, Y = rnd.Next(1000), Z = k };
                }
                if (f % 5 == 3) { var e = entities[rnd.Next(entities.Count)]; if (repo.IsAlive(e)) repo.DestroyEntity(e); }
                if (f % 4 == 1)   // spawns grow the used prefix of every table
                    for (int k = 0; k < 5; k++) { var e = repo.CreateEntity(); repo.AddComponent(e, new PfPos { X = -f }); entities.Add(e); }
                if (f % 6 == 2) { var e = entities[rnd.Next(entities.Count)]; if (repo.IsAlive(e) && repo.HasManagedComponent<PfInfo>(e)) repo.SetManagedComponent(e, new PfInfo { Name = "f" + f }); }

                long ticks = DateTime.UtcNow.Ticks;
                if (f % 12 == 0) { rb.CaptureKeyframe(repo, ticks, blocking: true); rw.CaptureKeyframe(repo, ticks, blocking: true); }
                else { rb.CaptureFrame(repo, prev, ticks, blocking: true); rw.CaptureFrame(repo, prev, ticks, blocking: true); }
                prev = now;
            }
        }

        private static unsafe string Fingerprint(EntityRepository repo)
        {
            // every byte of every PfPos chunk row up to the max issued entity + the entity masks
            var idx = repo.GetEntityIndex();
            var sb = new System.Text.StringBuilder();
            sb.Append(idx.ActiveCount).Append('/').Append(idx.MaxIssuedIndex).Append('|');
            repo.TryGetTable(typeof(PfPos), out var table);
            var t = (ComponentTable<PfPos>)table!;
            var buf = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            int cap = FdpConfig.GetChunkCapacity<PfPos>();
            for (int c = 0; c * cap <= idx.MaxIssuedIndex; c++)
            {
                int n = t.CopyChunkToBuffer(c, buf);
                h.AppendData(new ReadOnlySpan<byte>(buf, 0, n));
            }
            sb.Append(Convert.ToHexString(h.GetHashAndReset())).Append('|');
            for (int i = 0; i <= idx.MaxIssuedIndex; i++)
            {
                if (!idx.GetMetadataUnsafe(i).IsActive) { sb.Append('x'); continue; }
                sb.Append(idx.GetComponentMaskUnsafe(i).IsSet(506) ? 'P' : 'p').Append(idx.GetComponentMaskUnsafe(i).IsSet(507) ? 'I' : 'i');
            }
            return sb.ToString();
        }

        [Fact]
        public void A_TheBlockRecording_ReplaysToTheWholePrefixRecordingsWorld_AtEveryFrame_AndThroughSeeks()
        {
            Record();
            using var pb = new PlaybackController(_blocks);
            using var pw = new PlaybackController(_whole);
            using var a = NewWorld();
            using var b = NewWorld();
            Assert.Equal(pw.TotalFrames, pb.TotalFrames);
            for (int i = 0; i < pw.TotalFrames; i++)
            {
                pw.StepForward(a); pb.StepForward(b);
                Assert.Equal(Fingerprint(a), Fingerprint(b));
            }
            var rnd = new Random(1);
            for (int k = 0; k < 20; k++)
            {
                int target = rnd.Next(pw.TotalFrames);
                pw.SeekToFrame(a, target); pb.SeekToFrame(b, target);
                Assert.Equal(Fingerprint(a), Fingerprint(b));
            }
        }

        [Fact]
        public void B_TheDeltasAreSmallerOnDisk_AndTheFileCarriesFormatV8()
        {
            Record();
            Assert.True(new FileInfo(_blocks).Length < new FileInfo(_whole).Length, $"block {new FileInfo(_blocks).Length} B vs whole {new FileInfo(_whole).Length} B");
            using var pb = new PlaybackController(_blocks);
            Assert.Equal(8u, pb.FormatVersion);
            long rawBlock = 0, rawWhole = 0;
            using var pw = new PlaybackController(_whole);
            for (int i = 0; i < pb.TotalFrames; i++)
                if (pb.GetFrameMetadata(i).FrameType == FrameType.Delta) { rawBlock += pb.GetFrameMetadata(i).UncompressedSize; rawWhole += pw.GetFrameMetadata(i).UncompressedSize; }
            Assert.True(rawBlock * 100 < rawWhole * 85, $"raw delta bytes: block {rawBlock} vs whole {rawWhole}");
        }
    }
}
