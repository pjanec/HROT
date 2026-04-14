using System;
using System.Runtime.CompilerServices;
using ModuleHost.Core.Abstractions;

namespace Fdp.Kernel
{
    /// <summary>
    /// Records structural changes (create, destroy, add/remove components) for deferred execution.
    /// Thread-safe for recording (each thread should have its own buffer).
    /// Playback must be done on the main thread after parallel work completes.
    /// </summary>
    public unsafe class EntityCommandBuffer : IEntityCommandBuffer, IDisposable
    {

        private enum OpCode : byte
        {
            CreateEntity = 0,
            DestroyEntity = 1,
            AddUnmanagedComponent = 2,
            RemoveUnmanagedComponent = 3,
            SetUnmanagedComponent = 4,
            AddManagedComponent = 5,
            RemoveManagedComponent = 6,
            SetManagedComponent = 7,
            PublishUnmanagedEvent = 8,
            PublishManagedEvent = 9,
            SetLifecycleState = 10
        }
        
        // Simple byte buffer for command stream
        private byte[] _buffer;
        private int _position;
        private const int InitialCapacity = 4096;
        private const int MaxComponentSize = 1024; // Sanity check
        
        // Separate storage for managed component objects
        private System.Collections.Generic.List<object> _managedObjects;
        
        public EntityCommandBuffer(int initialCapacity = InitialCapacity)
        {
            _buffer = new byte[initialCapacity];
            _position = 0;
            _managedObjects = new System.Collections.Generic.List<object>();
        }
        
        // ============================================
        // RECORDING API (Thread-safe per buffer)
        // ============================================
        
        /// <summary>
        /// Records a CreateEntity command.
        /// Returns a placeholder Entity that will be mapped during playback.
        /// </summary>
        public Entity CreateEntity()
        {
            EnsureCapacity(1);
            _buffer[_position++] = (byte)OpCode.CreateEntity;
            
            // Return a placeholder entity with negative index
            // Will be remapped during playback
            return new Entity(-(++_createCounter), 0);
        }
        
        private int _createCounter = 0;
        
        /// <summary>
        /// Records a DestroyEntity command.
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            EnsureCapacity(1 + 8); // OpCode + Entity.Id (ulong)
            _buffer[_position++] = (byte)OpCode.DestroyEntity;
            WriteEntity(entity);
        }
        
        /// <summary>
        /// Records an AddComponent command with the specified value.
        /// </summary>
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            int componentSize = Unsafe.SizeOf<T>();
            if (componentSize > MaxComponentSize)
                throw new ArgumentException($"Component size {componentSize} exceeds maximum {MaxComponentSize}");
            
            int typeId = ComponentType<T>.ID;
            EnsureCapacity(1 + 8 + 4 + 4 + componentSize); // OpCode + Entity + TypeID + Size + Data
            
