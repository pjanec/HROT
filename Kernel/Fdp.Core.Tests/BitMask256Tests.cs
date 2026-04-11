using Xunit;
using Fdp.Kernel;
using System.Runtime.Intrinsics.X86;

namespace Fdp.Tests
{
    public class BitMask256Tests
    {
        [Fact]
        public void SetBit_SingleBit_SetsCorrectly()
        {
            var mask = new BitMask256();
            mask.SetBit(15);
            
            Assert.True(mask.IsSet(15));
            Assert.False(mask.IsSet(14));
            Assert.False(mask.IsSet(16));
        }
        
        [Fact]
        public void SetBit_AcrossQuads_SetsCorrectly()
        {
            var mask = new BitMask256();
            mask.SetBit(0);    // Quad 0
            mask.SetBit(64);   // Quad 1
            mask.SetBit(128);  // Quad 2
            mask.SetBit(192);  // Quad 3
            mask.SetBit(255);  // Last bit
            
            Assert.True(mask.IsSet(0));
            Assert.True(mask.IsSet(64));
            Assert.True(mask.IsSet(128));
            Assert.True(mask.IsSet(192));
            Assert.True(mask.IsSet(255));
        }
        
        [Fact]
        public void ClearBit_RemovesBit()
        {
            var mask = new BitMask256();
            mask.SetBit(42);
            Assert.True(mask.IsSet(42));
            
            mask.ClearBit(42);
            Assert.False(mask.IsSet(42));
        }
        
        [Fact]
        public void Clear_RemovesAllBits()
        {
            var mask = new BitMask256();
            for (int i = 0; i < 256; i += 17)
                mask.SetBit(i);
            
            Assert.False(mask.IsEmpty());
            
            mask.Clear();
            Assert.True(mask.IsEmpty());
        }
        
        [Fact]
        public void IsEmpty_NewMask_ReturnsTrue()
        {
            var mask = new BitMask256();
            Assert.True(mask.IsEmpty());
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(191)]
        [InlineData(192)]
        [InlineData(255)]
        public void Matches_IncludeOnly_WorksCorrectly(int bitToSet)
        {
            var target = new BitMask256();
            target.SetBit(bitToSet);
            target.SetBit(100); // Extra bit
            
            var include = new BitMask256();
            include.SetBit(bitToSet);
            
            var exclude = new BitMask256();
            
            Assert.True(BitMask256.Matches(target, include, exclude));
        }
        
        [Fact]
        public void Matches_MissingRequiredBit_ReturnsFalse()
        {
            var target = new BitMask256();
            target.SetBit(5);
            
            var include = new BitMask256();
            include.SetBit(5);
            include.SetBit(10); // Required but missing
            
            var exclude = new BitMask256();
            
            Assert.False(BitMask256.Matches(target, include, exclude));
        }
        
        [Fact]
        public void Matches_HasExcludedBit_ReturnsFalse()
        {
            var target = new BitMask256();
            target.SetBit(5);
            target.SetBit(20); // Forbidden bit
            
            var include = new BitMask256();
            include.SetBit(5);
            
            var exclude = new BitMask256();
            exclude.SetBit(20);
            
            Assert.False(BitMask256.Matches(target, include, exclude));
        }
        
        [Fact]
        public void Matches_ComplexQuery_WorksCorrectly()
        {
            // Target has bits: 1, 5, 10, 15, 20, 100
            var target = new BitMask256();
            target.SetBit(1);
            target.SetBit(5);
            target.SetBit(10);
            target.SetBit(15);
            target.SetBit(20);
            target.SetBit(100);
            
            // Require: 1, 5, 10
            var include = new BitMask256();
            include.SetBit(1);
            include.SetBit(5);
            include.SetBit(10);
            
            // Forbid: 99
            var exclude = new BitMask256();
            exclude.SetBit(99);
            
            Assert.True(BitMask256.Matches(target, include, exclude));
            
            // Now forbid bit 20 (which target has)
            exclude.SetBit(20);
            Assert.False(BitMask256.Matches(target, include, exclude));
        }
        
