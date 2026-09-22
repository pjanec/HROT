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
            
            // ⭐ O7c (2026-09-22): delegates to the size-driven form, so the generic and slot-resident
            //   paths cannot drift. The body moved, not the behaviour.
            Initialize((byte*)instance, size, definition);
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
        /// ⭐⭐⭐ <b>Initialize an instance that lives at a POINTER of a known SIZE</b> — the slot-resident
        /// form of <see cref="Initialize{T}"/>. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.8.
        ///
        /// <para>⛔⛔ <b>The deciding argument is MEMORY SAFETY, not convenience</b>, and it is the same one
        /// <see cref="HsmKernel.Update(HsmDefinitionBlob, byte*, int, void*, float, Data.CommandPage*, Data.HsmTraceContext*)"/>
        /// already makes: with occurrence payloads packed adjacently inside one component, a generic
        /// overload whose <c>sizeof(TInstance)</c> exceeds the slot writes past the payload and into the
        /// NEXT OCCURRENCE'S bytes — no compiler check, no runtime check. <b>Sizing from the allocation
        /// cannot disagree with the allocation.</b></para>
        ///
        /// <para>⭐ <b>Exactly the generic body, with <c>sizeof(T)</c> replaced by the caller's size.</b>
        /// ⚠ Deliberately NOT re-expressed: the generic overload now delegates here, so the two cannot
        /// drift — which is the failure mode this repo files as "two producers of one fact".</para>
        /// </summary>
        public static unsafe void Initialize(byte* instance, int instanceSize, HsmDefinitionBlob definition)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (instanceSize <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(instanceSize),
                    "The instance size must come from the slot's own PayloadSize; a non-positive size " +
                    "means the caller does not know how big its occurrence is.");

            Unsafe.InitBlock(instance, 0, (uint)instanceSize);

            ref InstanceHeader header = ref Unsafe.AsRef<InstanceHeader>(instance);
            header.MachineId  = definition.Header.StructureHash;
            header.Generation = 1;
            header.Phase      = InstancePhase.Entry;

            HsmKernelCore.ResetInstance(instance, instanceSize);
        }

        /// <summary>
        /// ⭐⭐ <b>Reset an instance at a POINTER of a known SIZE</b>, preserving <c>MachineId</c>,
        /// <c>RngState</c> and bumping <c>Generation</c> — the slot-resident form of <see cref="Reset{T}"/>.
        /// ⚠ Same sizing rule as <see cref="Initialize(byte*, int, HsmDefinitionBlob)"/>.
        /// </summary>
        public static unsafe void Reset(byte* instance, int instanceSize)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (instanceSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(instanceSize));

            ref InstanceHeader header = ref Unsafe.AsRef<InstanceHeader>(instance);

            uint   machineId  = header.MachineId;
            ushort generation = header.Generation;
            uint   rngState   = header.RngState;

            Unsafe.InitBlock(instance, 0, (uint)instanceSize);

            header.MachineId  = machineId;
            header.Generation = (ushort)(generation + 1);
            header.RngState   = rngState;
            header.Phase      = InstancePhase.Entry;

            HsmKernelCore.ResetInstance(instance, instanceSize);
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
