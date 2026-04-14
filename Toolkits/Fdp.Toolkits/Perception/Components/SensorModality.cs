using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>
    /// Bitmask enum identifying which sensor modalities observed a target.
    /// Stored per slot in <see cref="TargetMemory.Modalities"/>.
    /// </summary>
    [System.Flags]
    public enum SensorModality : byte
    {
        /// <summary>Optical / visual detection (cameras, human observers).</summary>
        Visual   = 1,

        /// <summary>Active radar detection.</summary>
        Radar    = 2,

        /// <summary>Infrared / thermal detection.</summary>
        Thermal  = 4,

        /// <summary>Passive acoustic detection.</summary>
        Acoustic = 8,
    }
}
