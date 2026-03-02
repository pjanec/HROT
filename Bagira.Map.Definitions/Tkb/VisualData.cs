using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Bagira.Map.Definitions.Tkb
{
    /// <summary>
    /// Runtime visual data applied to spawned entities from the TKB.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [ComponentId(GlobalComponentIds.VisualData)]
    public struct VisualData
    {
        /// <summary>
        /// MIL-STD-2525 symbol code (e.g., "SFGPUCIZ-------").
        /// </summary>
        public FixedString32 SymbolCode;

        /// <summary>
        /// Path to 3D model file (relative to models directory).
        /// </summary>
        public FixedString64 ModelPath;

        /// <summary>
        /// Base color in hex format (#RRGGBB or #RRGGBBAA).
        /// </summary>
        public FixedString32 ColorHex;
    }
}
