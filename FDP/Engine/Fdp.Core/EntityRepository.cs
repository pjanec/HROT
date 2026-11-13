using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
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
        // ⭐ CE-3183 — a WEAK handle: it gives access while something holds the world (every reader recovers it inside a
        //   tick, from a bridge HsmRunner fills from the live repo), and never keeps it alive. ⛔ It was Normal (strong):
        //   a world nobody disposed was rooted for the life of the process, so its NativeChunkTable finalizers never ran
        //   and every table's mmap reservation stayed — measured: 484 undisposed test worlds, 56 240 mappings against
        //   vm.max_map_count 65 530, then OutOfMemoryException wherever the next allocation landed.
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
        private long _destructionBase;      // ⭐ CE-3335 — the mark of _destructionLog[0] (every entry ever logged has one mark)
        private int _destructionKeepFrom;   // ⭐ CE-3335 — where this frame's deaths start; Tick() drops what came before the LAST frame
        
        // Stage 18: Singleton Storage
        // Index = ComponentType<T>.ID
        // Value = NativeChunkTable<T> (capacity 1) or ManagedComponentTable<T> (capacity 1)
        private object[] _singletons;
        
        // Versioning for Delta Iterator (Stage 11/16)
        // NGS-0.1: Semantic split — two clocks.
        // Invariant: _globalVersion >= _simulationTick always.
        // Both start equal and are bumped together by Tick().
        // Only _globalVersion is bumped by BumpMemoryVersion() (sub-tick debug granularity).
        private uint _globalVersion = 1;
        private uint _simulationTick = 1;
        
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
        
        private static int _liveInstanceCount;

        /// <summary>
        /// ⭐⭐⭐ <c>QA-004</c> — <b>how many <see cref="EntityRepository"/> instances are alive right now</b>
        /// (constructed and not yet <see cref="Dispose"/>d).
        ///
        /// <para>⛔ A leaked repository is INVISIBLE without this: it throws nothing, logs nothing, and
        /// fails no assertion — it just holds an <c>int[1_000_000]</c> free list plus one
        /// <c>NativeChunkTable</c> per registered component until the process dies. 📐 Measured
        /// 2026-08-26: accumulated leaks exhausted a 16 GB box mid-run and aborted the integration
        /// test host, which had been read as "flaky tests" for ~40 batches.</para>
        ///
        /// <para>⭐ So the count is the INSTRUMENT: a teardown rail asserts it returns to its pre-boot
        /// value, which turns "did anything leak a world?" from an argument into a number.
        /// ⚠ Process-wide and therefore only meaningful when read on a serialized path — compare a
        /// before/after pair inside one test, ⛔ never assert an absolute value.</para>
        /// </summary>
        public static int LiveInstanceCount => Volatile.Read(ref _liveInstanceCount);

        // ── QA-004: opt-in leak tracker ───────────────────────────────────────────────────────────
        // OFF unless FDP_TRACK_REPO_LEAKS=1, so the normal path pays one already-cached bool read.
        private static readonly bool _trackLeaks =
            Environment.GetEnvironmentVariable("FDP_TRACK_REPO_LEAKS") == "1";

        // EntityRepository does not override Equals/GetHashCode, so the default comparer IS reference
        // identity — which is what a per-instance origin table needs.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityRepository, string>
            _liveOrigins = new();

        /// <summary>
        /// ⭐⭐ <c>QA-004</c> — with <c>FDP_TRACK_REPO_LEAKS=1</c>, the construction stack of every
        /// still-live repository.
        ///
        /// <para>⛔ <see cref="LiveInstanceCount"/> answers <i>"did something leak?"</i> and stops there
        /// — 📐 measured 2026-08-26, one harness round-trip leaked <b>ten</b> repositories per node and
        /// the count alone could not say which ten. ⭐ This answers <i>"leaked from WHERE"</i>, which is
        /// the difference between a number and a fix.</para>
        /// </summary>
        public static string[] DumpLiveOrigins()
        {
            if (!_trackLeaks)
                return new[] { "FDP_TRACK_REPO_LEAKS is not set to 1 — no origins were recorded." };

            var origins = new System.Collections.Generic.List<string>();
            foreach (var kv in _liveOrigins) origins.Add(kv.Value);
            return origins.ToArray();
        }

        /// <summary>⭐ A-1 — this repository's id in the access trace (0 when the switch is off).</summary>
        internal readonly int TraceId = AccessTrace.On ? AccessTrace.NextRepoId() : 0;

        public EntityRepository()
        {
            Interlocked.Increment(ref _liveInstanceCount);
            if (_trackLeaks) _liveOrigins[this] = Environment.StackTrace;
            Bus = new FdpEventBus();
            if (AccessTrace.On) { AccessTrace.LabelBus(Bus.TraceId, TraceId); AccessTrace.RegisterRepo(TraceId, this); }
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);   // ⭐ CE-3183 — never roots the world
            _entityIndex = new EntityIndex();
            _componentTables = new Dictionary<Type, IComponentTable>();
            if (MemoryCensus.On) MemoryCensus.Track(this);   // ⭐ CE-4456 N-1
            _metadata = new ComponentMetadataTable();
            _singletons = new object[64]; // Start with 64 slots, will grow if needed
            _phaseConfig.BuildCache();  // Build ID cache at initialization
        }
        
        /// <summary>
        /// Raw unmanaged handle to this repository. Valid for passing through
        /// <c>unmanaged</c>-constrained contexts (e.g. <c>HsmKernelBridge</c>).
        /// Recover via: <c>(EntityRepository)GCHandle.FromIntPtr(handle).Target!</c>
        /// Remains valid until <see cref="Dispose"/> is called, while the repository is reachable — ⭐ <c>CE-3183</c>: the handle
        /// is weak, so read it only from code that holds the repository (the bridge is filled per tick from the live world).
        /// </summary>
        public IntPtr UnmanagedHandle => GCHandle.ToIntPtr(_selfHandle);

        /// <summary>
        /// Total number of active entities.
        /// </summary>
        public int EntityCount => _entityIndex.ActiveCount;

        private long _structuralChanges;

        // ⭐ CE-2418 — the query-candidate index and its scope (docs/DESIGN_Egress_Change_Detection.md §6)
        private QueryCandidateIndex? _candidateIndex;
        private int _queryIndexDepth;
        private bool _candidatesBuiltInScope;   // at most ONE build per scope: a live enumerator's array is never rewritten under it

        /// <summary>⭐ <c>CE-2418</c> — while the returned scope lives, a query on THIS repository walks only the entities of its rarest included
        /// type (a candidate index built from the masks in one pass, voided by any structural change) instead of every entity. For a burst of
        /// queries on one thread (the egress pass). Nests; dispose it.</summary>
        public QueryIndexScopeHandle QueryIndexScope() { _queryIndexDepth++; return new QueryIndexScopeHandle(this); }

        /// <summary>
        /// ⭐ CE-3427 N-1 — inside a <see cref="QueryIndexScope"/>: builds the candidate index NOW, so the scope's queries may run on several
        /// threads at once (each then only reads it). ⚠ Only while nothing changes this repository's structure — the off-frame egress calls it
        /// on its replica, which nothing writes during the scan. ⛔ The scope's depth counter itself is not thread-safe: open the scope on ONE
        /// thread around the parallel work.
        /// </summary>
        public void PrepareQueryIndex()
        {
            if (_queryIndexDepth == 0) return;
            TryQueryCandidates(default, out _, out _, out _);
        }

        /// <summary>Ends a <see cref="QueryIndexScope"/>.</summary>
        public readonly struct QueryIndexScopeHandle : IDisposable
        {
            private readonly EntityRepository? _repo;
            internal QueryIndexScopeHandle(EntityRepository repo) => _repo = repo;
            public void Dispose()
            {
                if (_repo == null || _repo._queryIndexDepth == 0) return;
                if (--_repo._queryIndexDepth == 0) _repo._candidatesBuiltInScope = false;
                // ⭐ the build is KEPT across scopes of the same frame: every egress system of the Export phase shares it (📐 a build per
                //   scope cancelled the saving — CE-2418's first A/B); a new frame, a structural change or a wholesale mask rewrite voids it
            }
        }

        /// <summary>Inside a scope: the candidates of <paramref name="include"/>'s rarest type (the index rebuilt when stale). False outside one,
        /// or when the mask includes nothing — the caller walks every entity.</summary>
        internal bool TryQueryCandidates(in BitMask512 include, out int[] entities, out int from, out int count)
        {
            entities = Array.Empty<int>(); from = 0; count = 0;
            if (_queryIndexDepth == 0) return false;
            var index = _candidateIndex ??= new QueryCandidateIndex();
            if (!index.IsFresh(this))
            {
                // a structural change inside the scope: walk everything for the rest of it (an enumerator may still hold the array)
                if (_candidatesBuiltInScope) return false;
                index.Build(this);
                _candidatesBuiltInScope = true;
            }
            return index.Rarest(include, out entities, out from, out count);
        }

        /// <summary>
        /// ⭐ <c>CE-2411</c> — a counter that moves on every STRUCTURAL change: an entity created, hydrated or destroyed, a component coming
        /// or going, a lifecycle / DIS-type change (every <c>LastChangeTick</c> stamp, plus destroy). Never on a value write.
        /// <para>For a per-frame index (<c>FrameEntityGrid</c>, the parts index) to know the world's SHAPE moved within the frame — where the
        /// entity count cannot tell (a destroy and a create recycle the same index). Monotonic; not recorded, not restored.</para>
        /// </summary>
        public long StructuralChanges => _structuralChanges;

        private long[] _addedByType = new long[64];

        private void BumpType(int typeId, int entityIndex)
        {
            if ((uint)typeId >= (uint)_addedByType.Length) Array.Resize(ref _addedByType, Math.Max(typeId + 1, _addedByType.Length * 2));
            _addedByType[typeId]++;
            if ((uint)typeId < (uint)_addLog.Length && _addLog[typeId] is { } log) log.Note(entityIndex, _globalVersion);
        }

        // ⭐ CE-4395 — WHICH entities gained a component, for the types a per-frame index asked about (LogAddsOf). Index-keyed, per GlobalVersion.
        private AddLog?[] _addLog = Array.Empty<AddLog?>();

        private sealed class AddLog
        {
            public int[] Indices = new int[16];
            public int Count;
            public uint Version;
            public void Note(int entityIndex, uint version)
            {
                if (version != Version) { Version = version; Count = 0; }
                if (Count == Indices.Length) Array.Resize(ref Indices, Count * 2);   // allocation on growth only
                Indices[Count++] = entityIndex;
            }
        }

        private void ClearAddLogs()
        {
            foreach (var log in _addLog) if (log != null) { log.Count = 0; log.Version = uint.MaxValue; }
        }

        /// <summary>
        /// ⭐ <c>CE-4395</c> — from now on, note the entity INDEX of every add of a component of type id <paramref name="typeId"/> (the same adds
        /// <see cref="ComponentsAddedOf"/> counts), so a per-frame index can take the parts added within the frame instead of rebuilding over
        /// every entity (📐 the parts index: 27 full rebuilds a frame at 3 000 men). Idempotent. Not recorded, not restored.
        /// </summary>
        public void LogAddsOf(int typeId)
        {
            if ((uint)typeId >= (uint)_addLog.Length) Array.Resize(ref _addLog, Math.Max(typeId + 1, Math.Max(64, _addLog.Length * 2)));
            _addLog[typeId] ??= new AddLog { Version = _globalVersion };
        }

        /// <summary>
        /// ⭐ <c>CE-4395</c> — the entity indices that gained a component of type id <paramref name="typeId"/> at the current
        /// <see cref="GlobalVersion"/>, in order (an index may repeat: added, removed, added). Empty for a type nobody asked to log
        /// (<see cref="LogAddsOf"/>) — ⚠ the caller compares its length with <see cref="ComponentsAddedOf"/>'s delta, never trusts it alone.
        /// </summary>
        public ReadOnlySpan<int> AddsThisVersion(int typeId)
        {
            if ((uint)typeId >= (uint)_addLog.Length || _addLog[typeId] is not { } log || log.Version != _globalVersion) return default;
            return new ReadOnlySpan<int>(log.Indices, 0, log.Count);
        }

        /// <summary>
        /// ⭐ <c>CE-2412</c> — a counter that moves whenever a component of type id <paramref name="typeId"/> is ADDED to an entity (it was not
        /// there). For a per-frame index over one component's entities (the parts index over <c>PartMetadata</c>) to learn, cheaply and
        /// exactly, that a new member may have joined within the frame — removals and destroys need no signal (a reader re-checks every
        /// hit live). Monotonic; not recorded, not restored.
        /// </summary>
        public long ComponentsAddedOf(int typeId) => (uint)typeId < (uint)_addedByType.Length ? _addedByType[typeId] : 0L;
        
        /// <summary>
        /// Highest entity index ever issued (for iteration bounds).
        /// </summary>
        public int MaxEntityIndex => _entityIndex.MaxIssuedIndex;
        
        /// <summary>
        /// Current global change version. Incremented by Tick().
        /// Also incremented alone (without SimulationTick) by BumpMemoryVersion() for sub-tick debug granularity.
        /// </summary>
        public uint GlobalVersion => _globalVersion;

        /// <summary>⭐ M-1 (DESIGN_Parallel_Simulation §8.3) — the component tables, for the write instrument only (allocates; instrument path).</summary>
        internal List<(Type, IComponentTable)> TablesForStats()
        {
            var list = new List<(Type, IComponentTable)>(_componentTables.Count);
            foreach (var kv in _componentTables) list.Add((kv.Key, kv.Value));
            return list;
        }

        /// <summary>
        /// Semantic frame clock. Incremented only by Tick() — frozen during a mid-tick debug burst.
        /// Frame-index / wall-tick consumers must read this, NOT GlobalVersion.
        /// </summary>
        public uint SimulationTick => _simulationTick;

        /// <summary>
        /// Increments both the memory clock and the frame clock. Should be called at start of frame.
        /// </summary>
        public void Tick()
        {
            System.Threading.Interlocked.Increment(ref _globalVersion);
            System.Threading.Interlocked.Increment(ref _simulationTick);
            _lastTickFresh = false;   // ⭐ N-15 — a new tick: the last-tick view must be taken again before a per-entity phase reads it
            TrimDestructionLog();   // ⭐ CE-3335
        }

        /// <summary>
        /// Advances ONLY the memory clock (_globalVersion) for sub-tick dirty granularity during debug.
        /// The frame clock (_simulationTick) is left frozen at its current value.
        /// Call once per blueprint node during a debug session to stamp node-level chunk versions.
        /// Invariant: _globalVersion >= _simulationTick always.
        /// </summary>
        public void BumpMemoryVersion()
        {
#if DEBUG
            System.Diagnostics.Debug.Assert(_globalVersion >= _simulationTick,
                "BumpMemoryVersion: invariant _globalVersion >= _simulationTick violated before bump.");
#endif
            System.Threading.Interlocked.Increment(ref _globalVersion);
        }

        /// <summary>
        /// Force sets both version clocks to the same value. Used by PlaybackSystem.
        /// Keeping both aligned ensures restored frames start with the invariant intact.
        /// </summary>
        internal void SetGlobalVersion(uint version)
        {
            ClearAddLogs();   // ⭐ CE-4395 — a version set back must not make an old version's adds read as this one's
            _globalVersion = version;
            _simulationTick = version;
            _contentEpoch++;   // ⭐ M-4 (CE-4483) — a replica may no longer trust a version match
        }

        /// <summary>
        /// Resets both version clocks to the specified value (default: 1).
        /// Intended for test setup so that version-dependent assertions start from a known baseline,
        /// regardless of how many component registrations or internal ticks occurred during construction.
        /// </summary>
        public void ResetGlobalVersion(uint version = 1)
        {
            ClearAddLogs();   // ⭐ CE-4395 — a version set back must not make an old version's adds read as this one's
            _globalVersion = version;
            _simulationTick = version;
            _contentEpoch++;   // ⭐ M-4 (CE-4483) — a replica may no longer trust a version match
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
            if (AccessTrace.On) AccessTrace.RecordName(TraceKind.EntityCreate, "(entity: hydrate)", TraceId);
             // 1. Delegate to Index to handle allocation/state
             _entityIndex.ForceRestoreEntity(id, true, generation, default);
             
             // 2. Set default metadata
             ref var meta = ref _entityIndex.GetMetadata(id);
             meta.LifecycleState = EntityLifecycle.Active;
             meta.LastChangeTick = _globalVersion; _structuralChanges++;
             
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
            // ⭐ N-10 / F-8 — in a phase the creation is DEFERRED: the batch's placeholder (remapped at playback); its components added in the same
            //   batch ride the same buffer, and it is usable from the next tick
            if (ParallelReadCheck.Defers(this)) return ParallelReadCheck.CurrentBatch!.Commands.CreateEntity();
            if (AccessTrace.On) AccessTrace.RecordName(TraceKind.EntityCreate, "(entity)", TraceId);
            var entity = _entityIndex.CreateEntity();
            ref var meta = ref _entityIndex.GetMetadata(entity.Index);
            meta.LastChangeTick = _globalVersion; _structuralChanges++;
            if (EntityCensus.On) EntityCensus.Created(TraceId, entity.Index);   // ⭐ N-1 — a read of it later this frame is counted
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
            if (AccessTrace.On) AccessTrace.RecordNameE(TraceKind.Write, "(entity lifecycle state)", TraceId, entity.Index);
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            
            ref var meta = ref _entityIndex.GetMetadata(entity.Index);
            meta.LifecycleState = state;
            meta.LastChangeTick = _globalVersion; _structuralChanges++;
        }

        /// <summary>
        /// Gets the lifecycle state of an entity.
        /// </summary>
        public EntityLifecycle GetLifecycleState(Entity entity)
        {
            if (AccessTrace.On) AccessTrace.RecordNameE(TraceKind.Read, "(entity lifecycle state)", TraceId, entity.Index);
            if (!IsAlive(entity)) throw new InvalidOperationException($"Entity {entity} is not alive");
            return _entityIndex.GetMetadata(entity.Index).LifecycleState;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3335</c> — the destruction log as ONE log with a mark per reader: <see cref="ReadDestructions"/> hands a reader every
        /// death since ITS mark and moves the mark, so no reader empties the log under another. <see cref="Tick"/> (the start of a frame)
        /// keeps the last frame's deaths and this one's, so a reader that reads once a frame — anywhere in it, or between frames — misses
        /// nothing, and the log stays bounded when nothing records. ⛔ SUPERSEDED: the recorder cleared the log inside the tick, so a reader
        /// after it saw nothing, and with no recorder the log grew forever. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12o.
        /// </summary>
        public long DestructionMark => _destructionBase + _destructionLog.Count;

        /// <summary>⭐ CE-3335 — the deaths since <paramref name="mark"/> (clamped to what is kept), and the mark moved past them. A new
        /// reader starts from <see cref="DestructionMark"/>.</summary>
        public ReadOnlySpan<Entity> ReadDestructions(ref long mark)
        {
            long end = DestructionMark;
            long from = Math.Clamp(mark, _destructionBase, end);
            mark = end;
            return CollectionsMarshal.AsSpan(_destructionLog).Slice((int)(from - _destructionBase));
        }

        /// <summary>
        /// The deaths the log still holds (this frame's and the last one's, or since the last <see cref="ClearDestructionLog"/>).
        /// ⚠ A reader on a LIVE world uses <see cref="ReadDestructions"/> with its own mark; this is for a world one owner steps and clears.
        /// </summary>
        public IReadOnlyList<Entity> GetDestructionLog() => _destructionLog;
    
        /// <summary>
        /// Empties the log (the marks move on). For a world ONE owner steps frame by frame (a playback sandbox); ⛔ never on a live world —
        /// another reader would miss the deaths (CE-3335).
        /// </summary>
        public void ClearDestructionLog()
        {
            _destructionBase += _destructionLog.Count;
            _destructionLog.Clear();
            _destructionKeepFrom = 0;
        }

        /// <summary>⭐ CE-3335 — at the start of a frame: drop the deaths from before the last frame.</summary>
        private void TrimDestructionLog()
        {
            if (_destructionKeepFrom > 0)
            {
                _destructionLog.RemoveRange(0, _destructionKeepFrom);
                _destructionBase += _destructionKeepFrom;
            }
            _destructionKeepFrom = _destructionLog.Count;
        }

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
            meta.LastChangeTick = _globalVersion; _structuralChanges++;
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

        /// <summary>
        /// ⭐ CE-3329 W1 — moves on every <see cref="Clear"/>: the world was replaced IN PLACE (a keyframe applied — a replay
        /// seek, a checkpoint / dry-run restore, a review's entry or exit — or the world emptied). Every in-place replacement
        /// goes through <see cref="Clear"/>, so a consumer that must notice one compares this instead of re-scanning the
        /// world each frame. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12b.
        /// </summary>
        public uint ClearGeneration => _clearGeneration;
        private uint _clearGeneration;

        internal void Clear()
        {
            _clearGeneration++;
            _contentEpoch++;   // ⭐ M-4 — a replica may no longer trust a version match
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
            if (ParallelReadCheck.Defers(this)) { ParallelReadCheck.CurrentBatch!.Commands.DestroyEntity(entity); return; }   // ⭐ N-10 / F-6 — deferred, merged in batch order
            if (AccessTrace.On) AccessTrace.RecordNameE(TraceKind.EntityDestroy, "(entity)", TraceId, entity.Index);
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
            _structuralChanges++;
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
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.Has, typeId, TraceId, entity.Index);
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
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawRead, typeId, TraceId, entity.Index);
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
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawRead, typeId, TraceId, entity.Index);
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
        /// <item>Classes: Default to DataPolicy.NoPreview (Safe safety rail)</item>
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
                
                bool snapshot = !effectivePolicy.HasFlag(DataPolicy.NoPreview);
                bool record = !effectivePolicy.HasFlag(DataPolicy.NoReplay);
                bool save = !effectivePolicy.HasFlag(DataPolicy.NoScenario);
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
                    
                    bool snapshot = !policy.HasFlag(DataPolicy.NoPreview);
                    bool record = !policy.HasFlag(DataPolicy.NoReplay);
                    bool save = !policy.HasFlag(DataPolicy.NoScenario);
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
                        // Mutable Class → Auto-default to NoPreview
                        effectivePolicy = DataPolicy.NoPreview;
                        
                        #if DEBUG
                        // Console.WriteLine($"WARNING: Mutable class '{type.Name}' registered without [DataPolicy]. Defaulting to NoPreview.");
                        #endif
                    }
                }
                
                // Apply flags
                bool finalSnapshot = !effectivePolicy.HasFlag(DataPolicy.NoPreview);
                bool finalRecord = !effectivePolicy.HasFlag(DataPolicy.NoReplay);
                bool finalSave = !effectivePolicy.HasFlag(DataPolicy.NoScenario);
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
            bool found;
            if (ThreadedReads.PureReads) found = TryGetComponentPure<T>(entity, out component);
            else if (HasComponent<T>(entity))
            {
                if (AccessTrace.On)
                {
                    // ⭐ A-1 — TryGet takes the RW path (it bumps the chunk version): ONE event, kind TryGetRW, the inner write hidden
                    AccessTrace.RecordE<T>(TraceKind.TryGetRW, TraceId, entity.Index);
                    AccessTrace.Mute();
                    try { component = GetComponentRW<T>(entity); } finally { AccessTrace.Unmute(); }
                }
                else component = GetComponentRW<T>(entity);
                found = true;
            }
            else { component = default!; found = false; }
            if (ManagedMutationCheck.On && found && !ComponentTypeHelper.IsUnmanaged<T>()) NoteManagedRead(entity, component);   // ⭐ CE-4445 M-2
            return found;
        }

        /// <summary>⭐ CE-4445 M-2 — a managed READ handed out <paramref name="obj"/>: the detector fingerprints it (only while it is on).</summary>
        internal void NoteManagedRead<T>(Entity entity, T obj)
        {
            if (obj is null || !ManagedMutationCheck.On) return;
            if (_componentTables.TryGetValue(typeof(T), out var table)) ManagedMutationCheck.OnRead(table, entity.Index, obj);
        }

        /// <summary>POC 2 / P-1 - <see cref="TryGetComponent{T}"/> through the read-only path: a copy, no chunk version stamped.</summary>
        internal bool TryGetComponentPure<T>(Entity entity, out T component)
        {
            if (HasComponent<T>(entity))
            {
                component = ComponentTypeHelper.IsUnmanaged<T>()
                    ? UnsafeShim.GetUnmanagedRO<T>(this, entity)
                    : UnsafeShim.GetManagedROPure<T>(this, entity);
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
        /// ⭐ <c>CE-3243</c> — sets the component bit and stamps <c>LastChangeTick</c> ONLY when the bit was not already set.
        /// <c>LastChangeTick</c> is the entity's STRUCTURAL clock (create, destroy, a component coming or going, lifecycle): the
        /// flight recorder rewrites the entity-index chunk and every table chunk holding a structurally changed entity, and
        /// <c>QueryDelta</c> treats it as "changed in every component". A VALUE write is already in the table's chunk version —
        /// the rule <see cref="GetComponentRW{T}"/> documents ("over-broad dirtying"). 📐 Value writes through
        /// <c>SetComponent</c> / the command buffer stamped it on 58–391 entities a frame, so the recorder rewrote ~180–270 whole
        /// table chunks a frame that had not changed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StampIfAdded(ref BitMask512 mask, int typeId, int entityIndex)
        {
            if (mask.IsSet(typeId)) return;
            mask.SetBit(typeId);
            _entityIndex.GetMetadata(entityIndex).LastChangeTick = _globalVersion; _structuralChanges++;
            BumpType(typeId, entityIndex);
        }

        /// <summary>
        /// Adds an unmanaged component (Tier 1) to an entity.
        /// Updates ComponentMask automatically.
        /// </summary>
        internal void AddUnmanagedComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            if (ParallelReadCheck.Defers(this) && DeferUnmanaged(entity, in component)) return;   // ⭐ N-10 / F-6
            if (AccessTrace.On) AccessTrace.RecordE<T>(_entityIndex.GetComponentMask(entity.Index).IsSet(ComponentType<T>.ID) ? TraceKind.Write : TraceKind.StructAdd, TraceId, entity.Index);
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
            // ⭐ CE-3243 — LastChangeTick is STRUCTURAL: stamped when the component COMES, not on every value write (SetComponent
            //   is this method). The value change is in the table's chunk version (table.Set above). See StampIfAdded.
            StampIfAdded(ref compMask, ComponentType<T>.ID, entity.Index);
        }
        
        /// <summary>
        /// Removes an unmanaged component (Tier 1) from an entity.
        /// Updates ComponentMask automatically.
        /// Component data remains in table but is inaccessible.
        /// </summary>
        internal void RemoveUnmanagedComponent<T>(Entity entity) where T : unmanaged
        {
            if (ParallelReadCheck.Defers(this)) { ParallelReadCheck.CurrentBatch!.Commands.RemoveComponent<T>(entity); return; }   // ⭐ N-10 / F-6 — deferred
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.StructRemove, TraceId, entity.Index);
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            if (!HasUnmanagedComponent<T>(entity))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
            #endif
            
            // Clear component mask bit
            ref var compMask = ref _entityIndex.GetComponentMask(entity.Index);
            compMask.ClearBit(ComponentType<T>.ID);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
            
            // Note: Component data is not cleared for performance
            // It will be overwritten if component is re-added
        }
        
        /// <summary>
        /// Checks if entity has a specific unmanaged component (Tier 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasUnmanagedComponent<T>(Entity entity) where T : unmanaged
        {
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Has, TraceId, entity.Index);
            return HasUnmanagedCore<T>(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasUnmanagedCore<T>(Entity entity) where T : unmanaged
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
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Write, TraceId, entity.Index);
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
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Write, TraceId, entity.Index);
            if (ParallelReadCheck.Compiled && ParallelReadCheck.AnyPhase && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index)) ParallelReadCheck.CheckOtherWrite(this, typeof(T), entity);   // ⭐ N-10 / F-5 — another entity's value only by a command (its own parts excepted)
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
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Read, TraceId, entity.Index);
            if (ParallelReadCheck.AnyPhase && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index)) return ref ParallelReadCheck.OtherUnmanagedRO<T>(this, entity);   // ⭐ N-10 — another entity, in a phase: the last tick
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
            
            if (!HasUnmanagedCore<T>(entity))   // ⭐ A-1 — the untraced check: a Set is ONE write event, not a Has + a write
            {
                AddUnmanagedComponent(entity, component);
            }
            else
            {
                if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Write, TraceId, entity.Index);
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

            if (metaS.AuthorityMask.IsSet(typeId) == hasAuthority) return;   // ⭐ CE-4327 — no change, no stamp
            if (hasAuthority)
                metaS.AuthorityMask.SetBit(typeId);
            else
                metaS.AuthorityMask.ClearBit(typeId);
            StampAuthorityChange(ref metaS);
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

            if (metaG.AuthorityMask.IsSet(typeId) == hasAuthority) return;   // ⭐ CE-4327 — no change, no stamp
            if (hasAuthority)
                metaG.AuthorityMask.SetBit(typeId);
            else
                metaG.AuthorityMask.ClearBit(typeId);
            StampAuthorityChange(ref metaG);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4327</c> (debt <c>D004</c>, 📄 docs/UX/DESIGN_Review_Perspective.md §6.2 M3 / §6.4 I8) — an ownership HANDOVER is recorded: the authority
        /// mask lives in the entity's COLD metadata, and the recorder writes an entity's index chunks (hot + cold) only when its <c>LastChangeTick</c> moved
        /// (<c>RecorderSystem.ChunkHasStructuralChanges</c>). So a changed bit stamps that clock — a replay then carries the NEW owner from the handover
        /// frame, not the old one until the next keyframe. ⛔ <see cref="StructuralChanges"/> is NOT bumped: an owner change is not a change of the
        /// world's SHAPE, and the per-frame indices keyed on it need not rebuild.
        /// </summary>
        private void StampAuthorityChange(ref EntityMetadataCold meta) => meta.LastChangeTick = _globalVersion;
        
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
            AssertNotRegisteringInPhase(type);
            
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
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Has, TraceId, entity.Index);
            return HasManagedCore<T>(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasManagedCore<T>(Entity entity) where T : class
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
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Read, TraceId, entity.Index);
            if (ParallelReadCheck.AnyPhase && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index)) return ParallelReadCheck.OtherManagedRO<T>(this, entity);   // ⭐ N-10 — another entity, in a phase: the last tick
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
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Write, TraceId, entity.Index);
            if (ParallelReadCheck.Compiled && ParallelReadCheck.AnyPhase && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index)) ParallelReadCheck.CheckOtherWrite(this, typeof(T), entity);   // ⭐ N-10 / F-5 — another entity's value only by a command (its own parts excepted)
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            var table = GetManagedTable<T>(false);
            ref var val = ref table.GetRW(entity.Index, _globalVersion);
            return ref Unsafe.As<T?, T>(ref val);
        }
        
        /// <summary>POC 2 / P-1 - a managed component read as a REFERENCE that stamps no chunk version and allocates nothing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref readonly T GetManagedComponentRefRO<T>(Entity entity) where T : class
        {
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Read, TraceId, entity.Index);   // ⭐ N-1 — the pure managed read (the default since CE-4445) must be seen too
            if (ParallelReadCheck.AnyPhase && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index)) return ref ParallelReadCheck.OtherManagedRO<T>(this, entity);   // ⭐ N-10 — another entity, in a phase: the last tick
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            var table = GetManagedTable<T>(false);
                        return ref Unsafe.As<T?, T>(ref Unsafe.AsRef(in table.GetRefRO(entity.Index)));
        }

        /// <summary>
        /// Gets managed component for read-only access.
        /// Does not update version or allocate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T GetManagedComponentRO<T>(Entity entity) where T : class
        {
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.Read, TraceId, entity.Index);
            if (ParallelReadCheck.AnyPhase && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index)) return ParallelReadCheck.OtherManagedRO<T>(this, entity);   // ⭐ N-10 — another entity, in a phase: the last tick
            var table = GetManagedTable<T>(false);
            var v = table.GetRO(entity.Index)!;
            if (ManagedMutationCheck.On) ManagedMutationCheck.OnRead(table, entity.Index, v);   // ⭐ CE-4445 M-2
            return v;
        }
        
        /// <summary>
        /// Sets (or adds) a managed component.
        /// </summary>
        public void SetManagedComponent<T>(Entity entity, T value) where T : class
        {
            if (ParallelReadCheck.Defers(this) && DeferManaged(entity, value)) return;   // ⭐ N-10 / F-6
            if (AccessTrace.On) AccessTrace.RecordE<T>(value == null ? TraceKind.StructRemove : _entityIndex.GetComponentMask(entity.Index).IsSet(ManagedComponentType<T>.ID) ? TraceKind.Write : TraceKind.StructAdd, TraceId, entity.Index);
            #if FDP_PARANOID_MODE
            if (!IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive");
            #endif
            
            var table = GetManagedTable<T>(false);
            table.Set(entity.Index, value, _globalVersion);
            
            // Update component mask
            ref var compMaskM = ref _entityIndex.GetComponentMask(entity.Index);
            bool had = compMaskM.IsSet(ManagedComponentType<T>.ID);
            
            if (value != null)
                compMaskM.SetBit(ManagedComponentType<T>.ID);
            else
                compMaskM.ClearBit(ManagedComponentType<T>.ID);

            // ⭐ CE-3243 — structural stamp only when the component comes or goes (see StampIfAdded)
            if (had != (value != null))
            {
                _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
                BumpType(ManagedComponentType<T>.ID, entity.Index);
            }
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
            if (ParallelReadCheck.Defers(this)) { ParallelReadCheck.CurrentBatch!.Commands.RemoveManagedComponent<T>(entity); return; }   // ⭐ N-10 / F-6 — deferred
            if (!IsAlive(entity) || !HasManagedCore<T>(entity))
                return;
            
            if (AccessTrace.On) AccessTrace.RecordE<T>(TraceKind.StructRemove, TraceId, entity.Index);
            var table = GetManagedTable<T>(false);
            // CRITICAL FIX: Use Set(null) with version tracking instead of Clear/indexer
            // This ensures the removal is captured by Flight Recorder deltas.
            table.Set(entity.Index, null, _globalVersion);
            
            // Update component mask
            ref var compMaskR = ref _entityIndex.GetComponentMask(entity.Index);
            compMaskR.ClearBit(ManagedComponentType<T>.ID);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
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
                AssertNotRegisteringInPhase(type);
                
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentTable<T> GetTable<T>(bool allowCreate) where T : unmanaged
        {
            // ⭐ CE-2393 (DESIGN_Evaluation_Cost_And_Cadence §5a) — the id-indexed cache every table is already put in (below, and by the
            //   managed paths): an array read + an exact-type check, where every GetComponentRO / RW / Has-then-Get took a Dictionary<Type>
            //   lookup. The type check keeps it right if the registry re-numbered (a test's Clear()): a miss falls to the dictionary.
            //   📐 Crossfire trace: GetComponentRO was the largest part of HasAuthority, the egress scan's common cost.
            int id = ComponentType<T>.ID;
            var cache = _tableCache;
            if ((uint)id < (uint)cache.Length && cache[id] is ComponentTable<T> cached) return cached;
            return GetTableSlow<T>(allowCreate);
        }

        private ComponentTable<T> GetTableSlow<T>(bool allowCreate) where T : unmanaged
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
            
            AssertNotRegisteringInPhase(type);
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
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.TableAccess, TraceId);
            return GetTable<T>(false);
        }

        /// <summary>
        /// Tries to get the component table for a given type (Unmanaged or Managed).
        /// Returns false if not registered/found, unless allowCreate is true (and it's registered).
        /// Since this is generic-less, we assume type is valid.
        /// </summary>
        public bool TryGetTable(Type type, out IComponentTable table)
        {
            if (AccessTrace.On) AccessTrace.RecordType(TraceKind.TableAccess, type, TraceId);
            return _componentTables.TryGetValue(type, out table!);
        }
        /// WARNING: Direct access bypasses safety checks!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BitMask512 GetComponentMask(int entityIndex)
        {
            if (AccessTrace.On) AccessTrace.RecordName(TraceKind.EntityIndexRead, "(component mask)", TraceId);
            return ref _entityIndex.GetComponentMask(entityIndex);
        }

        /// <summary>
        /// Gets the cold metadata (generation, authority, lifecycle, etc.) for a raw entity index.
        /// WARNING: Direct access bypasses safety checks!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref EntityMetadataCold GetMetadata(int entityIndex)
        {
            if (AccessTrace.On) AccessTrace.RecordName(TraceKind.EntityIndexRead, "(entity metadata)", TraceId);
            return ref _entityIndex.GetMetadata(entityIndex);
        }
        
        /// <summary>⭐ A-1 — the entity index without a trace record (the query walk records its own).</summary>
        internal EntityIndex EntityIndexUntraced => _entityIndex;

        /// <summary>⭐ CE-4456 N-1 — what this world holds: entities, native bytes (component chunks + the index), managed slot bytes, tables.</summary>
        internal (long Entities, long Native, long ManagedSlots, int Tables) MemoryFootprint()
        {
            if (_disposed) return default;
            long native = _entityIndex.NativeBytes, managed = 0; int tables = 0;
            IComponentTable[] all;
            lock (_componentTables) { all = new IComponentTable[_componentTables.Count]; _componentTables.Values.CopyTo(all, 0); }
            foreach (var t in all) if (t is IMemorySized m) { native += m.NativeBytes; managed += m.ManagedSlotBytes; tables++; }
            return (_entityIndex.ActiveCount, native, managed, tables);
        }

        // ════ ⭐⭐ N-10 (CE-4472) — the per-entity rule's repository half ════

        private EntityRepository? _lastTick;

        /// <summary>The last-tick view (null until <see cref="CaptureLastTick"/>).</summary>
        internal EntityRepository? LastTickWorld => _lastTick;

        /// <summary>Has a last-tick view been taken (the kernel takes it every tick once anything is declared)?</summary>
        public bool HasLastTickView => _lastTick != null;

        /// <summary>
        /// ⭐ N-10 (R-4214 ④) — takes the LAST-TICK view: a shadow world holding this world's rows of every DECLARED type
        /// (<see cref="ParallelReadCheck.DeclaredMask"/> — the types some per-entity system writes in the phase and another reads), copied by changed
        /// chunk (§18.1: 21 types ≈ 2.6 ms on the 1 000-man CGF). Taken SERIALLY at the start of the Simulation phase, so it holds this tick's ingress,
        /// ghosts, UI jobs and deferred creations; during a per-entity phase a read of ANOTHER entity's declared type is served from it.
        /// </summary>
        public void CaptureLastTick()
        {
            _lastTick ??= new EntityRepository();
            _lastTick.SyncFrom(this, ParallelReadCheck.DeclaredMask, includeTransient: true);
            _lastTickFresh = true;
        }

        private bool _lastTickFresh;

        /// <summary>
        /// ⭐ N-15 (CE-4477) — the view a per-entity phase reads must be THIS tick's: takes it unless it was already taken since the last
        /// <see cref="Tick"/> (or a world sync replaced this world's content). The kernel takes it before every Simulation dispatch, so on a kernel
        /// world this costs nothing; a world no kernel captures (the editor's preview copy, a rail) gets it here. 📌 Measured: the phases used to
        /// capture only when NO view existed — the preview world kept its FIRST view, entities spawned later were missing from it, and their
        /// reads fell through to the live rows the other threads were writing ⇒ the stepped run's world hash depended on the thread count.
        /// </summary>
        public void EnsureLastTickView()
        {
            if (!_lastTickFresh || _lastTick == null) CaptureLastTick();
        }

        /// <summary>A live unmanaged row, with no hook (the read check's own fallback).</summary>
        internal ref readonly T LiveUnmanagedRO<T>(Entity entity) where T : unmanaged => ref GetTable<T>(false).GetRO(entity.Index);

        /// <summary>A live managed reference, with no hook.</summary>
        internal ref readonly T LiveManagedRefRO<T>(Entity entity) where T : class
            => ref Unsafe.As<T?, T>(ref Unsafe.AsRef(in GetManagedTable<T>(false).GetRefRO(entity.Index)));

        /// <summary>Has <paramref name="entity"/> an unmanaged <typeparamref name="T"/> (no hook)?</summary>
        internal bool HasUnmanagedComponentUntraced<T>(Entity entity) where T : unmanaged
            => entity.Index >= 0 && entity.Index <= _entityIndex.MaxIssuedIndex && _entityIndex.GetComponentMask(entity.Index).IsSet(ComponentType<T>.ID);

        /// <summary>⭐ N-11 — a component table is created only OUTSIDE a per-entity phase: the type tables are read by every batch thread
        /// without a lock, so registration is structural and belongs before the phase (<c>BrainPhaseTypes</c>). Checks on: a defect, thrown.</summary>
        private void AssertNotRegisteringInPhase(Type type)
        {
            if (ParallelReadCheck.Compiled && ParallelReadCheck.Defers(this))
                if (ParallelReadCheck.Survey) ParallelReadCheck.Note("register-in-phase", type); else throw new InvalidOperationException($"ParallelReadCheck: component type {type.Name} registered inside a per-entity phase — register it before the phase (BrainPhaseTypes)");
        }

        /// <summary>
        /// ⭐ N-11 (CE-4473; §18.1a "own-part") — is <paramref name="index"/> a PART of the entity this thread is processing
        /// (<c>PartMetadata.ParentEntity</c>: a brain's child sensors) — or ⭐ F-11 an entity the job owns besides it (a squad job's members,
        /// <see cref="ParallelReadCheck.OwnAlso"/>)? Its own are read and written directly, never served from the last tick.
        /// </summary>
        internal unsafe bool IsOwnPartOfCurrent(int index)
        {
            int current = ParallelReadCheck.Current;
            if (current < 0 || index < 0 || index > _entityIndex.MaxIssuedIndex) return false;
            if (ParallelReadCheck.OwnsAlso(index)) return true;   // ⭐ F-11 — a member of the squad this job runs (ParallelReadCheck.OwnAlso)
            if (!_entityIndex.GetComponentMask(index).IsSet(GlobalComponentIds.PartMetadata)) return false;
            var table = _tableCache[GlobalComponentIds.PartMetadata];
            if (table == null) return false;
            int owner = ((Entity*)table.GetRawPointer(index))->Index;
            return owner == current || ParallelReadCheck.OwnsAlso(owner);   // ⭐ M-6 (CE-4485) — owning an entity owns its parts (a station weapon's turret)
        }

        // F-5 / F-6 — in a phase: a value write on the processed entity's EXISTING component stays direct; a structural change (an add, or anything
        // on a placeholder) and, with the checks off, a write to another entity are deferred into the batch's buffer (merged in batch order).
        private bool DeferUnmanaged<T>(Entity entity, in T component) where T : unmanaged
        {
            bool other = ParallelReadCheck.IsOther(entity.Index) && !IsOwnPartOfCurrent(entity.Index);
            bool has = HasUnmanagedComponentUntraced<T>(entity);
            if (!other && has) return false;
            if (other && has && ParallelReadCheck.Compiled) ParallelReadCheck.CheckOtherWrite(this, typeof(T), entity);
            ParallelReadCheck.CurrentBatch!.Commands.AddComponent(entity, in component);
            return true;
        }

        private bool DeferManaged<T>(Entity entity, T value) where T : class
        {
            bool other = ParallelReadCheck.IsOther(entity.Index) && !IsOwnPartOfCurrent(entity.Index);
            bool has = entity.Index >= 0 && entity.Index <= _entityIndex.MaxIssuedIndex && _entityIndex.GetComponentMask(entity.Index).IsSet(ManagedComponentType<T>.ID);
            if (!other && has) return false;
            if (other && has && ParallelReadCheck.Compiled) ParallelReadCheck.CheckOtherWrite(this, typeof(T), entity);
            ParallelReadCheck.CurrentBatch!.Commands.SetManagedComponent(entity, value);
            return true;
        }

        /// <summary>
        /// Gets the EntityIndex for advanced scenarios (iteration, chunk access).
        /// </summary>
        public EntityIndex GetEntityIndex()
        {
            if (AccessTrace.On) AccessTrace.RecordName(TraceKind.EntityIndexRead, "(entity index)", TraceId);
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

        /// <summary>
        /// ⭐ <c>CE-2045</c> — the same tables as the CONCRETE dictionary, for the recorder's per-frame walk: <c>foreach</c> over it
        /// uses the struct enumerator, where the <see cref="IReadOnlyDictionary{TKey,TValue}"/> above boxes one every frame.
        /// </summary>
        internal Dictionary<Type, IComponentTable> ComponentTablesForRecording => _componentTables;
        
        // ================================================
        // QUERY API
        // ================================================
        
        /// <summary>
        /// Creates a new query builder for filtering entities.
        /// Use fluent API: repo.Query().With<Position>().Without<Velocity>().Build()
        /// </summary>
        public QueryBuilder Query()
        {
            // ⭐ CE-2400 (R-2084) — the one reusable builder when it is free (it is returned by Build()); a nested or concurrent Query() gets
            //   its own. 📐 Each Query().Build() allocated two ~300 B objects, and ~25 systems build their query every frame.
            if (Interlocked.CompareExchange(ref _queryBuilderBusy, 1, 0) == 0)
            {
                var b = _queryBuilder ??= new QueryBuilder(this, pooled: true);
                b.Reset();
                return b;
            }
            return new QueryBuilder(this);
        }

        private QueryBuilder? _queryBuilder;
        private int _queryBuilderBusy;
        private System.Collections.Concurrent.ConcurrentDictionary<EntityQuery.Key, EntityQuery>? _queries;   // made on the first Build()

        internal void ReturnQueryBuilder(QueryBuilder b)
        {
            if (ReferenceEquals(b, _queryBuilder)) Volatile.Write(ref _queryBuilderBusy, 0);
        }

        /// <summary>⭐ CE-2400 — the query this repository built for <paramref name="key"/> (queries are immutable), built on first ask.</summary>
        internal EntityQuery GetOrCreateQuery(in EntityQuery.Key key)
        {
            var map = _queries ?? LazyInitializer.EnsureInitialized(ref _queries, () => new System.Collections.Concurrent.ConcurrentDictionary<EntityQuery.Key, EntityQuery>())!;
            if (map.TryGetValue(key, out var q)) return q;
            q = new EntityQuery(this, in key);
            return map.GetOrAdd(key, q);
        }
        
        /// <summary>
        /// Iterates entities that match the query AND have changed since the specified version.
        /// Checks structural changes (EntityHeader) and component value changes (Chunk Version).
        /// </summary>
        public void QueryDelta(EntityQuery query, uint sinceVersion, Action<Entity> action)
        {
            if (AccessTrace.On) AccessTrace.RecordMask(TraceKind.QueryInclude, query.IncludeMask, TraceId);
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
            if (AccessTrace.On) AccessTrace.RecordMask(TraceKind.QueryInclude, query.IncludeMask, TraceId);
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
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
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
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
            BumpType(typeId, entity.Index);
        }
        
        /// <summary>
        /// OPTIMIZED: Fast path for command buffer playback.
        /// Uses direct array lookup instead of dictionary + lock.
        /// </summary>
        internal unsafe void SetComponentRawFast(Entity entity, int typeId, IntPtr dataPtr, int size)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
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
            
            // Update component mask — ⭐ CE-3243: structural stamp only when the component comes
            StampIfAdded(ref _entityIndex.GetComponentMask(entity.Index), typeId, entity.Index);
        }

        /// <summary>
        /// Sets a component using raw bytes (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal unsafe void SetComponentRaw(Entity entity, int typeId, IntPtr dataPtr, int size)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
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
            
            // Update component mask (in case it wasn't set) — ⭐ CE-3243: structural stamp only when it comes
            StampIfAdded(ref _entityIndex.GetComponentMask(entity.Index), typeId, entity.Index);
        }
        
        /// <summary>
        /// Ruling 14 — writes <paramref name="size"/> bytes at <paramref name="byteOffset"/> inside a
        /// component and <b>touches nothing else</b>. Used by <see cref="EntityCommandBuffer"/> playback
        /// for a surgical field edit.
        ///
        /// <para>
        /// ⭐ <b>Why this exists at all:</b> a whole-component write is a read-modify-write whose read
        /// happened earlier — so it silently carries every OTHER field back to whatever it was at read
        /// time. On a shared blackboard component that reverts unrelated subsystems' state by a tick.
        /// </para>
        /// </summary>
        internal unsafe void SetComponentFieldRaw(Entity entity, int typeId, int byteOffset, IntPtr src, int size)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
            if (!IsAlive(entity)) return;

            IComponentTable? table = null;
            if (typeId < _tableCache.Length && _tableCache[typeId] != null)
            {
                table = _tableCache[typeId]!;
            }
            else
            {
                Type? componentType = ComponentTypeRegistry.GetType(typeId);
                if (componentType == null)
                    throw new InvalidOperationException($"Component type ID {typeId} not registered");
                if (!_componentTables.TryGetValue(componentType, out table))
                    throw new InvalidOperationException($"Component {componentType.Name} not registered. Call RegisterComponent first.");
            }

            table.SetRawAt(entity.Index, byteOffset, src, size, _globalVersion);

            // ⚠ The component mask is NOT set here, deliberately: a field write PATCHES a component
            //   that must already exist. Adding the bit would make a write to an absent component look
            //   like an add of a component whose other bytes are whatever the slot happened to hold.
            // ⭐ CE-3243 — and a patch is never structural, so it does not stamp LastChangeTick (the chunk version carries it).
        }

        /// <summary>
        /// Removes a component by type ID (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal void RemoveComponentRaw(Entity entity, int typeId)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
            if (!IsAlive(entity)) return;
            
            // Clear component mask bit
            _entityIndex.GetComponentMask(entity.Index).ClearBit(typeId);
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
        }
        
        /// <summary>
        /// Adds a managed component using object reference (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal void AddManagedComponentRaw(Entity entity, int typeId, object componentObj)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
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
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
            BumpType(typeId, entity.Index);
        }
        
        /// <summary>
        /// Sets a managed component using object reference (type-erased).
        /// Used internally by EntityCommand Buffer during playback.
        /// </summary>
        internal void SetManagedComponentRaw(Entity entity, int typeId, object componentObj)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
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
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
            BumpType(typeId, entity.Index);
        }
        
        /// <summary>
        /// Removes a managed component (type-erased).
        /// Used internally by EntityCommandBuffer during playback.
        /// </summary>
        internal void RemoveManagedComponentRaw(Entity entity, int typeId)
        {
            if (AccessTrace.On) AccessTrace.RecordIdE(TraceKind.RawWrite, typeId, TraceId, entity.Index);
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
            _entityIndex.GetMetadata(entity.Index).LastChangeTick = _globalVersion; _structuralChanges++;
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
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonSet, TraceId);
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
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonSet, TraceId);
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
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonGet, TraceId);
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
        /// POC 2 / P-1 - a READ-ONLY reference to the unmanaged singleton: stamps nothing and cannot be written through.
        /// Use it wherever the caller only reads; <see cref="GetSingletonUnmanaged{T}"/> hands out a writable ref, a write through which is never recorded.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetSingletonUnmanagedRO<T>() where T : unmanaged
        {
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonGet, TraceId);
            int typeId = ComponentType<T>.ID;

            #if FDP_PARANOID_MODE
            if (typeId >= _singletons.Length || _singletons[typeId] == null)
                throw new InvalidOperationException(
                    $"Singleton {typeof(T).Name} not set. Call SetSingletonUnmanaged<{typeof(T).Name}>() first.");
            #endif

            var storage = (ComponentTable<T>)_singletons[typeId];
            return ref storage.GetRW(0, 0);
        }

        /// <summary>
        /// Gets the global singleton component (managed classes).
        /// Throws if not set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T? GetSingletonManaged<T>() where T : class
        {
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonGet, TraceId);
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
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonHas, TraceId);
            int typeId = ComponentType<T>.ID;
            if (typeId >= _singletons.Length) return false;
            return _singletons[typeId] != null;
        }
        
        /// <summary>
        /// Checks if a managed singleton has been set.
        /// </summary>
        public bool HasSingletonManaged<T>() where T : class
        {
            if (AccessTrace.On) AccessTrace.Record<T>(TraceKind.SingletonHas, TraceId);
            int typeId = ManagedComponentType<T>.ID;
            if (typeId >= _singletons.Length) return false;
            return _singletons[typeId] != null;
        }

        /// <summary>
        /// Returns all active singleton tables.
        /// Used by Flight Recorder.
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-2045</c> — the singleton slots as the raw array (null = empty slot), for the recorder's per-frame walk:
        /// <see cref="GetSingletonTables"/> is a <c>yield</c> iterator and allocates its enumerator every frame.
        /// </summary>
        internal object[] SingletonSlotsForRecording => _singletons;

        public IEnumerable<IComponentTable> GetSingletonTables()
        {
            if (AccessTrace.On) AccessTrace.RecordName(TraceKind.SingletonEnumerate, "(all singletons)", TraceId);
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
            if (AccessTrace.On) AccessTrace.RecordId(TraceKind.SingletonGet, typeId, TraceId);
            if (typeId < 0 || typeId >= _singletons.Length) return null;
            return (IComponentTable?)_singletons[typeId];
        }
        
        /// <summary>
        /// Ensures singleton array has capacity for the given type ID.
        /// </summary>
        /// <summary>The singleton slot count — the width of <c>_borrowedSingletons</c> (BitMask512).</summary>
        private const int MaxSingletonSlots = 512;

        private void EnsureSingletonCapacity(int typeId)
        {
            if (typeId >= _singletons.Length)
            {
                // ⛔ CE-3016 — the slot array must never outgrow the 512-bit borrowed-singletons mask that
                //   Dispose (and Sync) index by the same position. `len*2` from an odd length (64→302→604)
                //   overshot it, and Dispose threw ArgumentOutOfRange on slot 512 under FDP_PARANOID_MODE.
                //   Component ids are < 512 (BitMask512), so 512 slots always suffice.
                if (typeId >= MaxSingletonSlots)
                    throw new ArgumentOutOfRangeException(nameof(typeId),
                        $"Component id {typeId} is outside the singleton range (0..{MaxSingletonSlots - 1}).");
                int newSize = Math.Min(Math.Max(_singletons.Length * 2, typeId + 1), MaxSingletonSlots);
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
            Interlocked.Decrement(ref _liveInstanceCount);   // QA-004 — see LiveInstanceCount
            if (_trackLeaks) _liveOrigins.TryRemove(this, out _);
        }

        /// <summary>
        /// ⭐ <c>QA-004</c> — <see langword="true"/> once <see cref="Dispose"/> has run. Exposed so a
        /// teardown rail can assert a specific world was released, rather than inferring it from a
        /// process-wide count.
        /// </summary>
        public bool IsDisposed => _disposed;
    }
}
