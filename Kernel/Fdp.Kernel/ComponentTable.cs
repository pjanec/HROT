using System;
using System.Runtime.CompilerServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Stores components of type T using NativeChunkTable.
    /// Provides O(1) access to components by entity index.
    /// </summary>
    public sealed class ComponentTable<T> : IComponentTable, FlightRecorder.IUnmanagedComponentTable where T : unmanaged
    {
        private readonly NativeChunkTable<T> _data;
        private readonly int _componentTypeId;
        
        public ComponentTable()
        {
            _data = new NativeChunkTable<T>();
            _componentTypeId = ComponentType<T>.ID;
        }
        
        public int ComponentTypeId => _componentTypeId;
        public Type ComponentType => typeof(T);
        public int ComponentSize => ComponentType<T>.Size;

        // NEW: Type-erased setter
        public void SetRawObject(int index, object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            
            // Fast cast - type safety guaranteed by caller logic or via InvalidCastException
            _data[index] = (T)value;
        }
        
        // NEW: Type-erased getter
        public object GetRawObject(int index)
        {
            // Box the struct
            return _data[index];
        }

        public void ClearRaw(int index)
        {
            // For unmanaged components, clearing data is optional as mask handles validity.
            // But for consistency we could zero it. 
            // However, FDP design says "Component data is not explicitly cleared".
            // So we can leave it empty or zero it.
            // Let's match current behavior: do nothing. Mask is what matters.
        }

        public void Clear()
        {
            // Unmanaged data doesn't trigger GC issues and is guarded by ComponentMask.
            // Clearing it is expensive and unnecessary.
        }
        
        /// <summary>
        /// Efficiently checks if this table has been modified since the specified version.
        /// </summary>
        public bool HasChanges(uint sinceVersion)
        {
            return _data.HasChanges(sinceVersion);
        }

        public uint GetVersionForEntity(int entityId)
        {
            int chunkIndex = entityId / _data.ChunkCapacity;
            return _data.GetChunkVersion(chunkIndex);
        }
        
        /// <summary>
        /// Gets reference to component for entity at given index.
        /// Does NOT validate if component exists - caller must check ComponentMask.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityIndex)
        {
            return ref _data[entityIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void* GetRawPointer(int entityIndex)
        {
            return Unsafe.AsPointer(ref _data.GetRefRW(entityIndex, 0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRW(int entityIndex, uint version)
        {
            return ref _data.GetRefRW(entityIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetRO(int entityIndex)
        {
            return ref _data.GetRefRO(entityIndex);
        }
        
        /// <summary>
        /// Sets component value for entity at given index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityIndex, in T component, uint version)
        {
            _data.GetRefRW(entityIndex, version) = component;
        }
        
        /// <summary>
        /// Legacy/Test helper. Sets with version 0 (no change tracking).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityIndex, in T component)
        {
            Set(entityIndex, component, 0);
        }
        
        /// <summary>
        /// Gets a Span over the specified chunk's data.
        /// Advanced API for heavy optimization.
        /// </summary>
        public Span<T> GetSpan(int chunkIndex) => _data.GetChunkSpan(chunkIndex);

        /// <summary>
        /// Gets the underlying chunk table (for advanced usage).
        /// </summary>
        public NativeChunkTable<T> GetChunkTable() => _data;
        
        /// <summary>
        /// Sets component from raw bytes (used by EntityCommandBuffer).
        /// </summary>
        public unsafe void SetRaw(int entityIndex, IntPtr dataPtr, int size, uint version)
        {
            if (size != Unsafe.SizeOf<T>())
                throw new ArgumentException($"Size mismatch: expected {Unsafe.SizeOf<T>()} but got {size}");
            
            ref T dest = ref _data.GetRefRW(entityIndex, version);
            void* destPtr = Unsafe.AsPointer(ref dest);
            Buffer.MemoryCopy((void*)dataPtr, destPtr, size, size);
        }
        
        public byte[] Serialize(EntityRepository repo, MessagePack.MessagePackSerializerOptions options)
        {
            var dataToSave = new System.Collections.Generic.List<EntityComponentPair<T>>();
            var index = repo.GetEntityIndex();
            int max = index.MaxIssuedIndex;
            
            for (int i = 0; i <= max; i++)
            {
                 ref var header = ref index.GetHeader(i);
                 if (header.IsActive && header.ComponentMask.IsSet(_componentTypeId))
                 {
                      dataToSave.Add(new EntityComponentPair<T> { EntityId = i, Value = Get(i) });
                 }
            }

            return MessagePack.MessagePackSerializer.Serialize(dataToSave, options);
        }

        public void Deserialize(EntityRepository repo, byte[] data, MessagePack.MessagePackSerializerOptions options)
        {
            var loadedData = MessagePack.MessagePackSerializer.Deserialize<System.Collections.Generic.List<EntityComponentPair<T>>>(data, options);
            
            foreach (var item in loadedData)
            {
                // Set with version 0 (fresh load)
                this.Set(item.EntityId, item.Value, 0); 
                
                // IMPORTANT: We must ensure the component mask is updated for this entity!
                // However, the caller (RepositorySerializer) handles mask reconstruction differently?
                // Step 5 says: "Deserialize method calls .Set() which updates masks/headers automatically in your kernel"
                // ... Wait. ComponentTable.Set() just sets DATA. It does NOT update the ComponentMask in the EntityHeader.
                // Repository.SetUnmanagedComponent() updates the mask.
                // BUT, ComponentTable.Set() is low-level.
                // IF we use `repo.SetComponent(new Entity(id, gen), val)`, it would be cleaner, but we need the generation.
                // Since this is LOAD, we might not have the generation handy unless we look it up.
                // The `Load` method in Step 5 already restored entities (active/generation).
                // So we can look up generation.
                
                // Re-reading Step 5: "Rebuild ComponentMasks for entities as we go (The Deserialize method calls .Set() which updates masks/headers automatically in your kernel)"
                // This implies the user THINKS .Set() updates masks. It DOES NOT in Fdp.Kernel.
                // `ComponentTable.Set` only touches `_data`.
                // `EntityRepository.SetUnmanagedComponent` calls `table.Set` AND updates `header.ComponentMask`.
                
                // SO: I should explicitly update the mask here, OR rely on `repo` to do it.
                // Since I have `repo`, I can access the header.
                
                ref var header = ref repo.GetHeader(item.EntityId);
                header.ComponentMask.SetBit(_componentTypeId);
            }
        }
        
        // ================================================
        // FLIGHT RECORDER SUPPORT
        // ================================================
        
        /// <summary>
        /// Sanitizes a chunk by zeroing dead entity slots.
        /// Required by IUnmanagedComponentTable for Flight Recorder.
        /// </summary>
        public void SanitizeChunk(int chunkIndex, ReadOnlySpan<bool> livenessMap)
        {
            _data.SanitizeChunk(chunkIndex, livenessMap);
        }
        
        /// <summary>
        /// Copies raw chunk data to a buffer.
        /// Required by IUnmanagedComponentTable for Flight Recorder.
        /// </summary>
        public int CopyChunkToBuffer(int chunkIndex, Span<byte> destination)
        {
            return _data.CopyChunkToBuffer(chunkIndex, destination);
        }
        
        /// <summary>
        /// Restores chunk data from a buffer.
        /// Required by IUnmanagedComponentTable for Flight Recorder playback.
        /// </summary>
        public void RestoreChunkFromBuffer(int chunkIndex, ReadOnlySpan<byte> source)
        {
            _data.RestoreChunkFromBuffer(chunkIndex, source);
        }
        
        public void Dispose()
        {
            _data?.Dispose();
        }

        public void SyncFrom(IComponentTable source)
        {
            if (source is ComponentTable<T> typedSource)
            {
                _data.SyncDirtyChunks(typedSource._data);
            }
            #if FDP_PARANOID_MODE
            else
            {
                throw new ArgumentException($"Source table type mismatch. Expected {typeof(ComponentTable<T>).Name}, got {source.GetType().Name}");
            }
            #endif
        }
    }

    [MessagePack.MessagePackObject]
    public struct EntityComponentPair<T>
    {
        [MessagePack.Key(0)] public int EntityId;
        [MessagePack.Key(1)] public T Value;
    }
}
