using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    public class RecorderSystemTests
    {
        [Fact]
        public void RecordDeltaFrame_WritesMetadataCorrectly()
        {
            // Arrange
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            repo.Tick(); // GlobalVersion 2
            
            // Act
            recorder.RecordDeltaFrame(repo, 0, writer, 0L);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            ulong version = reader.ReadUInt64();
            byte type = reader.ReadByte();
            reader.ReadInt64(); // WallClockTicks (FORMAT_VERSION 3+)
            int destroyCount = reader.ReadInt32();
            
            Assert.Equal(2ul, version);
            Assert.Equal(0, type); // Delta
            Assert.Equal(0, destroyCount);
        }
        
        [Fact]
        public void RecordDeltaFrame_IncludesDestructionLog()
        {
            // Arrange
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            var e1 = repo.CreateEntity();
            repo.DestroyEntity(e1);
            
            // Act
            recorder.RecordDeltaFrame(repo, 0, writer, 0L);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            reader.ReadUInt64(); // Version
            reader.ReadByte();   // Type
            reader.ReadInt64();  // WallClockTicks (FORMAT_VERSION 3+)
            int destroyCount = reader.ReadInt32();
            
            Assert.Equal(1, destroyCount);
            
            int index = reader.ReadInt32();
            ushort gen = reader.ReadUInt16();
            
            Assert.Equal(e1.Index, index);
            Assert.Equal(e1.Generation, gen);
        }
        
        [Fact]
        public void RecordDeltaFrame_StructuralChanges_IncludesEntityHeaders()
        {
            // Arrange
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            var e1 = repo.CreateEntity(); // LastChangeTick updated
            
            // Act
            // PrevTick = 0, so changes (tick 1) > 0. Should record.
            recorder.RecordDeltaFrame(repo, 0, writer, 0L);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            // Skip Header
            reader.ReadUInt64();
            reader.ReadByte();
            reader.ReadInt64(); // WallClockTicks (FORMAT_VERSION 3+)
            reader.ReadInt32(); // Destructions
            
            // Read Chunk Count
            reader.ReadInt32(); // Unmanaged Events
            reader.ReadInt32(); // Managed Events
            reader.ReadInt32(); // Singleton Count
            int chunkCount = reader.ReadInt32();
            // Should have at least 1 chunk (EntityIndex chunk 0)
            Assert.True(chunkCount >= 1, "Should have one chunk for EntityIndex updates");
            
            // Read first chunk header
            int chunkId = reader.ReadInt32(); // 0
            int typeCount = reader.ReadInt32(); // 1
            int typeId = reader.ReadInt32(); // -1 for EntityIndex
            int dataLen = reader.ReadInt32();
            
            Assert.Equal(0, chunkId);
            Assert.Equal(-1, typeId);
            Assert.Equal(FdpConfig.CHUNK_SIZE_BYTES, dataLen);
        }
        
        [Fact]
        public void RecordDeltaFrame_NoChanges_OutputsMinimalFrame()
        {
            // Arrange
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            repo.Tick(); // V=2
            var e1 = repo.CreateEntity(); // ChangeTick=2
            
            // Record initial state as "Baseline" (simulated)
            
            repo.Tick(); // V=3
            // No changes made after tick 2.
            
            // Act
            // Ask for changes since V=2. No structural/component changes > 2.
            recorder.RecordDeltaFrame(repo, 2, writer, 0L);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            Assert.Equal(3ul, reader.ReadUInt64());
            Assert.Equal(0, reader.ReadByte());
            reader.ReadInt64(); // WallClockTicks (FORMAT_VERSION 3+)
            Assert.Equal(0, reader.ReadInt32()); // DestroyCount
            Assert.Equal(0, reader.ReadInt32()); // Unmanaged Events
            Assert.Equal(0, reader.ReadInt32()); // Managed Events
            Assert.Equal(0, reader.ReadInt32()); // Singleton Count
            Assert.Equal(0, reader.ReadInt32()); // ChunkCount (Look ma, no changes!)
        }
        
        [Fact]
        public void RecordDeltaFrame_ComponentChanges_IncludesOnlyDirtyChunks()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>(); // Basic int component
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            repo.Tick(); // V=2
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new IntComponent { Value = 123 });
            
            // This set change tick to 2.
            
            repo.Tick(); // V=3
            // Modify component
            ref IntComponent val = ref repo.GetComponentRW<IntComponent>(e1);
            val.Value = 456;
            // Now change tick for chunk is 3.
            
            // Act
            // prevTick = 2. 3 > 2, so should record.
            recorder.RecordDeltaFrame(repo, 2, writer, 0L);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            // Skip to chunks
            reader.ReadUInt64();
            reader.ReadByte();
            reader.ReadInt64(); // WallClockTicks (FORMAT_VERSION 3+)
            reader.ReadInt32(); // Destroy
            reader.ReadInt32(); // Unmanaged Events
            reader.ReadInt32(); // Managed Events
            
            // Singletons
            int sCount = reader.ReadInt32();
            for(int k=0; k<sCount; k++) { reader.ReadInt32(); int l=reader.ReadInt32(); reader.ReadBytes(l); }
            
            int chunkCount = reader.ReadInt32();
            
            Assert.True(chunkCount >= 1);
            
            // We should find our component type
            bool foundComponent = false;
            for(int i=0; i<chunkCount; i++)
            {
                int chunkId = reader.ReadInt32();
                int typeCount = reader.ReadInt32();
                
                for(int t=0; t<typeCount; t++)
                {
                    int typeId = reader.ReadInt32();
                    int len = reader.ReadInt32();
                    byte[] data = reader.ReadBytes(len);
                    
                    if (typeId != -1) // Not EntityIndex
                    {
                        foundComponent = true;
                        // Verify value (456) is in data at index e1.Index
                        // int is 4 bytes.
                        int offset = e1.Index * 4;
                        int value = BitConverter.ToInt32(data, offset);
                        Assert.Equal(456, value);
                    }
                }
            }
            Assert.True(foundComponent, "Should contain the modified component chunk");
        }

        // -----------------------------------------------------------------------
        // TASK-E008: RecorderSystem dual-stream binary verification
        // -----------------------------------------------------------------------

        /// <summary>
        /// Helper: skip the fixed-size frame metadata prefix so tests can seek directly to chunks.
        /// Works for both keyframe (type=1) and delta (type=0) frames.
        /// </summary>
        private static void SkipFrameMetadata(BinaryReader reader)
        {
            reader.ReadUInt64(); // GlobalVersion
            byte frameType = reader.ReadByte(); // 0=delta, 1=keyframe
            reader.ReadInt64(); // WallClockTicks

            // Destructions
            int dCount = reader.ReadInt32();
            for (int i = 0; i < dCount; i++) { reader.ReadInt32(); reader.ReadUInt16(); }

            // Events (unmanaged + managed counts, assume 0 events)
            reader.ReadInt32(); // unmanaged stream count
            reader.ReadInt32(); // managed stream count

            // Singletons
            int sCount = reader.ReadInt32();
            for (int i = 0; i < sCount; i++)
            {
                reader.ReadInt32(); // typeId
                int len = reader.ReadInt32();
                reader.BaseStream.Seek(len, System.IO.SeekOrigin.Current);
            }
        }

        /// <summary>
        /// TASK-E008 SC-1: Recording a keyframe with at least one active entity must produce
        /// exactly one chunk with typeId==-1 (hot) and at least one with typeId==-2 (cold).
        /// </summary>
        [Fact]
        public void DualStream_Keyframe_WritesHotAndColdChunks()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            repo.CreateEntity();

            recorder.RecordKeyframe(repo, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            SkipFrameMetadata(reader);

            int chunkCount = reader.ReadInt32();
            Assert.True(chunkCount >= 2, "Keyframe must produce at least one hot chunk and one cold chunk");

            int hotCount = 0;
            int coldCount = 0;
            for (int i = 0; i < chunkCount; i++)
            {
                reader.ReadInt32(); // chunkId
                reader.ReadInt32(); // typeCount (always 1)
                int typeId = reader.ReadInt32();
                int dataLen = reader.ReadInt32();
                reader.ReadBytes(dataLen);

                if (typeId == -1) hotCount++;
                else if (typeId == -2) coldCount++;
            }

            Assert.True(hotCount >= 1, "Must have at least one hot entity-index chunk (typeId=-1)");
            Assert.True(coldCount >= 1, "Must have at least one cold entity-index chunk (typeId=-2)");
        }

        /// <summary>
        /// TASK-E008 SC-2: The byte count of the hot entity-index chunk must equal
        /// GetChunkCapacity() * sizeof(BitMask512) (== capacity * 64 == CHUNK_SIZE_BYTES).
        /// </summary>
        [Fact]
        public void DualStream_HotChunkSize_EqualsCapacityTimes64()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            repo.CreateEntity();

            recorder.RecordKeyframe(repo, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            SkipFrameMetadata(reader);

            int chunkCount = reader.ReadInt32();
            int hotDataLen = -1;
            for (int i = 0; i < chunkCount; i++)
            {
                reader.ReadInt32(); // chunkId
                reader.ReadInt32(); // typeCount
                int typeId = reader.ReadInt32();
                int dataLen = reader.ReadInt32();
                reader.ReadBytes(dataLen);
                if (typeId == -1) hotDataLen = dataLen;
            }

            Assert.True(hotDataLen >= 0, "Hot entity-index chunk must be present");
            int expectedBytes = repo.GetEntityIndex().GetChunkCapacity() * Unsafe.SizeOf<BitMask512>();
            Assert.Equal(expectedBytes, hotDataLen);
        }

        /// <summary>
        /// TASK-E008 SC-3: After destroying entity at slot 1, the 64-byte block at offset
        /// slot-1 * 64 in the hot chunk must be all zeros in the recorded data.
        /// </summary>
        [Fact]
        public void DualStream_Sanitization_DeadEntitySlotIsAllZeros()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e0 = repo.CreateEntity(); // slot 0
            var e1 = repo.CreateEntity(); // slot 1
            var e2 = repo.CreateEntity(); // slot 2

            repo.DestroyEntity(e1); // destroy slot 1 -- hot mask is cleared

            recorder.RecordKeyframe(repo, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            SkipFrameMetadata(reader);

            int chunkCount = reader.ReadInt32();
            byte[] hotData = null;
            for (int i = 0; i < chunkCount; i++)
            {
                reader.ReadInt32(); // chunkId
                reader.ReadInt32(); // typeCount
                int typeId = reader.ReadInt32();
                int dataLen = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLen);
                if (typeId == -1) hotData = data;
            }

            Assert.NotNull(hotData);

            int maskSize = Unsafe.SizeOf<BitMask512>(); // = 64
            int slotOffset = e1.Index * maskSize;

            bool allZero = true;
            for (int b = 0; b < maskSize; b++)
            {
                if (hotData[slotOffset + b] != 0) { allZero = false; break; }
            }
            Assert.True(allZero, "Dead entity slot must be a 64-byte zero block in the recorded hot chunk");
        }

        /// <summary>
        /// TASK-E008 SC-4: A component registered with DataPolicy.NoRecord must have its
        /// bit cleared in the recorded hot chunk data, even when the entity carries that component.
        /// </summary>
        [Fact]
        public void DualStream_RecordableMaskFilter_NonRecordableBitIsCleared()
        {
            // Use a dedicated component (ID 240) that is not registered by any other test,
            // so we can safely register it with NoRecord here without affecting global registry state.
            using var repo = new EntityRepository();
            // Register NoRecordTestComponent (ID 240) as non-recordable.
            repo.RegisterComponent<NoRecordTestComponent>(DataPolicy.NoRecord);

            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new NoRecordTestComponent { Value = 42 });

            // Verify the bit IS set in the live hot mask before recording.
            Assert.True(repo.GetEntityIndex().GetComponentMask(e.Index).IsSet(240),
                "Pre-condition: bit 240 must be set on the live entity before recording");

            recorder.RecordKeyframe(repo, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            SkipFrameMetadata(reader);

            int chunkCount = reader.ReadInt32();
            byte[] hotData = null;
            for (int i = 0; i < chunkCount; i++)
            {
                reader.ReadInt32(); // chunkId
                reader.ReadInt32(); // typeCount
                int typeId = reader.ReadInt32();
                int dataLen = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLen);
                if (typeId == -1) hotData = data;
            }

            Assert.NotNull(hotData);

            // Bit 240 is in quad 3 (240 >> 6 == 3), bit-in-quad == 240 & 63 == 48.
            // Each entity mask is 64 bytes at offset (entityIndex * 64).
            // Quad 3 starts at byte offset 24 within the mask (8 bytes per ulong quad).
            // Bit 48 occupies byte (48 / 8) == 6 within the quad, bit (48 % 8) == 0 within that byte.
            int entityOffset = e.Index * 64;
            int quadIndex = 240 >> 6;   // = 3
            int bitInQuad = 240 & 0x3F; // = 48
            int byteInQuad = bitInQuad / 8; // = 6
            int bitInByte = bitInQuad % 8;  // = 0
            int byteOffset = entityOffset + (quadIndex * 8) + byteInQuad;

            bool bitSet = (hotData[byteOffset] & (1 << bitInByte)) != 0;
            Assert.False(bitSet,
                "Non-recordable component bit 240 must be cleared in the recorded hot chunk");
        }

        /// <summary>
        /// TASK-E008 SC-5: The global recording header written by AsyncRecorder must carry
        /// FORMAT_VERSION == 5.
        /// </summary>
        [Fact]
        public void FormatVersion_WrittenInGlobalHeader_Is5()
        {
            // Also verify the constant value directly so any future bump is noticed here.
            Assert.Equal(5u, FdpConfig.FORMAT_VERSION);

            string testFilePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fdp_frtest_{Guid.NewGuid()}.fdp");
            try
            {
                using var repo = new EntityRepository();
                using (var asyncRec = new AsyncRecorder(testFilePath))
                {
                    asyncRec.CaptureKeyframe(repo, System.DateTime.UtcNow.Ticks, blocking: true);
                }

                using var fs = new System.IO.FileStream(testFilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                byte[] magic = new byte[6];
                fs.Read(magic, 0, 6);
                byte[] versionBytes = new byte[4];
                fs.Read(versionBytes, 0, 4);
                uint version = BitConverter.ToUInt32(versionBytes, 0);

                Assert.Equal(5u, version);
            }
            finally
            {
                try { System.IO.File.Delete(testFilePath); } catch { }
                try { System.IO.File.Delete(testFilePath + ".meta.json"); } catch { }
            }
        }
    }
}