            _buffer[_position++] = (byte)OpCode.AddUnmanagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
            WriteInt(componentSize);
            WriteComponent(component);
        }
        
        /// <summary>
        /// Records a SetComponent command (adds or updates).
        /// </summary>
        public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            int componentSize = Unsafe.SizeOf<T>();
            if (componentSize > MaxComponentSize)
                throw new ArgumentException($"Component size {componentSize} exceeds maximum {MaxComponentSize}");
            
            int typeId = ComponentType<T>.ID;
            EnsureCapacity(1 + 8 + 4 + 4 + componentSize);
            
            _buffer[_position++] = (byte)OpCode.SetUnmanagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
            WriteInt(componentSize);
            WriteComponent(component);
        }
        
        /// <summary>
        /// Records a RemoveComponent command.
        /// </summary>
        public void RemoveComponent<T>(Entity entity) where T : unmanaged
        {
            int typeId = ComponentType<T>.ID;
            EnsureCapacity(1 + 8 + 4); // OpCode + Entity + TypeID
            
            _buffer[_position++] = (byte)OpCode.RemoveUnmanagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
        }
        
        // ============================================
        // MANAGED COMPONENT RECORDING
        // ============================================
        
        /// <summary>
        /// Records an AddManagedComponent command with the specified value.
        /// 
        /// IMPORTANT: Stores the component by REFERENCE (not copied).
        /// Always pass a fresh object instance:
        ///   ✅ GOOD: ecb.AddManagedComponent(e, new PlayerName { Name = "Hero" });
        ///   ❌ BAD:  var shared = new PlayerName(); ecb.AddManagedComponent(e1, shared); ecb.AddManagedComponent(e2, shared);
        /// 
        /// Do not mutate the object after recording - changes will affect playback.
        /// This is the same as using EntityRepository.AddManagedComponent directly.
        /// </summary>
        public void AddManagedComponent<T>(Entity entity, T? component) where T : class
        {
            int typeId = ManagedComponentType<T>.ID;
            
            int objectIndex = _managedObjects.Count;
            _managedObjects.Add(component!);
            
            EnsureCapacity(1 + 8 + 4 + 4); // OpCode + Entity + TypeID + ObjectIndex
            
            _buffer[_position++] = (byte)OpCode.AddManagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
            WriteInt(objectIndex);
        }
        
        /// <summary>
        /// Records a SetManagedComponent command (adds or updates).
        /// 
        /// IMPORTANT: Stores the component by REFERENCE (not copied).
        /// Always pass a fresh object instance - see AddManagedComponent for details.
        /// </summary>
        public void SetManagedComponent<T>(Entity entity, T? component) where T : class
        {
            int typeId = ManagedComponentType<T>.ID;
            
            int objectIndex = _managedObjects.Count;
            _managedObjects.Add(component!);
            
            EnsureCapacity(1 + 8 + 4 + 4); // OpCode + Entity + TypeID + ObjectIndex
            
            _buffer[_position++] = (byte)OpCode.SetManagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
            WriteInt(objectIndex);
        }
        
        /// <summary>
        /// Records a RemoveManagedComponent command.
        /// </summary>
        public void RemoveManagedComponent<T>(Entity entity) where T : class
        {
            int typeId = ManagedComponentType<T>.ID;
            EnsureCapacity(1 + 8 + 4); // OpCode + Entity + TypeID
            
            _buffer[_position++] = (byte)OpCode.RemoveManagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
        }
        
        /// <summary>
        /// Records a PublishEvent command.
        /// </summary>
        public void PublishEvent<T>(in T evt) where T : unmanaged
        {
            int componentSize = Unsafe.SizeOf<T>();
            if (componentSize > MaxComponentSize)
                throw new ArgumentException($"Event size {componentSize} exceeds maximum {MaxComponentSize}");
            
            int typeId = EventType<T>.Id;
            EnsureCapacity(1 + 4 + 4 + componentSize); // OpCode + TypeID + Size + Data
            
            _buffer[_position++] = (byte)OpCode.PublishUnmanagedEvent;
            WriteInt(typeId);
            WriteInt(componentSize);
            WriteComponent(evt);
        }

        public void SetComponentRaw(Entity entity, int typeId, void* ptr, int size)
        {
            if (size > MaxComponentSize)
                throw new ArgumentException($"Component size {size} exceeds maximum {MaxComponentSize}");

            EnsureCapacity(1 + 8 + 4 + 4 + size); // OpCode + Entity + TypeID + Size + Data
            
            _buffer[_position++] = (byte)OpCode.SetUnmanagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
            WriteInt(size);
            
            // Raw copy
            fixed (byte* dest = &_buffer[_position])
            {
                Unsafe.CopyBlock(dest, ptr, (uint)size);
            }
            _position += size;
        }

        public void SetManagedComponentRaw(Entity entity, int typeId, object obj)
        {
            int objectIndex = _managedObjects.Count;
            _managedObjects.Add(obj);
            
            EnsureCapacity(1 + 8 + 4 + 4); // OpCode + Entity + TypeID + ObjectIndex
            
            _buffer[_position++] = (byte)OpCode.SetManagedComponent;
            WriteEntity(entity);
            WriteInt(typeId);
            WriteInt(objectIndex);
        }

        /// <summary>
        /// Records a PublishManagedEvent command.
        /// Use this for class-based events.
        /// </summary>
        public void PublishManagedEvent<T>(T evt) where T : class
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            
            int typeId = typeof(T).FullName!.GetHashCode() & 0x7FFFFFFF;
            int objectIndex = _managedObjects.Count;
            _managedObjects.Add(evt);
            
            EnsureCapacity(1 + 4 + 4); // OpCode + TypeID + ObjectIndex
            
            _buffer[_position++] = (byte)OpCode.PublishManagedEvent;
            WriteInt(typeId);
            WriteInt(objectIndex);
        }
        
        /// <summary>
        /// Records a SetLifecycleState command.
        /// </summary>
        public void SetLifecycleState(Entity entity, EntityLifecycle state)
        {
            EnsureCapacity(1 + 8 + 1); // OpCode + Entity + State
            
            _buffer[_position++] = (byte)OpCode.SetLifecycleState;
            WriteEntity(entity);
            _buffer[_position++] = (byte)state;
        }
        
        // ============================================
        // PLAYBACK API (Main thread only!)
        // ============================================
        
        /// <summary>
        /// Executes all recorded commands on the given repository.
        /// Must be called on the main thread.
        /// </summary>
        public void Playback(EntityRepository repo)
        {
            if (repo == null)
                throw new ArgumentNullException(nameof(repo));
            
            int readPos = 0;
            
            // Map placeholder entities (negative indices) to real entities
            var entityRemap = new System.Collections.Generic.Dictionary<int, Entity>();
            
            while (readPos < _position)
            {
                OpCode op = (OpCode)_buffer[readPos++];
                
                switch (op)
                {
                    case OpCode.CreateEntity:
                    {
                        Entity newEntity = repo.CreateEntity();
                        entityRemap[-(entityRemap.Count + 1)] = newEntity;
                        break;
                    }
                    
                    case OpCode.DestroyEntity:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        if (repo.IsAlive(entity))
                        {
                            repo.DestroyEntity(entity);
                        }
                        break;
                    }
                    
                    case OpCode.AddUnmanagedComponent:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        int typeId = ReadInt(ref readPos);
                        int size = ReadInt(ref readPos);
                        
                        if (repo.IsAlive(entity))
                        {
                            fixed (byte* dataPtr = &_buffer[readPos])
                            {
                                repo.AddComponentRaw(entity, typeId, (IntPtr)dataPtr, size);
                            }
                        }
                        
                        readPos += size;
                        break;
                    }
                    
                    case OpCode.SetUnmanagedComponent:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        int typeId = ReadInt(ref readPos);
                        int size = ReadInt(ref readPos);
                        
                        if (repo.IsAlive(entity))
                        {
                            fixed (byte* dataPtr = &_buffer[readPos])
                            {
                                repo.SetComponentRawFast(entity, typeId, (IntPtr)dataPtr, size);
                            }
                        }
                        
                        readPos += size;
                        break;
                    }
                    
                    case OpCode.RemoveUnmanagedComponent:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        int typeId = ReadInt(ref readPos);
                        
                        if (repo.IsAlive(entity))
                        {
                            repo.RemoveComponentRaw(entity, typeId);
                        }
                        break;
                    }
                    
                    case OpCode.AddManagedComponent:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        int typeId = ReadInt(ref readPos);
                        int objectIndex = ReadInt(ref readPos);
                        
                        if (repo.IsAlive(entity))
                        {
                            object obj = _managedObjects[objectIndex];
                            repo.AddManagedComponentRaw(entity, typeId, obj);
                        }
                        break;
                    }
                    
                    case OpCode.SetManagedComponent:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        int typeId = ReadInt(ref readPos);
                        int objectIndex = ReadInt(ref readPos);
                        
                        if (repo.IsAlive(entity))
                        {
                            object obj = _managedObjects[objectIndex];
                            repo.SetManagedComponentRaw(entity, typeId, obj);
                        }
                        break;
                    }
                    
                    case OpCode.RemoveManagedComponent:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        int typeId = ReadInt(ref readPos);
                        
                        if (repo.IsAlive(entity))
                        {
                            repo.RemoveManagedComponentRaw(entity, typeId);
                        }
                        break;
                    }
                    
                    case OpCode.PublishUnmanagedEvent:
                    {
                        int typeId = ReadInt(ref readPos);
                        int size = ReadInt(ref readPos);
                        
                        fixed (byte* dataPtr = &_buffer[readPos])
                        {
                            repo.Bus.PublishRaw(typeId, dataPtr);
                        }
                        
                        readPos += size;
                        break;
                    }
                    
                    case OpCode.PublishManagedEvent:
                    {
                        int typeId = ReadInt(ref readPos);
                        int objectIndex = ReadInt(ref readPos);
                        
                        object evt = _managedObjects[objectIndex];
                        repo.Bus.PublishManagedRaw(typeId, evt);
                        break;
                    }

                    case OpCode.SetLifecycleState:
                    {
                        Entity entity = ReadEntity(ref readPos, entityRemap);
                        EntityLifecycle state = (EntityLifecycle)_buffer[readPos++];
                        
                        if (repo.IsAlive(entity))
                        {
                            repo.SetLifecycleState(entity, state);
                        }
                        break;
                    }
                    
                    default:
                        throw new InvalidOperationException($"Unknown opcode: {op}");
                }
            }
            
            // Clear buffer after playback
            Clear();
        }
        
        /// <summary>
        /// Clears all recorded commands without executing them.
        /// </summary>
        public void Clear()
        {
            _position = 0;
            _createCounter = 0;
            _managedObjects.Clear();
        }
        
        /// <summary>
        /// Gets the number of bytes used in the command buffer.
        /// </summary>
        public int Size => _position;
        
        /// <summary>
        /// Gets whether the buffer has any recorded commands.
        /// </summary>
        public bool IsEmpty => _position == 0;

        /// <summary>
        /// Gets whether the buffer has any recorded commands.
        /// </summary>
        public bool HasCommands => _position > 0;
        
        // ============================================
        // INTERNAL HELPERS
        // ============================================
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int additionalBytes)
        {
            int required = _position + additionalBytes;
            if (required > _buffer.Length)
            {
                int newSize = Math.Max(_buffer.Length * 2, required);
                Array.Resize(ref _buffer, newSize);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteEntity(Entity entity)
        {
            ulong packed = entity.PackedValue;
            fixed (byte* ptr = &_buffer[_position])
            {
                *(ulong*)ptr = packed;
            }
            _position += 8;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteInt(int value)
        {
            fixed (byte* ptr = &_buffer[_position])
            {
                *(int*)ptr = value;
            }
            _position += 4;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteComponent<T>(in T component) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            fixed (byte* dst = &_buffer[_position])
            {
                Unsafe.Copy(dst, ref Unsafe.AsRef(in component));
            }
            _position += size;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Entity ReadEntity(ref int readPos, System.Collections.Generic.Dictionary<int, Entity> remap)
        {
            ulong packed;
            fixed (byte* ptr = &_buffer[readPos])
            {
                packed = *(ulong*)ptr;
            }
            readPos += 8;
            
            Entity entity = new Entity(packed);
            
            // Check if this is a placeholder entity (negative index)
            if (entity.Index < 0 && remap.TryGetValue(entity.Index, out Entity remapped))
            {
                return remapped;
            }
            
            return entity;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadInt(ref int readPos)
        {
            int value;
            fixed (byte* ptr = &_buffer[readPos])
            {
                value = *(int*)ptr;
            }
            readPos += 4;
            return value;
        }
        
        public void Dispose()
        {
            _buffer = null!;
        }
    }
}
