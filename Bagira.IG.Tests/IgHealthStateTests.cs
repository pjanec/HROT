using System.Reflection;
using Bagira.IG.Components;
using Fdp.Kernel;
using Xunit;

namespace Bagira.IG.Tests
{
    public class IgHealthStateTests
    {
        [Fact]
        public void DefaultValue_IsZero()
        {
            var state = new IgHealthState();

            Assert.Equal(0f, state.Damage);
        }

        [Fact]
        public void HasComponentIdAttribute()
        {
            var attribute = typeof(IgHealthState).GetCustomAttribute<ComponentIdAttribute>();

            Assert.NotNull(attribute);
        }
    }
}
