using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    public unsafe class DeepHistoryTests
    {
         private HsmDefinitionBlob Compile(HsmBuilder builder)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            HsmGraphValidator.Validate(graph);
            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
        }

        [Fact]
        public void DeepHistory_RestoresNestedState()
        {
            // Build machine: Root -> Composite (with deep history) -> Child1 -> GrandChild1/GrandChild2
            var builder = new HsmBuilder("DeepHistoryTest");
            var root = builder.State("Root");
            
            StateBuilder composite = null;
            root.Child("Composite", c => {
                c.History();
                c.State.IsDeepHistory = true;
                c.Initial();
                composite = c;
            });
            
            StateBuilder child1 = null;
            composite.Child("Child1", c => {
                c.Initial();
                child1 = c;
            });
            
            StateBuilder gc1 = null;
            child1.Child("GC1", c => {
                c.Initial();
                gc1 = c;
            });
            
            StateBuilder gc2 = null;
            child1.Child("GC2", c => gc2 = c);
            
            StateBuilder outside = null;
            root.Child("Outside", c => outside = c);

            // GC1 -> GC2 (Event 1)
            gc1.On(1).GoTo(gc2.State.Name);
            // GC2 -> Outside (Event 2)
            gc2.On(2).GoTo(outside.State.Name);
            // Outside -> Composite (Event 3)
            outside.On(3).GoTo(composite.State.Name);

            var blob = Compile(builder);
            
            // Identify GC2 index
            ushort gc2Index = 0xFFFF;
            for(int i=0; i<blob.Transitions.Length; i++) {
                if (blob.Transitions[i].EventId == 1) {
                    gc2Index = blob.Transitions[i].TargetStateIndex;
                    break;
                }
            }
            Assert.NotEqual(0xFFFF, gc2Index);

            // Runtime
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // 1. Initial Update -> GC1
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // 2. Event 1: GC1 -> GC2
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 1 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            Assert.Equal(gc2Index, instance.ActiveLeafIds[0]);
            
            // 3. Event 2: GC2 -> Outside
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 2 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            Assert.NotEqual(gc2Index, instance.ActiveLeafIds[0]);
            
            // 4. Event 3: Outside -> Composite (Restore Deep History)
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 3 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // Should be back at GC2
            Assert.Equal(gc2Index, instance.ActiveLeafIds[0]);
        }

        [Fact]
        public void ShallowHistory_RestoresOnlyDirectChild()
        {
            var builder = new HsmBuilder("ShallowHistoryTest");
            var root = builder.State("Root");
            
            StateBuilder composite = null;
            root.Child("Composite", c => {
                c.History(); // Shallow
                c.Initial();
                composite = c;
            });
            
            StateBuilder child1 = null;
            composite.Child("Child1", c => {
                c.Initial();
                child1 = c;
            });
            
            StateBuilder gc1 = null;
            child1.Child("GC1", c => {
                c.Initial();
                gc1 = c;
            });
            
            StateBuilder gc2 = null;
            child1.Child("GC2", c => gc2 = c);
            
            StateBuilder outside = null;
            root.Child("Outside", c => outside = c);

            // GC1 -> GC2 (Event 1)
            gc1.On(1).GoTo(gc2.State.Name);
            // GC2 -> Outside (Event 2)
            gc2.On(2).GoTo(outside.State.Name);
            // Outside -> Composite (Event 3)
            outside.On(3).GoTo(composite.State.Name);

            var blob = Compile(builder);
            
            ushort gc2Index = 0xFFFF;
            ushort gc1Index = 0xFFFF;
            for(int i=0; i<blob.Transitions.Length; i++) {
                if (blob.Transitions[i].EventId == 1) {
                    gc2Index = blob.Transitions[i].TargetStateIndex;
                    gc1Index = blob.Transitions[i].SourceStateIndex;
                }
            }
            Assert.NotEqual(0xFFFF, gc2Index);
            Assert.NotEqual(0xFFFF, gc1Index);

            // Runtime
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // 1.
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // 2. GC1 -> GC2
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 1 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            Assert.Equal(gc2Index, instance.ActiveLeafIds[0]);
            
            // 3. GC2 -> Outside
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 2 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // 4. Outside -> Composite (Shallow History)
            // Restores Child1. Child1 enters (no history) -> Initial Child (GC1)
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 3 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // Should be at GC1
            Assert.Equal(gc1Index, instance.ActiveLeafIds[0]);
        }
    }
}
