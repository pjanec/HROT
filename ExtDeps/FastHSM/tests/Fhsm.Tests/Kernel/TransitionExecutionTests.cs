using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Fhsm.Tests.Kernel
{
    public unsafe class TransitionExecutionTests
    {
        private HsmDefinitionBlob CreateBlob(
            Span<StateDef> states, 
            Span<TransitionDef> transitions)
        {
            var header = new HsmDefinitionHeader();
            header.StructureHash = 0x12345678;
            header.StateCount = (ushort)states.Length;
            header.TransitionCount = (ushort)transitions.Length;
            
            return new HsmDefinitionBlob(
                header,
                states.ToArray(),
                transitions.ToArray(),
                Array.Empty<RegionDef>(), 
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>()
            );
        }

        [Fact]
        public void Simple_Transition_Updates_Active_State()
        {
            var states = new StateDef[3];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF };
            states[2] = new StateDef(); 

            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef 
            { 
                SourceStateIndex = 0, 
                TargetStateIndex = 1, 
                EventId = 10,
                ActionId = 0
            };

            var blob = CreateBlob(states, transitions);

            var instances = new HsmInstance64[1];
            instances[0].Header.MachineId = 0x12345678;
            instances[0].Header.Phase = InstancePhase.RTC;
            instances[0].ActiveLeafIds[0] = 0; 
            int context = 0;

            fixed (HsmInstance64* ptr = instances)
            {
                byte* bPtr = (byte*)ptr;
                *(ushort*)(bPtr + 20) = 10;
            }
                
            HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                
            Assert.Equal(1, instances[0].ActiveLeafIds[0]);
        }

        [Fact]
        public void LCA_Transition_Parent_Child()
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 };
            states[1] = new StateDef { ParentIndex = 0, FirstTransitionIndex = 1, TransitionCount = 1 };

            var transitions = new TransitionDef[2];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = 10 };
            transitions[1] = new TransitionDef { SourceStateIndex = 1, TargetStateIndex = 0, EventId = 11 };

            var blob = CreateBlob(states, transitions);
            var instances = new HsmInstance64[1];
            instances[0].Header.MachineId = 0x12345678;
            instances[0].Header.Phase = InstancePhase.RTC;
            
            int context = 0;
            
            fixed (HsmInstance64* ptr = instances)
            {
                instances[0].ActiveLeafIds[0] = 0;
                *(ushort*)((byte*)ptr + 20) = 10;
                
                HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                Assert.Equal(1, instances[0].ActiveLeafIds[0]);
                
                instances[0].Header.Phase = InstancePhase.RTC; 
                *(ushort*)((byte*)ptr + 20) = 11;
                
                HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                Assert.Equal(0, instances[0].ActiveLeafIds[0]);
            }
        }

        [Fact]
        public void History_Save_And_Restore()
        {
            var states = new StateDef[4];
            states[0] = new StateDef 
            { 
                ParentIndex = 0xFFFF,
                Flags = StateFlags.IsHistory, // 2
                HistorySlotIndex = 0,
                // Ensure other potential slots are None
                TimerSlotIndex = 0xFFFF
            };
            
            states[1] = new StateDef { ParentIndex = 0, FirstTransitionIndex = 0, TransitionCount = 1, HistorySlotIndex = 0xFFFF };
            states[2] = new StateDef { ParentIndex = 0, FirstTransitionIndex = 1, TransitionCount = 1, HistorySlotIndex = 0xFFFF };
            states[3] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 2, TransitionCount = 1, HistorySlotIndex = 0xFFFF };
            
            var transitions = new TransitionDef[3];
            transitions[0] = new TransitionDef { SourceStateIndex = 1, TargetStateIndex = 3, EventId = 10 };
            transitions[1] = new TransitionDef { SourceStateIndex = 2, TargetStateIndex = 3, EventId = 10 };
            transitions[2] = new TransitionDef { SourceStateIndex = 3, TargetStateIndex = 0, EventId = 20 };

            var blob = CreateBlob(states, transitions);
            var instances = new HsmInstance64[1];
            instances[0].Header.MachineId = 0x12345678;

            int context = 0;

            fixed (HsmInstance64* ptr = instances)
            {
                instances[0].ActiveLeafIds[0] = 1;
                instances[0].Header.Phase = InstancePhase.RTC;
                *(ushort*)((byte*)ptr + 20) = 10;
                
                HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                Assert.Equal(3, instances[0].ActiveLeafIds[0]);
                
                ushort saved = *(ushort*)((byte*)ptr + 32);
                Assert.Equal(1, saved); 
                
                instances[0].Header.Phase = InstancePhase.RTC;
                *(ushort*)((byte*)ptr + 20) = 20;
                HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                Assert.Equal(1, instances[0].ActiveLeafIds[0]); 
                
                instances[0].ActiveLeafIds[0] = 2;
                instances[0].Header.Phase = InstancePhase.RTC;
                *(ushort*)((byte*)ptr + 20) = 10;
                
                HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                Assert.Equal(3, instances[0].ActiveLeafIds[0]);
                
                saved = *(ushort*)((byte*)ptr + 32);
                Assert.Equal(2, saved); 
                
                instances[0].Header.Phase = InstancePhase.RTC;
                *(ushort*)((byte*)ptr + 20) = 20;
                HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                Assert.Equal(2, instances[0].ActiveLeafIds[0]); 
            }
        }
    }
}
