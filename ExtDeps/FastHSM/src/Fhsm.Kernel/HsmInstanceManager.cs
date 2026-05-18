using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    public static class HsmInstanceManager
    {
        /// <summary>
        /// Initialize a new instance. Sets phase to Idle, clears all state.
        /// </summary>
        public static unsafe void Initialize<T>(T* instance, HsmDefinitionBlob definition) 
            where T : unmanaged
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            // Verify size matches tier
            int size = sizeof(T);
            int selectedTier = SelectTier(definition);
            
            // Basic size check - though strictly we might just trust the T provided
            // But good to warn if mismatch? taking "T" allows the caller to alloc the memory.
            
            // 1. Zero out the memory
            Unsafe.InitBlock(instance, 0, (uint)size);
            
            // 2. Setup Header
            ref InstanceHeader header = ref Unsafe.As<T, InstanceHeader>(ref *instance);
            
            header.MachineId = definition.Header.StructureHash;
            header.Generation = 1;
            header.Phase = InstancePhase.Entry; // Start in Entry to trigger initialization 
            // Seed could be set by caller or rng. For now 0 or default is fine.
            
            // 3. Mark as uninitialized (ActiveLeafIds = 0xFFFF)
            HsmKernelCore.ResetInstance((byte*)instance, size);
        }

        /// <summary>
        /// Reset instance to initial state. Clears active states, history, events.
        /// Preserves DefinitionId.
        /// </summary>
        public static unsafe void Reset<T>(T* instance) where T : unmanaged
        {
             if (instance == null) throw new ArgumentNullException(nameof(instance));
             
             ref InstanceHeader header = ref Unsafe.As<T, InstanceHeader>(ref *instance);
             
             // Preserve specific fields
             uint machineId = header.MachineId;
             ushort generation = header.Generation;
             uint rngState = header.RngState;
             
             // Zero everything
             Unsafe.InitBlock(instance, 0, (uint)sizeof(T));
             
             // Restore/Update fields
             header.MachineId = machineId;
             header.Generation = (ushort)(generation + 1); // Increment generation
             header.RngState = rngState;
             header.Phase = InstancePhase.Entry;

             // Mark as uninitialized
             HsmKernelCore.ResetInstance((byte*)instance, sizeof(T));
        }

        /// <summary>
        /// Select appropriate tier based on machine complexity.
        /// Returns 64, 128, or 256.
        /// </summary>
        public static int SelectTier(HsmDefinitionBlob definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            int stateCount = definition.States.Length; // or Header.StateCount
            
            // Find max depth and history slots usage
            // We need to iterate states to find max depth and history slot usage if not in header?
            // StateDef has Depth and HistorySlotIndex.
            
            int maxDepth = 0;
            int maxHistorySlot = 0;
            
            foreach (var state in definition.States)
            {
                if (state.Depth > maxDepth) maxDepth = state.Depth;
                if (state.HistorySlotIndex != 0xFFFF && state.HistorySlotIndex > maxHistorySlot)
                    maxHistorySlot = state.HistorySlotIndex;
            }
            // maxHistorySlot is index, so count is index+1 if 0-based used? 
            // HistorySlotIndex is 0xFFFF if none. If 0 is used, count is at least 1.
            // Let's assume HistorySlots count = maxIndex + 1 (if any exist).
            // Actually let's count unique or just take max.
            // If HistorySlotIndex is 0, we need 1 slot.
            
            int historySlotsNeeded = 0;
            // Iterate to find max used index
            int maxUsedIndex = -1;
             foreach (var state in definition.States)
            {
                if (state.HistorySlotIndex != 0xFFFF)
                {
                    if (state.HistorySlotIndex > maxUsedIndex) maxUsedIndex = state.HistorySlotIndex;
                }
            }
            if (maxUsedIndex >= 0) historySlotsNeeded = maxUsedIndex + 1;
            
            
            int regions = definition.Header.RegionCount; // or definition.Regions.Length

            // Tier 1 (64B) criteria:
            // - StateCount <= 8
            // - HistorySlots <= 2
            // - MaxDepth <= 3
            // - RegionCount <= 1
            if (stateCount <= 8 && historySlotsNeeded <= 2 && maxDepth <= 3 && regions <= 1)
            {
                return 64;
            }
            
            // Tier 2 (128B) criteria:
            // - StateCount <= 32
            // - HistorySlots <= 4
            // - MaxDepth <= 6
            // - RegionCount <= 2
            if (stateCount <= 32 && historySlotsNeeded <= 4 && maxDepth <= 6 && regions <= 2)
            {
                return 128;
            }

            // Tier 3 (256B)
            return 256;
        }
    }
}
