using System;
using System.Collections.Generic;
using Fdp.Kernel.Internal;

namespace Fdp.Kernel
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
        public void SyncFrom(EntityRepository source, BitMask256? mask = null, bool? includeTransient = null, Type[]? excludeTypes = null)
        {
            // 0. Determine Effective Mask
            BitMask256 effectiveMask;
            
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
            
            // 3. Sync global version
            // This ensures subsequent operations use the correct tick/version reference
            _globalVersion = source._globalVersion;
        }

        /// <summary>
        /// Builds a component mask containing only snapshotable component types.
        /// Used as default mask for SyncFrom when no explicit mask provided.
        /// </summary>
        /// <param name="includeTransient">If true, includes transient components in the mask</param>
        public BitMask256 GetSnapshotableMask(bool includeTransient = false)
        {
            var mask = new BitMask256();
            
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
        public BitMask256 GetRecordableMask()
        {
            var mask = new BitMask256();
            var recordableIds = ComponentTypeRegistry.GetRecordableTypeIds();
            foreach (var id in recordableIds)
                mask.SetBit(id);
            return mask;
        }

        /// <summary>
        /// Builds a component mask containing only saveable component types.
        /// Used by SaveGame/Checkpoint system to determine which components to persist.
        /// </summary>
        public BitMask256 GetSaveableMask()
        {
            var mask = new BitMask256();
            var saveableIds = ComponentTypeRegistry.GetSaveableTypeIds();
            foreach (var id in saveableIds)
                mask.SetBit(id);
            return mask;
        }
    }
}
