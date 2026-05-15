using System;
using System.IO;
using System.Reflection;
using Fdp.Core.Logging;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// Playback system for Flight Recorder snapshots.
    /// Implements FDP-DES-005 design for state restoration.
    /// </summary>
    public class PlaybackSystem
    {
        private readonly byte[] _scratchBuffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];

        private delegate void ManagedRestorerDelegate(object table, int chunkIndex, byte[] data, EntityRepository repo);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ManagedRestorerDelegate> _managedRestorers = new();
        
        /// <summary>
        /// Applies a recorded frame to the repository.
        /// Handles both keyframes and deltas.
        /// </summary>
        /// <param name="repo">Entity repository</param>
        /// <param name="reader">Binary reader to read from</param>
        /// <param name="eventBus">Optional event bus for event restoration</param>
        /// <param name="processEvents">If false, skips event processing (for seeking/fast-forward)</param>
        public void ApplyFrame(EntityRepository repo, BinaryReader reader, FdpEventBus? eventBus = null, bool processEvents = true)
        {
            ulong tick = reader.ReadUInt64();
            repo.SetGlobalVersion((uint)tick);
            byte frameType = reader.ReadByte();
            reader.ReadInt64(); // Skip WallClockTicks (FORMAT_VERSION 3+, 8 bytes)
            
            // 1. APPLY DESTRUCTIONS (Delta Only)
            if (frameType == 0)
            {
                // ...
                int dCount = reader.ReadInt32();
                for (int i = 0; i < dCount; i++)
                {
                    int idx = reader.ReadInt32();
                    ushort gen = reader.ReadUInt16();
                    
                    // Logic: If entity exists and matches gen, kill it.
                    var e = new Entity(idx, gen);
                    if (repo.IsAlive(e))
                    {
                        repo.DestroyEntity(e);
                    }
                }
            }
            else if (frameType == 1)
            {
                // Keyframe - Full state reset
                repo.Clear();
                
                // Keyframe - no destructions, skip destruction count
                int dCount = reader.ReadInt32();
            }
            
            // 2. RESTORE EVENTS (if eventBus provided)
            ReadAndInjectEvents(reader, eventBus, processEvents);

            
            // 3. RESTORE SINGLETONS
            int singletonCount = reader.ReadInt32();
            for (int i = 0; i < singletonCount; i++)
            {
                int typeId = reader.ReadInt32();
                int len = reader.ReadInt32();
                
                // Optimized: Read directly into scratch buffer to avoid allocation
                if (len > _scratchBuffer.Length)
                {
                    // Fallback for huge singletons (rare)
                    byte[] hugeBuffer = reader.ReadBytes(len);
                    RestoreSingleton(repo, typeId, hugeBuffer, 0, len);
                }
                else
                {
                    reader.Read(_scratchBuffer, 0, len);
                    RestoreSingleton(repo, typeId, _scratchBuffer, 0, len);
                }
            }

            // 4. APPLY CHUNKS
            int cCount = reader.ReadInt32();
            for (int i = 0; i < cCount; i++)
            {
                int chunkId = reader.ReadInt32();
                int compCount = reader.ReadInt32();
                
                for (int j = 0; j < compCount; j++)
                {
                    int typeId = reader.ReadInt32();
                    int len = reader.ReadInt32();
                    
                    if (len > 0)
                    {
                        byte[] data = reader.ReadBytes(len);
                        
                        // LOGIC:
                        // 1. Find the component table for typeId
                        // 2. Memcpy 'data' DIRECTLY into the table at chunkId
                        // 3. Implicit Creation: The data contains the components for entities.
                        //    If the EntityIndex marks them as dead, we must "Revive" them.
                        
                        ApplyChunkData(repo, typeId, chunkId, data);
                    }
                }
            }
            
            // 4. INDEX REPAIR PASS
            // After applying all chunk data, we need to synchronize the EntityIndex
            // Metadata (ActiveCount, MaxIssuedIndex, etc.) needs to be rebuilt from the restored headers
            repo.GetEntityIndex().RebuildMetadata();
            
            // 5. MANAGED COMPONENT MASK REPAIR (defensive)
            // In the normal case, EntityIndex chunks are written first by the recorder,
            // so masks are already correct after restoration in step 4 above.
            // However, SetChunk() does NOT update EntityHeader.ComponentMask, so if a
            // delta frame contains a managed chunk update without a corresponding
            // EntityIndex update, masks could drift. This scan ensures correctness.
            RepairManagedComponentMasks(repo);
        }

        /// <summary>
        /// Defensive repair of ComponentMask bits for managed components after chunk restoration.
        /// In the normal path, EntityIndex chunks already carry correct masks from the recording.
        /// This method acts as a safety net: it scans all managed tables and ensures mask bits
        /// match actual data (non-null = set, null = clear).
        /// Protects against edge cases where a delta frame updates managed data without a
        /// corresponding EntityIndex chunk (no structural change on the entity that frame).
        /// Must be called AFTER EntityIndex chunks and managed chunks are both restored,
        /// and AFTER RebuildMetadata() has run.
        /// </summary>
        private void RepairManagedComponentMasks(EntityRepository repo)
        {
            var componentTables = repo.GetRegisteredComponentTypes();
            var entityIndex = repo.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;

            // Strictly guard against empty worlds. Do not clamp maxIndex to 0.
            if (maxIndex < 0 || entityIndex.ActiveCount == 0) return;

            foreach (var kvp in componentTables)
            {
                var table = kvp.Value;

                // Only process managed component tables
                if (!table.GetType().IsGenericType ||
                    table.GetType().GetGenericTypeDefinition() != typeof(ManagedComponentTable<>))
                    continue;

                int typeId = table.ComponentTypeId;

                for (int i = 0; i <= maxIndex; i++)
                {
                    // Double-check bounds to prevent state-drift crashes
                    if (i > entityIndex.MaxIssuedIndex) break;

                    // Safe to bypass standard bounds check since we manually validated
                    ref var header = ref entityIndex.GetHeaderUnsafe(i);

                    if (!header.IsActive) continue;

                    // Check if the managed table has non-null data for this entity
                    object rawObj = table.GetRawObject(i);

                    if (rawObj != null)
                    {
                        // Data exists → ensure mask bit is set
                        header.ComponentMask.SetBit(typeId);
                    }
                    else
                    {
                        // No data → ensure mask bit is clear
                        header.ComponentMask.ClearBit(typeId);
                    }
                }
            }
        }

        /// <summary>
        /// Reads events from the binary reader and injects them into the event bus.
        /// Format: [UnmanagedStreamCount][...unmanaged...] [ManagedStreamCount][...managed...]
        /// Creates event streams on-demand - no registration needed!
        /// </summary>
        private void ReadAndInjectEvents(BinaryReader reader, FdpEventBus? eventBus, bool processEvents)
        {
            // ========== UNMANAGED EVENTS ==========
            int unmanagedStreamCount = reader.ReadInt32();
            
            if (eventBus == null || !processEvents)
            {
                // Skip unmanaged events
                for (int i = 0; i < unmanagedStreamCount; i++)
                {
                    reader.ReadInt32(); // typeId
                    int elementSize = reader.ReadInt32(); // elementSize
                    int count = reader.ReadInt32(); // count
                    reader.BaseStream.Seek((long)count * elementSize, System.IO.SeekOrigin.Current);
                }
            }
            else
            {
                eventBus.ClearCurrentBuffers();
                
                for (int i = 0; i < unmanagedStreamCount; i++)
                {
                    int typeId = reader.ReadInt32();
                    int elementSize = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    int byteCount = count * elementSize;
                    
                    byte[] eventData = reader.ReadBytes(byteCount);
                    eventBus.InjectIntoCurrentBySize(typeId, elementSize, eventData);
                }
            }
            
            // ========== MANAGED EVENTS ==========
            int managedStreamCount = reader.ReadInt32();
            
            if (managedStreamCount == 0) return; // No managed events

            
            if (eventBus == null || !processEvents)
            {
                // Skip managed events using BlockSize (Format v2)
                for (int i = 0; i < managedStreamCount; i++)
                {
                    reader.ReadInt32(); // typeId
                    reader.ReadInt32(); // elementSize (0)
                    
                    // [New] Read Block Size
                    int blockSize = reader.ReadInt32();
                    
                    // Efficient skip
                    reader.BaseStream.Seek(blockSize, SeekOrigin.Current);
                }
            }
            else
            {
                for (int i = 0; i < managedStreamCount; i++)
                {
                    int typeId = reader.ReadInt32();
                    reader.ReadInt32(); // elementSize (0)
                    
                    // [New] Read Block Size
                    int blockSize = reader.ReadInt32();
                    
                    // Normal Deserialization
                    string typeName = reader.ReadString();
                    int count = reader.ReadInt32();
                    
                    // Resolve type from fully qualified name
                    Type? eventType = Type.GetType(typeName);
                    if (eventType == null)
                    {
                        // Fallback: Skip if type unknown (thanks to BlockSize!)
                        // Actually, implementing accurate partial skip inside "else" is tricky without knowing start.
                        // But since we have BlockSize, we can verify stream position after reading.
                        
                        throw new InvalidOperationException(
                            $"Cannot deserialize managed event - type '{typeName}' not found. " +
                            "Ensure the assembly containing this type is loaded.");
                    }
                    
                    // Deserialize events using reflection to call FdpAutoSerializer.Deserialize<T>()
                    var events = new System.Collections.Generic.List<object>(count);
                    var deserializeMethod = typeof(FdpAutoSerializer)
                        .GetMethod( nameof(FdpAutoSerializer.Deserialize), new[] { typeof(BinaryReader) })!
                        .MakeGenericMethod(eventType);
                    
                    for (int j = 0; j < count; j++)
                    {
                        object evt = deserializeMethod.Invoke(null, new object[] { reader })!;
                        events.Add(evt);
                    }
                    
                    // Inject into event bus
                    eventBus.InjectManagedIntoCurrent(typeId, eventType, events);
                }
            }
        }


        
        private void ApplyChunkData(EntityRepository repo, int typeId, int chunkIndex, byte[] data)
        {
            
            // Case 0: Special Entity Index Chunk
            if (typeId == -1)
            {
                repo.GetEntityIndex().RestoreChunkFromBuffer(chunkIndex, data);
                return;
            }

            // Find the component table by type ID
            var componentTables = repo.GetRegisteredComponentTypes();
            
            IComponentTable? targetTable = null;
            foreach (var kvp in componentTables)
            {
                if (kvp.Value.ComponentTypeId == typeId)
                {
                    targetTable = kvp.Value;
                    break;
                }
            }
            
            if (targetTable == null)
            {
                throw new InvalidOperationException(
                    $"Component type ID {typeId} not found in repository. " +
                    "Ensure all component types are registered before playback.");
            }
            
            // For unmanaged tables, we can do raw memory copy
            if (targetTable is IUnmanagedComponentTable unmanagedTable)
            {
                // We need to copy data directly into the chunk
                // This requires access to the underlying NativeChunkTable
                // For now, we'll use a helper method
                RestoreChunkData(unmanagedTable, chunkIndex, data);
            }
            else 
            {
                // Managed Component Support
                if (targetTable.GetType().IsGenericType && 
                    targetTable.GetType().GetGenericTypeDefinition() == typeof(ManagedComponentTable<>))
                {
                    Type componentType = targetTable.GetType().GetGenericArguments()[0];
                    
                    if (!_managedRestorers.TryGetValue(componentType, out var restorer))
                    {
                        var method = typeof(PlaybackSystem).GetMethod(nameof(RestoreManagedTableAdapter), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                            .MakeGenericMethod(componentType);
                        restorer = (ManagedRestorerDelegate)Delegate.CreateDelegate(typeof(ManagedRestorerDelegate), method);
                        _managedRestorers.TryAdd(componentType, restorer);
                    }
                    
                    restorer(targetTable, chunkIndex, data, repo);
                }
                else
                {
                    throw new NotImplementedException($"Unknown component table type: {targetTable.GetType().Name}");
                }
            }
        }
        
        private static void RestoreManagedTableAdapter<T>(object tableObj, int chunkIndex, byte[] data, EntityRepository repo) where T : class
        {
             // Pass the repository to RestoreManagedTable so it can update component masks
             RestoreManagedTable((ManagedComponentTable<T>)tableObj, chunkIndex, data, repo);
        }

        private static void RestoreManagedTable<T>(ManagedComponentTable<T> table, int chunkIndex, byte[] data, EntityRepository repo) where T : class
        {
             using (var ms = new MemoryStream(data))
             using (var reader = new BinaryReader(ms))
             {
                 T?[] chunkData = FdpAutoSerializer.Deserialize<T?[]>(reader);

                 table.SetChunk(chunkIndex, chunkData, repo.GlobalVersion);
                 
                 // NOTE: ComponentMask bits are normally correct from the EntityIndex chunk
                 // (restored earlier in the same ApplyFrame call). RepairManagedComponentMasks()
                 // runs as a defensive pass after all chunks are restored to catch edge cases.
             }
        }
        
        private void RestoreSingleton(EntityRepository repo, int typeId, byte[] buffer, int offset, int length)
        {
            // 1. Get existing table or Auto-Create it
            var table = repo.GetSingletonTable(typeId);
            
            if (table == null)
            {
                // We need to auto-register the singleton if it doesn't exist on playback.
                // This requires the Type.
                Type? type = ComponentTypeRegistry.GetType(typeId);
                if (type == null) 
                {
                    // Warn or skip? If we don't know the type, we can't restore.
                    return; 
                }

                // Reflection hack to call "SetSingleton<T>(default)" to initialize the table
                if (type.IsValueType)
                {
                    // This will init the unmanaged table
                    var method = typeof(EntityRepository).GetMethod(nameof(EntityRepository.SetSingletonUnmanaged))!.MakeGenericMethod(type);
                    method.Invoke(repo, new object[] { Activator.CreateInstance(type)! });
                }
                else
                {
                    var method = typeof(EntityRepository).GetMethod(nameof(EntityRepository.SetSingletonManaged))!.MakeGenericMethod(type);
                    method.Invoke(repo, new object?[] { null });
                }
                
                // Re-fetch
                table = repo.GetSingletonTable(typeId)!;
            }

            // 2. Restore Data
            if (table is IUnmanagedComponentTable unmanaged)
            {
                // Zero-Alloc: Create span from buffer slice
                var span = new ReadOnlySpan<byte>(buffer, offset, length);
                unmanaged.RestoreChunkFromBuffer(0, span);
            }
            else
            {
                // Managed restoration
                Type type = table.ComponentType;
                // Zero-Alloc: Wrap existing buffer
                using (var ms = new MemoryStream(buffer, offset, length))
                using (var reader = new BinaryReader(ms))
                {
                    // Deserialize using dynamic dispatch to FdpAutoSerializer
                    var deserializeMethod = typeof(FdpAutoSerializer)
                        .GetMethod(nameof(FdpAutoSerializer.Deserialize), new[] { typeof(BinaryReader) })!
                        .MakeGenericMethod(type);
                    
                    object? val = deserializeMethod.Invoke(null, new object[] { reader });
                    
                    // Set to index 0 (Singleton) via SetRawObject
                    table.SetRawObject(0, val!);
                }
            }
        }

        private void RestoreChunkData(IUnmanagedComponentTable table, int chunkIndex, byte[] data)
        {
            // Copy data directly into the chunk
            table.RestoreChunkFromBuffer(chunkIndex, data);
        }
        
        /// <summary>
        /// Repairs the EntityIndex after loading chunk data.
        /// This is the "Index Repair Pass" from FDP-DES-005.
        /// </summary>
        private void RepairEntityIndex(EntityRepository repo)
        {
            var entityIndex = repo.GetEntityIndex();
            var componentTables = repo.GetRegisteredComponentTypes();
            
            int maxIndex = entityIndex.MaxIssuedIndex;
            int chunkCapacity = entityIndex.GetChunkCapacity();
            
            // Iterate all entities and check if they have components
            for (int i = 0; i <= maxIndex; i++)
            {
                ref var header = ref entityIndex.GetHeader(i);
                
                // Check if this entity has any components
                bool hasComponents = false;
                foreach (var kvp in componentTables)
                {
                    if (header.ComponentMask.IsSet(kvp.Value.ComponentTypeId))
                    {
                        hasComponents = true;
                        break;
                    }
                }
                
                // If entity has components but is marked dead, revive it
                if (hasComponents && !header.IsActive)
                {
                    // Force restore this entity
                    // We need to determine the generation from the data
                    // For now, use generation 1
                    repo.RestoreEntity(i, true, header.Generation > 0 ? header.Generation : 1, header.ComponentMask);
                }
            }
        }
    }
    
    /// <summary>
    /// Reader for Flight Recorder files.
    /// Handles file format and decompression.
    /// </summary>
    public class RecordingReader : IDisposable
    {
        private readonly FileStream _fileStream;
        private readonly BinaryReader _reader;
        private readonly PlaybackSystem _playback;
        
        public uint FormatVersion { get; private set; }
        public long RecordingTimestamp { get; private set; }
        
        public RecordingReader(string filePath)
        {
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                _reader = new BinaryReader(_fileStream);
                _playback = new PlaybackSystem();
                ReadGlobalHeader();
            }
            catch
            {
                _reader?.Dispose();
                _fileStream?.Dispose();
                throw;
            }
        }
        
        private void ReadGlobalHeader()
        {
            // Read magic
            byte[] magic = _reader.ReadBytes(6);
            string magicStr = System.Text.Encoding.ASCII.GetString(magic);
            
            if (magicStr != "FDPREC")
            {
                throw new InvalidDataException(
                    $"Invalid file format. Expected 'FDPREC', got '{magicStr}'");
            }
            
            // Read version
            FormatVersion = _reader.ReadUInt32();
            
            if (FormatVersion != FdpConfig.FORMAT_VERSION)
            {
                throw new InvalidDataException(
                    $"Format version mismatch. File version: {FormatVersion}, " +
                    $"Expected: {FdpConfig.FORMAT_VERSION}");
            }
            
            // Read timestamp
            RecordingTimestamp = _reader.ReadInt64();
        }
        
        /// <summary>
        /// Reads and applies the next frame to the repository.
        /// Returns false if end of file reached.
        /// </summary>
        public bool ReadNextFrame(EntityRepository repo)
        {
            try
            {
                // Read compressed size
                // Format: [CompLen: 4][UncompLen: 4][Tick: 8][Type: 1][CompressedData...]
                if (_fileStream.Position >= _fileStream.Length) 
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: EOF at {_fileStream.Position}/{_fileStream.Length}");
                    return false;
                }
                
                // Read entire outer header in one bulk read via the typed struct
                if (_fileStream.Position + FrameOuterHeader.Size > _fileStream.Length)
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: Not enough bytes for outer header at {_fileStream.Position}/{_fileStream.Length}");
                    return false;
                }

                Span<byte> outerHeaderBytes = stackalloc byte[FrameOuterHeader.Size];
                _fileStream.Read(outerHeaderBytes);
                FrameOuterHeader outerHeader = System.Runtime.InteropServices.MemoryMarshal.Read<FrameOuterHeader>(outerHeaderBytes);

                int compSize = outerHeader.CompressedSize;
                int uncompSize = outerHeader.UncompressedSize;

                if (compSize <= 0)
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: Invalid compSize {compSize} at {_fileStream.Position - FrameOuterHeader.Size}/{_fileStream.Length}");
                    return false;
                }
                
                // Read compressed data
                if (_fileStream.Position + compSize > _fileStream.Length)
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: Truncated compressed data. Needed {compSize}, Avail {_fileStream.Length - _fileStream.Position}");
                    return false;
                }

                byte[] compressedData = _reader.ReadBytes(compSize);
                
                if (compressedData.Length != compSize)
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: Read mismatch. Asked {compSize}, Got {compressedData.Length}");
                    return false; // Truncated or incomplete frame
                }
                
                // Decompress
                byte[] rawFrame = new byte[uncompSize];
                try 
                {
                    K4os.Compression.LZ4.LZ4Codec.Decode(compressedData, 0, compressedData.Length, rawFrame, 0, uncompSize);
                }
                catch (Exception ex)
                {
                   Console.WriteLine($"[KERNEL-DEBUG] Decompression failed: {ex.Message}");
                   return false; // Decompression failed (corrupted data)
                }
                
                // Apply frame
                using (var ms = new MemoryStream(rawFrame))
                using (var frameReader = new BinaryReader(ms))
                {
                    _playback.ApplyFrame(repo, frameReader);
                }
                
                return true;
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("[KERNEL-DEBUG] EndOfStreamException caught.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KERNEL-DEBUG] General Exception in ReadNextFrame: {ex}");
                return false; // Any other error during read/decode
            }
        }
        
        public void Dispose()
        {
            _reader?.Dispose();
            _fileStream?.Dispose();
        }
    }
}
