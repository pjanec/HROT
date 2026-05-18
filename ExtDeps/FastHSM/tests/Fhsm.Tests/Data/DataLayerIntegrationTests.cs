using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

#pragma warning disable CS8625

namespace Fhsm.Tests.Data
{
    public class DataLayerIntegrationTests
    {
        // === Blob Tests ===

        [Fact]
        public void Blob_Header_Magic_Is_Constant()
        {
            Assert.Equal(0x4D534846u, HsmDefinitionHeader.MagicNumber);
        }

        [Fact]
        public void Blob_Can_Create_And_Access_Spans()
        {
            var states = new StateDef[2];
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            
            Assert.Equal(2, blob.States.Length);
        }

        [Fact]
        public void Blob_Indexed_Access_Works()
        {
            var states = new StateDef[1];
            states[0].ParentIndex = 999;
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            
            ref readonly var state = ref blob.GetState(0);
            Assert.Equal(999, state.ParentIndex);
        }

        [Fact]
        public void Blob_Indexed_Access_Throws_OutOfRange()
        {
            var states = new StateDef[1];
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            
            Assert.Throws<IndexOutOfRangeException>(() => blob.GetState(1));
            Assert.Throws<IndexOutOfRangeException>(() => blob.GetState(-1));
        }

        [Fact]
        public void Blob_Header_IsValid_Checks_Magic()
        {
            var header = new HsmDefinitionHeader();
            header.Magic = HsmDefinitionHeader.MagicNumber;
            Assert.True(header.IsValid());
            
            header.Magic = 0xBADF00Du; // Bad Food
            Assert.False(header.IsValid());
        }

        // === Instance Manager Tests ===

        [Fact]
        public void InstanceManager_Initialize_Sets_Defaults()
        {
            var blob = new HsmDefinitionBlob();
            blob.Header.StructureHash = 0x12345678;

            unsafe
            {
                var inst = new HsmInstance64();
                // Dirty it
                inst.Header.Flags = InstanceFlags.Error;
                
                HsmInstanceManager.Initialize(&inst, blob);
                
                Assert.Equal(0x12345678u, inst.Header.MachineId);
                Assert.Equal(1, inst.Header.Generation);
                Assert.Equal(InstancePhase.Entry, inst.Header.Phase);
                Assert.Equal(InstanceFlags.None, inst.Header.Flags);
            }
        }

        [Fact]
        public void InstanceManager_Reset_Preserves_Id_Increments_Gen()
        {
             unsafe
            {
                var inst = new HsmInstance64();
                inst.Header.MachineId = 0x1111;
                inst.Header.Generation = 10;
                inst.Header.RngState = 0x9999;
                inst.Header.Phase = InstancePhase.RTC; // Was running
                
                HsmInstanceManager.Reset(&inst);
                
                Assert.Equal(0x1111u, inst.Header.MachineId);
                Assert.Equal(11, inst.Header.Generation);
                Assert.Equal(0x9999u, inst.Header.RngState);
                Assert.Equal(InstancePhase.Entry, inst.Header.Phase);
            }
        }

        [Fact]
        public void InstanceManager_SelectTier_Logic_Tier1()
        {
            var states = new StateDef[8];
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            blob.Header.RegionCount = 1;   // <= 1
            // Depth/History defaults are 0
            
            int tier = HsmInstanceManager.SelectTier(blob);
            Assert.Equal(64, tier);
        }

        [Fact]
        public void InstanceManager_SelectTier_Logic_Tier2()
        {
            var states = new StateDef[9];
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            blob.Header.RegionCount = 1;
            
            int tier = HsmInstanceManager.SelectTier(blob);
            Assert.Equal(128, tier);
        }

        [Fact]
        public void InstanceManager_SelectTier_Logic_Tier3()
        {
            var states = new StateDef[33];
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            
            int tier = HsmInstanceManager.SelectTier(blob);
            Assert.Equal(256, tier);
        }

        // === Event Queue Tests ===

        [Fact]
        public void EventQueue_Tier1_Enqueue_Dequeue()
        {
            unsafe
            {
                var inst = new HsmInstance64();
                HsmEventQueue.Clear(&inst);
                
                var evt = new HsmEvent { EventId = 1 };
                bool enq = HsmEventQueue.TryEnqueue(&inst, evt);
                Assert.True(enq);
                Assert.Equal(1, HsmEventQueue.GetCount(&inst));
                
                bool deq = HsmEventQueue.TryDequeue(&inst, out var outEvt);
                Assert.True(deq);
                Assert.Equal(1, outEvt.EventId);
                Assert.Equal(0, HsmEventQueue.GetCount(&inst));
            }
        }