        [Fact]
        public void HasAll_AllPresent_ReturnsTrue()
        {
            var source = new BitMask256();
            source.SetBit(1);
            source.SetBit(5);
            source.SetBit(200);
            
            var required = new BitMask256();
            required.SetBit(1);
            required.SetBit(200);
            
            Assert.True(BitMask256.HasAll(source, required));
        }
        
        [Fact]
        public void HasAll_SomeMissing_ReturnsFalse()
        {
            var source = new BitMask256();
            source.SetBit(1);
            
            var required = new BitMask256();
            required.SetBit(1);
            required.SetBit(5); // Missing
            
            Assert.False(BitMask256.HasAll(source, required));
        }
        
        [Fact]
        public void HasAny_OneMatch_ReturnsTrue()
        {
            var source = new BitMask256();
            source.SetBit(42);
            
            var test = new BitMask256();
            test.SetBit(10);
            test.SetBit(42);
            test.SetBit(100);
            
            Assert.True(BitMask256.HasAny(source, test));
        }
        
        [Fact]
        public void HasAny_NoMatch_ReturnsFalse()
        {
            var source = new BitMask256();
            source.SetBit(42);
            
            var test = new BitMask256();
            test.SetBit(10);
            test.SetBit(100);
            
            Assert.False(BitMask256.HasAny(source, test));
        }
        
        [Fact]
        public void Equality_SameBits_ReturnsTrue()
        {
            var a = new BitMask256();
            a.SetBit(10);
            a.SetBit(200);
            
            var b = new BitMask256();
            b.SetBit(10);
            b.SetBit(200);
            
            Assert.True(a == b);
            Assert.True(a.Equals(b));
        }
        
        [Fact]
        public void Equality_DifferentBits_ReturnsFalse()
        {
            var a = new BitMask256();
            a.SetBit(10);
            
            var b = new BitMask256();
            b.SetBit(11);
            
            Assert.True(a != b);
            Assert.False(a.Equals(b));
        }
        
        [Fact]
        public void Performance_MatchesOperation_IsFast()
        {
            // This is a smoke test to ensure AVX2 path compiles
            // Real benchmarks in Stage 1 benchmarks
            var target = new BitMask256();
            for (int i = 0; i < 256; i += 13)
                target.SetBit(i);
            
            var include = new BitMask256();
            include.SetBit(13);
            include.SetBit(26);
            
            var exclude = new BitMask256();
            exclude.SetBit(5);
            
            // Run 100K times
            bool result = false;
            for (int i = 0; i < 100_000; i++)
            {
                result = BitMask256.Matches(target, include, exclude);
            }
            
            Assert.True(result); // Should match
        }
        
        [Fact]
        public void StructSize_Is32Bytes()
        {
            // Verify size for alignment requirements
            int size = System.Runtime.InteropServices.Marshal.SizeOf<BitMask256>();
            Assert.Equal(32, size);
        }
        
        [Fact]
        public void AllBitsSettable()
        {
            var mask = new BitMask256();
            
            // Set all 256 bits
            for (int i = 0; i < 256; i++)
            {
                mask.SetBit(i);
            }
            
            // Verify all are set
            for (int i = 0; i < 256; i++)
            {
                Assert.True(mask.IsSet(i), $"Bit {i} should be set");
            }
        }
        
        [Fact]
        public void ClearBit_MultipleTimes_Works()
        {
            var mask = new BitMask256();
            mask.SetBit(42);
            
            mask.ClearBit(42);
            Assert.False(mask.IsSet(42));
            
            // Clearing again should be safe
            mask.ClearBit(42);
            Assert.False(mask.IsSet(42));
        }
        
        [Fact]
        public void SetBit_MultipleTimes_Works()
        {
            var mask = new BitMask256();
            
            mask.SetBit(42);
            Assert.True(mask.IsSet(42));
            
            // Setting again should be safe
            mask.SetBit(42);
            Assert.True(mask.IsSet(42));
        }
    }
}
