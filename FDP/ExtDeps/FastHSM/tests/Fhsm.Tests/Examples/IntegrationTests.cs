using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

namespace Fhsm.Tests.Examples
{
    [Xunit.Collection("HsmActionDispatcher")]
    public unsafe class IntegrationTests
    {
        private static int _entryCount = 0;
        private static int _exitCount = 0;
        private static int _activityCount = 0;
        private static int _transitionCount = 0;
        
        [HsmAction(Name = "TestEntry")]
        internal static void TestEntry(void* instance, void* context, HsmCommandWriter* writer)
        {
            _entryCount++;
        }
        
        [HsmAction(Name = "TestExit")]
        internal static void TestExit(void* instance, void* context, HsmCommandWriter* writer)
        {
            _exitCount++;
        }
        
        [HsmAction(Name = "TestActivity")]
        internal static void TestActivity(void* instance, void* context, HsmCommandWriter* writer)
        {
            _activityCount++;
        }
        
        [HsmAction(Name = "TestTransition")]
        internal static void TestTransition(void* instance, void* context, HsmCommandWriter* writer)
        {
            _transitionCount++;
        }
        
        [Fact]
        public void End_To_End_State_Machine_Works()
        {
            // Reset counters
            _entryCount = 0;
            _exitCount = 0;
            _activityCount = 0;
            _transitionCount = 0;
            
            // Build
            var builder = new HsmBuilder("TestMachine");
            builder.Event("StartEvent", 1);
            builder.Event("Event10", 10);
            
            // Add Idle state as the default initial state (first child)
            builder.State("Idle");
            
            var stateA = builder.State("A")
                .OnEntry("TestEntry")
                .OnExit("TestExit")
                .Activity("TestActivity");
                //.Initial(); // Not needed if we transition manually
            
            var stateB = builder.State("B");
            
            // A -> B on Event10
            stateA.On("Event10")
                .GoTo("B")
                .Action("TestTransition");
                
            // Compile
            var graph = builder.Build();
            
            // Manually add transition from Root to A on StartEvent (1)
            var root = graph.RootState;
            var startTrans = new Fhsm.Compiler.Graph.TransitionNode(root, stateA.State, 1);
            root.AddTransition(startTrans);
            
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            // Instance
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // Register actions
            Fhsm.Tests.Generated.HsmActionRegistrar.RegisterAll();
            
            // Run
            int context = 0;
            
            // Start (Trigger just prepares)
            HsmKernel.Trigger(ref instance);
            HsmKernel.Update(blob, ref instance, context, 0.016f); 
            // Now in Root state
            
            // Send Start Event to enter A
            var startEvt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, startEvt);
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, context, 0.016f);
            
            Assert.Equal(1, _entryCount); // A entered

            // ⭐⭐⭐ CE-334 (2026-09-26) — THE ACTIVITY ASSERTIONS INVERTED, AND THE OLD ONES WERE A
            //    WORKAROUND WRITTEN INTO A RAIL.
            //
            // 🔴 What stood here: `Assert.Equal(1, _activityCount)` after five ticks, then a DUMMY
            //    EVENT (id 999, matching no transition) enqueued purely to "drive cycle and hit
            //    Activity phase again", then `Assert.Equal(2, _activityCount)`. ⛔ That is the
            //    one-shot encoded as expected behaviour — a FOURTH independent workaround beyond the
            //    three §31.18.2 records, and the only one that lived inside the engine's own suite.
            //
            // ⭐ Now an active state's activity runs EVERY tick, so the count tracks TICKS. The claim
            //    the rail actually cares about — "entering A runs its activity, and the machine keeps
            //    running it" — is stronger this way, and needs no synthetic event to provoke it.
            // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.2.1; rail
            //    Fhsm.Tests.Kernel.ActivitySteadyStateTests.CE334_R1.
            int afterEntry = _activityCount;
            Assert.True(afterEntry >= 1,
                "entering A must run its activity at least once in that batch of updates");

            // ⭐ No dummy event: five more quiet ticks must each dispatch the activity.
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, context, 0.016f);

            Assert.Equal(afterEntry + 5, _activityCount);
            
            // Transition A -> B
            var transEvt = new HsmEvent { EventId = 10, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, transEvt);
            
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, context, 0.016f);
            
            Assert.Equal(1, _exitCount); // A exited
            Assert.Equal(1, _transitionCount); // Trans action
            
            // B has no entry action
        }

        
        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (ushort)(hash & 0xFFFF);
        }
    }
}
