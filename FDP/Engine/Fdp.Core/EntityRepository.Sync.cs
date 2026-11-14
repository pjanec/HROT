using System;
using System.Collections.Generic;
using Fdp.Core.Internal;

namespace Fdp.Core
{
    public sealed partial class EntityRepository
    {
        /// <summary>
        /// Synchronizes this repository from a source repository.
        /// Supports full synchronization (GDB/Backup) or filtered synchronization (SoD/Replication).
        /// </summary>
        /// <param name="source">The source repository to copy from.</param>
        /// <param name="mask">Optional mask to filter specific component types. If null, all components are synced.</param>
        /// <summary>
        /// Synchronizes this repository from a source repository.
        /// Supports full synchronization (GDB/Backup) or filtered synchronization (SoD/Replication).
        /// </summary>
        /// <param name="source">The source repository to copy from.</param>
        /// <param name="mask">Optional mask to filter specific component types. If filtered, ignores includeTransient/excludeTypes options.</param>
        /// <param name="includeTransient">If true, includes transient components even if they are normally excluded. Ignored if mask is provided.</param>
        /// <param name="excludeTypes">Optional types to exclude. Ignored if mask is provided.</param>
        /// <summary>
        /// ⭐ POC 3 T-5 (the parallel-simulation experiment; POC 2 D-7: the off-frame egress's replica sync, 11.8 ms on the CGF's main thread) —
        /// the per-table copies of <see cref="SyncFrom"/> run on the worker pool, one table per batch, while the caller waits (and helps):
        /// the tables are independent, the registration of a missing table stays serial. The same bytes, sooner. Opt-in:
        /// <c>HROT_PARALLEL_SYNC=1</c> (settable for tests). ⭐ 0 B per steady sync.
        /// </summary>
        public static bool ParallelSync { get; set; } = EnvVars.Read(EnvVars.HrotParallelSync) == "1";
        private readonly SyncPairs _syncPairs = new();

        private sealed class SyncPairs : Jobs.FdpWorkerPool.IBatchWork
        {
            private IComponentTable[] _dst = new IComponentTable[64], _src = new IComponentTable[64];
            public int Count;
            public void Add(IComponentTable dst, IComponentTable src)
            {
                if (Count == _dst.Length) { Array.Resize(ref _dst, Count * 2); Array.Resize(ref _src, Count * 2); }
                _dst[Count] = dst; _src[Count] = src; Count++;
            }
            public void Run()
            {
                try { Jobs.FdpWorkerPool.Shared.Run(this, Count); }
                finally { Array.Clear(_dst, 0, Count); Array.Clear(_src, 0, Count); Count = 0; }
            }
            public bool Force;
            public void RunBatch(int batch) => _dst[batch].SyncFrom(_src[batch], Force);
        }

        /// <summary>
        /// ⭐ M-4 (CE-4483) — hands a SNAPSHOT back to its pool for the next <see cref="SyncFrom"/>, KEEPING its content: the next sync is then
        /// INCREMENTAL — it copies only the chunks the source changed since (the replica pattern of <c>DoubleBufferProvider</c> and the staging
        /// buffers) — and gives the same world a <see cref="Clear"/> followed by a full sync gave. Two things make that true:
        /// <list type="number">
        /// <item>a background module may have WRITTEN into its view, and its writes carry the version the view synced at (or later) — the same
        /// version an unchanged live chunk may carry ⇒ every chunk stamped since that sync is marked never-equal here, so it is copied again;</item>
        /// <item>the source may have been REPLACED rather than advanced (a load's clear, a playback frame, a rewind from a snapshot) — its version
        /// numbers then repeat with other content ⇒ the sync is FULL when the source, its <see cref="ContentEpoch"/> or a non-decreasing
        /// version differ from the last sync.</item>
        /// </list>
        /// 📐 Measured (Release, 1 000 men, 4 threads, dotnet-trace): clearing every snapshot on release and copying it whole on acquire cost
        /// ≈ 3.7 s in 20 s of the sim thread (≈ 40 ms a tick: the decommit, the recommit, every managed reference chunk).
        /// ⛔ Only for a world whose next use is a <see cref="SyncFrom"/> (the snapshot providers' pools); <see cref="SoftClear"/> EMPTIES a world.
        /// </summary>
        public void RecycleForSync()
        {
            _clearGeneration++;
            _recycled = true;
            Bus.ClearAll();
            foreach (var kvp in _componentTables) kvp.Value.InvalidateChunksStampedSince(_syncedVersion);
        }

