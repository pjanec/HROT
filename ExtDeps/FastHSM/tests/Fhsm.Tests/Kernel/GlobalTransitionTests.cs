using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using System.Runtime.InteropServices;

namespace Fhsm.Tests.Kernel
{
    public unsafe class GlobalTransitionTests
    {
        private HsmDefinitionBlob CreateBlob(
            Span<StateDef> states, 
            Span<TransitionDef> transitions,
            Span<GlobalTransitionDef> globalTransitions)
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
                globalTransitions.ToArray(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>()
            );
        }

        [Fact]
        public void Global_Transition_Preempts_Local_Transition()
        {
            // Setup:
            // State 0 (A) -> Has local transition to State 1 (B) on Event 10
            // Global Transition -> To State 2 (C) on Event 10
            // Expected: End in C, because Global checks first
            
            var states = new StateDef[3];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 }; // A
            states[1] = new StateDef { ParentIndex = 0xFFFF }; // B
            states[2] = new StateDef { ParentIndex = 0xFFFF }; // C

            var transitions = new TransitionDef[1];
            // Priority 15 (Max local priority) -> (15 << 12)
            transitions[0] = new TransitionDef 
            { 
                SourceStateIndex = 0, 
                TargetStateIndex = 1, 
                EventId = 10,
                ActionId = 0,
                Flags = (TransitionFlags)(15 << 12) 
            };
            
            var globals = new GlobalTransitionDef[1];
            globals[0] = new GlobalTransitionDef
            {
                TargetStateIndex = 2,
                EventId = 10,
                GuardId = 0,
                ActionId = 0
            };

            var blob = CreateBlob(states, transitions, globals);
            
            // Instance in State 0
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // Trigger 
            HsmKernel.Trigger(ref instance);
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // Verify we are in state 0
            Assert.Equal(0, instance.ActiveLeafIds[0]);

            // Fire Event 10
            var evt = new HsmEvent { EventId = 10, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);
            
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // State should be 2 (C), not 1 (B)
            Assert.Equal(2, instance.ActiveLeafIds[0]);
        }
        
        [Fact]
        public void Global_Transition_Works_When_No_Local_Handlers()
        {
            // Setup:
            // State 0 (A) -> No transitions
            // Global -> To State 1 (B) on Event 99
            
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0 };
            states[1] = new StateDef { ParentIndex = 0xFFFF };
            
            var globals = new GlobalTransitionDef[1];
            globals[0] = new GlobalTransitionDef
            {
                TargetStateIndex = 1,
                EventId = 99
            };
            
            var blob = CreateBlob(states, Array.Empty<TransitionDef>(), globals);
            
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            HsmKernel.Trigger(ref instance);
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // Fire Event 99
            var evt = new HsmEvent { EventId = 99 };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);
            
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            Assert.Equal(1, instance.ActiveLeafIds[0]);
        }
    }
}
