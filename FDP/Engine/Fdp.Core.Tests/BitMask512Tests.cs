using System;
using System.Runtime.CompilerServices;
using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for BitMask512 (TASK-E003).
    /// Covers size, SetBit/ClearBit/IsSet, HasAll, HasAny, Matches, equality, and paranoid mode.
    /// </summary>
    public class BitMask512Tests
    {
        // ----------------------------------------------------------
        // 1. SIZE TEST
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_SizeIs64Bytes()
        {
            Assert.Equal(64, Unsafe.SizeOf<BitMask512>());
        }

        // ----------------------------------------------------------
        // 2. SetBit / IsSet ROUND-TRIP — boundary bits
        // ----------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(127)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(383)]
        [InlineData(511)]
        public void BitMask512_SetBit_IsSet_RoundTrip(int bit)
        {
            var mask = new BitMask512();
            mask.SetBit(bit);
            Assert.True(mask.IsSet(bit));
        }

        [Fact]
        public void BitMask512_SetBit_NoBitBleed_Across_Quads()
        {
            // Set each boundary bit one at a time and verify no other boundary bit is set
            int[] boundaries = { 0, 63, 64, 127, 255, 256, 383, 511 };
            foreach (int bitToSet in boundaries)
            {
                var mask = new BitMask512();
                mask.SetBit(bitToSet);

                foreach (int other in boundaries)
                {
                    if (other == bitToSet)
                        Assert.True(mask.IsSet(other), $"Expected bit {other} to be set");
                    else
                        Assert.False(mask.IsSet(other), $"Unexpected bit {other} set when only {bitToSet} was set");
                }
            }
        }

        // ----------------------------------------------------------
        // 3. ClearBit
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_ClearBit_400_ResultsInEmptyMask()
        {
            var mask = new BitMask512();
            mask.SetBit(400);
            Assert.True(mask.IsSet(400));

            mask.ClearBit(400);
            Assert.False(mask.IsSet(400));
            Assert.True(mask.IsEmpty());
        }

        // ----------------------------------------------------------
        // 4. HasAll
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_HasAll_LowerHalf_AllSet_ReturnsTrue()
        {
            var source = new BitMask512();
            source.SetBit(0);
            source.SetBit(63);
            source.SetBit(127);

            var required = new BitMask512();
            required.SetBit(0);
            required.SetBit(127);

            Assert.True(BitMask512.HasAll(source, required));
        }

        [Fact]
        public void BitMask512_HasAll_LowerHalf_MissingBit_ReturnsFalse()
        {
            var source = new BitMask512();
            source.SetBit(0);

            var required = new BitMask512();
            required.SetBit(0);
            required.SetBit(127); // not set in source

            Assert.False(BitMask512.HasAll(source, required));
        }

        [Fact]
        public void BitMask512_HasAll_UpperHalf_AllSet_ReturnsTrue()
        {
            var source = new BitMask512();
            source.SetBit(256);
            source.SetBit(383);
            source.SetBit(511);

            var required = new BitMask512();
            required.SetBit(256);
            required.SetBit(511);

            Assert.True(BitMask512.HasAll(source, required));
        }

        [Fact]
        public void BitMask512_HasAll_UpperHalf_MissingBit_ReturnsFalse()
        {
            var source = new BitMask512();
            source.SetBit(256);

            var required = new BitMask512();
            required.SetBit(256);
            required.SetBit(383); // not set in source

            Assert.False(BitMask512.HasAll(source, required));
        }

        [Fact]
        public void BitMask512_HasAll_Straddling_Quad3And4_ReturnsTrue()
        {
            // Quad 3 boundary: bit 255, Quad 4: bit 256
            var source = new BitMask512();
            source.SetBit(255);
            source.SetBit(256);

            var required = new BitMask512();
            required.SetBit(255);
            required.SetBit(256);

            Assert.True(BitMask512.HasAll(source, required));
        }

        [Fact]
        public void BitMask512_HasAll_Straddling_MissingUpperBit_ReturnsFalse()
        {
            var source = new BitMask512();
            source.SetBit(255); // lower half only

            var required = new BitMask512();
            required.SetBit(255);
            required.SetBit(256); // upper half bit not set in source

            Assert.False(BitMask512.HasAll(source, required));
        }

        [Fact]
        public void BitMask512_HasAll_EmptyRequired_ReturnsTrue()
        {
            var source = new BitMask512();
            source.SetBit(100);
            var required = new BitMask512(); // empty
            Assert.True(BitMask512.HasAll(source, required));
        }

        // ----------------------------------------------------------
        // 5. HasAny
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_HasAny_Overlap_ReturnsTrue()
        {
            var source = new BitMask512();
            source.SetBit(300);

            var test = new BitMask512();
            test.SetBit(300);

            Assert.True(BitMask512.HasAny(source, test));
        }

        [Fact]
        public void BitMask512_HasAny_NoOverlap_ReturnsFalse()
        {
            var source = new BitMask512();
            source.SetBit(100);

            var test = new BitMask512();
            test.SetBit(200);

            Assert.False(BitMask512.HasAny(source, test));
        }

        [Fact]
        public void BitMask512_HasAny_UpperHalf_Overlap_ReturnsTrue()
        {
            var source = new BitMask512();
            source.SetBit(400);

            var test = new BitMask512();
            test.SetBit(400);

            Assert.True(BitMask512.HasAny(source, test));
        }

        [Fact]
        public void BitMask512_HasAny_EmptyTest_ReturnsFalse()
        {
            var source = new BitMask512();
            source.SetBit(50);
            var test = new BitMask512(); // empty
            Assert.False(BitMask512.HasAny(source, test));
        }

        // ----------------------------------------------------------
        // 6. Matches — all four combinations
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_Matches_AllIncludeSet_NoExclude_ReturnsTrue()
        {
            var target = new BitMask512();
            target.SetBit(10);
            target.SetBit(300);

            var include = new BitMask512();
            include.SetBit(10);
            include.SetBit(300);

            var exclude = new BitMask512();

            Assert.True(BitMask512.Matches(target, include, exclude));
        }

        [Fact]
        public void BitMask512_Matches_MissingInclude_LowerHalf_ReturnsFalse()
        {
            var target = new BitMask512();
            target.SetBit(10);

            var include = new BitMask512();
            include.SetBit(10);
            include.SetBit(50); // not in target

            var exclude = new BitMask512();

            Assert.False(BitMask512.Matches(target, include, exclude));
        }

        [Fact]
        public void BitMask512_Matches_MissingInclude_UpperHalf_ReturnsFalse()
        {
            var target = new BitMask512();
            target.SetBit(300);

            var include = new BitMask512();
            include.SetBit(300);
            include.SetBit(400); // not in target

            var exclude = new BitMask512();

            Assert.False(BitMask512.Matches(target, include, exclude));
        }

        [Fact]
        public void BitMask512_Matches_ExcludePresent_LowerHalf_ReturnsFalse()
        {
            var target = new BitMask512();
            target.SetBit(10);
            target.SetBit(20); // excluded

            var include = new BitMask512();
            include.SetBit(10);

            var exclude = new BitMask512();
            exclude.SetBit(20);

            Assert.False(BitMask512.Matches(target, include, exclude));
        }

        [Fact]
        public void BitMask512_Matches_ExcludePresent_UpperHalf_ReturnsFalse()
        {
            var target = new BitMask512();
            target.SetBit(300);
            target.SetBit(400); // excluded

            var include = new BitMask512();
            include.SetBit(300);

            var exclude = new BitMask512();
            exclude.SetBit(400);

            Assert.False(BitMask512.Matches(target, include, exclude));
        }

        [Fact]
        public void BitMask512_Matches_LowerHalfOnly_IncludeAndExclude_Correct()
        {
            var target = new BitMask512();
            target.SetBit(5);
            target.SetBit(50);

            var include = new BitMask512();
            include.SetBit(5);

            var exclude = new BitMask512();
            exclude.SetBit(200); // not in target

            Assert.True(BitMask512.Matches(target, include, exclude));
        }

        [Fact]
        public void BitMask512_Matches_UpperHalfOnly_IncludeAndExclude_Correct()
        {
            var target = new BitMask512();
            target.SetBit(320);

            var include = new BitMask512();
            include.SetBit(320);

            var exclude = new BitMask512();
            exclude.SetBit(500); // not in target

            Assert.True(BitMask512.Matches(target, include, exclude));
        }

        // ----------------------------------------------------------
        // 7. Equality
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_EqualMasks_Equal()
        {
            var a = new BitMask512();
            a.SetBit(0);
            a.SetBit(255);
            a.SetBit(511);

            var b = new BitMask512();
            b.SetBit(0);
            b.SetBit(255);
            b.SetBit(511);

            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void BitMask512_DifferentMasks_NotEqual()
        {
            var a = new BitMask512();
            a.SetBit(100);

            var b = new BitMask512();
            b.SetBit(101);

            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void BitMask512_SingleDifferingBit_NotEqual()
        {
            var a = new BitMask512();
            a.SetBit(0);
            a.SetBit(255);

            var b = new BitMask512();
            b.SetBit(0);
            b.SetBit(256); // differs from a (255 vs 256)

            Assert.True(a != b);
        }

        [Fact]
        public void BitMask512_GetHashCode_ConsistentWithEquality()
        {
            var a = new BitMask512();
            a.SetBit(42);
            a.SetBit(300);

            var b = new BitMask512();
            b.SetBit(42);
            b.SetBit(300);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        // ----------------------------------------------------------
        // 8. Paranoid mode bounds checks
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_SetBit_NegativeIndex_ParanoidThrows_OrNoOp()
        {
            var mask = new BitMask512();
            // Fdp.Core is always compiled with FDP_PARANOID_MODE in Debug configuration.
            // The guard in SetBit throws for bitIndex < 0.
            Assert.Throws<ArgumentOutOfRangeException>(() => mask.SetBit(-1));
        }

        [Fact]
        public void BitMask512_SetBit_512_ParanoidThrows_OrNoOp()
        {
            var mask = new BitMask512();
            // Fdp.Core is always compiled with FDP_PARANOID_MODE in Debug configuration.
            // The guard in SetBit throws for bitIndex >= 512.
            Assert.Throws<ArgumentOutOfRangeException>(() => mask.SetBit(512));
        }

        // ----------------------------------------------------------
        // Additional coverage tests
        // ----------------------------------------------------------

        [Fact]
        public void BitMask512_Clear_ReturnsEmptyMask()
        {
            var mask = new BitMask512();
            mask.SetBit(0);
            mask.SetBit(255);
            mask.SetBit(511);
            mask.Clear();
            Assert.True(mask.IsEmpty());
        }

        [Fact]
        public void BitMask512_SetAll_IsNotEmpty()
        {
            var mask = new BitMask512();
            mask.SetAll();
            Assert.False(mask.IsEmpty());
            Assert.True(mask.IsSet(0));
            Assert.True(mask.IsSet(511));
        }

        [Fact]
        public void BitMask512_BitwiseAnd_LimitsToCommonBits()
        {
            var a = new BitMask512();
            a.SetBit(100);
            a.SetBit(300);

            var b = new BitMask512();
            b.SetBit(100);
            b.SetBit(400);

            a.BitwiseAnd(b);
            Assert.True(a.IsSet(100));
            Assert.False(a.IsSet(300));
            Assert.False(a.IsSet(400));
        }

        [Fact]
        public void BitMask512_BitwiseOr_CombinesBits()
        {
            var a = new BitMask512();
            a.SetBit(100);

            var b = new BitMask512();
            b.SetBit(300);

            a.BitwiseOr(b);
            Assert.True(a.IsSet(100));
            Assert.True(a.IsSet(300));
        }
    }
}
