using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    public class TacticalIntentMessageTests
    {
        // SC-1: Enum value is 92
        [Fact]
        public void EDescriptorType_TacticalIntentRequest_Value_Is92()
        {
            Assert.Equal(92, (int)EDescriptorType.dtTacticalIntentRequest);
        }

        // SC-2: Struct can be instantiated and fields accessed
        [Fact]
        public void TacticalIntentRequest_CanBeInstantiated_FieldsAccessible()
        {
            var msg = new TacticalIntentRequest
            {
                TargetEntityId = 42L,
                IntentId       = "DefendArea",
                JsonParams     = "{\"radius\":100}",
            };
            Assert.Equal(42L, msg.TargetEntityId);
            Assert.Equal("DefendArea", msg.IntentId);
            Assert.Equal("{\"radius\":100}", msg.JsonParams);
        }
    }
}
