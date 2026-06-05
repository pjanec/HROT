using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core.Internal;

namespace Fdp.Core
{
    public enum TimeSliceMetric
    {
        WallClockTime,
        EntityCount
    }

    /// <summary>
    /// Main ECS repository managing entities and their components.
    /// Provides high-level API for entity/component operations.
    /// Thread-safe for entity creation/destruction.
    /// Component access is NOT thread-safe (by design for performance).
    /// </summary>
    public sealed partial class EntityRepository : IDisposable
    {
        private readonly EntityIndex _entityIndex;
        private readonly Dictionary<Type, IComponentTable> _componentTables;
        private IComponentTable?[] _tableCache = new IComponentTable[FdpConfig.MAX_COMPONENT_TYPES];
        private readonly ComponentMetadataTable _metadata;
        private readonly object _tableLock = new object();
        private bool _disposed;
        private BitMask512 _borrowedSingletons;

        // Allocated once at construction; freed in Dispose.
        // Provides an unmanaged IntPtr that HSM action delegates (via HsmKernelBridge.WorldHandle)
        // use to recover this EntityRepository through the Fhsm.Kernel unmanaged constraint.
        // See DEBT-007-HSM-ANALYSIS.md for full explanation.
        private GCHandle _selfHandle;
        
        // Stage 21: Lifecycle Event Stream
        private NativeEventStream<EntityLifecycleEvent>? _lifecycleStream;
        
        public void RegisterLifecycleStream(NativeEventStream<EntityLifecycleEvent> stream)
        {
            _lifecycleStream = stream;
        }
        
        // Event Bus for Module Communication (Batch 2)
        public FdpEventBus Bus { get; }

        // Simulation Time for ISimulationView (Batch 2)
        private float _simulationTime;
        public float SimulationTime => _simulationTime;
        public void SetSimulationTime(float time) { _simulationTime = time; }
        
        
        // Flight Recorder Destruction Log
        private readonly List<Entity> _destructionLog = new List<Entity>(128);
        
        // Stage 18: Singleton Storage
        // Index = ComponentType<T>.ID
        // Value = NativeChunkTable<T> (capacity 1) or ManagedComponentTable<T> (capacity 1)
        private object[] _singletons;
        
        // Versioning for Delta Iterator (Stage 11/16)
        private uint _globalVersion = 1;
        
        /// <summary>
        /// Global configuration for Time Slicing metric. 
        /// Set to EntityCount for deterministic simulation.
        /// </summary>
        public TimeSliceMetric DefaultTimeSliceMetric { get; set; } = TimeSliceMetric.WallClockTime;
        
        // Stage 16: Phase System
        private Phase _currentPhase = Phase.Initialization;
        private PhasePermission _currentPhasePermission = PhasePermission.ReadWriteAll; // Cache
        public Phase CurrentPhase => _currentPhase;
        
        /// <summary>
        /// Configuration for phase transitions and permissions.
        /// Defaults to strict enforcement.
        /// </summary>
        private PhaseConfig _phaseConfig = PhaseConfig.Default;
        public PhaseConfig PhaseConfig 
        { 
            get => _phaseConfig;
            set
            {
                _phaseConfig = value;
                _phaseConfig?.BuildCache();  // Build ID-based cache for hot path
                UpdatePermissionCache();
            }
        }
        
        private void UpdatePermissionCache()
        {
            // HOT PATH: Use ID-based lookup
            if (_phaseConfig != null)
            {
                _currentPhasePermission = _phaseConfig.GetPermissionById(_currentPhase.Id);
            }
            else
            {
                _currentPhasePermission = PhasePermission.ReadWriteAll;
            }
        }
        
        public EntityRepository()
        {
            Bus = new FdpEventBus();
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            _entityIndex = new EntityIndex();
            _componentTables = new Dictionary<Type, IComponentTable>();
            _metadata = new ComponentMetadataTable();
            _singletons = new object[64]; // Start with 64 slots, will grow if needed
            _phaseConfig.BuildCache();  // Build ID cache at initialization
        }
        
        /// <summary>
        /// Raw unmanaged handle to this repository. Valid for passing through
        /// <c>unmanaged</c>-constrained contexts (e.g. <c>HsmKernelBridge</c>).
        /// Recover via: <c>(EntityRepository)GCHandle.FromIntPtr(handle).Target!</c>
        /// Remains valid until <see cref="Dispose"/> is called.
        /// </summary>
        public IntPtr UnmanagedHandle => GCHandle.ToIntPtr(_selfHandle);

        /// <summary>
        /// Total number of active entities.
        /// </summary>
        public int EntityCount => _entityIndex.ActiveCount;
        
        /// <summary>
        /// Highest entity index ever issued (for iteration bounds).
        /// </summary>
        public int MaxEntityIndex => _entityIndex.MaxIssuedIndex;
        
        /// <summary>
        /// Current global change version. Incremented by Tick().
        /// </summary>
        public uint GlobalVersion => _globalVersion;
        
        /// <summary>
        /// Increments the global version. Should be called at start of frame.
        /// </summary>
        public void Tick()
        {
            System.Threading.Interlocked.Increment(ref _globalVersion);
        }

        /// <summary>
        /// Force sets the global version. Used by PlaybackSystem.
        /// </summary>
        internal void SetGlobalVersion(uint version)
        {
            _globalVersion = version;
        }

        /// <summary>
        /// Resets the global version to the specified value (default: 1).
        /// Intended for test setup so that version-dependent assertions start from a known baseline,
        /// regardless of how many component registrations or internal ticks occurred during construction.
        /// </summary>
        public void ResetGlobalVersion(uint version = 1)
        {
            _globalVersion = version;
        }
        
        /// <summary>
        /// Advances or changes the current execution phase.
        /// </summary>
        public void SetPhase(Phase phase)
        {
            // HOT PATH: Use ID-based transition check (O(1) integer lookups)
            if (PhaseConfig != null && !PhaseConfig.IsTransitionValidById(_currentPhase.Id, phase.Id))
            {
                throw new InvalidOperationException(
                    $"Invalid phase transition: {_currentPhase} -> {phase}.");
            }
            
            _currentPhase = phase;
            
            // Cache permission for the new phase (called only on phase change, not hot path)
            UpdatePermissionCache();
        }
        
        /// <summary>
        /// Asserts that the engine is in the required phase.
        /// Throws WrongPhaseException if not.
        /// </summary>
        public void AssertPhase(Phase required)
        {
            if (_currentPhase != required)
                throw new WrongPhaseException(_currentPhase, required);
        }

        // ================================================
        // ENTITY LIFECYCLE
        // ================================================
        
        /// <summary>
        /// Reserves a range of entity IDs at the start of the index.
        /// Useful for ID partitioning strategies.
        /// </summary>
        public void ReserveIdRange(int maxId)
        {
            _entityIndex.ReserveIdRange(maxId);
        }

        /// <summary>
        /// Forces the creation of an entity at a specific ID and generation.
        /// Used by the Replay system to hydrate recorded entities or deterministic ghosts.
        /// Only use this when you guarantee the ID is free or you intend to overwrite it.
        /// </summary>
        public Entity HydrateEntity(int id, int generation)
        {
             // 1. Delegate to Index to handle allocation/state
             _entityIndex.ForceRestoreEntity(id, true, generation, default);
             
             // 2. Set default metadata
             ref var meta = ref _entityIndex.GetMetadata(id);
             meta.LifecycleState = EntityLifecycle.Active;
             meta.LastChangeTick = _globalVersion;
             
             var entity = new Entity(id, (ushort)generation);

             // 3. Emit Event
             if (_lifecycleStream != null)
             {
                _lifecycleStream.Write(new EntityLifecycleEvent {
                    Entity = entity,
                    Type = LifecycleEventType.Created,
                    Generation = generation
                });
             }
             
             return entity;
        }

