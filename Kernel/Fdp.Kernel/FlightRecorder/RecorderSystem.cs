using System;
using System.Collections.Generic;
using System.IO;

namespace Fdp.Kernel.FlightRecorder
{
    /// <summary>
    /// Core Flight Recorder system.
    /// Implements high-frequency (60Hz) state snapshots using raw memory copy strategy.
    /// Based on FDP-DES-001 and FDP-DES-002 design.
    /// </summary>
    public class RecorderSystem
    {
        // Special ID for EntityIndex headers
        private const int ENTITY_INDEX_TYPE_ID = -1;

        // Reusable scratch buffer for chunk copying (64KB)
        private readonly byte[] _scratchBuffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
        // Reusable liveness buffer (Max items per chunk = 64KB if 1 byte components)
        private readonly bool[] _livenessBuffer = new bool[FdpConfig.CHUNK_SIZE_BYTES];

        private delegate int ManagedRecorderDelegate(object table, EntityIndex index, BinaryWriter writer, uint prevTick);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ManagedRecorderDelegate> _managedRecorders = new();
        
        /// <summary>
        /// ID threshold below which entities are NOT recorded.
        /// Defaults to 0 (records everything).
        /// Set to FdpConfig.SYSTEM_ID_RANGE to exclude system entities.
        /// </summary>
        public int MinRecordableId { get; set; } = 0;

        /// <summary>
        /// Optional entity filter predicate.  When <c>null</c> (default), all entities
        /// above <see cref="MinRecordableId"/> are recorded — existing behaviour is
        /// completely unchanged.
        /// <para>
        /// When set, <c>FillLiveness</c> calls the predicate for every active entity;
        /// entities for which the predicate returns <c>false</c> are treated as
        /// non-live and their data is zeroed in the scratch buffer before writing.
        /// The predicate receives the full <see cref="Entity"/> handle (index +
        /// generation) so it can guard against recycled slots.
        /// </para>
        /// </summary>
        public Predicate<Entity>? EntityFilter { get; set; } = null;

