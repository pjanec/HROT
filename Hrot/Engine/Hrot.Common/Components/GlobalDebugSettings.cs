using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.Common.Components
{
    /// <summary>
    /// Singleton ECS component that controls global debug behavior across subsystems.
    /// Originally lived in <c>Hrot.IG.Gizmos</c> but moved to <c>Hrot.Common</c> so the
    /// SimHost/Brain genesis pipeline (which must not reference IG) can read it for
    /// auto-enabling per-entity diagnostics.
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

        /// <summary>Maximum milliseconds per frame for all gizmo projection work. 0 means unlimited. Default: 2.0f.</summary>
        public float MaxGizmoFrameMs;

        /// <summary>
        /// When true, the AI-diagnostics TKB translator stamps a <c>DebugState</c> +
        /// matching <c>BTreeTraceWorkingMemory1024</c> / <c>HsmTraceWorkingMemory1024</c>
        /// on every AI-enabled entity at genesis time.
        /// </summary>
        [MarshalAs(UnmanagedType.I1)] public bool AutoEnableAiTracing;
    }
}
