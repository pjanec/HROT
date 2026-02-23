using System.Runtime.CompilerServices;
using FDP.Toolkit.Behavior.Components;
using Fhsm.Kernel.Data;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    public class ComponentLayoutTests
    {
        [Fact]
        public void LocomotionChannel_SizeIsAtMost96Bytes()
        {
            Assert.True(Unsafe.SizeOf<LocomotionChannel>() <= 96);
        }

        [Fact]
        public void WeaponChannel_SameLayoutAsLocomotionChannel()
        {
            Assert.Equal(Unsafe.SizeOf<LocomotionChannel>(), Unsafe.SizeOf<WeaponChannel>());
        }

        [Fact]
        public void InteractionChannel_SameLayoutAsLocomotionChannel()
        {
            Assert.Equal(Unsafe.SizeOf<LocomotionChannel>(), Unsafe.SizeOf<InteractionChannel>());
        }

        [Fact]
        public void BrainBTreeState_Contains_BehaviorTreeState()
        {
            var s = new BrainBTreeState();
            // Checking property accessibility
            ushort idx = s.State.RunningNodeIndex;
            Assert.Equal(0, idx);
        }

        [Fact]
        public void BrainHsm128_Contains_HsmInstance128()
        {
            // Verifying type accessibility and size relation
            Assert.True(Unsafe.SizeOf<BrainHsm128>() >= Unsafe.SizeOf<HsmInstance128>());
        }

        [Fact]
        public void ActorCapabilities_CanMove_Is_Bit0()
        {
            Assert.Equal(1, (int)ActorCapabilities.CanMove);
        }
    }
}