        /// <summary>
        /// Records a delta frame containing only changed data since prevTick.
        /// Writes to the provided BinaryWriter.
        /// </summary>
        /// <param name="repo">Entity repository</param>
        /// <param name="prevTick">Previous version for delta comparison</param>
        /// <param name="writer">Binary writer to write to</param>
        /// <param name="eventBus">Optional event bus for event recording</param>
        public void RecordDeltaFrame(EntityRepository repo, uint prevTick, BinaryWriter writer, FdpEventBus? eventBus = null)
        {
            // ---------------------------------------------------------
            // 1. WRITE FRAME METADATA
            // ---------------------------------------------------------
            writer.Write((ulong)repo.GlobalVersion); // Current Tick (ulong) - Explicit Cast!
            writer.Write((byte)0);            // Type: Delta (0)
            
            // ---------------------------------------------------------
            // 2. WRITE DESTRUCTIONS
            // ---------------------------------------------------------
            var destroyed = repo.GetDestructionLog();
            writer.Write(destroyed.Count);
            
            foreach (var e in destroyed)
            {
                writer.Write(e.Index);
                writer.Write(e.Generation);
            }
            
            // ---------------------------------------------------------
            // 3. WRITE EVENTS (if eventBus provided)
            // ---------------------------------------------------------
            // Note: eventBus parameter is optional for backward compatibility
            // When null, this section writes 0 streams (no events)
            WriteEvents(writer, eventBus); // TODO: Pass actual eventBus when available
            
            // ---------------------------------------------------------
            // 4. RECORD SINGLETONS
            // ---------------------------------------------------------
            RecordSingletons(repo, writer, prevTick);
            
            // ---------------------------------------------------------
            // 5. DIRTY SCAN & WRITE (PER TABLE)
            // ---------------------------------------------------------
            var componentTables = repo.GetRegisteredComponentTypes();
            var entityIndex = repo.GetEntityIndex();
            
            // We write a "Chunk Blobs" count. 
            // A "Chunk Blob" is (ChunkID, TypeID, Data).
            // We no longer group by ChunkID because Chunk 0 of Type A != Chunk 0 of Type B.
            
            long chunkCountPos = writer.BaseStream.Position;
            writer.Write(0); // Placeholder for ChunkCount
            int actualChunkCount = 0;
            
            // ----------------------------------------------------------------
            // 3.1 FLUSH ENTITY INDEX (Structural Data)
            // MUST be written before components so receiver can revive entities first?
            // Actually, Playback processes full frame before RebuildMetadata, so order doesn't strictly matter
            // provided we have it.
            // But having headers allows validating component masks.
            // ----------------------------------------------------------------
            
            // Entity Index Chunk Iteration
            int indexTotalChunks = entityIndex.GetTotalChunks();
            int indexCapacity = entityIndex.GetChunkCapacity(); // 682 usually
            
            for (int c = 0; c < indexTotalChunks; c++)
            {
               if ((c + 1) * indexCapacity <= MinRecordableId) continue;

               // Check if chunk has ACTIVE entities
               int pop = entityIndex.GetChunkPopulation(c);
               // Structural change check: Did headers change?
               // If headers changed, we MUST send them.
               // Header change == StructuralChange.
               // ChunkHasStructuralChanges checks LastChangeTick inside the chunk.
               // FIX: Pass startID (c*capacity), not chunkIndex (c)!
               if (ChunkHasStructuralChanges(entityIndex, c * indexCapacity, indexCapacity, prevTick))
               {
                   // Sanitize Headers
                   // Liveness is determined by... checking validity?
                   // EntityIndex headers ARE the source of truth.
                   // SanitizeChunk on EntityIndex will zero out !IsActive slots.
                   
                   // Careful: We need to know which slots are active to sanitize.
                   // The headers themselves say if they are active.
                   // So SanitizeChunk implementation in EntityIndex should use IsActive flag within the header itself?
                   // But SanitizeChunk takes "liveness" span.
                   // So we must generate liveness span FROM the headers.
                   // Circular? Yes, but safe. 
                   // Read Headers -> Build Liveness -> Sanitize (Zero dead) -> Write.
                   
                   // Reuse shared liveness buffer
                   FillLiveness(entityIndex, c * indexCapacity, indexCapacity, _livenessBuffer);
                   
                   // Safe sanitization: Copy first, then sanitize the copy
                   int bytes = entityIndex.CopyChunkToBuffer(c, _scratchBuffer);
                   
                   if (bytes > 0)
                   {
                       // 1. Zero out dead entities
                       SanitizeScratchBuffer(_scratchBuffer, bytes, System.Runtime.CompilerServices.Unsafe.SizeOf<EntityHeader>(), new ReadOnlySpan<bool>(_livenessBuffer, 0, indexCapacity));
                       
                       // 2. Filter Component Masks (Remove transient bits)
                       // 2. Filter Component Masks (Remove transient bits)
                       // Ideally we compute this once per frame
                       var recordableMask = GetRecordableMask();
                       SanitizeHeadersMask(_scratchBuffer, bytes, recordableMask);
                       
                       actualChunkCount++;
                       writer.Write(c);
                       writer.Write(1); // Count = 1 (Headers only)
                       writer.Write(ENTITY_INDEX_TYPE_ID);
                       writer.Write(bytes);
                       writer.Write(_scratchBuffer, 0, bytes);
                   }
               }
            }

            // ----------------------------------------------------------------
            // 3.2 FLUSH COMPONENT TABLES
            // ----------------------------------------------------------------
            
            foreach (var kvp in componentTables)
            {
                var table = kvp.Value;
                if (!ComponentTypeRegistry.IsRecordable(table.ComponentTypeId)) continue;
                
                // Only support unmanaged for now -> REMOVED because we now support managed.
                // if (!(table is IUnmanagedComponentTable unmanagedTable)) continue;
                
                // Determine table-specific chunking
                // We need access to the underlying NativeChunkTable properties
                // but IComponentTable doesn't expose them directly.
                // We can assume it follows the entity ID space.
                // We iterate "Logical" chunks of the table.
                
                // HACK: We don't know the capacity of the table via interface.
                // But we know entity MaxIndex.
                int maxIndex = entityIndex.MaxIssuedIndex;
                if (maxIndex < 0) continue;
                
                // Managed Component Support (Tier 2)
                if (table.GetType().IsGenericType && 
                    table.GetType().GetGenericTypeDefinition() == typeof(ManagedComponentTable<>))
                {
                    Type componentType = table.GetType().GetGenericArguments()[0];
                    
                    if (!_managedRecorders.TryGetValue(componentType, out var recorder))
                    {
                        var method = typeof(RecorderSystem).GetMethod(nameof(RecordManagedTableAdapter), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                            .MakeGenericMethod(componentType);
                        recorder = (ManagedRecorderDelegate)Delegate.CreateDelegate(typeof(ManagedRecorderDelegate), method);
                        _managedRecorders.TryAdd(componentType, recorder);
                    }
                        
                    // Return chunk count delta
                    int delta = recorder(table, entityIndex, writer, prevTick);
                    actualChunkCount += delta;
                    continue;
                }

                if (!(table is IUnmanagedComponentTable unmanagedTable)) continue;

                // We need to iterate pages for this specific table.
                // Ideally, we iterate "Chunks" of the table.
                // Let's try to infer chunks.
                
                // Assuming standard implementation:
                // We loop from entity 0 to maxIndex.
                // But we want to do it per-chunk to use CopyChunkToBuffer.
                // We can't know the Chunk Capacity of the Table without casting or extending interface.
                // Let's rely on the interface method CopyChunkToBuffer(i, ...) which works on Chunk Index.
                // We just need to know HOW MANY chunks to iterate.
                // (MaxIndex / Capacity) + 1. But we don't know Capacity.
                
                // We must extend IComponentTable or use reflection/dynamic?
                // Or just try accessing chunks until we go out of bounds? No.
                
                // Let's iterate EntityIndex chunks as a "Base Clock"? No, that was the bug.
                
                // SOLUTION: We check "structural changes" globally? No.
                
                // Let's blindly try to iterate chunks 0..N assuming a safe upper bound?
                // Max chunks = MaxEntities / MinCapacity (1 element per chunk) = 1M.
                // 1M iterations is acceptable if fast check.
                
                // Better: generic Cast to ComponentTable<T> to get capacity?
                // We can't easily.
                
                // Workaround: Use EntityIndex chunks as "Logical Units" and map to Table chunks?
                // No, that's complex math (LCM of capacities).
                
                // Let's assume we iterate 0 to 2048 (Max possible chunks for 1M entities with reasonable struct size).
                // Safe upper bound is FdpConfig.GetRequiredChunks<byte>(...).
                
                // Scan this table for dirty chunks.
                Type type = kvp.Key; 
                int capacity = GetCapacityForType(type);
                int tableChunkCount = (maxIndex / capacity) + 1;
                
                for (int c = 0; c < tableChunkCount; c++)
                {
                    int startId = c * capacity;
                    if (startId > maxIndex) break; // Optimization using maxIndex
                    if (startId + capacity <= MinRecordableId) continue;
                    
                    // Check if chunk changed
                    bool changed = HasChunkChanged(table, c, capacity, prevTick) || 
                                   ChunkHasStructuralChanges(entityIndex, startId, capacity, prevTick);
                                   
                    if (!changed) continue;
                    
                    // Get liveness for this specific range
                    int endId = Math.Min(startId + capacity, maxIndex + 1);
                    int count = endId - startId;
                    if (count <= 0) continue;

                    // Reuse shared liveness buffer
                    FillLiveness(entityIndex, startId, count, _livenessBuffer);
                    
                    // Sanitize
                    // Copy first
                    int bytes = unmanagedTable.CopyChunkToBuffer(c, _scratchBuffer);

                    // Sanitize the COPY
                    if (bytes > 0)
                    {
                        SanitizeScratchBuffer(_scratchBuffer, bytes, table.ComponentSize, new ReadOnlySpan<bool>(_livenessBuffer, 0, capacity));
                        actualChunkCount++;
                        writer.Write(c);            // Chunk ID (specific to this table)
                        writer.Write(1);            // Count (1 component type)
                        writer.Write(table.ComponentTypeId); // Type ID
                        writer.Write(bytes);        // Data Length
                        writer.Write(_scratchBuffer, 0, bytes); // Data
                    }
                }
            }
            
            // Patch the ChunkCount
            long endPos = writer.BaseStream.Position;
            writer.BaseStream.Position = chunkCountPos;
            writer.Write(actualChunkCount);
            writer.BaseStream.Position = endPos;
        }
        
        /// <summary>
        /// Records a full keyframe (all active data).
        /// This is identical to RecordDeltaFrame with prevTick = 0.
        /// </summary>
        /// <param name="repo">Entity repository</param>
        /// <param name="writer">Binary writer to write to</param>
        /// <param name="eventBus">Optional event bus for event recording</param>
        public void RecordKeyframe(EntityRepository repo, BinaryWriter writer, FdpEventBus? eventBus = null)
        {
            // Write frame metadata
            writer.Write((ulong)repo.GlobalVersion); // Current Tick - Explicit Cast!
            writer.Write((byte)1);            // Type: Keyframe (1)
            
            // Keyframes don't need destruction log (full state)
            writer.Write(0); // DestroyCount = 0
            
            // Write events (same as delta frames)
            WriteEvents(writer, eventBus);
            
            // Record Singletons (force all dirty)
            RecordSingletons(repo, writer, 0);
            
            // Force all chunks to be considered "dirty" by using prevTick = 0
            RecordAllChunks(repo, writer);
        }
        
        private void RecordAllChunks(EntityRepository repo, BinaryWriter writer)
        {
            var componentTables = repo.GetRegisteredComponentTypes();
            var entityIndex = repo.GetEntityIndex();
            
            writer.Flush(); // Flush before reading position
            long chunkCountPos = writer.BaseStream.Position;
            
            writer.Write(0); // Placeholder
            
            int actualChunkCount = 0;
            
            // 1. Record EntityIndex Chunks (Keyframe = Force All Active)
            int indexCapacity = entityIndex.GetChunkCapacity();
            int maxEntityId = entityIndex.MaxIssuedIndex;
            int maxChunkToCheck = maxEntityId >= 0 ? (maxEntityId / indexCapacity) + 1 : 0;
            
            for (int c = 0; c < maxChunkToCheck; c++)
            {
                if ((c + 1) * indexCapacity <= MinRecordableId) continue;
                
                int pop = entityIndex.GetChunkPopulation(c);
                if (pop == 0) continue;

                FillLiveness(entityIndex, c * indexCapacity, indexCapacity, _livenessBuffer);
                
                int bytes = entityIndex.CopyChunkToBuffer(c, _scratchBuffer);
                
                if (bytes > 0)
                {
                    SanitizeScratchBuffer(_scratchBuffer, bytes, System.Runtime.CompilerServices.Unsafe.SizeOf<EntityHeader>(), new ReadOnlySpan<bool>(_livenessBuffer, 0, indexCapacity));
                    
                    // Filter Component Masks
                    var recordableMask = GetRecordableMask();
                    SanitizeHeadersMask(_scratchBuffer, bytes, recordableMask);
                    
                    actualChunkCount++;
                    writer.Write(c);
                    writer.Write(1);
                    writer.Write(ENTITY_INDEX_TYPE_ID);
                    writer.Write(bytes);
                    writer.Write(_scratchBuffer, 0, bytes);
                }
            }
            
            // 2. Record Component Chunks
            foreach (var kvp in componentTables)
            {
                var table = kvp.Value;
                if (!ComponentTypeRegistry.IsRecordable(table.ComponentTypeId)) continue;
                if (table.GetType().IsGenericType && 
                    table.GetType().GetGenericTypeDefinition() == typeof(ManagedComponentTable<>))
                {
                    Type componentType = table.GetType().GetGenericArguments()[0];
                    
                    if (!_managedRecorders.TryGetValue(componentType, out var recorder))
                    {
                        var method = typeof(RecorderSystem).GetMethod(nameof(RecordManagedTableAdapter), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                            .MakeGenericMethod(componentType);
                        recorder = (ManagedRecorderDelegate)Delegate.CreateDelegate(typeof(ManagedRecorderDelegate), method);
                        _managedRecorders.TryAdd(componentType, recorder);
                    }
                        
                    // For Keyframes, we force recording by passing prevTick = 0
                    int delta = recorder(table, entityIndex, writer, 0);
                    actualChunkCount += delta;
                    continue;
                }

                if (!(table is IUnmanagedComponentTable unmanagedTable)) continue;
                
                int capacity = GetCapacityForType(kvp.Key);
                int maxIndex = entityIndex.MaxIssuedIndex;
                if (maxIndex < 0) continue;
                
                // Iterate chunks for this table
                for (int c = 0; ; c++)
                {
                    int startId = c * capacity;
                    if (startId > maxIndex) break;
                    
                    if (startId + capacity <= MinRecordableId) continue;

                    // Check population of this entity range
                    if (!HasActiveEntities(entityIndex, startId, capacity)) continue;
                    
                    // Fill liveness
                    FillLiveness(entityIndex, startId, capacity, _livenessBuffer);
                    
                    int bytesWritten = unmanagedTable.CopyChunkToBuffer(c, _scratchBuffer);
                    
                    if (bytesWritten > 0)
                    {
                        SanitizeScratchBuffer(_scratchBuffer, bytesWritten, table.ComponentSize, new ReadOnlySpan<bool>(_livenessBuffer, 0, capacity));
                        actualChunkCount++;
                        writer.Write(c);
                        writer.Write(1); // Count
                        writer.Write(table.ComponentTypeId);
                        writer.Write(bytesWritten);
                        writer.Write(_scratchBuffer, 0, bytesWritten);
                    }
                }
            }
            
            writer.Flush(); // Flush before seeking
            long endPos = writer.BaseStream.Position;
            writer.BaseStream.Position = chunkCountPos;
            writer.Write(actualChunkCount);
            writer.Flush(); // Flush count
            writer.BaseStream.Position = endPos;
        }
        
        private bool ChunkHasStructuralChanges(EntityIndex index, int startId, int count, uint sinceVersion)
        {
            int endId = startId + count;
            int max = index.MaxIssuedIndex;
            
            for (int i = startId; i < endId && i <= max; i++)
            {
                // Optimization: could skip blocks of stable entities if we had a hierarchy
                ref var header = ref index.GetHeader(i);
                if (header.LastChangeTick > sinceVersion) return true;
                
                // Also check if it became inactive recently?
                // IsActive change also updates LastChangeTick via DestroyEntity/CreateEntity
            }
            return false;
        }
        
        private bool HasChunkChanged(IComponentTable table, int chunkIndex, int chunkCapacity, uint sinceVersion)
        {
            int startId = chunkIndex * chunkCapacity;
            int endId = startId + chunkCapacity;
            
            // We can't iterate table entities directly easily without access to internal array.
            // But IComponentTable.GetVersionForEntity works.
            for (int i = startId; i < endId; i++)
            {
                if (table.GetVersionForEntity(i) > sinceVersion) return true;
            }
            return false;
        }
        
        // Helpers
        private int GetCapacityForType(Type type)
        {
            // Invoke generic FdpConfig.GetChunkCapacity<T> via reflection
            // Or use the formula directly since we know CHUNK_SIZE_BYTES
            if (type.IsValueType)
            {
                 int size = System.Runtime.InteropServices.Marshal.SizeOf(type); // Safe for unmanaged
                 return FdpConfig.CHUNK_SIZE_BYTES / size;
            }
            throw new InvalidOperationException($"Cannot determine chunk capacity for managed type {type.Name} in unmanaged path.");
        }
        
        private void FillLiveness(EntityIndex index, int startId, int count, Span<bool> liveness)
        {
            int max = index.MaxIssuedIndex;
            int end = startId + count;
            for (int i = startId; i < end; i++)
            {
                bool alive = false;
                if (i >= MinRecordableId && i <= max)
                {
                    ref var header = ref index.GetHeader(i);
                    alive = header.IsActive;
                    // Apply entity filter when set — uses full Entity handle (index + generation)
                    // to allow callers to validate the generation and avoid filtering recycled slots.
                    if (alive && EntityFilter != null)
                        alive = EntityFilter(new Entity(i, header.Generation));
                }
                liveness[i - startId] = alive;
            }
        }

        private BitMask256 GetRecordableMask()
        {
            var mask = new BitMask256();
            var ids = ComponentTypeRegistry.GetRecordableTypeIds();
            foreach (var id in ids) mask.SetBit(id);
            return mask;
        }

        private unsafe void SanitizeHeadersMask(byte[] buffer, int bytesWritten, BitMask256 mask)
        {
            int headerSize = System.Runtime.CompilerServices.Unsafe.SizeOf<EntityHeader>();
            int count = bytesWritten / headerSize;
            
            fixed (byte* ptr = buffer)
            {
                EntityHeader* headers = (EntityHeader*)ptr;
                for (int i = 0; i < count; i++)
                {
                    // Intersect existing mask with snapshotable mask
                    headers[i].ComponentMask.BitwiseAnd(mask);
                }
            }
        }
        
        private bool HasActiveEntities(EntityIndex index, int startId, int count)
        {
            int max = index.MaxIssuedIndex;
            if (startId > max) return false;

            int eiCapacity = index.GetChunkCapacity();
            int startChunk = startId / eiCapacity;
            int endChunk = (Math.Min(startId + count - 1, max)) / eiCapacity; // Clamp to max issued
            
            for (int c = startChunk; c <= endChunk; c++)
            {
                 if (index.GetChunkPopulation(c) > 0) return true;
            }
            
            return false;
        }

        private static int RecordManagedTableAdapter<T>(object tableObj, EntityIndex index, BinaryWriter writer, uint tick) where T : class 
        {
            return RecordManagedTable((ManagedComponentTable<T>)tableObj, index, writer, tick);
        }

        private static int RecordManagedTable<T>(ManagedComponentTable<T> table, EntityIndex entityIndex, BinaryWriter writer, uint prevTick) where T : class
        {
            int chunkCount = 0;
            int maxEntities = entityIndex.MaxIssuedIndex;
            int chunkSize = table.ChunkCapacity;
            int maxChunks = (maxEntities / chunkSize) + 1;

            for (int c = 0; c < maxChunks; c++)
            {
                // Check if chunk has changed since prevTick
                // ManagedTable stores ChunkVersions
                uint chunkVersion = table.GetChunkVersion(c);
                if (chunkVersion <= prevTick) continue;

                if (!table.IsChunkAllocated(c)) continue;

                int startId = c * chunkSize;
                
                // Serialize the chunk (T?[])
                T?[] chunkData = new T?[chunkSize];
                for (int i = 0; i < chunkSize; i++)
                {
                    int entityId = startId + i;
                    if (entityId > maxEntities) break;

                    ref var header = ref entityIndex.GetHeader(entityId);
                    if (header.IsActive && header.ComponentMask.IsSet(table.ComponentTypeId))
                    {
                         chunkData[i] = table.GetRO(entityId);
                    }
                    else
                    {
                        chunkData[i] = null;
                    }
                }

                // Optimization: We write even safely "empty" chunks if they track a dirty version to clear data on restore.
                // But typically if hasData=false and it was previously empty, version wouldn't change.

                using (var ms = new MemoryStream())
                using (var chunkWriter = new BinaryWriter(ms))
                {
                    FdpAutoSerializer.Serialize(chunkData, chunkWriter);
                    byte[] serialized = ms.ToArray();

                    if (serialized.Length > 0)
                    {
                        chunkCount++;
                        writer.Write(c);
                        writer.Write(1);
                        writer.Write(table.ComponentTypeId);
                        writer.Write(serialized.Length);
                        writer.Write(serialized);
                    }
                }
            }
            return chunkCount;
        }

        /// <summary>
        /// Writes events from the event bus to the binary writer.
        /// Captures events from the Pending buffer (events that just happened).
        /// Format: [StreamCount] then for each stream: [TypeID][ElementSize][Count][Data]
        /// This follows FDP-DES-011 specification.
        /// </summary>
        // Reusable lists for event recording (Zero-Alloc)
        private readonly List<INativeEventStream> _cachedNativeStreams = new();
        private readonly List<IManagedEventStreamInfo> _cachedManagedStreams = new();

        /// <summary>
        /// Writes all events from the event bus to the recorder.
        /// Zero-allocation on hot path.
        /// </summary>
        // Caching for Event Filtering
        private readonly Dictionary<int, bool> _eventPolicyCache = new();

        private bool ShouldRecordEvent(Type eventType, int typeId)
        {
            if (_eventPolicyCache.TryGetValue(typeId, out bool shouldRecord))
                return shouldRecord;

            var attr = (Fdp.Kernel.DataPolicyAttribute?)Attribute.GetCustomAttribute(eventType, typeof(Fdp.Kernel.DataPolicyAttribute));

            shouldRecord = true;
            if (attr != null && attr.Policy.HasFlag(Fdp.Kernel.DataPolicy.NoRecord))
            {
                shouldRecord = false;
            }

            _eventPolicyCache[typeId] = shouldRecord;
            return shouldRecord;
        }

        private void WriteEvents(BinaryWriter writer, FdpEventBus? eventBus)
        {
            if (eventBus == null)
            {
                // No event bus provided - write empty event blocks
                writer.Write(0); // unmanagedStreamCount = 0
                writer.Write(0); // managedStreamCount = 0
                return;
            }

            // Get all streams with pending events (Zero-Alloc using population)
            eventBus.PopulatePendingStreams(_cachedNativeStreams);
            
            // Filter Unmanaged
            int validCount = 0;
            foreach(var s in _cachedNativeStreams) {
                Type? type = (s as IEventStreamInspector)?.EventType;
                if (ShouldRecordEventInternal(type, s.EventTypeId)) validCount++;
            }
            
            writer.Write(validCount);

            foreach (var stream in _cachedNativeStreams)
            {
                Type? type = (stream as IEventStreamInspector)?.EventType;
                if (!ShouldRecordEventInternal(type, stream.EventTypeId)) continue;

                writer.Write(stream.EventTypeId);
                writer.Write(stream.ElementSize);  // CRITICAL: Store element size for replay!

                ReadOnlySpan<byte> eventBytes = stream.GetPendingBytes();
                int count = eventBytes.Length / stream.ElementSize;
                
                writer.Write(count);  // Event count
                writer.Write(eventBytes);  // Raw data
            }
            
            // ========== MANAGED EVENTS ==========
            // Write managed events with type name for auto-recreation
            eventBus.PopulatePendingManagedStreams(_cachedManagedStreams);
            
            // Filter Managed
            int validManagedCount = 0;
            foreach (var streamInfo in _cachedManagedStreams) {
                if (ShouldRecordEventInternal(streamInfo.EventType, streamInfo.TypeId)) validManagedCount++;
            }
            
            writer.Write(validManagedCount);
            
            foreach (var streamInfo in _cachedManagedStreams)
            {
                if (!ShouldRecordEventInternal(streamInfo.EventType, streamInfo.TypeId)) continue;

                writer.Write(streamInfo.TypeId);
                writer.Write(0);  // ElementSize = 0 indicates Managed
                
                // ------------------------------------------------------------
                // [New] Block Size Tracking (Format Version 2)
                // Format: [BlockBytes: int] [TypeName] [Count] [Data...]
                // ------------------------------------------------------------
                
                // 1. Remember position of the size placeholder
                writer.Flush();
                long sizeFieldPos = writer.BaseStream.Position;
                
                // 2. Write Placeholder (0)
                writer.Write((int)0);
                
                // 3. Track start of data payload
                long payloadStartPos = writer.BaseStream.Position;

                // 4. Write Data (TypeName + Count + Events)
                writer.Write(streamInfo.EventType.AssemblyQualifiedName!);
                writer.Write(streamInfo.PendingEvents.Count);
                
                // Get serializer for this type (cached reflection)
                var serializerMethod = typeof(FdpAutoSerializer)
                    .GetMethod(nameof(FdpAutoSerializer.Serialize), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(streamInfo.EventType);
                
                var args = new object[2];
                args[1] = writer;

                foreach (var evt in streamInfo.PendingEvents)
                {
                    // FdpAutoSerializer.Serialize<T>(evt, writer);
                    args[0] = evt;
                    serializerMethod.Invoke(null, args);
                }
                
                // 5. Calculate Size
                writer.Flush();
                long payloadEndPos = writer.BaseStream.Position;
                int blockSize = (int)(payloadEndPos - payloadStartPos);
                
                // 6. Seek back and Patch
                writer.BaseStream.Position = sizeFieldPos;
                writer.Write(blockSize);
                
                // 7. Seek forward to continue
                writer.BaseStream.Position = payloadEndPos;
            }
        }


        private bool ShouldRecordEventInternal(Type? type, int typeId)
        {
             if (type == null) return true;
             
             var attr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DataPolicyAttribute>(type);
             if (attr != null)
             {
                 return !attr.Policy.HasFlag(DataPolicy.NoRecord);
             }
             return true;
        }

        private void RecordSingletons(EntityRepository repo, BinaryWriter writer, uint prevTick)
        {
            var tables = repo.GetSingletonTables();
            
            // We need to write the count first, but we don't know how many are dirty.
            // So we write a placeholder, write data, then patch count.
            long countPos = writer.BaseStream.Position;
            writer.Write(0); // Placeholder
            int actualCount = 0;

            foreach (var table in tables)
            {
                if (!ComponentTypeRegistry.IsRecordable(table.ComponentTypeId)) continue;

                // Check version (Singletons are always in Chunk 0)
                if (table.GetVersionForEntity(0) <= prevTick) 
                    continue;

                int typeId = table.ComponentTypeId;

                // Serialize based on Tier 1 vs Tier 2
                if (table is IUnmanagedComponentTable unmanaged)
                {
                    // Unmanaged: Copy raw bytes
                    int bytes = unmanaged.CopyChunkToBuffer(0, _scratchBuffer);
                    if (bytes > 0)
                    {
                        actualCount++;
                        writer.Write(typeId);
                        writer.Write(bytes);
                        writer.Write(_scratchBuffer, 0, bytes);
                    }
                }
                else
                {
                    // Managed: Use Serializer (Reflection for generic access)
                    // Accessing index 0 (Singleton)
                    object? val = table.GetRawObject(0);
                    
                    if (val != null)
                    {
                        using (var ms = new MemoryStream())
                        using (var bw = new BinaryWriter(ms))
                        {
                            FdpAutoSerializer.Serialize((dynamic)val, bw);
                            byte[] data = ms.ToArray();
                            
                            actualCount++;
                            writer.Write(typeId);
                            writer.Write(data.Length);
                            writer.Write(data);
                        }
                    }
                }
            }

            // Patch count
            long endPos = writer.BaseStream.Position;
            writer.BaseStream.Position = countPos;
            writer.Write(actualCount);
            writer.BaseStream.Position = endPos;
        }

        /// <summary>
        /// Sanitizes the scratch buffer by zeroing out data for dead entities.
        /// Does NOT modify the live component table.
        /// </summary>
        private unsafe void SanitizeScratchBuffer(byte[] buffer, int bytesWritten, int entitySize, ReadOnlySpan<bool> liveness)
        {
            fixed (byte* ptr = buffer)
            {
                // Iterate only up to what fits in liveness or bytesWritten
                // Note: liveness.Length is typically ChunkCapacity.
                for (int i = 0; i < liveness.Length; i++)
                {
                    // If the entity is dead, zero out its slot in the scratch buffer
                    if (!liveness[i])
                    {
                        int offset = i * entitySize;
                        
                        // Buffer safety check
                        if (offset + entitySize <= bytesWritten)
                        {
                            System.Runtime.CompilerServices.Unsafe.InitBlock(ptr + offset, 0, (uint)entitySize);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Interface for unmanaged component tables that support raw chunk operations.
    /// </summary>
    public interface IUnmanagedComponentTable
    {
        void SanitizeChunk(int chunkIndex, ReadOnlySpan<bool> livenessMap);
        int CopyChunkToBuffer(int chunkIndex, Span<byte> destination);
        void RestoreChunkFromBuffer(int chunkIndex, ReadOnlySpan<byte> source);
    }
}