        /// <summary>
        /// Creates a new entity.
        /// Thread-safe.
        /// </summary>
        public Entity CreateEntity()
        {
            var entity = _entityIndex.CreateEntity();
            ref var meta = ref _entityIndex.GetMetadata(entity.Index);
            meta.LastChangeTick = _globalVersion;
            meta.LifecycleState = EntityLifecycle.Active; // Default to Active
            
            // Emit Lifecycle Event
            if (_lifecycleStream != null)
            {
                _lifecycleStream.Write(new EntityLifecycleEvent {
                    Entity = entity,
                    Type = LifecycleEventType.Created,
                    Generation = (int)meta.Generation
                });
            }
            
            return entity;
        }

        /// <summary>
        /// Sets the lifecycle state of an entity.
        /// Used by ELM to transition from Constructing -> Active -> TearDown.
        /// </summary>
        public void SetLifecycleState(Entity entity, EntityLifecycle state)
        {
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            
            ref var meta = ref _entityIndex.GetMetadata(entity.Index);
            meta.LifecycleState = state;
            meta.LastChangeTick = _globalVersion;
        }

        /// <summary>
        /// Gets the lifecycle state of an entity.
        /// </summary>
        public EntityLifecycle GetLifecycleState(Entity entity)
        {
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            return _entityIndex.GetMetadata(entity.Index).LifecycleState;
        }

        /// <summary>
        /// Returns the list of entities destroyed since the last ClearDestructionLog().
        /// Used by the Recorder to write the "Destroyed" block.
        /// </summary>
        public IReadOnlyList<Entity> GetDestructionLog() => _destructionLog;
    
        /// <summary>
        /// Clears the log. Must be called AFTER the Recorder has run for the frame.
        /// </summary>
        public void ClearDestructionLog() => _destructionLog.Clear();

        /// <summary>
        /// Restores an entity from serialized data.
        /// Internal use only by serializer.
        /// </summary>
        internal void RestoreEntity(int index, bool isActive, int generation, BitMask512 componentMask, DISEntityType disType = default)
        {
            // Note: Authority mask matches default (cleared) unless we serialize it later
            _entityIndex.ForceRestoreEntity(index, isActive, generation, componentMask, disType);
            
            if (isActive && _lifecycleStream != null)
            {
                 // We rely on the caller to fire 'BatchRestored' if this is too spammy, 
                 // but strictly speaking, each re-appearance is a Restore event.
                 // User approved "BatchRestore" preference, but RestoreEntity is per-entity.
                 // We will emit 'Restored' here to be safe and consistent.
                 var entity = new Entity(index, (ushort)generation);
                 _lifecycleStream.Write(new EntityLifecycleEvent {
                    Entity = entity,
                    Type = LifecycleEventType.Restored,
                    Generation = generation
                 });
            }
        }
        
        /// <summary>
        /// Sets the DIS Entity Type in the EntityHeader.
        /// Optimization: Stored directly in header for single-instruction filtering.
        /// </summary>
        public void SetDisType(Entity entity, DISEntityType type)
        {
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            
            ref var meta = ref _entityIndex.GetMetadata(entity.Index);
            meta.DisType = type;
            meta.LastChangeTick = _globalVersion;
        }

        /// <summary>
        /// Returns the DIS Entity Type stored in the EntityHeader.
        /// Complementary to <see cref="SetDisType"/>.
        /// Returns a zeroed <see cref="DISEntityType"/> if no type has been assigned.
        /// </summary>
        public DISEntityType GetDisType(Entity entity)
        {
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            return _entityIndex.GetMetadata(entity.Index).DisType;
        }

        internal void Clear()
        {
            _entityIndex.Clear();
            
            // CRITICAL FIX: We must clear component tables to remove stale references.
            // When seeking backwards in PlaybackSystem, we rely on Clear() to reset state.
            // If we don't clear tables, high indices (or indices not overwritten by the target frame)
            // will retain data from future frames. This causes RepairManagedComponentMasks
            // to incorrecty revive components that shouldn't exist.
            foreach (var table in _componentTables.Values)
            {
                table.Clear();
            }

            // Preserve registered event schemas while flushing buffered event data.
            Bus.ClearAll();
        }

        /// <summary>
        /// Resets the repository state but keeps allocations (for pooling).
        /// </summary>
        public void SoftClear()
        {
             Clear();
        }
        
        internal void RebuildFreeList()
        {
            _entityIndex.RebuildFreeList();
        }
        
        /// <summary>
        /// Explicitly registers an unmanaged component type (Tier 1) and allocates its table.
        /// </summary>
        internal void RegisterUnmanagedComponent<T>() where T : unmanaged
        {
            GetTable<T>(true);
        }

        /// <summary>
        /// Registers an event type to ensure the stream exists.
        /// Useful for delayed events via CommandBuffer.
        /// </summary>
        public void RegisterEvent<T>() where T : unmanaged
        {
            // Forces creation of the stream via Publish (dummy call? No, creating stream is enough).
            // But we don't have public "CreateStream".
            // Bus.Publish<T>(default) would create it but also emit an empty event.
            // We need Bus.Register<T>().
            Bus.Register<T>();
        }
        
        /// <summary>
        /// Registers a managed event type to ensure the stream exists.
        /// Useful for schema validation and delayed events via CommandBuffer.
        /// </summary>
        public void RegisterManagedEvent<T>()
        {
            Bus.RegisterManaged<T>();
        }
        
        /// <summary>
        /// Destroys an entity and removes all its components.
        /// Thread-safe.
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            // Note: Component data is not explicitly cleared (for performance)
            // The ComponentMask is cleared in EntityIndex.DestroyEntity
            
            // RECORDER HOOK: Log destruction before freeing (so we have valid generation)
            _destructionLog.Add(entity);
            
            // Emit Lifecycle Event (BEFORE destruction logic so systems can read final state)
            if (_lifecycleStream != null)
            {
                ref readonly var metaForEvent = ref _entityIndex.GetMetadata(entity.Index);
                _lifecycleStream.Write(new EntityLifecycleEvent {
                    Entity = entity,
                    Type = LifecycleEventType.Destroyed,
                     // Use current generation
                    Generation = metaForEvent.Generation
                });
            }
            
            _entityIndex.DestroyEntity(entity);
        }
        
        /// <summary>
        /// Checks if an entity is currently alive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(Entity entity)
        {
            return _entityIndex.IsAlive(entity);
        }

        /// <summary>
        /// Reconstructs an Entity handle from a raw index.
        /// INTERNAL USE ONLY — valid for:
        ///   1. Translating raw array indices returned by C++ physics/navmesh plugins back to Entity handles
        ///      within the same frame (the generation is looked up and validated).
        ///   2. Kernel-internal bit-scanning in EntityQuery.
        /// DO NOT use this to restore entity references stored from previous frames.
        /// Always pass and store the full Entity struct instead.
        /// </summary>
        internal Entity GetEntity(int index)
        {
            if (index < 0 || index > MaxEntityIndex) return Entity.Null;
            
            ref readonly var meta = ref _entityIndex.GetMetadata(index);
            if (!meta.IsActive) return Entity.Null;
            
            return new Entity(index, meta.Generation);
        }

        /// <summary>
        /// Reconstructs an <see cref="Entity"/> handle from a raw entity index.
        /// <para>
        /// <b>SAFETY (DEBT-027 pattern):</b> A raw index does not carry the generation counter,
        /// so the returned entity may belong to a <em>different</em> entity if the original
        /// occupant of that slot was destroyed and the slot was subsequently recycled.
        /// Always call <see cref="IsAlive"/> on the returned value immediately, and confirm
        /// relevant components are still present before accessing them.
        /// </para>
        /// <para>
        /// Returns <see cref="Entity.Null"/> when <paramref name="index"/> is out of range
        /// or the slot is not currently active.
        /// </para>
        /// </summary>
        public Entity GetEntityByIndex(int index) => GetEntity(index);

        // ========================================================================
        // PUBLIC API (Clean, Unified, High-Performance)
        // ========================================================================

        /// <summary>
        /// Gets the component type ID for a given type.
        /// </summary>
        public int GetComponentTypeId(Type type)
        {
            return ComponentTypeRegistry.GetId(type);
        }

