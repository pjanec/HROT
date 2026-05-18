using System;
using Xunit;
using Fhsm.Compiler.Hashing;

namespace Fhsm.Tests.Compiler
{
    public class XxHash64Tests
    {
        [Fact]
        public void XxHash64_Deterministic()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            
            var hash1 = XxHash64.ComputeHash(data);
            var hash2 = XxHash64.ComputeHash(data);
            
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void XxHash64_DifferentInput_DifferentHash()
        {
            var data1 = new byte[] { 1, 2, 3, 4, 5 };
            var data2 = new byte[] { 1, 2, 3, 4, 6 }; // Last byte different
            
            var hash1 = XxHash64.ComputeHash(data1);
            var hash2 = XxHash64.ComputeHash(data2);
            
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void XxHash64_EmptyInput_ReturnsNonZero()
        {
            var hash = XxHash64.ComputeHash(ReadOnlySpan<byte>.Empty);
            Assert.NotEqual(0UL, hash);
        }
    }
}
