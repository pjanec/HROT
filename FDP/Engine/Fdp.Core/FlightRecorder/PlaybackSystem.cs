using System;
using System.Collections.Generic;
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

        // ⭐ CE-1478 P2 (DES-006 §5.1 "touchedChunkIds → partial repair") — what ONE apply wrote, so the two repair passes walk only that instead of
        //   every entity of the world (RepairManagedComponentMasks was 34 of 61 ms of a 1 000-man apply, 15 ms even for a frame with NO chunks).
        //   Reused arrays, grown never shrunk: no allocation in the steady state.
        private struct TouchedManagedChunk { public int TableSlot; public int Chunk; }
        private TouchedManagedChunk[] _touchedManaged = new TouchedManagedChunk[64];
        private int _touchedManagedCount;
        private int[] _touchedHot = new int[64];
        private int _touchedHotCount;
        private int[] _touchedCold = new int[64];
        private int _touchedColdCount;
        private int[] _destroyed = new int[64];
        private int _destroyedCount;
        private EntityRepository? _repairedRepo;   // the world the last FULL repair left consistent: a partial repair is only sound on top of one

        // the managed tables, by slot (rebuilt with _tablesByTypeId)
        private IComponentTable[] _managedTables = Array.Empty<IComponentTable>();
        private int[] _managedCapacity = Array.Empty<int>();
        private int _managedTableCount;

        /// <summary>
        /// ⭐ CE-1478 P2 — repair the WHOLE world after every frame, as before P2. A keyframe, the first apply onto a world, and an apply that registered a
        /// component table always do; set this for the equivalence rail (partial == full) or as a safety valve.
        /// </summary>
        public bool ForceFullRepair { get; set; }

        /// <summary>Entities the last apply's repair passes looked at (entity-index pass + managed-mask pass; diagnostic — a full repair visits the whole index per pass).</summary>
        public long LastRepairEntitiesVisited { get; private set; }
        /// <summary>Was the last apply's repair the full one?</summary>
        public bool LastRepairWasFull { get; private set; }

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
        /// <param name="unconsumedEvents">⭐ <c>CE-4118</c> — inject what was still UNCONSUMED when a keyframe was written (its trailing section,
        /// a checkpoint restore — <c>WorldKeyframes.ApplyKeepingUnconsumedEvents</c>) instead of the frame's own events (what its readers saw,
        /// the replay's). A keyframe written before CE-4118 has no such section: its main block, which then held the unconsumed events.</param>
        public void ApplyFrame(EntityRepository repo, BinaryReader reader, FdpEventBus? eventBus = null, bool processEvents = true,
                               bool unconsumedEvents = false)
        {
            ulong tick = reader.ReadUInt64();
            repo.SetGlobalVersion((uint)tick);
            byte frameType = reader.ReadByte();
            reader.ReadInt64(); // Skip WallClockTicks (FORMAT_VERSION 3+, 8 bytes)

            // ⭐ CE-1478 P2 — the tables as they are NOW (a type registered during this apply changes the count ⇒ a full repair), and an empty touched list
            EnsureTables(repo, repo.GetRegisteredComponentTypes());
            int tablesAtStart = repo.GetRegisteredComponentTypes().Count;
            _touchedManagedCount = _touchedHotCount = _touchedColdCount = _destroyedCount = 0;
            
            // 1. APPLY DESTRUCTIONS (Delta Only)
            if (frameType == 0)
            {
                // ...
                int dCount = reader.ReadInt32();
                for (int i = 0; i < dCount; i++)
                {
                    int idx = reader.ReadInt32();
                    ushort gen = reader.ReadUInt16();
                    Touch(ref _destroyed, ref _destroyedCount, idx);
                    
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
            long eventsAt = reader.BaseStream.Position;
            ReadAndInjectEvents(reader, unconsumedEvents ? null : eventBus, processEvents && !unconsumedEvents);

            
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
                        byte[] data = ChunkBuffer(reader, len);
                        
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

            // ⭐ CE-1478 P2 — a keyframe (a whole new world), the first apply onto this world and an apply that registered a table repair EVERYTHING; a delta repairs
            //   only what it wrote (the entity-index chunks and managed chunks it restored, the entities it destroyed)
            bool full = ForceFullRepair || frameType == 1 || !ReferenceEquals(_repairedRepo, repo) || repo.GetRegisteredComponentTypes().Count != tablesAtStart;
            LastRepairWasFull = full;
            LastRepairEntitiesVisited = 0;
            if (full)
            {
                RepairEntityIndex(repo);

                // 5. MANAGED COMPONENT MASK REPAIR (defensive)
                // In the normal case, EntityIndex chunks are written first by the recorder,
                // so masks are already correct after restoration in step 4 above.
                // However, SetChunk() does NOT update EntityHeader.ComponentMask, so if a
                // delta frame contains a managed chunk update without a corresponding
                // EntityIndex update, masks could drift. This scan ensures correctness.
                RepairManagedComponentMasks(repo);
                _repairedRepo = repo;
            }
            else
            {
                RepairEntityIndexPartial(repo);
                RepairManagedComponentMasksPartial(repo);
            }

            // ⭐ CE-4118 — the unconsumed events: the trailing section, else (a pre-CE-4118 keyframe) the main block
            if (unconsumedEvents && eventBus != null && processEvents)
            {
                var stream = reader.BaseStream;
                if (stream.Length - stream.Position >= sizeof(int) && reader.ReadInt32() == RecorderSystem.UnconsumedEventsMagic)
                    ReadAndInjectEvents(reader, eventBus, processEvents: true);
                else
                {
                    stream.Position = eventsAt;
                    ReadAndInjectEvents(reader, eventBus, processEvents: true);
                }
            }
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
            var entityIndex = repo.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;

            // Strictly guard against empty worlds. Do not clamp maxIndex to 0.
            if (maxIndex < 0 || entityIndex.ActiveCount == 0) return;

            for (int s = 0; s < _managedTableCount; s++)
                RepairMaskRange(entityIndex, _managedTables[s], 0, maxIndex);
        }

        // one managed table over the entity indexes [lo, hi] (hi clamped to what is issued): the mask bit follows the data (non-null = set, null = clear)
        private void RepairMaskRange(EntityIndex entityIndex, IComponentTable table, int lo, int hi)
        {
            int typeId = table.ComponentTypeId;
            if (hi > entityIndex.MaxIssuedIndex) hi = entityIndex.MaxIssuedIndex;
            for (int i = lo; i <= hi; i++)
            {
                LastRepairEntitiesVisited++;
                ref readonly var metaRMCM = ref entityIndex.GetMetadataUnsafe(i);
                if (!metaRMCM.IsActive) continue;

                if (table.GetRawObject(i) != null) entityIndex.GetComponentMaskUnsafe(i).SetBit(typeId);   // data exists -- the bit is set
                else entityIndex.GetComponentMaskUnsafe(i).ClearBit(typeId);                               // no data -- the bit is clear
            }
        }

        /// <summary>
        /// ⭐ CE-1478 P2 — the mask repair of a DELTA: a managed table only over the chunks the apply restored (its data moved), and every managed table over the
        /// entities of the entity-index (hot / mask) chunks it restored (their masks were rewritten). 📐 What it does NOT re-derive is a mask the recording ITSELF
        /// carries inconsistent in a chunk this frame did not touch — the keyframe's full repair settled that, and the equivalence rail pins partial == full.
        /// </summary>
        private void RepairManagedComponentMasksPartial(EntityRepository repo)
        {
            var entityIndex = repo.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            if (maxIndex < 0 || entityIndex.ActiveCount == 0) return;

            for (int k = 0; k < _touchedManagedCount; k++)
            {
                int cap = _managedCapacity[_touchedManaged[k].TableSlot];
                int lo = _touchedManaged[k].Chunk * cap;
                if (lo > maxIndex) continue;
                RepairMaskRange(entityIndex, _managedTables[_touchedManaged[k].TableSlot], lo, lo + cap - 1);
            }

            int hotCap = entityIndex.GetChunkCapacity();
            for (int k = 0; k < _touchedHotCount; k++)
            {
                int lo = _touchedHot[k] * hotCap;
                if (lo > maxIndex) continue;
                for (int s = 0; s < _managedTableCount; s++)
                    RepairMaskRange(entityIndex, _managedTables[s], lo, lo + hotCap - 1);
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


        
        private readonly HashSet<int> _unresolvedTypes = new();   // ⭐ CE-4050 — said once per type

        /// <summary>⭐ CE-4050 — the component type ids whose recorded data this playback skipped (no assembly declares them).</summary>
        public IReadOnlyCollection<int> UnresolvedTypes => _unresolvedTypes;

        // ⭐ CE-4407 (R-2084) — a chunk's bytes were `reader.ReadBytes(len)` per component per chunk: 11–65 MB of garbage per applied frame of a 1 000-man recording. The restorers
        //   COPY out of the array (native memcpy / a MemoryStream deserialise) and never keep it, and a table's chunk length is the same for every chunk ⇒ one array per EXACT length
        //   is reused (a restorer may read `data.Length`). Bounded: a managed blob's length varies, so at most MaxChunkBuffers distinct lengths are pooled — the rest allocate as before.
        private readonly Dictionary<int, byte[]> _chunkBuffers = new();
        private const int MaxChunkBuffers = 64;

        private byte[] ChunkBuffer(BinaryReader reader, int len)
        {
            if (!_chunkBuffers.TryGetValue(len, out var buffer))
            {
                if (_chunkBuffers.Count >= MaxChunkBuffers) return reader.ReadBytes(len);
                _chunkBuffers[len] = buffer = new byte[len];
            }
            int got = 0;
            while (got < len)
            {
                int n = reader.Read(buffer, got, len - got);
                if (n <= 0) throw new EndOfStreamException("The recording ended inside a chunk.");
                got += n;
            }
            return buffer;
        }

        // ⭐ CE-4407 — the table of a type id: the registered tables were walked (an interface enumerator, boxed) for EVERY chunk of EVERY component; now one lookup, rebuilt when the repository
        //   or its table count changes (a type registered on first use, CE-3299).
        private readonly Dictionary<int, IComponentTable> _tablesByTypeId = new();
        private EntityRepository? _tablesRepo;
        private int _tablesCount = -1;

        private IComponentTable? TableOf(EntityRepository repo, IReadOnlyDictionary<Type, IComponentTable> tables, int typeId)
        {
            if (!ReferenceEquals(_tablesRepo, repo) || _tablesCount != tables.Count)
            {
                _tablesByTypeId.Clear();
                foreach (var kvp in tables) _tablesByTypeId.TryAdd(kvp.Value.ComponentTypeId, kvp.Value);   // the first of a type id wins, as the walk did
                _tablesRepo = repo; _tablesCount = tables.Count;
                RebuildManagedList(tables);
            }
            return _tablesByTypeId.TryGetValue(typeId, out var table) ? table : null;
        }

        private void EnsureTables(EntityRepository repo, IReadOnlyDictionary<Type, IComponentTable> tables)
        {
            if (!ReferenceEquals(_tablesRepo, repo) || _tablesCount != tables.Count) TableOf(repo, tables, int.MinValue);
        }

        private void RebuildManagedList(IReadOnlyDictionary<Type, IComponentTable> tables)
        {
            if (_managedTables.Length < tables.Count) { _managedTables = new IComponentTable[tables.Count]; _managedCapacity = new int[tables.Count]; }
            int n = 0;
            foreach (var kvp in tables)
            {
                if (kvp.Value is IManagedChunkedTable managed)
                {
                    _managedTables[n] = kvp.Value;
                    _managedCapacity[n] = managed.ChunkCapacity;
                    n++;
                }
            }
            _managedTableCount = n;
        }

        private static void Touch(ref int[] list, ref int count, int value)
        {
            if (count == list.Length) Array.Resize(ref list, list.Length * 2);
            list[count++] = value;
        }

        private void NoteManagedChunk(IComponentTable table, int chunkIndex)
        {
            for (int s = 0; s < _managedTableCount; s++)
            {
                if (!ReferenceEquals(_managedTables[s], table)) continue;
                if (_touchedManagedCount == _touchedManaged.Length) Array.Resize(ref _touchedManaged, _touchedManaged.Length * 2);
                _touchedManaged[_touchedManagedCount++] = new TouchedManagedChunk { TableSlot = s, Chunk = chunkIndex };
                return;
            }
        }

        private void ApplyChunkData(EntityRepository repo, int typeId, int chunkIndex, byte[] data)
        {
            
            // Case 0: Special Entity Index hot (component-mask) chunk
            if (typeId == -1)
            {
                Touch(ref _touchedHot, ref _touchedHotCount, chunkIndex);
                repo.GetEntityIndex().RestoreHotChunkFromBuffer(chunkIndex, data);
                return;
            }

            // Case 0b: Special Entity Index cold (metadata) chunk
            if (typeId == -2)
            {
                Touch(ref _touchedCold, ref _touchedColdCount, chunkIndex);
                repo.GetEntityIndex().RestoreColdChunkFromBuffer(chunkIndex, data);
                return;
            }

            // Find the component table by type ID
            var componentTables = repo.GetRegisteredComponentTypes();
            
            IComponentTable? targetTable = TableOf(repo, componentTables, typeId);
            
            // ⭐ CE-3299 B5 (D10) — a type the recording's process registered on first use, which this world has not used yet: register it
            //   now (its [ComponentId] names it), as a fresh process restoring a checkpoint or replaying a recording must
            if (targetTable == null && ComponentTypeRegistry.FindType(typeId) is { } missing)
            {
                typeof(EntityRepository).GetMethod(nameof(EntityRepository.RegisterComponent))!.MakeGenericMethod(missing)
                    .Invoke(repo, new object?[] { null });
                repo.TryGetTable(missing, out targetTable!);
            }

            if (targetTable == null)
            {
                // ⭐ CE-4050 — a type no assembly here declares (a legacy recording that does not name its assembly, or an assembly this
                //   install does not carry): its data is skipped, said once per type, and the rest of the world plays on.
                //   ⛔ SUPERSEDED: an InvalidOperationException that stopped playback (the browser, the dumper, the index builder).
                if (_unresolvedTypes.Add(typeId))
                    Fdp.Core.Logging.FdpLog<PlaybackSystem>.Warn(
                        "[Playback] CE-4050: component type id {0} is declared by no loaded assembly — its recorded data is skipped.", typeId);
                return;
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
                    NoteManagedChunk(targetTable, chunkIndex);
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
                Type? type = ComponentTypeRegistry.FindType(typeId);   // ⭐ B5 (D10) — also a type this process has not touched yet
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
        private int[] _componentIds = Array.Empty<int>();   // ⭐ CE-4407 — reused by RepairEntityIndex

        private void RepairEntityIndex(EntityRepository repo)
        {
            var entityIndex = repo.GetEntityIndex();
            var ids = PrepareComponentIds(repo, out int idCount);
            RepairEntityRange(repo, entityIndex, ids, idCount, 0, entityIndex.MaxIssuedIndex);
        }

        private int[] PrepareComponentIds(EntityRepository repo, out int idCount)
        {
            var componentTables = repo.GetRegisteredComponentTypes();
            idCount = 0;
            if (_componentIds.Length < componentTables.Count) _componentIds = new int[componentTables.Count];
            var ids = _componentIds;
            foreach (var kvp in componentTables) ids[idCount++] = kvp.Value.ComponentTypeId;
            return ids;
        }

        /// <summary>⭐ CE-1478 P2 — the entity-index repair of a DELTA: the entities of the hot / cold index chunks it restored and the ones it destroyed.</summary>
        private void RepairEntityIndexPartial(EntityRepository repo)
        {
            var entityIndex = repo.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            var ids = PrepareComponentIds(repo, out int idCount);

            int hotCap = entityIndex.GetChunkCapacity();
            for (int k = 0; k < _touchedHotCount; k++)
                RepairEntityRange(repo, entityIndex, ids, idCount, _touchedHot[k] * hotCap, Math.Min(_touchedHot[k] * hotCap + hotCap - 1, maxIndex));
            int coldCap = entityIndex.GetColdChunkCapacity();
            for (int k = 0; k < _touchedColdCount; k++)
                RepairEntityRange(repo, entityIndex, ids, idCount, _touchedCold[k] * coldCap, Math.Min(_touchedCold[k] * coldCap + coldCap - 1, maxIndex));
            for (int k = 0; k < _destroyedCount; k++)
                RepairEntityRange(repo, entityIndex, ids, idCount, _destroyed[k], Math.Min(_destroyed[k], maxIndex));
        }

        private void RepairEntityRange(EntityRepository repo, EntityIndex entityIndex, int[] ids, int idCount, int lo, int hi)
        {
            // Iterate the entities and check if they have components
            for (int i = lo < 0 ? 0 : lo; i <= hi; i++)
            {
                LastRepairEntitiesVisited++;
                ref readonly var metaREI  = ref entityIndex.GetMetadata(i);
                ref var          compREI  = ref entityIndex.GetComponentMask(i);

                // Check if this entity has any components
                // ⭐ CE-4407 (R-2084) — over a plain array of the type ids, built once per pass: this walked the interface dictionary (a boxed enumerator, and every registered
                //   table) for EVERY entity of EVERY applied frame
                bool hasComponents = false;
                for (int k = 0; k < idCount; k++)
                {
                    if (compREI.IsSet(ids[k]))
                    {
                        hasComponents = true;
                        break;
                    }
                }

                // If entity has components but is marked dead, revive it
                if (hasComponents && !metaREI.IsActive)
                {
                    // Force restore this entity
                    repo.RestoreEntity(i, true, metaREI.Generation > 0 ? metaREI.Generation : 1, compREI);
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
            
            // ⭐ CE-3329 P0 — v6 differs from v7 only in the outer header (FrameOuterHeader.SizeFor)
            if (FormatVersion < FdpConfig.MIN_READABLE_FORMAT_VERSION || FormatVersion > FdpConfig.FORMAT_VERSION)
            {
                throw new InvalidDataException(
                    $"Format version mismatch. File version: {FormatVersion}, " +
                    $"Expected: {FdpConfig.MIN_READABLE_FORMAT_VERSION}–{FdpConfig.FORMAT_VERSION}");
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
                int outerSize = FrameOuterHeader.SizeFor(FormatVersion);
                if (_fileStream.Position + outerSize > _fileStream.Length)
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: Not enough bytes for outer header at {_fileStream.Position}/{_fileStream.Length}");
                    return false;
                }

                Span<byte> outerHeaderBytes = stackalloc byte[FrameOuterHeader.Size];
                outerHeaderBytes = outerHeaderBytes.Slice(0, outerSize);
                _fileStream.Read(outerHeaderBytes);
                FrameOuterHeader outerHeader = FrameOuterHeader.Read(outerHeaderBytes, FormatVersion);

                int compSize = outerHeader.CompressedSize;
                int uncompSize = outerHeader.UncompressedSize;

                if (compSize <= 0)
                {
                    Console.WriteLine($"[KERNEL-DEBUG] ReadNextFrame: Invalid compSize {compSize} at {_fileStream.Position - outerSize}/{_fileStream.Length}");
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
