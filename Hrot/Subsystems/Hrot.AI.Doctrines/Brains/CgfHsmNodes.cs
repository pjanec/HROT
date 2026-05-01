using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;

namespace Hrot.AI.Doctrines
{
    /// <summary>
    /// HSM action/guard delegates for CGF doctrine state machines.
    /// The Fhsm.SourceGen analyzer scans this file and emits
    /// <c>Hrot.AI.Doctrines.Generated.HsmActionRegistrar</c> with <c>RegisterAll()</c>.
    /// </summary>
    internal static unsafe class CgfHsmNodes
    {
        /// <summary>Stub idle action: no-op while entity is in the Idle HSM state.</summary>
        [HsmAction]
        public static void StubIdle(void* instance, void* ctx, HsmCommandWriter* writer)
        {
            // Intentionally empty: the Idle HSM state has no output.
        }
    }
}
