using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="BitMask512.BitwiseAndNot"/> (RBF-P3T4).
    /// </summary>
    public sealed class BitMask512AndNotTests
    {
        // ── RBF-P3T4 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies that BitwiseAndNot clears every bit that is set in the
        /// "claimed" mask, spanning all 8 internal 64-bit quads (bits 0, 63, 64, 511).
        /// After the operation, no bit from the claimed mask should remain in
        /// the candidate mask.
        /// </summary>
        [Fact]
        public void RBF_P3T4_ConsensusMask_AndNot_AllBitsCovered()
        {
            var candidate = new BitMask512();
            // Set bits in every quad: 0, 63 (quad 0), 64 (quad 1), 511 (quad 7).
            candidate.SetBit(0);
            candidate.SetBit(63);
            candidate.SetBit(64);
            candidate.SetBit(511);

            var claimed = new BitMask512();
            claimed.SetBit(0);
            claimed.SetBit(63);
            claimed.SetBit(64);
            claimed.SetBit(511);

            candidate.BitwiseAndNot(claimed);

            Assert.False(candidate.IsSet(0),   "Bit 0 must be cleared after AndNot.");
            Assert.False(candidate.IsSet(63),  "Bit 63 must be cleared after AndNot.");
            Assert.False(candidate.IsSet(64),  "Bit 64 must be cleared after AndNot.");
            Assert.False(candidate.IsSet(511), "Bit 511 must be cleared after AndNot.");
        }

        /// <summary>
        /// When the claimed mask is empty, BitwiseAndNot must be a no-op:
        /// the candidate mask is unchanged.
        /// </summary>
        [Fact]
        public void RBF_P3T4_ConsensusMask_EmptyClaimed_ReturnsCandidate()
        {
            var candidate = new BitMask512();
            candidate.SetBit(1);
            candidate.SetBit(100);
            candidate.SetBit(300);

            var empty = new BitMask512();

            candidate.BitwiseAndNot(empty);

            Assert.True(candidate.IsSet(1),   "Bit 1 must remain after AndNot with empty mask.");
            Assert.True(candidate.IsSet(100), "Bit 100 must remain after AndNot with empty mask.");
            Assert.True(candidate.IsSet(300), "Bit 300 must remain after AndNot with empty mask.");
        }
    }
}
