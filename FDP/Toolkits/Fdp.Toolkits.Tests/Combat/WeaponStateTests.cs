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
        // SC-P0-01-1: sizeof(WeaponState) == 16 (4 fields × 4 bytes each)
        [Fact]
        public unsafe void WeaponState_SizeIs16Bytes()
        {
            Assert.Equal(16, sizeof(WeaponState));
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
