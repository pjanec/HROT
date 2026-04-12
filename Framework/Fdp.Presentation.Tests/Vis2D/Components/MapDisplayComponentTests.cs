using Xunit;
using FDP.Toolkit.Vis2D.Components;

namespace FDP.Toolkit.Vis2D.Tests.Components
{
    public class MapDisplayComponentTests
    {
        [Fact]
        public void MapDisplayComponent_Default_IsLayerZero()
        {
            var defaults = MapDisplayComponent.Default;
            
            // LayerZero usually corresponds to bitmask 1 (1 << 0)
            Assert.Equal(1u, defaults.LayerMask);
            
            // Or verify bit 0 is set
            Assert.True((defaults.LayerMask & 1) != 0);
        }

        [Fact]
        public void MapDisplayComponent_IsValueType()
        {
            Assert.True(typeof(MapDisplayComponent).IsValueType);
        }
    }
}
