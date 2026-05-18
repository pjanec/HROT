using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Manages hot reload of HSM definitions.
    /// Supports soft reload (parameters) and hard reset (structure).
    /// </summary>
    public class HotReloadManager
    {
        private readonly Dictionary<uint, HsmDefinitionBlob> _loadedBlobs = new();
        
        /// <summary>
        /// Try to reload definitions for a machine.
        /// </summary>
        /// <typeparam name="TInstance">The instance type (struct).</typeparam>
        /// <param name="machineId">Current machine ID (or intended machine ID).</param>
        /// <param name="newBlob">The new definition blob.</param>
        /// <param name="instances">Span of instances to update.</param>
        /// <returns>Reload result.</returns>
        public ReloadResult TryReload<TInstance>(
            uint machineId,
            HsmDefinitionBlob newBlob,
            Span<TInstance> instances)
            where TInstance : unmanaged
        {
            // 1. Check if machine exists
            if (!_loadedBlobs.TryGetValue(machineId, out var oldBlob))
            {
                _loadedBlobs[machineId] = newBlob;
                return ReloadResult.NewMachine;
            }

            // 2. Compare hashes
            bool structureChanged = newBlob.Header.StructureHash != oldBlob.Header.StructureHash;
            bool parameterChanged = newBlob.Header.ParameterHash != oldBlob.Header.ParameterHash;

            // 3. No change
            if (!structureChanged && !parameterChanged)
            {
                return ReloadResult.NoChange;
            }

            // 4. Structure changed -> Hard Reset
            if (structureChanged)
            {
                _loadedBlobs[machineId] = newBlob;
                
                for (int i = 0; i < instances.Length; i++)
                {
                    ref TInstance inst = ref instances[i];
                    // Verify instance belongs to this machine
                    // InstanceHeader is first member of valid instance
                    // Using Unsafe to peek header
                    ref InstanceHeader header = ref Unsafe.As<TInstance, InstanceHeader>(ref inst);
                    
                    // Only reset if it matches the OLD machine ID or current machine ID?
                    // Instructions say: "instance.Header.MachineId == machineId"
                    // But if structure changed, structure hash changed.
                    // If machineId param is the "Registry ID" (user key), then header.MachineId usually stores StructureHash.
                    // So we should check if header.MachineId matches oldBlob.Header.StructureHash.
                    
                    if (header.MachineId == oldBlob.Header.StructureHash)
                    {
                        HardReset(ref inst, newBlob);
                    }
                }
                return ReloadResult.HardReset;
            }

            // 5. Parameters changed -> Soft Reload
            if (parameterChanged)
            {
                _loadedBlobs[machineId] = newBlob;
                // Instances continue running on new blob (assuming caller updates the blob reference used in Update loop)
                return ReloadResult.SoftReload;
            }
            
            return ReloadResult.NoChange;
        }
        
        private unsafe void HardReset<TInstance>(
            ref TInstance instance,
            HsmDefinitionBlob newBlob)
            where TInstance : unmanaged
        {
            fixed (void* ptr = &instance)
            {
                InstanceHeader* header = (InstanceHeader*)ptr;
                
                // 1. Increment generation
                header->Generation++;
                
                // 2. Clear runtime state
                header->Phase = InstancePhase.Idle;
                header->MicroStep = 0;
                header->QueueHead = 0;
                header->ActiveTail = 0;
                header->DeferredTail = 0;
                
                // 3. Update machine ID (to new StructureHash)
                header->MachineId = newBlob.Header.StructureHash;
                
                // 4. Clear tier-specific state
                int instanceSize = sizeof(TInstance);
                
                if (instanceSize == 64)
                {
                    ClearInstance64State((HsmInstance64*)ptr);
                }
                else if (instanceSize == 128)
                {
                    ClearInstance128State((HsmInstance128*)ptr);
                }
                else if (instanceSize == 256)
                {
                    ClearInstance256State((HsmInstance256*)ptr);
                }
                // If unknown size, maybe do generic reset (using HsmKernelCore.ResetInstance logic?)
                // HsmKernelCore.ResetInstance relies on size too.
                else
                {
                     HsmKernelCore.ResetInstance((byte*)ptr, instanceSize);
                }
            }
        }

        private unsafe void ClearInstance64State(HsmInstance64* instance)
        {
            // Clear active leaves (1 region max in 64, but array size 2?)
            // Definition: fixed ushort ActiveLeafIds[2];
            instance->ActiveLeafIds[0] = 0xFFFF;
            instance->ActiveLeafIds[1] = 0xFFFF;
            
            // Clear timers
            instance->TimerDeadlines[0] = 0;
            instance->TimerDeadlines[1] = 0;
            
            // Clear history (2 slots)
            instance->HistorySlots[0] = 0xFFFF;
            instance->HistorySlots[1] = 0xFFFF;
            
            // Clear event queue metadata
            instance->EventCount = 0;
        }

        private unsafe void ClearInstance128State(HsmInstance128* instance)
        {
            // Tier 2: 4 active leaves, 4 timers, 4 active leaves? No 8 history.
            // HsmInstance128 has 8 history slots (verified in HsmInstance128.cs: [FieldOffset(40)] public fixed ushort HistorySlots[8];)
            for (int i = 0; i < 4; i++) instance->ActiveLeafIds[i] = 0xFFFF;
            for (int i = 0; i < 4; i++) instance->TimerDeadlines[i] = 0;
            for (int i = 0; i < 8; i++) instance->HistorySlots[i] = 0xFFFF;
            
            // Clear event queue metadata
            instance->InterruptSlotUsed = 0;
            instance->EventCount = 0;
        }

        private unsafe void ClearInstance256State(HsmInstance256* instance)
        {
            // Tier 3: 8 active leaves, 8 timers, 16 history
            for (int i = 0; i < 8; i++) instance->ActiveLeafIds[i] = 0xFFFF;
            for (int i = 0; i < 8; i++) instance->TimerDeadlines[i] = 0;
            for (int i = 0; i < 16; i++) instance->HistorySlots[i] = 0xFFFF;
            
            // Clear event queue metadata
            instance->InterruptSlotUsed = 0;
            instance->EventCount = 0;
        }
    }
    
    public enum ReloadResult
    {
        NewMachine,    // First time loading this machine ID
        NoChange,      // Hashes match, no reload needed
        SoftReload,    // Parameters changed, state preserved
        HardReset,     // Structure changed, state cleared
    }
}
