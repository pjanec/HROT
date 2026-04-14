using System.Reflection;
using Hrot.IG.Components;
using Fdp.Core;
using Xunit;

namespace Hrot.IG.Tests
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
