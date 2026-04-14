using Xunit;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Tkb.Tests
{
    public class MandatoryDescriptorTests
    {
        [Fact]
        public void Structure_LayoutIsCorrect()
        {
            var req = new MandatoryDescriptor
            {
                PackedKey = PackedKey.Create(1, 1),
                IsHard = true,
                SoftTimeoutFrames = 0
            };
            
            Assert.True(req.IsHard);
            Assert.Equal(0u, req.SoftTimeoutFrames);
            Assert.Contains("Hard", req.ToString());
        }

        [Fact]
        public void SoftRequirement_ToString_ShowsTimeout()
        {
             var req = new MandatoryDescriptor
            {
                PackedKey = PackedKey.Create(1, 1),
                IsHard = false,
                SoftTimeoutFrames = 100
            };
            
            Assert.False(req.IsHard);
            Assert.Equal(100u, req.SoftTimeoutFrames);
            Assert.Contains("Soft:100f", req.ToString());
        }
    }
}