        /// <summary>⭐ M-4 (CE-4483) — bumped whenever this world's content or version clock is REPLACED rather than advanced by stamped writes
        /// (<see cref="Clear"/>, <see cref="SetGlobalVersion"/>, <see cref="ResetGlobalVersion"/>, a <see cref="SyncFrom"/> INTO it): a replica of
        /// it may skip a chunk by version only while this is unchanged.</summary>
        public uint ContentEpoch => _contentEpoch;
        internal uint _contentEpoch;

        private bool _recycled;
        private EntityRepository? _syncSource;              // what this world last synced from, at which epoch and version
        private uint _syncSourceEpoch, _syncSourceVersion, _syncedVersion = 1;

        public void SyncFrom(EntityRepository source, BitMask512? mask = null, bool? includeTransient = null, Type[]? excludeTypes = null)
        {
            if (!AccessTrace.On) { SyncFromCore(source, mask, includeTransient, excludeTypes); return; }
            // ⭐ A-1 — ONE world-copy event on the SOURCE (it reads every shared table); the copy's own accesses are hidden
            AccessTrace.RecordName(TraceKind.WorldCopy, "(SyncFrom: whole-world copy, reads the source)", source.TraceId);
            AccessTrace.Mute();
            try { SyncFromCore(source, mask, includeTransient, excludeTypes); } finally { AccessTrace.Unmute(); }
        }

