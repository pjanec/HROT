using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    public class SlotConflictTests
    {
        [Fact]
        public void Orthogonal_Regions_With_Conflicting_Timer_Slots_Errors()
        {
            var builder = new HsmBuilder("TestMachine");
            
            StateBuilder parallel = builder.State("Parallel");
            parallel.State.IsParallel = true;
            
            StateBuilder region1 = null;
            parallel.Child("Region1", c => region1 = c);
            
            StateBuilder region2 = null;
            parallel.Child("Region2", c => region2 = c);
            
            // Both regions use timer slot 0 (conflict!)
            region1.State.TimerSlotIndex = 0; 
            region2.State.TimerSlotIndex = 0;
            
            var graph = builder.Build();
            // Note: HsmGraphValidator is called directly here on the raw graph.
            // With Validator update, "Composite has no initial child" should be gone for Parallel.
            // We check for slot usage error.
            var errors = HsmGraphValidator.Validate(graph);
            
            Assert.Contains(errors, e => e.Message.Contains("Timer slot") && e.Message.Contains("used in multiple regions"));
        }

        [Fact]
        public void Orthogonal_Regions_With_Different_Slots_Passes()
        {
            var builder = new HsmBuilder("TestMachine");
            
            StateBuilder parallel = builder.State("Parallel");
            parallel.State.IsParallel = true;

            StateBuilder region1 = null;
            parallel.Child("Region1", c => region1 = c);
            
            StateBuilder region2 = null;
            parallel.Child("Region2", c => region2 = c);
            
            region1.State.TimerSlotIndex = 0;
            region2.State.TimerSlotIndex = 1; // Different slot
            
            var graph = builder.Build();
            var errors = HsmGraphValidator.Validate(graph);
            
            Assert.DoesNotContain(errors, e => e.Message.Contains("Timer slot"));
        }
    }
}
