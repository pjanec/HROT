using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Perception.Components
{
    /// <summary>
    /// Per-entity active radar sensor configuration.
    /// Attach to any entity that performs active radar scanning.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RadarReceptor)]
    public struct RadarReceptor
    {
        /// <summary>Maximum radar detection range (metres).</summary>
        public float MaxRange;

        /// <summary>Effective emission power (arbitrary units); higher values penetrate clutter better.</summary>
        public float EmissionPower;

        /// <summary>Bitmask of target categories this radar can detect (application-defined bit flags).</summary>
        public int TargetMask;
    }
}