        /// <summary>
        /// Checks if an entity has a component by type ID.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponentByTypeId(Entity entity, int typeId)
        {
             if (!IsAlive(entity)) return false;
             return _entityIndex.GetComponentMask(entity.Index).IsSet(typeId);
        }

        /// <summary>
        /// Gets a raw pointer to an unmanaged component by type ID.
        /// Throws if component is managed or missing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void* GetComponentPointer(Entity entity, int typeId)
        {
            // Verify component exists? Or assume caller checked?
            // "Internal access" usually implies speed, but let's be safe-ish
            // The table lookup is safe. The pointer is unsafe.
            
            var table = _tableCache[typeId];
            if (table == null) throw new InvalidOperationException($"Component type {typeId} is not registered");
            
            return table.GetRawPointer(entity.Index);
        }

        /// <summary>
        /// Gets a managed component object by type ID.
        /// Throws if component is unmanaged or missing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetManagedComponentByTypeId(Entity entity, int typeId)
        {
            var table = _tableCache[typeId];
            if (table == null) throw new InvalidOperationException($"Component type {typeId} is not registered");
            
            return table.GetRawObject(entity.Index);
        }

        /// <summary>
        /// Registers a component type (auto-detects Managed vs Unmanaged).
        /// </summary>
        /// <param name="policyOverride">Nullable override logic. If null, auto-detects based on convention:
        /// <list type="bullet">
        /// <item>Structs: Default true (Snapshotable)</item>
        /// <item>Records: Default true (Snapshotable)</item>
        /// <item>Classes: Default to DataPolicy.NoSnapshot (Safe safety rail)</item>
        /// </list>
        /// </param>
        /// <summary>
        /// Registers a component type with specific data policy override.
        /// </summary>
        /// <param name="policyOverride">Overrides the [DataPolicy] attribute on the type.</param>
        public void RegisterComponent<T>(DataPolicy? policyOverride = null)
        {
            Type type = typeof(T);
            
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                // ━━━ UNMANAGED (Struct) ━━━
                UnsafeShim.RegisterUnmanaged<T>(this);
                
                int typeId = ComponentTypeRegistry.GetId(type);
                if (typeId < 0) return; // Registration failed
                
                DataPolicy effectivePolicy;

                // Priority 1: Explicit override
                if (policyOverride.HasValue)
                {
                    effectivePolicy = policyOverride.Value;
                }
                else
                {
                    // Priority 2: Attribute
                    var attr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DataPolicyAttribute>(type);
                    if (attr != null)
                    {
                        effectivePolicy = attr.Policy;
                    }
                    else
                    {
                        // Priority 3: Default for Structs
                        effectivePolicy = DataPolicy.Default;
                    }
                }
                
                bool snapshot = !effectivePolicy.HasFlag(DataPolicy.NoSnapshot);
                bool record = !effectivePolicy.HasFlag(DataPolicy.NoRecord);
                bool save = !effectivePolicy.HasFlag(DataPolicy.NoSave);
                bool clone = effectivePolicy.HasFlag(DataPolicy.SnapshotViaClone);
                
                ComponentTypeRegistry.SetSnapshotable(typeId, snapshot);
                ComponentTypeRegistry.SetRecordable(typeId, record);
                ComponentTypeRegistry.SetSaveable(typeId, save);
                ComponentTypeRegistry.SetNeedsClone(typeId, clone);  // Structs usually don't need clone, but flag is respected (though memcpy is usually sufficient)
            }
            else
            {
                // ━━━ MANAGED (Class/Record) ━━━
                UnsafeShim.RegisterManaged<T>(this);
                int typeId = ComponentTypeRegistry.GetId(type);
                if (typeId < 0) return;
                
                // Priority 1: Explicit override parameter (highest priority)
                if (policyOverride.HasValue)
                {
                    DataPolicy policy = policyOverride.Value;
                    
                    bool snapshot = !policy.HasFlag(DataPolicy.NoSnapshot);
                    bool record = !policy.HasFlag(DataPolicy.NoRecord);
                    bool save = !policy.HasFlag(DataPolicy.NoSave);
                    bool clone = policy.HasFlag(DataPolicy.SnapshotViaClone);
                    
                    // If SnapshotViaClone is set, force snapshot=true
                    if (clone) snapshot = true;
                    
                    ComponentTypeRegistry.SetSnapshotable(typeId, snapshot);
                    ComponentTypeRegistry.SetRecordable(typeId, record);
                    ComponentTypeRegistry.SetSaveable(typeId, save);
                    ComponentTypeRegistry.SetNeedsClone(typeId, clone);
                    return;
                }
                
                // Priority 2: DataPolicy attribute
                var dataPolicyAttr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DataPolicyAttribute>(type);
                
                DataPolicy effectivePolicy;
                
                if (dataPolicyAttr != null)
                {
                    effectivePolicy = dataPolicyAttr.Policy;
                }
                else
                {
                    // Priority 3: Convention-based defaults
                    bool isRecord = ComponentTypeRegistry.IsRecordType(type);
                    
                    if (isRecord)
                    {
                        // Record → Safe everywhere
                        effectivePolicy = DataPolicy.Default;  // All enabled
                    }
                    else
                    {
                        // Mutable Class → Auto-default to NoSnapshot
                        effectivePolicy = DataPolicy.NoSnapshot;
                        
                        #if DEBUG
                        // Console.WriteLine($"WARNING: Mutable class '{type.Name}' registered without [DataPolicy]. Defaulting to NoSnapshot.");
                        #endif
                    }
                }
                
                // Apply flags
                bool finalSnapshot = !effectivePolicy.HasFlag(DataPolicy.NoSnapshot);
                bool finalRecord = !effectivePolicy.HasFlag(DataPolicy.NoRecord);
                bool finalSave = !effectivePolicy.HasFlag(DataPolicy.NoSave);
                bool finalClone = effectivePolicy.HasFlag(DataPolicy.SnapshotViaClone);
                
                // If SnapshotViaClone is set, force snapshot=true
                if (finalClone) finalSnapshot = true;
                
                ComponentTypeRegistry.SetSnapshotable(typeId, finalSnapshot);
                ComponentTypeRegistry.SetRecordable(typeId, finalRecord);
                ComponentTypeRegistry.SetSaveable(typeId, finalSave);
                ComponentTypeRegistry.SetNeedsClone(typeId, finalClone);
            }
        }

        /// <summary>
        /// Returns true if the unmanaged component type <typeparamref name="T"/> has been
        /// registered with this repository.  Used by template applicators to silently skip
        /// server-side components when applying shared TkbTemplates on a client world (e.g. IG).
        /// </summary>
        public bool IsComponentTypeRegistered<T>() where T : unmanaged
            => _componentTables.ContainsKey(typeof(T));

        /// <summary>
        /// Register a managed component type with convention-based safety.
        /// Wrapper around RegisterComponent for explicit managed registration.
        /// </summary>
        public void RegisterManagedComponent<T>(DataPolicy? policyOverride = null) where T : class
        {
            RegisterComponent<T>(policyOverride);
        }
        /// Works for both struct (Unmanaged) and class (Managed) components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponent<T>(Entity entity, T component)
        {
            // JIT will compile this down to a direct call, removing the if/else.
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                UnsafeShim.AddUnmanaged(this, entity, component);
            }
            else
            {
                UnsafeShim.AddManaged(this, entity, component);
            }
        }

        /// <summary>
        /// Set component value (alias for AddComponent - upsert behavior).
        /// Use this when semantically updating an existing component.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetComponent<T>(Entity entity, T component)
        {
            // In FDP, AddComponent is already upsert (update-or-insert)
            AddComponent<T>(entity, component);
        }

        /// <summary>
        /// Gets a reference (Read/Write) to a component.
        /// Updates the <em>chunk</em> version so recorder/delta systems can detect
        /// that the component table was touched this frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method intentionally does <b>not</b> stamp <c>EntityHeader.LastChangeTick</c>.
        /// Here is why:
        /// </para>
        /// <list type="bullet">
        ///   <item><b>False sharing / cache thrashing:</b> <c>EntityHeader</c> is stored in
        ///   a separate <c>NativeChunkTable</c> from the component data.  Writing to it on every
        ///   RW access forces the CPU to fetch a second cache line and, when multiple threads
        ///   process entities in the same header chunk concurrently, causes L1 invalidation
        ///   storms that destroy multi-core scalability.</item>
        ///   <item><b>Over-broad dirtying:</b> <c>LastChangeTick</c> is entity-wide, not
        ///   component-specific.  A write to <c>WeaponState</c> would appear identical to a
        ///   write to <c>SimTransform</c>, causing any consumer that watches the tick to take
        ///   false-positive actions (e.g. broadcasting a <c>GeoSpatial</c> packet for a
        ///   stationary tank whose weapon just reloaded).</item>
        ///   <item><b>Existing granularity is sufficient:</b> The chunk-level version written
        ///   by this method already supports the Flight Recorder and delta serialisation.
        ///   High-frequency egress systems (e.g. <c>GeoSpatialEgressTranslator</c>) should
        ///   use <em>shadow-state comparison</em> on the specific component values they care
        ///   about, which is both faster and more precise.</item>
        /// </list>
        /// <para>
        /// If you need to signal that an entity requires a network update, call
        /// <c>SmartEgressUtil.MarkDirty</c> for the specific descriptor ordinal, or
        /// implement per-translator state comparison.
        /// </para>
        /// <para>
        /// <b>⚠ DANGER — <c>[InlineArray]</c> Mutation Trap (C# 12)</b><br/>
        /// Components that contain an <c>[InlineArray]</c> field (e.g.
        /// <c>MissionPlanQueue.Phases</c>) are susceptible to a JIT defensive-copy bug.
        /// When you write:
        /// <code>
        /// ref var q = ref GetComponentRW&lt;MissionPlanQueue&gt;(e);
        /// q.Phases[0] = x;  // ❌ compiler emits ldobj → copies buffer to temp → mutation lost!
        /// </code>
        /// The C# compiler emits an <c>ldobj</c> IL instruction that copies the inline array
        /// to a temporary evaluation slot before applying the indexer. The mutation hits the
        /// temporary, not the ECS chunk. The write is <b>silently lost</b>.
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     <b>Flat-struct components</b> (<c>SimTransform</c>, <c>SimVelocity</c>,
        ///     <c>BehaviorState</c>, etc.) — use <c>GetComponentRW</c> freely; no inline
        ///     arrays, no risk.
        ///   </item>
        ///   <item>
        ///     <b>Components with <c>[InlineArray]</c> fields</b> — use one of these safe
        ///     patterns instead:<br/>
        ///     ✅ Cast to <c>Span&lt;T&gt;</c> first: <c>Span&lt;MissionPhase&gt; s = q.Phases; s[0] = x;</c> (zero-alloc, in-place).<br/>
        ///     ✅ Read with <c>GetComponent</c>, mutate the local copy, then call <c>SetComponent</c>.
        ///   </item>
        /// </list>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponentRW<T>(Entity entity)
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                return ref UnsafeShim.GetUnmanagedRW<T>(this, entity);
            }
            else
            {
                return ref UnsafeShim.GetManagedRW<T>(this, entity);
            }
        }

        /// <summary>
        /// Gets a read-only reference to a component.
        /// Does NOT update version/dirty flags (faster for queries).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetComponentRO<T>(Entity entity)
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                return ref UnsafeShim.GetUnmanagedRO<T>(this, entity);
            }
            else
            {
                return ref UnsafeShim.GetManagedRO<T>(this, entity);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetComponent<T>(Entity entity)
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                return ref UnsafeShim.GetUnmanagedRO<T>(this, entity);
            }
            else
            {
                return ref UnsafeShim.GetManagedRO<T>(this, entity);
            }
        }

        /// <summary>
        /// Checks if an entity has a component.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>(Entity entity)
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                return UnsafeShim.HasUnmanaged<T>(this, entity);
            }
            else
            {
                return UnsafeShim.HasManaged<T>(this, entity);
            }
        }

        /// <summary>
        /// Tries to get component, returns false if not present.
        /// Unified helper wrapper.
        /// </summary>
        public bool TryGetComponent<T>(Entity entity, out T component)
        {
            if (HasComponent<T>(entity))
            {
                component = GetComponentRW<T>(entity);
                return true;
            }
            component = default!;
            return false;
        }

        /// <summary>
        /// Checks if a component table has been modified since the specified tick.
        /// Uses lazy scan of chunk versions (fast, no writes).
        /// </summary>
        public bool HasComponentChanged(Type componentType, uint sinceTick)
        {
            if (_componentTables.TryGetValue(componentType, out var table))
                return table.HasChanges(sinceTick);
            return false;
        }

        /// <summary>
        /// Removes a component.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(Entity entity)
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                UnsafeShim.RemoveUnmanaged<T>(this, entity);
            }
            else
            {
                UnsafeShim.RemoveManaged<T>(this, entity);
            }
        }

        // ================================================
        // INTERNAL IMPLEMENTATION (Hide these from public API)
        // ================================================
        
        // ================================================
        // COMPONENT MANAGEMENT
        // ================================================
        
        /// <summary>
        /// Adds an unmanaged component (Tier 1) to an entity.
        /// Updates ComponentMask automatically.
        /// </summary>
        internal void AddUnmanagedComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            ValidateWriteAccess<T>(entity);
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            // Ensure component table exists (must be registered)
            var table = GetTable<T>(false);
            
            // Set component data (updates chunk version)
            table.Set(entity.Index, component, _globalVersion);
            
            // Update component mask
            ref var compMask = ref _entityIndex.GetComponentMask(entity.Index);
            compMask.SetBit(ComponentType<T>.ID);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// Removes an unmanaged component (Tier 1) from an entity.
        /// Updates ComponentMask automatically.
        /// Component data remains in table but is inaccessible.
        /// </summary>
        internal void RemoveUnmanagedComponent<T>(Entity entity) where T : unmanaged
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            if (!HasUnmanagedComponent<T>(entity))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
            #endif
            
            // Clear component mask bit
            ref var compMask = ref _entityIndex.GetComponentMask(entity.Index);
            compMask.ClearBit(ComponentType<T>.ID);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
            
            // Note: Component data is not cleared for performance
            // It will be overwritten if component is re-added
        }
        
        /// <summary>
        /// Checks if entity has a specific unmanaged component (Tier 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasUnmanagedComponent<T>(Entity entity) where T : unmanaged
        {
            if (!IsAlive(entity))
                return false;
            
            return _entityIndex.GetComponentMask(entity.Index).IsSet(ComponentType<T>.ID);
        }
        
        /// <summary>
        /// Gets reference to unmanaged component (Tier 1).
        /// DANGEROUS: Does not validate if component exists!
        /// Use HasUnmanagedComponent first or ensure component exists via query.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T GetUnmanagedComponent<T>(Entity entity) where T : unmanaged
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            if (!HasUnmanagedComponent<T>(entity))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
            #endif
            
            var table = GetTable<T>(false);
            // Legacy/Default: Treat as Read/Write (updates version)
            return ref table.GetRW(entity.Index, _globalVersion);
        }
        
        /// <summary>
        /// Gets WRITE access to unmanaged component (Tier 1). Updates chunk version.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method intentionally does <b>not</b> stamp <c>EntityHeader.LastChangeTick</c>.
        /// </para>
        /// <list type="bullet">
        ///   <item><b>False sharing:</b> Kinematic systems (e.g. <c>CarKinematicsSystem</c>)
        ///   call this in tight per-entity loops, potentially on multiple threads via
        ///   <c>ForEachParallel</c>.  Writing to the <c>EntityHeader</c> chunk on every call
        ///   causes concurrent cache-line invalidation between cores processing adjacent
        ///   entities, destroying multi-threaded throughput.</item>
        ///   <item><b>Over-broad dirtying:</b> <c>LastChangeTick</c> is shared across
        ///   all component types on the entity.  A mutation to any unrelated component
        ///   (e.g. <c>WeaponState</c>) would falsely appear as a position change to any
        ///   subscriber watching the tick, causing unnecessary network traffic.</item>
        ///   <item><b>Chunk version is the right granularity:</b> The chunk-level
        ///   <c>_chunkVersions</c> written here (padded to 64 bytes to prevent false sharing)
        ///   is sufficient for the Flight Recorder and delta serialisation, which operate
        ///   at chunk granularity anyway.</item>
        /// </list>
        /// <para>
        /// For network egress of high-frequency data, use shadow-state comparison
        /// (compare the current component value against the last published value).
        /// For low-frequency discrete data, call <c>SmartEgressUtil.MarkDirty</c>.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T GetUnmanagedComponentRW<T>(Entity entity) where T : unmanaged
        {
            ValidateWriteAccess<T>(entity);
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            if (!HasUnmanagedComponent<T>(entity)) throw new InvalidOperationException($"Entity {entity} missing {typeof(T).Name}");
            #endif
            return ref GetTable<T>(false).GetRW(entity.Index, _globalVersion);
        }
        
        /// <summary>
        /// Gets READ-ONLY access to unmanaged component (Tier 1). Does NOT update chunk version.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref readonly T GetUnmanagedComponentRO<T>(Entity entity) where T : unmanaged
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            if (!HasUnmanagedComponent<T>(entity)) throw new InvalidOperationException($"Entity {entity} missing {typeof(T).Name}");
            #endif
            return ref GetTable<T>(false).GetRO(entity.Index);
        }
        
        /// <summary>
        /// Sets unmanaged component (Tier 1) value.
        /// Adds component if it doesn't exist.
        /// </summary>
        internal void SetUnmanagedComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            ValidateWriteAccess<T>(entity);
            
            if (!HasUnmanagedComponent<T>(entity))
            {
                AddUnmanagedComponent(entity, component);
            }
            else
            {
                var table = GetTable<T>(false);
                table.Set(entity.Index, component, _globalVersion);
            }
        }
        


        /// <summary>
        /// Returns true if this peer has authority over the specified component type ID.
        /// Returns false if the entity is not alive or the bit is not set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAuthority(Entity entity, int componentId)
        {
            if (!IsAlive(entity)) return false;
            return _entityIndex.GetMetadata(entity.Index).AuthorityMask.IsSet(componentId);
        }

        /// <summary>
        /// Sets whether this peer has authority over the specified ECS component type ID.
        /// The <paramref name="typeId"/> must correspond to a component that is actually present
        /// on the entity — use <see cref="HasComponentByTypeId"/> to guard call sites that obtain
        /// the ID from an external source (e.g. <c>DescriptorOwnershipMap</c>).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive, or when no component with <paramref name="typeId"/>
        /// exists on the entity (strict mode preserves ECS invariants).
        /// </exception>
        public void SetAuthority(Entity entity, int typeId, bool hasAuthority)
        {
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");

            ref var compMask = ref _entityIndex.GetComponentMask(entity.Index);
            ref var metaS    = ref _entityIndex.GetMetadata(entity.Index);

            if (!compMask.IsSet(typeId))
                throw new InvalidOperationException(
                    $"Cannot set authority for component ID {typeId}: component is not present on entity {entity}.");

            if (hasAuthority)
                metaS.AuthorityMask.SetBit(typeId);
            else
                metaS.AuthorityMask.ClearBit(typeId);
        }

        /// <summary>
        /// Sets whether this peer has authority over the specified component.
        /// Throws if component is missing.
        /// </summary>
        public void SetAuthority<T>(Entity entity, bool hasAuthority) where T : unmanaged
        {
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            // Verify component type is registered
            GetTable<T>(false);
            
            ref var compMaskG = ref _entityIndex.GetComponentMask(entity.Index);
            ref var metaG     = ref _entityIndex.GetMetadata(entity.Index);
            int typeId = ComponentType<T>.ID;
            
            if (!compMaskG.IsSet(typeId))
                 throw new InvalidOperationException($"Cannot set authority for {typeof(T).Name}: Entity does not have component.");

            if (hasAuthority)
                metaG.AuthorityMask.SetBit(typeId);
            else
                metaG.AuthorityMask.ClearBit(typeId);
        }
        
        /// <summary>
        /// Checks if this peer has authority over the specified component.
        /// Returns false if component is missing or no authority.
        /// </summary>
        public bool HasAuthority<T>(Entity entity) where T : unmanaged
        {
            // Verify component type is registered
            GetTable<T>(false);
            
            if (!IsAlive(entity)) return false;
            
            int typeId = ComponentType<T>.ID;
            return _entityIndex.GetMetadata(entity.Index).AuthorityMask.IsSet(typeId);
        }
        
        private void ValidateWriteAccess<T>(Entity entity) where T : unmanaged
        {
            if (_currentPhasePermission == PhasePermission.ReadWriteAll) return;
            
            if (_currentPhasePermission == PhasePermission.ReadOnly)
                  throw new InvalidOperationException($"Phase Error: Cannot modify component {typeof(T).Name} during {_currentPhase} (ReadOnly).");
            
            // Allow adding new components (structural change) unless ReadOnly.
            if (!HasUnmanagedComponent<T>(entity))
                return;

            bool hasAuth = HasAuthority<T>(entity);
            
            if (_currentPhasePermission == PhasePermission.OwnedOnly && !hasAuth)
                 throw new InvalidOperationException($"Phase Error: Cannot modify REMOTE component {typeof(T).Name} during {_currentPhase} (OwnedOnly).");
                 
            if (_currentPhasePermission == PhasePermission.UnownedOnly && hasAuth)
                 throw new InvalidOperationException($"Phase Error: Cannot modify OWNED component {typeof(T).Name} during {_currentPhase} (UnownedOnly).");
        }
        


        // ================================================
        // TIER 2: MANAGED COMPONENTS
        // ================================================
        
        /// <summary>
        /// Registers a managed component type (classes, strings, etc.).
        /// Tier 2 storage uses GC-managed arrays.
        /// </summary>
        internal void RegisterManagedComponentInternal<T>() where T : class
        {
            Type type = typeof(T);
            if (_componentTables.ContainsKey(type))
                return;
            
            var table = new ManagedComponentTable<T>();
            _componentTables[type] = table;
            
            // Type ID is auto-assigned via ManagedComponentType<T>.ID
            int typeId = ManagedComponentType<T>.ID;
            
            // Update _tableCache so GetManagedComponentByTypeId can find this table
            if (typeId < _tableCache.Length)
            {
                _tableCache[typeId] = table;
            }
            else
            {
                Array.Resize(ref _tableCache, typeId + 1);
                _tableCache[typeId] = table;
            }
        }
        
        /// <summary>
        /// Checks if entity has a managed component.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasManagedComponent<T>(Entity entity) where T : class
        {
            if (!IsAlive(entity))
                return false;
            
            if (_entityIndex.GetComponentMask(entity.Index).IsSet(ManagedComponentType<T>.ID))
                return true;

            return false;
        }
        
        /// <summary>
        /// Gets managed component reference (nullable).
        /// Returns null if component not set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T GetManagedComponent<T>(Entity entity) where T : class
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            var table = GetManagedTable<T>(false);
            return table[entity.Index]!;
        }
        
        /// <summary>
        /// Gets managed component for read/write access.
        /// Allocates if component doesn't exist yet.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T GetManagedComponentRW<T>(Entity entity) where T : class
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            var table = GetManagedTable<T>(false);
            ref var val = ref table.GetRW(entity.Index, _globalVersion);
            return ref Unsafe.As<T?, T>(ref val);
        }
        
        /// <summary>
        /// Gets managed component for read-only access.
        /// Does not update version or allocate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T GetManagedComponentRO<T>(Entity entity) where T : class
        {
            var table = GetManagedTable<T>(false);
            return table.GetRO(entity.Index)!;
        }
        
        /// <summary>
        /// Sets (or adds) a managed component.
        /// </summary>
        public void SetManagedComponent<T>(Entity entity, T value) where T : class
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            var table = GetManagedTable<T>(false);
            table.Set(entity.Index, value, _globalVersion);
            
            // Update component mask
            ref var compMaskM = ref _entityIndex.GetComponentMask(entity.Index);
            
            if (value != null)
                compMaskM.SetBit(ManagedComponentType<T>.ID);
            else
                compMaskM.ClearBit(ManagedComponentType<T>.ID);
                
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }

        // ================================================
        // ALIASES (For API Convenience/TKB)
        // ================================================


        
        /// <summary>
        /// Adds a managed component to an entity.
        /// </summary>
        internal void AddManagedComponent<T>(Entity entity, T value) where T : class
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
			// commented out because Add is currently an alias for Set (upsert). If we want strict Add semantics, we can re-enable this check.
			//if (HasManagedComponent<T>(entity))
			//    throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}");
