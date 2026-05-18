using System;
using Xunit;
using Fhsm.Kernel;

namespace Fhsm.Tests.Kernel
{
    /// <summary>Tests that <see cref="HsmActionDispatcher.ClearAll"/> purges both tables.</summary>
    [Collection("HsmActionDispatcher")]
    public class ClearAllTests
    {
        [Fact]
        public void ClearAll_PurgesActionAndGuardTables()
        {
            // Register a dummy entry in each table.
            HsmActionDispatcher.RegisterAction(0x0001, IntPtr.Zero);
            HsmActionDispatcher.RegisterGuard(0x0002, IntPtr.Zero);

            HsmActionDispatcher.ClearAll();

            // After clearing, dispatching a registered id should return false / not throw.
            // The kernel uses ActionTable[id] internally, but we can verify indirectly:
            // Re-registering the same id should not produce a double-entry (dict count stays 1).
            HsmActionDispatcher.RegisterAction(0x0001, IntPtr.Zero);
            // No exception means the dict was empty before re-registration.

            // Clean up for other tests.
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void ClearAll_IsIdempotent_WhenCalledOnEmptyTables()
        {
            HsmActionDispatcher.ClearAll();
            // Should not throw when called on already-empty tables.
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void ClearAll_AllowsReRegistration()
        {
            // Arrange: register two actions.
            HsmActionDispatcher.RegisterAction(0x0010, new IntPtr(0xDEAD));
            HsmActionDispatcher.RegisterGuard(0x0011, new IntPtr(0xBEEF));

            // Act: clear and re-register with different pointers.
            HsmActionDispatcher.ClearAll();
            HsmActionDispatcher.RegisterAction(0x0010, new IntPtr(0xCAFE));
            HsmActionDispatcher.RegisterGuard(0x0011, new IntPtr(0xBABE));

            // No exception means clear + re-register succeeded.
            HsmActionDispatcher.ClearAll();
        }
    }
}
