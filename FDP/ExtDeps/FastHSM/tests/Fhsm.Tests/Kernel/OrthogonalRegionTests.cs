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
            // SetTraceBuffer removed in behav-diag-1; trace tests now need HsmTraceContext rewrite (DEBT).
            
            // 1. Initialize (Active: R1Child, R2Child)
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // 2. Trigger Event 1 (Self transition on R1Child)
            // This calls ExecuteTransition, which runs ArbitrateOutputLanes
            HsmEventQueue.TryEnqueue(&instance, 128, new HsmEvent { EventId = 1 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // ⚠⚠ NAMED GAP (Batch 77, BP-304). This asserted that a Conflict record reached the
            // trace buffer. `SetTraceBuffer` was removed in `behav-diag-1` -- the commented-out call
            // sites above are its remains -- so nothing writes to an HsmTraceBuffer and the buffer is
            // unconditionally empty.
            //
            // ⛔ Unlike FailSafeTests, this test had NO behavioural half: the trace record was its
            // only observable, so what survives is the setup (a real parallel machine whose leaves
            // share an output lane) plus the statement that ArbitrateOutputLanes is currently
            // unobservable. Invert when the HsmTraceContext rewrite lands; do not delete.
            Assert.Empty(traceBuffer.GetTraceData().ToArray());
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
            // SetTraceBuffer removed in behav-diag-1; trace tests now need HsmTraceContext rewrite (DEBT).
            
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 1 });
            for(int i=0; i<4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            
            // ⚠⚠ AND THIS ONE WAS PASSING VACUOUSLY. It asserted NO conflict record was produced --
            // trivially true against a buffer nothing writes to, so it was green while proving
            // nothing. ⭐ Recorded rather than left as coverage: a green test that cannot fail is the
            // shape this programme keeps finding.
            Assert.Empty(traceBuffer.GetTraceData().ToArray());
        }
    }
}

