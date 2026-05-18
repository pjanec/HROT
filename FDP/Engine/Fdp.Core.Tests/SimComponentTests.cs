using System.Runtime.CompilerServices;
using Xunit;

namespace Fdp.Core.Tests
{
    public class SimComponentTests
    {
        [Fact] 
        public void SimTransform_Is28Bytes() =>
            Assert.Equal(28, Unsafe.SizeOf<SimTransform>());

        [Fact] 
        public void SimVelocity_Is24Bytes() =>
            Assert.Equal(24, Unsafe.SizeOf<SimVelocity>());

        [Fact] 
        public void SimComponents_AreUnmanagedValueTypes() 
        {
            Assert.True(typeof(SimTransform).IsValueType);
            Assert.True(typeof(SimVelocity).IsValueType);
        }
    }
}
