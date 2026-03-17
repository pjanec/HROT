using System;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    public class PlaybackIndexRepairTests
    {

        [Fact]
        public void RepairIndex_RebuildsMetadata_AndFreeList()
        {
            // Test that RebuildMetadata (and Repair) correctly sets up free list
            // If we have Entity 0 and 2 active. Entity 1 is dead.
            // Free list should link 1.
            // MaxIssuedIndex should be 2.

            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            var playback = new PlaybackSystem();
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            ushort expectedGen0, expectedGen1, expectedGen2;
            
            // Use a temporary source repository to generate the recording
            using (var temp = new EntityRepository())
            {
                temp.RegisterComponent<IntComponent>();
                
                var e0 = temp.CreateEntity(); // 0
                var e1 = temp.CreateEntity(); // 1
                var e2 = temp.CreateEntity(); // 2
                
                expectedGen0 = e0.Generation;
                expectedGen1 = e1.Generation;
                expectedGen2 = e2.Generation;
                
                temp.DestroyEntity(e1); // Kill 1
                
                // Record using the actual system
                new RecorderSystem().RecordKeyframe(temp, writer, 0L);
            }
            
            // Act
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            playback.ApplyFrame(repo, reader);
            
            // Assert
            Assert.True(repo.IsAlive(new Entity(0, expectedGen0)));
            Assert.False(repo.IsAlive(new Entity(1, expectedGen1)));
            Assert.True(repo.IsAlive(new Entity(2, expectedGen2)));
            
            // Validate MaxIssuedIndex
            Assert.Equal(2, repo.GetEntityIndex().MaxIssuedIndex);
            
            // Validate creation of new entity picks up 1 (Free list works)
            var eNew = repo.CreateEntity();
            Assert.Equal(1, eNew.Index);
        }
    }
}
