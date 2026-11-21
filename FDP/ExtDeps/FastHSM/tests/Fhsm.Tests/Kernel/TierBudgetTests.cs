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
    
        // ════════════════════════════════════════════════════════════════════════════
        // CE-325 — SelectTier defers to CheckTierBudget for the LAYOUT limits
        // ════════════════════════════════════════════════════════════════════════════
        //
        // SelectTier used to carry its own region/history numbers and they DISAGREED with the ones
        // here: it admitted regions <= 1 / <= 2 and history <= 2 / <= 4, where the structs hold
        // 2 / 4 / 8 regions and 2 / 8 / 16 history slots. Two tables of one fact, and the one that
        // ran was the conservative one. It also ignored TIMER slots entirely.

        /// <summary>A blob with N states, R regions and explicit history/timer slot indices.</summary>
        private static HsmDefinitionBlob Machine(
            int stateCount, int regionCount, ushort historySlot = 0xFFFF, ushort timerSlot = 0xFFFF,
            byte depth = 0)
        {
            var states = new StateDef[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                states[i] = new StateDef
                {
                    ParentIndex      = 0xFFFF,
                    Depth            = depth,
                    HistorySlotIndex = historySlot,
                    TimerSlotIndex   = timerSlot,
                };
            }
            var header = new HsmDefinitionHeader { StateCount = (ushort)stateCount, RegionCount = (ushort)regionCount };
            return new HsmDefinitionBlob(header, states, null, new RegionDef[regionCount], null, null, null);
        }

        /// <summary>
        /// CE-325 — THE HEADLINE. A 3-region machine fits HsmInstance128's FOUR leaf slots exactly,
        /// and used to be sent to 256 because SelectTier's own gate said "regions &lt;= 2".
        /// </summary>
        [Fact]
        public void SelectTier_ThreeRegions_LandsOn128_NotOn256_CE325()
        {
            Assert.Equal(128, HsmInstanceManager.SelectTier(Machine(stateCount: 4, regionCount: 3)));
        }

        /// <summary>CE-325 — four regions is EXACTLY the 128 layout's capacity, and is accepted.</summary>
        [Fact]
        public void SelectTier_FourRegions_IsExactlyThe128Capacity_CE325()
        {
            Assert.Equal(128, HsmInstanceManager.SelectTier(Machine(stateCount: 4, regionCount: 4)));
        }

        /// <summary>CE-325 — five regions genuinely overflows 128 and must still reach 256.</summary>
        [Fact]
        public void SelectTier_FiveRegions_StillNeeds256_CE325()
        {
            Assert.Equal(256, HsmInstanceManager.SelectTier(Machine(stateCount: 4, regionCount: 5)));
        }

        /// <summary>
        /// CE-325 — ANTI-REGRESSION, and the reason tier 1 keeps an explicit regions &lt;= 1.
        /// CheckTierBudget(64) ACCEPTS two regions because the 64-byte layout holds two — but tier 1
        /// is the only tier with NO RESERVED INTERRUPT SLOT, so an orthogonal machine must not land
        /// there. Routing tier 1 through the budget check alone dropped every 2-region machine to 64
        /// and reddened nine rails, including CE-324's two.
        /// </summary>
        [Fact]
        public void SelectTier_TwoRegions_StaysOn128_NeverDropsTo64_CE325()
        {
            var blob = Machine(stateCount: 2, regionCount: 2);

            // The budget check alone would allow 64 — that is exactly why SelectTier does not use it alone.
            Assert.True(HsmValidator.CheckTierBudget(blob, 64, out _));
            Assert.Equal(128, HsmInstanceManager.SelectTier(blob));
        }

        /// <summary>
        /// CE-325 — the history axis, relaxed the same way: the 128 layout holds EIGHT history slots
        /// and SelectTier's own gate stopped at four.
        /// </summary>
        [Fact]
        public void SelectTier_HistorySlotSix_LandsOn128_CE325()
        {
            Assert.Equal(128, HsmInstanceManager.SelectTier(
                Machine(stateCount: 4, regionCount: 1, historySlot: 6)));
        }

        /// <summary>
        /// CE-325 — TIMER SLOTS ARE CONSIDERED AT ALL NOW. SelectTier never looked at them, so a
        /// machine using timer slot 5 could be handed the 64-byte tier, which has TWO timer slots.
        /// </summary>
        [Fact]
        public void SelectTier_TimerSlotBeyondTheSmallTiers_IsNoLongerIgnored_CE325()
        {
            var blob = Machine(stateCount: 2, regionCount: 1, timerSlot: 5);

            Assert.False(HsmValidator.CheckTierBudget(blob, 64,  out _));
            Assert.False(HsmValidator.CheckTierBudget(blob, 128, out _));
            Assert.Equal(256, HsmInstanceManager.SelectTier(blob));
        }

        /// <summary>
        /// CE-325 — A MACHINE THAT FITS NO TIER NOW THROWS RATHER THAN BEING HANDED 256.
        /// It used to fall through to `return 256` unchecked, and InitializeMachine writes
        /// activeLeafIds[r] for r &lt; Header.RegionCount WITHOUT a bounds check — so a 9-region
        /// machine wrote past the leaf array. History and timer over-indexing are both bounds-checked
        /// and merely lose data; regions were the one genuine memory hazard.
        /// </summary>
        [Fact]
        public void SelectTier_NineRegions_Throws_RatherThanReturning256_CE325()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => HsmInstanceManager.SelectTier(Machine(stateCount: 2, regionCount: 9)));

            Assert.Contains("Region Count 9 exceeds limit 8", ex.Message);
        }
}
}
