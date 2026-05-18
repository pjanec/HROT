using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    public unsafe class TimerCancellationTests
    {
        private void RunUpdates<T>(HsmDefinitionBlob blob, ref T instance, int count) where T : unmanaged
        {
            for (int i = 0; i < count; i++)
            {
                HsmKernel.Update(blob, ref instance, 0, 0.016f);
            }
        }

        [Fact]
        public void Timer_Cancelled_When_State_Exits()
        {
            var builder = new HsmBuilder("TimerMachine");
            var stateA = builder.State("A");
            var stateB = builder.State("B");
            
            // Transition A -> B on Event 1
            stateA.On(1).GoTo(stateB);
            stateA.Initial();
            
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // Initialize -> Enter A (Needs a few ticks to settle)
            HsmKernel.Trigger(ref instance);
            RunUpdates(blob, ref instance, 2); 
            
            // Manually arm timer
            instance.TimerDeadlines[0] = 1000;
            
            // Trigger transition A -> B
            var evt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);
            
            // Run enough updates to process timer phase -> detect event -> entry -> rtc -> transition -> exit A
            RunUpdates(blob, ref instance, 5);
            
            // Verify timer cleared
            Assert.Equal(0u, instance.TimerDeadlines[0]);
        }

        [Fact]
        public void Multiple_Timers_Cancelled_On_Exit()
        {
            var builder = new HsmBuilder("TimerMachine2");
            var stateA = builder.State("A");
            var stateB = builder.State("B");
            stateA.On(1).GoTo(stateB);
            stateA.Initial();
            
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            var instance = new HsmInstance128();
            HsmInstanceManager.Initialize(&instance, blob);
            
            HsmKernel.Trigger(ref instance);
            RunUpdates(blob, ref instance, 2);
            
            instance.TimerDeadlines[0] = 1000;
            instance.TimerDeadlines[1] = 2000;
            instance.TimerDeadlines[2] = 3000;
            
            var evt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 128, evt);
            
            RunUpdates(blob, ref instance, 5);
            
            Assert.Equal(0u, instance.TimerDeadlines[0]);
            Assert.Equal(0u, instance.TimerDeadlines[1]);
            Assert.Equal(0u, instance.TimerDeadlines[2]);
        }

        [Fact]
        public void Timer_Fires_If_State_Still_Active()
        {
            var builder = new HsmBuilder("TimerMachineActive");
            var stateA = builder.State("A");
            stateA.Initial();
            
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            HsmKernel.Trigger(ref instance);
            RunUpdates(blob, ref instance, 2);
            // Now likely in Idle phase if no activities
            
            // Manually arm timer
            instance.TimerDeadlines[0] = 5000; 
            
            // Run update (delta 16ms)
            HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // 5000 - 16 = 4984
            uint expected = 5000 - 16;
            Assert.Equal(expected, instance.TimerDeadlines[0]);
        }
    }
}
