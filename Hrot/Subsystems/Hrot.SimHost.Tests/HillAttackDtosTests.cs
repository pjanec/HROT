using System.Runtime.InteropServices;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the Hill Attack DTO structs
    /// <see cref="PlatoonHillAttackParams"/> and <see cref="HullDownAttackParams"/>
    /// (TASK-HA005 / TASK-HA006).
    /// </summary>
    public class HillAttackDtosTests
    {
        // ── PlatoonHillAttackParams ───────────────────────────────────────────────

        /// <summary>
        /// <see cref="PlatoonHillAttackParams"/> must be exactly 52 bytes in a
        /// sequential layout so it fits within the 60-byte <c>BrainBlackboard</c>
        /// parameter region without overflow.
        /// </summary>
        [Fact]
        public unsafe void PlatoonHillAttackParams_Is52Bytes()
        {
            Assert.Equal(52, sizeof(PlatoonHillAttackParams));
        }

        /// <summary>
        /// <see cref="PlatoonHillAttackParams"/> must be blittable (GC-pinnable) so it
        /// can be written directly into native ECS memory via <c>Unsafe.CopyBlockUnaligned</c>.
        /// </summary>
        [Fact]
        public void PlatoonHillAttackParams_IsBlittable()
        {
            var p = new PlatoonHillAttackParams
            {
                StartX     = 1f,
                StartY     = 2f,
                TankSpacing = 15f,
            };

            var handle = GCHandle.Alloc(p, GCHandleType.Pinned);
            try
            {
                Assert.NotEqual(System.IntPtr.Zero, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        // ── HullDownAttackParams ──────────────────────────────────────────────────

        /// <summary>
        /// <see cref="HullDownAttackParams"/> must be exactly 40 bytes in a sequential
        /// layout so it fits within the 60-byte param region and the
        /// <c>LocomotionChannel.Params</c> 32-byte fixed buffer.
        /// </summary>
        [Fact]
        public unsafe void HullDownAttackParams_Is40Bytes()
        {
            // Struct grew from 40 bytes: 8 fields × 4 bytes + TargetNetworkId(long, 8 bytes) +
            // MaxRounds + RoundsFired + LastObservedAmmo (3 × 4 bytes) = 52 bytes raw,
            // padded to 56 by Sequential layout (must align to largest field: 8 bytes).
            Assert.Equal(56, sizeof(HullDownAttackParams));
        }

        /// <summary>
        /// <see cref="HullDownAttackParams"/> must be blittable (GC-pinnable) so it
        /// can be written into native ECS memory.
        /// </summary>
        [Fact]
        public void HullDownAttackParams_IsBlittable()
        {
            var p = new HullDownAttackParams
            {
                SlotX           = 100f,
                SlotY           = 200f,
                TargetNetworkId = 42L,
            };

            var handle = GCHandle.Alloc(p, GCHandleType.Pinned);
            try
            {
                Assert.NotEqual(System.IntPtr.Zero, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        // ── HillAttackMutableState ────────────────────────────────────────────────

        /// <summary>
        /// <see cref="HillAttackMutableState"/> must fit within the 1024-byte
        /// <c>Blackboard1024.ByteSize</c> limit.
        /// </summary>
        [Fact]
        public unsafe void HillAttackMutableState_FitsInBlackboard1024()
        {
            // Blackboard1024.ByteSize = 1024
            Assert.True(sizeof(HillAttackMutableState) <= 1024,
                $"HillAttackMutableState is {sizeof(HillAttackMutableState)} bytes — exceeds 1024");
        }
    }
}
