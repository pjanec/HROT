using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using System.Runtime.InteropServices;

namespace Fhsm.Tests.Kernel
{
    public unsafe class OrthogonalRegionTests
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
        public void OutputLane_Conflict_Detected()
        {
            var builder = new HsmBuilder("ConflictTest");
            var parallel = builder.State("Parallel");
            parallel.State.IsParallel = true;
            
            StateBuilder region1 = null;
            parallel.Child("Region1", c => {
                c.Initial();
                region1 = c;
            });
            
            StateBuilder region2 = null;
            parallel.Child("Region2", c => {
                c.Initial();
                region2 = c;
            });
            
            StateBuilder r1Child = null;
            region1.Child("R1Child", c => {
                c.Initial();
                r1Child = c;
            });
            
            StateBuilder r2Child = null;
            region2.Child("R2Child", c => {
                c.Initial();
                r2Child = c;
            });
            
            // Add a self-transition to trigger ExecuteTransition
            r1Child.On(1).GoTo(r1Child.State.Name);
            
            var blob = Compile(builder);
            
            // Manually set OutputLaneMask on all leaves to Animation (1)
            fixed (StateDef* states = blob.States)
            {
                for(int i=0; i<blob.States.Length; i++) {
                   if (states[i].FirstChildIndex == 0xFFFF) {
                       states[i].OutputLaneMask = (byte)(1 << (int)CommandLane.Animation);
                   }
                }
            }
            
            // Runtime - Needs Tier 2 for 3 regions
            var instance = new HsmInstance128();
            HsmInstanceManager.Initialize(&instance, blob);
            
            var traceBuffer = new HsmTraceBuffer(4096);
            HsmKernelCore.SetTraceBuffer(traceBuffer);
            
            // 1. Initialize (Active: R1Child, R2Child)
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // 2. Trigger Event 1 (Self transition on R1Child)
            // This calls ExecuteTransition, which runs ArbitrateOutputLanes
            HsmEventQueue.TryEnqueue(&instance, 128, new HsmEvent { EventId = 1 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // Check for Conflict trace
            var data = traceBuffer.GetTraceData();
            bool foundConflict = false;
            
            fixed (byte* ptr = data)
            {
                byte* curr = ptr;
                byte* end = ptr + data.Length;
                while (curr < end)
                {
                    TraceRecordHeader* header = (TraceRecordHeader*)curr;
                    if (header->OpCode == TraceOpCode.Conflict)
                    {
                        foundConflict = true;
                        break;
                    }
                    
                    int size = 12;
                    switch (header->OpCode)
                    {
                        case TraceOpCode.Transition: size = 16; break;
                        case TraceOpCode.GuardEvaluated: size = 16; break;
                        case TraceOpCode.Conflict: size = 12; break;
                    }
                    curr += size;
                }
            }
            
            HsmKernelCore.SetTraceBuffer(null);
            
            if (!foundConflict)
            {
                 Assert.Fail("Expected conflict trace record");
            }
        }

        [Fact]
        public void OutputLane_NoConflict_Passes()
        {
            var builder = new HsmBuilder("NoConflictTest");
            var parallel = builder.State("Parallel");
            parallel.State.IsParallel = true;
            
            StateBuilder region1 = null;
            parallel.Child("Region1", c => {
                c.Initial();
                region1 = c;
            });
            
            StateBuilder region2 = null;
            parallel.Child("Region2", c => {
                c.Initial();
                region2 = c;
            });
            
            StateBuilder r1Child = null;
            region1.Child("R1Child", c => {
                c.Initial();
                r1Child = c;
            });
            
            StateBuilder r2Child = null;
            region2.Child("R2Child", c => {
                c.Initial();
                r2Child = c;
            });
            
            r1Child.On(1).GoTo(r1Child.State.Name);
            
            var blob = Compile(builder);
            
            // Set R1Child to Animation (1)
            // Set R2Child to Navigation (2)
            int leafCount = 0;
            fixed (StateDef* states = blob.States)
            {
                for(int i=0; i<blob.States.Length; i++) {
                   if (states[i].FirstChildIndex == 0xFFFF) {
                       if (leafCount == 0) states[i].OutputLaneMask = (byte)(1 << (int)CommandLane.Animation);
                       else if (leafCount == 1) states[i].OutputLaneMask = (byte)(1 << (int)CommandLane.Navigation); 
                       leafCount++;
                   }
                }
            }
            
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            var traceBuffer = new HsmTraceBuffer(4096);
            HsmKernelCore.SetTraceBuffer(traceBuffer);
            
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 1 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            var data = traceBuffer.GetTraceData();
            bool foundConflict = false;
             fixed (byte* ptr = data)
            {
                byte* curr = ptr;
                byte* end = ptr + data.Length;
                while (curr < end)
                {
                    TraceRecordHeader* header = (TraceRecordHeader*)curr;
                    if (header->OpCode == TraceOpCode.Conflict)
                    {
                        foundConflict = true;
                        break;
                    }
                     int size = 12;
                    switch (header->OpCode)
                    {
                        case TraceOpCode.Transition: size = 16; break;
                        case TraceOpCode.GuardEvaluated: size = 16; break;
                    }
                    curr += size;
                }
            }
            
            HsmKernelCore.SetTraceBuffer(null);
            
            Assert.False(foundConflict, "Did not expect conflict trace record");
        }
    }
}

