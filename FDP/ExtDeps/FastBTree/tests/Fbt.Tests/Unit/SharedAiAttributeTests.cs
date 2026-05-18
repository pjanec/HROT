using Fbt.Kernel;
using System;
using System.Reflection;
using Xunit;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for BHU-011: SharedAiConditionAttribute, SharedAiActionAttribute, WritesChannelAttribute.</summary>
    public class SharedAiAttributeTests
    {
        // ---- SharedAiConditionAttribute ----------------------------------------

        [Fact]
        public void SharedAiConditionAttribute_StoresTypeAndField()
        {
            var attr = new SharedAiConditionAttribute(typeof(int), "SomeField");
            Assert.Equal(typeof(int), attr.DtoType);
            Assert.Equal("SomeField", attr.FieldName);
        }

        [Fact]
        public void SharedAiConditionAttribute_AllowsMultipleOnSameMethod()
        {
            // Verify AllowMultiple = true by applying two instances in the fixture below.
            var attrs = typeof(MultiConditionFixture)
                .GetMethod("Target", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetCustomAttributes(typeof(SharedAiConditionAttribute), false);
            Assert.Equal(2, attrs.Length);
        }

        // ---- SharedAiActionAttribute -------------------------------------------

        [Fact]
        public void SharedAiActionAttribute_StoresTypeAndField()
        {
            var attr = new SharedAiActionAttribute(typeof(float), "Speed");
            Assert.Equal(typeof(float), attr.DtoType);
            Assert.Equal("Speed", attr.FieldName);
        }

        [Fact]
        public void SharedAiActionAttribute_AllowsMultipleOnSameMethod()
        {
            var attrs = typeof(MultiActionFixture)
                .GetMethod("Target", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetCustomAttributes(typeof(SharedAiActionAttribute), false);
            Assert.Equal(2, attrs.Length);
        }

        // ---- WritesChannelAttribute ---------------------------------------------

        [Fact]
        public void WritesChannelAttribute_StoresChannelKind()
        {
            var attr = new WritesChannelAttribute(ChannelKind.Locomotion);
            Assert.Equal(ChannelKind.Weapon, new WritesChannelAttribute(ChannelKind.Weapon).Channel);
            Assert.Equal(ChannelKind.Interaction, new WritesChannelAttribute(ChannelKind.Interaction).Channel);
            Assert.Equal(ChannelKind.Locomotion, attr.Channel);
        }

        [Fact]
        public void WritesChannelAttribute_AllowsMultipleOnSameMethod()
        {
            var attrs = typeof(MultiChannelFixture)
                .GetMethod("Target", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetCustomAttributes(typeof(WritesChannelAttribute), false);
            Assert.Equal(2, attrs.Length);
        }

        // ---- ChannelKind enum --------------------------------------------------

        [Fact]
        public void ChannelKind_HasExpectedValues()
        {
            Assert.Equal(0, (int)ChannelKind.Locomotion);
            Assert.Equal(1, (int)ChannelKind.Weapon);
            Assert.Equal(2, (int)ChannelKind.Interaction);
        }

        // ---- Fixtures ----------------------------------------------------------

        private static class MultiConditionFixture
        {
            [SharedAiCondition(typeof(int), "Field1")]
            [SharedAiCondition(typeof(float), "Field2")]
            private static void Target() { }
        }

        private static class MultiActionFixture
        {
            [SharedAiAction(typeof(int), "Field1")]
            [SharedAiAction(typeof(float), "Field2")]
            private static void Target() { }
        }

        private static class MultiChannelFixture
        {
            [WritesChannel(ChannelKind.Locomotion)]
            [WritesChannel(ChannelKind.Weapon)]
            private static void Target() { }
        }
    }
}
