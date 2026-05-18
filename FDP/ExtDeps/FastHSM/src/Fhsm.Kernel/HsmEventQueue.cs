using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    public static class HsmEventQueue
    {
        // Offsets based on constructs in BATCH-02
        // HsmInstance64
        private const int Tier1_EventCount_Offset = 36;
        private const int Tier1_Buffer_Offset = 40;
        private const int Tier1_Capacity = 1;

        // HsmInstance128
        private const int Tier2_IntSlotUsed_Offset = 56;
        private const int Tier2_EventCount_Offset = 57;
        private const int Tier2_Buffer_Offset = 60;
        private const int Tier2_Ring_Offset = 60 + 24; // 84
        private const int Tier2_Ring_Capacity = 1; // (68-24)/24 = 1

        // HsmInstance256
        private const int Tier3_IntSlotUsed_Offset = 96;
        private const int Tier3_EventCount_Offset = 97;
        private const int Tier3_Buffer_Offset = 100;
        private const int Tier3_Ring_Offset = 100 + 24; // 124
        private const int Tier3_Ring_Capacity = 5; // (156-24)/24 = 5.5 -> 5

        // === Type-erased (void*) Overloads for Kernel Core ===

        public static unsafe bool TryEnqueue(void* instance, int size, in HsmEvent evt)
        {
            if (instance == null) return false;

            switch (size)
            {
                case 64: return EnqueueTier1((byte*)instance, evt);
                case 128: return EnqueueTier2((byte*)instance, evt, Tier2_IntSlotUsed_Offset, Tier2_EventCount_Offset, Tier2_Buffer_Offset, Tier2_Ring_Offset, Tier2_Ring_Capacity);
                case 256: return EnqueueTier2((byte*)instance, evt, Tier3_IntSlotUsed_Offset, Tier3_EventCount_Offset, Tier3_Buffer_Offset, Tier3_Ring_Offset, Tier3_Ring_Capacity);
                default: throw new ArgumentException("Invalid instance size");
            }
        }

        public static unsafe bool TryDequeue(void* instance, int size, out HsmEvent evt)
        {
            Unsafe.SkipInit(out evt);
            if (instance == null) return false;

            switch (size)
            {
                case 64: return DequeueTier1((byte*)instance, out evt);
                case 128: return DequeueTier2((byte*)instance, out evt, Tier2_IntSlotUsed_Offset, Tier2_EventCount_Offset, Tier2_Buffer_Offset, Tier2_Ring_Offset, Tier2_Ring_Capacity);
                case 256: return DequeueTier2((byte*)instance, out evt, Tier3_IntSlotUsed_Offset, Tier3_EventCount_Offset, Tier3_Buffer_Offset, Tier3_Ring_Offset, Tier3_Ring_Capacity);
                default: throw new ArgumentException("Invalid instance size");
            }
        }

        public static unsafe int GetCount(void* instance, int size)
        {
            if (instance == null) return 0;
            byte* ptr = (byte*)instance;
            
            if (size == 64)
            {
                return ptr[Tier1_EventCount_Offset];
            }
            else if (size == 128)
            {
                return ptr[Tier2_IntSlotUsed_Offset] + ptr[Tier2_EventCount_Offset];
            }
            else if (size == 256)
            {
                return ptr[Tier3_IntSlotUsed_Offset] + ptr[Tier3_EventCount_Offset];
            }
            return 0;
        }

        // === Generic API ===

        public static unsafe bool TryEnqueue<T>(T* instance, in HsmEvent evt) where T : unmanaged
        {
            if (instance == null) return false;

            int size = sizeof(T);
            switch (size)
            {
                case 64: return EnqueueTier1((byte*)instance, evt);
                case 128: return EnqueueTier2((byte*)instance, evt, Tier2_IntSlotUsed_Offset, Tier2_EventCount_Offset, Tier2_Buffer_Offset, Tier2_Ring_Offset, Tier2_Ring_Capacity);
                case 256: return EnqueueTier2((byte*)instance, evt, Tier3_IntSlotUsed_Offset, Tier3_EventCount_Offset, Tier3_Buffer_Offset, Tier3_Ring_Offset, Tier3_Ring_Capacity);
                default: throw new ArgumentException("Invalid instance size");
            }
        }

        public static unsafe bool TryDequeue<T>(T* instance, out HsmEvent evt) where T : unmanaged
        {
            Unsafe.SkipInit(out evt);
            if (instance == null) return false;

            int size = sizeof(T);
            switch (size)
            {
                case 64: return DequeueTier1((byte*)instance, out evt);
                case 128: return DequeueTier2((byte*)instance, out evt, Tier2_IntSlotUsed_Offset, Tier2_EventCount_Offset, Tier2_Buffer_Offset, Tier2_Ring_Offset, Tier2_Ring_Capacity);
                case 256: return DequeueTier2((byte*)instance, out evt, Tier3_IntSlotUsed_Offset, Tier3_EventCount_Offset, Tier3_Buffer_Offset, Tier3_Ring_Offset, Tier3_Ring_Capacity);
                default: throw new ArgumentException("Invalid instance size");
            }
        }
        
        public static unsafe bool TryPeek<T>(T* instance, out HsmEvent evt) where T : unmanaged
        {
             Unsafe.SkipInit(out evt);
            if (instance == null) return false;

            int size = sizeof(T);
            switch (size)
            {
                case 64: return PeekTier1((byte*)instance, out evt);
                case 128: return PeekTier2((byte*)instance, out evt, Tier2_IntSlotUsed_Offset, Tier2_EventCount_Offset, Tier2_Buffer_Offset, Tier2_Ring_Offset, Tier2_Ring_Capacity);
                case 256: return PeekTier2((byte*)instance, out evt, Tier3_IntSlotUsed_Offset, Tier3_EventCount_Offset, Tier3_Buffer_Offset, Tier3_Ring_Offset, Tier3_Ring_Capacity);
                default: throw new ArgumentException("Invalid instance size");
            }
        }

        public static unsafe void Clear<T>(T* instance) where T : unmanaged
        {
             if (instance == null) return;

             int size = sizeof(T);
             InstanceHeader* header = (InstanceHeader*)instance;
             header->QueueHead = 0;
             header->ActiveTail = 0;
             header->DeferredTail = 0;

             byte* ptr = (byte*)instance;
             
             if (size == 64)
             {
                 ptr[Tier1_EventCount_Offset] = 0;
             }
             else if (size == 128)
             {
                 ptr[Tier2_IntSlotUsed_Offset] = 0;
                 ptr[Tier2_EventCount_Offset] = 0;
             }
             else if (size == 256)
             {
                 ptr[Tier3_IntSlotUsed_Offset] = 0;
                 ptr[Tier3_EventCount_Offset] = 0;
             }
        }

        public static unsafe int GetCount<T>(T* instance) where T : unmanaged
        {
            if (instance == null) return 0;
            int size = sizeof(T);
            byte* ptr = (byte*)instance;
            
            if (size == 64)
            {
                return ptr[Tier1_EventCount_Offset];
            }
            else if (size == 128)
            {
                return ptr[Tier2_IntSlotUsed_Offset] + ptr[Tier2_EventCount_Offset];
            }
            else if (size == 256)
            {
                return ptr[Tier3_IntSlotUsed_Offset] + ptr[Tier3_EventCount_Offset];
            }
            return 0;
        }

        // --- Tier 1 Implementation ---

        private static unsafe bool EnqueueTier1(byte* ptr, in HsmEvent evt)
        {
            ref byte count = ref ptr[Tier1_EventCount_Offset];
            byte* buffer = ptr + Tier1_Buffer_Offset;

            if (count == 0)
            {
                // Empty, just write
                Unsafe.Write(buffer, evt);
                count = 1;
                return true;
            }
            else 
            {
                // Full (Cap 1).
                // If new is Interrupt and existing is Normal/Low, overwrite.
                HsmEvent* existing = (HsmEvent*)buffer;
                if (evt.Priority == EventPriority.Interrupt && existing->Priority < EventPriority.Interrupt)
                {
                    Unsafe.Write(buffer, evt);
                    return true;
                }
            }
            return false;
        }

        private static unsafe bool DequeueTier1(byte* ptr, out HsmEvent evt)
        {
            ref byte count = ref ptr[Tier1_EventCount_Offset];
            
            if (count > 0)
            {
                byte* buffer = ptr + Tier1_Buffer_Offset;
                evt = Unsafe.Read<HsmEvent>(buffer);
                count = 0;
                return true;
            }
            Unsafe.SkipInit(out evt);
            return false;
        }

        private static unsafe bool PeekTier1(byte* ptr, out HsmEvent evt)
        {
            ref byte count = ref ptr[Tier1_EventCount_Offset];
            
            if (count > 0)
            {
                byte* buffer = ptr + Tier1_Buffer_Offset;
                evt = Unsafe.Read<HsmEvent>(buffer);
                return true;
            }
            Unsafe.SkipInit(out evt);
            return false;
        }

        // --- Tier 2/3 Implementation (Generic with offsets) ---
        // Note: Used for both Tier 2 and Tier 3 just changing offsets/cap
        private static unsafe bool EnqueueTier2(byte* ptr, in HsmEvent evt, int intSlotUsedOffset, int eventCountOffset, int bufferOffset, int ringOffset, int ringCapacity)
        {
            ref byte intSlotUsed = ref ptr[intSlotUsedOffset];
            ref byte ringCount = ref ptr[eventCountOffset]; // Count of items in RING
            
            if (evt.Priority == EventPriority.Interrupt)
            {
                // Check reserved slot
                if (intSlotUsed == 0)
                {
                    byte* intSlot = ptr + bufferOffset; // First 24 bytes
                    Unsafe.Write(intSlot, evt);
                    intSlotUsed = 1;
                    return true;
                }
                return false; // Interrupt slot full
            }
            else
            {
                // Normal/Low -> Shared Ring
                if (ringCount < ringCapacity)
                {
                    InstanceHeader* header = (InstanceHeader*)ptr;
                    byte* ringStart = ptr + ringOffset;
                    
                    // tail index
                    int tail = header->ActiveTail;
                    HsmEvent* slot = (HsmEvent*)(ringStart + (tail * sizeof(HsmEvent)));
                    Unsafe.Write(slot, evt);
                    
                    header->ActiveTail = (byte)((tail + 1) % ringCapacity);
                    ringCount++;
                    return true;
                }
                return false; // Ring full
            }
        }

        private static unsafe bool DequeueTier2(byte* ptr, out HsmEvent evt, int intSlotUsedOffset, int eventCountOffset, int bufferOffset, int ringOffset, int ringCapacity)
        {
            ref byte intSlotUsed = ref ptr[intSlotUsedOffset];
            
            // 1. Check Interrupt Slot
            if (intSlotUsed == 1)
            {
                byte* intSlot = ptr + bufferOffset;
                evt = Unsafe.Read<HsmEvent>(intSlot);
                intSlotUsed = 0;
                return true;
            }

            // 2. Check Ring
            ref byte ringCount = ref ptr[eventCountOffset];
            if (ringCount > 0)
            {
                InstanceHeader* header = (InstanceHeader*)ptr;
                byte* ringStart = ptr + ringOffset;
                
                int head = header->QueueHead;
                HsmEvent* slot = (HsmEvent*)(ringStart + (head * sizeof(HsmEvent)));
                evt = Unsafe.Read<HsmEvent>(slot);
                
                header->QueueHead = (byte)((head + 1) % ringCapacity);
                ringCount--;
                return true;
            }

            Unsafe.SkipInit(out evt);
            return false;
        }

         private static unsafe bool PeekTier2(byte* ptr, out HsmEvent evt, int intSlotUsedOffset, int eventCountOffset, int bufferOffset, int ringOffset, int ringCapacity)
        {
            ref byte intSlotUsed = ref ptr[intSlotUsedOffset];
            
            // 1. Check Interrupt Slot
            if (intSlotUsed == 1)
            {
                byte* intSlot = ptr + bufferOffset;
                evt = Unsafe.Read<HsmEvent>(intSlot);
                return true;
            }

            // 2. Check Ring
            ref byte ringCount = ref ptr[eventCountOffset];
            if (ringCount > 0)
            {
                InstanceHeader* header = (InstanceHeader*)ptr;
                byte* ringStart = ptr + ringOffset;
                
                int head = header->QueueHead;
                HsmEvent* slot = (HsmEvent*)(ringStart + (head * sizeof(HsmEvent)));
                evt = Unsafe.Read<HsmEvent>(slot);
                return true;
            }

            Unsafe.SkipInit(out evt);
            return false;
        }

        public static unsafe void RecallDeferredEvents(void* instance, int size)
        {
             if (instance == null) return;
             switch (size)
             {
                 case 64: RecallDeferredTier1((byte*)instance); break;
                 case 128: RecallDeferredTier2((byte*)instance, Tier2_IntSlotUsed_Offset, Tier2_EventCount_Offset, Tier2_Buffer_Offset, Tier2_Ring_Offset, Tier2_Ring_Capacity); break;
                 case 256: RecallDeferredTier2((byte*)instance, Tier3_IntSlotUsed_Offset, Tier3_EventCount_Offset, Tier3_Buffer_Offset, Tier3_Ring_Offset, Tier3_Ring_Capacity); break;
             }
        }

        private static unsafe void RecallDeferredTier1(byte* ptr)
        {
            ref byte count = ref ptr[Tier1_EventCount_Offset];
            if (count > 0)
            {
                byte* buffer = ptr + Tier1_Buffer_Offset;
                HsmEvent* evt = (HsmEvent*)buffer;
                if ((evt->Flags & EventFlags.IsDeferred) != 0)
                {
                    evt->Flags &= ~EventFlags.IsDeferred;
                }
            }
        }

        private static unsafe void RecallDeferredTier2(byte* ptr, int intSlotUsedOffset, int eventCountOffset, int bufferOffset, int ringOffset, int ringCapacity)
        {
            ref byte intSlotUsed = ref ptr[intSlotUsedOffset];
            if (intSlotUsed == 1)
            {
                 byte* intSlot = ptr + bufferOffset;
                 HsmEvent* evt = (HsmEvent*)intSlot;
                 if ((evt->Flags & EventFlags.IsDeferred) != 0)
                 {
                     evt->Flags &= ~EventFlags.IsDeferred;
                 }
            }

            ref byte ringCount = ref ptr[eventCountOffset];
            if (ringCount > 0)
            {
                InstanceHeader* header = (InstanceHeader*)ptr;
                byte* ringStart = ptr + ringOffset;
                
                int head = header->QueueHead;
                for (int i = 0; i < ringCount; i++)
                {
                    int index = (head + i) % ringCapacity;
                    HsmEvent* slot = (HsmEvent*)(ringStart + (index * sizeof(HsmEvent)));
                    if ((slot->Flags & EventFlags.IsDeferred) != 0)
                    {
                        slot->Flags &= ~EventFlags.IsDeferred;
                    }
                }
            }
        }
    }
}