        [Fact]
        public void EventQueue_Tier1_Full_Rejects_Normal()
        {
             unsafe
            {
                var inst = new HsmInstance64();
                HsmEventQueue.Clear(&inst);
                
                // Fill
                HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 1 });
                
                // Enqueue generic normal
                bool enq = HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 2 });
                Assert.False(enq);
                Assert.Equal(1, HsmEventQueue.GetCount(&inst));
            }
        }

        [Fact]
        public void EventQueue_Tier1_Overwrite_Logic()
        {
             unsafe
            {
                var inst = new HsmInstance64();
                HsmEventQueue.Clear(&inst);
                
                // Fill with Normal
                HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 1, Priority = EventPriority.Normal });
                
                // Enqueue Interrupt
                bool enq = HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 2, Priority = EventPriority.Interrupt });
                
                Assert.True(enq); // Should succeed by overwriting
                Assert.Equal(1, HsmEventQueue.GetCount(&inst));
                
                // Verify content
                HsmEventQueue.TryDequeue(&inst, out var outEvt);
                Assert.Equal(2, outEvt.EventId); // Should be the interrupt
            }
        }

        [Fact]
        public void EventQueue_Tier2_Enqueue_Dequeue()
        {
             unsafe
            {
                var inst = new HsmInstance128();
                HsmEventQueue.Clear(&inst);
                
                Assert.True(HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 1 }));
                Assert.Equal(1, HsmEventQueue.GetCount(&inst));
                
                Assert.True(HsmEventQueue.TryDequeue(&inst, out var outEvt));
                Assert.Equal(1, outEvt.EventId);
            }
        }

        [Fact]
        public void EventQueue_Tier2_Reserved_Slot_Logic()
        {
             unsafe
            {
                var inst = new HsmInstance128();
                HsmEventQueue.Clear(&inst);
                
                // 1. Enqueue Interrupt -> Goes to reserved slot
                Assert.True(HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 99, Priority = EventPriority.Interrupt }));
                
                // 2. Enqueue Normal -> Goes to ring
                Assert.True(HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 1, Priority = EventPriority.Normal }));
                
                // Count should be 2 (1 interrupt + 1 normal)
                // Actually my GetCount impl sums them: 1 + 1 = 2.
                Assert.Equal(2, HsmEventQueue.GetCount(&inst));
                
                // 3. Dequeue -> Should get Interrupt first
                HsmEventQueue.TryDequeue(&inst, out var evt1);
                Assert.Equal(99, evt1.EventId);
                
                // 4. Dequeue -> Should get Normal next
                HsmEventQueue.TryDequeue(&inst, out var evt2);
                Assert.Equal(1, evt2.EventId);
            }
        }

        [Fact]
        public void EventQueue_Tier2_Ring_Full()
        {
             unsafe
            {
                // Tier 2 Ring Cap is 1. Interrupt cap is 1.
                var inst = new HsmInstance128();
                HsmEventQueue.Clear(&inst);
                
                // Fill Ring
                Assert.True(HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 1 }));
                
                // Try enqueue another Normal -> Full
                Assert.False(HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 2 }));
            }
        }
        
        [Fact]
        public void EventQueue_Wraparound_Works()
        {
            // Use Tier 3 which has ring cap 5.
            unsafe
            {
                var inst = new HsmInstance256();
                HsmEventQueue.Clear(&inst);
                
                // Fill ring 5 times
                for(int i=0; i<5; i++)
                    HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = (ushort)i });
                    
                // Consume 1
                HsmEventQueue.TryDequeue(&inst, out _);
                
                // Enqueue 1 (should wrap)
                bool enq = HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 100 });
                Assert.True(enq);
                
                // Inst header counters should reflect wrap
                Assert.Equal(5, HsmEventQueue.GetCount(&inst));
            }
        }

        [Fact]
        public void EventQueue_Clear_Resets_All()
        {
             unsafe
            {
                var inst = new HsmInstance128();
                HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 99, Priority = EventPriority.Interrupt });
                HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 1 });
                
                HsmEventQueue.Clear(&inst);
                Assert.Equal(0, HsmEventQueue.GetCount(&inst));
            }
        }

        // === Validation Tests ===

        [Fact]
        public void Validator_Validates_Good_Blob()
        {
            var states = new StateDef[1];
            states[0].ParentIndex = 0xFFFF; // Root
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            blob.Header.Magic = HsmDefinitionHeader.MagicNumber;
            
            bool valid = HsmValidator.ValidateDefinition(blob, out var err);
            Assert.True(valid, err);
        }

        [Fact]
        public void Validator_Catches_Bad_Magic()
        {
            var blob = new HsmDefinitionBlob();
            // Magic 0 by default, so invalid
            bool valid = HsmValidator.ValidateDefinition(blob, out var err);
            Assert.False(valid);
            Assert.Contains("Magic", err);
        }

        [Fact]
        public void Validator_Catches_Bad_Root()
        {
            var states = new StateDef[1];
            states[0].ParentIndex = 1; // Invalid for root
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            blob.Header.Magic = HsmDefinitionHeader.MagicNumber;
            
            bool valid = HsmValidator.ValidateDefinition(blob, out var err);
            Assert.False(valid);
            Assert.Contains("Root", err);
        }

        [Fact]
        public void Validator_Validates_Instance()
        {
            var states = new StateDef[1];
            states[0].ParentIndex = 0xFFFF;
            var blob = new HsmDefinitionBlob(new HsmDefinitionHeader(), states, null, null, null, null, null);
            blob.Header.StructureHash = 0x1234;
            // Must have at least one state (Root) for instance to be valid (it init to 0)
            
            unsafe
            {
                var inst = new HsmInstance64();
                inst.Header.MachineId = 0x1234;
                inst.Header.Phase = InstancePhase.Idle;
                
                bool valid = HsmValidator.ValidateInstance(&inst, blob, out var err);
                Assert.True(valid, err);
                
                // Bad ID
                inst.Header.MachineId = 0x9999;
                Assert.False(HsmValidator.ValidateInstance(&inst, blob, out err));
                Assert.Contains("MachineId", err);
            }
        }
        
        // --- Additional Tests to Meet Requirement ---

        [Fact]
        public void Blob_Empty_Works()
        {
            var blob = new HsmDefinitionBlob();
            Assert.Equal(0, blob.States.Length);
            Assert.Equal(0, blob.Transitions.Length);
        }
        
        [Fact]
        public void Blob_Accessors_Throw_On_Empty()
        {
             var blob = new HsmDefinitionBlob();
             Assert.Throws<IndexOutOfRangeException>(() => blob.GetTransition(0));
             Assert.Throws<IndexOutOfRangeException>(() => blob.GetRegion(0));
        }
        
        [Fact]
        public void InstanceManager_Initialize_Works_For_128()
        {
             var blob = new HsmDefinitionBlob();
             blob.Header.StructureHash = 0xABC;
             unsafe
             {
                 var inst = new HsmInstance128();
                 HsmInstanceManager.Initialize(&inst, blob);
                 Assert.Equal(0xABCu, inst.Header.MachineId);
             }
        }
        
        [Fact]
        public void InstanceManager_Initialize_Works_For_256()
        {
             var blob = new HsmDefinitionBlob();
             blob.Header.StructureHash = 0xDEF;
             unsafe
             {
                 var inst = new HsmInstance256();
                 HsmInstanceManager.Initialize(&inst, blob);
                 Assert.Equal(0xDEFu, inst.Header.MachineId);
             }
        }
        
        [Fact]
        public void InstanceManager_Initialize_Clears_Queue_Cursors()
        {
             var blob = new HsmDefinitionBlob();
             unsafe
             {
                 var inst = new HsmInstance128();
                 inst.Header.QueueHead = 5;
                 inst.Header.ActiveTail = 5;
                 
                 HsmInstanceManager.Initialize(&inst, blob);
                 Assert.Equal(0, inst.Header.QueueHead);
                 Assert.Equal(0, inst.Header.ActiveTail);
             }
        }
        
        [Fact]
        public void EventQueue_Peek_Works()
        {
            unsafe
            {
                var inst = new HsmInstance64();
                HsmEventQueue.Clear(&inst);
                HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 10 });
                
                bool peek = HsmEventQueue.TryPeek(&inst, out var evt);
                Assert.True(peek);
                Assert.Equal(10, evt.EventId);
                
                // Should still be there
                Assert.Equal(1, HsmEventQueue.GetCount(&inst));
            }
        }
        
        [Fact]
        public void EventQueue_Peek_Does_Not_Remove()
        {
             unsafe
            {
                var inst = new HsmInstance128(); // Tier 2
                HsmEventQueue.Clear(&inst);
                HsmEventQueue.TryEnqueue(&inst, new HsmEvent { EventId = 20 });
                
                HsmEventQueue.TryPeek(&inst, out var evt);
                Assert.Equal(20, evt.EventId);
                Assert.Equal(1, HsmEventQueue.GetCount(&inst));
            }
        }
        
        [Fact]
        public void EventQueue_Peek_Returns_False_On_Empty()
        {
             unsafe
            {
                var inst = new HsmInstance64();
                HsmEventQueue.Clear(&inst);
                
                Assert.False(HsmEventQueue.TryPeek(&inst, out _));
            }
        }
    }
}
