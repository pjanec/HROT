using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.IG.Gizmos
{
    /// <summary>
    /// Singleton ECS component that controls global gizmo visibility and layer masking
    /// for the IG subsystem. Written by the debug settings panel; read by gizmo systems.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(HrotComponentIds.GlobalDebugSettings)]
    [DataPolicy(DataPolicy.Transient)]
    public struct GlobalDebugSettings
    {
        /// <summary>When true, all gizmos are visible regardless of DebugLayerMask.</summary>
        [MarshalAs(UnmanagedType.I1)] public bool ForceAllGizmosVisible;
        /// <summary>Bitmask for layers 0-15. Bit N set means layer N is visible. Default 0xFFFF (all on).</summary>
        public ushort DebugLayerMask;
        /// <summary>
        /// Maximum milliseconds per frame for all gizmo projection work.
        /// 0 means unlimited. Default: 2.0f.
        /// </summary>
        public float MaxGizmoFrameMs;
    }
}
