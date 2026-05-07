using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests
{
    public class ContractsStandaloneTests
    {
        // SC-GZ041-3: standalone usage of DebugPrimitiveBuffer without Fdp.Toolkits reference.
        [Fact]
        public void SC_GZ041_3_DebugPrimitiveBuffer_StandaloneUsage()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            buffer.DrawLine(
                System.Numerics.Vector3.Zero,
                System.Numerics.Vector3.UnitX,
                new Rgba32(255, 0, 0, 255));
            Assert.Equal(1, buffer.GetFrame().Length);
        }
    }
}
