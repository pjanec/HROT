using System.Reflection;
using Hrot.NED.Descriptors;
using Fdp.Core;
using Xunit;

namespace Hrot.IG.Tests
{
    public class IgEntityDataTests
    {
        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var data = new Fdp.Core.EntityInfo();

            Assert.Equal(string.Empty, data.Name);
            Assert.Equal(ForceId.Neutral, data.ForceId);
        }

        [Fact]
        public void ComponentIdAttribute_IsPresent()
        {
            var attribute = typeof( Fdp.Core.EntityInfo ).GetCustomAttribute<ComponentIdAttribute>();

            Assert.NotNull(attribute);
        }
    }
}
