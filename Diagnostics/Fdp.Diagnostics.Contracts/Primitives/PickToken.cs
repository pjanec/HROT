using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PickToken
    {
        public Entity Target;
        public uint SubElementId;
        public uint GizmoTypeId;  // composite routing key carried through the ECS event bus

        // A zero-value PickToken (Entity.Null) is non-interactive.
        public bool IsValid => !Target.IsNull;
    }
}
