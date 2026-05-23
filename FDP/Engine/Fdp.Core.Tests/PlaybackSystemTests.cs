using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    public class PlaybackSystemTests
    {
        [Fact]
        public void ApplyFrame_RestoresHeaderAndActiveCount()
        {
            // Arrange - Record data using the actual RecorderSystem
            using var sourceRepo = new EntityRepository();
            
            // Advance tick to 10 to simulate some history and verify tick restoration
            for(int i=0; i<10; i++) sourceRepo.Tick();
            
            var e0 = sourceRepo.CreateEntity();
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            var recorder = new RecorderSystem();
            recorder.RecordKeyframe(sourceRepo, writer, 0L);

            // Act - Replay into a fresh repository
            using var destRepo = new EntityRepository();
            var playback = new PlaybackSystem();
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            playback.ApplyFrame(destRepo, reader);
            
            // Assert
            Assert.Equal(1, destRepo.EntityCount);
            // Note: e0.Generation comes from sourceRepo.
            Assert.True(destRepo.IsAlive(new Entity(0, e0.Generation)));
            
            // Verify GlobalVersion was restored
            Assert.Equal(sourceRepo.GlobalVersion, destRepo.GlobalVersion);
        }

        [Fact]
        public void ApplyFrame_RestoresComponentData()
        {
             // Arrange
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            // Advance tick to 20
            for(int i=0; i<20; i++) sourceRepo.Tick();

            var e0 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e0, new IntComponent { Value = 999 });
            
            // Record
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            var recorder = new RecorderSystem();
            recorder.RecordKeyframe(sourceRepo, writer, 0L);
            
            // Act
            using var destRepo = new EntityRepository();
            destRepo.RegisterComponent<IntComponent>(); // Must register same components
            
            var playback = new PlaybackSystem();
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            playback.ApplyFrame(destRepo, reader);
            
            // Assert
            Assert.Equal(sourceRepo.GlobalVersion, destRepo.GlobalVersion);

            var entity = new Entity(0, e0.Generation);
            Assert.True(destRepo.IsAlive(entity));
            Assert.True(destRepo.HasComponent<IntComponent>(entity));
            
            ref IntComponent val = ref destRepo.GetComponentRW<IntComponent>(entity);
            Assert.Equal(999, val.Value);
        }

        // -----------------------------------------------------------------------
        // TASK-E009: PlaybackSystem dual-stream routing verification
        // -----------------------------------------------------------------------

        /// <summary>
        /// TASK-E009 SC-1/SC-2/SC-3: Full round-trip: record entity with component and generation,
        /// play back, assert active count, hot mask bits, and cold metadata all match.
        /// </summary>
        [Fact]
        public void RoundTrip_EntityIndexHotAndColdMatchOriginal()
        {
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();

            for (int i = 0; i < 5; i++) sourceRepo.Tick(); // advance generation counter

            var e0 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e0, new IntComponent { Value = 77 });

            // Capture generation and IsActive BEFORE recording.
            ushort srcGen = sourceRepo.GetEntityIndex().GetMetadata(e0.Index).Generation;
            Assert.True(sourceRepo.GetEntityIndex().GetMetadata(e0.Index).IsActive);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            var recorder = new RecorderSystem();
            recorder.RecordKeyframe(sourceRepo, writer, 0L);

            using var destRepo = new EntityRepository();
            destRepo.RegisterComponent<IntComponent>();
            var playback = new PlaybackSystem();
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            playback.ApplyFrame(destRepo, reader);

            // SC-1: active entity count matches.
            Assert.Equal(sourceRepo.EntityCount, destRepo.EntityCount);

            // SC-2: hot mask bit for IntComponent (ID 164) is set after playback.
            Assert.True(destRepo.GetEntityIndex().GetComponentMask(e0.Index).IsSet(164),
                "Hot chunk must be applied: bit 164 must be set after playback");

            // SC-3: cold metadata Generation and IsActive match.
            ref readonly var dstMeta = ref destRepo.GetEntityIndex().GetMetadata(e0.Index);
            Assert.Equal(srcGen, dstMeta.Generation);
            Assert.True(dstMeta.IsActive, "Cold chunk must be applied: entity must be active after playback");
        }

        /// <summary>
        /// TASK-E009 SC-5: Attempting to open a recording with FORMAT_VERSION 4 (old) must
        /// throw InvalidDataException, not silently misinterpret the binary data.
        /// </summary>
        [Fact]
        public void VersionMismatch_OldFormat_ThrowsInvalidDataException()
        {
            string testFilePath = Path.Combine(
                Path.GetTempPath(),
                $"fdp_v4test_{Guid.NewGuid()}.fdp");
            try
            {
                // Write a minimal recording file with FORMAT_VERSION 4 (one version behind).
                using (var fs = new FileStream(testFilePath, FileMode.Create))
                {
                    byte[] magic = System.Text.Encoding.ASCII.GetBytes("FDPREC");
                    fs.Write(magic, 0, 6);
                    byte[] versionBytes = BitConverter.GetBytes(4u); // old version
                    fs.Write(versionBytes, 0, 4);
                    byte[] timestampBytes = BitConverter.GetBytes(0L);
                    fs.Write(timestampBytes, 0, 8);
                    // No frame data needed; the header check fires first.
                }

                // RecordingReader must reject the stale format version.
                Assert.Throws<InvalidDataException>(() =>
                {
                    using var rr = new RecordingReader(testFilePath);
                });
            }
            finally
            {
                try { File.Delete(testFilePath); } catch { }
            }
        }
    }
}
