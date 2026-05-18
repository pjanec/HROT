using System;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    public static class HsmValidator
    {
        public static bool ValidateDefinition(HsmDefinitionBlob blob, out string? error)
        {
            error = null;
            if (blob == null)
            {
                error = "Blob is null";
                return false;
            }

            if (!blob.Header.IsValid())
            {
                error = $"Invalid Magic Number: {blob.Header.Magic:X}";
                return false;
            }

            if (blob.States.Length == 0)
            {
                error = "StateCount is 0";
                return false;
            }

            // Root validation
            if (blob.States[0].ParentIndex != 0xFFFF)
            {
                error = "Root state (Index 0) must have ParentIndex 0xFFFF";
                return false;
            }

            // State validation
            for (int i = 0; i < blob.States.Length; i++)
            {
                ref readonly var state = ref blob.States[i];
                if (state.ParentIndex != 0xFFFF && state.ParentIndex >= blob.States.Length)
                {
                    error = $"State {i} has invalid ParentIndex {state.ParentIndex}";
                    return false;
                }
                
                // Depth check could be here (parent depth + 1)
            }

            // Transition validation
            for (int i = 0; i < blob.Transitions.Length; i++)
            {
                ref readonly var trans = ref blob.Transitions[i];
                if (trans.SourceStateIndex >= blob.States.Length)
                {
                    error = $"Transition {i} has invalid SourceStateIndex {trans.SourceStateIndex}";
                    return false;
                }
                if (trans.TargetStateIndex >= blob.States.Length)
                {
                    error = $"Transition {i} has invalid TargetStateIndex {trans.TargetStateIndex}";
                    return false;
                }
            }

            return true;
        }

        public static unsafe bool ValidateInstance<T>(T* instance, HsmDefinitionBlob definition, out string? error)
            where T : unmanaged
        {
            error = null;
            if (instance == null)
            {
                error = "Instance is null";
                return false;
            }
            if (definition == null)
            {
                error = "Definition is null";
                return false;
            }

            ref InstanceHeader header = ref System.Runtime.CompilerServices.Unsafe.As<T, InstanceHeader>(ref *instance);

            if (header.MachineId != definition.Header.StructureHash)
            {
                error = $"Instance MachineId ({header.MachineId}) does not match Definition StructureHash ({definition.Header.StructureHash})";
                return false;
            }

            if (!Enum.IsDefined(typeof(InstancePhase), header.Phase))
            {
                error = $"Invalid Phase: {header.Phase}";
                return false;
            }
            
            // Validate ActiveLeafIds
            // Need to know how many active leaves to check?
            // Depends on Tier.
            int size = sizeof(T);
            ushort* leaves = null;
            int maxRegions = 0;
            
            // Get pointer to leaves array
            byte* ptr = (byte*)instance;
            
            if (size == 64)
            {
                 leaves = (ushort*)(ptr + 16);
                 maxRegions = 2;
            }
            else if (size == 128)
            {
                 leaves = (ushort*)(ptr + 16);
                 maxRegions = 4;
            }
            else if (size == 256)
            {
                 leaves = (ushort*)(ptr + 16);
                 maxRegions = 8;
            }
            else
            {
                error = $"Invalid instance size: {size}";
                return false;
            }
            
            // Check active leaves (only up to RegionCount defined in blob)
            // But we don't track per-instance active region count easily without scanning.
            // Assumption: ActiveLeafIds slots corresponding to regions should be valid.
            // Indices > RegionCount should probably be 0 or 0xFFFF?
            // Initialize sets to 0. But 0 is Root.
            // Let's just validate values that are < StateCount IF they are not 0 (or if they are 0, State 0 exists).
            
            for(int i=0; i<maxRegions; i++)
            {
                ushort stateId = leaves[i];
                // 0 is valid state index.
                if (stateId >= definition.States.Length && stateId != 0xFFFF) // Assuming 0xFFFF means none? But Initialize uses 0.
                {
                     // If InitBlock zeroed it, it's 0. State 0 is Root.
                     // Usually leaves are initialized to something?
                     // Or unused regions are 0.
                     if (stateId >= definition.States.Length)
                     {
                        error = $"ActiveLeafId[{i}] has invalid state index {stateId}";
                        return false;
                     }
                }
            }

            return true;
        }

        public static bool IsValidStateId(HsmDefinitionBlob blob, ushort stateId)
        {
            if (blob == null) return false;
            return stateId < blob.States.Length;
        }

        public static bool IsValidTransitionId(HsmDefinitionBlob blob, ushort transitionId)
        {
            if (blob == null) return false;
            return transitionId < blob.Transitions.Length;
        }

        public static bool CheckTierBudget(HsmDefinitionBlob blob, int tierSize, out string? error)
        {
            error = null;
            if (blob == null)
            {
                error = "Blob is null";
                return false;
            }

            int limitRegions, limitTimers, limitHistory;
            switch(tierSize)
            {
                case 64: limitRegions = 2; limitTimers = 2; limitHistory = 2; break;
                case 128: limitRegions = 4; limitTimers = 4; limitHistory = 8; break;
                case 256: limitRegions = 8; limitTimers = 8; limitHistory = 16; break;
                default: error = $"Invalid Tier Size {tierSize}"; return false;
            }

            if (blob.Header.RegionCount > limitRegions)
            {
                error = $"Region Count {blob.Header.RegionCount} exceeds limit {limitRegions} for Tier {tierSize}";
                return false;
            }
            
            for (int i = 0; i < blob.States.Length; i++)
            {
                ref readonly var state = ref blob.States[i];
                if (state.HistorySlotIndex != 0xFFFF)
                {
                    if (state.HistorySlotIndex >= limitHistory)
                    {
                        error = $"State {i} requests History Slot {state.HistorySlotIndex} (Limit {limitHistory - 1})";
                        return false;
                    }
                }
                
                if (state.TimerSlotIndex != 0xFFFF)
                {
                    if (state.TimerSlotIndex >= limitTimers)
                    {
                        error = $"State {i} requests Timer Slot {state.TimerSlotIndex} (Limit {limitTimers - 1})";
                        return false;
                    }
                }
            }
            
            return true;
        }
    }
}
