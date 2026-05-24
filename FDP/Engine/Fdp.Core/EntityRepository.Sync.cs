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
        public void SyncFrom(EntityRepository source, BitMask512? mask = null, bool? includeTransient = null, Type[]? excludeTypes = null)
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
                myTable.SyncFrom(srcTable);
            }
            
            // 3. Sync specific singleton tables required by SoD background solvers.
            // Only the EQS solver (EqsSolverSystem) reads singletons from a SoD snapshot:
            //   - GlobalComponentIds.SpatialGridData (47): spatial hash grid for neighbour queries.
            //   - GlobalComponentIds.EqsResultPool (209): pre-allocated ring-buffer shared between
            //     the background solver (writes results) and EqsResultUpdateSystem (reads results).
            //     Both sides must reference the same table object so that the handle published in
            //     EqsResultEvent is valid when the main-thread consumer reads from it.
            //   - GlobalComponentIds.IEqsTemplateRegistry (210): query template lookup.
            //   - GlobalComponentIds.ICoverProvider (211): cover database for positional EQS queries.
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

            // 4. Sync global version
            // This ensures subsequent operations use the correct tick/version reference
            _globalVersion = source._globalVersion;
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
            _singletons[typeId] = srcTable;
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