#endif

			SetManagedComponent( entity, value);
        }
        
        /// <summary>
        /// Removes a managed component from an entity.
        /// </summary>
        internal void RemoveManagedComponent<T>(Entity entity) where T : class
        {
            if (!IsAlive(entity) || !HasManagedComponent<T>(entity))
                return;
            
            var table = GetManagedTable<T>(false);
            // CRITICAL FIX: Use Set(null) with version tracking instead of Clear/indexer
            // This ensures the removal is captured by Flight Recorder deltas.
            table.Set(entity.Index, null, _globalVersion);
            
            // Update component mask
            ref var compMaskR = ref _entityIndex.GetComponentMask(entity.Index);
            compMaskR.ClearBit(ManagedComponentType<T>.ID);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// Gets managed component table (Tier 2).
        /// </summary>
        private ManagedComponentTable<T> GetManagedTable<T>(bool allowCreate) where T : class
        {
            Type type = typeof(T);
            
            if (!_componentTables.TryGetValue(type, out var table))
            {
                if (!allowCreate)
                    throw new InvalidOperationException($"Managed component {type.Name} has not been registered. Call RegisterManagedComponent<{type.Name}>() first.");
                
                var newTable = new ManagedComponentTable<T>();
                _componentTables[type] = newTable;
                
                // Update Cache
                int typeId = ManagedComponentType<T>.ID;
                if (typeId < _tableCache.Length)
                {
                    _tableCache[typeId] = newTable;
                }
                else
                {
                    Array.Resize(ref _tableCache, typeId + 1);
                    _tableCache[typeId] = newTable;
                }
                
                return newTable;
            }
            
            return (ManagedComponentTable<T>)table;
        }
        
        // ================================================
        // COMPONENT TABLE MANAGEMENT (Internal)
        // ================================================
        
        internal ComponentTable<T> GetTable<T>(bool allowCreate) where T : unmanaged
        {
            Type type = typeof(T);
            
            // Fast path: table already exists
            if (_componentTables.TryGetValue(type, out var existingTable))
            {
                return (ComponentTable<T>)existingTable;
            }
            
            if (!allowCreate)
            {
                throw new InvalidOperationException($"Component {type.Name} is not registered. Call RegisterComponent<{type.Name}>() first.");
            }
            
            // Slow path: create new table (thread-safe)
            lock (_tableLock)
            {
                // Double-check after acquiring lock
                if (_componentTables.TryGetValue(type, out existingTable))
                {
                    return (ComponentTable<T>)existingTable;
                }
                
                var newTable = new ComponentTable<T>();
                _componentTables[type] = newTable;
                
                // Update Cache
                int typeId = ComponentType<T>.ID;
                if (typeId < _tableCache.Length)
                {
                    _tableCache[typeId] = newTable;
                }
                else
                {
                    // Expand cache if needed (though MAX_COMPONENT_TYPES should cover)
                    Array.Resize(ref _tableCache, typeId + 1);
                    _tableCache[typeId] = newTable;
                }
                
                return newTable;
            }
        }
        
        /// <summary>
        /// Gets component table for advanced scenarios.
        /// Creates table if it doesn't exist.
        /// </summary>
        public ComponentTable<T> GetComponentTable<T>() where T : unmanaged
        {
            return GetTable<T>(false);
        }

        /// <summary>
        /// Tries to get the component table for a given type (Unmanaged or Managed).
        /// Returns false if not registered/found, unless allowCreate is true (and it's registered).
        /// Since this is generic-less, we assume type is valid.
        /// </summary>
        public bool TryGetTable(Type type, out IComponentTable table)
        {
            return _componentTables.TryGetValue(type, out table!);
        }
        /// WARNING: Direct access bypasses safety checks!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BitMask512 GetComponentMask(int entityIndex)
        {
            return ref _entityIndex.GetComponentMask(entityIndex);
        }

        /// <summary>
        /// Gets the cold metadata (generation, authority, lifecycle, etc.) for a raw entity index.
        /// WARNING: Direct access bypasses safety checks!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref EntityMetadataCold GetMetadata(int entityIndex)
        {
            return ref _entityIndex.GetMetadata(entityIndex);
        }
        
        /// <summary>
        /// Gets the EntityIndex for advanced scenarios (iteration, chunk access).
        /// </summary>
        public EntityIndex GetEntityIndex()
        {
            return _entityIndex;
        }
        
        /// <summary>
        /// Gets all registered component tables.
        /// Used by Flight Recorder for serialization.
        /// </summary>
        public IReadOnlyDictionary<Type, IComponentTable> GetRegisteredComponentTypes()
        {
            return _componentTables;
        }
        
        // ================================================
        // QUERY API
        // ================================================
        
        /// <summary>
        /// Creates a new query builder for filtering entities.
        /// Use fluent API: repo.Query().With<Position>().Without<Velocity>().Build()
        /// </summary>
        public QueryBuilder Query()
        {
            return new QueryBuilder(this);
        }
        
        /// <summary>
        /// Iterates entities that match the query AND have changed since the specified version.
        /// Checks structural changes (EntityHeader) and component value changes (Chunk Version).
        /// </summary>
        public void QueryDelta(EntityQuery query, uint sinceVersion, Action<Entity> action)
        {
            // Iterate component tables inline (no List allocation) so this method is
            // zero-allocation when no entities match (SR-T34 invariant).
            int maxIndex = _entityIndex.MaxIssuedIndex;
            
            // Iterate entities
            // Optimization TODO: Use Chunk Skipping if possible (requires aligned chunks or block checks)
            // For now, linear scan with O(1) version checks is reasonably fast.
            for (int i = 0; i <= maxIndex; i++)
            {
                 ref readonly var metaD   = ref _entityIndex.GetMetadata(i);
                 ref var          compMD  = ref _entityIndex.GetComponentMask(i);
                 if (!metaD.IsActive) continue;
                 
                 bool changed = false;
                 
                 // Check structural change (metadata version)
                 if (metaD.LastChangeTick > sinceVersion)
                 {
                     changed = true;
                 }
                 else
                 {
                     // Check component value changes for tables in the query.
                     // Using foreach on Dictionary<K,V> uses the value-type enumerator -- no allocation.
                     foreach (var kvp in _componentTables)
                     {
                         int typeId = kvp.Value.ComponentTypeId;
                         if (query.IncludeMask.IsSet(typeId) &&
                             kvp.Value.GetVersionForEntity(i) > sinceVersion)
                         {
                             changed = true;
                             break;
                         }
                     }
                 }
                 
                 if (changed)
                 {
                     if (query.Matches(in compMD, in metaD))
                     {
                         action(new Entity(i, metaD.Generation));
                     }
                 }
            }
        
        }
        
        /// <summary>
        /// Iterates entities with a time budget using the DefaultTimeSliceMetric.
        /// </summary>
        public void QueryTimeSliced(EntityQuery query, IteratorState state, double budget, Action<Entity> action)
        {
            QueryTimeSliced(query, state, budget, DefaultTimeSliceMetric, action);
        }

        /// <summary>
        /// Iterates entities with a time budget. Resumes from where it left off.
        /// </summary>
        public void QueryTimeSliced(EntityQuery query, IteratorState state, double budget, TimeSliceMetric metric, Action<Entity> action)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            
            // Allow restarting automatically if previously complete
            if (state.IsComplete) state.Reset();
            
            int maxIndex = _entityIndex.MaxIssuedIndex;
            long startTick = (metric == TimeSliceMetric.WallClockTime && budget > 0) ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            double freq = System.Diagnostics.Stopwatch.Frequency;
            int checkInterval = 64; // Check time every 64 entities
            int processedCount = 0;
            
            for (int i = state.NextEntityId; i <= maxIndex; i++)
            {
                // Verify entity matches
                ref readonly var metaTS  = ref _entityIndex.GetMetadata(i);
                ref var          compTS  = ref _entityIndex.GetComponentMask(i);
                if (metaTS.IsActive && query.Matches(in compTS, in metaTS))
                {
                    action(new Entity(i, metaTS.Generation));
                    processedCount++;
                }
                
                // Check limit
                if (metric == TimeSliceMetric.EntityCount && processedCount >= (int)budget)
                {
                    state.NextEntityId = i + 1;
                    state.IsComplete = false;
                    return;
                }
                
                // Check budget periodically (Wall Clock)
                if (metric == TimeSliceMetric.WallClockTime && (i % checkInterval) == 0)
                {
                    long currentTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    double elapsedMs = (currentTick - startTick) * 1000.0 / freq;
                    
                    if (elapsedMs >= budget)
                    {
                        // Time's up! Save state and exit
                        state.NextEntityId = i + 1;
                        state.IsComplete = false;
                        return;
                    }
                }
            }
            
            // Completed
            state.IsComplete = true;
            state.NextEntityId = 0;
        }

        
        // ================================================
        // MULTI-PART METADATA (Stage 10)
        // ================================================
        
        /// <summary>
        /// Sets the part descriptor for a component on an entity.
        /// Used for network synchronization to track which parts are present.
        /// </summary>
        public void SetPartDescriptor<T>(Entity entity, PartDescriptor descriptor) where T : unmanaged
        {
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            if (!HasUnmanagedComponent<T>(entity))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
            #endif
            
            _metadata.SetPartDescriptor(entity.Index, ComponentType<T>.ID, descriptor);
        }
        
        /// <summary>
        /// Gets the part descriptor for a component on an entity.
        /// Returns full descriptor if not explicitly set.
        /// </summary>
        public PartDescriptor GetPartDescriptor<T>(Entity entity) where T : unmanaged
        {
            return _metadata.GetPartDescriptor(entity.Index, ComponentType<T>.ID);
        }
        
        /// <summary>
        /// Checks if a specific part is present for a component.
        /// </summary>
        public bool HasPart<T>(Entity entity, int partIndex) where T : unmanaged
        {
            return _metadata.HasPart(entity.Index, ComponentType<T>.ID, partIndex);
        }
        
        /// <summary>
        /// Gets the metadata table for advanced scenarios.
        /// </summary>
        public ComponentMetadataTable GetMetadataTable()
        {
            return _metadata;
        }
        
        // ================================================
        // RAW COMPONENT ACCESS (For EntityCommandBuffer)
        // ================================================
        
        /// <summary>
        /// Adds a component using raw bytes (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal unsafe void AddComponentRaw(Entity entity, int typeId, IntPtr dataPtr, int size)
        {
            if (!IsAlive(entity)) return;
            
            Type? componentType = ComponentTypeRegistry.GetType(typeId);
            if (componentType == null)
                throw new InvalidOperationException($"Component type ID {typeId} not registered");
            
            if (!_componentTables.TryGetValue(componentType, out var table))
            {
                var registered = string.Join(", ", _componentTables.Keys.Select(k => k.Name));
                throw new InvalidOperationException($"Component {componentType.Name} (ID: {typeId}) not registered. Registered: {registered}. Call RegisterComponent first.");
            }
            
            // Use the raw write interface
            table.SetRaw(entity.Index, dataPtr, size, _globalVersion);
            
            // Update component mask
            _entityIndex.GetComponentMask(entity.Index).SetBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// OPTIMIZED: Fast path for command buffer playback.
        /// Uses direct array lookup instead of dictionary + lock.
        /// </summary>
        internal unsafe void SetComponentRawFast(Entity entity, int typeId, IntPtr dataPtr, int size)
        {
            // O(1) array access - NO LOCKS, NO HASHING
            if (typeId >= _tableCache.Length || _tableCache[typeId] == null)
            {
                #if DEBUG || FDP_PARANOID_MODE
                throw new InvalidOperationException(
                    $"Component type {typeId} not registered. " +
                    $"All components must be registered before command buffer playback.");
                #else
                // In release: fallback to slow path (should never happen in production)
                SetComponentRaw(entity, typeId, dataPtr, size);
                return;
                #endif
            }
            
            var table = _tableCache[typeId]!;
            
            // Direct memory copy
            table.SetRaw(entity.Index, dataPtr, size, _globalVersion);
            
            // Update component mask
            _entityIndex.GetComponentMask(entity.Index).SetBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }

        /// <summary>
        /// Sets a component using raw bytes (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal unsafe void SetComponentRaw(Entity entity, int typeId, IntPtr dataPtr, int size)
        {
            // Use fast path if cache available
            if (typeId < _tableCache.Length && _tableCache[typeId] != null)
            {
                SetComponentRawFast(entity, typeId, dataPtr, size);
                return;
            }

            if (!IsAlive(entity)) return;
            
            Type? componentType = ComponentTypeRegistry.GetType(typeId);
            if (componentType == null)
                throw new InvalidOperationException($"Component type ID {typeId} not registered");
            
            if (!_componentTables.TryGetValue(componentType, out var table))
                throw new InvalidOperationException($"Component {componentType.Name} not registered. Call RegisterComponent first.");
            
            // Use the raw write interface
            table.SetRaw(entity.Index, dataPtr, size, _globalVersion);
            
            // Update component mask (in case it wasn't set)
            _entityIndex.GetComponentMask(entity.Index).SetBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// Removes a component by type ID (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal void RemoveComponentRaw(Entity entity, int typeId)
        {
            if (!IsAlive(entity)) return;
            
            // Clear component mask bit
            _entityIndex.GetComponentMask(entity.Index).ClearBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// Adds a managed component using object reference (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal void AddManagedComponentRaw(Entity entity, int typeId, object componentObj)
        {
            if (!IsAlive(entity)) return;
            
            Type? componentType = ComponentTypeRegistry.GetType(typeId);
            if (componentType == null)
                throw new InvalidOperationException($"Component type ID {typeId} not registered");
            
            if (!_componentTables.TryGetValue(componentType, out var table))
                throw new InvalidOperationException($"Component {componentType.Name} not registered. Call RegisterManagedComponent first.");
            
            // Cast to managed table and set
            table.SetRawObject(entity.Index, componentObj);
            
            // Update component mask
            ref var header2 = ref _entityIndex.GetComponentMask(entity.Index);
            header2.SetBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// Sets a managed component using object reference (type-erased).
        /// Used internally by EntityCommand Buffer during playback.
        /// </summary>
        internal void SetManagedComponentRaw(Entity entity, int typeId, object componentObj)
        {
            if (!IsAlive(entity)) return;
            
            Type? componentType = ComponentTypeRegistry.GetType(typeId);
            if (componentType == null)
                throw new InvalidOperationException($"Component type ID {typeId} not registered");
            
            if (!_componentTables.TryGetValue(componentType, out var table))
                throw new InvalidOperationException($"Component {componentType.Name} not registered. Call RegisterManagedComponent first.");
            
            // Cast to managed table and set
            table.SetRawObject(entity.Index, componentObj);
            
            // Update component mask
            ref var header2b = ref _entityIndex.GetComponentMask(entity.Index);
            header2b.SetBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        /// <summary>
        /// Removes a managed component (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal void RemoveManagedComponentRaw(Entity entity, int typeId)
        {
            if (!IsAlive(entity)) return;
            
            Type? componentType = ComponentTypeRegistry.GetType(typeId);
            if (componentType == null)
                throw new InvalidOperationException($"Component type ID {typeId} not registered");
            
            if (!_componentTables.TryGetValue(componentType, out var table))
                return; // Component not registered, nothing to remove
            
            // Cast to managed table and clear
            table.ClearRaw(entity.Index);
            
            // Clear component mask bit
            ref var header2c = ref _entityIndex.GetComponentMask(entity.Index);
            header2c.ClearBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion;
        }
        
        // =========================================================
        // STAGE 18: SINGLETON API (Global Components)
        // =========================================================
        
        /// <summary>
        /// Sets or updates a global singleton component (unmanaged).
        /// Allocates Tier 1 memory if it doesn't exist.
        /// Use this for struct/value types that you want ref access to.
        /// </summary>
        public void SetSingletonUnmanaged<T>(in T value) where T : unmanaged
        {
            int typeId = ComponentType<T>.ID;  // Will auto-register via ComponentType<T>
            EnsureSingletonCapacity(typeId);
            
            // Lazy allocation
            if (_singletons[typeId] == null)
            {
                // Tier 1: Create table with just one slot
                var table = new ComponentTable<T>();
                _singletons[typeId] = table;
            }
            
            // Write data
            var storage = (ComponentTable<T>)_singletons[typeId];
            storage.Set(0, value, _globalVersion);
        }
        
        /// <summary>
        /// Sets or updates a global singleton component (managed).
        /// Allocates Tier 2 memory if it doesn't exist.
        /// Use this for class/reference types.
        /// </summary>
        public void SetSingletonManaged<T>(T value) where T : class
        {
            int typeId = ManagedComponentType<T>.ID;  // Will auto-register via ManagedComponentType<T>
            EnsureSingletonCapacity(typeId);
            
            // Lazy allocation
            if (_singletons[typeId] == null)
            {
                // Tier 2: Create managed table
                var table = new ManagedComponentTable<T>();
                _singletons[typeId] = table;
            }
            
            // Write data
            var storage = (ManagedComponentTable<T>)_singletons[typeId];
            storage[0] = value;
        }
        
        /// <summary>
        /// Gets a reference to the global singleton component (unmanaged structs only).
        /// Throws if not set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetSingletonUnmanaged<T>() where T : unmanaged
        {
            int typeId = ComponentType<T>.ID;
            
            #if FDP_PARANOID_MODE
            if (typeId >= _singletons.Length || _singletons[typeId] == null)
                throw new InvalidOperationException(
                    $"Singleton {typeof(T).Name} not set. Call SetSingletonUnmanaged<{typeof(T).Name}>() first.");
            #endif
            
            // Fast path: direct cast and access
            var storage = (ComponentTable<T>)_singletons[typeId];
            // GetRW with version 0 guarantees access but doesn't bump version to current global.
            // This mimics legacy behavior but beware of delta recording issues if modified via ref.
            return ref storage.GetRW(0, 0);
        }
        
        /// <summary>
        /// Gets the global singleton component (managed classes).
        /// Throws if not set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T? GetSingletonManaged<T>() where T : class
        {
            int typeId = ManagedComponentType<T>.ID;
            
            #if FDP_PARANOID_MODE
            if (typeId >= _singletons.Length || _singletons[typeId] == null)
                throw new InvalidOperationException(
                    $"Singleton {typeof(T).Name} not set. Call SetSingletonManaged<{typeof(T).Name}>() first.");
            #endif
            
            var storage = (ManagedComponentTable<T>)_singletons[typeId]!;
            return storage[0];
        }
        
        /// <summary>
        /// Checks if a singleton has been set (Unmanaged).
        /// </summary>
        public bool HasSingletonUnmanaged<T>() where T : unmanaged
        {
            int typeId = ComponentType<T>.ID;
            if (typeId >= _singletons.Length) return false;
            return _singletons[typeId] != null;
        }
        
        /// <summary>
        /// Checks if a managed singleton has been set.
        /// </summary>
        public bool HasSingletonManaged<T>() where T : class
        {
            int typeId = ManagedComponentType<T>.ID;
            if (typeId >= _singletons.Length) return false;
            return _singletons[typeId] != null;
        }

        /// <summary>
        /// Returns all active singleton tables.
        /// Used by Flight Recorder.
        /// </summary>
        public IEnumerable<IComponentTable> GetSingletonTables()
        {
            for (int i = 0; i < _singletons.Length; i++)
            {
                if (_singletons[i] != null)
                {
                    yield return (IComponentTable)_singletons[i];
                }
            }
        }

        /// <summary>
        /// Gets a singleton table by Type ID.
        /// Used by PlaybackSystem to restore data.
        /// </summary>
        internal IComponentTable? GetSingletonTable(int typeId)
        {
            if (typeId < 0 || typeId >= _singletons.Length) return null;
            return (IComponentTable?)_singletons[typeId];
        }
        
        /// <summary>
        /// Ensures singleton array has capacity for the given type ID.
        /// </summary>
        private void EnsureSingletonCapacity(int typeId)
        {
            if (typeId >= _singletons.Length)
            {
                int newSize = Math.Max(_singletons.Length * 2, typeId + 1);
                Array.Resize(ref _singletons, newSize);
            }
        }

        // =========================================================
        // UNIFIED SINGLETON API
        // =========================================================

        /// <summary>
        /// Sets a singleton component (Managed or Unmanaged).
        /// </summary>
        public void SetSingleton<T>(T value)
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                // Unbox/Cast via UnsafeShim logic would be ideal if T is constrained
                // But here we can't easily cast T to unmanaged without constraint.
                // We use our UnsafeShim helper pattern.
                UnsafeShim.SetSingletonUnmanaged(this, value);
            }
            else
            {
                UnsafeShim.SetSingletonManaged(this, value);
            }
        }

        /// <summary>
        /// Gets a singleton component (Managed or Unmanaged).
        /// Throws if missing.
        /// </summary>
        public ref T GetSingleton<T>()
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                return ref UnsafeShim.GetSingletonUnmanaged<T>(this);
            }
            else
            {
                // For managed, we return ref to table entry? 
                // GetSingletonManaged returns T? validation issues with ref return of class.
                // We return ref T via shim.
                return ref UnsafeShim.GetSingletonManaged<T>(this);
            }
        }

        /// <summary>
        /// Checks if singleton exists.
        /// </summary>
        public bool HasSingleton<T>()
        {
            if (ComponentTypeHelper.IsUnmanaged<T>())
            {
                return UnsafeShim.HasSingletonUnmanaged<T>(this);
            }
            else
            {
                return UnsafeShim.HasSingletonManaged<T>(this);
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;

            if (_selfHandle.IsAllocated)
                _selfHandle.Free();

            Bus?.Dispose();

            // Dispose all component tables
            foreach (var table in _componentTables.Values)
            {
                table?.Dispose();
            }
            _componentTables.Clear();
            
            // Dispose singletons
            for (int i = 0; i < _singletons.Length; i++)
            {
                if (!_borrowedSingletons.IsSet(i) && _singletons[i] is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _singletons[i] = default!;
            }
            _borrowedSingletons.Clear();
            
            // Dispose metadata
            _metadata?.Dispose();
            
            // Dispose entity index
            _entityIndex?.Dispose();

            // Dispose ThreadLocal Command Buffers (BATCH-05 Fix)
            // Since trackAllValues: true was enabled, we can clean up all created instances.
            if (_perThreadCommandBuffer != null)
            {
                foreach (var buffer in _perThreadCommandBuffer.Values)
                {
                    buffer.Dispose();
                }
                _perThreadCommandBuffer.Dispose();
            }

            _disposed = true;
        }
    }
}