        private void SyncFromCore(EntityRepository source, BitMask512? mask, bool? includeTransient, Type[]? excludeTypes)
        {
            // 0. Determine Effective Mask
            BitMask512 effectiveMask;
            
            if (mask.HasValue)
            {
                effectiveMask = mask.Value;
                
                // Enforce transient filtering on explicit mask (Safety Rule)
                // Unless explicitly overridden by includeTransient=true
                if (!(includeTransient ?? false))
                {
                    var snapshotableMask = GetSnapshotableMask(false);
                    effectiveMask.BitwiseAnd(snapshotableMask);
                }
            }
            else
            {
                // Build mask based on snapshotable components
                effectiveMask = GetSnapshotableMask(includeTransient: includeTransient ?? false);
                
                // Apply per-snapshot exclusions
                if (excludeTypes != null && excludeTypes.Length > 0)
                {
                    foreach (var type in excludeTypes)
                    {
                        var typeId = ComponentTypeRegistry.GetId(type);
                        if (typeId >= 0)
                        {
                            effectiveMask.ClearBit(typeId);
                        }
                    }
                }
            }

            // ⭐ M-4 — a recycled snapshot: incremental while the source only ADVANCED since the last sync, else a full copy; every table this
            //   sync will NOT copy is emptied (as Clear did)
            bool recycled = _recycled;
            _recycled = false;
            bool force = recycled && !(ReferenceEquals(_syncSource, source) && _syncSourceEpoch == source._contentEpoch
                                       && source._globalVersion >= _syncSourceVersion);
            _contentEpoch++;   // this world's content is being replaced
            if (recycled)
            {
                foreach (var kvp in _componentTables)
                    if (!effectiveMask.IsSet(kvp.Value.ComponentTypeId) || !source._componentTables.ContainsKey(kvp.Key)) kvp.Value.Clear();
            }

            // 1. Sync EntityIndex
            // This copies critical structural data (Generations, IsActive, Masks)
            _entityIndex.SyncFrom(source._entityIndex);
            
            // Always apply component filter (even if it's the default snapshotable mask)
            _entityIndex.ApplyComponentFilter(effectiveMask);
            
            // 2. Sync component tables (with optional filtering)
            foreach (var kvp in source._componentTables)
            {
                Type type = kvp.Key;
                IComponentTable srcTable = kvp.Value;
                int typeId = srcTable.ComponentTypeId;
                
                // Mask Filtering
                if (!effectiveMask.IsSet(typeId))
                    continue;  // Skip filtered components
                
                // Get or Create destination table
                if (!_componentTables.TryGetValue(type, out var myTable))
                {
                    // Schema Mismatch: Destination missing table.
                    // Automatically register component to match schema.
                    // Use Reflection to invoke generic RegisterComponent<T>
                    var method = typeof(EntityRepository).GetMethod(nameof(RegisterComponent))
                        ?.MakeGenericMethod(type);
                    
                    if (method != null)
                    {
                        // Invoke with null for optional parameter 'snapshotable'
                        method.Invoke(this, new object[] { null! });
                        myTable = _componentTables[type];
                    }
                    else
                    {
                        // Should not happen, but safe fallback
                        continue;
                    }
                }
                
                // Sync data
                if (ParallelSync) { _syncPairs.Add(myTable, srcTable); continue; }   // ⭐ POC 3 T-5 — copied in parallel below
                myTable.SyncFrom(srcTable, force);
            }
            if (ParallelSync && _syncPairs.Count > 0) { _syncPairs.Force = force; _syncPairs.Run(); }
            
            // 3. Sync specific singleton tables required by SoD background solvers.
            // Only the EQS solver (EqsSolverSystem) reads singletons from a SoD snapshot:
            //   - GlobalComponentIds.SpatialGridData (47): spatial hash grid for neighbour queries.
            //   - GlobalComponentIds.EqsResultPool (209): pre-allocated ring-buffer shared between
            //     the background solver (writes results) and EqsResultUpdateSystem (reads results).
            //     Both sides must reference the same table object so that the handle published in
            //     EqsResultEvent is valid when the main-thread consumer reads from it.
            //   - GlobalComponentIds.IEqsTemplateRegistry (210): query template lookup.
            //   - GlobalComponentIds.ICoverProvider (211): cover database for positional EQS queries.
            //   - GlobalComponentIds.IGeographicTransform (75): ⭐ CE-3216 S2-0 — the perception sensors read the weather
            //     (WeatherField), whose sun needs the point's latitude / longitude; without it the sun stood at 0° N 0° E.
            // Syncing all singletons would expose live-world state to unrelated SoD modules and
            // could alter their behaviour (e.g. slow code paths) causing unrelated tests to fail.
            // Background readers treat these as immutable for the duration of the task; the SoD
            // contract ensures the main thread does not structurally mutate them mid-frame.
            SyncSingletonById(source, GlobalComponentIds.SpatialGridData);
            SyncSingletonById(source, GlobalComponentIds.EqsResultPool);
            SyncSingletonById(source, GlobalComponentIds.IEqsTemplateRegistry);
            SyncSingletonById(source, GlobalComponentIds.ICoverProvider);
            SyncSingletonById(source, GlobalComponentIds.INavmeshProvider); // NavmeshSamplesGenerator / NavmeshReachableTest
            SyncSingletonById(source, GlobalComponentIds.RaycastBatchData); // AccurateLineOfSightTest ring-buffer reads
            SyncSingletonById(source, GlobalComponentIds.EqsSolverGlobalState); // per-tick accurate-LOS ray budget
            SyncSingletonById(source, GlobalComponentIds.TerrainWorld); // ⭐ EQS §19 — terrain sight + ground placement
            SyncSingletonById(source, GlobalComponentIds.IGeographicTransform); // ⭐ CE-3216 S2-0 (R-3017) — the sun's lat/lon (WeatherField), read-only
            SyncSingletonById(source, GlobalComponentIds.SoundEmissionRing); // ⭐ CE-3282 (Q95 C) — the lossless sound ring; BORROWED live, read by seqlock
            SyncSingletonById(source, GlobalComponentIds.WorldEpoch); // ⭐ CE-3294 — which world this is: a background system forgets the last one's entities too
            SyncSingletonById(source, GlobalComponentIds.ReplayState); // ⭐ CE-3329 P2 — is this world a replay: a background writer stands down too (borrowed: the live flag)

            // 4. Sync BOTH version clocks.
            //
            // ⭐⭐⭐ QA-030 — _simulationTick used to be left behind here, and that is a silent defect
            //    with a wide blast radius: ISimulationView.Tick (EntityRepository.View.cs) reads
            //    _simulationTick, NOT _globalVersion. A fresh EntityRepository starts both at 1, so
            //    every SoD / background snapshot handed to a module system reported Tick == 1 FOREVER
            //    while its GlobalVersion tracked the live world correctly.
            //
            // 📐 Measured 2026-08-26 (EditorHarness, EQS solver): the live repo advanced 1 -> 121 -> 241
            //    over 240 pumped frames and the solver re-evaluated 37 times — every one of them seeing
            //    view.Tick == 1. Two cross-tick mechanisms are built on that value and both were inert:
            //      · EqsSolverSystem stamps EqsResultEvent.RefreshTick = tick + 1, so every publish
            //        wrote the same LastUpdateTick and consumers could not tell one from the next;
            //      · SensorEvalState.AwaitingSinceTick == currentTick is the _AwaitingRaycasts
            //        skip-guard, so once a sensor entered that phase it could never leave it —
            //        contradicting EQS_Design_v1.3_final.md:422 ("on subsequent ticks polls the
            //        raycast result ring buffer for completion").
            //
            // ⚠ The class invariant (_globalVersion >= _simulationTick, EntityRepository.cs:65) held
            //    throughout, which is why nothing asserted: advancing one clock and not the other keeps
            //    the inequality true. The comment below this line already CLAIMED to sync "the correct
            //    tick"; it only ever synced the version.
            _globalVersion  = source._globalVersion;
            _simulationTick = source._simulationTick;
            _syncSource = source; _syncSourceEpoch = source._contentEpoch; _syncSourceVersion = source._globalVersion; _syncedVersion = _globalVersion;   // ⭐ M-4
            _lastTickFresh  = false;   // ⭐ N-15 — this world's content was replaced: its last-tick view no longer describes it
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3267</c> — gives a MODULE'S SNAPSHOT the frame's clock: the source's <see cref="GlobalTime"/> as a VALUE (the main thread
        /// rewrites the live singleton every frame, so the table is never shared). A background module's view then reads the time of the
        /// frame it was taken at, as it reads that frame's positions. Called by the snapshot providers right after <see cref="SyncFrom"/>;
        /// ⛔ deliberately NOT part of <see cref="SyncFrom"/> itself, which also restores the LIVE world from a snapshot (a preview's rewind)
        /// and must never rewind the clock there (docs/blueprints/DESIGN_Time_Architecture.md §12a: one time discontinuity).
        /// <para>📌 Before (measured 2026-10-19): a snapshot carried no clock, so the EQS memory stage read 0 for ever and a sighting's
        /// acquisition dwell (CE-3260) never passed — a fully exposed man beyond 100 m was never acquired on SimHost.</para>
        /// </summary>
        public void CopyClockFrom(EntityRepository source)
        {
            if (source.HasSingleton<GlobalTime>()) SetSingletonUnmanaged(source.GetSingleton<GlobalTime>());
        }

        /// <summary>
        /// Copies a single singleton table slot from <paramref name="source"/> into this repository
        /// by sharing the table reference.  Only used for SoD singleton sync where the background
        /// thread reads the singleton as immutable data and the main thread does not structurally
        /// replace the table object mid-frame.
        /// </summary>
        private void SyncSingletonById(EntityRepository source, int typeId)
        {
            if (source._singletons == null || typeId >= source._singletons.Length)
                return;
            var srcTable = source._singletons[typeId];
            if (srcTable == null)
                return;
            EnsureSingletonCapacity(typeId);
            if (!ReferenceEquals(_singletons[typeId], srcTable))
            {
                _singletons[typeId] = srcTable;
                _borrowedSingletons.SetBit(typeId);
            }
        }

        /// <summary>
        /// Builds a component mask containing only snapshotable component types.
        /// Used as default mask for SyncFrom when no explicit mask provided.
        /// </summary>
        /// <param name="includeTransient">If true, includes transient components in the mask</param>
        public BitMask512 GetSnapshotableMask(bool includeTransient = false)
        {
            var mask = new BitMask512();
            
            if (includeTransient)
            {
                // Iterate actual registered IDs (supports sparse/non-sequential [ComponentId] values)
                foreach (var id in ComponentTypeRegistry.GetAllIds())
                    mask.SetBit(id);
            }
            else
            {
                var snapshotableIds = ComponentTypeRegistry.GetSnapshotableTypeIds();
                foreach (var id in snapshotableIds)
                    mask.SetBit(id);
            }
            
            return mask;
        }

        /// <summary>
        /// Builds a component mask containing only recordable component types.
        /// Used by FlightRecorder to determine which components to serialize to .fdp files.
        /// </summary>
        public BitMask512 GetRecordableMask()
        {
            var mask = new BitMask512();
            var recordableIds = ComponentTypeRegistry.GetRecordableTypeIds();
            foreach (var id in recordableIds)
                mask.SetBit(id);
            return mask;
        }

        /// <summary>
        /// Builds a component mask containing only saveable component types.
        /// Used by SaveGame/Checkpoint system to determine which components to persist.
        /// </summary>
        public BitMask512 GetSaveableMask()
        {
            var mask = new BitMask512();
            var saveableIds = ComponentTypeRegistry.GetSaveableTypeIds();
            foreach (var id in saveableIds)
                mask.SetBit(id);
            return mask;
        }
    }
}
