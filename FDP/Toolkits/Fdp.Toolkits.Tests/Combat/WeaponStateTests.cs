using System.Runtime.InteropServices;
using Fdp.Toolkit.Combat.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WeaponState"/> struct — P0.01 success criteria.
    /// </summary>
    public class WeaponStateTests
    {
        // SC-P0-01-1 (.dev/_DONE/utility-ai/PREREQ_Phase0_Bundle.md:86 — "natural-aligned, no padding regressions"): every field
        // is 4 bytes, so the size is exactly 4 × the field count. ⭐ CE-3136 P-5 added the magazine (4 fields): 8 × 4 = 32 (was 16).
        // WeaponState is never on the wire (DESIGN_Ownership_Groups_And_Grants.md §1: "linked, never sent").
        [Fact]
        public unsafe void WeaponState_SizeIs32Bytes_NoPadding()
        {
            Assert.Equal(32, sizeof(WeaponState));
        }

        // SC-P0-01-2: A spawned WeaponState has MaxAmmo == initialAmmunition
        [Fact]
        public void WeaponState_MaxAmmo_EqualsInitialAmmunition()
        {
            const int initialAmmunition = 50;
            var state = new WeaponState
            {
                Ammo           = initialAmmunition,
                MaxAmmo        = initialAmmunition,
                MuzzleVelocity = 800f
            };

            Assert.Equal(initialAmmunition, state.MaxAmmo);
            Assert.Equal(initialAmmunition, state.Ammo);
        }

        // SC-P0-01-3: Firing (decrementing Ammo) leaves MaxAmmo unchanged
        [Fact]
        public void WeaponState_FireDecrementsAmmo_MaxAmmoUnchanged()
        {
            const int initialAmmunition = 30;
            var state = new WeaponState
            {
                Ammo    = initialAmmunition,
                MaxAmmo = initialAmmunition
            };

            // Simulate firing three times
            state.Ammo--;
            state.Ammo--;
            state.Ammo--;

            Assert.Equal(27, state.Ammo);
            Assert.Equal(initialAmmunition, state.MaxAmmo);
        }

        // SC-P0-01-4: default(WeaponState).MaxAmmo == 0 (safe default)
        [Fact]
        public void WeaponState_DefaultMaxAmmoIsZero()
        {
            var state = default(WeaponState);
            Assert.Equal(0, state.MaxAmmo);
            Assert.Equal(0, state.Ammo);
        }

        // Additional: struct is unmanaged value type
        [Fact]
        public void WeaponState_IsUnmanagedValueType()
        {
            Assert.True(typeof(WeaponState).IsValueType);
        }
    }
}
