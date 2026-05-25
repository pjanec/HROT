using Fdp.Core;
using FDP.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EqsSensorHandle"/>.
    ///
    /// <list type="number">
    ///   <item>T-SH1 -- ChildId round-trips through the constructor.</item>
    ///   <item>T-SH2 -- Two handles with the same Entity are Equals and have equal hash codes.</item>
    ///   <item>T-SH3 -- default(EqsSensorHandle).IsValid == false.</item>
    ///   <item>T-SH4 -- Two handles with different Entities are != (not equal).</item>
    /// </list>
    /// </summary>
    public class EqsSensorHandleTests
    {
        // T-SH1: ChildId round-trips through the constructor.
        [Fact]
        public void EqsSensorHandle_ChildId_RoundTrips()
        {
            var entity = new Entity(7, 3);
            var handle = new EqsSensorHandle(entity);
            Assert.Equal(entity, handle.ChildId);
        }

        // T-SH2: Two handles with the same Entity are Equals and have equal hash codes.
        [Fact]
        public void EqsSensorHandle_SameEntity_EqualsAndSameHashCode()
        {
            var entity = new Entity(42, 1);
            var a = new EqsSensorHandle(entity);
            var b = new EqsSensorHandle(entity);

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        // T-SH3: default(EqsSensorHandle).IsValid == false.
        [Fact]
        public void EqsSensorHandle_Default_IsNotValid()
        {
            var handle = default(EqsSensorHandle);
            Assert.False(handle.IsValid);
        }

        // T-SH4: Two handles with different Entities are != (not equal).
        [Fact]
        public void EqsSensorHandle_DifferentEntities_NotEqual()
        {
            var a = new EqsSensorHandle(new Entity(1, 1));
            var b = new EqsSensorHandle(new Entity(2, 1));

            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }
    }
}
