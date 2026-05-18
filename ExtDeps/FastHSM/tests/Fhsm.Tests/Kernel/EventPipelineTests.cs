using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Fhsm.Tests.Kernel
{
    public unsafe class EventPipelineTests
    {
        private struct TestContext
        {
#pragma warning disable CS0649
            public int Value;
#pragma warning restore CS0649
        }

        private HsmDefinitionBlob CreateBlob(
            Span<StateDef> states, 
            Span<TransitionDef> transitions, 
            Span<GlobalTransitionDef> globalTransitions)
        {
            var header = new HsmDefinitionHeader();
            header.StructureHash = 0x12345678;
            header.StateCount = (ushort)states.Length;
            
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
        public void Timer_Fires_And_Trigger_Workflow()
        {
            // Setup Blob (empty)
            var blob = CreateBlob(Array.Empty<StateDef>(), Array.Empty<TransitionDef>(), Array.Empty<GlobalTransitionDef>());
            
            // Setup Instance
            var instance = new HsmInstance64(); 
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.Idle;
            
            var instances = new[] { instance };
            
            // Set Timer 0 to 1ms
            {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    uint* timers = (uint*)(bPtr + 24);
                    timers[0] = 10; // 10ms
                }
            }

            var dummyContext = new TestContext();
            
            // 1. Tick with dt=5ms. Timer (10) -> 5. Not fired.
            HsmKernel.UpdateBatch<HsmInstance64, TestContext>(blob, instances, dummyContext, 0.005f);
            
            // Should still be Idle, Queue empty
            Assert.Equal(InstancePhase.Idle, instances[0].Header.Phase);
            {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    uint* timers = (uint*)(bPtr + 24);
                    Assert.Equal(5u, timers[0]);
                    Assert.Equal(0, HsmEventQueue.GetCount(ptr, 64));
                }
            }
            
            // 2. Tick with dt=10ms. Timer (5) -> 0. Fired.
            HsmKernel.UpdateBatch<HsmInstance64, TestContext>(blob, instances, dummyContext, 0.010f);
            
            Assert.Equal(InstancePhase.Entry, instances[0].Header.Phase);
            {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    Assert.Equal(1, HsmEventQueue.GetCount(ptr, 64));
                }
            }
            
            // 3. Tick. Entry -> Event Phase (Dequeue) -> RTC
            HsmKernel.UpdateBatch<HsmInstance64, TestContext>(blob, instances, dummyContext, 0.001f);
            Assert.Equal(InstancePhase.RTC, instances[0].Header.Phase);
            
            {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    ushort* eventIdPtr = (ushort*)(bPtr + 20);
                    Assert.Equal(0xFFFE, *eventIdPtr);
                    Assert.Equal(0, HsmEventQueue.GetCount(ptr, 64));
                }
            }
        }

        [Fact]
        public void Event_Priority_Is_Respected()
        {
             var blob = CreateBlob(Array.Empty<StateDef>(), Array.Empty<TransitionDef>(), Array.Empty<GlobalTransitionDef>());
             var instance = new HsmInstance128();
             instance.Header.MachineId = blob.Header.StructureHash;
             instance.Header.Phase = InstancePhase.Entry; 
             
             var instances = new[] { instance };
             
             var evt1 = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
             var evt2 = new HsmEvent { EventId = 2, Priority = EventPriority.Interrupt };
             
             {
                 fixed (HsmInstance128* ptr = &instances[0])
                 {
                     HsmEventQueue.TryEnqueue(ptr, 128, evt1);
                     HsmEventQueue.TryEnqueue(ptr, 128, evt2);
                     Assert.Equal(2, HsmEventQueue.GetCount(ptr, 128));
                 }
             }
             
             var ctx = new TestContext();
             HsmKernel.UpdateBatch<HsmInstance128, TestContext>(blob, instances, ctx, 0.0f);
             
             Assert.Equal(InstancePhase.RTC, instances[0].Header.Phase);
             
             {
                 fixed (HsmInstance128* ptr = &instances[0])
                 {
                     byte* bPtr = (byte*)ptr;
                     ushort* eventIdPtr = (ushort*)(bPtr + 58); // Offset 58 for 128
                     Assert.Equal(2, *eventIdPtr);
                     Assert.Equal(1, HsmEventQueue.GetCount(ptr, 128));
                 }
             }
        }
        
        [Fact]
        public void Global_Transition_Beats_Local()
        {
            var state0 = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 };
            var state1 = new StateDef { ParentIndex = 0xFFFF };
            
            var localTrans = new TransitionDef { EventId = 1, TargetStateIndex = 0, Flags = TransitionFlags.None };
            var globalTrans = new GlobalTransitionDef { EventId = 1, TargetStateIndex = 1, Flags = TransitionFlags.IsExternal };
            
            var blob = CreateBlob(new[] { state0, state1 }, new[] { localTrans }, new[] { globalTrans });
                
            var instance = new HsmInstance64();
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.RTC;
            
            var instances = new[] { instance };
            
            {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    ushort* active = (ushort*)(bPtr + 16);
                    active[0] = 0;
                    ushort* evt = (ushort*)(bPtr + 20);
                    *evt = 1; 
                }
            }
            
            var ctx = new TestContext();
            HsmKernel.UpdateBatch<HsmInstance64, TestContext>(blob, instances, ctx, 0.0f);
            
            Assert.Equal(InstancePhase.Activity, instances[0].Header.Phase);
            
            {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    ushort* active = (ushort*)(bPtr + 16);
                    Assert.Equal(1, active[0]);
                }
            }
        }
        
        [Fact]
        public void Local_Transition_With_Higher_Priority_Wins()
        {
            var state0 = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 2 };
            var state1 = new StateDef { ParentIndex = 0xFFFF };
            
            var trans1 = new TransitionDef { EventId = 1, TargetStateIndex = 0, Flags = (TransitionFlags)(0 << 12) };
            var trans2 = new TransitionDef { EventId = 1, TargetStateIndex = 1, Flags = (TransitionFlags)(1 << 12) };
            
            var blob = CreateBlob(new[] { state0, state1 }, new[] { trans1, trans2 }, Array.Empty<GlobalTransitionDef>());
                
            var instance = new HsmInstance64();
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.RTC;
            
            var instances = new[] { instance };
            
             {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    ushort* active = (ushort*)(bPtr + 16);
                    active[0] = 0;
                    ushort* evt = (ushort*)(bPtr + 20);
                    *evt = 1; 
                }
            }
            
            var ctx = new TestContext();
            HsmKernel.UpdateBatch<HsmInstance64, TestContext>(blob, instances, ctx, 0.0f);

             {
                fixed (HsmInstance64* ptr = &instances[0])
                {
                    byte* bPtr = (byte*)ptr;
                    ushort* active = (ushort*)(bPtr + 16);
                    Assert.Equal(1, active[0]);
                }
            }
        }
    }
}
