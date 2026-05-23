using System;
using System.Runtime.CompilerServices;
using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for EntityMetadataCold (TASK-E004).
    /// Covers size, IsActive/SetActive, field layout, AuthorityMask, and unmanaged constraint.
    /// </summary>
    public class EntityMetadataColdTests
    {
        // Helper: verify a type satisfies where T : unmanaged at compile time.
        private static void RequireUnmanaged<T>() where T : unmanaged { }

        // ----------------------------------------------------------
        // 1. SIZE TEST
        // ----------------------------------------------------------

        [Fact]
        public void EntityMetadataCold_SizeIs128Bytes()
        {
            Assert.Equal(128, Unsafe.SizeOf<EntityMetadataCold>());
        }

        // ----------------------------------------------------------
        // 2. IsActive / SetActive round-trip
        // ----------------------------------------------------------

        [Fact]
        public void EntityMetadataCold_Default_IsActive_IsFalse()
        {
            var meta = new EntityMetadataCold();
            Assert.False(meta.IsActive);
        }

        [Fact]
        public void EntityMetadataCold_SetActive_True_IsActive_IsTrue()
        {
            var meta = new EntityMetadataCold();
            meta.SetActive(true);
            Assert.True(meta.IsActive);
        }

        [Fact]
        public void EntityMetadataCold_SetActive_False_IsActive_IsFalse()
        {
            var meta = new EntityMetadataCold();
            meta.SetActive(true);
            meta.SetActive(false);
            Assert.False(meta.IsActive);
        }

        // ----------------------------------------------------------
        // 3. SetActive does not touch other bits in Flags
        // ----------------------------------------------------------

        [Fact]
        public void EntityMetadataCold_SetActive_True_DoesNotModifyOtherFlagBits()
        {
            var meta = new EntityMetadataCold();
            // Set all bits except bit 0
            meta.Flags = 0xFFFE;
            meta.SetActive(true);
            // All bits including bit 0 should now be set
            Assert.Equal((ushort)0xFFFF, meta.Flags);
        }

        [Fact]
        public void EntityMetadataCold_SetActive_False_DoesNotModifyOtherFlagBits()
        {
            var meta = new EntityMetadataCold();
            meta.Flags = 0xFFFF;
            meta.SetActive(false);
            // All bits except bit 0 should remain set
            Assert.Equal((ushort)0xFFFE, meta.Flags);
        }

        // ----------------------------------------------------------
        // 4. AuthorityMask field is a BitMask512
        // ----------------------------------------------------------

        [Fact]
        public void EntityMetadataCold_AuthorityMask_SetBit300_IsSet()
        {
            var meta = new EntityMetadataCold();
            meta.AuthorityMask.SetBit(300);
            Assert.True(meta.AuthorityMask.IsSet(300));
        }

        [Fact]
        public void EntityMetadataCold_AuthorityMask_IsIndependentOfFlags()
        {
            var meta = new EntityMetadataCold();
            meta.AuthorityMask.SetBit(511);
            meta.SetActive(true);
            // Both should hold their state independently
            Assert.True(meta.AuthorityMask.IsSet(511));
            Assert.True(meta.IsActive);
        }

        // ----------------------------------------------------------
        // 5. Unmanaged constraint verification
        // ----------------------------------------------------------

        [Fact]
        public void EntityMetadataCold_SatisfiesUnmanagedConstraint()
        {
            // This line must compile without error.
            // RequireUnmanaged<T>() requires where T : unmanaged.
            RequireUnmanaged<EntityMetadataCold>();
            Assert.True(true); // reaching here proves the struct is unmanaged
        }

        // ----------------------------------------------------------
        // Additional field tests
        // ----------------------------------------------------------

        [Fact]
        public void EntityMetadataCold_Generation_CanBeSet()
        {
            var meta = new EntityMetadataCold();
            meta.Generation = 42;
            Assert.Equal((ushort)42, meta.Generation);
        }

        [Fact]
        public void EntityMetadataCold_LastChangeTick_CanBeSet()
        {
            var meta = new EntityMetadataCold();
            meta.LastChangeTick = 9876543210UL;
            Assert.Equal(9876543210UL, meta.LastChangeTick);
        }

        [Fact]
        public void EntityMetadataCold_LifecycleState_DefaultIsConstructing()
        {
            var meta = new EntityMetadataCold();
            Assert.Equal(EntityLifecycle.Constructing, meta.LifecycleState);
        }

        [Fact]
        public void EntityMetadataCold_LifecycleState_CanBeSetToActive()
        {
            var meta = new EntityMetadataCold();
            meta.LifecycleState = EntityLifecycle.Active;
            Assert.Equal(EntityLifecycle.Active, meta.LifecycleState);
        }
    }
}
