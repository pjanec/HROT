using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    public class ManagedComponentRecordingTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public ManagedComponentRecordingTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"managed_rec_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        // Test class for managed component
        [MessagePackObject]
        [ComponentId(229)]
        public record SquadName
        {
            [Key(0)]
            public string? Name { get; set; }
        }

        [ComponentId(254)]
        public struct CE2397Pos { public float X; }

        /// <summary>⭐ CE-2397 — a delta that changes managed AND unmanaged components records with almost no garbage — also the first
        /// delta after a keyframe — and plays back the same values. ✅ Red-proof: a new T?[16384] per managed chunk again (or dropping the
        /// 64 KB comparison buffers at a keyframe) ⇒ the allocation assertion goes red (≥ 64 KB).</summary>
        [Fact]
        public void CE2397_ADeltaAfterAKeyframe_AllocatesAlmostNothing_AndPlaysBack()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<SquadName>();
            repo.RegisterComponent<CE2397Pos>();
            var ents = new Entity[40];
            for (int i = 0; i < ents.Length; i++)
            {
                ents[i] = repo.CreateEntity();
                repo.AddManagedComponent(ents[i], new SquadName { Name = "n" + i });
                repo.AddComponent(ents[i], new CE2397Pos { X = i });
            }
            var names = new SquadName[ents.Length];
            for (int i = 0; i < names.Length; i++) names[i] = new SquadName { Name = "m" + i };   // built before any measuring

            var recorder = new Fdp.Core.FlightRecorder.RecorderSystem();
            var frames = new System.Collections.Generic.List<byte[]>();
            var ms = new MemoryStream(8 << 20);
            var w = new BinaryWriter(ms);
            byte[] Take() { w.Flush(); var b = ms.ToArray(); ms.SetLength(0); ms.Position = 0; return b; }

            long allocated = 0;
            for (int round = 0; round < 3; round++)
            {
                repo.Tick();
                recorder.RecordKeyframe(repo, w, 0L);
                frames.Add(Take());
                uint prev = repo.GlobalVersion;
                repo.Tick();
                for (int i = 0; i < ents.Length; i++)
                {
                    repo.SetManagedComponent(ents[i], names[i]);
                    repo.GetComponentRW<CE2397Pos>(ents[i]).X = 100 * round + i;
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                recorder.RecordDeltaFrame(repo, prev, w, 0L);
                if (round == 2) allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                frames.Add(Take());
            }
            Assert.True(allocated < 16 * 1024, $"a steady delta after a keyframe allocated {allocated / 1024} KB");

            using var replay = new EntityRepository();
            replay.RegisterComponent<SquadName>();
            replay.RegisterComponent<CE2397Pos>();
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            foreach (var f in frames.GetRange(frames.Count - 2, 2))
            {
                using var r = new BinaryReader(new MemoryStream(f));
                playback.ApplyFrame(replay, r);
            }
            for (int i = 0; i < ents.Length; i++)
            {
                Assert.Equal("m" + i, replay.GetComponentRO<SquadName>(ents[i]).Name);
                Assert.Equal(200f + i, replay.GetComponentRO<CE2397Pos>(ents[i]).X);
            }
        }

        [Fact]
        public void RecordAndReplay_ManagedComponent_ShouldRestore()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<SquadName>();
            
            var e = repo.CreateEntity();
            
            // Act
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Tick first
                repo.Tick();
                
                // Then add component
                repo.AddManagedComponent(e, new SquadName { Name = "Alpha" });
                
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<SquadName>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.True(replayRepo.HasManagedComponent<SquadName>(e), "Managed component should be restored");
            var comp = replayRepo.GetComponentRO<SquadName>(e);
            Assert.NotNull(comp);
            Assert.Equal("Alpha", comp.Name);
        }
    }
}
