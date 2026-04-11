using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    public class RecorderDeltaLogicTests
    {
        // Define some Component structs for testing
        [ComponentId(220)]
        public struct Position
        {
            public float X, Y, Z;
        }

        [ComponentId(221)]
        public struct Velocity
        {
            public float VX, VY, VZ;
        }

        [Fact]
        public void DirtyScan_DetectsChangedChunks_ViaVersions()
        {
            // Scenario: 
            // 1. Fill Chunk 0 completely.
            // 2. Add one entity to Chunk 1.
            // 3. Update ONLY the entity in Chunk 1.
            // 4. Verify Delta only records Chunk 1 for that component.

            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            int capacity = FdpConfig.GetChunkCapacity<Position>();
            
            // Fill Chunk 0
            for (int i = 0; i < capacity; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i });
            }

            // Start Chunk 1
            var eChunk1 = repo.CreateEntity();
            repo.AddComponent(eChunk1, new Position { X = 999 });

            repo.Tick(); // Advance tick. All chunks are "dirty" relative to 0.
            uint baselineTick = (uint)repo.GlobalVersion; // Tick = 1

            // Modify Chunk 1 only
            repo.Tick(); // Tick = 2
            ref var pos = ref repo.GetComponentRW<Position>(eChunk1);
            pos.X = 888; 
            // This updates Component Chunk 1 version to 2.
            // Component Chunk 0 version should stay at 1 (creation time).

            // Record Delta relative to baselineTick (1)
            recorder.RecordDeltaFrame(repo, baselineTick, writer, 0L);

            // Verify
            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            ReadFrameMetadata(reader); // Read headers

            // Verify Chunks
            int chunkCount = reader.ReadInt32(); // Placeholder was patched
            
            // We expect:
            // - Possibly EntityIndex Chunk 0 (if valid), EntityIndex Chunk 1 (modified).
            // - Position Component Chunk 1 (modified).
            // - Position Component Chunk 0 (should use version check and be skipped).
            
            // Note: EntityIndex headers might be updated for eChunk1 (LastChangeTick).
            // EntityIndex Chunk 0 headers: LastChangeTick was at creation (tick 0 or 1). 
            // If they weren't touched, they shouldn't be recorded if baseline is 1.
            
            bool foundPosChunk0 = false;
            bool foundPosChunk1 = false;

            for (int i = 0; i < chunkCount; i++)
            {
                int cId = reader.ReadInt32();
                int typeCount = reader.ReadInt32();
                int typeId = reader.ReadInt32();
                int dataLen = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLen);

                // Check for Position Component (which has a positive TypeID, EntityIndex is -1)
                if (typeId != -1)
                {
                    // Check if it's our Position component (assume it's the only one registered)
                    if (cId == 0) foundPosChunk0 = true;
                    if (cId == 1) foundPosChunk1 = true;
                }
            }

            Assert.False(foundPosChunk0, "Chunk 0 should not be recorded as it was not modified after baseline.");
            Assert.True(foundPosChunk1, "Chunk 1 should be recorded as it was modified.");
        }

        [Fact]
        public void DestructionLog_CapturesDeletions()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            repo.Tick();
            uint baseline = (uint)repo.GlobalVersion;

            repo.DestroyEntity(e1);

            recorder.RecordDeltaFrame(repo, baseline, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            reader.ReadUInt64(); // Ver
            reader.ReadByte();   // Type
            reader.ReadInt64();  // WallClockTicks (FORMAT_VERSION 3+)
            int dCount = reader.ReadInt32();
            
            Assert.Equal(1, dCount);
            int idx = reader.ReadInt32();
            ushort gen = reader.ReadUInt16();
            
            Assert.Equal(e1.Index, idx);
            Assert.Equal(e1.Generation, gen);
        }

        [Fact]
        public void Delta_OnlyRecordsModifiedComponents()
        {
            // Scenario:
            // Entity has Component A and B.
            // Modify A.
            // Delta should record chunk for A, but NOT for B.

            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1 });
            repo.AddComponent(e, new Velocity { VX = 1 });

            repo.Tick();
            uint baseline = (uint)repo.GlobalVersion;

            repo.Tick(); // Advance tick so modification is > baseline

            // Modify Position only
            ref var pos = ref repo.GetComponentRW<Position>(e);
            pos.X = 2;

            // No need for another tick here strictly, but usually we record at end of frame
            
            recorder.RecordDeltaFrame(repo, baseline, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            ReadFrameMetadata(reader);

            int chunkCount = reader.ReadInt32();
            
            int componentChunkCount = 0;

            for(int i=0; i<chunkCount; i++)
            {
                 int cId = reader.ReadInt32();
                 int tCount = reader.ReadInt32();
                 int tId = reader.ReadInt32();
                 int len = reader.ReadInt32();
                 reader.ReadBytes(len);

                 if (tId != -1)
                 {
                     componentChunkCount++;
                 }
            }
            
            // Expected: 1 component chunk (Position). Velocity should be skipped.
            Assert.Equal(1, componentChunkCount);
        }
        
        [Fact]
        public void Keyframe_RecordsAllChunks_RegardlessOfVersion()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1 });

            repo.Tick(); // Tick 1
            repo.Tick(); // Tick 2
            
            // No changes in Tick 2. 
            // Delta would be empty.
            // Keyframe should include everything.

            recorder.RecordKeyframe(repo, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            ulong v = reader.ReadUInt64();
            byte type = reader.ReadByte(); // 1 = Keyframe
            Assert.Equal(1, type);
            reader.ReadInt64(); // WallClockTicks (FORMAT_VERSION 3+)
            reader.ReadInt32(); // DestroyCount = 0
            reader.ReadInt32(); // Unmanaged Events
            reader.ReadInt32(); // Managed Events
            reader.ReadInt32(); // Singleton Count (0)

            int chunkCount = reader.ReadInt32();
            Assert.True(chunkCount > 0, "Keyframe must contain chunks even if stable");
        }

        // Helper
        private void ReadFrameMetadata(BinaryReader reader)
        {
            reader.ReadUInt64(); // Version
            reader.ReadByte();   // Type
            reader.ReadInt64();  // WallClockTicks (FORMAT_VERSION 3+)
            int dCount = reader.ReadInt32(); // Destructions
            for(int i=0; i<dCount; i++) { reader.ReadInt32(); reader.ReadUInt16(); }
            
            // Skip Events (Unmanaged + Managed)
            // Assuming 0 events for these logic tests
            reader.ReadInt32(); // Unmanaged Count
            reader.ReadInt32(); // Managed Count
            
            // Skip Singletons
            int sCount = reader.ReadInt32();
            for(int i=0; i<sCount; i++) 
            {
                reader.ReadInt32(); // Type
                int len = reader.ReadInt32(); 
                reader.BaseStream.Seek(len, SeekOrigin.Current); // Skip Data
            }
        }
    }
}

