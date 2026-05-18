using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    public class TierBudgetTests
    {
        [Fact]
        public void Tier64_RegionLimit_Enforced()
        {
            var header = new HsmDefinitionHeader { RegionCount = 3 }; // Limit 2
            var states = new StateDef[1];
            var blob = new HsmDefinitionBlob(header, states, null, null, null, null, null);
            
            bool result = HsmValidator.CheckTierBudget(blob, 64, out string? error);
            
            Assert.False(result);
            Assert.Contains("Region Count 3 exceeds limit 2", error);
        }

        [Fact]
        public void Tier64_TimerLimit_Enforced()
        {
            var header = new HsmDefinitionHeader { RegionCount = 1 };
            var states = new StateDef[1];
            
            // Limit 2 timers (Indices 0, 1 allowed). Requesting 2 is INVALID (Limit 2 means max index 1).
            // Wait, Limit is count. Indices are 0-based.
            // My implementation checks: index >= limit. 
            // If limit is 2. Index 2 means 3rd timer. Violation. Matches logic.
            
            states[0] = new StateDef 
            { 
                TimerSlotIndex = 2, // Violation
                HistorySlotIndex = 0xFFFF 
            };
            
            var blob = new HsmDefinitionBlob(header, states, null, null, null, null, null);
            
            bool result = HsmValidator.CheckTierBudget(blob, 64, out string? error);
            
            Assert.False(result);
            Assert.Contains("Timer Slot 2", error);
        }

        [Fact]
        public void Tier128_HistoryLimit_Enforced()
        {
            var header = new HsmDefinitionHeader { RegionCount = 1 };
            // Tier 128: Limit History 8. Indices 0-7 allowed. Accessing 8 is violation.
            var states = new StateDef[1];
            states[0] = new StateDef 
            { 
                TimerSlotIndex = 0xFFFF,
                HistorySlotIndex = 8 
            };
            
            var blob = new HsmDefinitionBlob(header, states, null, null, null, null, null);
            
            bool result = HsmValidator.CheckTierBudget(blob, 128, out string? error);
            Assert.False(result);
            Assert.Contains("History Slot 8", error);
        }

        [Fact]
        public void Valid_Configuration_Passes()
        {
            // Tier 64 valid config
            var header = new HsmDefinitionHeader { RegionCount = 2 };
            var states = new StateDef[1];
            states[0] = new StateDef 
            { 
                TimerSlotIndex = 1, // Max allowed
                HistorySlotIndex = 1 // Max allowed
            };
            
            var blob = new HsmDefinitionBlob(header, states, null, null, null, null, null);
            
            bool result = HsmValidator.CheckTierBudget(blob, 64, out string? error);
            Assert.True(result, error);
            Assert.Null(error);
        }
    }
}
