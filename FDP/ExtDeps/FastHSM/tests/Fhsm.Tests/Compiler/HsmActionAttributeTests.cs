using System;
using Xunit;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    /// <summary>
    /// Tests for TASK-K-01: HsmActionAttribute.Lane property and CommandLane.None sentinel.
    /// </summary>
    public class HsmActionAttributeTests
    {
        // K-01-T1: Default Lane is None when not specified.
        [Fact]
        public void HsmAction_DefaultLane_IsNone()
        {
            var attr = new HsmActionAttribute();
            Assert.Equal(CommandLane.None, attr.Lane);
        }

        // K-01-T2: Explicit Lane value is preserved.
        [Fact]
        public void HsmAction_ExplicitLane_Animation_IsPreserved()
        {
            var attr = new HsmActionAttribute { Lane = CommandLane.Animation };
            Assert.Equal(CommandLane.Animation, attr.Lane);
        }

        // K-01-T3: Every named lane value can be round-tripped through the attribute.
        [Theory]
        [InlineData(CommandLane.Animation)]
        [InlineData(CommandLane.Navigation)]
        [InlineData(CommandLane.Gameplay)]
        [InlineData(CommandLane.Blackboard)]
        [InlineData(CommandLane.Audio)]
        [InlineData(CommandLane.VFX)]
        [InlineData(CommandLane.Message)]
        public void HsmAction_ExplicitLane_IsPreserved(CommandLane lane)
        {
            var attr = new HsmActionAttribute { Lane = lane };
            Assert.Equal(lane, attr.Lane);
        }

        // K-01-T4: CommandLane.None has value 0xFF.
        [Fact]
        public void CommandLane_None_HasValue0xFF()
        {
            Assert.Equal(0xFF, (byte)CommandLane.None);
        }

        // K-01-T5: CommandLane.Count == 7 (sentinel does not shift count).
        [Fact]
        public void CommandLane_Count_IsStill7()
        {
            Assert.Equal(7, (byte)CommandLane.Count);
        }

        // K-01-T6: Attribute read back via reflection has correct Lane value.
        [HsmAction(Name = "LaneTest", Lane = CommandLane.Audio)]
        private static unsafe void LaneTestMethod(void* _, void* __, Fhsm.Kernel.Data.HsmCommandWriter* ___) { }

        [Fact]
        public void HsmAction_Lane_ReadsBack_ViaReflection()
        {
            var method = typeof(HsmActionAttributeTests)
                .GetMethod("LaneTestMethod",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var attr = (HsmActionAttribute)method.GetCustomAttributes(typeof(HsmActionAttribute), false)[0];

            Assert.Equal(CommandLane.Audio, attr.Lane);
        }
    }
}
