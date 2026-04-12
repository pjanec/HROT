using Xunit;
using Hrot.Map.Common;

namespace Hrot.Map.Common.Tests
{
    public class ConstantsTests
    {
        [Fact]
        public void VerifyTkbConstants()
        {
            Assert.Equal(100, TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(101, TkbEntityTypes.IFV_Bradley);
            Assert.Equal(8801, TkbEntityTypes.TacGraphic_FireLine);
        }

        [Fact]
        public void VerifyMapConfigDefaults()
        {
            Assert.Equal(0, MapConfig.DefaultMapGroupId);
            Assert.Equal(1, MapConfig.DefaultMapId);
        }

        [Fact]
        public void VerifyContextKeys()
        {
            Assert.Equal("place_tank", ContextKeys.PlaceTank);
            Assert.Equal("draw_route", ContextKeys.DrawRoute);
            Assert.Equal("measure", ContextKeys.Measure);
        }
    }
}
