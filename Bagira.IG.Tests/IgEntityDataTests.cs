using System.Reflection;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using Fdp.Kernel;
using Xunit;

namespace Bagira.IG.Tests
{
    public class IgEntityDataTests
    {
        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var data = new IgEntityData();

            Assert.Equal(string.Empty, data.Name);
            Assert.Equal(ForceId.Unknown, data.ForceId);
            Assert.Equal(0, data.CommanderId);
        }

        [Fact]
        public void ComponentIdAttribute_IsPresent()
        {
            var attribute = typeof(IgEntityData).GetCustomAttribute<ComponentIdAttribute>();

            Assert.NotNull(attribute);
        }
    }
}
