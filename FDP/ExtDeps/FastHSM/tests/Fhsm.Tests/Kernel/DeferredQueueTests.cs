using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    public unsafe class DeferredQueueTests
    {
        private void RunUpdates<T>(HsmDefinitionBlob blob, ref T instance, int count) where T : unmanaged
        {
            for (int i = 0; i < count; i++)
            {
                HsmKernel.Update(blob, ref instance, 0, 0.016f);
            }
        }

        [Fact]
        public void Deferred_Event_Is_Skipped_Then_Recalled_On_Transition()
        {
            var builder = new HsmBuilder("DeferredMachine");
            var stateA = builder.State("A");
            var stateB = builder.State("B");
            var stateC = builder.State("C");
            
            stateA.Initial();
            
            // A transitions on 1 -> B (But we will defer 1 effectively by flag)
            // Wait, if 1 matches, it transitions.
            // If deferred flag is set, Kernel skips checking transitions!
            // Correct. ProcessEventPhase handles deferred flag before selecting transition.
            
            // A transitions on 2 -> B
            stateA.On(2).GoTo(stateB);
            
            // B transitions on 1 -> C
            stateB.On(1).GoTo(stateC);
            
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            var instance = new HsmInstance256();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // 1. Enter A
            HsmKernel.Trigger(ref instance);
            RunUpdates(blob, ref instance, 2);
            // Verify in A (ActiveLeafIds[0])
            // Need to know IDs. 
            // A=1, B=2, C=3 (0 is Root).
            // But builder IDs are not guaranteed unless we check.
            // However, we can check by behavior.
            
            // 2. Enqueue Event 1 as DEFERRED
            // We manually set the flag.
            var evt1 = new HsmEvent 
            { 
                EventId = 1, 
                Priority = EventPriority.Normal,
                Flags = EventFlags.IsDeferred 
            };
            HsmEventQueue.TryEnqueue(&instance, 256, evt1);
            
            // 3. Update. Event 1 should be skipped (re-queued). State stays A.
            RunUpdates(blob, ref instance, 2);
            
            // Check State is still A (ActiveLeafIds[0]). 
            // If it processed 1, it would be C? No, A has no transition for 1? 
            // Wait, if A has no transition for 1, it would discard it anyway.
            // I need A to HAVE transition for 1 to prove it skipped it.
            // If A -> D on 1. 
            // If skipped, we stay in A.
            // If processed, we go to D.
            
            // But I want to test Recall in B.
            // So let's make A -> Failed on 1.
            // Actually, if A discards it, it's gone.
            // But Deferred event is NOT discarded, it is re-queued.
            // So if A has valid transition for 1, and we flag it deferred, it SHOULD be skipped.
            // If A does NOT have transition, it would be discarded if processed.
            // But deferred -> re-queued. So it persists.
            
            // So even if A has NO transition for 1, if it is deferred, it stays.
            // Then in B, we un-defer it, and B -> C on 1.
            
            // 4. Enqueue Event 2 (Normal). causes A->B.
            var evt2 = new HsmEvent { EventId = 2, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 256, evt2);
            
            // 5. Update. A -> B. Transition Logic calls MergeDeferredQueue.
            RunUpdates(blob, ref instance, 10);
            
            // Now logic:
            // Transition A->B happened.
            // MergeDeferredQueue Un-flagged Event 1.
            // B is active.
            // Next Update: Event 1 (Normal) is popped.
            // B -> C on 1.
            
            // So verify we are in C.
            // How to verify C?
            // HsmInstance128 doesn't expose easy state name.
            // We can check ActiveLeafIds.
            // Or easier: use TraceBuffer? No.
            // Or assume if C is reached, we are good.
            
            // Let's use TimerDEADLINES to verify state!
            // C has Entry Action that sets Timer? No actions here.
            
            // Let's rely on ActiveLeafIds[0].
            // ID 1 should be A.
            // ID 2 should be B.
            // ID 3 should be C.
            // (Assuming creation order 0=Root, 1=A, 2=B, 3=C).
            
            Assert.Equal(3, instance.ActiveLeafIds[0]);
        }
    }
}
