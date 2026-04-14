using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using System.IO;

namespace Fdp.Tests
{
    public class RecorderFilteringTests
    {
        [Fact]
        public void RecorderSystem_SkipsSystemRange()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem { MinRecordableId = 0 };
            recorder.MinRecordableId = 100;

            // Entity 1: ID 50 (System)
            repo.HydrateEntity(50, 1);
            
            // Entity 2: ID 150 (Recordable)
            repo.HydrateEntity(150, 1);
            
            // Record
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Use Keyframe to force write all valid chunks
            recorder.RecordKeyframe(repo, writer, 0L);
            writer.Flush();
            ms.Position = 0;

            // Replay
            using var replayRepo = new EntityRepository();
            var playback = new PlaybackSystem();
            using var reader = new BinaryReader(ms);
            
            playback.ApplyFrame(replayRepo, reader);
            
            // Check
            var e50 = new Entity(50, 1);
            var e150 = new Entity(150, 1);
            
            Assert.False(replayRepo.IsAlive(e50), "Entity 50 should be dead/skipped");
            Assert.True(replayRepo.IsAlive(e150), "Entity 150 should be alive");
        }
    }
}
