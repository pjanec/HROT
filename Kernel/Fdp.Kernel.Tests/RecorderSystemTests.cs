using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

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
            recorder.RecordDeltaFrame(repo, 0, writer);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            ulong version = reader.ReadUInt64();
            byte type = reader.ReadByte();
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
            recorder.RecordDeltaFrame(repo, 0, writer);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            reader.ReadUInt64(); // Version
            reader.ReadByte();   // Type
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
            recorder.RecordDeltaFrame(repo, 0, writer);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            // Skip Header
            reader.ReadUInt64();
            reader.ReadByte();
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
            recorder.RecordDeltaFrame(repo, 2, writer);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            Assert.Equal(3ul, reader.ReadUInt64());
            Assert.Equal(0, reader.ReadByte());
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
            recorder.RecordDeltaFrame(repo, 2, writer);
            
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            
            // Skip to chunks
            reader.ReadUInt64();
            reader.ReadByte();
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
    }
}
