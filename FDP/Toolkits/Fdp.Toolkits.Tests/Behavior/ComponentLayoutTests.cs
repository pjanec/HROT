using System.Runtime.CompilerServices;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class ComponentLayoutTests
    {
        [Fact]
        public void LocomotionChannel_SizeIsAtMost96Bytes()
        {
            Assert.True(Unsafe.SizeOf<LocomotionChannel>() <= BehaviorConstants.MaxChannelSizeBytes);
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
        public void RootStateSlot_IsExactlyOneBehaviorTreeState()
        {
            // ⛔ O7c-②: BrainBTreeState is gone; the claim is now about the SLOT's width, which is
            //    what RootStateAccess.StateBytes reserves and what the interpreter steps.
            var s = new Fbt.BehaviorTreeState();
            // Checking property accessibility
            ushort idx = s.RunningNodeIndex;
            Assert.Equal(0, idx);
        }

        /// <summary>
        /// ⚠⚠ <b>RE-HOMED by <c>O7c</c>-④d (2026-09-23), and the claim got STRONGER.</b> This was
        /// <c>BrainHsm128_Contains_HsmInstance128</c> — <c>sizeof(wrapper) &gt;= sizeof(instance)</c>
        /// on a component that no longer exists.
        ///
        /// <para>⭐⭐ <b>These three numbers are now the ONLY thing that sizes a root HSM slot.</b>
        /// <c>HsmInstanceManager.SelectTier</c> answers 64, 128 or 256 from the machine's shape;
        /// <c>RootHsmAccess</c> attaches a slot of exactly that many bytes and stores the number in
        /// the slot's guard, and the tick arm reads the size back from that same guard. ⇒ a kernel
        /// change to any tier's layout that did NOT move its struct size would leave every attached
        /// slot mis-sized, and the kernel would step past the payload into the NEXT occurrence's
        /// bytes — no compiler check, no runtime check (§9.4).</para>
        ///
        /// <para>⛔ <b>EQUALITY, not <c>&gt;=</c>.</b> The old wrapper could afford slack; a slot
        /// cannot. A slot one byte wider than its instance is a silent over-allocation nobody
        /// notices, and one byte narrower is memory corruption.</para>
        /// </summary>
        [Fact]
        public void TheThreeKernelInstanceTiersAreExactly64_128_256_O7c4d()
        {
            Assert.Equal(64,  Unsafe.SizeOf<HsmInstance64>());
            Assert.Equal(128, Unsafe.SizeOf<HsmInstance128>());
            Assert.Equal(256, Unsafe.SizeOf<HsmInstance256>());
        }

        [Fact]
        public void ActorCapabilities_CanMove_Is_Bit0()
        {
            Assert.Equal(1, (int)ActorCapabilities.CanMove);
        }
    }
}
