using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// A Tag Component marking an entity as fully initialized and ready for simulation.
    /// Used by EntityQuery to efficiently filter out "Constructing" or "Dead" entities via bitmask.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    [ComponentId(GlobalComponentIds.IsActiveTag)]
    public struct IsActiveTag
    {
        // No data. 
        // Its presence in the EntityHeader.ComponentMask is the data.
    }
}
