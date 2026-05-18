using Xunit;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class IAIContextTests
    {
        [Fact]
        public void RaycastResult_IsValueType()
        {
            Assert.True(typeof(RaycastResult).IsValueType);
        }

        [Fact]
        public void PathResult_IsValueType()
        {
            Assert.True(typeof(PathResult).IsValueType);
        }
    }
}
