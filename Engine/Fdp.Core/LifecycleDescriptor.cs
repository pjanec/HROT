using System.Runtime.InteropServices;

namespace Fdp.Core
{
    public enum EntityState : byte 
    {
        Constructing = 0, // Just created, waiting for modules
        Hydrated = 1,     // Components attached, waiting for validation
        Active = 2,       // Fully simulated
        TearDown = 3      // Scheduled for destruction
    }

    /// <summary>
    /// Tier 1 Component that tracks the initialization state of an entity.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LifecycleDescriptor)]
    public struct LifecycleDescriptor 
    {
        public EntityState State;
        public ulong RequiredModulesMask; // Bitmask of who needs to 'Sign Off'
        public ulong AckedModulesMask;    // Bitmask of who HAS 'Signed Off'
        public float CreatedTime;       // For timeout logic (Sim Time)
    }
}
